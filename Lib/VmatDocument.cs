namespace Lib;

/// <summary>
/// A strongly-typed summary of a parsed VMAT file, surfaced to the UI layer.
///
/// Tree shape (as produced by <see cref="KvAdapter.ParseViaValve"/> and used by
/// <see cref="VmatGenerator.BuildDocument"/>):
///   root (Key = "Layer0")
///   ├── "shader"          -> "csgo_environment.vfx"
///   ├── "F_TRANSLUCENT"   -> "0"
///   ├── ...
///   ├── "Compiled Textures" { "g_tColor" -> "...vtex", ... }
///   └── "SystemAttributes" { "PhysicsSurfaceProperties" -> "plaster" }
/// </summary>
public sealed class VmatDocument
{
    public VmatDocument(string filePath, VmatNode root)
    {
        FilePath = filePath;
        Root = root;
        ShaderName = root.Children
            .FirstOrDefault(c => string.Equals(c.Key, "shader", StringComparison.Ordinal))?.Value
            ?? string.Empty;
    }

    public string FilePath { get; }
    public VmatNode Root { get; }
    public string ShaderName { get; }

    public string DisplayName => string.IsNullOrEmpty(FilePath) ? "(new)" : Path.GetFileName(FilePath);

    /// <summary>Top-level keys of <c>Layer0</c>: feature flags, parameters, sub-blocks.</summary>
    public IEnumerable<VmatNode> Layers => Root.Children;

    /// <summary>The <c>Layer0</c> node itself (the root of the VMAT tree).</summary>
    public VmatNode Layer0 => Root;
}