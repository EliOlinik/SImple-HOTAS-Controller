using System.Text.Json;

namespace HotasController.Core.Diagnostics;

/// <summary>One difference between two device snapshots.</summary>
public sealed record SnapshotDifference(string Path, string? Before, string? After)
{
    public string Kind => Before is null ? "added" : After is null ? "removed" : "changed";
}

/// <summary>
/// Compares two snapshot JSON documents (written by the Dump tool's <c>--snapshot</c> mode) by flattening
/// each to <c>path = value</c> pairs. Pure logic so it can be tested without hardware.
/// </summary>
public static class SnapshotDiff
{
    /// <summary>Top-level keys that always differ between two runs and say nothing about the device.</summary>
    private static readonly string[] IgnoredTopLevel = ["capturedAt", "label", "tool"];

    public static IReadOnlyList<SnapshotDifference> Compare(string beforeJson, string afterJson)
    {
        var before = Flatten(beforeJson);
        var after = Flatten(afterJson);

        var result = new List<SnapshotDifference>();
        foreach (var path in before.Keys.Union(after.Keys).OrderBy(p => p, StringComparer.Ordinal))
        {
            before.TryGetValue(path, out var b);
            after.TryGetValue(path, out var a);
            if (!string.Equals(b, a, StringComparison.Ordinal)) result.Add(new SnapshotDifference(path, b, a));
        }
        return result;
    }

    /// <summary>Flattens JSON to dotted paths (arrays as <c>[i]</c>). Throws <see cref="JsonException"/> on invalid JSON.</summary>
    public static SortedDictionary<string, string> Flatten(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Walk(doc.RootElement, "", map, topLevel: true);
        return map;
    }

    private static void Walk(JsonElement element, string path, SortedDictionary<string, string> map, bool topLevel = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (topLevel && IgnoredTopLevel.Contains(property.Name, StringComparer.Ordinal)) continue;
                    Walk(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", map);
                }
                // An empty object still matters ("this node has no properties").
                if (!element.EnumerateObject().Any() && path.Length > 0) map[path] = "{}";
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray()) Walk(item, $"{path}[{index++}]", map);
                if (index == 0) map[path] = "[]";
                break;
            case JsonValueKind.Null:
                map[path] = "null";
                break;
            default:
                map[path] = element.ToString();
                break;
        }
    }
}
