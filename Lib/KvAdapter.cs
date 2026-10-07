using ValveKeyValue;

namespace Lib;

/// <summary>
/// Bidirectional adapter between ValveKeyValue's <see cref="KVObject"/> tree and the
/// library's own <see cref="VmatNode"/> tree. ValveKeyValue is the canonical parser
/// for Source 2 KeyValues; the adapter's only job is to expose its result in a form
/// the UI can bind to (<see cref="VmatNode"/>) and to ingest <see cref="VmatNode"/>
/// trees produced by the editor / <see cref="VmatGenerator"/>.
///
/// <para><b>写出用 KV1，读取同时兼容 KV1 与 KV3。</b>
/// 写出固定使用 <see cref="KVSerializationFormat.KeyValues1Text"/>：文档名会被库写成
/// 正文的顶层键（<c>"Layer0" { … }</c>），不需要 KV3 那种
/// <c>&lt;!-- kv3 encoding:text:version{…} format:generic:version{…} --&gt;</c> 头。
/// 读取仍两条路都要走——KV3 文本<b>必须</b>以该头开头（缺头直接抛
/// <see cref="KeyValueException"/>），而磁盘上的既有文件多半是没有这个头的 KV1，
/// 因此先按内容判定格式再解析。</para>
/// </summary>
public static class KvAdapter
{
    private static readonly KVSerializer Kv3Serializer =
        KVSerializer.Create(KVSerializationFormat.KeyValues3Text);

    private static readonly KVSerializer Kv1Serializer =
        KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

    /// <summary>
    /// KV3 文本的头部标记；也是判定一份文本「是不是 KV3」最可靠的依据。
    /// 写出时该头由库自动生成，不需要手工拼接。
    /// </summary>
    private const string Kv3HeaderMarker = "<!--";

    /// <summary>Source 2 .vmat 的顶层节点名。</summary>
    private const string Layer0Key = "Layer0";

    /// <summary>
    /// Parse KV1 or KV3 text via ValveKeyValue and convert the resulting tree to a
    /// <see cref="VmatNode"/>. The document's root key is preserved.
    /// </summary>
    /// <remarks>
    /// 判定顺序：文本以 <c>&lt;!--</c> 开头才按 KV3 试解析。即便带了头，
    /// 正文损坏时也会回落到 KV1 再试一次，避免把「还能读的旧文件」误判成坏文件。
    /// </remarks>
    public static VmatNode ParseViaValve(string text)
    {
        // Use the string-based source-map overload — the stream-based Deserialize
        // path drops trailing content (which most VMAT files have after the closing
        // brace) and returns a KVDocument with Root = null. The string overload
        // tolerates the trailing newline correctly.
        if (text.TrimStart().StartsWith(Kv3HeaderMarker, StringComparison.Ordinal))
        {
            try
            {
                return BuildRoot(Kv3Serializer.DeserializeWithSourceMap(text));
            }
            catch (KeyValueException)
            {
                // 头在但正文坏了（多半是手工改坏），仍给 KV1 一次机会。
            }
        }
        return BuildRoot(Kv1Serializer.DeserializeWithSourceMap(text));
    }

    /// <summary>
    /// Serialize a <see cref="VmatNode"/> to <b>KV1</b> text via ValveKeyValue.
    /// </summary>
    /// <remarks>
    /// <para><b>文档名就是顶层键：</b>KV1 会把传给库的文档名逐字写成正文的第一层
    /// （<c>"Layer0" { … }</c>），所以这里把根节点的键（缺省用 <c>Layer0</c>）作为
    /// 文档名传入、把它的直接子节点集合作为正文载荷，输出恰好一层 <c>Layer0</c>，
    /// 与 Valve 出厂材质逐字节同形。这与 KV3 相反——KV3 顶层是匿名对象、不写文档名，
    /// 才需要手工补一层 <c>Layer0</c> 节点。</para>
    /// </remarks>
    public static string SerializeViaValve(VmatNode root)
    {
        string documentName = string.IsNullOrEmpty(root.Key) ? Layer0Key : root.Key;

        KVObject payload;
        if (root.IsContainer)
        {
            // 容器根：根键交给文档名，正文只放子节点，避免 Layer0 出现两层。
            var inner = KVObject.ListCollection();
            foreach (var child in root.Children)
                inner.Add(child.Key, ToKVObjectInternal(child));
            payload = inner;
        }
        else
        {
            // 叶子根（理论形态）：包一层集合，键名照常写入正文。
            var wrapped = KVObject.ListCollection();
            wrapped.Add(documentName, ToKVObjectInternal(root));
            payload = wrapped;
        }

        using var ms = new MemoryStream();
        Kv1Serializer.Serialize(ms, payload, documentName);
        ms.Position = 0;
        using var sr = new StreamReader(ms);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// Convenience: full round-trip — parse, serialize, parse again, and verify the two
    /// <see cref="VmatNode"/> trees are structurally equivalent.
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

    /// <summary>
    /// 把库返回的文档转成本库的节点树，必要时把正文里的 <c>Layer0</c> 提升为根节点。
    /// </summary>
    /// <remarks>
    /// 本库的内存模型是「根节点自己就叫 <c>Layer0</c>，参数是它的直接子节点」
    /// （<see cref="VmatDocument.ShaderName"/> 就依赖这一点在根的子节点里找 shader）。
    /// KV1 文件把文档名写进正文，读回来天然就是「名为 Layer0 的根」，无需提升；
    /// 而 KV3 文件的顶层是匿名对象、<c>Layer0</c> 是正文中一个真实子节点，
    /// 不做这一步提升，KV3 文件读进来就会变成「无名根 + 一个 Layer0 子节点」，
    /// 编辑器随后找不到 shader。
    /// <para>只在「正好一个 Layer0 子节点」时提升；出现 Layer1 等多层时保持原样，
    /// 因为本库的内存模型只承载单根。</para>
    /// </remarks>
    private static VmatNode BuildRoot((KVDocument Document, object _) parsed)
    {
        var root = new VmatNode(parsed.Document.Name ?? string.Empty);
        CopyChildren(parsed.Document.Root, root);

        if (root.Key.Length == 0
            && root.Children.Count == 1
            && string.Equals(root.Children[0].Key, Layer0Key, StringComparison.Ordinal)
            && root.Children[0].IsContainer)
        {
            var lifted = new VmatNode(Layer0Key);
            lifted.Children.AddRange(root.Children[0].Children);
            return lifted;
        }
        return root;
    }

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