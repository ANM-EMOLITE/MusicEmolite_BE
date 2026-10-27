using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS_Application.DataTransferObjects.Artist;
using MS_Application.DataTransferObjects.Base;
using MS_Application.Services.Interfaces;

namespace MS_API.Controllers
{
    [ApiController]
    [Route("api/artists")]
    public class ArtistController : BaseController
    {
        private readonly IArtistService _artistsService;

        public ArtistController(IArtistService artistsService)
        {
            _artistsService = artistsService;
        }

        [AllowAnonymous]
        [HttpPost("search")]
        public async Task<IActionResult> GetArtists([FromBody] BaseSearchDto<ArtistRequestDto> dto)
        {
            var result = await _artistsService.GetArtists(dto);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetArtistById(long id)
        {
            var result = await _artistsService.GetArtistById(id);
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateArtist([FromBody] ArtistCreateDto dto)
        {
            var result = await _artistsService.CreateArtist(dto, UserId);
            return Ok(result);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateArtist(long id, [FromBody] ArtistUpdateDto dto)
        {
            var result = await _artistsService.UpdateArtist(id, dto, UserId);
            return Ok(result);
        }
    }
}
