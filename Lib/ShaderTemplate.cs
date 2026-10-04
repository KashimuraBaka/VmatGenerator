using System.Text;

namespace Lib;

/// <summary>
/// Declarative description of a shader parameter: key, display label, type, and
/// (for vectors/scalars) default value. Used to drive the editor UI.
/// </summary>
public enum ShaderParamKind
{
    Float,
    Int,
    Bool,
    String,
    Vector,
    Texture,
}

/// <summary>
/// Marker used by the editor to decide whether the key is a "texture or vec4 constant"
/// (e.g. TextureColor / TextureRoughness). The library still stores a single default,
/// but the UI may offer a toggle.
/// </summary>
public enum ShaderValueShape
{
    Scalar,
    Vector,
    Texture,
    TextureOrVector,
}

/// <summary>
/// Declarative description of a single shader parameter.
/// </summary>
public sealed class ShaderParamTemplate
{
    public ShaderParamTemplate(string key, string display, ShaderParamKind kind, string defaultValue = "")
    {
        Key = key;
        Display = display;
        Kind = kind;
        DefaultValue = defaultValue;
    }

    public string Key { get; }
    public string Display { get; }
    public ShaderParamKind Kind { get; }
    public string DefaultValue { get; }

    /// <summary>
    /// Optional feature flag that gates this parameter in the editor. When <c>null</c>
    /// the parameter is always shown; otherwise it is only displayed when that flag is
    /// enabled. Used for "subgroup" keys like the detail-blend or phong-spec groups.
    /// </summary>
    public string? RequiredFeatureFlag { get; init; }

    /// <summary>
    /// UI hint for how the value should be rendered (scalar/texture/vector hybrid).
    /// </summary>
    public ShaderValueShape Shape { get; init; } = ShaderValueShape.Scalar;
}

/// <summary>
/// Declarative description of a single shader. The <see cref="ShaderName"/> is the
/// value that ends up in the <c>"shader"</c> property of the VMAT <c>Layer0</c>.
/// </summary>
public sealed class ShaderTemplate
{
    public ShaderTemplate(
        string shaderName,
        string displayName,
        string description,
        IReadOnlyList<ShaderParamTemplate> parameters,
        IReadOnlyList<string> featureFlags,
        IReadOnlyList<string> attributeFlags,
        IReadOnlyDictionary<string, string>? systemAttributeDefaults = null,
        IReadOnlyList<string>? compiledTextureKeys = null)
    {
        ShaderName = shaderName;
        DisplayName = displayName;
        Description = description;
        Parameters = parameters;
        FeatureFlags = featureFlags;
        AttributeFlags = attributeFlags;
        SystemAttributeDefaults = systemAttributeDefaults ?? new Dictionary<string, string>();
        CompiledTextureKeys = compiledTextureKeys ?? Array.Empty<string>();
    }

    public string ShaderName { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public IReadOnlyList<ShaderParamTemplate> Parameters { get; }
    public IReadOnlyList<string> FeatureFlags { get; }
    public IReadOnlyList<string> AttributeFlags { get; }

    /// <summary>
    /// Default values for the <c>SystemAttributes</c> sub-block emitted alongside the
    /// shader parameters. Common keys: <c>PhysicsSurfaceProperties</c>,
    /// <c>LightMapTextureName</c>, <c>DetailTexture</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> SystemAttributeDefaults { get; }

    /// <summary>
    /// Names of the compiled-texture placeholders that should be emitted even when the
    /// user has not supplied a value. Prevents the editor from accidentally stripping
    /// the Source 2 compiler output keys.
    /// </summary>
    public IReadOnlyList<string> CompiledTextureKeys { get; }

    public override string ToString() => $"{DisplayName} ({ShaderName})";

    /// <summary>
    /// Helper for emitting templates: flatten every parameter (including subgroups) into
    /// a dictionary keyed by Key with the default value. Used by the emitter to render
    /// a baseline VMAT.
    /// </summary>
    public IDictionary<string, string> BuildDefaultValueMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in Parameters)
        {
            if (string.IsNullOrEmpty(p.DefaultValue)) continue;
            // If a subgroup key is gated on a flag that is off, still emit it so the
            // default template shows the contract, but the UI can hide it.
            map[p.Key] = p.DefaultValue;
        }
        return map;
    }

    /// <summary>
    /// Render a single-line summary used in the editor nav pane. (Pure helper.)
    /// </summary>
    public string RenderSummary()
    {
        var sb = new StringBuilder();
        sb.Append(DisplayName).Append(" (").Append(ShaderName).Append(") — ");
        sb.Append(Parameters.Count).Append(" params, ");
        sb.Append(FeatureFlags.Count).Append(" features");
        return sb.ToString();
    }
}
