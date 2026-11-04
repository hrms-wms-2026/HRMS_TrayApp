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
    public void HashWindowTitle_IsHmacSha256_KnownVector()
    {
        // RFC 4231 test case 2: key "Jefe", data "what do ya want for nothing?".
        Assert.Equal(
            "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
            HashingService.HashWindowTitle("what do ya want for nothing?", "Jefe"u8.ToArray()));
    }

    [Fact]
    public void HashWindowTitle_IsNotAPlainSha256_SoAGuessedTitleCannotBeChecked()
    {
        // SHA-256("hello"): what anyone could compute for a guessed title without the device key.
        Assert.NotEqual(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            HashingService.HashWindowTitle("hello"));
    }

    [Fact]
    public void DeviceKey_IsCreatedOnce_ThenReadBack()
    {
        var original = HashingService.KeyPath;
        var dir = Path.Combine(Path.GetTempPath(), $"onevo-hash-key-{Guid.NewGuid():N}");
        try
        {
            HashingService.KeyPath = Path.Combine(dir, "title-hash.key");

            var first = HashingService.LoadOrCreateKey();
            var second = HashingService.LoadOrCreateKey();

            Assert.Equal(32, first.Length);
            Assert.Equal(first, second);
            // Stored DPAPI-protected, never as the raw key.
            Assert.NotEqual(first, File.ReadAllBytes(HashingService.KeyPath));
        }
        finally
        {
            HashingService.KeyPath = original;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void DeviceKey_CorruptFile_IsReplaced()
    {
        var original = HashingService.KeyPath;
        var dir = Path.Combine(Path.GetTempPath(), $"onevo-hash-key-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            HashingService.KeyPath = Path.Combine(dir, "title-hash.key");
            File.WriteAllBytes(HashingService.KeyPath, [1, 2, 3]);

            var key = HashingService.LoadOrCreateKey();

            Assert.Equal(32, key.Length);
            Assert.Equal(key, HashingService.LoadOrCreateKey());
        }
        finally
        {
            HashingService.KeyPath = original;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
