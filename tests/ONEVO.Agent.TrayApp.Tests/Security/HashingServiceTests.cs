namespace ONEVO.Agent.TrayApp.Tests.Security;

using ONEVO.Agent.TrayApp.Security;

public sealed class HashingServiceTests
{
    [Fact]
    public void HashWindowTitle_ProducesSha256Hex()
    {
        var result = HashingService.HashWindowTitle("Untitled - Notepad");
        Assert.NotNull(result);
        Assert.Equal(64, result.Length);
        Assert.Matches(@"^[0-9a-f]{64}$", result);
    }

    [Fact]
    public void HashWindowTitle_SameInput_SameOutput()
    {
        const string title = "Document.docx - Microsoft Word";
        Assert.Equal(
            HashingService.HashWindowTitle(title),
            HashingService.HashWindowTitle(title));
    }

    [Fact]
    public void HashWindowTitle_DifferentInputs_DifferentOutputs()
    {
        var h1 = HashingService.HashWindowTitle("title-one");
        var h2 = HashingService.HashWindowTitle("title-two");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void HashWindowTitle_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HashingService.HashWindowTitle(string.Empty));
    }

    [Fact]
    public void HashWindowTitle_DoesNotContainRawTitle()
    {
        const string title = "SuperSecretDocumentTitle";
        var result = HashingService.HashWindowTitle(title);
        Assert.DoesNotContain("SuperSecretDocumentTitle", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HashWindowTitle_KnownVector()
    {
        // SHA-256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            HashingService.HashWindowTitle("hello"));
    }
}
