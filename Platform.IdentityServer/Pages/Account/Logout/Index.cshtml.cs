using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Platform.IdentityServer.Data;

namespace Platform.IdentityServer.Pages.Account.Logout;

public sealed class IndexModel(SignInManager<ApplicationUser> signIn, IIdentityServerInteractionService interaction) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? logoutId)
    {
        await signIn.SignOutAsync();
        var context = await interaction.GetLogoutContextAsync(logoutId, HttpContext.RequestAborted);
        return string.IsNullOrEmpty(context?.PostLogoutRedirectUri) ? Page() : Redirect(context.PostLogoutRedirectUri);
    }
}
