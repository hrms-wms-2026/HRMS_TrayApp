namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.TrayApp.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

public sealed record DailySummaryPdfScreenshot(string TimeDisplay, byte[] JpegBytes, bool IsSkipped = false);

public sealed record DailySummaryPdfData(
    string StatusText,
    string ClockInDisplay,
    string ClockOutDisplay,
    string TotalShiftDisplay,
    string WorkingTimeDisplay,
    string BreakTimeDisplay,
    string ProductiveTimeDisplay,
    string IdleTimeDisplay,
    string BreakSessionsDisplay,
    IReadOnlyList<TopAppItem> TopApps,
    IReadOnlyList<DailySummaryPdfScreenshot>? Screenshots = null);

/// <summary>
/// Renders the same locally-known session summary shown on EndSessionPage as a one-page
/// PDF, entirely client-side — no backend call. The TrayApp only holds a device credential
/// with no HR permissions, so it cannot call the HR-facing daily-report export API; this
/// mirrors that endpoint's layout using data already loaded into the view model.
/// </summary>
public static class DailySummaryPdfBuilder
{
    public static byte[] Build(DailySummaryPdfData data)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text("OneXso WorkPulse").FontSize(18).Bold();
                    col.Item().Text("Daily Work Summary").FontSize(12).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(2).Text(DateTime.Now.ToString("dddd, d MMMM yyyy"))
                        .FontSize(10).FontColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Component(new SummaryTile("Status", data.StatusText));
                        row.RelativeItem().Component(new SummaryTile("Clock In", data.ClockInDisplay));
                        row.RelativeItem().Component(new SummaryTile("Clock Out", data.ClockOutDisplay));
                        row.RelativeItem().Component(new SummaryTile("Total Shift", data.TotalShiftDisplay));
                    });

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Component(new SummaryTile("Working Time", data.WorkingTimeDisplay));
                        row.RelativeItem().Component(new SummaryTile("Break Time", data.BreakTimeDisplay));
                        row.RelativeItem().Component(new SummaryTile("Break Sessions", data.BreakSessionsDisplay));
                    });

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Component(new SummaryTile("Productive Time", data.ProductiveTimeDisplay));
                        row.RelativeItem().Component(new SummaryTile("Idle Time", data.IdleTimeDisplay));
                        row.RelativeItem();
                    });

                    col.Item().PaddingTop(8).Text("Top Applications Used Today").FontSize(12).Bold();

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCellStyle).Text("Application");
                            header.Cell().Element(HeaderCellStyle).AlignRight().Text("Duration");
                        });

                        var apps = data.TopApps.Count == 0
                            ? [new TopAppItem("No app activity yet", "00:00:00")]
                            : data.TopApps;

                        foreach (var app in apps)
                        {
                            table.Cell().Element(BodyCellStyle).Text(app.Name);
                            table.Cell().Element(BodyCellStyle).AlignRight().Text(app.Duration);
                        }

                        static IContainer HeaderCellStyle(IContainer c) =>
                            c.PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

                        static IContainer BodyCellStyle(IContainer c) =>
                            c.PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                    });

                    var shots = data.Screenshots ?? [];
                    if (shots.Count > 0)
                    {
                        var allowedCount = shots.Count(s => !s.IsSkipped);
                        var skippedCount = shots.Count - allowedCount;
                        col.Item().PaddingTop(8).Text("Activity Screenshots").FontSize(12).Bold();
                        col.Item().Text(BuildPdfScreenshotsCaption(allowedCount, skippedCount))
                            .FontSize(9).FontColor(Colors.Grey.Medium);

                        foreach (var shot in shots)
                        {
                            var captured = shot;
                            if (captured.IsSkipped)
                            {
                                col.Item().Row(row =>
                                {
                                    row.RelativeItem().Height(110)
                                        .Background(Colors.Red.Lighten4)
                                        .Border(1).BorderColor(Colors.Red.Medium)
                                        .AlignCenter().AlignMiddle()
                                        .Text("Screenshot skipped")
                                        .FontSize(11).FontColor(Colors.Red.Darken2).Bold();
                                    row.ConstantItem(72).AlignMiddle().Text(captured.TimeDisplay).FontSize(9);
                                });
                                continue;
                            }

                            if (captured.JpegBytes is not { Length: >= 2 }
                                || captured.JpegBytes[0] != 0xFF
                                || captured.JpegBytes[1] != 0xD8)
                                continue;

                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Height(110).Image(captured.JpegBytes).FitArea();
                                row.ConstantItem(72).AlignMiddle().Text(captured.TimeDisplay).FontSize(9);
                            });
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated by OneXso WorkPulse on ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span(DateTime.Now.ToString("d MMM yyyy, h:mm tt")).FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }

    public static async Task<string> WriteToDownloadsAsync(DailySummaryPdfData data, CancellationToken ct = default)
    {
        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(downloads, "Downloads");
        if (!Directory.Exists(dir))
            dir = downloads;

        var path = Path.Combine(dir, $"OneXso-Daily-Summary-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, Build(data), ct);
        return path;
    }

    private static string BuildPdfScreenshotsCaption(int allowedCount, int skippedCount)
    {
        if (skippedCount == 0)
            return $"{allowedCount} captured after you allowed an activity check.";
        if (allowedCount == 0)
            return $"{skippedCount} skipped activity check{(skippedCount == 1 ? "" : "s")}.";
        return $"{allowedCount} captured, {skippedCount} skipped.";
    }

    private sealed class SummaryTile(string label, string value) : IComponent
    {
        public void Compose(IContainer container)
        {
            container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(col =>
            {
                col.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Medium);
                col.Item().Text(value).FontSize(12).Bold();
            });
        }
    }
}
