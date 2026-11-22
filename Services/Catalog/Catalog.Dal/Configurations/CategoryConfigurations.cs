using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever(); // фіксовані Id довідника
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Slug).IsUnique().HasDatabaseName("UQ_Categories_Slug");
        builder.Navigation(c => c.Products).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Довідникові дані з фіксованими Id — частина міграції
        builder.HasData(
            new { Id = 1, Name = "Художня література", Slug = "fiction" },
            new { Id = 2, Name = "Українська класика", Slug = "ua-classics" },
            new { Id = 3, Name = "Програмування", Slug = "programming" },
            new { Id = 4, Name = "Архітектура ПЗ", Slug = "software-architecture" },
            new { Id = 5, Name = "Поезія", Slug = "poetry" });
    }
}

public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategories");
        builder.HasKey(pc => new { pc.ProductId, pc.CategoryId }); // складений ключ
        builder.Property(pc => pc.AddedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(pc => pc.IsPrimary).HasDefaultValue(false);
        builder.HasIndex(pc => new { pc.CategoryId, pc.AddedAt }).HasDatabaseName("IX_ProductCategories_CategoryId_AddedAt");

        builder.HasOne(pc => pc.Product).WithMany(p => p.Categories)
            .HasForeignKey(pc => pc.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(pc => pc.Category).WithMany(c => c.Products)
            .HasForeignKey(pc => pc.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PublisherConfiguration : IEntityTypeConfiguration<Publisher>
{
    public void Configure(EntityTypeBuilder<Publisher> builder)
    {
        builder.ToTable("Publishers");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Country).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Website).HasMaxLength(200);
        builder.HasIndex(p => p.Name).IsUnique().HasDatabaseName("UQ_Publishers_Name");
        builder.Navigation(p => p.Products).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
