namespace ONEVO.Agent.TrayApp.Services;

using System.Text.Json;

/// <summary>
/// Bounded, on-disk list of generic notification delivery ids this tray has already shown. Survives a tray
/// restart, so a delivery the service re-offers (because the previous acknowledgement never reached the
/// backend) is confirmed again without showing the toast twice. Keeps only the newest ids.
/// </summary>
public sealed class ShownNotificationLog
{
    public const int DefaultCapacity = 500;

    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "shown-notifications.json");

    private readonly string _path;
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly LinkedList<Guid> _order = new();
    private readonly HashSet<Guid> _ids = new();

    public ShownNotificationLog() : this(DefaultPath)
    {
    }

    public ShownNotificationLog(string path, int capacity = DefaultCapacity)
    {
        _path = path;
        _capacity = Math.Max(1, capacity);
        foreach (var id in Load())
            Remember(id);
    }

    public bool Contains(Guid deliveryId)
    {
        lock (_gate)
            return _ids.Contains(deliveryId);
    }

    public void Add(Guid deliveryId) => TryAdd(deliveryId);

    /// <summary>Atomically records the id; false if it was already there. Overlapping polls (push-triggered and
    /// fallback) therefore cannot both decide to show the same toast.</summary>
    public bool TryAdd(Guid deliveryId)
    {
        lock (_gate)
        {
            if (!Remember(deliveryId))
                return false;
            Save();
            return true;
        }
    }

    /// <summary>Forgets an id whose toast could not be shown, so a later offer can show it.</summary>
    public void Remove(Guid deliveryId)
    {
        lock (_gate)
        {
            if (!_ids.Remove(deliveryId))
                return;
            _order.Remove(deliveryId);
            Save();
        }
    }

    private bool Remember(Guid id)
    {
        if (!_ids.Add(id))
            return false;
        _order.AddLast(id);
        while (_order.Count > _capacity)
        {
            _ids.Remove(_order.First!.Value);
            _order.RemoveFirst();
        }
        return true;
    }

    private IEnumerable<Guid> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<Guid>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (Exception)
        {
            // A corrupt or unreadable file only costs possible repeat toasts - never block notifications.
            return [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_order.ToList()));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception)
        {
            // Best effort: the in-memory set still suppresses repeats for this session.
        }
    }
}
