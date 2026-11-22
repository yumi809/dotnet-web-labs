using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

public static class ReviewTypes
{
    public const string Text = "text";
    public const string ProsCons = "pros-cons";
    public const string Video = "video";
    public static readonly string[] All = [Text, ProsCons, Video];
}

/// <summary>Агрегат «Відгук»: VO вбудовані, коментарі — вкладена колекція (embedding).</summary>
public sealed class Review : BaseEntity
{
    public const int MaxComments = 200;

    private readonly List<Comment> _comments = [];
    private readonly List<string> _pros = [];
    private readonly List<string> _cons = [];

    public ProductRef Product { get; private set; } = null!;
    public AuthorInfo Author { get; private set; } = null!;
    public Rating Rating { get; private set; } = null!;
    public string Type { get; private set; } = ReviewTypes.Text;
    public string Title { get; private set; } = null!;
    public string? Body { get; private set; }
    public string? VideoUrl { get; private set; }
    public int HelpfulVotes { get; private set; }

    public IReadOnlyCollection<Comment> Comments => _comments.AsReadOnly();
    public IReadOnlyCollection<string> Pros => _pros.AsReadOnly();
    public IReadOnlyCollection<string> Cons => _cons.AsReadOnly();

    private Review() { } // для BsonClassMap

    public Review(ProductRef product, AuthorInfo author, Rating rating, string type, string title, string? body,
                  IEnumerable<string>? pros = null, IEnumerable<string>? cons = null, string? videoUrl = null)
    {
        Product = product ?? throw new DomainException("Product is required");
        Author = author ?? throw new DomainException("Author is required");
        Apply(rating, type, title, body, pros, cons, videoUrl);
    }

    public void Edit(Rating rating, string title, string? body, IEnumerable<string>? pros, IEnumerable<string>? cons, string? videoUrl)
    {
        Apply(rating, Type, title, body, pros, cons, videoUrl);
        Touch();
    }

    public Comment AddComment(AuthorInfo author, string text)
    {
        if (_comments.Count >= MaxComments) throw new DomainException($"Review cannot have more than {MaxComments} comments");
        var comment = new Comment(author, text);
        _comments.Add(comment);
        Touch();
        return comment;
    }

    public void RemoveComment(ObjectId commentId, string requestedBy)
    {
        var comment = _comments.FirstOrDefault(c => c.Id == commentId) ?? throw new DomainException($"Comment {commentId} not found");
        if (comment.Author.UserId != requestedBy && Author.UserId != requestedBy)
            throw new DomainException("Only the comment author or review author can remove the comment");
        _comments.Remove(comment);
        Touch();
    }

    public void MarkHelpful() { HelpfulVotes++; Touch(); }

    public void RenameProduct(string newName) { Product = Product.WithName(newName); Touch(); }

    private void Apply(Rating rating, string type, string title, string? body, IEnumerable<string>? pros, IEnumerable<string>? cons, string? videoUrl)
    {
        if (!ReviewTypes.All.Contains(type)) throw new DomainException($"Unknown review type '{type}'");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 150) throw new DomainException("Title must be 1..150 characters");
        if (body is { Length: > 4000 }) throw new DomainException("Body must be at most 4000 characters");

        var prosList = pros?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
        var consList = cons?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? [];
        switch (type)
        {
            case ReviewTypes.Text when string.IsNullOrWhiteSpace(body):
                throw new DomainException("Text review requires a body");
            case ReviewTypes.ProsCons when prosList.Count == 0 && consList.Count == 0:
                throw new DomainException("Pros/cons review requires at least one pro or con");
            case ReviewTypes.Video when !Uri.IsWellFormedUriString(videoUrl, UriKind.Absolute):
                throw new DomainException("Video review requires a valid videoUrl");
        }

        Rating = rating ?? throw new DomainException("Rating is required");
        Type = type;
        Title = title.Trim();
        Body = body;
        VideoUrl = type == ReviewTypes.Video ? videoUrl : null;
        _pros.Clear(); _pros.AddRange(prosList);
        _cons.Clear(); _cons.AddRange(consList);
    }
}

/// <summary>Коментар — вкладена сутність агрегату Review.</summary>
public sealed class Comment
{
    public ObjectId Id { get; private set; } = ObjectId.GenerateNewId();
    public AuthorInfo Author { get; private set; } = null!;
    public string Text { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private Comment() { }

    internal Comment(AuthorInfo author, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000) throw new DomainException("Comment must be 1..1000 characters");
        Author = author;
        Text = text.Trim();
    }
}
