using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence;

/// <summary>Секція MongoDb в appsettings.</summary>
public sealed class MongoDbSettings
{
    public const string SectionName = "MongoDb";
    public const string ConnectionStringName = "ReviewsDb";
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "ReviewsDb";
    public bool SeedData { get; set; } = true;
}

/// <summary>Контекст MongoDB: налаштування через IOptions, доступ до типізованих колекцій.</summary>
public sealed class MongoDbContext
{
    public const string ReviewsCollection = "reviews";
    public const string DiscussionsCollection = "discussions";
    public const string MessagesCollection = "discussionMessages";

    public MongoDbContext(IMongoClient client, IOptions<MongoDbSettings> options)
    {
        BsonMappings.Register(); // ідемпотентно, виконується один раз
        Database = client.GetDatabase(options.Value.DatabaseName);
    }

    public IMongoDatabase Database { get; }
    public IMongoCollection<Review> Reviews => Database.GetCollection<Review>(ReviewsCollection);
    public IMongoCollection<Discussion> Discussions => Database.GetCollection<Discussion>(DiscussionsCollection);
    public IMongoCollection<DiscussionMessage> Messages => Database.GetCollection<DiscussionMessage>(MessagesCollection);
}
