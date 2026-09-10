using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.User;
using MS_Application.DataTransferObjects.User.BankUser;
using MS_Application.Services.Interfaces;

namespace MS_API.Controllers
{
    [Route("api/users")]
    [ApiController]
    public class UsersController : BaseController
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPost]
        public async Task<IActionResult> GetUsers([FromBody] BaseSearchDto<CrmUserRequestDto> dto)
        {
            var result = await _userService.GetUsers(dto);
            return Ok(result);
        }

        [HttpPost("profile")]
        public async Task<IActionResult> GetUserProfile()
        {
            var result = await _userService.GetUserProfile(UserId);
            return Ok(result);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateUser([FromForm] UpdateUserRequestDto dto)
        {
            var result = await _userService.UpdateUser(UserId, dto);
            return Ok(result);
        }

        [HttpGet("bank-user")]
        public async Task<IActionResult> GetBankUser()
        {
            var result = await _userService.GetBankUser(UserId);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpGet("bank-user/admin")]
        public async Task<IActionResult> GetAdminBankUsers()
        {
            var result = await _userService.GetAdminBankUsers();
            return Ok(result);
        }

        [HttpPost("bank-user/add")]
        public async Task<IActionResult> CreateBankUser([FromBody] BankUserRequestDto dto)
        {
            var result = await _userService.CreateBankUser(UserId, RefCode, dto);
            return Ok(result);
        }

        [HttpPut("{id}/bank-user")]
        public async Task<IActionResult> UpdateBankUser(long id, [FromBody] BankUserRequestDto dto)
        {
            var result = await _userService.UpdateBankUser(UserId, id, dto);
            return Ok(result);
        }

        [HttpDelete("{id}/bank-user")]
        public async Task<IActionResult> DeleteBankUser(long id)
        {
            var result = await _userService.DeleteBankUser(UserId, id);
            return Ok(result);
        }
    }
}
