using DesktopOrbit.Core.Config;
using DesktopOrbit.Core.Models;
using DesktopOrbit.Core.Search;

namespace DesktopOrbit.Shell.Search;

public sealed class SearchResult
{
    public required string Path { get; init; }
    public required string Label { get; init; }
    public required ItemKind Kind { get; init; }
    public required string Folder { get; init; }
    public int Score { get; init; }
}

/// <summary>
/// Local index over the configured roots. Depth and entry count are bounded so
/// the scan can never stall the UI on a huge tree; results are scored with the
/// shared fuzzy matcher so Chinese and pinyin-free substring queries both work.
/// </summary>
public sealed class SearchIndex
{
    private sealed record Entry(string Path, string Label, ItemKind Kind, string Folder);

    private readonly List<Entry> _entries = new();
    private readonly object _gate = new();
    private volatile bool _built;

    public bool IsBuilt => _built;
    public int Count => _entries.Count;

    public void Build(IEnumerable<string> roots, SearchOptions options)
    {
        var collected = new List<Entry>();
        int budget = Math.Max(1000, options.MaxIndexEntries);

        foreach (string root in roots)
        {
            if (collected.Count >= budget) break;
            if (!Directory.Exists(root)) continue;

            try
            {
                Collect(root, 0, Math.Max(1, options.MaxDepth), budget, collected);
            }
            catch (Exception)
            {
            }
        }

        lock (_gate)
        {
            _entries.Clear();
            _entries.AddRange(collected);
        }

        _built = true;
    }

    private static void Collect(string directory, int depth, int maxDepth, int budget, List<Entry> sink)
    {
        if (sink.Count >= budget) return;

        var enumeration = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(directory, "*", enumeration);
        }
        catch (Exception)
        {
            return;
        }

        foreach (string child in children)
        {
            if (sink.Count >= budget) return;
            if (IsNoise(child)) continue;

            bool isFolder;
            try
            {
                isFolder = Directory.Exists(child);
            }
            catch (Exception)
            {
                continue;
            }

            string label = Path.GetFileName(child);
            sink.Add(new Entry(child, label, isFolder ? ItemKind.Folder : KindOf(child), directory));

            if (isFolder && depth + 1 < maxDepth)
            {
                Collect(child, depth + 1, maxDepth, budget, sink);
            }
        }
    }

    private static bool IsNoise(string path)
    {
        string name = Path.GetFileName(path);
        return name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
            || name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    private static ItemKind KindOf(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return ItemKind.Shortcut;
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) return ItemKind.Url;
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return ItemKind.Application;
        return ItemKind.File;
    }

    public IReadOnlyList<SearchResult> Query(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchResult>();

        query = query.Trim();
        var results = new List<(Entry Entry, int Score)>();

        lock (_gate)
        {
            foreach (Entry entry in _entries)
            {
                int score = FuzzyMatcher.Score(entry.Label, query);
                if (score <= 0) continue;

                if (entry.Kind == ItemKind.Folder) score += 40;
                results.Add((entry, score));
            }
        }

        return results
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.Label.Length)
            .Take(Math.Max(1, maxResults))
            .Select(item => new SearchResult
            {
                Path = item.Entry.Path,
                Label = item.Entry.Label,
                Kind = item.Entry.Kind,
                Folder = item.Entry.Folder,
                Score = item.Score
            })
            .ToArray();
    }
}
