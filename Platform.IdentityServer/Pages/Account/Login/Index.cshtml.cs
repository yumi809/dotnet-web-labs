using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Platform.IdentityServer.Data;

namespace Platform.IdentityServer.Pages.Account.Login;

/// <summary>Спрощена сторінка входу (за мотивами Quickstart UI): ASP.NET Identity + повернення в authorize-flow.</summary>
[AllowAnonymous]
public sealed class IndexModel(
    SignInManager<ApplicationUser> signIn,
    IIdentityServerInteractionService interaction) : PageModel
{
    [BindProperty] public LoginInput Input { get; set; } = new();
    public string? Error { get; private set; }

    public void OnGet(string? returnUrl) => Input.ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync()
    {
        var context = await interaction.GetAuthorizationContextAsync(Input.ReturnUrl, HttpContext.RequestAborted);
        var result = await signIn.PasswordSignInAsync(Input.UserName, Input.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            Error = result.IsLockedOut ? "Обліковий запис тимчасово заблоковано" : "Невірний логін або пароль";
            return Page();
        }

        if (context is not null || interaction.IsValidReturnUrl(Input.ReturnUrl)) return Redirect(Input.ReturnUrl!);
        return Url.IsLocalUrl(Input.ReturnUrl) ? Redirect(Input.ReturnUrl!) : Redirect("~/");
    }

    public sealed class LoginInput
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? ReturnUrl { get; set; }
    }
}
