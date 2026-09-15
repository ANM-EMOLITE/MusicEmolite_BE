using MS_Domain.Common;
using Microsoft.EntityFrameworkCore;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Dashboard;
using MS_Application.Helpers;
using MS_Application.Repositories.Interfaces;
using MS_Application.Services.Interfaces;
using MS_Domain.Entities.CRMS;
using MS_Domain.Entities.DISTS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS_Application.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly IDistUnitOfWork _distUnitOfWork;
        private readonly ICrmUnitOfWork _crmUnitOfWork;

        public DashboardService(
            IDistUnitOfWork distUnitOfWork,
            ICrmUnitOfWork crmUnitOfWork)
        {
            _distUnitOfWork = distUnitOfWork;
            _crmUnitOfWork = crmUnitOfWork;
        }

        public async Task<BaseResponse<DashboardSummaryResponseDto>> GetSummary()
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

        public async Task<BaseResponse<List<DashboardTrendResponseDto>>> GetTrend()
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
    }
}
