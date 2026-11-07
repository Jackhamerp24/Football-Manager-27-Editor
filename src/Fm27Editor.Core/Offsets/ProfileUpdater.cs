using System.Text.Json;
using System.Text.Json.Nodes;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Offsets;

/// <summary>One resolved profile value and how it was found, for the PASS/FAIL table.</summary>
public sealed record ResolvedValue(string Path, string? Value, string Via, bool Ok, string? Note);

/// <summary>
/// Rebuilds the static part of a profile for a new game build. Every vtable is re-found by its MSVC RTTI class name
/// and every static root by its formula, so a stripped profile (all RVAs zeroed) is enough input. Field offsets inside
/// objects are copied unchanged from the base profile.
/// </summary>
public static class ProfileUpdater
{
    public const string RttiSection = "rtti";
    public const string RootsSection = "roots";

    /// <summary>Fills <c>rtti</c> with the class name of every vtable RVA currently in the profile.</summary>
    public static (JsonObject Json, IReadOnlyList<ResolvedValue> Rows) Label(JsonNode baseNode, RttiIndex rtti)
    {
        var node = baseNode.DeepClone().AsObject();
        var rows = new List<ResolvedValue>();
        var section = new JsonObject();
        foreach (var (path, value) in VtablePaths(node))
        {
            uint rva = value;
            string? name = rtti.NameOf(rva);
            if (name is null) { rows.Add(new(path, $"0x{rva:X}", "rtti", false, "no RTTI locator at this vtable")); continue; }
            section[path] = name;
            rows.Add(new(path, $"0x{rva:X}", name, true, null));
        }
        node[RttiSection] = section;
        return (node, rows);
    }

    /// <summary>Rebuilds every vtable RVA and root RVA from the profile's RTTI names and root formulas.</summary>
    public static (JsonObject Json, IReadOnlyList<ResolvedValue> Rows) Rebuild(JsonNode baseNode, RttiIndex rtti,
        ModuleImage image, IProcessMemory memory)
    {
        var node = baseNode.DeepClone().AsObject();
        var rows = new List<ResolvedValue>();

        if (RootFinder.FindBuildString(image) is { } buildString)
        {
            node["buildStringRva"] = Hex(buildString);
            rows.Add(new("buildStringRva", Hex(buildString), "image scan", true, null));
        }
        else rows.Add(new("buildStringRva", null, "image scan", false, "build string not found"));

        if (node[RttiSection] is not JsonObject names)
            throw new InvalidOperationException($"Profile has no \"{RttiSection}\" section; run update-profile --label once on a working build.");
        foreach (var (path, classNameNode) in names)
        {
            string className = classNameNode!.GetValue<string>();
            var vtables = rtti.Vtables(className);
            if (vtables.Count != 1)
            {
                rows.Add(new(path, null, className!, false, vtables.Count == 0 ? "class not found" : $"ambiguous: {string.Join(", ", vtables.Select(v => $"0x{v:X}"))}"));
                continue;
            }
            SetPath(node, path, Hex(vtables[0]));
            rows.Add(new(path, Hex(vtables[0]), className!, true, null));
        }

        if (node[RootsSection] is not JsonObject roots)
            throw new InvalidOperationException($"Profile has no \"{RootsSection}\" section; run update-profile --label once on a working build.");
        foreach (var (path, formulaNode) in roots)
        {
            if (formulaNode is not JsonObject formula)
            {
                rows.Add(new(path, null, "formula", false, "formula is not an object"));
                continue;
            }
            string className = formula["vtable"]?.GetValue<string>() ?? "";
            string kind = formula["kind"]?.GetValue<string>() ?? RootFormula.Vector;
            int offset = ParseHex(formula["offset"]);
            int min = formula["min"]?.GetValue<int>() ?? 1;
            int max = formula["max"]?.GetValue<int>() ?? int.MaxValue;
            var vtables = rtti.Vtables(className);
            if (vtables.Count != 1) { rows.Add(new(path, null, $"{className} {kind}", false, "vtable class not unique")); continue; }
            var candidates = kind == RootFormula.Object
                ? RootFinder.FindObject(image, memory, vtables[0])
                : RootFinder.FindVector(image, memory, vtables[0], offset, min, max);
            if (candidates.Count != 1)
            {
                rows.Add(new(path, null, $"{className} {kind}", false,
                    candidates.Count == 0 ? "no slot matches the formula" : $"{candidates.Count} slots match: {string.Join(", ", candidates.Take(6).Select(c => $"0x{c:X}"))}"));
                continue;
            }
            SetPath(node, path, Hex(candidates[0]));
            rows.Add(new(path, Hex(candidates[0]), $"{className} {kind}", true, null));
        }
        return (node, rows);
    }

    static string Hex(uint value) => $"0x{value:X}";

    static int ParseHex(JsonNode? node) =>
        node is null ? 0 : node.GetValueKind() switch
        {
            JsonValueKind.Number => node.GetValue<int>(),
            _ => int.Parse(node.GetValue<string>().Replace("0x", ""), System.Globalization.NumberStyles.HexNumber),
        };

    /// <summary>Every "0x…" value whose key mentions a vtable, plus the entries of <c>playerVtables</c>.</summary>
    public static IEnumerable<(string Path, uint Value)> VtablePaths(JsonNode node)
    {
        var found = new List<(string, uint)>();
        Walk(node, "");
        return found;

        void Walk(JsonNode? current, string path)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        string child = path.Length == 0 ? key : $"{path}.{key}";
                        if (value is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.String &&
                            scalar.GetValue<string>().StartsWith("0x") &&
                            (key.Contains("vtable", StringComparison.OrdinalIgnoreCase) || path.EndsWith("playerVtables", StringComparison.Ordinal)))
                            found.Add((child, ParseUInt(scalar.GetValue<string>())));
                        else Walk(value, child);
                    }
                    break;
                case JsonArray array:
                    for (int i = 0; i < array.Count; i++)
                    {
                        if (array[i] is JsonValue item && item.GetValueKind() == JsonValueKind.String &&
                            item.GetValue<string>().StartsWith("0x"))
                            found.Add(($"{path}[{i}]", ParseUInt(item.GetValue<string>())));
                        else Walk(array[i], $"{path}[{i}]");
                    }
                    break;
            }
        }
    }

    static uint ParseUInt(string text) => uint.Parse(text.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);

    public static JsonNode? GetPath(JsonNode node, string path)
    {
        JsonNode? current = node;
        foreach (var part in path.Split('.'))
        {
            if (current is null) return null;
            int bracket = part.IndexOf('[');
            if (bracket < 0) { current = current[part]; continue; }
            current = current[part[..bracket]];
            foreach (var index in part[(bracket + 1)..^1].Split("][" ).Select(int.Parse))
                current = current?[index];
        }
        return current;
    }

    public static void SetPath(JsonNode node, string path, string value)
    {
        var parts = path.Split('.');
        JsonNode current = node;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            bool last = i == parts.Length - 1;
            int bracket = part.IndexOf('[');
            string key = bracket < 0 ? part : part[..bracket];
            int[] indexes = bracket < 0 ? [] : part[(bracket + 1)..^1].Split("][").Select(int.Parse).ToArray();
            if (indexes.Length == 0)
            {
                if (last) { current[key] = value; return; }
                current = current[key] ?? throw new InvalidOperationException($"Missing {path}");
                continue;
            }
            var array = (current[key] ?? throw new InvalidOperationException($"Missing {path}")).AsArray();
            for (int a = 0; a < indexes.Length - 1; a++) array = array[indexes[a]]!.AsArray();
            if (last) array[indexes[^1]] = value;
            else current = array[indexes[^1]] ?? throw new InvalidOperationException($"Missing {path}");
        }
    }
}
