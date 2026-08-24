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
}
