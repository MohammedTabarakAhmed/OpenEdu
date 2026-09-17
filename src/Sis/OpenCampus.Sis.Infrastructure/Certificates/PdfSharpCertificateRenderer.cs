using System.Globalization;
using OpenCampus.Sis.Application.Certificates;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace OpenCampus.Sis.Infrastructure.Certificates;

/// <summary>
/// Renders the certificate with PDFsharp (MIT; section 7 "PDF generation"). One landscape A4 page: institution line,
/// the learner, what was completed, the final grade and date, and the verification code the anonymous endpoint
/// answers to. Fonts come from the operating system through <see cref="PlatformFontResolver"/>; PDFsharp performs no
/// bidirectional shaping, so the printed body is English while the Arabic names travel on the API responses for the
/// client to render.
/// </summary>
public sealed class PdfSharpCertificateRenderer : ICertificateDocumentRenderer
{
    private const string FontFamily = PlatformFontResolver.Sans;

    static PdfSharpCertificateRenderer()
    {
        // PDFsharp holds one process-wide resolver; it is assigned once, before the first font is requested.
        GlobalFontSettings.FontResolver ??= new PlatformFontResolver();
    }

    public byte[] Render(CertificateDocument document)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Certificate of Completion – {document.CourseCode}";
        pdf.Info.Author = "OpenCampus";
        pdf.Info.Subject = document.VerificationCode;

        var page = pdf.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        page.Orientation = PdfSharp.PageOrientation.Landscape;

        using var gfx = XGraphics.FromPdfPage(page);
        var width = page.Width.Point;
        var height = page.Height.Point;

        var title = new XFont(FontFamily, 30, XFontStyleEx.Bold);
        var heading = new XFont(FontFamily, 16, XFontStyleEx.Regular);
        var name = new XFont(FontFamily, 26, XFontStyleEx.Bold);
        var body = new XFont(FontFamily, 13, XFontStyleEx.Regular);
        var small = new XFont(FontFamily, 10, XFontStyleEx.Regular);
        var mono = new XFont(PlatformFontResolver.Mono, 14, XFontStyleEx.Bold);

        var ink = new XSolidBrush(XColor.FromArgb(0x1F, 0x2A, 0x44));
        var accent = new XPen(XColor.FromArgb(0x0B, 0x6E, 0x4F), 3);
        var faint = new XSolidBrush(XColor.FromArgb(0x55, 0x5F, 0x71));

        // Border and rule.
        gfx.DrawRectangle(accent, 28, 28, width - 56, height - 56);
        gfx.DrawLine(accent, 120, 150, width - 120, 150);

        var y = 78d;
        Centered("OpenCampus", heading, faint, ref y, 26);
        Centered("CERTIFICATE OF COMPLETION", title, ink, ref y, 84);
        Centered("This certifies that", body, faint, ref y, 40);
        Centered(document.LearnerFullNameEn, name, ink, ref y, 40);
        Centered($"Learner number {document.LearnerNumber}", small, faint, ref y, 44);
        Centered("has successfully completed", body, faint, ref y, 30);
        Centered($"{document.CourseCode} — {document.CourseNameEn}", heading, ink, ref y, 30);
        Centered($"Section {document.SectionCode}, {document.Term} · Programme {document.ProgrammeCode} — {document.ProgrammeNameEn}", body, ink, ref y, 44);
        Centered(
            $"Final grade {document.FinalGradePercent.ToString("0.##", CultureInfo.InvariantCulture)} %  ·  Completed {document.CompletedAtUtc:d MMMM yyyy}",
            body, ink, ref y, 70);

        Centered($"Issued {document.IssuedAtUtc:d MMMM yyyy} (UTC)", small, faint, ref y, 20);
        Centered("Verification code", small, faint, ref y, 20);
        Centered(document.VerificationCode, mono, ink, ref y, 20);

        using var output = new MemoryStream();
        pdf.Save(output, closeStream: false);
        return output.ToArray();

        void Centered(string text, XFont font, XBrush brush, ref double top, double advance)
        {
            gfx.DrawString(text, font, brush, new XRect(40, top, width - 80, advance), XStringFormats.TopCenter);
            top += advance;
        }
    }
}
