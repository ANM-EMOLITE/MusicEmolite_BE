using Microsoft.EntityFrameworkCore;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.Albums;
using MS_Application.DataTransferObjects.Base;
using MS_Application.Helpers;
using MS_Application.Repositories.Interfaces;
using MS_Application.Services.Interfaces;
using MS_Application.Services.Interfaces.External;
using MS_Domain.Entities.CRMS;
using MS_Domain.Entities.DISTS;
using MS_Domain.Enums;

namespace MS_Application.Services
{
    public class AlbumsService : IAlbumsService
    {
        private readonly IDistUnitOfWork _distUnitOfWork;
        private readonly ICrmUnitOfWork _crmUnitOfWork;
        private readonly ICloudinaryService _cloudinaryService;

        public AlbumsService(IDistUnitOfWork distUnitOfWork, ICrmUnitOfWork crmUnitOfWork, ICloudinaryService cloudinaryService)
        {
            _distUnitOfWork = distUnitOfWork;
            _crmUnitOfWork = crmUnitOfWork;
            _cloudinaryService = cloudinaryService;
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

        public async Task<BaseTableResponse<AlbumResponseDto>> GetAlbums(BaseSearchDto<AlbumRequestDto> dto, long userId)
        {
            var result = new BaseTableResponse<AlbumResponseDto>();

            var repoAlbum = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistAlbums>().QueryAll();

            var query = repoAlbum.Where(x => !x.IsDeleted && x.CreatedBy == userId);

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
            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "albums"));
        }

        public async Task<BaseTableResponse<AlbumResponseDto>> GetAlbumsByUserForAdmin(long userId, BaseSearchDto<AlbumRequestDto> dto)
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

        public async Task<BaseTableResponse<AlbumResponseDto>> GetPublicAlbums(BaseSearchDto<AlbumRequestDto> dto)
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

        public async Task<BaseResponse<AlbumResponseDto>> GetAlbumById(long id)
        {
            var result = new BaseResponse<AlbumResponseDto>();
            var album = await _distUnitOfWork.GetRepositoryReadOnlyAsync<DistAlbums>().QueryAll().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (album == null)
                return result.Fail(string.Format(Messages.Validation.NotFound, "album"));

            var userNames = ResolveUserNames(new long?[] { album.CreatedBy });

            result.Data = new AlbumResponseDto
            {
                Id = album.Id,
                Title = album.Title,
                ReleaseDate = album.ReleaseDate,
                ArtistId = album.ArtistId,
                AlbumTypeName = EnumHelper.GetDisplayName((MS_Domain.Enums.Type)album.AlbumType),
                Uri = string.IsNullOrEmpty(album.Uri) ? null : _cloudinaryService.BuildImageUrl(album.Uri),
                IsActived = album.IsActived,
                IsDeleted = album.IsDeleted,
                CreatedAt = album.CreatedAt,
                CreatedBy = album.CreatedBy,
                CreatedByName = album.CreatedBy.HasValue && userNames.TryGetValue(album.CreatedBy.Value, out var creatorName) ? creatorName : null
            };

            result.Code = ResponseStatusCode.Status200;
            return result.Success(string.Format(Messages.Action.GetSuccess, "album"));
        }

        public async Task<BaseResponse<AlbumResponseDto>> CreateAlbum(AlbumCreateDto dto, long userId)
        {
            var result = new BaseResponse<AlbumResponseDto>();

            var repoAlbum = _distUnitOfWork.GetRepositoryAsync<DistAlbums>();
            var uploadResult = await _cloudinaryService.UploadMusicImageAsync(dto.Image);

            var entity = new DistAlbums
            {
                Title = dto.Title,
                ReleaseDate = dto.ReleaseDate,
                ArtistId = 1,
                AlbumType = dto.AlbumType,
                Uri = uploadResult.Data,
                CreatedBy = userId
            };

            await repoAlbum.AddAsync(entity);
            await _distUnitOfWork.SaveChangesAsync();
            result.Code = ResponseStatusCode.Status200;
            return result.Success(string.Format(Messages.Action.CreateSuccess, "album"));
        }

        public async Task<BaseResponse<AlbumResponseDto>> UpdateAlbum(long id, AlbumUpdateDto dto, long userId)
        {
            var result = new BaseResponse<AlbumResponseDto>();
            var repoAlbum = _distUnitOfWork.GetRepositoryAsync<DistAlbums>();

            var album = await repoAlbum.QueryAll().FirstOrDefaultAsync(x => x.Id == id);

            if (album == null || album.IsDeleted)
                return result.Fail(string.Format(Messages.Validation.NotFound, "album"));

            album.Title = dto.Title;
            album.ReleaseDate = dto.ReleaseDate;
            album.ArtistId = dto.ArtistId;
            album.IsActived = dto.IsActived;
            album.UpdatedBy = userId;

            repoAlbum.UpdateAsync(album);
            await _distUnitOfWork.SaveChangesAsync();

            result.Code = ResponseStatusCode.Status200;
            return result.Success(string.Format(Messages.Action.UpdateSuccess, "album"));
        }

        public async Task<BaseResponse<bool>> DeleteAlbum(long id, long userId)
        {
            var result = new BaseResponse<bool>();
            var repoAlbum = _distUnitOfWork.GetRepositoryAsync<DistAlbums>();
            var album = await repoAlbum.QueryAll().FirstOrDefaultAsync(x => x.Id == id);

            if (album == null || album.IsDeleted)
                return result.Fail(string.Format(Messages.Validation.NotFound, "album"));

            album.IsDeleted = true;
            album.UpdatedBy = userId;

            repoAlbum.UpdateAsync(album);
            await _distUnitOfWork.SaveChangesAsync();

            result.Data = true;
            result.Code = ResponseStatusCode.Status200;
            return result.Success(string.Format(Messages.Action.DeleteSuccess, "album"));
        }
    }
}