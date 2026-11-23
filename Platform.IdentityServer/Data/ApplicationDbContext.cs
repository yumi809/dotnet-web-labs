using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Platform.IdentityServer.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
}

/// <summary>Користувачі IdentityServer (ASP.NET Identity) в окремій базі IdentityServerDb.</summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options);
