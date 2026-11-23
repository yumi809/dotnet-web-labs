using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Auth.Api.Data;
using Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace Auth.Api.Controllers;

public sealed record RegisterRequest([Required, EmailAddress] string Email, [Required, StringLength(50, MinimumLength = 3)] string UserName,
    [Required, StringLength(200)] string FullName, [Required] string Password);
public sealed record LoginRequest([Required] string UserNameOrEmail, [Required] string Password);
public sealed record EmailRequest([Required, EmailAddress] string Email);
public sealed record VerifyEmailRequest([Required] string UserId, [Required] string Token);
public sealed record ResetPasswordRequest([Required, EmailAddress] string Email, [Required] string Token, [Required] string NewPassword);
public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);
public sealed record RevokeRequest(string? RefreshToken);
public sealed record AccessTokenResponse(string AccessToken, DateTime ExpiresAt, string TokenType = "Bearer");

/// <summary>
/// Auth Service: register / login / refresh-token / revoke-token / logout, підтвердження email,
/// відновлення та зміна пароля. Access-токен — у тілі відповіді, refresh — у HttpOnly cookie.
/// </summary>
[ApiController]
[Route("auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    TokenService tokens,
    IAuthEmailSender email,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string RefreshCookie = "bookhub_refresh";
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var user = new ApplicationUser { UserName = request.UserName, Email = request.Email, FullName = request.FullName };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return IdentityProblem(result);

        await users.AddToRoleAsync(user, Roles.User);          // нові користувачі — роль User
        await SendVerificationAsync(user, ct);
        logger.LogInformation("User {UserId} registered", user.Id);
        return CreatedAtAction(nameof(UsersController.GetById), "Users", new { id = user.Id }, new { user.Id, user.UserName, user.Email });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null) return NotFound();
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? NoContent() : IdentityProblem(result);
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(EmailRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is { EmailConfirmed: false }) await SendVerificationAsync(user, ct);
        return Accepted(); // однакова відповідь — без розкриття, чи існує email
    }

    [HttpPost("login")]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = request.UserNameOrEmail.Contains('@')
            ? await users.FindByEmailAsync(request.UserNameOrEmail)
            : await users.FindByNameAsync(request.UserNameOrEmail);
        if (user is null) return Unauthorized(Problem("Invalid credentials"));

        // RequireConfirmedEmail + lockout: login без підтвердження email заблоковано
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut) return Problem(statusCode: 423, title: "Account is locked out, try later");
        if (result.IsNotAllowed) return Problem(statusCode: 403, title: "Email is not confirmed");
        if (!result.Succeeded) return Unauthorized(Problem("Invalid credentials"));

        var pair = await tokens.IssueAsync(user, ClientIp, ct);
        SetRefreshCookie(pair.RefreshToken, pair.RefreshTokenExpiresAt);
        logger.LogInformation("User {UserId} logged in", user.Id);
        return Ok(new AccessTokenResponse(pair.AccessToken, pair.AccessTokenExpiresAt));
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var refresh)) return Unauthorized(Problem("Refresh token is missing"));
        var pair = await tokens.RotateAsync(refresh, ClientIp, ct);
        if (pair is null)
        {
            Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UtcNow));
            return Unauthorized(Problem("Refresh token is invalid or expired"));
        }
        SetRefreshCookie(pair.RefreshToken, pair.RefreshTokenExpiresAt);
        return Ok(new AccessTokenResponse(pair.AccessToken, pair.AccessTokenExpiresAt));
    }

    [Authorize]
    [HttpPost("revoke-token")]
    public async Task<IActionResult> RevokeToken(RevokeRequest request, CancellationToken ct)
    {
        var token = request.RefreshToken ?? Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(token)) return BadRequest(Problem("Token is required"));
        return await tokens.RevokeAsync(token, "revoked by user", ct) ? NoContent() : NotFound(Problem("Token not found or inactive"));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(RefreshCookie, out var token)) await tokens.RevokeAsync(token, "logout", ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UtcNow));
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is { EmailConfirmed: true })
        {
            // одноразовий токен Identity (інвалідовується зміною SecurityStamp після reset)
            var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
            await email.SendAsync(user.Email!, "Reset your BookHub password", $"Reset token: {token}", ct);
        }
        return Accepted();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null) return BadRequest(Problem("Invalid token"));
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded) return IdentityProblem(result);
        await tokens.RevokeAllAsync(user.Id, "password reset", ct);   // відкликання всіх refresh-токенів
        return NoContent();
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        if (user is null) return Unauthorized();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return IdentityProblem(result);
        await tokens.RevokeAllAsync(user.Id, "password changed", ct);
        return NoContent();
    }

    private async Task SendVerificationAsync(ApplicationUser user, CancellationToken ct)
    {
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        await email.SendAsync(user.Email!, "Confirm your BookHub email", $"POST /auth/verify-email {{ userId: {user.Id}, token: {token} }}", ct);
    }

    private void SetRefreshCookie(string token, DateTime expires) =>
        Response.Cookies.Append(RefreshCookie, token, CookieOptions(expires));

    private static CookieOptions CookieOptions(DateTime expires) => new()
    {
        HttpOnly = true,                  // недоступний JavaScript
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/auth",
        Expires = expires
    };

    private ProblemDetails Problem(string title) => new() { Title = title, Status = StatusCodes.Status401Unauthorized };

    private IActionResult IdentityProblem(IdentityResult result)
    {
        foreach (var e in result.Errors) ModelState.AddModelError(e.Code, e.Description);
        return ValidationProblem(ModelState);
    }
}
