
using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS_API.Extensions;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Lyrics;
using MS_Application.DataTransferObjects.Songs;
using MS_Application.DataTransferObjects.Youtube;
using MS_Application.External;
using MS_Application.Services;
using MS_Application.Services.Interfaces;
using MS_Application.Services.Interfaces.External;

namespace MS_API.Controllers
{
    [Route("api/songs")]
    [ApiController]
    public class SongsController : BaseController
    {
        private readonly ILyricsService _lyricsService;
        private readonly IYoutubeAPIService _youtubeAPIService;
        private readonly ISongsService _songsService;

        public SongsController(ISongsService songsService, ILyricsService lyricsService, IYoutubeAPIService youtubeAPIService)
        {
            _songsService = songsService;
            _lyricsService = lyricsService;
            _youtubeAPIService = youtubeAPIService;
        }

        [HttpPost("search")]
        public async Task<IActionResult> GetSongs(BaseSearchDto<SongRequestDto> dto)
        {
            var result = await _songsService.GetSongs(dto, UserId);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpPost("public/search")]
        public async Task<IActionResult> GetPublicSongs([FromBody] BaseSearchDto<SongRequestDto> dto)
        {
            var result = await _songsService.GetPublicSongs(dto, UserId);

            return Ok(result);
        }

        [AllowAnonymous]
        [HttpPost("public/trending")]
        public async Task<IActionResult> GetTrendingSongs([FromBody] BaseSearchDto<SongRequestDto> dto)
        {
            var result = await _songsService.GetTrendingSongs(dto, UserId);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpPost("public/newest")]
        public async Task<IActionResult> GetNewestSongs([FromBody] BaseSearchDto<SongRequestDto> dto)
        {
            var response = await _songsService.GetNewestSongs(dto, UserId);
            return Ok(response);
        }

        [HttpPost("recent")]
        public async Task<IActionResult> GetRecentSongs([FromBody] BaseSearchDto<SongRequestDto> dto)
        {
            var result = await _songsService.GetRecentSongs(dto, UserId);
            return Ok(result);
        }

        [HttpGet("most-played")]
        public async Task<IActionResult> GetMostPlayedSongs([FromQuery] int top = 20)
        {
            var result = await _songsService.GetMostPlayedSongs(UserId, top);
            return Ok(result);
        }

        // Danh sách "kết hợp" kiểu Mix (Youtube/Spotify) cho bài hay nghe, dùng chung cho web & mobile
        [HttpGet("mixes/most-played")]
        public async Task<IActionResult> GetMostPlayedMix([FromQuery] int top = 20)
        {
            var result = await _songsService.GetMostPlayedMix(UserId, top);
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetDetail(long id)
        {
            var result = await _songsService.GetSongDetail(id, User.GetUserId());
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateSong(SongCreateDto dto)
        {
            var result = await _songsService.CreateSong(dto, User.GetUserId());
            return Ok(result);
        }

        [HttpPost("albums")]
        public async Task<IActionResult> AddSongToAlbum(long songId, long albumId)
        {
            var result = await _songsService.AddSongToAlbum(songId, albumId, User.GetUserId());
            return Ok(result);
        }

        [HttpDelete("albums")]
        public async Task<IActionResult> RemoveSongFromAlbum(long songId, long albumId)
        {
            var result = await _songsService.RemoveSongFromAlbum(songId, albumId, User.GetUserId());
            return Ok(result);
        }

        [HttpPost("{id}/view")]
        public async Task<IActionResult> IncrementView(long id)
        {
            var result = await _songsService.IncrementView(id, UserId);
            return Ok(result);
        }

        [HttpPost("history")]
        public async Task<IActionResult> AddSongHistory([FromBody] AddSongHistoryDto dto)
        {
            var result = await _songsService.AddSongHistory(dto, UserId);

            return Ok(result);
        }

        [HttpPost("{id}/like")]
        public async Task<IActionResult> ToggleLike(long id)
        {
            var result = await _songsService.ToggleLike(id, UserId);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpGet("lyrics")]
        public async Task<IActionResult> GetLyrics([FromQuery] LyricsRequestDto request)
        {
            var result = await _lyricsService.GetLyricsAsync(request);
            if (result.Code == ResponseStatusCode.Status404)
                return NotFound("Lyrics not found");

            if (result.Code == ResponseStatusCode.Status400)
                return BadRequest(result.Message);

            return Ok(result);
        }

        [AllowAnonymous]
        [HttpGet("lyrics/{id}")]
        public async Task<IActionResult> GetLyricsById(int id)
        {
            var response = await _lyricsService.GetLyricsByIdAsync(id);
            return Ok(response);
        }

        [AllowAnonymous]
        [HttpPost("lyrics/search")]
        public async Task<IActionResult> SearchLyrics([FromBody] BaseSearchDto<LyricsSearchRequestDto> request)
        {
            var response = await _lyricsService.SearchLyricsAsync(request);
            return Ok(response);
        }

        [AllowAnonymous]
        [HttpPost("publish")]
        public async Task<IActionResult> PublishLyrics([FromBody] PublishLyricsRequestDto request)
        {
            var result = await _lyricsService.PublishLyricsAsync(request);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpPost("youtube/search")]
        public async Task<IActionResult> SearchYoutube([FromBody] BaseSearchDto<YoutubeSearchRequestDto> request)
        {
            var result = await _youtubeAPIService.SearchAsync(request, UserId);

            return Ok(result);
        }
    }
}
