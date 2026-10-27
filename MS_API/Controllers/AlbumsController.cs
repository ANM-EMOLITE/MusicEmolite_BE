using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Albums;
using MS_Application.Services.Interfaces;

namespace MS_API.Controllers
{
    [ApiController]
    [Route("api/albums")]
    public class AlbumsController : BaseController
    {
        private readonly IAlbumsService _albumsService;

        public AlbumsController(IAlbumsService albumsService)
        {
            _albumsService = albumsService;
        }

        [HttpPost("search")]
        public async Task<IActionResult> GetAlbums([FromBody] BaseSearchDto<AlbumRequestDto> dto)
        {
            var result = await _albumsService.GetAlbums(dto, UserId);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpPost("public/search")]
        public async Task<IActionResult> GetPublicAlbums([FromBody] BaseSearchDto<AlbumRequestDto> dto)
        {
            var result = await _albumsService.GetPublicAlbums(dto);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAlbumById(long id)
        {
            var result = await _albumsService.GetAlbumById(id);
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateAlbum([FromForm] AlbumCreateDto dto)
        {
            var result = await _albumsService.CreateAlbum(dto, UserId);
            return Ok(result);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAlbum(long id, [FromBody] AlbumUpdateDto dto)
        {
            var result = await _albumsService.UpdateAlbum(id, dto, UserId);
            return Ok(result);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAlbum(long id)
        {
            var result = await _albumsService.DeleteAlbum(id, UserId);
            return Ok(result);
        }
    }
}