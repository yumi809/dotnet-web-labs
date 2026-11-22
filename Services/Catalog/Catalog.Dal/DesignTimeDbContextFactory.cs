using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Dal;

/// <summary>Фабрика для dotnet ef migrations (без запуску Api).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=CatalogDb;User Id=sa;Password=Your_strong_password1;TrustServerCertificate=True")
            .Options;
        return new CatalogDbContext(options);
    }
}
