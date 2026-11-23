using System.Security.Claims;
using Auth.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Controllers;

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
    public static readonly string[] All = [User, Admin];
}

public sealed record UserDto(string Id, string UserName, string Email, string FullName, bool EmailConfirmed, IList<string> Roles);
public sealed record UpdateProfileRequest(string FullName);

/// <summary>/users: адміністративні ендпоінти — [Authorize(Roles = "Admin")]; owner-ендпоінти звіряють userId з токена.</summary>
[ApiController]
[Route("users")]
[Authorize]
public sealed class UsersController(UserManager<ApplicationUser> users) : ControllerBase
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!;

    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken ct)
    {
        var list = await users.Users.OrderBy(u => u.UserName).Take(200).ToListAsync(ct);
        var result = new List<UserDto>();
        foreach (var u in list) result.Add(await MapAsync(u));
        return result;
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me() =>
        await users.FindByIdAsync(CurrentUserId) is { } u ? await MapAsync(u) : NotFound();

    /// <summary>Власник або адміністратор.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetById(string id)
    {
        if (id != CurrentUserId && !User.IsInRole(Roles.Admin)) return Forbid();
        return await users.FindByIdAsync(id) is { } u ? await MapAsync(u) : NotFound();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProfile(string id, UpdateProfileRequest request)
    {
        if (id != CurrentUserId) return Forbid();   // профіль змінює лише власник
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        user.FullName = request.FullName;
        await users.UpdateAsync(user);
        return NoContent();
    }

    [HttpPost("{id}/roles/{role}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AddRole(string id, string role)
    {
        if (!Roles.All.Contains(role)) return BadRequest();
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        await users.AddToRoleAsync(user, role);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        await users.DeleteAsync(user);
        return NoContent();
    }

    private async Task<UserDto> MapAsync(ApplicationUser u) =>
        new(u.Id, u.UserName!, u.Email!, u.FullName, u.EmailConfirmed, await users.GetRolesAsync(u));
}
