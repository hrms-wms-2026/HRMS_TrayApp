using System.Text.RegularExpressions;

namespace ONEVO.Agent.TrayApp.Tests.Views;

/// <summary>
/// Every image the tray UI names must ship in Resources/Images. Unused images were removed to
/// shrink the MSIX; a missing one would show as an empty space on screen, not as a build error.
/// </summary>
public sealed partial class ImageResourceContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string TrayRoot = Path.Combine(RepoRoot, "ONEVO.Agent.TrayApp");
    private static readonly string ImagesDir = Path.Combine(TrayRoot, "Resources", "Images");

    [GeneratedRegex("\"([A-Za-z0-9_]+)\\.png\"")]
    private static partial Regex ImageLiteral();

    [Fact]
    public void EveryImageNamedInTheTrayUi_IsPackaged()
    {
        var referenced = SourceFiles()
            .SelectMany(file => ImageLiteral().Matches(File.ReadAllText(file)).Select(m => (File: file, Name: m.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(referenced);
        // MAUI packages an SVG under the same name with a .png extension.
        var missing = referenced
            .Where(r => !File.Exists(Path.Combine(ImagesDir, r.Name + ".png"))
                     && !File.Exists(Path.Combine(ImagesDir, r.Name + ".svg")))
            .Select(r => $"{r.Name}.png in {Path.GetRelativePath(RepoRoot, r.File)}")
            .ToList();

        Assert.True(missing.Count == 0, "Images referenced but not in Resources/Images: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryPackagedImage_IsUsedByTheTrayUi()
    {
        var sources = string.Join("\n", SourceFiles().Select(File.ReadAllText));
        var unused = Directory.EnumerateFiles(ImagesDir)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !sources.Contains(name + ".png", StringComparison.Ordinal))
            .ToList();

        Assert.True(unused.Count == 0, "Images packaged but never used (they only add MSIX size): " + string.Join(", ", unused));
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(TrayRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ONEVO.Agent.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ONEVO.Agent.slnx above the test output directory.");
    }
}
