using System.ComponentModel.DataAnnotations;
using app.repositories;
using app.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace app.Controllers;

[Authorize]
[Route("users")]
[ApiController]
public class UsersController : ControllerBase
{
    private readonly UserRepository _repository;
    private readonly AuthService _authService;

    public UsersController(UserRepository repository, AuthService authService)
    {
        _repository = repository;
        _authService = authService;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create([FromBody] UserDto dto)
    {
        try
        {
            var user = new User
            {
                Username = dto.Username,
                Password = _authService.HashPassword(dto.Password),
                DisplayName = "User",
                AvatarUrl = "https://static-00.iconduck.com/assets.00/avatar-icon-2048x2048-ilrgk6vk.png"
            };

            await _repository.Create(user);
            return Ok(new { message = "Usuario registrado correctamente." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("me/profile")]
    public async Task<IActionResult> SaveProfile([FromBody] UserProfile profile)
    {
        var userIdClaim = HttpContext.User.FindFirst("UserId");
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return Unauthorized();

        await _repository.SaveUserProfile(userId, profile);
        return Ok();
    }

    [HttpGet("me/profile")]
    public async Task<ActionResult<UserProfile>> GetProfile()
    {
        var userIdClaim = HttpContext.User.FindFirst("UserId");
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return Unauthorized();

        var profile = await _repository.GetUserProfile(userId);
        return profile == null ? NotFound() : Ok(profile);
    }

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteCurrentUser()
    {
        var userIdClaim = HttpContext.User.FindFirst("UserId");
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return Unauthorized(new { message = "Usuario no autenticado o inválido." });

        await _repository.Delete(userId);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        foreach (var cookie in Request.Cookies.Keys)
            Response.Cookies.Delete(cookie);

        return Ok(new { message = "Usuario eliminado correctamente." });
    }

    public class UserDto
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }
}
