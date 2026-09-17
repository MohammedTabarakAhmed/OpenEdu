using FluentValidation;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Domain.Content;

namespace OpenCampus.Lms.Application.Content;

// ----- Requests (API-02, SEC-18) -----

public sealed record CreateCourseContentRequest(string TitleEn, string TitleAr, int? SortOrder);

public sealed record UpdateCourseContentRequest(string TitleEn, string TitleAr, int SortOrder);

public sealed record CreateContentItemRequest(string TitleEn, string TitleAr, ContentItemType ItemType, string? Body, int? SortOrder);

public sealed record UpdateContentItemRequest(string TitleEn, string TitleAr, string? Body, int SortOrder);

public sealed record ReorderItemsRequest(IReadOnlyList<Guid> ItemIds);

/// <summary>An upload as the application layer sees it: the client name is display data only (SEC-23); the length is the declared size, re-checked while streaming.</summary>
public sealed record FileUpload(string FileName, string ContentType, long Length, Stream Content);

// ----- Responses -----

public sealed record ResourceResponse(Guid Id, Guid ContentItemId, string FileName, string ContentType, long SizeBytes, string ContentHash);

public sealed record ContentItemResponse(
    Guid Id,
    Guid CourseContentId,
    string TitleEn,
    string TitleAr,
    ContentItemType ItemType,
    string? Body,
    int SortOrder,
    bool IsPublished,
    IReadOnlyList<ResourceResponse> Resources);

public sealed record CourseContentResponse(
    Guid Id,
    Guid SectionId,
    string TitleEn,
    string TitleAr,
    int SortOrder,
    IReadOnlyList<ContentItemResponse> Items,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

/// <summary>A section's content hierarchy with the caller's scope: managers see every item, learners only published ones (BR-15).</summary>
public sealed record SectionContentResponse(SectionSummary Section, bool CanManage, IReadOnlyList<CourseContentResponse> Contents);

/// <summary>A stored file opened for an authorised download (SEC-25); the caller owns the stream.</summary>
public sealed record ResourceDownload(Stream Content, string FileName, string ContentType, long SizeBytes, string ContentHash);

// ----- Validators (18.2) -----

public sealed class CreateCourseContentRequestValidator : AbstractValidator<CreateCourseContentRequest>
{
    public CreateCourseContentRequestValidator()
    {
        RuleFor(r => r.TitleEn).NotEmpty().MaximumLength(CourseContent.TitleMaxLength);
        RuleFor(r => r.TitleAr).NotEmpty().MaximumLength(CourseContent.TitleMaxLength);
        RuleFor(r => r.SortOrder).GreaterThanOrEqualTo(0).When(r => r.SortOrder.HasValue);
    }
}

public sealed class UpdateCourseContentRequestValidator : AbstractValidator<UpdateCourseContentRequest>
{
    public UpdateCourseContentRequestValidator()
    {
        RuleFor(r => r.TitleEn).NotEmpty().MaximumLength(CourseContent.TitleMaxLength);
        RuleFor(r => r.TitleAr).NotEmpty().MaximumLength(CourseContent.TitleMaxLength);
        RuleFor(r => r.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateContentItemRequestValidator : AbstractValidator<CreateContentItemRequest>
{
    public CreateContentItemRequestValidator()
    {
        RuleFor(r => r.TitleEn).NotEmpty().MaximumLength(ContentItem.TitleMaxLength);
        RuleFor(r => r.TitleAr).NotEmpty().MaximumLength(ContentItem.TitleMaxLength);
        RuleFor(r => r.ItemType).IsInEnum();
        RuleFor(r => r.Body).NotEmpty().MaximumLength(ContentItem.PageBodyMaxLength).When(r => r.ItemType == ContentItemType.Page);
        RuleFor(r => r.Body).NotEmpty().MaximumLength(ContentItem.LinkBodyMaxLength)
            .Must(b => Uri.TryCreate(b, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Body must be an absolute http or https address.")
            .When(r => r.ItemType == ContentItemType.Link);
        RuleFor(r => r.Body).MaximumLength(ContentItem.DescriptionMaxLength).When(r => r.ItemType == ContentItemType.File);
        RuleFor(r => r.SortOrder).GreaterThanOrEqualTo(0).When(r => r.SortOrder.HasValue);
    }
}

public sealed class UpdateContentItemRequestValidator : AbstractValidator<UpdateContentItemRequest>
{
    public UpdateContentItemRequestValidator()
    {
        RuleFor(r => r.TitleEn).NotEmpty().MaximumLength(ContentItem.TitleMaxLength);
        RuleFor(r => r.TitleAr).NotEmpty().MaximumLength(ContentItem.TitleMaxLength);
        RuleFor(r => r.Body).MaximumLength(ContentItem.PageBodyMaxLength);
        RuleFor(r => r.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class ReorderItemsRequestValidator : AbstractValidator<ReorderItemsRequest>
{
    public ReorderItemsRequestValidator()
    {
        RuleFor(r => r.ItemIds).NotEmpty();
        RuleForEach(r => r.ItemIds).NotEmpty();
    }
}

internal static class ContentMapping
{
    public static ResourceResponse ToResponse(this Resource r) =>
        new(r.Id, r.ContentItemId, r.FileName, r.ContentType, r.SizeBytes, r.ContentHash);

    public static ContentItemResponse ToResponse(this ContentItem i) =>
        new(i.Id, i.CourseContentId, i.TitleEn, i.TitleAr, i.ItemType, i.Body, i.SortOrder, i.IsPublished,
            i.Resources.OrderBy(r => r.CreatedAtUtc).Select(ToResponse).ToList());

    /// <summary>The full view for managers, or the learner's view (published items only, BR-15).</summary>
    public static CourseContentResponse ToResponse(this CourseContent c, bool includeUnpublished)
    {
        var items = includeUnpublished ? c.Items.AsEnumerable() : c.ItemsVisibleToLearners;
        return new CourseContentResponse(c.Id, c.SectionId, c.TitleEn, c.TitleAr, c.SortOrder,
            items.OrderBy(i => i.SortOrder).Select(ToResponse).ToList(), c.CreatedAtUtc, c.ModifiedAtUtc);
    }
}
