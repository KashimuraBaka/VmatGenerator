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

    private static readonly KVSerializer Serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

    /// <summary>
    /// Parse <paramref name="source"/> and re-emit it in Valve's canonical layout.
    /// Throws <see cref="System.ArgumentException"/> when the input is not valid KeyValues1.
    /// </summary>
    public static string Format(string source)
    {
        var (document, _) = Serializer.DeserializeWithSourceMap(source);
        return Write(document);
    }

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

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, collection, document.Name ?? string.Empty);
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
        var (docA, _) = Serializer.DeserializeWithSourceMap(a);
        var (docB, _) = Serializer.DeserializeWithSourceMap(b);
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