using System.Globalization;

namespace Lib;

/// <summary>
/// Public entry points for parsing and serializing Source 2 KeyValues documents
/// (VMAT). Per project guidance all parsing is delegated to ValveKeyValue (the
/// official Valve format library) via <see cref="KvAdapter"/>; the legacy
/// hand-rolled parser has been retired to reduce maintenance surface.
/// </summary>
public static class VmatFormat
{
    /// <summary>
    /// Parse a VMAT text document. Thin wrapper around
    /// <see cref="KvAdapter.ParseViaValve"/> for backwards compatibility with
    /// callers that imported <c>VmatFormat.Parse</c>.
    /// </summary>
    public static VmatNode Parse(string source) => KvAdapter.ParseViaValve(source);

    /// <summary>
    /// Serialize a VMAT node tree back to text. Routed through ValveKeyValue's
    /// serializer so the output always matches Valve's canonical layout — see
    /// <see cref="VmatFormatter"/> for the exact shape and the guarantees.
    /// </summary>
    public static string Serialize(VmatNode root) => KvAdapter.SerializeViaValve(root);

    /// <summary>
    /// Convenience for code that wants to validate ValveKeyValue compatibility:
    /// serialize via the official library and parse it back via the official
    /// library, returning the parsed <see cref="VmatNode"/> tree.
    /// </summary>
    public static (string Text, VmatNode Parsed) SerializeAndReParseViaValve(VmatNode root)
    {
        var text = KvAdapter.SerializeViaValve(root);
        var reparsed = KvAdapter.ParseViaValve(text);
        return (text, reparsed);
    }
}

/// <summary>
/// Helper extensions for working with parsed <see cref="VmatNode"/> trees.
/// </summary>
public static class VmatNodeExtensions
{
    /// <inheritdoc/>
    public static IEnumerable<VmatNode> Flatten(this VmatNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var desc in child.Flatten())
                yield return desc;
        }
    }

    /// <inheritdoc/>
    public static string ToKeyValueText(this VmatNode root) => VmatFormat.Serialize(root);

    /// <inheritdoc/>
    public static float GetFloat(this VmatNode node, string key, float fallback = 0f)
    {
        var c = node.FindChild(key);
        return c?.TryParseFloat() ?? fallback;
    }

    /// <inheritdoc/>
    public static int GetInt(this VmatNode node, string key, int fallback = 0)
    {
        var c = node.FindChild(key);
        return c?.TryParseInt() ?? fallback;
    }

    /// <inheritdoc/>
    public static bool GetBool(this VmatNode node, string key, bool fallback = false)
    {
        var c = node.FindChild(key);
        return c?.TryParseBool() ?? fallback;
    }

    /// <inheritdoc/>
    public static string GetString(this VmatNode node, string key, string fallback = "")
    {
        var c = node.FindChild(key);
        return c?.Value ?? fallback;
    }

    /// <inheritdoc/>
    public static Vector4 GetVector(this VmatNode node, string key, Vector4? fallback = null)
    {
        var c = node.FindChild(key);
        return c is null ? fallback ?? default : c.TryParseVector() ?? fallback ?? default;
    }

    /// <inheritdoc/>
    public static void SetFloat(this VmatNode node, string key, float value)
    {
        var c = node.FindChild(key) ?? node.AddChild(key);
        c.Value = value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public static void SetInt(this VmatNode node, string key, int value)
    {
        var c = node.FindChild(key) ?? node.AddChild(key);
        c.Value = value.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public static void SetBool(this VmatNode node, string key, bool value)
    {
        var c = node.FindChild(key) ?? node.AddChild(key);
        c.Value = value ? "1" : "0";
    }

    /// <inheritdoc/>
    public static void SetString(this VmatNode node, string key, string value)
    {
        var c = node.FindChild(key) ?? node.AddChild(key);
        c.Value = value;
    }

    /// <inheritdoc/>
    public static void SetVector(this VmatNode node, string key, Vector4 value)
    {
        var c = node.FindChild(key) ?? node.AddChild(key);
        c.Value = value.ToString();
    }

    /// <inheritdoc/>
    public static VmatNode AddChild(this VmatNode node, string key)
    {
        var existing = node.FindChild(key);
        if (existing is not null) return existing;
        var child = new VmatNode(key);
        node.Children.Add(child);
        return child;
    }

    /// <inheritdoc/>
    public static void RemoveChild(this VmatNode node, string key) => node.Children.RemoveAll(c => string.Equals(c.Key, key, StringComparison.Ordinal));

    /// <inheritdoc/>
    public static VmatNode FirstContainer(this VmatNode node) => node.Children.FirstOrDefault(c => c.IsContainer) ?? node;
}