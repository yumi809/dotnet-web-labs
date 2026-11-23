using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auth.Api.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

/// <summary>Refresh-токен у БД: зберігається лише SHA-256 хеш, дати видачі/закінчення, відкликання і ротація.</summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(u => u.Property(x => x.FullName).HasMaxLength(200));
        builder.Entity<RefreshToken>(t =>
        {
            t.ToTable("RefreshTokens");
            t.HasKey(x => x.Id);
            t.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            t.HasIndex(x => x.TokenHash).IsUnique();
            t.Property(x => x.RevokedReason).HasMaxLength(200);
            t.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            t.Property(x => x.CreatedByIp).HasMaxLength(64);
            t.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            t.Ignore(x => x.IsActive);
        });
    }
}

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=IdentityDb;User Id=sa;Password=Your_strong_password1;TrustServerCertificate=True")
            .Options);
}
