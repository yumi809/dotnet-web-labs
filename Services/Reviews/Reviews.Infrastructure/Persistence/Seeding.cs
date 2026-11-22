using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;

namespace Reviews.Infrastructure.Persistence;

public interface IDataSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

/// <summary>Ідемпотентний сідінг: InsertMany лише якщо колекція порожня.</summary>
public sealed class ReviewsDataSeeder(MongoDbContext context, ILogger<ReviewsDataSeeder> logger) : IDataSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await context.Reviews.Find(FilterDefinition<Review>.Empty).AnyAsync(ct))
        {
            logger.LogInformation("Reviews collection already contains data, seeding skipped");
            return;
        }

        var olena = new AuthorInfo("u-olena", "Олена К.");
        var taras = new AuthorInfo("u-taras", "Тарас Б.");
        var iryna = new AuthorInfo("u-iryna", "Ірина М.");

        var r1 = new Review(new ProductRef(1, "Кобзар"), olena, new Rating(5), ReviewTypes.Text, "Must have", "Чудове видання, якісний папір.");
        r1.AddComment(taras, "Погоджуюсь!");
        var r2 = new Review(new ProductRef(3, "Clean Code"), taras, new Rating(4), ReviewTypes.ProsCons, "Класика для розробника", null,
            ["Практичні приклади", "Зрозумілі правила"], ["Приклади на Java"]);
        var r3 = new Review(new ProductRef(4, "Designing Data-Intensive Applications"), iryna, new Rating(5), ReviewTypes.Video,
            "Відеоогляд", null, videoUrl: "https://example.com/video/ddia-review");

        await context.Reviews.InsertManyAsync([r1, r2, r3], cancellationToken: ct);

        var discussion = new Discussion(new ProductRef(3, "Clean Code"), "Чи актуальна книга у 2026?");
        var m1 = discussion.Post(olena, "Так, принципи вічні");
        var m2 = discussion.Post(taras, "Частково");
        await context.Discussions.InsertOneAsync(discussion, cancellationToken: ct);
        await context.Messages.InsertManyAsync([m1, m2], cancellationToken: ct);

        logger.LogInformation("Reviews database seeded with {Count} reviews", 3);
    }
}

/// <summary>Створює індекси при старті: складений, унікальний, текстовий (createIndex ідемпотентний).</summary>
public sealed class MongoIndexInitializer(
    MongoDbContext context,
    IDataSeeder seeder,
    IOptions<MongoDbSettings> settings,
    ILogger<MongoIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var keys = Builders<Review>.IndexKeys;
        await context.Reviews.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Review>(keys.Ascending("product.productId").Descending("createdAt"),
                new CreateIndexOptions { Name = "ix_reviews_product_created" }),
            new CreateIndexModel<Review>(keys.Ascending("product.productId").Ascending("author.userId"),
                new CreateIndexOptions { Name = "ux_reviews_product_author", Unique = true }),
            new CreateIndexModel<Review>(keys.Text("title").Text("body"),
                new CreateIndexOptions { Name = "tx_reviews_text" })
        ], ct);

        await context.Discussions.Indexes.CreateOneAsync(new CreateIndexModel<Discussion>(
            Builders<Discussion>.IndexKeys.Ascending("product.productId").Descending("lastActivityAt"),
            new CreateIndexOptions { Name = "ix_discussions_product_activity" }), cancellationToken: ct);

        await context.Messages.Indexes.CreateOneAsync(new CreateIndexModel<DiscussionMessage>(
            Builders<DiscussionMessage>.IndexKeys.Ascending("discussionId").Ascending("createdAt"),
            new CreateIndexOptions { Name = "ix_messages_discussion_created" }), cancellationToken: ct);

        logger.LogInformation("MongoDB indexes ensured");
        if (settings.Value.SeedData) await seeder.SeedAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
