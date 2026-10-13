namespace ONEVO.Agent.Service.Buffer;

/// <summary>
/// Operator recovery for quarantined buffer rows (AGT-01). Runs instead of the service host:
/// <code>
/// ONEVO.Agent.Service.exe --export-quarantine &lt;file.jsonl&gt; [--db &lt;agent_activity.db&gt;]
/// ONEVO.Agent.Service.exe --requeue-quarantine [&lt;reason-prefix&gt;] [--db &lt;agent_activity.db&gt;]
/// </code>
/// The service account's database lives under its own LocalAppData (LocalSystem:
/// <c>%WINDIR%\System32\config\systemprofile\AppData\Local\ONEVO\Agent\agent_activity.db</c>), so pass
/// <c>--db</c> when running from an administrator console.
/// </summary>
public static class QuarantineCommands
{
    public const string Export = "--export-quarantine";
    public const string Requeue = "--requeue-quarantine";

    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] is Export or Requeue;

    public static int Run(string[] args, TextWriter output)
    {
        var dbIndex = Array.IndexOf(args, "--db");
        var dbPath = dbIndex >= 0 && dbIndex + 1 < args.Length
            ? args[dbIndex + 1]
            : ActivityRecordBuffer.GetDefaultDatabasePath();
        var positional = args.Skip(1).Where((_, i) => i + 1 != dbIndex && i + 1 != dbIndex + 1).ToArray();

        if (!File.Exists(dbPath))
        {
            output.WriteLine($"Buffer database not found: {dbPath}");
            return 2;
        }

        using var buffer = new ActivityRecordBuffer(dbPath);
        if (args[0] == Export)
        {
            if (positional.Length == 0)
            {
                output.WriteLine($"Usage: {Export} <file.jsonl> [--db <path>]");
                return 1;
            }

            using var file = new StreamWriter(positional[0], append: false);
            var count = buffer.ExportQuarantined(file);
            output.WriteLine($"Exported {count} quarantined row(s) to {positional[0]}");
            return 0;
        }

        var prefix = positional.Length > 0 ? positional[0] : null;
        var requeued = buffer.RequeueQuarantined(prefix);
        output.WriteLine($"Requeued {requeued} quarantined row(s){(prefix is null ? string.Empty : $" with reason '{prefix}*'")}");
        return 0;
    }
}
