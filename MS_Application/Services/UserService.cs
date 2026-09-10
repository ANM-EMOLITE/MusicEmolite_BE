using MS_Domain.Common;
using AMFC_Domain.Entities.Crms;
using Microsoft.EntityFrameworkCore;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.User;
using MS_Application.DataTransferObjects.User.BankUser;
using MS_Application.Helpers;
using MS_Application.Repositories.Interfaces;
using MS_Application.Services.Interfaces;
using MS_Application.Services.Interfaces.External;
using MS_Domain.Entities.CRMS;

namespace MS_Application.Services
{
    public class UserService : IUserService
    {
        private readonly ICrmUnitOfWork _crmUnitOfWork;
        private readonly ICloudinaryService _cloudinaryService;

        public UserService(ICrmUnitOfWork crmUnitOfWork, ICloudinaryService cloudinaryService)
        {
            _crmUnitOfWork = crmUnitOfWork;
            _cloudinaryService = cloudinaryService;
        }


        public async Task<BaseTableResponse<CrmUserResponseDto>> GetUsers(BaseSearchDto<CrmUserRequestDto> dto)
        {
            var result = new BaseTableResponse<CrmUserResponseDto>();

            var repoUser = _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmUser>()
                .QueryAll();

            var repoProfile = _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmUserProfile>()
                .QueryAll();

            var query =
                from user in repoUser

                join profile in repoProfile
                    on user.Id equals profile.UserId into profiles

                from profile in profiles.DefaultIfEmpty()

                where user.RoleCode == null
                      || !user.RoleCode.Contains("ADMIN")

                select new CrmUserResponseDto
                {
                    RefCode = user.RefCode,

                    Username = user.Username,

                    Email = user.Email,

                    RoleCode = user.RoleCode,

                    FullName = profile != null
                        ? profile.FullName
                        : null,

                    Phone = profile != null
                        ? profile.Phone
                        : null,

                    DateOfBirth = profile != null
                        ? profile.DateOfBirth
                        : null,

                    Gender = profile != null
                        ? profile.Gender
                        : null,

                    Bio = profile != null
                        ? profile.Bio
                        : null,

                    Uri = profile != null &&
                          !string.IsNullOrWhiteSpace(profile.Uri)
                        ? _cloudinaryService.BuildImageUrl(profile.Uri)
                        : null,

                    IsActived = profile != null &&
                                profile.IsActived,

                    IsDeleted = profile != null &&
                                profile.IsDeleted,

                    CreatedAt = profile != null
                        ? profile.CreatedAt
                        : null,

                    UpdatedAt = profile != null
                        ? profile.UpdatedAt
                        : null,

                    CreatedBy = user.CreatedBy,

                    UpdatedBy = profile != null
                        ? profile.UpdatedBy
                        : null
                };

            if (!string.IsNullOrWhiteSpace(dto.SearchParams?.RefCode))
            {
                var keyword =
                    dto.SearchParams.RefCode
                        .Trim()
                        .ToLower();

                query = query.Where(x =>
                    x.RefCode != null &&
                    x.RefCode.ToLower().Contains(keyword));
            }

            if (!string.IsNullOrWhiteSpace(dto.SearchParams?.Keyword))
            {
                var keyword = dto.SearchParams.Keyword
                    .Trim()
                    .ToLower();

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

            if (!string.IsNullOrWhiteSpace(dto.SearchParams?.Role))
            {
                var keyword =
                    dto.SearchParams.Role
                        .Trim()
                        .ToLower();

                query = query.Where(x =>
                    x.RoleCode != null &&
                    x.RoleCode.ToLower().Contains(keyword));
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

                    case "rolecode":
                        query = dto.Asc
                            ? query.OrderBy(x => x.RoleCode)
                            : query.OrderByDescending(x => x.RoleCode);
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

            var data = query
                .Skip(dto.Start)
                .Take(dto.PageSize)
                .ToList();

            result.TotalRecords = totalRecords;

            result.TotalPages =
                (int)Math.Ceiling(
                    (double)totalRecords / dto.PageSize
                );

            result.Data = data;

            result.Code = ResponseStatusCode.Status200;

            return result.Success(
                string.Format(Messages.Action.GetSuccess, "users")
            );
        }

        public async Task<BaseResponse<CrmUserProfileResponseDto>> GetUserProfile(long userId)
        {
            var result = new BaseResponse<CrmUserProfileResponseDto>();

            var repoProfile = _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUserProfile>().QueryAll();
            var profile = repoProfile.FirstOrDefault(x => x.UserId == userId);

            if (profile == null)
            {
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

        public async Task<BaseResponse<bool>> UpdateUser(long userId, UpdateUserRequestDto dto)
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

            var profile = await _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmUserProfile>()
                .QueryAll()
                .FirstOrDefaultAsync(x => x.UserId == userId);

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

        public async Task<BaseResponse<List<BankUserResponseDto>>> GetAdminBankUsers()
        {
            var result = new BaseResponse<List<BankUserResponseDto>>();

            var adminProfileIds = await _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmUser>()
                .QueryAll()
                .Where(x => x.UserType == (short)MS_Domain.Enums.UserType.ADMIN)
                .Join(
                    _crmUnitOfWork.GetRepositoryReadOnlyAsync<CrmUserProfile>().QueryAll(),
                    user => user.Id,
                    profile => profile.UserId,
                    (user, profile) => profile.Id
                )
                .ToListAsync();

            var data = await _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmBankUserInfo>()
                .QueryAll()
                .Where(x => adminProfileIds.Contains(x.UserProfileId) && !x.IsDeleted)
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
                .ToListAsync();

            result.Data = data;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.GetSuccess, "bank"));
        }

        public async Task<BaseResponse<BankUserResponseDto>> CreateBankUser(long userId, string refCode, BankUserRequestDto dto)
        {
            var result = new BaseResponse<BankUserResponseDto>();

            var repo = _crmUnitOfWork.GetRepositoryAsync<CrmBankUserInfo>();
            var profile = await _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmUserProfile>()
                .QueryAll()
                .FirstOrDefaultAsync(x => x.UserId == userId);

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

            var profile = await _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmUserProfile>()
                .QueryAll()
                .FirstOrDefaultAsync(x => x.UserId == userId);

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

        public async Task<BaseResponse<bool>> DeleteBankUser(long userId, long id)
        {
            var result = new BaseResponse<bool>();

            var profile = await _crmUnitOfWork
                .GetRepositoryReadOnlyAsync<CrmUserProfile>()
                .QueryAll()
                .FirstOrDefaultAsync(x => x.UserId == userId);

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

            entity.IsDeleted = true;
            entity.IsActived = false;
            entity.UpdatedAt = DateTimeHelper.VnNow;
            entity.UpdatedBy = userId;

            await repo.UpdateAsync(entity);
            await _crmUnitOfWork.SaveChangesAsync();

            result.Data = true;
            result.Code = ResponseStatusCode.Status200;

            return result.Success(string.Format(Messages.Action.DeleteSuccess, "bank"));
        }
    }
}
