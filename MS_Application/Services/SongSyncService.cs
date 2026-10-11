using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Lyrics;
using MS_Application.DataTransferObjects.Songs;
using MS_Application.DataTransferObjects.Youtube;
using MS_Application.Helpers;
using MS_Application.Repositories.Interfaces;
using MS_Application.Services.Interfaces;
using MS_Application.Services.Interfaces.External;
using MS_Application.Services.SongSync;
using MS_Domain.Common;
using MS_Domain.Entities.DISTS;

namespace MS_Application.Services
{
    /// <summary>
    /// Đồng bộ nhạc Youtube vào dist.songs (kèm lyrics từ LRCLIB).
    ///
    /// Nguồn (theo thứ tự):
    ///  1. Chart mostPopular category Music của mọi quốc gia Youtube hỗ trợ
    ///     (~30 bài/quốc gia, 1 quota/quốc gia).
    ///  2. search.list trong category Music theo bộ từ khóa, xoay vòng mỗi lần bấm
    ///     (100 quota/từ khóa, search bị giới hạn ~100 request/ngày nên chỉ lấy ít).
    ///  3. Quét kênh nghệ sĩ: từ kênh của các bài ở (1)+(2), lấy playlist "uploads"
    ///     (toàn bộ video kênh đã đăng). ~2 quota/50 video -> nguồn chính, ra hàng nghìn bài.
    /// Video đã có trong DB bị bỏ qua trước khi gọi videos.list để tiết kiệm quota.
    ///
    /// Start() trả về ngay, việc đồng bộ chạy nền với scope DI riêng; FE poll GetStatus().
    /// </summary>
    public class SongSyncService : ISongSyncService
    {
        // Dùng khi không lấy được danh sách quốc gia từ i18nRegions
        private static readonly string[] FallbackRegions =
        {
            "VN", "US", "GB", "KR", "JP", "TW", "TH", "ID", "PH", "IN",
            "FR", "DE", "ES", "BR", "MX", "CA", "AU"
        };

        private static readonly string[] SearchKeywords =
        {
            "nhạc trẻ", "vpop", "rap việt", "nhạc bolero", "nhạc trữ tình", "ballad việt",
            "indie việt", "nhạc remix", "lofi chill", "nhạc acoustic", "nhạc xưa", "nhạc cách mạng",
            "kpop", "jpop", "cpop", "thai pop", "us uk pop", "hip hop", "r&b", "edm",
            "rock", "indie pop", "acoustic cover", "jazz", "latin pop", "reggaeton",
            "country music", "anime song", "ost", "k-ballad", "city pop", "afrobeats",
            "dance pop", "piano cover", "lofi hip hop", "phonk", "rap", "soul music"
        };

        private const int MaxPagesPerRegion = 4;
        private const int KeywordsPerSync = 15;
        private const int MaxChannels = 400;
        private const int MaxUploadPagesPerChannel = 3;
        private const int YoutubeParallelism = 6;
        private const int YoutubeIdsPerCall = 50;

        private const int MinDurationSeconds = 60;
        private const int MaxDurationSeconds = 600;
        private const int InsertBatchSize = 100;
        private const int LyricsChunkSize = 40;
        private const int LyricsParallelism = 8;

        // So khớp trên tiêu đề đã bỏ dấu + chữ thường
        private static readonly Regex NonMusicTitleRegex = new(
            @"\b(trailer|teaser|full movie|vlog|reaction|review|podcast|gameplay|interview|behind the scenes|hau truong|phong van|livestream|episode \d+|tap \d+)\b|#shorts",
            RegexOptions.Compiled);

        private static readonly Regex BracketRegex = new(
            @"\(.*?\)|\[.*?\]|【.*?】|「.*?」", RegexOptions.Compiled);

        private static readonly Regex TitleNoiseRegex = new(
            @"\b(official\s+)?(music\s+video|m/v|mv|lyrics?\s+video|lyrics?|audio|visualizer|video\s+clip|4k|hd)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FeatRegex = new(
            @"\s(ft|feat|featuring)\.?\s.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ArtistTitleRegex = new(
            @"^(.+?)\s+[-–—]\s+(.+)$", RegexOptions.Compiled);

        private static readonly Regex ChannelNoiseRegex = new(
            @"(\s*-\s*topic$|vevo$|\bofficial\b|\bchannel\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SongSyncState _state;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly LyricsApiSettingsDto _lyricsSettings;
        private readonly ILogger<SongSyncService> _logger;

        public SongSyncService(
            IServiceScopeFactory scopeFactory,
            SongSyncState state,
            IHttpClientFactory httpClientFactory,
            IOptions<LyricsApiSettingsDto> lyricsSettings,
            ILogger<SongSyncService> logger)
        {
            _scopeFactory = scopeFactory;
            _state = state;
            _httpClientFactory = httpClientFactory;
            _lyricsSettings = lyricsSettings.Value;
            _logger = logger;
        }

        public BaseResponse<SongSyncStatusDto> Start(long userId)
        {
            var result = new BaseResponse<SongSyncStatusDto>();

            if (!_state.TryStart())
                return result.Fail(_state.Snapshot(), "Đang đồng bộ, vui lòng chờ", ResponseStatusCode.Status409);

            // Không await: request trả về ngay, FE poll status.
            // Chỉ dùng field singleton trong RunAsync (service này là scoped, sẽ bị dispose sau request).
            _ = Task.Run(() => RunAsync(userId));

            return result.Success(_state.Snapshot(), "Bắt đầu đồng bộ");
        }

        public BaseResponse<SongSyncStatusDto> GetStatus()
        {
            return new BaseResponse<SongSyncStatusDto>()
                .Success(_state.Snapshot(), string.Format(Messages.Action.GetSuccess, "sync status"));
        }

        private async Task RunAsync(long userId)
        {
            try
            {
                var videos = await FetchVideosAsync();

                var music = videos.Where(IsMusic).ToList();
                _state.Update(s => s.Filtered = videos.Count - music.Count);

                var existing = await GetExistingVideoIdsAsync(music.Select(x => x.VideoId));
                var newVideos = music.Where(x => !existing.Contains(x.VideoId)).ToList();
                _state.Update(s => s.Duplicated += music.Count - newVideos.Count);

                var inserted = await InsertSongsAsync(newVideos, userId);

                await FetchLyricsAsync(inserted, userId);

                _state.Finish(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Song sync failed");
                _state.Finish($"Đồng bộ lỗi: {ex.Message}");
            }
        }

        /// <summary>
        /// Dữ liệu dùng chung cho 1 lần lấy video (các bước gọi Youtube song song).
        /// </summary>
        private sealed class FetchContext
        {
            public required IYoutubeAPIService Youtube { get; init; }
            public ConcurrentDictionary<string, YoutubeVideoDto> Videos { get; } = new();
            public volatile bool QuotaExceeded;

            // true = được phép gọi tiếp; false = hết quota, dừng lấy thêm
            public bool Check<T>(YoutubeApiPageDto<T> page)
            {
                if (page.QuotaExceeded)
                    QuotaExceeded = true;

                return !QuotaExceeded;
            }
        }

        private async Task<List<YoutubeVideoDto>> FetchVideosAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = new FetchContext
            {
                Youtube = scope.ServiceProvider.GetRequiredService<IYoutubeAPIService>()
            };

            await FetchChartsAsync(ctx);

            if (!ctx.QuotaExceeded)
                await FetchKeywordSearchAsync(ctx);

            if (!ctx.QuotaExceeded)
                await FetchChannelUploadsAsync(ctx);

            if (ctx.QuotaExceeded)
                _state.Update(s => s.Message = "Hết quota Youtube trong ngày, chỉ lưu những bài đã lấy được");

            _state.Update(s => s.Detail = null);

            return ctx.Videos.Values.ToList();
        }

        private async Task FetchChartsAsync(FetchContext ctx)
        {
            var regionPage = await ctx.Youtube.GetRegionCodesAsync();
            if (!ctx.Check(regionPage))
                return;

            var regions = regionPage.Items.Count > 0 ? regionPage.Items : FallbackRegions.ToList();
            var done = 0;

            await Parallel.ForEachAsync(
                regions,
                new ParallelOptions { MaxDegreeOfParallelism = YoutubeParallelism },
                async (region, _) =>
                {
                    string? pageToken = null;

                    for (var page = 0; page < MaxPagesPerRegion && !ctx.QuotaExceeded; page++)
                    {
                        var chart = await ctx.Youtube.GetMostPopularMusicAsync(region, pageToken);

                        // Một số quốc gia không hỗ trợ chart Music -> bỏ qua quốc gia đó
                        if (!ctx.Check(chart) || chart.Error != null)
                            break;

                        foreach (var video in chart.Items)
                            ctx.Videos.TryAdd(video.VideoId, video);

                        pageToken = chart.NextPageToken;
                        if (string.IsNullOrEmpty(pageToken))
                            break;
                    }

                    var current = Interlocked.Increment(ref done);
                    _state.Update(s =>
                    {
                        s.Fetched = ctx.Videos.Count;
                        s.Detail = $"Chart quốc gia {current}/{regions.Count}";
                    });
                });
        }

        private async Task FetchKeywordSearchAsync(FetchContext ctx)
        {
            var keywords = _state.TakeKeywords(SearchKeywords, KeywordsPerSync);
            var ids = new ConcurrentBag<string>();
            var done = 0;

            await Parallel.ForEachAsync(
                keywords,
                new ParallelOptions { MaxDegreeOfParallelism = YoutubeParallelism },
                async (keyword, _) =>
                {
                    if (ctx.QuotaExceeded)
                        return;

                    var page = await ctx.Youtube.SearchMusicVideoIdsAsync(keyword, null);
                    if (ctx.Check(page))
                    {
                        foreach (var id in page.Items)
                            ids.Add(id);
                    }

                    var current = Interlocked.Increment(ref done);
                    _state.Update(s => s.Detail = $"Tìm theo từ khóa {current}/{keywords.Count}");
                });

            await FetchDetailsAsync(ctx, ids);
        }

        private async Task FetchChannelUploadsAsync(FetchContext ctx)
        {
            // Kênh có tổng view cao nhất trước (thường là kênh chính thức của nghệ sĩ/hãng đĩa)
            var channelIds = ctx.Videos.Values
                .Where(IsMusic)
                .Where(x => !string.IsNullOrEmpty(x.ChannelId))
                .GroupBy(x => x.ChannelId)
                .OrderByDescending(g => g.Sum(x => x.Views))
                .Select(g => g.Key)
                .Take(MaxChannels)
                .ToList();

            var playlistIds = new List<string>();

            foreach (var chunk in channelIds.Chunk(YoutubeIdsPerCall))
            {
                var page = await ctx.Youtube.GetUploadsPlaylistIdsAsync(chunk);
                if (!ctx.Check(page))
                    return;

                playlistIds.AddRange(page.Items.Select(x => x.PlaylistId));
            }

            var ids = new ConcurrentBag<string>();
            var done = 0;

            await Parallel.ForEachAsync(
                playlistIds,
                new ParallelOptions { MaxDegreeOfParallelism = YoutubeParallelism },
                async (playlistId, _) =>
                {
                    string? pageToken = null;

                    for (var page = 0; page < MaxUploadPagesPerChannel && !ctx.QuotaExceeded; page++)
                    {
                        var items = await ctx.Youtube.GetPlaylistVideoIdsAsync(playlistId, pageToken);
                        if (!ctx.Check(items) || items.Error != null)
                            break;

                        foreach (var id in items.Items)
                            ids.Add(id);

                        pageToken = items.NextPageToken;
                        if (string.IsNullOrEmpty(pageToken))
                            break;
                    }

                    var current = Interlocked.Increment(ref done);
                    _state.Update(s => s.Detail = $"Quét kênh nghệ sĩ {current}/{playlistIds.Count}");
                });

            await FetchDetailsAsync(ctx, ids);
        }

        /// <summary>
        /// Lấy chi tiết (duration/category/status) cho các videoId mới: bỏ id đã lấy rồi
        /// và id đã có trong DB trước, để không tốn quota videos.list vô ích.
        /// </summary>
        private async Task FetchDetailsAsync(FetchContext ctx, IEnumerable<string> videoIds)
        {
            var candidates = videoIds
                .Distinct()
                .Where(id => !ctx.Videos.ContainsKey(id))
                .ToList();

            var existing = await GetExistingVideoIdsAsync(candidates);
            var newIds = candidates.Where(id => !existing.Contains(id)).ToList();

            _state.Update(s => s.Duplicated += existing.Count);

            var chunks = newIds.Chunk(YoutubeIdsPerCall).ToList();
            var done = 0;

            await Parallel.ForEachAsync(
                chunks,
                new ParallelOptions { MaxDegreeOfParallelism = YoutubeParallelism },
                async (chunk, _) =>
                {
                    if (ctx.QuotaExceeded)
                        return;

                    var page = await ctx.Youtube.GetVideosByIdsAsync(chunk);
                    if (ctx.Check(page))
                    {
                        foreach (var video in page.Items)
                            ctx.Videos.TryAdd(video.VideoId, video);
                    }

                    var current = Interlocked.Increment(ref done);
                    _state.Update(s =>
                    {
                        s.Fetched = ctx.Videos.Count;
                        s.Detail = $"Lấy thông tin video {Math.Min(current * YoutubeIdsPerCall, newIds.Count)}/{newIds.Count}";
                    });
                });
        }

        private static bool IsMusic(YoutubeVideoDto video)
        {
            if (video.CategoryId != "10")
                return false;

            if (!string.IsNullOrEmpty(video.LiveBroadcastContent) && video.LiveBroadcastContent != "none")
                return false;

            if (!video.Embeddable || video.PrivacyStatus != "public")
                return false;

            if (video.Duration < MinDurationSeconds || video.Duration > MaxDurationSeconds)
                return false;

            return !NonMusicTitleRegex.IsMatch(TextSearchHelper.Normalize(video.Title));
        }

        /// <summary>
        /// Các videoId đã có trong DB. Tính cả bài đã xóa: admin xóa rồi thì không tự thêm lại.
        /// </summary>
        private async Task<HashSet<string>> GetExistingVideoIdsAsync(IEnumerable<string> videoIds)
        {
            var result = new HashSet<string>();

            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IDistUnitOfWork>();
            var repoSong = uow.GetRepositoryReadOnlyAsync<DistSongs>();

            foreach (var chunk in videoIds.Chunk(5000))
            {
                var ids = chunk.ToList();

                var existing = await repoSong
                    .QueryAll()
                    .Where(x => x.YoutubeVideoId != null && ids.Contains(x.YoutubeVideoId))
                    .Select(x => x.YoutubeVideoId!)
                    .ToListAsync();

                result.UnionWith(existing);
            }

            return result;
        }

        private async Task<List<(long SongId, YoutubeVideoDto Video)>> InsertSongsAsync(List<YoutubeVideoDto> videos, long userId)
        {
            _state.Update(s => s.Stage = SongSyncStage.Saving);

            var inserted = new List<(long SongId, YoutubeVideoDto Video)>();

            foreach (var batch in videos.Chunk(InsertBatchSize))
            {
                var artistIds = await EnsureArtistsAsync(batch, userId);

                try
                {
                    inserted.AddRange(await SaveSongsAsync(batch, artistIds, userId));
                }
                catch (DbUpdateException)
                {
                    // Có bài vừa được user nghe chen vào giữa chừng (vướng unique index) -> lưu lại từng bài
                    foreach (var video in batch)
                    {
                        try
                        {
                            inserted.AddRange(await SaveSongsAsync(new[] { video }, artistIds, userId));
                        }
                        catch (DbUpdateException)
                        {
                            _state.Update(s => s.Duplicated++);
                        }
                    }
                }

                _state.Update(s => s.Inserted = inserted.Count);
            }

            return inserted;
        }

        /// <summary>
        /// Artist = tên kênh, giống cách AddSongHistory đang tạo artist cho bài Youtube.
        /// </summary>
        private async Task<Dictionary<string, long>> EnsureArtistsAsync(IEnumerable<YoutubeVideoDto> videos, long userId)
        {
            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IDistUnitOfWork>();
            var repoArtist = uow.GetRepositoryAsync<DistArtists>();

            var channels = videos
                .Where(x => !string.IsNullOrWhiteSpace(x.Channel))
                .GroupBy(x => x.Channel.Trim().ToLower())
                .ToDictionary(g => g.Key, g => g.First());

            var keys = channels.Keys.ToList();

            var existing = await repoArtist
                .QueryCondition(x => !x.IsDeleted && keys.Contains(x.Name.ToLower()))
                .ToListAsync();

            var result = new Dictionary<string, long>();

            foreach (var artist in existing)
                result.TryAdd(artist.Name.ToLower(), artist.Id);

            var created = channels
                .Where(x => !result.ContainsKey(x.Key))
                .Select(x => new DistArtists
                {
                    Name = x.Value.Channel.Trim(),
                    StageName = x.Value.Channel.Trim(),
                    CreatedBy = userId
                })
                .ToList();

            if (created.Count > 0)
            {
                await repoArtist.AddAsync(created);
                await uow.SaveChangesAsync();

                foreach (var artist in created)
                    result.TryAdd(artist.Name.ToLower(), artist.Id);
            }

            return result;
        }

        private async Task<List<(long SongId, YoutubeVideoDto Video)>> SaveSongsAsync(
            IEnumerable<YoutubeVideoDto> videos, Dictionary<string, long> artistIds, long userId)
        {
            // Scope riêng cho mỗi lần lưu: lỗi ở batch trước không làm kẹt change tracker
            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IDistUnitOfWork>();
            var repoSong = uow.GetRepositoryAsync<DistSongs>();

            var pairs = videos
                .Select(video => (
                    Song: new DistSongs
                    {
                        Title = video.Title,
                        YoutubeVideoId = video.VideoId,
                        Duration = video.Duration,
                        ImgUrl = video.ThumbnailHigh,
                        ReleaseDate = DateOnly.FromDateTime(video.PublishedAt ?? DateTimeHelper.VnNow),
                        ArtistId = artistIds.TryGetValue(video.Channel.Trim().ToLower(), out var artistId)
                            ? artistId
                            : null,
                        Type = 1,
                        SourceType = 3,
                        PlayCount = 0,
                        Views = 0,
                        Likes = 0,
                        CreatedBy = userId
                    },
                    Video: video))
                .ToList();

            await repoSong.AddAsync(pairs.Select(x => x.Song));
            await uow.SaveChangesAsync();

            return pairs.Select(x => (x.Song.Id, x.Video)).ToList();
        }

        private async Task FetchLyricsAsync(List<(long SongId, YoutubeVideoDto Video)> songs, long userId)
        {
            _state.Update(s =>
            {
                s.Stage = SongSyncStage.Lyrics;
                s.LyricsTotal = songs.Count;
            });

            if (songs.Count == 0)
                return;

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MusicEmolite/1.0");

            foreach (var chunk in songs.Chunk(LyricsChunkSize))
            {
                var found = new ConcurrentBag<DistSongLyrics>();

                await Parallel.ForEachAsync(
                    chunk,
                    new ParallelOptions { MaxDegreeOfParallelism = LyricsParallelism },
                    async (song, _) =>
                    {
                        try
                        {
                            var match = await FindLyricsAsync(client, song.Video);

                            if (match != null)
                            {
                                var synced = LyricsService.ParseLrc(match.RawSyncedLyrics);
                                var plain = !string.IsNullOrWhiteSpace(match.Lyrics)
                                    ? match.Lyrics
                                    : string.Join("\n", synced.Select(x => x.Text));

                                found.Add(new DistSongLyrics
                                {
                                    SongId = song.SongId,
                                    Content = plain,
                                    SyncedJson = synced.Count > 0 ? JsonSerializer.Serialize(synced) : null,
                                    IsOfficial = false,
                                    CreatedBy = userId
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            // Lỗi lyrics 1 bài không làm hỏng cả lần đồng bộ
                            _logger.LogWarning(ex, "Lyrics lookup failed for {VideoId}", song.Video.VideoId);
                        }

                        _state.Update(s => s.LyricsProcessed++);
                    });

                if (found.IsEmpty)
                    continue;

                using var scope = _scopeFactory.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IDistUnitOfWork>();

                await uow.GetRepositoryAsync<DistSongLyrics>().AddAsync(found.ToList());
                await uow.SaveChangesAsync();

                _state.Update(s => s.LyricsFound += found.Count);
            }
        }

        private async Task<LyricsResponseDto?> FindLyricsAsync(HttpClient client, YoutubeVideoDto video)
        {
            var (title, artist) = ParseTitle(video.Title, video.Channel);

            if (string.IsNullOrWhiteSpace(title))
                return null;

            var query = $"track_name={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrWhiteSpace(artist))
                query += $"&artist_name={Uri.EscapeDataString(artist)}";

            // MV thường dài hơn bản audio (intro/outro) nên nới lệch thời lượng khi đã khớp tên + ca sĩ
            var best = PickBest(await SearchLrclibAsync(client, query), video.Duration, 30);

            if (best == null)
            {
                var q = $"{artist} {title}".Trim();
                best = PickBest(await SearchLrclibAsync(client, $"q={Uri.EscapeDataString(q)}"), video.Duration, 10);
            }

            return best;
        }

        private async Task<List<LyricsResponseDto>> SearchLrclibAsync(HttpClient client, string query)
        {
            var result = new List<LyricsResponseDto>();

            var response = await client.GetAsync($"{_lyricsSettings.Url}/search?{query}");
            if (!response.IsSuccessStatusCode)
                return result;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                result.Add(new LyricsResponseDto
                {
                    Duration = item.TryGetProperty("duration", out var duration)
                        && duration.ValueKind == JsonValueKind.Number
                            ? (int)Math.Round(duration.GetDouble())
                            : 0,
                    Instrumental = item.TryGetProperty("instrumental", out var instrumental)
                        && instrumental.ValueKind == JsonValueKind.True,
                    Lyrics = item.TryGetProperty("plainLyrics", out var plain)
                        && plain.ValueKind == JsonValueKind.String
                            ? plain.GetString() ?? ""
                            : "",
                    RawSyncedLyrics = item.TryGetProperty("syncedLyrics", out var synced)
                        && synced.ValueKind == JsonValueKind.String
                            ? synced.GetString() ?? ""
                            : ""
                });
            }

            return result;
        }

        private static LyricsResponseDto? PickBest(List<LyricsResponseDto> candidates, int videoDuration, int toleranceSeconds)
        {
            return candidates
                .Where(x => !x.Instrumental)
                .Where(x => !string.IsNullOrWhiteSpace(x.Lyrics) || !string.IsNullOrWhiteSpace(x.RawSyncedLyrics))
                .Where(x => videoDuration == 0 || x.Duration == 0 || Math.Abs(x.Duration - videoDuration) <= toleranceSeconds)
                .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.RawSyncedLyrics))
                .ThenBy(x => Math.Abs(x.Duration - videoDuration))
                .FirstOrDefault();
        }

        /// <summary>
        /// "Sơn Tùng M-TP - Lạc Trôi (Official MV) ft. X" -> ("Lạc Trôi", "Sơn Tùng M-TP").
        /// Không có dấu "-" thì lấy tên kênh làm ca sĩ.
        /// </summary>
        private static (string Title, string Artist) ParseTitle(string rawTitle, string channel)
        {
            var text = TitleNoiseRegex.Replace(BracketRegex.Replace(rawTitle, " "), " ");
            var channelArtist = CleanPart(ChannelNoiseRegex.Replace(channel, " "));

            var segments = text
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(CleanPart)
                .Where(x => x.Length > 0)
                .ToList();

            if (segments.Count == 0)
                return ("", channelArtist);

            var match = ArtistTitleRegex.Match(segments[0]);
            if (match.Success)
                return (CleanPart(match.Groups[2].Value), CleanPart(match.Groups[1].Value));

            // "CA SĨ | TÊN BÀI | MV" hoặc "TÊN BÀI | CA SĨ": đoạn nào giống tên kênh thì là ca sĩ
            if (segments.Count >= 2)
            {
                var channelKey = TextSearchHelper.Normalize(channelArtist).Replace(" ", "");
                bool IsChannel(string s)
                {
                    var key = TextSearchHelper.Normalize(s).Replace(" ", "");
                    return channelKey.Length > 0 && (channelKey.Contains(key) || key.Contains(channelKey));
                }

                if (IsChannel(segments[0]))
                    return (segments[1], segments[0]);

                if (IsChannel(segments[1]))
                    return (segments[0], segments[1]);
            }

            return (segments[0], channelArtist);
        }

        private static string CleanPart(string value)
        {
            // Cắt "ft. X" theo từng phần (sau khi tách "ca sĩ - tên bài") để không cắt mất tên bài
            value = FeatRegex.Replace(" " + value, " ");
            value = Regex.Replace(value, @"\s+", " ");
            return value.Trim(' ', '-', '–', '—', '"', '\'', '“', '”', '‘', '’');
        }
    }
}
