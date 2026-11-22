using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", t =>
        {
            t.HasCheckConstraint("CK_Products_Price", "[Price] > 0");
            t.HasCheckConstraint("CK_Products_Stock", "[StockQuantity] >= 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).UseIdentityColumn();
        builder.Property(p => p.Sku).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Author).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Price).HasColumnType("decimal(18,2)");
        builder.Property(p => p.StockQuantity).HasDefaultValue(0);
        builder.Property(p => p.IsActive).HasDefaultValue(true);
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.Sku).IsUnique().HasDatabaseName("UQ_Products_Sku");
        builder.HasIndex(p => p.Name).HasDatabaseName("IX_Products_Name");
        builder.HasIndex(p => new { p.IsActive, p.Price }).HasDatabaseName("IX_Products_IsActive_Price");

        // 1:N Publisher → Products
        builder.HasOne(p => p.Publisher)
            .WithMany(pub => pub.Products)
            .HasForeignKey(p => p.PublisherId)
            .OnDelete(DeleteBehavior.Restrict);

        // 1:1 Product → ProductDetail (FK = PK)
        builder.HasOne(p => p.Detail)
            .WithOne()
            .HasForeignKey<ProductDetail>(d => d.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Categories).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ProductDetailConfiguration : IEntityTypeConfiguration<ProductDetail>
{
    public void Configure(EntityTypeBuilder<ProductDetail> builder)
    {
        builder.ToTable("ProductDetails", t =>
        {
            t.HasCheckConstraint("CK_ProductDetails_Pages", "[Pages] > 0");
            t.HasCheckConstraint("CK_ProductDetails_Year", "[PublishedYear] BETWEEN 1450 AND 2100");
        });
        builder.HasKey(d => d.ProductId);
        builder.Property(d => d.Isbn).HasMaxLength(17).IsRequired();
        builder.Property(d => d.Language).HasMaxLength(30).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(2000);
        builder.HasIndex(d => d.Isbn).IsUnique().HasDatabaseName("UQ_ProductDetails_Isbn");
    }
}
