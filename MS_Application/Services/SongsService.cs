using MS_Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Lyrics;
using MS_Application.DataTransferObjects.Songs;
using MS_Application.Helpers;
using MS_Application.Repositories.Interfaces;
using MS_Application.Services.Interfaces;
using MS_Application.Services.Interfaces.External;
using MS_Domain.Entities.DISTS;
using System.Text.Json;
using TagLib;

namespace MS_Application.Services
{
    public class SongsService : ISongsService
    {
        private readonly IDistUnitOfWork _distUnitOfWork;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SongsService(
            IDistUnitOfWork distUnitOfWork,
            ICloudinaryService cloudinaryService,
            IHttpContextAccessor httpContextAccessor)
        {
            _distUnitOfWork = distUnitOfWork;
            _cloudinaryService = cloudinaryService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<BaseTableResponse<SongResponseDto>> GetSongs(BaseSearchDto<SongRequestDto> dto, long userId)
        {
            var result = new BaseTableResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongs>().QueryAll();
            var repoSongAlbum = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongAlbums>().QueryAll();

            var query = repoSong.Where(x => !x.IsDeleted && x.CreatedBy == userId);

            if (!string.IsNullOrEmpty(dto.SearchParams.Keyword))
            {
                query = query.Where(x =>
                    x.Title.Contains(dto.SearchParams.Keyword));
            }

            var totalRecords = query.Count();

            var data = query
                .OrderByDescending(x => x.CreatedAt)
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .Select(x => new SongResponseDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Duration = x.Duration,
                    AlbumId = x.AlbumId,
                    ReleaseDate = x.ReleaseDate,
                    FileUrl = _cloudinaryService.BuildAudioUrl(x.FileUrl),
                    ImgUrl = string.IsNullOrWhiteSpace(x.ImgUrl) ? null : _cloudinaryService.BuildImageUrl(x.ImgUrl),
                    ArtistName = x.Artist.Name,
                    TypeSong = EnumHelper.GetDisplayName((MS_Domain.Enums.Type)x.Type),
                    Views = x.Views,
                    Likes = x.Likes,
                    AlbumIds = repoSongAlbum
                        .Where(sa => sa.SongId == x.Id && !sa.IsDeleted)
                        .Select(sa => sa.AlbumId)
                        .ToList(),
                    IsActived = x.IsActived,
                    IsDeleted = x.IsDeleted,
                    CreatedAt = x.CreatedAt,
                    CreatedBy = x.CreatedBy
                })
                .ToList();

            result.TotalRecords = totalRecords;
            result.TotalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "songs"));
        }

        public async Task<BaseTableResponse<SongResponseDto>> GetPublicSongs(BaseSearchDto<SongRequestDto> dto, long userId)
        {
            var result = new BaseTableResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongs>().QueryAll();
            var repoSongAlbum = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongAlbums>().QueryAll();
            var repoAlbum = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistAlbums>().QueryAll();
            var repoUserLike = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistUserLikes>().QueryAll();

            var query = repoSong
                .Where(x =>
                    !x.IsDeleted
                    && x.Type == 1);

            if (!string.IsNullOrWhiteSpace(dto.SearchParams.Keyword))
            {
                // search_text = tên bài + ca sĩ (không dấu), có trigram index nên LIKE '%..%' vẫn nhanh
                var pattern = TextSearchHelper.ToContainsPattern(dto.SearchParams.Keyword);

                query = query.Where(x =>
                    x.SearchText != null
                    && EF.Functions.Like(x.SearchText, pattern));
            }

            if (dto.SearchParams.AlbumId.HasValue)
            {
                var albumId = dto.SearchParams.AlbumId.Value;

                query = query.Where(x =>
                    x.AlbumId == albumId
                    || repoSongAlbum.Any(sa =>
                        sa.SongId == x.Id
                        && sa.AlbumId == albumId
                        && !sa.IsDeleted));
            }

            if (dto.SearchParams.ArtistId.HasValue)
            {
                // Bài hát không gán ArtistId sẽ tự động bị loại vì so sánh != artistId
                query = query.Where(x =>
                    x.ArtistId == dto.SearchParams.ArtistId.Value);
            }

            if (dto.SearchParams.Type.HasValue)
            {
                query = query.Where(x =>
                    x.Type == dto.SearchParams.Type.Value);
            }

            if (dto.SearchParams.IsActived.HasValue)
            {
                query = query.Where(x =>
                    x.IsActived == dto.SearchParams.IsActived.Value);
            }

            query = dto.SearchParams.SortBy?.ToLower() switch
            {
                "views" => dto.Asc
                    ? query.OrderBy(x => x.Views)
                    : query.OrderByDescending(x => x.Views),

                "likes" => dto.Asc
                    ? query.OrderBy(x => x.Likes)
                    : query.OrderByDescending(x => x.Likes),

                "createdat" => dto.Asc
                    ? query.OrderBy(x => x.CreatedAt)
                    : query.OrderByDescending(x => x.CreatedAt),

                _ => dto.Asc
                    ? query.OrderBy(x => x.Id)
                    : query.OrderByDescending(x => x.Id)
            };

            var totalRecords = query.Count();

            var data = query
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .Select(x => new SongResponseDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Duration = x.Duration,
                    AlbumId = x.AlbumId,

                    AlbumName = repoAlbum
                        .Where(a =>
                            a.Id == x.AlbumId
                            && !a.IsDeleted)
                        .Select(a => a.Title)
                        .FirstOrDefault(),

                    ReleaseDate = x.ReleaseDate,

                    FileUrl = _cloudinaryService
                        .BuildAudioUrl(x.FileUrl),

                    ImgUrl = string.IsNullOrWhiteSpace(x.ImgUrl)
                        ? null
                        : _cloudinaryService.BuildImageUrl(x.ImgUrl),

                    ArtistName = x.Artist.Name,

                    TypeSong = EnumHelper.GetDisplayName(
                        (MS_Domain.Enums.Type)x.Type),

                    Views = x.Views,
                    Likes = x.Likes,

                    IsLiked = repoUserLike.Any(l =>
                        l.UserId == userId
                        && l.SongId == x.Id
                        && !l.IsDeleted),

                    YoutubeVideoId = x.YoutubeVideoId,
                    PlayCount = x.PlayCount,
                    SourceType = x.SourceType,

                    AlbumIds = repoSongAlbum
                        .Where(sa =>
                            sa.SongId == x.Id
                            && !sa.IsDeleted)
                        .Select(sa => sa.AlbumId)
                        .ToList(),

                    IsActived = x.IsActived,
                    IsDeleted = x.IsDeleted,
                    CreatedAt = x.CreatedAt,
                    CreatedBy = x.CreatedBy
                })
                .ToList();

            result.TotalRecords = totalRecords;
            result.TotalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "songs"));
        }

        public async Task<BaseTableResponse<SongResponseDto>> GetRecentSongs(BaseSearchDto<SongRequestDto> dto, long userId)
        {
            var result = new BaseTableResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongs>()
                .QueryAll();

            var repoSongHistory = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongHistories>()
                .QueryAll();

            var repoSongAlbum = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongAlbums>()
                .QueryAll();

            var repoUserLike = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistUserLikes>()
                .QueryAll();

            var recentSongIds = repoSongHistory
                .Where(x =>
                    !x.IsDeleted &&
                    x.UserId == userId)
                .OrderByDescending(x => x.PlayedAt)
                .Select(x => x.SongId)
                .Distinct();

            var query = repoSong
                .Where(x =>
                    !x.IsDeleted &&
                    recentSongIds.Contains(x.Id));

            var totalRecords = query.Count();

            var data = query
                .OrderByDescending(x =>
                    repoSongHistory
                        .Where(h =>
                            h.SongId == x.Id &&
                            h.UserId == userId)
                        .Max(h => h.PlayedAt))
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .Select(x => new SongResponseDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Duration = x.Duration,
                    AlbumId = x.AlbumId,
                    ReleaseDate = x.ReleaseDate,

                    FileUrl = _cloudinaryService
                        .BuildAudioUrl(x.FileUrl),

                    ImgUrl = string.IsNullOrWhiteSpace(x.ImgUrl)
                        ? null
                        : _cloudinaryService.BuildImageUrl(x.ImgUrl),

                    ArtistName = x.Artist.Name,

                    TypeSong = EnumHelper.GetDisplayName(
                        (MS_Domain.Enums.Type)x.Type),

                    Views = x.Views,
                    Likes = x.Likes,

                    IsLiked = repoUserLike.Any(l =>
                        l.UserId == userId
                        && l.SongId == x.Id
                        && !l.IsDeleted),

                    YoutubeVideoId = x.YoutubeVideoId,
                    PlayCount = x.PlayCount,
                    SourceType = x.SourceType,

                    AlbumIds = repoSongAlbum
                        .Where(sa =>
                            sa.SongId == x.Id &&
                            !sa.IsDeleted)
                        .Select(sa => sa.AlbumId)
                        .ToList(),

                    IsActived = x.IsActived,
                    IsDeleted = x.IsDeleted,
                    CreatedAt = x.CreatedAt,
                    CreatedBy = x.CreatedBy
                })
                .ToList();

            result.TotalRecords = totalRecords;
            result.TotalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(
                string.Format(Messages.Action.GetSuccess, "recent songs"));
        }

        public async Task<BaseTableResponse<SongResponseDto>> GetTrendingSongs(BaseSearchDto<SongRequestDto> dto, long userId)
        {
            var result = new BaseTableResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongs>()
                .QueryAll();

            var repoSongAlbum = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongAlbums>()
                .QueryAll();

            var repoUserLike = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistUserLikes>()
                .QueryAll();

            var query = repoSong
                .Where(x =>
                    !x.IsDeleted
                    && x.Type == 1);

            if (!string.IsNullOrEmpty(dto.SearchParams.Keyword))
            {
                query = query.Where(x =>
                    x.Title.Contains(dto.SearchParams.Keyword));
            }

            var totalRecords = query.Count();

            var data = query
                .OrderByDescending(x =>
                    x.Views + (x.Likes * 2))
                .ThenByDescending(x =>
                    x.CreatedAt)
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .Select(x => new SongResponseDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Duration = x.Duration,
                    AlbumId = x.AlbumId,
                    ReleaseDate = x.ReleaseDate,

                    FileUrl = _cloudinaryService
                        .BuildAudioUrl(x.FileUrl),

                    ImgUrl = string.IsNullOrWhiteSpace(x.ImgUrl)
                        ? null
                        : _cloudinaryService.BuildImageUrl(x.ImgUrl),

                    ArtistName = x.Artist.Name,

                    TypeSong = EnumHelper.GetDisplayName(
                        (MS_Domain.Enums.Type)x.Type),

                    Views = x.Views,
                    Likes = x.Likes,

                    IsLiked = repoUserLike.Any(l =>
                        l.UserId == userId
                        && l.SongId == x.Id
                        && !l.IsDeleted),

                    YoutubeVideoId = x.YoutubeVideoId,
                    PlayCount = x.PlayCount,
                    SourceType = x.SourceType,

                    AlbumIds = repoSongAlbum
                        .Where(sa =>
                            sa.SongId == x.Id &&
                            !sa.IsDeleted)
                        .Select(sa => sa.AlbumId)
                        .ToList(),

                    IsActived = x.IsActived,
                    IsDeleted = x.IsDeleted,
                    CreatedAt = x.CreatedAt,
                    CreatedBy = x.CreatedBy
                })
                .ToList();

            result.TotalRecords = totalRecords;
            result.TotalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(
                string.Format(Messages.Action.GetSuccess, "trending songs"));
        }

        public async Task<BaseTableResponse<SongResponseDto>> GetNewestSongs(BaseSearchDto<SongRequestDto> dto, long userId)
        {
            var result = new BaseTableResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongs>()
                .QueryAll();

            var repoSongAlbum = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongAlbums>()
                .QueryAll();

            var repoAlbum = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistAlbums>()
                .QueryAll();

            var repoUserLike = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistUserLikes>()
                .QueryAll();

            var query = repoSong
                .Where(x =>
                    !x.IsDeleted
                    && x.IsActived
                    && x.Type == 1);

            if (!string.IsNullOrWhiteSpace(dto.SearchParams.Keyword))
            {
                // search_text = tên bài + ca sĩ (không dấu), có trigram index nên LIKE '%..%' vẫn nhanh
                var pattern = TextSearchHelper.ToContainsPattern(dto.SearchParams.Keyword);

                query = query.Where(x =>
                    x.SearchText != null
                    && EF.Functions.Like(x.SearchText, pattern));
            }

            var totalRecords = query.Count();

            var data = query
                .OrderByDescending(x => x.CreatedAt)
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .Select(x => new SongResponseDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Duration = x.Duration,
                    AlbumId = x.AlbumId,

                    AlbumName = repoAlbum
                        .Where(a =>
                            a.Id == x.AlbumId
                            && !a.IsDeleted)
                        .Select(a => a.Title)
                        .FirstOrDefault(),

                    ReleaseDate = x.ReleaseDate,

                    FileUrl = _cloudinaryService
                        .BuildAudioUrl(x.FileUrl),

                    ImgUrl = string.IsNullOrWhiteSpace(x.ImgUrl)
                        ? null
                        : _cloudinaryService.BuildImageUrl(x.ImgUrl),

                    ArtistName = x.Artist.Name,

                    TypeSong = EnumHelper.GetDisplayName(
                        (MS_Domain.Enums.Type)x.Type),

                    Views = x.Views,
                    Likes = x.Likes,

                    IsLiked = repoUserLike.Any(l =>
                        l.UserId == userId
                        && l.SongId == x.Id
                        && !l.IsDeleted),

                    YoutubeVideoId = x.YoutubeVideoId,
                    PlayCount = x.PlayCount,
                    SourceType = x.SourceType,

                    AlbumIds = repoSongAlbum
                        .Where(sa =>
                            sa.SongId == x.Id
                            && !sa.IsDeleted)
                        .Select(sa => sa.AlbumId)
                        .ToList(),

                    IsActived = x.IsActived,
                    IsDeleted = x.IsDeleted,
                    CreatedAt = x.CreatedAt,
                    CreatedBy = x.CreatedBy
                })
                .ToList();

            result.TotalRecords = totalRecords;
            result.TotalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(
                string.Format(Messages.Action.GetSuccess, "newest songs"));
        }

        public async Task<BaseTableResponse<SongResponseDto>> GetMostPlayedSongs(long userId, int top = 20)
        {
            var result = new BaseTableResponse<SongResponseDto>();

            var data = GetMostPlayedSongsData(userId, top);

            result.TotalRecords = data.Count;
            result.TotalPages = 1;
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(
                string.Format(Messages.Action.GetSuccess, "most played songs"));
        }

        // Gói danh sách "bài hay nghe" thành 1 playlist ảo (kiểu Mix của Youtube/Spotify):
        // có title + cover ghép từ ảnh các bài hát, để FE (web lẫn mobile) chỉ cần render
        // 1 card duy nhất thay vì tự lắp ráp từ danh sách bài hát rời rạc.
        public async Task<BaseResponse<MixPlaylistResponseDto>> GetMostPlayedMix(long userId, int top = 20)
        {
            var result = new BaseResponse<MixPlaylistResponseDto>();

            var songs = GetMostPlayedSongsData(userId, top);

            var coverImages = songs
                .Where(s => !string.IsNullOrWhiteSpace(s.ImgUrl))
                .Select(s => s.ImgUrl!)
                .Distinct()
                .Take(4)
                .ToList();

            result.Data = new MixPlaylistResponseDto
            {
                Key = "most-played",
                Title = "Danh sách kết hợp - Bài hay của tôi",
                Description = "Tổng hợp những bài hát bạn nghe nhiều nhất",
                CoverImages = coverImages,
                TotalSongs = songs.Count,
                Songs = songs
            };

            result.TotalRecords = songs.Count;
            result.TotalPages = 1;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(
                string.Format(Messages.Action.GetSuccess, "most played mix"));
        }

        private List<SongResponseDto> GetMostPlayedSongsData(long userId, int top)
        {
            top = Math.Min(top, 20);

            var repoSong = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongs>()
                .QueryAll();

            var repoSongHistory = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongHistories>()
                .QueryAll();

            var repoSongAlbum = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongAlbums>()
                .QueryAll();

            var repoUserLike = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistUserLikes>()
                .QueryAll();

            // Đếm số lần user này nghe từng bài, lấy top N bài nghe nhiều nhất
            var mostPlayedSongIds = repoSongHistory
                .Where(x => !x.IsDeleted && x.UserId == userId)
                .GroupBy(x => x.SongId)
                .Select(g => new
                {
                    SongId = g.Key,
                    PlayCount = g.Count()
                })
                .OrderByDescending(x => x.PlayCount)
                .Take(top)
                .ToList();

            var songIds = mostPlayedSongIds
                .Select(x => x.SongId)
                .ToList();

            var songs = repoSong
                .Where(x => !x.IsDeleted && songIds.Contains(x.Id))
                .ToList();

            // Lấy 1 lần cho cả top N bài thay vì query lại DB cho từng bài trong vòng Join bên dưới
            // (trước đây gây N+1 query, mỗi request most-played mất tới hàng chục round-trip DB)
            var likedSongIds = repoUserLike
                .Where(l =>
                    l.UserId == userId
                    && songIds.Contains(l.SongId)
                    && !l.IsDeleted)
                .Select(l => l.SongId)
                .ToHashSet();

            var albumIdsBySong = repoSongAlbum
                .Where(sa =>
                    songIds.Contains(sa.SongId)
                    && !sa.IsDeleted)
                .Select(sa => new { sa.SongId, sa.AlbumId })
                .ToList()
                .GroupBy(sa => sa.SongId)
                .ToDictionary(g => g.Key, g => g.Select(sa => sa.AlbumId).ToList());

            // Giữ đúng thứ tự "nghe nhiều nhất -> ít nhất" vì query DB không đảm bảo giữ order
            return mostPlayedSongIds
                .Join(songs,
                    m => m.SongId,
                    s => s.Id,
                    (m, s) => new SongResponseDto
                    {
                        Id = s.Id,
                        Title = s.Title,
                        Duration = s.Duration,
                        AlbumId = s.AlbumId,
                        ReleaseDate = s.ReleaseDate,

                        FileUrl = _cloudinaryService.BuildAudioUrl(s.FileUrl),

                        ImgUrl = string.IsNullOrWhiteSpace(s.ImgUrl)
                            ? null
                            : s.ImgUrl.StartsWith("http")
                                ? s.ImgUrl
                                : _cloudinaryService.BuildImageUrl(s.ImgUrl),

                        ArtistName = s.Artist?.Name ?? "",

                        TypeSong = EnumHelper.GetDisplayName(
                            (MS_Domain.Enums.Type)s.Type),

                        Views = s.Views,
                        Likes = s.Likes,

                        IsLiked = likedSongIds.Contains(s.Id),

                        YoutubeVideoId = s.YoutubeVideoId,
                        PlayCount = m.PlayCount, // số lần user này nghe bài này, không phải PlayCount tổng
                        SourceType = s.SourceType,

                        AlbumIds = albumIdsBySong.TryGetValue(s.Id, out var albumIds)
                            ? albumIds
                            : new List<long>(),

                        IsActived = s.IsActived,
                        IsDeleted = s.IsDeleted,
                        CreatedAt = s.CreatedAt,
                        CreatedBy = s.CreatedBy
                    })
                .ToList();
        }

        public async Task<BaseResponse<SongResponseDto>> GetSongDetail(long id, long userId)
        {
            var result = new BaseResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongs>().QueryAll();
            var repoSongLyric = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongLyrics>().QueryAll();
            var repoUserLike = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistUserLikes>().QueryAll();
            var repoSongAlbum = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongAlbums>().QueryAll();

            var song = repoSong
                .FirstOrDefault(x =>
                    x.Id == id &&
                    !x.IsDeleted);

            if (song == null)
            {
                return result.Fail(string.Format(Messages.Validation.NotFound, "song"));
            }

            var lyrics = repoSongLyric
                .FirstOrDefault(x =>
                    x.SongId == id &&
                    !x.IsDeleted);

            var isLiked = repoUserLike
                .Any(x =>
                    x.UserId == userId &&
                    x.SongId == id &&
                    !x.IsDeleted);

            var deserializeOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            result.Data = new SongResponseDto
            {
                Id = song.Id,
                Title = song.Title,
                Duration = song.Duration,

                FileUrl = song.SourceType == 3
                    ? song.YoutubeVideoId
                    : _cloudinaryService.BuildAudioUrl(song.FileUrl),

                ImgUrl = string.IsNullOrWhiteSpace(song.ImgUrl)
                    ? null
                    : song.ImgUrl.StartsWith("http")
                        ? song.ImgUrl
                        : _cloudinaryService.BuildImageUrl(song.ImgUrl),

                ReleaseDate = song.ReleaseDate,
                AlbumId = song.AlbumId,
                ArtistName = song.Artist != null ? song.Artist.Name : "",
                TypeSong = EnumHelper.GetDisplayName((MS_Domain.Enums.Type)song.Type),

                IsLiked = isLiked,

                Views = song.Views,
                Likes = song.Likes,
                YoutubeVideoId = song.YoutubeVideoId,
                PlayCount = song.PlayCount,
                SourceType = song.SourceType,

                AlbumIds = repoSongAlbum
                    .Where(sa =>
                        sa.SongId == song.Id &&
                        !sa.IsDeleted)
                    .Select(sa => sa.AlbumId)
                    .ToList(),

                Lyrics = lyrics?.Content,

                SyncedLyrics = string.IsNullOrWhiteSpace(lyrics?.SyncedJson)
                    ? null
                    : JsonSerializer.Deserialize<List<LyricsLineDto>>(
                        lyrics.SyncedJson,
                        deserializeOptions),

                IsActived = song.IsActived,
                IsDeleted = song.IsDeleted,
                CreatedAt = song.CreatedAt,
                CreatedBy = song.CreatedBy
            };

            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "song"));
        }

        public async Task<BaseResponse<SongResponseDto>> IncrementView(long id, long userId)
        {
            var result = new BaseResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork.GetRepositoryAsync<DistSongs>();
            var repoSongView = _distUnitOfWork.GetRepositoryAsync<DistSongViews>();

            var song = await repoSong.FindByIdAsync(id);

            if (song == null || song.IsDeleted)
            {
                return result.Fail(string.Format(Messages.Validation.NotFound, "song"));
            }

            var ipAddress = _httpContextAccessor.HttpContext?.Request?.Headers["X-Forwarded-For"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                ipAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
            }

            if (!string.IsNullOrWhiteSpace(ipAddress) && ipAddress.Contains(','))
            {
                ipAddress = ipAddress.Split(',').First().Trim();
            }

            var limitTime = DateTimeHelper.VnNow.AddSeconds(-30);

            var recentView = repoSongView.QueryAll().Any(x =>
                x.SongId == id &&
                (
                    (userId > 0 && x.UserId == userId)
                    || x.IpAddress == ipAddress
                ) &&
                x.ViewedAt >= limitTime &&
                !x.IsDeleted);

            if (!recentView)
            {
                song.Views = (song.Views ?? 0) + 1;

                await repoSong.UpdateAsync(song);

                await repoSongView.AddAsync(new DistSongViews
                {
                    SongId = id,
                    UserId = userId > 0 ? userId : null,
                    IpAddress = ipAddress,
                    ViewedAt = DateTimeHelper.VnNow,
                    IsActived = true,
                    IsDeleted = false,
                    CreatedBy = userId > 0 ? userId : null
                });

                await _distUnitOfWork.SaveChangesAsync();
            }

            result.Data = new SongResponseDto
            {
                Id = song.Id,
                Title = song.Title,
                Duration = song.Duration,
                FileUrl = song.FileUrl,
                ImgUrl = song.ImgUrl,
                ReleaseDate = song.ReleaseDate,
                AlbumId = song.AlbumId,
                ArtistName = song.Artist?.Name ?? "",
                TypeSong = EnumHelper.GetDisplayName((MS_Domain.Enums.Type)song.Type),
                Views = song.Views,
                Likes = song.Likes,
                YoutubeVideoId = song.YoutubeVideoId,
                PlayCount = song.PlayCount,
                SourceType = song.SourceType,
                IsActived = song.IsActived,
                IsDeleted = song.IsDeleted,
                CreatedAt = song.CreatedAt,
                CreatedBy = song.CreatedBy
            };

            return result.Success(string.Format(Messages.Action.UpdateSuccess, "view"));
        }

        public async Task<BaseResponse<SongResponseDto>> AddSongHistory(AddSongHistoryDto dto, long userId)
        {
            var result = new BaseResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork
                .GetRepositoryAsync<DistSongs>();

            var repoArtist = _distUnitOfWork
                .GetRepositoryAsync<DistArtists>();

            var repoHistory = _distUnitOfWork
                .GetRepositoryAsync<DistSongHistories>();

            var artist = await repoArtist
                .QueryCondition(x =>
                    x.Name.ToLower() == dto.Channel.Trim().ToLower())
                .FirstOrDefaultAsync();

            if (artist == null)
            {
                artist = new DistArtists
                {
                    Name = dto.Channel.Trim(),
                    StageName = dto.Channel.Trim(),
                    Url = dto.ChannelThumbnail,
                    CreatedBy = userId
                };

                await repoArtist.AddAsync(artist);
                await _distUnitOfWork.SaveChangesAsync();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(dto.ChannelThumbnail)) artist.Url = dto.ChannelThumbnail;
                artist.UpdatedBy = userId;
                artist.UpdatedAt = DateTimeHelper.VnNow;

                await repoArtist.UpdateAsync(artist);
                await _distUnitOfWork.SaveChangesAsync();
            }

            var song = repoSong
                .QueryAll()
                .FirstOrDefault(x =>
                    x.YoutubeVideoId == dto.VideoId
                    && !x.IsDeleted);

            if (song == null)
            {
                song = new DistSongs
                {
                    Title = dto.Title,
                    YoutubeVideoId = dto.VideoId,
                    Duration = dto.Duration,

                    ImgUrl = dto.ThumbnailHigh,

                    ArtistId = artist.Id,

                    Type = 1,
                    SourceType = 3,

                    PlayCount = 1,

                    Views = 0,
                    Likes = 0,

                    CreatedBy = userId
                };

                await repoSong.AddAsync(song);
                await _distUnitOfWork.SaveChangesAsync();
            }
            else
            {
                song.Duration = dto.Duration;

                if (!string.IsNullOrWhiteSpace(dto.ThumbnailHigh))
                {
                    song.ImgUrl = dto.ThumbnailHigh;
                }

                song.PlayCount = (song.PlayCount ?? 0) + 1;

                await repoSong.UpdateAsync(song);
                await _distUnitOfWork.SaveChangesAsync();
            }

            var history = new DistSongHistories
            {
                SongId = song.Id,
                UserId = userId,
                PlayedAt = DateTimeHelper.VnNow,
                CreatedBy = userId
            };

            await repoHistory.AddAsync(history);

            await _distUnitOfWork.SaveChangesAsync();

            var repoSongAlbum = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongAlbums>()
                .QueryAll();

            var repoUserLike = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistUserLikes>()
                .QueryAll();

            result.Data = new SongResponseDto
            {
                Id = song.Id,
                Title = song.Title,
                Duration = song.Duration,
                FileUrl = song.FileUrl,
                ImgUrl = song.ImgUrl,
                ReleaseDate = song.ReleaseDate,
                AlbumId = song.AlbumId,
                ArtistName = artist.Name,
                Views = song.Views,
                Likes = song.Likes,
                IsLiked = repoUserLike.Any(l =>
                    l.UserId == userId
                    && l.SongId == song.Id
                    && !l.IsDeleted),
                YoutubeVideoId = song.YoutubeVideoId,
                PlayCount = song.PlayCount,
                SourceType = song.SourceType,
                AlbumIds = repoSongAlbum
                    .Where(sa => sa.SongId == song.Id && !sa.IsDeleted)
                    .Select(sa => sa.AlbumId)
                    .ToList()
            };

            return result.Success(string.Format(Messages.Action.CreateSuccess, "song history"));
        }

        public async Task<BaseResponse<SongResponseDto>> ToggleLike(long id, long userId)
        {
            var result = new BaseResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork.GetRepositoryAsync<DistSongs>();
            var repoLike = _distUnitOfWork.GetRepositoryAsync<DistUserLikes>();

            var song = await repoSong.FindByIdAsync(id);

            if (song == null || song.IsDeleted)
            {
                return result.Fail(string.Format(Messages.Validation.NotFound, "song"));
            }

            var existed = repoLike
                .QueryCondition(x =>
                    x.UserId == userId &&
                    x.SongId == id &&
                    !x.IsDeleted)
                .FirstOrDefault();

            if (existed != null)
            {
                await repoLike.DeleteAsync(existed);

                if ((song.Likes ?? 0) > 0)
                {
                    song.Likes = (song.Likes ?? 0) - 1;
                }
            }
            else
            {
                await repoLike.AddAsync(new DistUserLikes
                {
                    UserId = userId,
                    SongId = id,
                    CreatedBy = userId
                });

                song.Likes = (song.Likes ?? 0) + 1;
            }

            await repoSong.UpdateAsync(song);
            await _distUnitOfWork.SaveChangesAsync();

            result.Data = new SongResponseDto
            {
                Id = song.Id,
                Title = song.Title,
                Duration = song.Duration,
                FileUrl = song.FileUrl,
                ImgUrl = song.ImgUrl,
                ReleaseDate = song.ReleaseDate,
                AlbumId = song.AlbumId,
                ArtistName = song.Artist?.Name ?? "",
                TypeSong = EnumHelper.GetDisplayName((MS_Domain.Enums.Type)song.Type),
                IsLiked = existed == null,
                Views = song.Views,
                Likes = song.Likes,
                YoutubeVideoId = song.YoutubeVideoId,
                PlayCount = song.PlayCount,
                SourceType = song.SourceType,
                IsActived = song.IsActived,
                IsDeleted = song.IsDeleted,
                CreatedAt = song.CreatedAt,
                CreatedBy = song.CreatedBy
            };

            return result.Success(string.Format(Messages.Action.UpdateSuccess, "like"));
        }

        public async Task<BaseResponse<SongResponseDto>> CreateSong(SongCreateDto dto, long userId)
        {
            var result = new BaseResponse<SongResponseDto>();

            var repoSongWrite = _distUnitOfWork.GetRepositoryAsync<DistSongs>();
            var repoLyricsWrite = _distUnitOfWork.GetRepositoryAsync<DistSongLyrics>();
            var repoArtistWrite = _distUnitOfWork.GetRepositoryAsync<DistArtists>();

            var extension = Path.GetExtension(dto.FileUrl.FileName);

            var tempFile = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid()}{extension}");

            await using (var stream = dto.FileUrl.OpenReadStream())
            await using (var fs = System.IO.File.Create(tempFile))
            {
                await stream.CopyToAsync(fs);
            }

            int duration = 0;

            try
            {
                var tagFile = TagLib.File.Create(tempFile);

                duration = (int)tagFile
                    .Properties
                    .Duration
                    .TotalSeconds;
            }
            catch{}

            System.IO.File.Delete(tempFile);

            var uploadAudioTask = _cloudinaryService
                .UploadAudioAsync(dto.FileUrl);

            var uploadImageTask = _cloudinaryService
                .UploadMusicImageAsync(dto.ImgUrl);

            await Task.WhenAll(uploadAudioTask, uploadImageTask);

            var uploadAudio = await uploadAudioTask;
            var uploadImage = await uploadImageTask;

            if (string.IsNullOrEmpty(uploadAudio.Data))
            {
                return result.Fail(
                    string.Format(Messages.Action.UploadFail, "audio"));
            }

            if (string.IsNullOrEmpty(uploadImage.Data))
            {
                return result.Fail(
                    string.Format(Messages.Action.UploadFail, "image"));
            }

            long? artistId = null;

            if (!string.IsNullOrWhiteSpace(dto.ArtistName))
            {
                var normalizedName = dto.ArtistName.Trim();

                var artist = await repoArtistWrite
                    .QueryCondition(x =>
                        x.Name.ToLower() == normalizedName.ToLower())
                    .FirstOrDefaultAsync();

                if (artist == null)
                {
                    artist = new DistArtists
                    {
                        Name = normalizedName,
                        StageName = normalizedName,
                        CreatedBy = userId
                    };

                    await repoArtistWrite.AddAsync(artist);
                    await _distUnitOfWork.SaveChangesAsync();
                }

                artistId = artist.Id;
            }

            var entity = new DistSongs
            {
                Title = dto.Title,
                Duration = duration,
                ReleaseDate = dto.ReleaseDate,
                FileUrl = uploadAudio.Data,
                ImgUrl = uploadImage.Data,
                AlbumId = dto.AlbumId,
                Type = dto.Type,
                ArtistId = artistId,
                CreatedBy = userId,
            };

            await repoSongWrite.AddAsync(entity);
            await _distUnitOfWork.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(dto.Lyrics))
            {
                var serializeOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var deserializeOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var lyricsDto = JsonSerializer.Deserialize<SongLyricsCreateDto>(dto.Lyrics, deserializeOptions);

                var lyrics = new DistSongLyrics
                {
                    SongId = entity.Id,
                    Content = lyricsDto?.Lyrics ?? dto.Lyrics,
                    SyncedJson = lyricsDto?.SyncedLyrics != null
                        ? JsonSerializer.Serialize(lyricsDto.SyncedLyrics, serializeOptions)
                        : null,
                    CreatedBy = userId,
                };

                await repoLyricsWrite.AddAsync(lyrics);
                await _distUnitOfWork.SaveChangesAsync();
            }

            result.Data = new SongResponseDto
            {
                Title = entity.Title,
                Duration = entity.Duration,
                FileUrl = _cloudinaryService
                    .BuildAudioUrl(entity.FileUrl),

                ImgUrl = _cloudinaryService
                    .BuildImageUrl(entity.ImgUrl)
            };

            return result.Success(
                string.Format(Messages.Action.CreateSuccess, "song"));
        }

        public async Task<BaseResponse<SongResponseDto>> AddSongToAlbum(long songId, long albumId, long userId)
        {
            var result = new BaseResponse<SongResponseDto>();

            var repoSong = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongs>().QueryAll();
            var repoAlbum = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistAlbums>().QueryAll();
            var repoSongAlbum = _distUnitOfWork.GetRepositoryAsync<DistSongAlbums>();

            var song = repoSong.FirstOrDefault(x => x.Id == songId && !x.IsDeleted);
            if (song == null)
                return result.Fail(string.Format(Messages.Validation.NotFound, "song"));

            var album = repoAlbum.FirstOrDefault(x => x.Id == albumId && !x.IsDeleted);
            if (album == null)
                return result.Fail(string.Format(Messages.Validation.NotFound, "album"));

            if (album.CreatedBy != userId)
                return result.Fail("Bạn không có quyền chỉnh sửa album này");

            var existed = repoSongAlbum
                .QueryCondition(x => x.SongId == songId && x.AlbumId == albumId && !x.IsDeleted)
                .FirstOrDefault();

            if (existed != null)
                return result.Fail("Bài hát đã có trong album này");

            await repoSongAlbum.AddAsync(new DistSongAlbums
            {
                SongId = songId,
                AlbumId = albumId,
                CreatedBy = userId
            });

            await _distUnitOfWork.SaveChangesAsync();

            result.Code = ResponseStatusCode.Status200;
            return result.Success(string.Format(Messages.Action.UpdateSuccess, "album"));
        }

        public async Task<BaseResponse<bool>> RemoveSongFromAlbum(long songId, long albumId, long userId)
        {
            var result = new BaseResponse<bool>();

            var repoAlbum = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistAlbums>().QueryAll();
            var repoSongAlbum = _distUnitOfWork.GetRepositoryAsync<DistSongAlbums>();

            var album = repoAlbum.FirstOrDefault(x => x.Id == albumId && !x.IsDeleted);
            if (album == null)
                return result.Fail(false, string.Format(Messages.Validation.NotFound, "album"));

            if (album.CreatedBy != userId)
                return result.Fail(false, "Bạn không có quyền chỉnh sửa album này");

            var existed = repoSongAlbum
                .QueryCondition(x => x.SongId == songId && x.AlbumId == albumId && !x.IsDeleted)
                .FirstOrDefault();

            if (existed == null)
                return result.Fail(false, "Bài hát không có trong album này");

            existed.IsDeleted = true;
            existed.UpdatedAt = DateTimeHelper.VnNow;
            existed.UpdatedBy = userId;

            await repoSongAlbum.UpdateAsync(existed);
            await _distUnitOfWork.SaveChangesAsync();

            result.Data = true;
            result.Code = ResponseStatusCode.Status200;
            return result.Success(true, string.Format(Messages.Action.UpdateSuccess, "album"));
        }

        /// <summary>Admin-only: all songs a specific user has liked.</summary>
        public async Task<BaseTableResponse<SongResponseDto>> GetLikedSongsByUserForAdmin(long userId, BaseSearchDto<SongRequestDto> dto)
        {
            var result = new BaseTableResponse<SongResponseDto>();

            dto.Page = dto.Page <= 0 ? 1 : dto.Page;
            dto.PageSize = dto.PageSize <= 0 ? GlobalConstants.DefaultPageSize : dto.PageSize;

            var repoLike = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistUserLikes>().QueryAll();
            var repoSong = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongs>().QueryAll();

            var likedSongIds = repoLike
                .Where(x => !x.IsDeleted && x.UserId == userId)
                .Select(x => x.SongId)
                .ToList();

            var query = repoSong.Where(x => !x.IsDeleted && likedSongIds.Contains(x.Id));

            var totalRecords = query.Count();

            var data = query
                .OrderByDescending(x => x.CreatedAt)
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .Select(x => new SongResponseDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Duration = x.Duration,
                    ImgUrl = string.IsNullOrWhiteSpace(x.ImgUrl) ? null : _cloudinaryService.BuildImageUrl(x.ImgUrl),
                    ArtistName = x.Artist != null ? x.Artist.Name : "",
                    TypeSong = EnumHelper.GetDisplayName((MS_Domain.Enums.Type)x.Type),
                    Views = x.Views,
                    Likes = x.Likes,
                    IsLiked = true,
                    YoutubeVideoId = x.YoutubeVideoId,
                    PlayCount = x.PlayCount,
                    SourceType = x.SourceType,
                    CreatedAt = x.CreatedAt
                })
                .ToList();

            result.TotalRecords = totalRecords;
            result.TotalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "liked songs"));
        }
    }
}