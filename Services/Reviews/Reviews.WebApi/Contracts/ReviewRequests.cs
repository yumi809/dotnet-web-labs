using System.ComponentModel.DataAnnotations;
using AutoMapper;
using Reviews.Application.Commands;

namespace Reviews.WebApi.Contracts;

public sealed class CreateReviewRequest
{
    [Range(1, long.MaxValue)] public long ProductId { get; set; }
    [Required, StringLength(200)] public string ProductName { get; set; } = string.Empty;
    /// <summary>Ігнорується: автор береться з токена.</summary>
    [StringLength(64)] public string AuthorId { get; set; } = string.Empty;
    [StringLength(100)] public string AuthorName { get; set; } = string.Empty;
    [Range(1, 5)] public int Rating { get; set; }
    [Required] public string Type { get; set; } = "text";
    [Required, StringLength(150)] public string Title { get; set; } = string.Empty;
    [StringLength(4000)] public string? Body { get; set; }
    public List<string>? Pros { get; set; }
    public List<string>? Cons { get; set; }
    [Url] public string? VideoUrl { get; set; }
}

public sealed class UpdateReviewRequest
{
    /// <summary>Ігнорується: береться з токена.</summary>
    public string RequestedBy { get; set; } = string.Empty;
    [Range(1, 5)] public int Rating { get; set; }
    [Required, StringLength(150)] public string Title { get; set; } = string.Empty;
    [StringLength(4000)] public string? Body { get; set; }
    public List<string>? Pros { get; set; }
    public List<string>? Cons { get; set; }
    public string? VideoUrl { get; set; }
    /// <summary>Очікувана версія документа (оптимістична конкурентність).</summary>
    [Range(1, int.MaxValue)] public int Version { get; set; }
}

public sealed class AddCommentRequest
{
    public string AuthorId { get; set; } = string.Empty;
    [StringLength(100)] public string AuthorName { get; set; } = string.Empty;
    [Required, StringLength(1000)] public string Text { get; set; } = string.Empty;
}

/// <summary>Мапінг HTTP-контрактів у команди CQRS.</summary>
public sealed class RequestMappingProfile : Profile
{
    public RequestMappingProfile()
    {
        CreateMap<CreateReviewRequest, CreateReviewCommand>();
        // Id береться з маршруту: контролер доповнює команду через `with { Id = ... }`
        CreateMap<UpdateReviewRequest, UpdateReviewCommand>()
            .ForCtorParam(nameof(UpdateReviewCommand.Id), o => o.MapFrom(_ => string.Empty))
            .ForCtorParam(nameof(UpdateReviewCommand.ExpectedVersion), o => o.MapFrom(s => s.Version));
        CreateMap<AddCommentRequest, AddCommentCommand>()
            .ForCtorParam(nameof(AddCommentCommand.ReviewId), o => o.MapFrom(_ => string.Empty));
    }
}
