namespace ONEVO.Agent.Service.Tests.Diagnostics;

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Agent.Service.Diagnostics;
using ONEVO.Agent.Shared.Diagnostics;
using Xunit;

public sealed class ServiceLoggingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"onevo-service-log-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void DefaultDirectory_IsUnderAgentDataRoot()
    {
        Assert.Equal(
            Path.Combine(ONEVO.Agent.Service.Security.AgentDataPaths.Root, "logs"),
            ServiceLogging.DefaultDirectory);
    }

    [Fact]
    public void CreateFileProvider_CreatesRestrictedFolder()
    {
        ServiceLogging.CreateFileProvider(_dir);

        var security = new DirectoryInfo(_dir).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        Assert.DoesNotContain(
            security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            r => r.AccessControlType == AccessControlType.Allow && users.Equals(r.IdentityReference));
    }

    [Fact]
    public void Lines_CarryCorrelationIdScope()
    {
        var provider = ServiceLogging.CreateFileProvider(_dir);
        using (var factory = LoggerFactory.Create(b => b.AddProvider(provider)))
        {
            var logger = factory.CreateLogger("ONEVO.Agent.Service.AgentWorker");
            using (logger.BeginScope(AgentCorrelation.Scope("cid-from-tray")))
                logger.LogInformation("Lifecycle {Action}", "ClockIn");
        }

        Assert.Equal(Path.Combine(_dir, ServiceLogging.FileName), provider.FilePath);
        var line = JsonDocument.Parse(Assert.Single(File.ReadAllLines(provider.FilePath))).RootElement;
        Assert.Equal("cid-from-tray", line.GetProperty("scope").GetProperty(AgentCorrelation.ScopeKey).GetString());
    }
}
