using MS_Domain.Common;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.Artist;
using MS_Application.DataTransferObjects.Base;
using MS_Application.Helpers;
using MS_Application.Repositories.Interfaces;
using MS_Application.Services.Interfaces;
using MS_Domain.Entities.DISTS;

namespace MS_Application.Services
{
    public class ArtistService : IArtistService
    {
        private readonly IDistUnitOfWork _distUnitOfWork;

        public ArtistService(IDistUnitOfWork distUnitOfWork)
        {
            _distUnitOfWork = distUnitOfWork;
        }

        public async Task<BaseTableResponse<ArtistResponseDto>> GetArtists(BaseSearchDto<ArtistRequestDto> dto)
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

        public async Task<BaseResponse<ArtistResponseDto>> GetArtistById(long id)
        {
            var result = new BaseResponse<ArtistResponseDto>();
            var repo = _distUnitOfWork.GetRepositoryReadOnlyAsync<DistArtists>().QueryAll();

            var artist = repo.FirstOrDefault(x => x.Id == id && !x.IsDeleted);

            if (artist == null)
            {
                return result.Fail(string.Format(Messages.Validation.NotFound, "artist"));
            }

            result.Data = new ArtistResponseDto
            {
                Id = artist.Id,
                Name = artist.Name,
                StageName = artist.StageName,
                Country = artist.Country,
                Url = artist.Url,
                IsActived = artist.IsActived,
                IsDeleted = artist.IsDeleted,
                CreatedAt = artist.CreatedAt
            };

            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "artist"));
        }

        public async Task<BaseResponse<ArtistResponseDto>> CreateArtist(ArtistCreateDto dto, long userId)
        {
            var result = new BaseResponse<ArtistResponseDto>();
            var repo = _distUnitOfWork.GetRepositoryAsync<DistArtists>();

            var entity = new DistArtists
            {
                Name = dto.Name,
                StageName = dto.StageName,
                Country = dto.Country,
                Url = dto.Url,
                CreatedBy = userId
            };

            await repo.AddAsync(entity);
            await _distUnitOfWork.SaveChangesAsync();

            result.Data = new ArtistResponseDto
            {
                Id = entity.Id,
                Name = entity.Name,
                StageName = entity.StageName,
                Country = entity.Country,
                Url = entity.Url,
                CreatedAt = entity.CreatedAt
            };

            return result.Success(string.Format(Messages.Action.CreateSuccess, "artist"));
        }

        public async Task<BaseResponse<ArtistResponseDto>> UpdateArtist(long id, ArtistUpdateDto dto, long userId)
        {
            var result = new BaseResponse<ArtistResponseDto>();
            var repo = _distUnitOfWork.GetRepositoryAsync<DistArtists>();

            var artist = await repo.FindByIdAsync(id);

            if (artist == null || artist.IsDeleted)
            {
                return result.Fail(string.Format(Messages.Validation.NotFound, "artist"));
            }

            artist.Name = dto.Name;
            artist.StageName = dto.StageName;
            artist.Country = dto.Country;
            artist.Url = dto.Url;
            artist.UpdatedBy = userId;
            artist.UpdatedAt = DateTimeHelper.VnNow;

            await repo.UpdateAsync(artist);
            await _distUnitOfWork.SaveChangesAsync();

            result.Data = new ArtistResponseDto
            {
                Id = artist.Id,
                Name = artist.Name,
                StageName = artist.StageName,
                Country = artist.Country,
                Url = artist.Url,
                CreatedAt = artist.CreatedAt
            };

            return result.Success(string.Format(Messages.Action.UpdateSuccess, "artist"));
        }
    }
}