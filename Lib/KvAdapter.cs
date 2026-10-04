using ValveKeyValue;

namespace Lib;

/// <summary>
/// Bidirectional adapter between ValveKeyValue's <see cref="KVObject"/> tree and the
/// library's own <see cref="VmatNode"/> tree. ValveKeyValue is the canonical parser
/// for Source 2 KeyValues; the adapter's only job is to expose its result in a form
/// the UI can bind to (<see cref="VmatNode"/>) and to ingest <see cref="VmatNode"/>
/// trees produced by the editor / <see cref="VmatGenerator"/>.
/// </summary>
public static class KvAdapter
{
    private static readonly KVSerializer Serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

    /// <summary>
    /// Parse KV1 text via ValveKeyValue and convert the resulting tree to a
    /// <see cref="VmatNode"/>. The document's root key (if any) is preserved on the
    /// returned node's <see cref="VmatNode.Key"/>.
    /// </summary>
    public static VmatNode ParseViaValve(string text)
    {
        // Use the string-based source-map overload — the stream-based Deserialize
        // path drops trailing content (which most VMAT files have after the closing
        // brace) and returns a KVDocument with Root = null. The string overload
        // tolerates the trailing newline correctly.
        var (document, _) = Serializer.DeserializeWithSourceMap(text);
        var node = new VmatNode(document.Name ?? string.Empty);
        CopyChildren(document.Root, node);
        return node;
    }

    /// <summary>
    /// Serialize a <see cref="VmatNode"/> to KV1 text via ValveKeyValue and return the
    /// resulting string. The root key is supplied to the serializer so the document
    /// round-trips with the same opening name.
    /// </summary>
    public static string SerializeViaValve(VmatNode root)
    {
        var collection = KVObject.ListCollection();
        foreach (var child in root.Children)
            collection.Add(child.Key, ToKVObjectInternal(child));
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, collection, root.Key);
        ms.Position = 0;
        using var sr = new StreamReader(ms);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// Convenience: full round-trip — parse via ValveKeyValue, serialize via
    /// ValveKeyValue, parse the result again, and verify the two <see cref="VmatNode"/>
    /// trees are structurally equivalent.
    /// </summary>
    public static bool RoundTripEquals(string kvText)
    {
        var first = ParseViaValve(kvText);
        var re = SerializeViaValve(first);
        var second = ParseViaValve(re);
        return StructuralEquals(first, second);
    }

    /// <summary>
    /// Field-by-field comparison of two VmatNode trees for use in round-trip tests.
    /// Compares key, value, child count, and recursive structure.
    /// </summary>
    public static bool StructuralEquals(VmatNode a, VmatNode b)
    {
        if (!string.Equals(a.Key, b.Key, StringComparison.Ordinal)) return false;
        if (!string.Equals(a.Value ?? string.Empty, b.Value ?? string.Empty, StringComparison.Ordinal)) return false;
        if (a.Children.Count != b.Children.Count) return false;
        for (int i = 0; i < a.Children.Count; i++)
            if (!StructuralEquals(a.Children[i], b.Children[i])) return false;
        return true;
    }

    // ─── internal helpers ───────────────────────────────────────────────────

    private static KVObject ToKVObjectInternal(VmatNode node)
    {
        if (node.IsContainer)
        {
            var collection = KVObject.ListCollection();
            foreach (var c in node.Children)
                collection.Add(c.Key, ToKVObjectInternal(c));
            return collection;
        }
        // Implicit string conversion produces a scalar KVObject.
        return (KVObject)(node.Value ?? string.Empty);
    }

    private static void CopyChildren(KVObject src, VmatNode dst)
    {
        foreach (var kvp in src)
        {
            var name = kvp.Key ?? string.Empty;
            var child = new VmatNode(name);
            var value = kvp.Value;
            switch (value.ValueType)
            {
                case KVValueType.Collection:
                    CopyChildren(value, child);
                    break;
                case KVValueType.Array:
                    {
                        // KV1 has no native arrays; numeric keys are used. Flatten into a
                        // single anonymous container so the structure round-trips.
                        var arr = new VmatNode(string.Empty);
                        int i = 0;
                        foreach (var el in value)
                        {
                            var itemChild = new VmatNode(i.ToString());
                            if (el.Value.ValueType == KVValueType.Collection)
                                CopyChildren(el.Value, itemChild);
                            else
                                itemChild.Value = el.Value.ToString();
                            arr.Children.Add(itemChild);
                            i++;
                        }
                        child.Children.Add(arr);
                        break;
                    }
                default:
                    child.Value = value.ToString();
                    break;
            }
            dst.Children.Add(child);
        }
    }
}