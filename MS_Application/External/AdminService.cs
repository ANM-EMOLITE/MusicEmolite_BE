using AMFC_Domain.Entities.Crms;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.ActivityLog;
using MS_Application.DataTransferObjects.Albums;
using MS_Application.DataTransferObjects.Artist;
using MS_Application.DataTransferObjects.Bank;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Dashboard;
using MS_Application.DataTransferObjects.Lyrics;
using MS_Application.DataTransferObjects.Songs;
using MS_Application.DataTransferObjects.User;
using MS_Application.DataTransferObjects.User.BankUser;
using MS_Application.Helpers;
using MS_Application.Repositories.Interfaces;
using MS_Application.Services.Interfaces;
using MS_Application.Services.Interfaces.External;
using MS_Domain.Common;
using MS_Domain.Entities.CRMS;
using MS_Domain.Entities.DISTS;
using MS_Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace MS_Application.External
{
    public class AdminService : IAdminService
    {
        private readonly IDistUnitOfWork _distUnitOfWork;
        private readonly ICrmUnitOfWork _crmUnitOfWork;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly ISongSyncService _songSyncService;
        private readonly ILyricsService _lyricsService;

        public AdminService(
            IDistUnitOfWork distUnitOfWork,
            ICrmUnitOfWork crmUnitOfWork,
            ICloudinaryService cloudinaryService,
            ISongSyncService songSyncService,
            ILyricsService lyricsService)
        {
            _distUnitOfWork = distUnitOfWork;
            _crmUnitOfWork = crmUnitOfWork;
            _cloudinaryService = cloudinaryService;
            _songSyncService = songSyncService;
            _lyricsService = lyricsService;
        }

        public async Task<BaseResponse<DashboardSummaryResponseDto>> GetDashboardSummary()
        {
            var result = new BaseResponse<DashboardSummaryResponseDto>();
            var fromDate = DateTimeHelper.VnNow.Date.AddDays(-30);

            var repoSong = _distUnitOfWork
                .GetRepositoryReadOnlyAsync<DistSongs>()
                .QueryAll();

            var repoUser = _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmUser>()
                .QueryAll();

            var data = new DashboardSummaryResponseDto
            {
                TotalViews = await repoSong
        .Where(x => !x.IsDeleted)
        .SumAsync(x => x.Views) ?? 0,

                Last30DaysViews = await repoSong
        .Where(x =>
            !x.IsDeleted
            && x.CreatedAt >= fromDate)
        .SumAsync(x => x.Views) ?? 0,

                TotalLikes = await repoSong
        .Where(x => !x.IsDeleted)
        .SumAsync(x => x.Likes) ?? 0,

                Last30DaysLikes = await repoSong
        .Where(x =>
            !x.IsDeleted
            && x.CreatedAt >= fromDate)
        .SumAsync(x => x.Likes) ?? 0,

                TotalUsers = await repoUser
        .Where(x => !x.IsDeleted)
        .CountAsync(),

                Last30DaysUsers = await repoUser
        .Where(x =>
            !x.IsDeleted
            && x.CreatedAt >= fromDate)
        .CountAsync()
            };

            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "dashboard"));
        }

        public async Task<BaseResponse<List<DashboardTrendResponseDto>>> GetDashboardTrend()
        {
            var result = new BaseResponse<List<DashboardTrendResponseDto>>();

            var fromDate = DateTimeHelper.VnNow.Date.AddDays(-29);
            var toDate = DateTimeHelper.VnNow.Date;

            var repoSongView = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongViews>().QueryAll();
            var repoUserLike = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistUserLikes>().QueryAll();
            var repoUser = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUser>().QueryAll();

            var viewStats = await repoSongView
                .Where(x =>
                    !x.IsDeleted
                    && x.CreatedAt.HasValue
                    && x.CreatedAt.Value.Date >= fromDate
                    && x.CreatedAt.Value.Date <= toDate)
                .GroupBy(x => x.CreatedAt!.Value.Date)
                .Select(x => new
                {
                    Date = x.Key,
                    Views = x.Count()
                })
                .ToListAsync();

            var likeStats = await repoUserLike
                .Where(x =>
                    !x.IsDeleted
                    && x.CreatedAt.HasValue
                    && x.CreatedAt.Value.Date >= fromDate
                    && x.CreatedAt.Value.Date <= toDate)
                .GroupBy(x => x.CreatedAt!.Value.Date)
                .Select(x => new
                {
                    Date = x.Key,
                    Likes = x.Count()
                })
                .ToListAsync();

            var userStats = await repoUser
                .Where(x =>
                    !x.IsDeleted
                    && x.CreatedAt.HasValue
                    && x.CreatedAt.Value.Date >= fromDate
                    && x.CreatedAt.Value.Date <= toDate)
                .GroupBy(x => x.CreatedAt!.Value.Date)
                .Select(x => new
                {
                    Date = x.Key,
                    Users = x.Count()
                })
                .ToListAsync();

            var data = Enumerable.Range(0, 30)
                .Select(i =>
                {
                    var date = fromDate.AddDays(i);

                    var view = viewStats.FirstOrDefault(x => x.Date == date);
                    var like = likeStats.FirstOrDefault(x => x.Date == date);
                    var user = userStats.FirstOrDefault(x => x.Date == date);

                    return new DashboardTrendResponseDto
                    {
                        Date = date,
                        Views = view?.Views ?? 0,
                        Likes = like?.Likes ?? 0,
                        Users = user?.Users ?? 0
                    };
                })
                .ToList();

            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "dashboard trend"));
        }

        public async Task<BaseTableResponse<ActivityLogResponseDto>> SearchActivityLogs(BaseSearchDto<ActivityLogSearchRequest> dto)
        {
            var result = new BaseTableResponse<ActivityLogResponseDto>();

            var repoHistory = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongHistories>().QueryAll();
            var repoLike = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistUserLikes>().QueryAll();
            var repoSong = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistSongs>().QueryAll();

            var songs = repoSong.ToList();

            var plays = repoHistory.ToList()
                .Select(x => new ActivityLogResponseDto
                {
                    UserId = x.UserId,
                    ActionType = "PLAY",
                    SongId = x.SongId,
                    SongTitle = songs.FirstOrDefault(s => s.Id == x.SongId)?.Title ?? "",
                    CreatedAt = x.PlayedAt
                });

            var likes = repoLike.Where(x => !x.IsDeleted).ToList()
                .Select(x => new ActivityLogResponseDto
                {
                    UserId = x.UserId,
                    ActionType = "LIKE",
                    SongId = x.SongId,
                    SongTitle = songs.FirstOrDefault(s => s.Id == x.SongId)?.Title ?? "",
                    CreatedAt = x.CreatedAt ?? DateTime.MinValue
                });

            var merged = plays.Concat(likes).ToList();

            var userNames = ResolveUserNames(merged.Select(x => x.UserId));

            foreach (var item in merged)
            {
                item.UserName = userNames.TryGetValue(item.UserId, out var name) ? name : "Không rõ";
            }

            IEnumerable<ActivityLogResponseDto> filtered = merged;

            var keyword = dto.SearchParams?.Keyword?.Trim();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var lowerKeyword = keyword.ToLower();
                filtered = filtered.Where(x => x.SongTitle.ToLower().Contains(lowerKeyword) || x.UserName.ToLower().Contains(lowerKeyword));
            }

            if (!string.IsNullOrWhiteSpace(dto.SearchParams?.ActionType))
            {
                filtered = filtered.Where(x => x.ActionType == dto.SearchParams!.ActionType);
            }

            if (dto.SearchParams?.FromDate != null)
            {
                var fromDate = dto.SearchParams.FromDate.Value.Date;
                filtered = filtered.Where(x => x.CreatedAt >= fromDate);
            }

            if (dto.SearchParams?.ToDate != null)
            {
                var toDate = dto.SearchParams.ToDate.Value.Date.AddDays(1);
                filtered = filtered.Where(x => x.CreatedAt < toDate);
            }

            filtered = dto.Asc ? filtered.OrderBy(x => x.CreatedAt) : filtered.OrderByDescending(x => x.CreatedAt);

            var materialized = filtered.ToList();
            var totalRecords = materialized.Count;

            var pageItems = materialized.Skip((dto.Page - 1) * dto.PageSize).Take(dto.PageSize).ToList();
            result.TotalRecords = totalRecords;
            result.TotalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);

            return result.Success(pageItems, "Get activity logs successfully");
        }

        public async Task<BaseTableResponse<SongResponseDto>> SearchSongs(BaseSearchDto<SongRequestDto> dto, long userId)
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

        public async Task<BaseTableResponse<SongResponseDto>> GetLikedSongsByUser(long userId, BaseSearchDto<SongRequestDto> dto)
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

        public async Task<BaseTableResponse<AlbumResponseDto>> SearchAlbums(BaseSearchDto<AlbumRequestDto> dto)
        {
            var result = new BaseTableResponse<AlbumResponseDto>();

            dto.SearchParams ??= new AlbumRequestDto();
            dto.Page = dto.Page <= 0 ? 1 : dto.Page;
            dto.PageSize = dto.PageSize <= 0 ? GlobalConstants.DefaultPageSize : dto.PageSize;

            var repoAlbum = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistAlbums>().QueryAll();

            var query = repoAlbum.Where(x => !x.IsDeleted && x.AlbumType == 1);

            if (!string.IsNullOrWhiteSpace(dto.SearchParams.Keyword))
            {
                var keyword = dto.SearchParams.Keyword.Trim().ToLower();
                query = query.Where(x =>x.Title.ToLower().Contains(keyword));
            }

            if (dto.SearchParams.IsActived.HasValue)
            {
                query = query.Where(x =>x.IsActived == dto.SearchParams.IsActived.Value);
            }

            query = dto.SearchParams.SortBy?.ToLower() switch
            {
                "releasedate" => dto.Asc
                    ? query.OrderBy(x => x.ReleaseDate)
                    : query.OrderByDescending(x => x.ReleaseDate),

                "createdat" => dto.Asc
                    ? query.OrderBy(x => x.CreatedAt)
                    : query.OrderByDescending(x => x.CreatedAt),

                _ => dto.Asc
                    ? query.OrderBy(x => x.Id)
                    : query.OrderByDescending(x => x.Id)
            };

            var totalRecords = query.Count();

            var data = query.Skip(dto.Start).Take(dto.PageSize)
                .Select(x => new AlbumResponseDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    ReleaseDate = x.ReleaseDate,
                    ArtistId = x.ArtistId,
                    AlbumTypeName = EnumHelper.GetDisplayName((MS_Domain.Enums.Type)x.AlbumType),
                    Uri = string.IsNullOrEmpty(x.Uri) ? null : _cloudinaryService.BuildImageUrl(x.Uri),
                    IsActived = x.IsActived,
                    IsDeleted = x.IsDeleted,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt,
                    CreatedBy = x.CreatedBy,
                    UpdatedBy = x.UpdatedBy
                })
                .ToList();

            var userNames = ResolveUserNames(data.Select(x => x.CreatedBy));

            foreach (var item in data)
            {
                if (item.CreatedBy.HasValue && userNames.TryGetValue(item.CreatedBy.Value, out var name))
                    item.CreatedByName = name;
            }

            result.TotalRecords = totalRecords;
            result.TotalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "albums"));
        }

        public async Task<BaseTableResponse<AlbumResponseDto>> GetAlbumsByUser(long userId, BaseSearchDto<AlbumRequestDto> dto)
        {
            var result = new BaseTableResponse<AlbumResponseDto>();

            dto.Page = dto.Page <= 0 ? 1 : dto.Page;
            dto.PageSize = dto.PageSize <= 0 ? GlobalConstants.DefaultPageSize : dto.PageSize;

            var repoAlbum = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistAlbums>().QueryAll();

            var query = repoAlbum.Where(x => !x.IsDeleted && x.CreatedBy == userId);

            var totalRecords = query.Count();

            var data = query
                .OrderByDescending(x => x.CreatedAt)
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .Select(x => new AlbumResponseDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    ReleaseDate = x.ReleaseDate,
                    ArtistId = x.ArtistId,
                    AlbumTypeName = EnumHelper.GetDisplayName((MS_Domain.Enums.Type)x.AlbumType),
                    Uri = string.IsNullOrEmpty(x.Uri) ? null : _cloudinaryService.BuildImageUrl(x.Uri),
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

            return result.Success(string.Format(Messages.Action.GetSuccess, "albums"));
        }

        public async Task<BaseTableResponse<ArtistResponseDto>> SearchArtists(BaseSearchDto<ArtistRequestDto> dto)
        {
            var result = new BaseTableResponse<ArtistResponseDto>();
            var repo = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistArtists>().QueryAll();

            var query = repo.Where(x => !x.IsDeleted);

            if (!string.IsNullOrEmpty(dto.SearchParams.Keyword))
            {
                query = query.Where(x =>
                    x.Name.Contains(dto.SearchParams.Keyword) ||
                    x.StageName.Contains(dto.SearchParams.Keyword));
            }

            if (!string.IsNullOrWhiteSpace(dto.SearchParams.Country))
            {
                query = query.Where(x =>
                    x.Country.Contains(dto.SearchParams.Country));
            }

            if (dto.SearchParams.IsActived.HasValue)
            {
                query = query.Where(x =>
                    x.IsActived == dto.SearchParams.IsActived.Value);
            }

            query = dto.SearchParams.SortBy?.ToLower() == "createdat" && dto.Asc
                ? query.OrderBy(x => x.CreatedAt)
                : query.OrderByDescending(x => x.CreatedAt);

            var totalRecords = query.Count();

            var data = query
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .Select(x => new ArtistResponseDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    StageName = x.StageName,
                    Country = x.Country,
                    Url = x.Url,
                    IsActived = x.IsActived,
                    IsDeleted = x.IsDeleted,
                    CreatedAt = x.CreatedAt
                })
                .ToList();

            result.TotalRecords = totalRecords;
            result.TotalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "artists"));
        }

        public async Task<BaseTableResponse<CrmUserResponseDto>> SearchUsers(BaseSearchDto<CrmUserRequestDto> dto)
        {
            var result = new BaseTableResponse<CrmUserResponseDto>();

            var repoUser = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUser>().QueryAll();
            var repoProfile = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUserProfile>().QueryAll();

            var users = repoUser.Where(user => user.RoleCode == null || !user.RoleCode.Contains("ADMIN"));

            if (users.Any(user => !repoProfile.Any(profile => profile.UserId == user.Id)))
            {
                result.Code = ResponseStatusCode.Status404;
                return result.Fail(string.Format(Messages.Validation.NotFound, "profile"));
            }

            var query =
                from user in users
                join profile in repoProfile on user.Id equals profile.UserId
                select new CrmUserResponseDto
                {
                    Id = user.Id,
                    RefCode = user.RefCode,
                    Username = user.Username,
                    Email = user.Email,
                    UserType = user.UserType,
                    FullName = profile.FullName,
                    Phone = profile.Phone,
                    DateOfBirth = profile.DateOfBirth,
                    Gender = profile.Gender,
                    Bio = profile.Bio,
                    Uri = profile.Uri,
                    IsActived = profile.IsActived,
                    IsDeleted = profile.IsDeleted,
                    CreatedAt = profile.CreatedAt,
                    UpdatedAt = profile.UpdatedAt,
                    CreatedBy = user.CreatedBy,
                    UpdatedBy = profile.UpdatedBy
                };

            if (!string.IsNullOrWhiteSpace(dto.SearchParams?.RefCode))
            {
                var keyword = dto.SearchParams.RefCode.Trim().ToLower();

                query = query.Where(x =>
                    x.RefCode != null &&
                    x.RefCode.ToLower().Contains(keyword));
            }

            if (!string.IsNullOrWhiteSpace(dto.SearchParams?.Keyword))
            {
                var keyword = dto.SearchParams.Keyword.Trim().ToLower();

                query = query.Where(x =>
                    (x.Username != null &&
                     x.Username.ToLower().Contains(keyword))

                    ||

                    (x.Email != null &&
                     x.Email.ToLower().Contains(keyword))

                    ||

                    (x.FullName != null &&
                     x.FullName.ToLower().Contains(keyword))

                     ||

                    (x.Phone != null &&
                     x.Phone.ToLower().Contains(keyword))
                );
            }

            if (dto.SearchParams?.UserType.HasValue == true)
            {
                var userType = dto.SearchParams.UserType.Value;
                query = query.Where(x => x.UserType == userType);
            }

            if (!string.IsNullOrWhiteSpace(dto.SearchParams?.Gender))
            {
                var keyword =
                    dto.SearchParams.Gender
                        .Trim()
                        .ToLower();

                query = query.Where(x =>
                    x.Gender != null &&
                    x.Gender.ToLower().Contains(keyword));
            }

            if (!string.IsNullOrWhiteSpace(dto.SearchParams?.Bio))
            {
                var keyword =
                    dto.SearchParams.Bio
                        .Trim()
                        .ToLower();

                query = query.Where(x =>
                    x.Bio != null &&
                    x.Bio.ToLower().Contains(keyword));
            }

            if (dto.SearchParams?.IsActived.HasValue == true)
            {
                query = query.Where(x =>
                    x.IsActived ==
                    dto.SearchParams.IsActived.Value);
            }

            if (dto.SearchParams?.IsDeleted.HasValue == true)
            {
                query = query.Where(x =>
                    x.IsDeleted ==
                    dto.SearchParams.IsDeleted.Value);
            }

            if (dto.SearchParams?.FromDate.HasValue == true)
            {
                var fromDate =
                    dto.SearchParams.FromDate.Value.Date;

                query = query.Where(x =>
                    x.CreatedAt.HasValue &&
                    x.CreatedAt.Value >= fromDate);
            }

            if (dto.SearchParams?.ToDate.HasValue == true)
            {
                var toDate =
                    dto.SearchParams.ToDate.Value
                        .Date
                        .AddDays(1)
                        .AddTicks(-1);

                query = query.Where(x =>
                    x.CreatedAt.HasValue &&
                    x.CreatedAt.Value <= toDate);
            }

            if (!string.IsNullOrWhiteSpace(dto.SearchParams?.SortBy))
            {
                switch (dto.SearchParams.SortBy.Trim().ToLower())
                {
                    case "username":
                        query = dto.Asc
                            ? query.OrderBy(x => x.Username)
                            : query.OrderByDescending(x => x.Username);
                        break;

                    case "fullname":
                        query = dto.Asc
                            ? query.OrderBy(x => x.FullName)
                            : query.OrderByDescending(x => x.FullName);
                        break;

                    case "email":
                        query = dto.Asc
                            ? query.OrderBy(x => x.Email)
                            : query.OrderByDescending(x => x.Email);
                        break;

                    case "phone":
                        query = dto.Asc
                            ? query.OrderBy(x => x.Phone)
                            : query.OrderByDescending(x => x.Phone);
                        break;

                    case "gender":
                        query = dto.Asc
                            ? query.OrderBy(x => x.Gender)
                            : query.OrderByDescending(x => x.Gender);
                        break;

                    case "usertype":
                        query = dto.Asc
                            ? query.OrderBy(x => x.UserType)
                            : query.OrderByDescending(x => x.UserType);
                        break;

                    case "createdat":
                        query = dto.Asc
                            ? query.OrderBy(x => x.CreatedAt)
                            : query.OrderByDescending(x => x.CreatedAt);
                        break;

                    case "updatedat":
                        query = dto.Asc
                            ? query.OrderBy(x => x.UpdatedAt)
                            : query.OrderByDescending(x => x.UpdatedAt);
                        break;

                    default:
                        query = query.OrderByDescending(x => x.CreatedAt);
                        break;
                }
            }
            else
            {
                query = query.OrderByDescending(x => x.CreatedAt);
            }

            var totalRecords = query.Count();
            var totalPages = (int)Math.Ceiling((double)totalRecords / dto.PageSize);

            var data = query
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .ToList();

            foreach (var item in data)
            {
                item.Uri = string.IsNullOrWhiteSpace(item.Uri) ? null : _cloudinaryService.BuildImageUrl(item.Uri);
                item.UserTypeName = Enum.IsDefined(typeof(MS_Domain.Enums.UserType), (int)item.UserType)
                    ? EnumHelper.GetDisplayName((MS_Domain.Enums.UserType)item.UserType)
                    : null;
            }

            result.TotalRecords = totalRecords;
            result.TotalPages = totalPages;
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "users"));
        }

        public async Task<BaseResponse<CrmUserProfileResponseDto>> GetProfile(long userId)
        {
            var result = new BaseResponse<CrmUserProfileResponseDto>();

            var repoProfile = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUserProfile>().QueryAll();
            var profile = repoProfile.FirstOrDefault(x => x.UserId == userId);

            if (profile == null)
            {
                result.Code = ResponseStatusCode.Status404;
                return result.Fail(string.Format(Messages.Validation.NotFound, "User info"));
            }

            result.Data = new CrmUserProfileResponseDto
            {
                Id = profile.Id,
                RefCode = profile.RefCode,
                UserId = profile.UserId,
                FullName = profile.FullName,
                Phone = profile.Phone,
                DateOfBirth = profile.DateOfBirth,
                Gender = profile.Gender,
                Bio = profile.Bio,
                Uri = string.IsNullOrWhiteSpace(profile.Uri) ? null : _cloudinaryService.BuildImageUrl(profile.Uri),
                IsActived = profile.IsActived,
                IsDeleted = profile.IsDeleted,
                CreatedAt = profile.CreatedAt,
                CreatedBy = profile.CreatedBy,
                UpdatedAt = profile.UpdatedAt,
                UpdatedBy = profile.UpdatedBy
            };

            return result.Success(string.Format(Messages.Action.GetSuccess, "data"));
        }

        public async Task<BaseResponse<bool>> UpdateProfile(long userId, UpdateUserRequestDto dto)
        {
            var result = new BaseResponse<bool>();

            var repoUser = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUser>().QueryAll();
            var repoProfile = _crmUnitOfWork.GetRepositoryAsync<CrmUserProfile>();

            var user = repoUser.FirstOrDefault(x => x.Id == userId);

            if (user == null)
            {
                result.Code = ResponseStatusCode.Status404;
                return result.Fail(string.Format(Messages.Validation.NotFound, "user"));
            }

            var profile = repoProfile.QueryAll().FirstOrDefault(x => x.UserId == userId);

            if (profile == null)
            {
                result.Code = ResponseStatusCode.Status404;
                return result.Fail(string.Format(Messages.Validation.NotFound, "profile"));
            }

            profile.FullName = dto.FullName;
            profile.Phone = dto.Phone;
            profile.DateOfBirth = dto.DateOfBirth;
            profile.Gender = dto.Gender;
            profile.Bio = dto.Bio;
            profile.UpdatedAt = DateTimeHelper.VnNow;
            profile.UpdatedBy = userId;

            if (dto.Uri != null)
            {
                var uploadResult = await _cloudinaryService.UploadProfileImageAsync(dto.Uri);

                if (string.IsNullOrEmpty(uploadResult.Data))
                {
                    result.Code = ResponseStatusCode.Status500;
                    return result.Fail(string.Format(Messages.Action.UploadFail, "image profile"));
                }

                profile.Uri = uploadResult.Data;
            }

            await repoProfile.UpdateAsync(profile);
            await _crmUnitOfWork.SaveChangesAsync();

            result.Data = true;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.UpdateSuccess, "data"));
        }

        public async Task<BaseResponse<BankUserResponseDto>> GetBankUser(long userId)
        {
            var result = new BaseResponse<BankUserResponseDto>();

            var profile = await _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUserProfile>().QueryAll().FirstOrDefaultAsync(x => x.UserId == userId);

            if (profile == null)
            {
                result.Code = ResponseStatusCode.Status404;
                return result.Fail(string.Format(Messages.Validation.NotFound, "profile"));
            }

            var data = await _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmBankUserInfo>()
                .QueryAll()
                .Where(x =>
                    x.UserProfileId == profile.Id
                    && !x.IsDeleted)
                .Select(x => new BankUserResponseDto
                {
                    Id = x.Id,
                    UserProfileId = x.UserProfileId,
                    BankCode = x.BankCode,
                    BankName = x.BankName,
                    AccountNo = x.AccountNo,
                    AccountName = x.AccountName,
                    VietQrUrl = x.VietQrUrl,
                    QrImageUrl = x.QrImageUrl,
                    IsActive = x.IsActived,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "bank"));
        }

        public async Task<BaseResponse<BankUserResponseDto>> CreateBankUser(long userId, string refCode, BankUserRequestDto dto)
        {
            var result = new BaseResponse<BankUserResponseDto>();

            var repo = _crmUnitOfWork.GetRepositoryAsync<CrmBankUserInfo>();
            var profile = await _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUserProfile>().QueryAll().FirstOrDefaultAsync(x => x.UserId == userId);

            if (profile == null)
            {
                result.Code = ResponseStatusCode.Status404;
                return result.Fail(string.Format(Messages.Validation.NotFound, "profile"));
            }
            var entity = new CrmBankUserInfo
            {
                RefCode = refCode,
                UserProfileId = profile.Id,
                BankCode = dto.BankCode,
                BankName = dto.BankName,
                AccountNo = dto.AccountNo,
                AccountName = dto.AccountName,
                VietQrUrl = dto.VietQrUrl,
                QrImageUrl = dto.QrImageUrl,
                IsActived = true,
                IsDeleted = false,
                TransDate = DateTimeHelper.VnNow,
                CreatedBy = userId
            };

            await repo.AddAsync(entity);
            await _crmUnitOfWork.SaveChangesAsync();

            result.Data = new BankUserResponseDto
            {
                Id = entity.Id,
                UserProfileId = entity.UserProfileId,
                BankCode = entity.BankCode,
                BankName = entity.BankName,
                AccountNo = entity.AccountNo,
                AccountName = entity.AccountName,
                VietQrUrl = entity.VietQrUrl,
                QrImageUrl = entity.QrImageUrl,
                IsActive = entity.IsActived,
                CreatedAt = entity.CreatedAt
            };

            result.Code = ResponseStatusCode.Status200;
            return result.Success(string.Format(Messages.Action.CreateSuccess, "bank"));
        }

        public async Task<BaseResponse<BankUserResponseDto>> UpdateBankUser(long userId, long id, BankUserRequestDto dto)
        {
            var result = new BaseResponse<BankUserResponseDto>();

            var profile = await _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUserProfile>().QueryAll().FirstOrDefaultAsync(x => x.UserId == userId);

            if (profile == null)
            {
                result.Code = ResponseStatusCode.Status404;
                return result.Fail(string.Format(Messages.Validation.NotFound, "profile"));
            }

            var repo = _crmUnitOfWork.GetRepositoryAsync<CrmBankUserInfo>();

            var entity = await repo.QueryAll()
                .FirstOrDefaultAsync(x =>
                    x.Id == id
                    && x.UserProfileId == profile.Id
                    && !x.IsDeleted);

            if (entity == null)
            {
                return result.Fail(string.Format(Messages.Validation.NotFound, "bank"));
            }

            entity.BankCode = dto.BankCode;
            entity.BankName = dto.BankName;
            entity.AccountNo = dto.AccountNo;
            entity.AccountName = dto.AccountName;
            entity.VietQrUrl = dto.VietQrUrl;
            entity.QrImageUrl = dto.QrImageUrl;
            entity.UpdatedAt = DateTimeHelper.VnNow;
            entity.UpdatedBy = userId;

            await repo.UpdateAsync(entity);
            await _crmUnitOfWork.SaveChangesAsync();

            result.Data = new BankUserResponseDto
            {
                Id = entity.Id,
                UserProfileId = entity.UserProfileId,
                BankCode = entity.BankCode,
                BankName = entity.BankName,
                AccountNo = entity.AccountNo,
                AccountName = entity.AccountName,
                VietQrUrl = entity.VietQrUrl,
                QrImageUrl = entity.QrImageUrl,
                IsActive = entity.IsActived,
                CreatedAt = entity.CreatedAt
            };

            result.Code = ResponseStatusCode.Status200;
            return result.Success(string.Format(Messages.Action.UpdateSuccess, "bank"));
        }

        public async Task<BaseTableResponse<BankResponseDto>> GetBanks()
        {
            using var client = new HttpClient();
            var response = await client.GetFromJsonAsync<VietQrResponse>("https://api.vietqr.io/v2/banks");

            return new BaseTableResponse<BankResponseDto>
            {
                Data = response?.Data?
                    .Select(x => new BankResponseDto
                    {
                        Code = x.Code,
                        Bin = x.Bin,
                        Name = x.Name,
                        ShortName = x.ShortName,
                        Logo = x.Logo
                    })
                    .ToList()
            };
        }

        public BaseResponse<SongSyncStatusDto> StartSongSync(long userId) => _songSyncService.Start(userId);

        public BaseResponse<SongSyncStatusDto> GetSongSyncStatus() => _songSyncService.GetStatus();

        public Task<BaseResponse<LyricsResponseDto?>> GetLyricsById(int id) => _lyricsService.GetLyricsByIdAsync(id);

        public Task<BaseTableResponse<LyricsResponseDto>> SearchLyrics(BaseSearchDto<LyricsSearchRequestDto> request) => _lyricsService.SearchLyricsAsync(request);

        public Task<BaseResponse<bool>> PublishLyrics(PublishLyricsRequestDto request) => _lyricsService.PublishLyricsAsync(request);

        private Dictionary<long, string> ResolveUserNames(IEnumerable<long> userIds)
        {
            var ids = userIds.Distinct().ToList();

            if (ids.Count == 0)
                return new Dictionary<long, string>();

            var repoUser = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUser>().QueryAll();
            var repoProfile = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUserProfile>().QueryAll();

            var users = repoUser.Where(u => ids.Contains(u.Id)).ToList();
            var profiles = repoProfile.Where(p => ids.Contains(p.UserId)).ToList();

            return users.ToDictionary(u => u.Id, u => profiles.FirstOrDefault(p => p.UserId == u.Id)?.FullName ?? u.Username);
        }

        private Dictionary<long, string> ResolveUserNames(IEnumerable<long?> userIds)
        {
            var ids = userIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();

            if (ids.Count == 0)
                return new Dictionary<long, string>();

            var repoUser = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUser>().QueryAll();
            var repoProfile = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUserProfile>().QueryAll();

            var users = repoUser.Where(u => ids.Contains(u.Id)).ToList();
            var profiles = repoProfile.Where(p => ids.Contains(p.UserId)).ToList();

            return users.ToDictionary(
                u => u.Id,
                u => profiles.FirstOrDefault(p => p.UserId == u.Id)?.FullName ?? u.Username);
        }
    }
}
