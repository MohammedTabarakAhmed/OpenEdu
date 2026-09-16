using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Programmes;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Application.Provisioning;

/// <summary>SDD Appendix B "Provisioning", as seen by the SIS module. Bound from the same "Provisioning" section as Identity.</summary>
public sealed class SisProvisioningOptions
{
    public const string SectionName = "Provisioning";

    /// <summary>Demonstration academic structure, learners and enrolments (DEP-11). Development and demonstration environments only (18.7).</summary>
    public bool DemonstrationDataEnabled { get; set; }
}

/// <summary>
/// SDD 18.7 / DEP-11: demonstration academic data of sufficient volume to exercise pagination and
/// filtering. Idempotent: a no-op once the demonstration programmes exist. Reference sets of 13.6
/// (delivery modes, enrolment statuses) are fixed enumerations declared in the domain and need no rows.
/// Runs after Identity provisioning because instructor and learner references resolve through
/// <see cref="IUserDirectory"/> (13.5).
/// </summary>
public sealed class SisProvisioner(
    IProgrammeRepository programmes,
    ICourseRepository courses,
    ISectionRepository sections,
    ILearnerRepository learners,
    IEnrolmentRepository enrolments,
    IUserDirectory users,
    ISisUnitOfWork unitOfWork,
    IOptions<SisProvisioningOptions> options,
    TimeProvider clock,
    ILogger<SisProvisioner> logger)
{
    private static readonly DateOnly TermStart = new(2026, 9, 7);
    private static readonly DateOnly TermEnd = new(2026, 12, 18);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.DemonstrationDataEnabled)
        {
            return;
        }

        if (await programmes.CodeExistsAsync(DemonstrationData.Programmes[0].Code, cancellationToken))
        {
            logger.LogInformation("SIS demonstration data already present; nothing to provision");
            return;
        }

        var instructors = await users.ListActiveInRoleAsync("Instructor", cancellationToken);
        var learnerUsers = await users.ListActiveInRoleAsync("Learner", cancellationToken);
        if (instructors.Count == 0 || learnerUsers.Count == 0)
        {
            logger.LogWarning("SIS demonstration data skipped: no instructor or learner accounts are provisioned");
            return;
        }

        var createdSections = ProvisionStructure(instructors);
        var createdLearners = ProvisionLearners(learnerUsers);
        var enrolled = ProvisionEnrolments(createdSections, createdLearners);

        // Sections that demonstrate the Closed state are closed only after their enrolments exist (BR-03).
        foreach (var (section, _) in createdSections.Where(s => s.TargetStatus == SectionStatus.Closed))
        {
            section.Close();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "SIS demonstration data provisioned: {Programmes} programmes, {Courses} courses, {Sections} sections, {Learners} learners, {Enrolments} enrolments",
            DemonstrationData.Programmes.Count, DemonstrationData.Courses.Count, createdSections.Count, createdLearners.Count, enrolled);
    }

    private List<(CourseSection Section, SectionStatus TargetStatus)> ProvisionStructure(IReadOnlyList<UserSummary> instructors)
    {
        var programmesByCode = new Dictionary<string, Programme>();
        foreach (var p in DemonstrationData.Programmes)
        {
            var programme = Programme.Create(p.Code, p.NameEn, p.NameAr, p.DurationMonths);
            programmes.Add(programme);
            programmesByCode[p.Code] = programme;
        }

        var created = new List<(CourseSection, SectionStatus)>();
        var index = 0;
        foreach (var c in DemonstrationData.Courses)
        {
            var course = Course.Create(programmesByCode[c.ProgrammeCode].Id, c.Code, c.NameEn, c.NameAr, c.DescriptionEn, c.DescriptionAr, c.Credits);
            courses.Add(course);

            var instructor = instructors[index % instructors.Count];
            var section = CourseSection.Create(
                course.Id, $"{c.Code}-A", "2026 Autumn", TermStart, TermEnd, c.Capacity, instructor.Id, c.DeliveryMode);

            // One weekly two-hour slot per section; slots are staggered per instructor so BR-16 holds.
            var slot = index / instructors.Count;
            var weekday = slot % 5;
            var hour = 9 + (slot / 5) * 3;
            var first = new DateTime(TermStart.Year, TermStart.Month, TermStart.Day, hour, 0, 0, DateTimeKind.Utc).AddDays(weekday);
            for (var week = 0; week < 12; week++)
            {
                var start = first.AddDays(7 * week);
                section.AddSession(start, start.AddHours(2), c.DeliveryMode == DeliveryMode.Online ? "Online" : $"Room {101 + slot}", []);
            }

            section.AddGradeComponent("Coursework", "الأعمال الفصلية", 40m, 100m);
            section.AddGradeComponent("Midterm examination", "الامتحان النصفي", 20m, 50m);
            section.AddGradeComponent("Final examination", "الامتحان النهائي", 40m, 100m);

            if (c.Status != SectionStatus.Draft)
            {
                section.Open();
            }

            sections.Add(section);
            created.Add((section, c.Status));
            index++;
        }

        return created;
    }

    private List<Learner> ProvisionLearners(IReadOnlyList<UserSummary> learnerUsers)
    {
        var created = new List<Learner>();
        var number = 1;
        foreach (var user in learnerUsers.OrderBy(u => u.UserName, StringComparer.Ordinal))
        {
            var learner = Learner.Create(
                user.Id,
                $"L2026{number:000}",
                nationalId: null,
                dateOfBirth: new DateOnly(2000 + number % 8, 1 + number % 12, 1 + number % 28),
                gender: number % 2 == 0 ? Gender.Female : Gender.Male,
                phone: $"+9665{number:00000000}");
            learners.Add(learner);
            created.Add(learner);
            number++;
        }

        return created;
    }

    /// <summary>Each learner joins every third open section from a staggered offset, within capacity, giving a varied spread.</summary>
    private int ProvisionEnrolments(List<(CourseSection Section, SectionStatus TargetStatus)> createdSections, List<Learner> createdLearners)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var open = createdSections.Where(s => s.Section.Status == SectionStatus.Open).Select(s => s.Section).ToList();
        var activeCounts = open.ToDictionary(s => s.Id, _ => 0);
        var enrolled = 0;

        for (var i = 0; i < createdLearners.Count; i++)
        {
            for (var j = i % 3; j < open.Count; j += 3)
            {
                var section = open[j];
                if (activeCounts[section.Id] >= section.Capacity)
                {
                    continue;
                }

                enrolments.Add(Enrolment.Create(createdLearners[i], section, activeCounts[section.Id], false, now.AddDays(-(30 - i % 30))));
                activeCounts[section.Id]++;
                enrolled++;
            }
        }

        return enrolled;
    }
}

/// <summary>Demonstration academic structure (DEP-11): four programmes, twelve courses, one section each.</summary>
internal static class DemonstrationData
{
    public sealed record ProgrammeSeed(string Code, string NameEn, string NameAr, int DurationMonths);

    public sealed record CourseSeed(
        string ProgrammeCode, string Code, string NameEn, string NameAr, string DescriptionEn, string DescriptionAr,
        int Credits, int Capacity, DeliveryMode DeliveryMode, SectionStatus Status);

    public static readonly IReadOnlyList<ProgrammeSeed> Programmes =
    [
        new("BSC-CS", "Bachelor of Science in Computer Science", "بكالوريوس العلوم في علوم الحاسوب", 48),
        new("BSC-BA", "Bachelor of Business Administration", "بكالوريوس إدارة الأعمال", 48),
        new("DIP-DS", "Diploma in Data Science", "دبلوم علوم البيانات", 12),
        new("CERT-EAP", "Certificate in English for Academic Purposes", "شهادة اللغة الإنجليزية للأغراض الأكاديمية", 6),
    ];

    public static readonly IReadOnlyList<CourseSeed> Courses =
    [
        new("BSC-CS", "CS101", "Introduction to Programming", "مقدمة في البرمجة",
            "Fundamentals of programming using Python: variables, control flow, functions and data structures.",
            "أساسيات البرمجة باستخدام بايثون: المتغيرات، وتدفق التحكم، والدوال، وهياكل البيانات.", 4, 30, DeliveryMode.InPerson, SectionStatus.Open),
        new("BSC-CS", "CS201", "Data Structures and Algorithms", "هياكل البيانات والخوارزميات",
            "Lists, trees, graphs, sorting and searching, with an introduction to complexity analysis.",
            "القوائم والأشجار والرسوم البيانية والفرز والبحث، مع مقدمة في تحليل التعقيد.", 4, 25, DeliveryMode.Blended, SectionStatus.Open),
        new("BSC-CS", "CS305", "Database Systems", "نظم قواعد البيانات",
            "Relational modelling, SQL, transactions and an overview of non-relational stores.",
            "النمذجة العلائقية، وSQL، والمعاملات، ونظرة عامة على المخازن غير العلائقية.", 3, 5, DeliveryMode.InPerson, SectionStatus.Open),
        new("BSC-BA", "BA110", "Principles of Management", "مبادئ الإدارة",
            "Planning, organising, leading and controlling in contemporary organisations.",
            "التخطيط والتنظيم والقيادة والرقابة في المنظمات المعاصرة.", 3, 40, DeliveryMode.InPerson, SectionStatus.Open),
        new("BSC-BA", "BA215", "Financial Accounting", "المحاسبة المالية",
            "Recording transactions, preparing financial statements and interpreting results.",
            "تسجيل المعاملات وإعداد القوائم المالية وتفسير النتائج.", 3, 35, DeliveryMode.Online, SectionStatus.Open),
        new("BSC-BA", "BA320", "Marketing Strategy", "استراتيجية التسويق",
            "Segmentation, positioning and the marketing mix in digital and traditional channels.",
            "التجزئة وتحديد المواقع والمزيج التسويقي في القنوات الرقمية والتقليدية.", 3, 30, DeliveryMode.Blended, SectionStatus.Draft),
        new("DIP-DS", "DS120", "Statistics for Data Science", "الإحصاء لعلوم البيانات",
            "Descriptive and inferential statistics with applied exercises in Python.",
            "الإحصاء الوصفي والاستدلالي مع تمارين تطبيقية في بايثون.", 3, 30, DeliveryMode.Online, SectionStatus.Open),
        new("DIP-DS", "DS230", "Machine Learning Foundations", "أسس تعلم الآلة",
            "Supervised and unsupervised learning, model evaluation and responsible use.",
            "التعلم الخاضع للإشراف وغير الخاضع له، وتقييم النماذج، والاستخدام المسؤول.", 4, 25, DeliveryMode.Blended, SectionStatus.Open),
        new("DIP-DS", "DS240", "Data Visualisation", "تصوير البيانات",
            "Principles of visual encoding and practical dashboards.",
            "مبادئ الترميز البصري ولوحات المعلومات العملية.", 2, 20, DeliveryMode.Online, SectionStatus.Closed),
        new("CERT-EAP", "EN050", "Academic Reading and Writing", "القراءة والكتابة الأكاديمية",
            "Reading strategies, paragraph and essay structure, citation and paraphrase.",
            "استراتيجيات القراءة وبنية الفقرة والمقال والاقتباس وإعادة الصياغة.", 2, 20, DeliveryMode.InPerson, SectionStatus.Open),
        new("CERT-EAP", "EN060", "Academic Listening and Speaking", "الاستماع والتحدث الأكاديمي",
            "Note-taking from lectures, seminar participation and presentations.",
            "تدوين الملاحظات من المحاضرات والمشاركة في الندوات والعروض التقديمية.", 2, 20, DeliveryMode.InPerson, SectionStatus.Closed),
        new("CERT-EAP", "EN070", "Research Skills", "مهارات البحث",
            "Locating, evaluating and synthesising academic sources.",
            "تحديد المصادر الأكاديمية وتقييمها وتوليفها.", 1, 15, DeliveryMode.Online, SectionStatus.Draft),
    ];
}
