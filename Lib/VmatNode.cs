using System.Globalization;
using System.Text;

namespace Lib;

/// <summary>
/// Represents a node in a Source 2 KeyValues (VMAT) document. A node can have a value
/// (leaf) or children (sub-table). Values that look like <c>[x y z w]</c> vectors are
/// exposed as <see cref="Vector4"/>.
/// </summary>
public sealed class VmatNode
{
    public VmatNode(string key)
    {
        Key = key;
    }

    public string Key { get; }
    public string? Value { get; set; }
    public List<VmatNode> Children { get; } = new();

    public bool IsContainer => Children.Count > 0;

    public Vector4? TryParseVector()
    {
        if (Value is null) return null;
        return Vector4.TryParse(Value, out var v) ? v : null;
    }

    public float? TryParseFloat()
    {
        if (Value is null) return null;
        return float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
    }

    public int? TryParseInt()
    {
        if (Value is null) return null;
        return int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    public bool? TryParseBool()
    {
        if (Value is null) return null;
        return Value switch
        {
            "1" => true,
            "0" => false,
            _ => bool.TryParse(Value, out var b) ? b : null,
        };
    }

    public VmatNode? FindChild(string name)
    {
        foreach (var child in Children)
        {
            if (string.Equals(child.Key, name, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    /// <summary>
    /// Serialize this tree to KV1 text in Valve's canonical layout
    /// (one TAB per level, value on the same line as its key).
    /// <para>
    /// This used to be a hand-rolled writer that emitted unindented keys with the
    /// value on its own line — the layout Valve does <i>not</i> use. That made the
    /// KV preview, saved files and <see cref="ShaderTemplateEmitter.EmitDefault"/>
    /// all diverge from the formatted <c>Templates/*.vmat</c> on disk. All writing
    /// now goes through ValveKeyValue via <see cref="VmatFormat.Serialize"/>, so
    /// there is exactly one output layout in the project.
    /// </para>
    /// </summary>
    public string Serialize(int indentLevel = 0) => VmatFormat.Serialize(this);

    public override string ToString() => IsContainer ? $"{{{Key}, {Children.Count} children}}" : $"{Key} = {Value}";
}

/// <summary>
/// A 4-component vector used by VMAT scalar/vector parameters.
/// </summary>
public readonly record struct Vector4(float X, float Y, float Z, float W)
{
    public static bool TryParse(string raw, out Vector4 value)
    {
        value = default;
        var s = raw.Trim();
        if (!s.StartsWith('[') || !s.EndsWith(']')) return false;
        var inner = s[1..^1].Trim();
        var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return false;
        float x = 0, y = 0, z = 0, w = 0;
        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
        if (parts.Length > 1 && !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
        if (parts.Length > 2 && !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
        if (parts.Length > 3 && !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) return false;
        value = new Vector4(x, y, z, w);
        return true;
    }

    // Source 2 convention: vectors are always emitted with 6 fixed decimals so the
    // diff with the original .vmat file is whitespace-only.
    public override string ToString() =>
        $"[{X.ToString("0.000000", CultureInfo.InvariantCulture)} {Y.ToString("0.000000", CultureInfo.InvariantCulture)} {Z.ToString("0.000000", CultureInfo.InvariantCulture)} {W.ToString("0.000000", CultureInfo.InvariantCulture)}]";
}
