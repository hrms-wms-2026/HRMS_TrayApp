namespace ONEVO.Agent.TrayApp.Tests.Services;

using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;
using Xunit;

public sealed class UpdateNotifierTests
{
    private sealed class StubChecker : IUpdateChecker
    {
        public UpdateCheckResultPayload? Result { get; set; }
        public bool Throws { get; set; }

        public Task<UpdateCheckResultPayload?> CheckAsync(CancellationToken ct) =>
            Throws ? throw new InvalidOperationException("boom") : Task.FromResult(Result);
    }

    private static UpdateCheckResultPayload Update(string version, bool mandatory = false) => new(
        true, true, mandatory, version, "https://dl.example.com/a.msix", new string('a', 64), 10, null, null);

    private static (UpdateNotifier Notifier, List<(string Title, string Message)> Shown) Build(StubChecker checker)
    {
        var shown = new List<(string, string)>();
        return (new UpdateNotifier(checker, (t, m) => shown.Add((t, m)), NullLogger<UpdateNotifier>.Instance), shown);
    }

    [Fact]
    public async Task NotifiesOnce_PerVersion()
    {
        var checker = new StubChecker { Result = Update("1.3.0") };
        var (notifier, shown) = Build(checker);

        await notifier.CheckOnceAsync(CancellationToken.None);
        await notifier.CheckOnceAsync(CancellationToken.None);

        Assert.Single(shown);
        Assert.Contains("1.3.0", shown[0].Message);
    }

    [Fact]
    public async Task NotifiesAgain_WhenANewerVersionAppears()
    {
        var checker = new StubChecker { Result = Update("1.3.0") };
        var (notifier, shown) = Build(checker);
        await notifier.CheckOnceAsync(CancellationToken.None);

        checker.Result = Update("1.4.0");
        await notifier.CheckOnceAsync(CancellationToken.None);

        Assert.Equal(2, shown.Count);
    }

    [Fact]
    public async Task MandatoryUpdate_UsesRequiredWording()
    {
        var (notifier, shown) = Build(new StubChecker { Result = Update("1.3.0", mandatory: true) });

        await notifier.CheckOnceAsync(CancellationToken.None);

        Assert.Contains("required update", shown.Single().Message);
    }

    [Fact]
    public async Task NoUpdate_ShowsNothing()
    {
        var (notifier, shown) = Build(new StubChecker { Result = null });

        await notifier.CheckOnceAsync(CancellationToken.None);

        Assert.Empty(shown);
    }

    [Fact]
    public async Task CheckerFailure_IsSwallowed()
    {
        var (notifier, shown) = Build(new StubChecker { Throws = true });

        await notifier.CheckOnceAsync(CancellationToken.None);

        Assert.Empty(shown);
    }
}
