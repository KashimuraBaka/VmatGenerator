using System.Text;
using ValveKeyValue;

namespace Lib;

/// <summary>
/// Normalizes VMAT (KeyValues1) text into Valve's canonical layout.
///
/// Valve ships its materials in a very specific shape:
/// <code>
/// "Layer0"
/// {
/// →"shader"→"csgo_vertexlitgeneric.vfx"
/// →"Attributes"
/// →{
/// →→"mapbuilder.nodraw"→"1"
/// →}
/// }
/// </code>
/// i.e. one TAB per nesting level, and the value placed on the same line as its key.
/// Hand-written or machine-generated files often drift from that (unindented keys,
/// values pushed onto their own lines, mixed indentation), which makes diffs noisy
/// and hides real changes.
///
/// The implementation deliberately does not re-implement any parsing: it reads with
/// ValveKeyValue — the canonical Source 2 KeyValues library — and writes with the
/// same library's serializer, so formatting can never change the document's meaning.
///
/// <para><b>写出走 KV1，读取 KV1 / KV3 都接受。</b>
/// 写出固定输出 KeyValues1 文本（顶层键 <c>"Layer0"</c>，无文件头）。
/// KV3 文本必须带 <c>&lt;!-- kv3 encoding:text:version{…} --&gt;</c> 头，缺头库会直接抛异常，
/// 所以解析仍要先按内容判定格式；读到 KV3 文档时写回会转成 KV1。</para>
///
/// <para><b>Known limitations</b> (inherited from ValveKeyValue 0.71):</para>
/// <list type="bullet">
/// <item>Comments are dropped — <c>KVDocument</c> exposes no comment collection and
/// <c>KVSerializerOptions</c> has no <c>PreserveComments</c>.</item>
/// <item>Duplicate keys are preserved (Valve permits them), but formatting cannot
/// re-attach a comment to any specific entry.</item>
/// </list>
/// </summary>
public static class VmatFormatter
{
    /// <summary>Tab indent per nesting level, matching Valve's own material files.</summary>
    public const string Indent = "\t";

    private static readonly KVSerializer Kv3Serializer =
        KVSerializer.Create(KVSerializationFormat.KeyValues3Text);

    private static readonly KVSerializer Kv1Serializer =
        KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

    /// <summary>按内容判定格式并解析：带 KV3 头的按 KV3 读，否则按 KV1 读。</summary>
    private static KVDocument ParseDocument(string source) =>
        source.TrimStart().StartsWith("<!--", StringComparison.Ordinal)
            ? TryKv3(source) ?? Kv1Serializer.DeserializeWithSourceMap(source).Document
            : Kv1Serializer.DeserializeWithSourceMap(source).Document;

    /// <summary>按 KV3 解析；失败返回 <c>null</c>，由调用方回落到 KV1。</summary>
    private static KVDocument? TryKv3(string source)
    {
        try
        {
            return Kv3Serializer.DeserializeWithSourceMap(source).Document;
        }
        catch (KeyValueException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse <paramref name="source"/> and re-emit it in Valve's canonical layout.
    /// Throws <see cref="System.ArgumentException"/> when the input is not valid KeyValues.
    /// </summary>
    public static string Format(string source) => Write(ParseDocument(source));

    /// <summary>Format a file in place, writing UTF-8 without a BOM and with LF line endings.</summary>
    /// <returns><c>true</c> when the file's bytes actually changed.</returns>
    public static bool FormatFile(string path)
    {
        var original = File.ReadAllText(path);
        var formatted = Format(original);
        if (string.Equals(original, formatted, StringComparison.Ordinal)) return false;

        // Explicit LF + UTF-8 without BOM: Valve's own files use neither CRLF nor a BOM,
        // and a BOM makes the first key unparseable by some Source 2 tools.
        File.WriteAllText(path, formatted, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    /// <summary>
    /// Serialize an already-parsed document. Exposed so callers that need to inspect
    /// the tree before writing can skip the parse step.
    /// </summary>
    public static string Write(KVDocument document)
    {
        // ListCollection (not Collection) is mandatory: Valve allows the same key to
        // appear twice at one level, and KVObject.Collection throws on duplicates.
        var collection = KVObject.ListCollection();
        foreach (var entry in document.Root)
            collection.Add(entry.Key ?? string.Empty, entry.Value);

        // KV1 把文档名逐字写成正文的顶层键。KV1 来源的文档带着该键（"Layer0"），
        // 直接照写即可；KV3 来源的文档顶层是匿名对象、没有文档名，此时若正文恰好
        // 只有一个集合子节点，就拿它的键当文档名并把它的子节点升为正文，
        // 避免写出空引号 "" 当顶层键——即完成 KV3 → KV1 的转换。
        string name = document.Name ?? string.Empty;
        var body = collection;
        if (name.Length == 0)
        {
            var first = default(KeyValuePair<string, KVObject>);
            bool single = false;
            using (var e = collection.GetEnumerator())
            {
                if (e.MoveNext())
                {
                    first = e.Current;
                    single = !e.MoveNext() && first.Value.ValueType == KVValueType.Collection;
                }
            }
            if (single)
            {
                name = first.Key;
                body = KVObject.ListCollection();
                foreach (var entry in first.Value)
                    body.Add(entry.Key ?? string.Empty, entry.Value);
            }
            else
            {
                name = "Layer0";
            }
        }

        using var ms = new MemoryStream();
        Kv1Serializer.Serialize(ms, body, name);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Report whether the source already matches canonical formatting, i.e. whether
    /// <see cref="Format"/> would be a no-op.
    /// </summary>
    public static bool IsCanonical(string source) =>
        string.Equals(Format(source), source, StringComparison.Ordinal);

    /// <summary>
    /// Structural equality between two VMAT texts, ignoring layout. Both sides are
    /// parsed with ValveKeyValue and compared key-by-key in document order — which is
    /// what makes this safe as a guard: a value changing is a failure, a tab changing is not.
    /// </summary>
    public static bool Equivalent(string a, string b)
    {
        var docA = ParseDocument(a);
        var docB = ParseDocument(b);
        return Same(docA, docB);
    }

    private static bool Same(KVDocument a, KVDocument b)
    {
        if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal)) return false;
        return SameChildren(a.Root, b.Root);
    }

    private static bool SameChildren(KVObject a, KVObject b)
    {
        int n = 0, m = 0;
        var ea = a.GetEnumerator();
        var eb = b.GetEnumerator();
        while (true)
        {
            bool ha = ea.MoveNext(), hb = eb.MoveNext();
            if (ha != hb) return false;
            if (!ha) return true;

            var ka = ea.Current.Key ?? string.Empty;
            var kb = eb.Current.Key ?? string.Empty;
            if (!string.Equals(ka, kb, StringComparison.Ordinal)) return false;
            if (!SameValue(ea.Current.Value, eb.Current.Value)) return false;
            n++; m++;
        }
    }

    private static bool SameValue(KVObject a, KVObject b)
    {
        // Compare by rendered text so that "1" and 1 (which serialize identically)
        // are treated the same, while ordering and nesting still have to match.
        if (a.ValueType != b.ValueType) return false;
        if (a.ValueType == KVValueType.Collection) return SameChildren(a, b);
        if (a.ValueType == KVValueType.Array) return SameArray(a, b);
        return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    private static bool SameArray(KVObject a, KVObject b)
    {
        // Array elements enumerate as KeyValuePair<string, KVObject>.
        var ea = a.GetEnumerator();
        var eb = b.GetEnumerator();
        while (true)
        {
            bool ha = ea.MoveNext(), hb = eb.MoveNext();
            if (ha != hb) return false;
            if (!ha) return true;
            if (!string.Equals(ea.Current.Key ?? string.Empty, eb.Current.Key ?? string.Empty, StringComparison.Ordinal)) return false;
            if (!SameValue(ea.Current.Value, eb.Current.Value)) return false;
        }
    }
}