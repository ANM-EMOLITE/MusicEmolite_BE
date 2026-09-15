using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MS_API.Controllers
{
    [Authorize]
    public class BaseController : ControllerBase
    {
        protected long UserId => long.Parse(User.FindFirst("UserId")?.Value ?? "0");

        protected string Username => User.FindFirst(ClaimTypes.Name)?.Value ?? "";

        protected string RoleCode => User.FindFirst(ClaimTypes.Role)?.Value ?? "";

        protected string Email => User.FindFirst(ClaimTypes.Email)?.Value ?? "";

        protected string RefCode => User.FindFirst("RefCode")?.Value ?? "";
    }
}
