namespace OpenCampus.Lms.Domain.Content;

/// <summary>
/// Content item types (reference data, SDD 13.6). The SDD names the set without enumerating it; the
/// minimum needed for content delivery is a page of text, an external link and a file-backed item.
/// </summary>
public enum ContentItemType
{
    /// <summary>The body is the content itself (plain text, output-encoded on display per SEC-20).</summary>
    Page = 1,

    /// <summary>The body is an absolute http(s) address.</summary>
    Link = 2,

    /// <summary>The content is carried by one or more attached resources; the body is an optional description.</summary>
    File = 3,
}
