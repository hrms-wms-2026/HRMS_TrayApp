using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.Services;

public sealed class DailySummaryPdfBuilderTests
{
    private static DailySummaryPdfData SampleData(IReadOnlyList<TopAppItem>? topApps = null) => new(
        StatusText: "Clocked Out",
        ClockInDisplay: "09:00 AM",
        ClockOutDisplay: "06:00 PM",
        TotalShiftDisplay: "09:00:00",
        WorkingTimeDisplay: "08:10:00",
        BreakTimeDisplay: "00:50:00",
        ProductiveTimeDisplay: "07:50:00",
        IdleTimeDisplay: "00:20:00",
        BreakSessionsDisplay: "2",
        TopApps: topApps ?? [new TopAppItem("Visual Studio Code", "02:30:00")]);

    [Fact]
    public void Build_ProducesValidPdfBytes()
    {
        var bytes = DailySummaryPdfBuilder.Build(SampleData());

        Assert.NotEmpty(bytes);
        // Every PDF file starts with this magic header.
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    [Fact]
    public void Build_WithNoTopApps_StillProducesValidPdf()
    {
        var bytes = DailySummaryPdfBuilder.Build(SampleData(topApps: []));

        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    [Fact]
    public void Build_WithAllowedScreenshot_ProducesValidPdf()
    {
        using var bmp = new System.Drawing.Bitmap(8, 8);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        var jpeg = ms.ToArray();

        var bytes = DailySummaryPdfBuilder.Build(SampleData() with
        {
            Screenshots = [new DailySummaryPdfScreenshot("10:05 AM", jpeg)]
        });

        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    [Fact]
    public void Build_WithSkippedScreenshot_ProducesValidPdf()
    {
        var bytes = DailySummaryPdfBuilder.Build(SampleData() with
        {
            Screenshots = [new DailySummaryPdfScreenshot("10:07 AM", [], IsSkipped: true)]
        });

        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
        Assert.True(bytes.Length > DailySummaryPdfBuilder.Build(SampleData() with { Screenshots = [] }).Length);
    }
}
