using System.Text.RegularExpressions;
using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Tests.Services;

/// <summary>
/// AUD-032: an exception escaping an <c>async void</c> page or handler method goes to the dispatcher's
/// unhandled-exception path, which can terminate the tray (awaiting an AsyncRelayCommand rethrows the
/// command's exception). Such methods run their work through <see cref="UiTasks.RunSafelyAsync"/>.
/// </summary>
[Collection(UiTasksCollection.Name)]
public sealed class UiTasksTests : IDisposable
{
    private readonly List<(string Context, Exception Error)> _reported = [];

    public UiTasksTests() => UiTasks.Report = (context, error) => _reported.Add((context, error));

    public void Dispose() => UiTasks.ResetForTests();

    [Fact]
    public async Task RunSafelyAsync_Exception_IsReportedNotThrown()
    {
        var failure = new InvalidOperationException("camera unavailable");

        await UiTasks.RunSafelyAsync(() => Task.FromException(failure), "PhotoCaptureWindow.OnAppearing");

        var (context, error) = Assert.Single(_reported);
        Assert.Equal("PhotoCaptureWindow.OnAppearing", context);
        Assert.Same(failure, error);
    }

    [Fact]
    public async Task RunSafelyAsync_Cancellation_IsIgnored()
    {
        await UiTasks.RunSafelyAsync(() => Task.FromCanceled(new CancellationToken(canceled: true)), "page");

        Assert.Empty(_reported);
    }

    [Fact]
    public async Task RunSafelyAsync_Success_ReportsNothing()
    {
        var ran = false;

        await UiTasks.RunSafelyAsync(() => { ran = true; return Task.CompletedTask; }, "page");

        Assert.True(ran);
        Assert.Empty(_reported);
    }

    [Fact]
    public void EveryAsyncVoidMethodInTheTrayApp_IsGuarded()
    {
        var root = FindTrayAppSourceRoot();
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"\basync\s+void\s+\w+\s*\("))
                    continue;

                var body = MethodBody(lines, i);
                if (!body.Contains("UiTasks.RunSafelyAsync", StringComparison.Ordinal)
                    && !body.Contains("catch", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(root, path)}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "async void methods without UiTasks.RunSafelyAsync or a catch:\n" + string.Join('\n', offenders));
    }

    /// <summary>The method's text from its signature to the brace that closes its body.</summary>
    private static string MethodBody(string[] lines, int signatureLine)
    {
        var depth = 0;
        var opened = false;
        var body = new System.Text.StringBuilder();
        for (var i = signatureLine; i < lines.Length; i++)
        {
            body.AppendLine(lines[i]);
            foreach (var ch in lines[i])
            {
                if (ch == '{') { depth++; opened = true; }
                else if (ch == '}') depth--;
            }

            if (opened && depth == 0)
                break;
        }

        return body.ToString();
    }

    private static string FindTrayAppSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "ONEVO.Agent.TrayApp");
            if (File.Exists(Path.Combine(candidate, "ONEVO.Agent.TrayApp.csproj")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ONEVO.Agent.TrayApp.csproj.");
    }
}

[CollectionDefinition(Name)]
public sealed class UiTasksCollection
{
    public const string Name = "UiTasksReport";
}
