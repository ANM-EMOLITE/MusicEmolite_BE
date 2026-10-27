using Microsoft.AspNetCore.Mvc;
using MS_API.Filters;
using MS_Application.DataTransferObjects.ActivityLog;
using MS_Application.DataTransferObjects.Albums;
using MS_Application.DataTransferObjects.Artist;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Lyrics;
using MS_Application.DataTransferObjects.Songs;
using MS_Application.DataTransferObjects.User;
using MS_Application.DataTransferObjects.User.BankUser;
using MS_Application.Services.Interfaces.External;

namespace MS_API.Controllers
{
    [ApiController]
    [AdminOnly]
    [Route("api/admin")]
    public class AdminController : BaseController
    {
        private readonly IAdminService _adminService;

        public AdminController(IAdminService adminService)
        {
            _adminService = adminService;
        }

        [HttpGet("dashboard/summary")]
        public async Task<IActionResult> GetDashboardSummary()
        {
            var result = await _adminService.GetDashboardSummary();
            return Ok(result);
        }

        [HttpGet("dashboard/trend")]
        public async Task<IActionResult> GetDashboardTrend()
        {
            var result = await _adminService.GetDashboardTrend();
            return Ok(result);
        }

        [HttpPost("activity-logs/search")]
        public async Task<IActionResult> SearchActivityLogs([FromBody] BaseSearchDto<ActivityLogSearchRequest> dto)
        {
            var result = await _adminService.SearchActivityLogs(dto);
            return Ok(result);
        }

        [HttpPost("songs/search")]
        public async Task<IActionResult> SearchSongs([FromBody] BaseSearchDto<SongRequestDto> dto)
        {
            var result = await _adminService.SearchSongs(dto, UserId);
            return Ok(result);
        }

        [HttpPost("songs")]
        public async Task<IActionResult> CreateSong(SongCreateDto dto)
        {
            var result = await _adminService.CreateSong(dto, UserId);
            return Ok(result);
        }

        [HttpPost("songs/liked-by-user/{userId}")]
        public async Task<IActionResult> GetLikedSongsByUser(long userId, [FromBody] BaseSearchDto<SongRequestDto> dto)
        {
            var result = await _adminService.GetLikedSongsByUser(userId, dto);
            return Ok(result);
        }

        [HttpPost("songs/sync")]
        public IActionResult StartSongSync()
        {
            return Ok(_adminService.StartSongSync(UserId));
        }

        [HttpGet("songs/sync/status")]
        public IActionResult GetSongSyncStatus()
        {
            return Ok(_adminService.GetSongSyncStatus());
        }

        [HttpGet("lyrics/{id}")]
        public async Task<IActionResult> GetLyricsById(int id)
        {
            var result = await _adminService.GetLyricsById(id);
            return Ok(result);
        }

        [HttpPost("lyrics/search")]
        public async Task<IActionResult> SearchLyrics([FromBody] BaseSearchDto<LyricsSearchRequestDto> request)
        {
            var result = await _adminService.SearchLyrics(request);
            return Ok(result);
        }

        [HttpPost("lyrics/publish")]
        public async Task<IActionResult> PublishLyrics([FromBody] PublishLyricsRequestDto request)
        {
            var result = await _adminService.PublishLyrics(request);
            return Ok(result);
        }

        [HttpPost("albums/search")]
        public async Task<IActionResult> SearchAlbums([FromBody] BaseSearchDto<AlbumRequestDto> dto)
        {
            var result = await _adminService.SearchAlbums(dto);
            return Ok(result);
        }

        [HttpPost("albums/by-user/{userId}")]
        public async Task<IActionResult> GetAlbumsByUser(long userId, [FromBody] BaseSearchDto<AlbumRequestDto> dto)
        {
            var result = await _adminService.GetAlbumsByUser(userId, dto);
            return Ok(result);
        }

        [HttpPost("artists/search")]
        public async Task<IActionResult> SearchArtists([FromBody] BaseSearchDto<ArtistRequestDto> dto)
        {
            var result = await _adminService.SearchArtists(dto);
            return Ok(result);
        }

        [HttpPost("users/search")]
        public async Task<IActionResult> SearchUsers([FromBody] BaseSearchDto<CrmUserRequestDto> dto)
        {
            var result = await _adminService.SearchUsers(dto);
            return Ok(result);
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var result = await _adminService.GetProfile(UserId);
            return Ok(result);
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromForm] UpdateUserRequestDto dto)
        {
            var result = await _adminService.UpdateProfile(UserId, dto);
            return Ok(result);
        }

        [HttpGet("bank-user")]
        public async Task<IActionResult> GetBankUser()
        {
            var result = await _adminService.GetBankUser(UserId);
            return Ok(result);
        }

        [HttpPost("bank-user")]
        public async Task<IActionResult> CreateBankUser([FromBody] BankUserRequestDto dto)
        {
            var result = await _adminService.CreateBankUser(UserId, RefCode, dto);
            return Ok(result);
        }

        [HttpPut("bank-user/{id}")]
        public async Task<IActionResult> UpdateBankUser(long id, [FromBody] BankUserRequestDto dto)
        {
            var result = await _adminService.UpdateBankUser(UserId, id, dto);
            return Ok(result);
        }

        [HttpGet("banks")]
        public async Task<IActionResult> GetBanks()
        {
            var result = await _adminService.GetBanks();
            return Ok(result);
        }
    }
}
