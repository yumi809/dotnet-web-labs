using Reviews.Domain.Common;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>
/// Обговорення товару — гібридна модель: останні 3 повідомлення вбудовані (RecentMessages),
/// повна історія — окрема колекція discussionMessages (посилання за discussionId).
/// </summary>
public sealed class Discussion : BaseEntity
{
    public const int RecentLimit = 3;
    private readonly List<DiscussionMessage> _recentMessages = [];

    public ProductRef Product { get; private set; } = null!;
    public string Topic { get; private set; } = null!;
    public int MessagesCount { get; private set; }
    public DateTime LastActivityAt { get; private set; }
    public IReadOnlyCollection<DiscussionMessage> RecentMessages => _recentMessages.AsReadOnly();

    private Discussion() { }

    public Discussion(ProductRef product, string topic)
    {
        if (string.IsNullOrWhiteSpace(topic) || topic.Length > 200) throw new DomainException("Topic must be 1..200 characters");
        Product = product;
        Topic = topic.Trim();
        LastActivityAt = DateTime.UtcNow;
    }

    public DiscussionMessage Post(AuthorInfo author, string text)
    {
        var message = new DiscussionMessage(Id, author, text);
        _recentMessages.Add(message);
        if (_recentMessages.Count > RecentLimit) _recentMessages.RemoveAt(0);
        MessagesCount++;
        LastActivityAt = message.CreatedAt;
        Touch();
        return message;
    }

    public void RenameProduct(string newName) { Product = Product.WithName(newName); Touch(); }
}

public sealed class DiscussionMessage
{
    public MongoDB.Bson.ObjectId Id { get; private set; } = MongoDB.Bson.ObjectId.GenerateNewId();
    public MongoDB.Bson.ObjectId DiscussionId { get; private set; }
    public AuthorInfo Author { get; private set; } = null!;
    public string Text { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private DiscussionMessage() { }

    internal DiscussionMessage(MongoDB.Bson.ObjectId discussionId, AuthorInfo author, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new DomainException("Message must be 1..2000 characters");
        DiscussionId = discussionId;
        Author = author;
        Text = text.Trim();
    }
}
