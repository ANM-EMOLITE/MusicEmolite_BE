using MS_Application.DataTransferObjects.ActivityLog;
using MS_Application.DataTransferObjects.Base;
using MS_Application.Helpers;
using MS_Application.Repositories.Interfaces;
using MS_Application.Services.Interfaces;
using MS_Domain.Entities.CRMS;
using MS_Domain.Entities.DISTS;

namespace MS_Application.Services
{
    public class ActivityLogService : IActivityLogService
    {
        private readonly IDistUnitOfWork _distUnitOfWork;
        private readonly ICrmUnitOfWork _crmUnitOfWork;

        public ActivityLogService(IDistUnitOfWork distUnitOfWork, ICrmUnitOfWork crmUnitOfWork)
        {
            _distUnitOfWork = distUnitOfWork;
            _crmUnitOfWork = crmUnitOfWork;
        }

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

        public async Task<BaseTableResponse<ActivityLogResponseDto>> SearchAsync(BaseSearchDto<ActivityLogSearchRequest> dto)
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
    }
}
