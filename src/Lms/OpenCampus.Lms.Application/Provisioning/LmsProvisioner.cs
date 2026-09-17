using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Storage;
using OpenCampus.Lms.Domain.Content;

namespace OpenCampus.Lms.Application.Provisioning;

/// <summary>SDD Appendix B "Provisioning", as seen by the LMS module. Bound from the same "Provisioning" section as Identity and SIS.</summary>
public sealed class LmsProvisioningOptions
{
    public const string SectionName = "Provisioning";

    public bool DemonstrationDataEnabled { get; set; }
}

/// <summary>
/// SDD 18.7 / DEP-11: demonstration content for every live demonstration section — three units each with a
/// published page, a published link, a published file item (a small generated text resource through the
/// file store) and one unpublished page so BR-15 can be shown. Idempotent: a no-op once any content exists.
/// Content item types (13.6) are a fixed enumeration in the domain and need no rows. Runs after SIS
/// provisioning because sections resolve through <see cref="ISectionAccess"/>.
/// </summary>
public sealed class LmsProvisioner(
    ICourseContentRepository contents,
    ISectionAccess sections,
    IFileStore files,
    IOptions<StorageOptions> storage,
    IOptions<LmsProvisioningOptions> options,
    ILmsUnitOfWork unitOfWork,
    ILogger<LmsProvisioner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.DemonstrationDataEnabled || await contents.AnyAsync(cancellationToken))
        {
            return;
        }

        var live = await sections.ListLiveSectionsAsync(cancellationToken);
        var units = 0;
        var resources = 0;

        foreach (var section in live)
        {
            for (var week = 1; week <= 3; week++)
            {
                var unit = CourseContent.Create(section.Id, $"Week {week}", $"الأسبوع {week}", week - 1);
                var page = unit.AddItem($"Overview of week {week}", $"نظرة عامة على الأسبوع {week}", ContentItemType.Page,
                    $"This week of {section.CourseNameEn} ({section.Code}) covers the core material for week {week}. Read the notes before the session.");
                unit.PublishItem(page.Id);

                var link = unit.AddItem("Further reading", "قراءات إضافية", ContentItemType.Link, "https://www.example.org/reading");
                unit.PublishItem(link.Id);

                if (week == 1)
                {
                    var file = unit.AddItem("Course syllabus", "الخطة الدراسية", ContentItemType.File, "Syllabus and schedule for the term.");
                    var text = $"{section.CourseCode} {section.Code} — {section.CourseNameEn}\r\nTerm: {section.TermName}\r\n\r\nSyllabus (demonstration data).\r\n";
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                    var stored = await files.SaveAsync($"content/{section.Id:N}/{file.Id:N}", ".txt", stream, storage.Value.MaxUploadSizeBytes, cancellationToken);
                    if (stored is not null)
                    {
                        unit.AttachResource(file.Id, "syllabus.txt", stored.RelativePath, "text/plain", stored.SizeBytes, stored.ContentHash);
                        unit.PublishItem(file.Id);
                        resources++;
                    }
                }

                if (week == 3)
                {
                    // Left unpublished on purpose: invisible to learners until the instructor publishes it (BR-15).
                    unit.AddItem("Draft: revision notes", "مسودة: ملاحظات المراجعة", ContentItemType.Page, "Draft notes, not yet released.");
                }

                contents.Add(unit);
                units++;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("LMS demonstration data provisioned: {Sections} sections, {Units} content units, {Resources} resources", live.Count, units, resources);
    }
}
