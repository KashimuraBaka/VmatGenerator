using System.Globalization;

namespace Lib;

/// <summary>
/// Recursively discovers <c>.vmat</c> files under a root directory, parses each
/// file, and groups them by shader so the UI can show a "shader &rarr; materials"
/// hierarchy.
/// </summary>
public sealed class MaterialScanner
{
    public IReadOnlyList<VmatDocument> Scan(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<VmatDocument>();
        var results = new List<VmatDocument>();
        foreach (var path in Directory.EnumerateFiles(root, "*.vmat", SearchOption.AllDirectories))
        {
            try
            {
                var text = File.ReadAllText(path);
                var root2 = VmatFormat.Parse(text);
                results.Add(new VmatDocument(path, root2));
            }
            catch (Exception ex)
            {
                var errNode = new VmatNode("Layer0");
                errNode.Value = ex.Message;
                results.Add(new VmatDocument(path, errNode));
            }
        }
        return results;
    }

    public IEnumerable<(string Shader, IReadOnlyList<VmatDocument> Materials)> GroupByShader(IEnumerable<VmatDocument> docs) =>
        docs
            .GroupBy(d => string.IsNullOrEmpty(d.ShaderName) ? "(no shader)" : d.ShaderName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, (IReadOnlyList<VmatDocument>)g.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()));
}

/// <summary>
/// Builds a complete <see cref="VmatNode"/> document from a <see cref="ShaderTemplate"/>
/// plus a parameter dictionary, then renders it back out in the Valve KeyValues form
/// the workspace files use.
/// </summary>
public sealed class VmatGenerator
{
    /// <summary>
    /// Build a <c>Layer0</c> document from the supplied template, value map, and feature
    /// / attribute selections. Includes the <c>Compiled Textures</c> and
    /// <c>SystemAttributes</c> sub-blocks when the template defines them, so the
    /// emitted text is a complete Source 2 VMAT.
    /// </summary>
    public VmatNode BuildDocument(
        ShaderTemplate shader,
        IDictionary<string, string> values,
        IEnumerable<string>? enabledFeatureFlags = null,
        IEnumerable<string>? enabledAttributeFlags = null,
        IDictionary<string, string>? systemAttributeOverrides = null,
        IDictionary<string, string>? compiledTextureOverrides = null)
    {
        var root = new VmatNode("Layer0");
        root.SetString("shader", shader.ShaderName);

        var enabledFlags = new HashSet<string>(enabledFeatureFlags ?? Array.Empty<string>(), StringComparer.Ordinal);
        var disabledFlags = new HashSet<string>(shader.FeatureFlags, StringComparer.Ordinal);
        foreach (var flag in enabledFlags) disabledFlags.Remove(flag);

        // Feature flags come first so they show up near the top of the file (this is the
        // convention the workspace samples use).
        foreach (var flag in shader.FeatureFlags)
        {
            var c = root.AddChild(flag);
            c.Value = enabledFlags.Contains(flag) ? "1" : "0";
        }

        // Parameters: include every param regardless of subgroup gating so the
        // template text contains the full contract; downstream code can filter for UI.
        foreach (var p in shader.Parameters)
        {
            string? raw = null;
            if (values.TryGetValue(p.Key, out var v) && !string.IsNullOrEmpty(v))
                raw = v;
            else if (!string.IsNullOrEmpty(p.DefaultValue))
                raw = p.DefaultValue;
            if (raw is null) continue;
            // Skip keys that are also feature flags AND were already emitted as a
            // feature flag above — the feature flag loop is the authoritative
            // source of truth for those (it honours enabledFlags).
            if (shader.FeatureFlags.Contains(p.Key) && root.FindChild(p.Key) is not null)
                continue;
            var child = root.AddChild(p.Key);
            child.Value = NormalizeValue(p, raw);
        }

        // Compiled Textures: always present so the Source 2 compiler's automatic keys
        // are not accidentally stripped. Values come from user overrides or are left
        // empty for the user to fill in.
        var compiled = root.AddChild("Compiled Textures");
        var compiledKeys = new HashSet<string>(shader.CompiledTextureKeys, StringComparer.Ordinal);
        // Compute the mapping TextureFoo -> g_tFoo for every selected texture value.
        var compiledValueMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in shader.Parameters)
        {
            if (p.Kind != ShaderParamKind.Texture) continue;
            if (!values.TryGetValue(p.Key, out var tex) || string.IsNullOrEmpty(tex)) continue;
            foreach (var key in MapToCompiledKey(shader.ShaderName, p.Key))
            {
                compiledKeys.Add(key);
                compiledValueMap[key] = tex + ".vtex";
            }
        }
        foreach (var key in compiledKeys)
        {
            string? cVal = null;
            if (compiledTextureOverrides is not null && compiledTextureOverrides.TryGetValue(key, out var ov) && !string.IsNullOrEmpty(ov))
                cVal = ov;
            else if (compiledValueMap.TryGetValue(key, out var fromValue))
                cVal = fromValue;
            var child = compiled.AddChild(key);
            child.Value = cVal ?? string.Empty;
        }

        // SystemAttributes: always present when the template defines defaults; user
        // overrides win.
        if (shader.SystemAttributeDefaults.Count > 0 || systemAttributeOverrides is { Count: > 0 })
        {
            var attrs = root.AddChild("SystemAttributes");
            foreach (var (key, defaultValue) in shader.SystemAttributeDefaults)
            {
                var child = attrs.AddChild(key);
                if (systemAttributeOverrides is not null && systemAttributeOverrides.TryGetValue(key, out var ov) && !string.IsNullOrEmpty(ov))
                    child.Value = ov;
                else
                    child.Value = defaultValue;
            }
            if (systemAttributeOverrides is not null)
            {
                foreach (var (key, ov) in systemAttributeOverrides)
                {
                    if (shader.SystemAttributeDefaults.ContainsKey(key)) continue;
                    var child = attrs.AddChild(key);
                    child.Value = ov;
                }
            }
        }

        // Attributes block: only emitted when the user explicitly enables at least one
        // attribute flag, matching the workspace convention where attributes are rare.
        if (enabledAttributeFlags is not null && shader.AttributeFlags.Count > 0)
        {
            var attrs = root.AddChild("Attributes");
            foreach (var flag in shader.AttributeFlags)
            {
                var c = attrs.AddChild(flag);
                c.Value = enabledAttributeFlags.Contains(flag) ? "1" : "0";
            }
        }

        return root;
    }

    public string Render(
        ShaderTemplate shader,
        IDictionary<string, string> values,
        IEnumerable<string>? enabledFeatureFlags = null,
        IEnumerable<string>? enabledAttributeFlags = null,
        IDictionary<string, string>? systemAttributeOverrides = null,
        IDictionary<string, string>? compiledTextureOverrides = null)
    {
        var doc = BuildDocument(
            shader,
            values,
            enabledFeatureFlags,
            enabledAttributeFlags,
            systemAttributeOverrides,
            compiledTextureOverrides);
        return doc.Serialize();
    }

    private static string NormalizeValue(ShaderParamTemplate p, string raw)
    {
        return p.Kind switch
        {
            ShaderParamKind.Float => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f.ToString("0.######", CultureInfo.InvariantCulture) : raw,
            ShaderParamKind.Int => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i.ToString(CultureInfo.InvariantCulture) : raw,
            ShaderParamKind.Bool => (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase)) ? "1" : "0",
            ShaderParamKind.Vector => NormalizeVector(raw),
            _ => raw,
        };
    }

    private static string NormalizeVector(string raw)
    {
        if (Vector4.TryParse(raw, out var v)) return v.ToString();
        return raw;
    }

    private static IEnumerable<string> MapToCompiledKey(string shader, string paramKey)
    {
        // For Source 2 a Texture* parameter named TextureFoo normally maps to g_tFoo
        // in the compiled block. Numeric or vector variants keep the same name.
        if (!paramKey.StartsWith("Texture", StringComparison.Ordinal))
            yield break;

        var name = paramKey["Texture".Length..];
        if (string.IsNullOrEmpty(name)) yield break;

        // Layer1 / sub-channel aware naming (e.g. TextureLayer1Color -> g_tColor).
        // "LayerN..." where N is one or more digits, followed by a non-digit channel
        // suffix. e.g. "Layer1Color" -> g_tColor, "Layer10Detail" -> g_tLayer9Detail.
        if (name.StartsWith("Layer", StringComparison.Ordinal))
        {
            var digitsStart = "Layer".Length;
            var digitsEnd = digitsStart;
            while (digitsEnd < name.Length && char.IsDigit(name[digitsEnd])) digitsEnd++;
            // Need at least one digit AND a non-empty channel suffix.
            if (digitsEnd == digitsStart || digitsEnd >= name.Length) yield break;
            var layerNumber = name[digitsStart..digitsEnd];
            var channel = name[digitsEnd..];
            if (int.TryParse(layerNumber, out var n) && n > 1)
                yield return $"g_tLayer{n - 1}{channel}";
            else
                yield return $"g_t{channel}";
        }
        else
        {
            yield return $"g_t{name}";
        }
    }
}