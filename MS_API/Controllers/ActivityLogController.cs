using Microsoft.AspNetCore.Mvc;
using MS_Application.DataTransferObjects.ActivityLog;
using MS_Application.DataTransferObjects.Base;
using MS_Application.Services.Interfaces;

namespace MS_API.Controllers
{
    [ApiController]
    [Route("api/activity-logs")]
    public class ActivityLogController : BaseController
    {
        private readonly IActivityLogService _activityLogService;

        public ActivityLogController(IActivityLogService activityLogService)
        {
            _activityLogService = activityLogService;
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] BaseSearchDto<ActivityLogSearchRequest> dto)
        {
            var result = await _activityLogService.SearchAsync(dto);
            return Ok(result);
        }
    }
}
