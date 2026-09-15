using Microsoft.AspNetCore.Mvc;
using MS_Application.External;
using MS_Application.Services.Interfaces.External;

namespace MS_API.Controllers
{
    [ApiController]
    [Route("api/cloudinary")]
    public class CloudinaryController : ControllerBase
    {
        private readonly ICloudinaryService _cloudinaryService;

        public CloudinaryController(ICloudinaryService cloudinaryService)
        {
            _cloudinaryService = cloudinaryService;
        }

        [HttpPost("upload-video")]
        public async Task<IActionResult> UploadVideo(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is required");
            }

            var result = await _cloudinaryService.UploadVideoAsync(file);

            return Ok(result);
        }

        [HttpPost("upload-audio")]
        public async Task<IActionResult> UploadAudio(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is required");
            }

            var result = await _cloudinaryService.UploadAudioAsync(file);

            return Ok(result);
        }

        [HttpPost("upload-profile-image")]
        public async Task<IActionResult> UploadProfileImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is required");
            }

            var result = await _cloudinaryService.UploadProfileImageAsync(file);

            return Ok(result);
        }

        [HttpPost("upload-music-image")]
        public async Task<IActionResult> UploadMusicImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is required");
            }

            var result = await _cloudinaryService.UploadMusicImageAsync(file);

            return Ok(result);
        }

        [HttpPost("upload-chat-image")]
        public async Task<IActionResult> UploadChatImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is required");
            }

            var result = await _cloudinaryService.UploadChatImageAsync(file);

            return Ok(result);
        }
    }
}