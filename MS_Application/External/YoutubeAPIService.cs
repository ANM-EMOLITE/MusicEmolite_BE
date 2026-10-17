using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Youtube;
using MS_Application.Repositories.Interfaces;
using MS_Application.Services.Interfaces.External;
using MS_Domain.Common;
using MS_Domain.Entities.DISTS;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Xml;

namespace MS_Application.External;

public partial class YoutubeAPIService : IYoutubeAPIService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly IDistUnitOfWork _distUnitOfWork;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// One Youtube search.list page = up to 50 results = 100 quota units. We
    /// used to eagerly prefetch a large batch (originally 300, i.e. up to 6
    /// calls) on every NEW keyword so infinite-scroll on the FE would never
    /// have to hit Youtube again. That's what was blowing through the
    /// "Search Queries per day" quota (hard-capped at 100 requests/day) after
    /// only a handful of new keywords per day.
    ///
    /// Instead we now grow the cached result list lazily, one Youtube page at
    /// a time, only when a request actually needs records we don't have yet
    /// (see SearchAsync). A user who never scrolls past the first screen
    /// costs exactly 1 search.list call for that keyword, no matter how deep
    /// the cache eventually grows for users who keep scrolling.
    /// </summary>
    private const int YoutubePageSize = 50;

    // How long we keep a keyword's accumulated results around. Generous
    // because repeat searches for the same/popular keyword should be pure
    // cache hits instead of new search.list calls.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    // When Youtube tells us we're quota-exceeded (429), stop calling it for a
    // while instead of letting every request for a new/deeper keyword page
    // fire another doomed request.
    private const string QuotaExceededCacheKey = "youtube_quota_exceeded";
    private static readonly TimeSpan QuotaExceededCooldown = TimeSpan.FromMinutes(15);

    // Per-keyword gate: if N users hit a brand-new (or not-yet-deep-enough)
    // keyword at the same moment, only the first actually calls Youtube - the
    // rest wait, then read the cache entry that call just grew.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _searchLocks = new();

    private class SearchCacheEntry
    {
        public List<YoutubeVideoDto> Videos { get; } = new();
        public string? NextPageToken { get; set; }
        public bool NoMoreResults { get; set; }
    }

    public YoutubeAPIService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IDistUnitOfWork distUnitOfWork,
        IMemoryCache cache)
    {
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
        _distUnitOfWork = distUnitOfWork;
        _cache = cache;
    }

    public async Task<BaseTableResponse<YoutubeVideoDto>> SearchAsync(BaseSearchDto<YoutubeSearchRequestDto> request, long userId)
    {
        var response = new BaseTableResponse<YoutubeVideoDto>();

        var keyword = request.SearchParams?.Keyword;

        if (string.IsNullOrWhiteSpace(keyword))
        {
            response.Code = ResponseStatusCode.Status400;
            response.Type = GlobalConstants.ResponseType.Error;
            response.Message = "Keyword is required";

            return response;
        }

        var apiKey = _configuration["Youtube:ApiKey"];

        var pageSize = request.PageSize > 0
            ? Math.Min(request.PageSize, 50)
            : 20;

        var currentPage = Math.Max(request.Page, 1);
        var neededCount = currentPage * pageSize;

        var cacheKey = $"youtube_search_{keyword.Trim().ToLowerInvariant()}";

        if (!_cache.TryGetValue(cacheKey, out SearchCacheEntry? entry) || entry == null)
        {
            entry = new SearchCacheEntry();
        }

        var quotaBlocked = false;

        // Only talk to Youtube if the cache doesn't already have enough records
        // to serve the page being requested right now.
        if (entry.Videos.Count < neededCount && !entry.NoMoreResults)
        {
            var gate = _searchLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();

            try
            {
                // Another request may have grown (or created) this cache entry while we waited.
                if (_cache.TryGetValue(cacheKey, out SearchCacheEntry? fresh) && fresh != null)
                {
                    entry = fresh;
                }

                while (entry.Videos.Count < neededCount && !entry.NoMoreResults)
                {
                    if (_cache.TryGetValue(QuotaExceededCacheKey, out _))
                    {
                        quotaBlocked = true;
                        break;
                    }

                    var (fetched, nextToken, hitQuota) =
                        await FetchYoutubePageAsync(keyword, apiKey, entry.NextPageToken);

                    if (hitQuota)
                    {
                        quotaBlocked = true;
                        _cache.Set(QuotaExceededCacheKey, true, QuotaExceededCooldown);
                        break;
                    }

                    if (fetched.Count > 0)
                    {
                        entry.Videos.AddRange(fetched);
                    }

                    entry.NextPageToken = nextToken;

                    if (fetched.Count == 0 || string.IsNullOrEmpty(nextToken))
                    {
                        entry.NoMoreResults = true;
                    }
                }

                if (entry.Videos.Count > 0)
                {
                    _cache.Set(cacheKey, entry, CacheDuration);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        if (entry.Videos.Count == 0)
        {
            if (quotaBlocked)
            {
                response.Code = ResponseStatusCode.Status429;
                response.Type = GlobalConstants.ResponseType.Error;
                response.Message = "Youtube API quota exceeded, please try again later";

                return response;
            }

            response.Code = ResponseStatusCode.Status200;
            response.Type = GlobalConstants.ResponseType.Success;
            response.Message = "Search youtube success";
            response.TotalRecords = 0;
            response.TotalPages = 0;
            response.Data = new List<YoutubeVideoDto>();

            return response;
        }

        var totalRecords = entry.Videos.Count;
        var loadedPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

        // While Youtube may still have more (we just haven't fetched it yet),
        // report one page beyond what's loaded so the FE's infinite-scroll
        // `hasMore` check (page < totalPages) keeps triggering `loadMore()` -
        // that next call is what actually fetches it. Once Youtube is
        // exhausted, or we're sitting out a quota cooldown, stop advertising
        // a page we can't actually serve.
        var totalPages = (entry.NoMoreResults || quotaBlocked)
            ? loadedPages
            : Math.Max(currentPage + 1, loadedPages);

        // Clone so per-user like status (set below) never mutates the shared cache entry.
        var pageVideos = entry.Videos
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize)
            .Select(CloneVideo)
            .ToList();

        if (pageVideos.Count > 0)
        {
            await SaveNewVideosAsync(pageVideos, userId);
            await AttachLikeStatusAsync(pageVideos, userId);
        }

        response.Code = ResponseStatusCode.Status200;
        response.Type = GlobalConstants.ResponseType.Success;
        response.Message = "Search youtube success";

        response.TotalRecords = totalRecords;
        response.TotalPages = totalPages;
        response.Data = pageVideos;

        return response;
    }

    /// <summary>
    /// Fetches exactly one Youtube search.list page (up to 50 results) plus
    /// the videos.list/channels.list enrichment for just that page, and
    /// returns Youtube's next page token so the caller can keep going lazily.
    /// </summary>
    private async Task<(List<YoutubeVideoDto> Videos, string? NextPageToken, bool QuotaExceeded)> FetchYoutubePageAsync(
        string keyword, string? apiKey, string? pageToken)
    {
        var searchUrl =
            $"https://www.googleapis.com/youtube/v3/search" +
            $"?part=snippet" +
            $"&type=video" +
            $"&maxResults={YoutubePageSize}" +
            $"&q={Uri.EscapeDataString(keyword)}" +
            (string.IsNullOrEmpty(pageToken) ? "" : $"&pageToken={pageToken}") +
            $"&key={apiKey}";

        var youtubeResponse = await _httpClient.GetAsync(searchUrl);

        if (youtubeResponse.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return (new List<YoutubeVideoDto>(), null, true);
        }

        if (!youtubeResponse.IsSuccessStatusCode)
        {
            return (new List<YoutubeVideoDto>(), null, false);
        }

        var videos = new List<YoutubeVideoDto>();
        string? nextPageToken;

        var json = await youtubeResponse.Content.ReadAsStringAsync();

        using (var doc = JsonDocument.Parse(json))
        {
            foreach (var item in doc.RootElement
                         .GetProperty("items")
                         .EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                var thumbnails = snippet.GetProperty("thumbnails");

                var video = new YoutubeVideoDto
                {
                    VideoId = item
                        .GetProperty("id")
                        .GetProperty("videoId")
                        .GetString() ?? "",

                    Kind = item.TryGetProperty("kind", out var kind)
                        ? kind.GetString() ?? ""
                        : "",

                    Etag = item.TryGetProperty("etag", out var etag)
                        ? etag.GetString() ?? ""
                        : "",

                    Title = snippet.TryGetProperty("title", out var title)
                        ? title.GetString() ?? ""
                        : "",

                    Description = snippet.TryGetProperty("description", out var desc)
                        ? desc.GetString() ?? ""
                        : "",

                    ChannelId = snippet.TryGetProperty("channelId", out var channelId)
                        ? channelId.GetString() ?? ""
                        : "",

                    Channel = snippet.TryGetProperty("channelTitle", out var channel)
                        ? channel.GetString() ?? ""
                        : "",

                    PublishedAt = snippet.TryGetProperty("publishedAt", out var publishedAt)
                        && publishedAt.TryGetDateTime(out var parsedPublishedAt)
                            ? parsedPublishedAt
                            : null,

                    ThumbnailDefault = GetThumbnailUrl(thumbnails, "default"),
                    ThumbnailMedium = GetThumbnailUrl(thumbnails, "medium"),
                    ThumbnailHigh = GetThumbnailUrl(thumbnails, "high"),
                    ThumbnailStandard = GetThumbnailUrl(thumbnails, "standard"),
                    ThumbnailMaxres = GetThumbnailUrl(thumbnails, "maxres"),

                    LiveBroadcastContent = snippet.TryGetProperty("liveBroadcastContent", out var live)
                        ? live.GetString() ?? ""
                        : "",

                    PublishTime = snippet.TryGetProperty("publishTime", out var publishTime)
                        ? publishTime.GetString() ?? ""
                        : ""
                };

                videos.Add(video);
            }

            nextPageToken = doc.RootElement.TryGetProperty("nextPageToken", out var npt)
                ? npt.GetString()
                : null;
        }

        await EnrichVideosAsync(videos, apiKey);

        return (videos, nextPageToken, false);
    }

    /// <summary>
    /// Fills in duration/stats/embeddability/channel-thumbnail for one page
    /// (&lt;= 50) of freshly-fetched videos via videos.list + channels.list.
    /// These endpoints aren't the tight "Search Queries per day" quota - they
    /// draw from the much larger general daily pool - so batching per page
    /// here (instead of across the whole cached list) is fine.
    /// </summary>
    private async Task EnrichVideosAsync(List<YoutubeVideoDto> videos, string? apiKey)
    {
        var videoIds = videos
            .Select(x => x.VideoId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (videoIds.Count == 0)
            return;

        var channelIds = videos
            .Select(x => x.ChannelId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var detailsById = new Dictionary<string, JsonElement>();
        var channelById = new Dictionary<string, JsonElement>();
        var docsToDispose = new List<JsonDocument>();

        try
        {
            // videoIds/channelIds are each <= 50 here (one search page), so this is always a single call.
            var detailsUrl =
                $"https://www.googleapis.com/youtube/v3/videos" +
                $"?part=contentDetails,statistics,status,player" +
                $"&id={string.Join(",", videoIds)}" +
                $"&key={apiKey}";

            var detailsResponse = await _httpClient.GetAsync(detailsUrl);

            if (detailsResponse.IsSuccessStatusCode)
            {
                var detailsJson = await detailsResponse.Content.ReadAsStringAsync();
                var detailsDoc = JsonDocument.Parse(detailsJson);
                docsToDispose.Add(detailsDoc);

                foreach (var item in detailsDoc.RootElement.GetProperty("items").EnumerateArray())
                {
                    var id = item.GetProperty("id").GetString() ?? "";
                    if (!string.IsNullOrEmpty(id))
                        detailsById[id] = item;
                }
            }

            if (channelIds.Count > 0)
            {
                var channelsUrl =
                    $"https://www.googleapis.com/youtube/v3/channels" +
                    $"?part=snippet" +
                    $"&id={string.Join(",", channelIds)}" +
                    $"&key={apiKey}";

                var channelsResponse = await _httpClient.GetAsync(channelsUrl);

                if (channelsResponse.IsSuccessStatusCode)
                {
                    var channelsJson = await channelsResponse.Content.ReadAsStringAsync();
                    var channelsDoc = JsonDocument.Parse(channelsJson);
                    docsToDispose.Add(channelsDoc);

                    foreach (var item in channelsDoc.RootElement.GetProperty("items").EnumerateArray())
                    {
                        var id = item.GetProperty("id").GetString() ?? "";
                        if (!string.IsNullOrEmpty(id))
                            channelById[id] = item;
                    }
                }
            }

            foreach (var video in videos)
            {
                if (detailsById.TryGetValue(video.VideoId, out var detail))
                {
                    if (detail.TryGetProperty("contentDetails", out var contentDetails))
                    {
                        video.DurationRaw = contentDetails.TryGetProperty("duration", out var duration)
                            ? duration.GetString() ?? ""
                            : "";

                        video.Duration = ParseYoutubeDurationToSeconds(video.DurationRaw);

                        video.Dimension = contentDetails.TryGetProperty("dimension", out var dimension)
                            ? dimension.GetString() ?? ""
                            : "";

                        video.Definition = contentDetails.TryGetProperty("definition", out var definition)
                            ? definition.GetString() ?? ""
                            : "";

                        video.Caption = contentDetails.TryGetProperty("caption", out var caption)
                            && caption.GetString() == "true";

                        video.LicensedContent = contentDetails.TryGetProperty("licensedContent", out var licensedContent)
                            && licensedContent.ValueKind == JsonValueKind.True;

                        video.Projection = contentDetails.TryGetProperty("projection", out var projection)
                            ? projection.GetString() ?? ""
                            : "";
                    }

                    if (detail.TryGetProperty("statistics", out var statistics))
                    {
                        video.Views = statistics.TryGetProperty("viewCount", out var viewCount)
                            && long.TryParse(viewCount.GetString(), out var parsedViews)
                                ? parsedViews
                                : 0;

                        video.LikeCount = statistics.TryGetProperty("likeCount", out var likeCount)
                            && long.TryParse(likeCount.GetString(), out var parsedLikes)
                                ? parsedLikes
                                : 0;

                        video.CommentCount = statistics.TryGetProperty("commentCount", out var commentCount)
                            && long.TryParse(commentCount.GetString(), out var parsedComments)
                                ? parsedComments
                                : 0;
                    }

                    if (detail.TryGetProperty("status", out var status))
                    {
                        video.Embeddable = status.TryGetProperty("embeddable", out var embeddable)
                            && embeddable.ValueKind == JsonValueKind.True;

                        video.PublicStatsViewable = status.TryGetProperty("publicStatsViewable", out var publicStats)
                            && publicStats.ValueKind == JsonValueKind.True;

                        video.PrivacyStatus = status.TryGetProperty("privacyStatus", out var privacyStatus)
                            ? privacyStatus.GetString() ?? ""
                            : "";

                        video.UploadStatus = status.TryGetProperty("uploadStatus", out var uploadStatus)
                            ? uploadStatus.GetString() ?? ""
                            : "";
                    }

                    if (detail.TryGetProperty("player", out var player))
                    {
                        video.EmbedHtml = player.TryGetProperty("embedHtml", out var embedHtml)
                            ? embedHtml.GetString() ?? ""
                            : "";
                    }
                }

                if (channelById.TryGetValue(video.ChannelId, out var channelDetail)
                    && channelDetail.TryGetProperty("snippet", out var channelSnippet)
                    && channelSnippet.TryGetProperty("thumbnails", out var channelThumbnails))
                {
                    video.ChannelThumbnail = GetThumbnailUrl(channelThumbnails, "high");

                    if (string.IsNullOrEmpty(video.ChannelThumbnail))
                        video.ChannelThumbnail = GetThumbnailUrl(channelThumbnails, "medium");

                    if (string.IsNullOrEmpty(video.ChannelThumbnail))
                        video.ChannelThumbnail = GetThumbnailUrl(channelThumbnails, "default");
                }
            }
        }
        finally
        {
            foreach (var d in docsToDispose) d.Dispose();
        }
    }

    /// <summary>
    /// Lưu luôn các bài của trang kết quả vào DistSongs (play vẫn tự add như cũ qua AddSongHistory).
    /// Check trùng theo YoutubeVideoId, tính cả bài đã xóa: admin xóa rồi thì không tự thêm lại (giống SongSyncService).
    /// Bỏ qua live/upcoming vì không phải bài hát.
    /// </summary>
    private async Task SaveNewVideosAsync(List<YoutubeVideoDto> pageVideos, long userId)
    {
        var candidates = pageVideos.Where(x => !string.IsNullOrWhiteSpace(x.VideoId) && x.LiveBroadcastContent is not ("live" or "upcoming")).DistinctBy(x => x.VideoId).ToList();
        if (candidates.Count == 0) return;

        var repoSong = _distUnitOfWork.GetRepositoryAsync<DistSongs>();
        var repoArtist = _distUnitOfWork.GetRepositoryAsync<DistArtists>();

        var videoIds = candidates.Select(x => x.VideoId).ToList();
        var existingIds = await repoSong.QueryAll().Where(x => x.YoutubeVideoId != null && videoIds.Contains(x.YoutubeVideoId)).Select(x => x.YoutubeVideoId!).ToListAsync();

        var newVideos = candidates.Where(x => !existingIds.Contains(x.VideoId)).ToList();
        if (newVideos.Count == 0) return;

        try
        {
            // Artist = tên kênh, giống AddSongHistory / SongSyncService
            var channels = newVideos.Where(x => !string.IsNullOrWhiteSpace(x.Channel)).GroupBy(x => x.Channel.Trim().ToLower()).ToDictionary(g => g.Key, g => g.First());
            var keys = channels.Keys.ToList();

            var artistIds = (await repoArtist.QueryCondition(x => !x.IsDeleted && keys.Contains(x.Name.ToLower())).ToListAsync())
                .GroupBy(x => x.Name.ToLower()).ToDictionary(g => g.Key, g => g.First().Id);

            var newArtists = channels.Where(x => !artistIds.ContainsKey(x.Key))
                .Select(x => new DistArtists { Name = x.Value.Channel.Trim(), StageName = x.Value.Channel.Trim(), Url = x.Value.ChannelThumbnail, CreatedBy = userId })
                .ToList();

            if (newArtists.Count > 0)
            {
                await repoArtist.AddAsync(newArtists);
                await _distUnitOfWork.SaveChangesAsync();
                foreach (var artist in newArtists) artistIds.TryAdd(artist.Name.ToLower(), artist.Id);
            }

            var songs = newVideos.Select(video => new DistSongs
            {
                Title = video.Title,
                YoutubeVideoId = video.VideoId,
                Duration = video.Duration,
                ImgUrl = video.ThumbnailHigh,
                ReleaseDate = DateOnly.FromDateTime(video.PublishedAt ?? DateTimeHelper.VnNow),
                ArtistId = artistIds.TryGetValue(video.Channel.Trim().ToLower(), out var artistId) ? artistId : null,
                Type = 1,
                SourceType = 3,
                PlayCount = 0,
                Views = 0,
                Likes = 0,
                CreatedBy = userId
            }).ToList();

            await repoSong.AddAsync(songs);
            await _distUnitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Có bài vừa được user khác search/play chen vào (vướng unique index) - bỏ qua, lần search/play sau sẽ lưu nốt.
            // Search vẫn trả kết quả bình thường.
        }
    }

    private async Task AttachLikeStatusAsync(List<YoutubeVideoDto> pageVideos, long userId)
    {
        var videoIds = pageVideos
            .Select(x => x.VideoId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (videoIds.Count == 0)
            return;

        var repoSong = _distUnitOfWork
            .GetRepositoryReadOnlyAsync<DistSongs>()
            .QueryAll();

        var repoUserLike = _distUnitOfWork
            .GetRepositoryReadOnlyAsync<DistUserLikes>()
            .QueryAll();

        var repoSongAlbum = _distUnitOfWork
            .GetRepositoryReadOnlyAsync<DistSongAlbums>()
            .QueryAll();

        var songByVideoId = repoSong
            .Where(x =>
                !x.IsDeleted &&
                x.YoutubeVideoId != null &&
                videoIds.Contains(x.YoutubeVideoId))
            .Select(x => new
            {
                x.Id,
                x.YoutubeVideoId
            })
            .ToList();

        var songIds = songByVideoId
            .Select(x => x.Id)
            .ToList();

        var likedSongIds = repoUserLike
            .Where(x =>
                !x.IsDeleted &&
                x.UserId == userId &&
                songIds.Contains(x.SongId))
            .Select(x => x.SongId)
            .ToList();

        var albumIdsBySongId = repoSongAlbum
            .Where(x =>
                !x.IsDeleted &&
                songIds.Contains(x.SongId))
            .Select(x => new { x.SongId, x.AlbumId })
            .ToList()
            .GroupBy(x => x.SongId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.AlbumId).ToList());

        foreach (var video in pageVideos)
        {
            var song = songByVideoId
                .FirstOrDefault(x =>
                    x.YoutubeVideoId == video.VideoId);
            video.SongId = song?.Id;
            video.IsLiked =
                song != null &&
                likedSongIds.Contains(song.Id);
            video.AlbumIds =
                song != null && albumIdsBySongId.TryGetValue(song.Id, out var albumIds)
                    ? albumIds
                    : new List<long>();
        }
    }

    private static YoutubeVideoDto CloneVideo(YoutubeVideoDto v) => new()
    {
        VideoId = v.VideoId,
        Kind = v.Kind,
        Etag = v.Etag,
        Title = v.Title,
        Description = v.Description,
        ChannelId = v.ChannelId,
        Channel = v.Channel,
        ChannelThumbnail = v.ChannelThumbnail,
        PublishedAt = v.PublishedAt,
        ThumbnailDefault = v.ThumbnailDefault,
        ThumbnailMedium = v.ThumbnailMedium,
        ThumbnailHigh = v.ThumbnailHigh,
        ThumbnailStandard = v.ThumbnailStandard,
        ThumbnailMaxres = v.ThumbnailMaxres,
        LiveBroadcastContent = v.LiveBroadcastContent,
        PublishTime = v.PublishTime,
        DurationRaw = v.DurationRaw,
        Duration = v.Duration,
        Dimension = v.Dimension,
        Definition = v.Definition,
        Caption = v.Caption,
        LicensedContent = v.LicensedContent,
        Projection = v.Projection,
        Views = v.Views,
        LikeCount = v.LikeCount,
        CommentCount = v.CommentCount,
        Embeddable = v.Embeddable,
        PublicStatsViewable = v.PublicStatsViewable,
        PrivacyStatus = v.PrivacyStatus,
        UploadStatus = v.UploadStatus,
        EmbedHtml = v.EmbedHtml
    };

    private static string GetThumbnailUrl(JsonElement thumbnails, string key)
    {
        if (!thumbnails.TryGetProperty(key, out var thumbnail))
            return "";

        return thumbnail.TryGetProperty("url", out var url)
            ? url.GetString() ?? ""
            : "";
    }

    private static int ParseYoutubeDurationToSeconds(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return 0;

        try
        {
            return (int)XmlConvert.ToTimeSpan(duration).TotalSeconds;
        }
        catch
        {
            return 0;
        }
    }
}
