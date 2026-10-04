namespace Lib;

/// <summary>
/// Emits "factory default" VMAT text for every shader in <see cref="ShaderCatalog"/>,
/// both to disk (under <c>Templates/</c>) and as embedded resources in the assembly.
/// The embedded copies are used by the library to bootstrap an editor session even when
/// the developer hasn't run the build-time emitter script.
/// </summary>
public static class ShaderTemplateEmitter
{
    /// <summary>
    /// Render a single shader to VMAT text using the template's parameter defaults and
    /// a minimal flag set. The result is a complete Source 2 VMAT — <c>Layer0</c> with
    /// every feature flag, every parameter, a populated <c>Compiled Textures</c> block,
    /// and any <c>SystemAttributes</c> defaults.
    /// </summary>
    public static string EmitDefault(ShaderTemplate shader, IEnumerable<string>? enabledFeatureFlags = null)
    {
        var values = shader.BuildDefaultValueMap();
        var generator = new VmatGenerator();
        // Enable every feature flag so subgroup parameters show up in the baseline.
        var enabledFlags = enabledFeatureFlags?.ToList() ?? shader.FeatureFlags.ToList();
        return generator.Render(
            shader,
            values,
            enabledFeatureFlags: enabledFlags,
            enabledAttributeFlags: shader.AttributeFlags,
            systemAttributeOverrides: shader.SystemAttributeDefaults.ToDictionary(kv => kv.Key, kv => kv.Value),
            compiledTextureOverrides: shader.CompiledTextureKeys.ToDictionary(kv => kv, _ => string.Empty));
    }

    /// <summary>
    /// Write every shader's factory default to a file under <paramref name="directory"/>.
    /// Returns the list of file paths that were written.
    /// </summary>
    public static IReadOnlyList<string> EmitAllToDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var paths = new List<string>(ShaderCatalog.All.Count);
        foreach (var shader in ShaderCatalog.All)
        {
            var name = shader.ShaderName;
            // Replace illegal filename characters (dots are fine; we keep them).
            var path = Path.Combine(directory, name + ".vmat");
            File.WriteAllText(path, EmitDefault(shader));
            paths.Add(path);
        }
        return paths;
    }

    /// <summary>
    /// Load a specific template that was embedded into the assembly at build time.
    /// Returns <c>null</c> if no template was embedded for that shader.
    /// </summary>
    public static string? LoadEmbedded(string shaderName)
    {
        var assembly = typeof(ShaderTemplateEmitter).Assembly;
        var resourceName = assembly.GetName().Name + ".Templates." + shaderName + ".vmat";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var sr = new StreamReader(stream);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// Enumerate every embedded template resource. Used by the round-trip test to make
    /// sure ValveKeyValue can reparse what we shipped.
    /// </summary>
    public static IEnumerable<(string ShaderName, string Text)> EnumerateEmbedded()
    {
        var assembly = typeof(ShaderTemplateEmitter).Assembly;
        var prefix = assembly.GetName().Name + ".Templates.";
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(".vmat", StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var sr = new StreamReader(stream);
            yield return (name[prefix.Length..^".vmat".Length], sr.ReadToEnd());
        }
    }
}