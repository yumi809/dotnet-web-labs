using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.IdentityServer.Data;

namespace Platform.IdentityServer;

/// <summary>Міграції трьох контекстів (Identity, Configuration, PersistedGrant) і ідемпотентний сідінг.</summary>
public static class SeedData
{
    public static async Task EnsureSeedDataAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<PersistedGrantDbContext>().Database.MigrateAsync();
        var config = sp.GetRequiredService<ConfigurationDbContext>();
        await config.Database.MigrateAsync();

        foreach (var client in Config.Clients(app.Configuration))
            if (!await config.Clients.AnyAsync(c => c.ClientId == client.ClientId)) config.Clients.Add(client.ToEntity());
        foreach (var resource in Config.IdentityResources)
            if (!await config.IdentityResources.AnyAsync(r => r.Name == resource.Name)) config.IdentityResources.Add(resource.ToEntity());
        foreach (var scopeItem in Config.ApiScopes)
            if (!await config.ApiScopes.AnyAsync(s => s.Name == scopeItem.Name)) config.ApiScopes.Add(scopeItem.ToEntity());
        foreach (var api in Config.ApiResources)
            if (!await config.ApiResources.AnyAsync(a => a.Name == api.Name)) config.ApiResources.Add(api.ToEntity());
        await config.SaveChangesAsync();

        // Тестові користувачі (паролі — з конфігурації/секретів AppHost)
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "admin", "user" })
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var password = app.Configuration["Seed:UserPassword"];
        if (string.IsNullOrEmpty(password)) return;
        foreach (var (name, role) in new[] { ("alice", "admin"), ("bob", "user") })
        {
            if (await users.FindByNameAsync(name) is not null) continue;
            var user = new ApplicationUser { UserName = name, Email = $"{name}@bookhub.local", EmailConfirmed = true, FullName = name };
            if ((await users.CreateAsync(user, password)).Succeeded) await users.AddToRoleAsync(user, role);
        }
    }
}
