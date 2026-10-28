namespace ONEVO.Agent.Shared.Diagnostics;

using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Writes <see cref="ILogger"/> output to a size-capped, rolling JSON-lines file (one object per
/// line: ts, level, category, msg, props, scope, ex). Used by the Tray (tray.log) and the Service
/// (service.log) so both read the same way and carry the same CorrelationId scope (AUD-018).
/// Each write opens, appends and closes the file under one lock, so a line is on disk before a
/// crash takes the process down and a second process never hits a locked file. Logging must
/// never break the app: every IO failure is swallowed.
/// </summary>
public sealed class JsonFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    public const long DefaultMaxFileBytes = 5 * 1024 * 1024;
    public const int DefaultRetainedFiles = 3;

    // A log file is never rendered as HTML, so keep '+', quotes and non-ASCII readable.
    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly long _maxFileBytes;
    private readonly int _retainedFiles;
    private readonly Func<DateTimeOffset> _clock;
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public JsonFileLoggerProvider(
        string directory,
        string fileName,
        long maxFileBytes = DefaultMaxFileBytes,
        int retainedFiles = DefaultRetainedFiles,
        Func<DateTimeOffset>? clock = null)
    {
        LogDirectory = directory;
        FilePath = Path.Combine(directory, fileName);
        _maxFileBytes = Math.Max(1, maxFileBytes);
        _retainedFiles = Math.Max(1, retainedFiles);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string LogDirectory { get; }

    public string FilePath { get; }

    public ILogger CreateLogger(string categoryName) => new JsonFileLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
        // Nothing held open between writes.
    }

    internal void Write<TState>(
        string category, LogLevel level, EventId eventId, TState state, Exception? exception, string message)
    {
        byte[] line;
        try
        {
            line = FormatLine(category, level, eventId, state, exception, message);
        }
        catch
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                RollIfNeeded(line.Length);
                using var stream = new FileStream(
                    FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(line);
            }
            catch
            {
                // Unwritable or locked log directory: drop the line rather than fail the caller.
            }
        }
    }

    private byte[] FormatLine<TState>(
        string category, LogLevel level, EventId eventId, TState state, Exception? exception, string message)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, JsonOptions))
        {
            json.WriteStartObject();
            json.WriteString("ts", _clock().ToString("O"));
            json.WriteString("level", level.ToString());
            json.WriteString("category", category);
            if (eventId.Id != 0)
                json.WriteNumber("eventId", eventId.Id);
            json.WriteString("msg", message);

            if (state is IEnumerable<KeyValuePair<string, object?>> props)
                WriteProperties(json, "props", props);

            WriteScopes(json);

            if (exception is not null)
                json.WriteString("ex", exception.ToString());
            json.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    private void WriteScopes(Utf8JsonWriter json)
    {
        var values = new List<KeyValuePair<string, object?>>();
        var texts = new List<string>();
        _scopes.ForEachScope(static (scope, acc) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                acc.values.AddRange(pairs);
            else if (scope?.ToString() is { } text)
                acc.texts.Add(text);
        }, (values, texts));

        if (values.Count > 0)
            WriteProperties(json, "scope", values);
        if (texts.Count > 0)
        {
            json.WriteStartArray("scopes");
            foreach (var text in texts)
                json.WriteStringValue(text);
            json.WriteEndArray();
        }
    }

    private static void WriteProperties(
        Utf8JsonWriter json, string name, IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        var started = false;
        foreach (var (key, value) in pairs)
        {
            // The message template is already rendered into "msg".
            if (key == "{OriginalFormat}")
                continue;
            if (!started)
            {
                json.WriteStartObject(name);
                started = true;
            }
            switch (value)
            {
                case null: json.WriteNull(key); break;
                case bool b: json.WriteBoolean(key, b); break;
                case int i: json.WriteNumber(key, i); break;
                case long l: json.WriteNumber(key, l); break;
                case double d when double.IsFinite(d): json.WriteNumber(key, d); break;
                default: json.WriteString(key, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)); break;
            }
        }
        if (started)
            json.WriteEndObject();
    }

    private void RollIfNeeded(int incomingBytes)
    {
        var current = new FileInfo(FilePath);
        if (!current.Exists || current.Length + incomingBytes <= _maxFileBytes)
            return;

        // x.log -> x.log.1 -> ... -> x.log.(retained-1); the oldest falls off.
        var archives = _retainedFiles - 1;
        if (archives == 0)
        {
            current.Delete();
            return;
        }
        File.Delete($"{FilePath}.{archives}");
        for (var i = archives - 1; i >= 1; i--)
        {
            var from = $"{FilePath}.{i}";
            if (File.Exists(from))
                File.Move(from, $"{FilePath}.{i + 1}");
        }
        current.MoveTo($"{FilePath}.1");
    }

    private sealed class JsonFileLogger : ILogger
    {
        private readonly JsonFileLoggerProvider _provider;
        private readonly string _category;

        public JsonFileLogger(JsonFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            _provider._scopes.Push(state);

        // Level filtering is configured on the logging builder (see TrayLogging / the Service host).
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch
            {
                return;
            }
            _provider.Write(_category, logLevel, eventId, state, exception, message);
        }
    }
}
