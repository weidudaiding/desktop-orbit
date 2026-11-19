using System.Text.Json;
using DesktopOrbit.Core.Models;
using DesktopOrbit.Core.Recent;

namespace DesktopOrbit.Shell.Shell;

public sealed class RecentStore
{
    private sealed class UsageRecord
    {
        public int Count { get; set; }
        public DateTime LastUsedUtc { get; set; }
    }

    private readonly string _file;
    private readonly Dictionary<string, UsageRecord> _usage = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _dirty;
    private DateTime _lastFlush = DateTime.UtcNow;

    public RecentStore(string file)
    {
        _file = file;
        Load();
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopOrbit", "usage.json");

    public void Record(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_gate)
        {
            if (!_usage.TryGetValue(path, out UsageRecord? record))
            {
                record = new UsageRecord();
                _usage[path] = record;
            }

            record.Count++;
            record.LastUsedUtc = DateTime.UtcNow;
            _dirty = true;

            if ((DateTime.UtcNow - _lastFlush).TotalSeconds > 5)
            {
                FlushLocked();
            }
        }
    }

    public IReadOnlyList<string> Top(int limit, DateTime? nowUtc = null)
    {
        DateTime now = nowUtc ?? DateTime.UtcNow;
        lock (_gate)
        {
            return _usage
                .Select(pair => (pair.Key, Score: Frecency.Score(pair.Value.Count, pair.Value.LastUsedUtc, now)))
                .OrderByDescending(item => item.Score)
                .Take(Math.Max(0, limit))
                .Select(item => item.Key)
                .ToArray();
        }
    }

    public IReadOnlyList<MenuItem> TopExisting(int limit)
    {
        var results = new List<MenuItem>();
        foreach (string path in Top(limit * 3))
        {
            if (results.Count >= limit) break;
            bool isFolder = Directory.Exists(path);
            if (!isFolder && !File.Exists(path)) continue;

            results.Add(new MenuItem
            {
                Path = path,
                Label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),
                Kind = isFolder ? ItemKind.Folder : ItemKind.File
            });
        }
        return results;
    }

    public IReadOnlyList<MenuItem> RecentFromShell(int limit)
    {
        string recentDir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (!Directory.Exists(recentDir)) return Array.Empty<MenuItem>();

        var results = new List<MenuItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string link in Directory.EnumerateFiles(recentDir, "*.lnk")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(limit * 4))
            {
                string? target = ShortcutResolver.TryResolveFromLinkFile(link);
                if (target is null || !seen.Add(target)) continue;

                bool isFolder = Directory.Exists(target);
                results.Add(new MenuItem
                {
                    Path = target,
                    Label = Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar)),
                    Kind = isFolder ? ItemKind.Folder : ItemKind.File
                });

                if (results.Count >= limit) break;
            }
        }
        catch (Exception)
        {
        }

        return results;
    }

    public void Flush()
    {
        lock (_gate) FlushLocked();
    }

    private void FlushLocked()
    {
        if (!_dirty) return;
        try
        {
            string? dir = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var payload = _usage.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            string temp = _file + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _file, overwrite: true);
            _dirty = false;
            _lastFlush = DateTime.UtcNow;
        }
        catch (Exception)
        {
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            string json = File.ReadAllText(_file);
            var payload = JsonSerializer.Deserialize<Dictionary<string, UsageRecord>>(json);
            if (payload is null) return;
            foreach (var pair in payload)
            {
                _usage[pair.Key] = pair.Value;
            }
        }
        catch (Exception)
        {
        }
    }
}
