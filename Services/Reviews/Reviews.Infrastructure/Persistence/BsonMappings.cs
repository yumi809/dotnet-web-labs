using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using Reviews.Domain.Common;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;

namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// BSON-мапінг без атрибутів у домені: конвенція camelCase, мапінг приватних полів колекцій,
/// створення Value Objects через конструктор. Реєструється один раз при старті.
/// </summary>
public static class BsonMappings
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;

        ConventionRegistry.Register("reviews-conventions", new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true),
            new EnumRepresentationConvention(BsonType.String)
        }, _ => true);

        BsonSerializer.TryRegisterSerializer(new DecimalSerializer(BsonType.Decimal128));

        BsonClassMap.TryRegisterClassMap<BaseEntity>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(e => e.Id);
            cm.SetIsRootClass(true);
        });

        BsonClassMap.TryRegisterClassMap<Rating>(cm =>
        {
            cm.MapMember(r => r.Value);
            cm.MapCreator(r => new Rating(r.Value));
        });
        BsonClassMap.TryRegisterClassMap<AuthorInfo>(cm =>
        {
            cm.MapMember(a => a.UserId);
            cm.MapMember(a => a.DisplayName);
            cm.MapCreator(a => new AuthorInfo(a.UserId, a.DisplayName));
        });
        BsonClassMap.TryRegisterClassMap<ProductRef>(cm =>
        {
            cm.MapMember(p => p.ProductId);
            cm.MapMember(p => p.ProductName);
            cm.MapCreator(p => new ProductRef(p.ProductId, p.ProductName));
        });

        BsonClassMap.TryRegisterClassMap<Review>(cm =>
        {
            cm.AutoMap();
            cm.UnmapMember(r => r.Comments);
            cm.UnmapMember(r => r.Pros);
            cm.UnmapMember(r => r.Cons);
            // приватні поля-колекції зберігаються як масиви
            cm.MapField("_comments").SetElementName("comments");
            cm.MapField("_pros").SetElementName("pros");
            cm.MapField("_cons").SetElementName("cons");
        });
        BsonClassMap.TryRegisterClassMap<Comment>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.Id);
        });

        BsonClassMap.TryRegisterClassMap<Discussion>(cm =>
        {
            cm.AutoMap();
            cm.UnmapMember(d => d.RecentMessages);
            cm.MapField("_recentMessages").SetElementName("recentMessages");
        });
        BsonClassMap.TryRegisterClassMap<DiscussionMessage>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(m => m.Id);
        });
    }
}
