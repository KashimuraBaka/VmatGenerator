using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Lib;

namespace GUI.ViewModels;

/// <summary>
/// One loaded .vmat file. Surfaces the display name + the shader name for the
/// navigation pane; the underlying <see cref="VmatDocument"/> is kept so the
/// generator can re-parse if needed.
/// </summary>
public sealed partial class MaterialEntryViewModel : ObservableObject
{
    public MaterialEntryViewModel(VmatDocument document)
    {
        Document = document;
        _displayName = document.DisplayName;
        _shaderName = document.ShaderName;
    }

    public VmatDocument Document { get; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _shaderName;
}

/// <summary>
/// One node in the left-hand navigation tree. Each node represents a shader
/// (e.g. "csgo_environment.vfx") and groups all materials using it. The XAML
/// re-binds to <see cref="MaterialEntries"/>.
/// </summary>
public sealed partial class ShaderGroupViewModel : ObservableObject
{
    public ShaderGroupViewModel(string shaderName, IEnumerable<MaterialEntryViewModel> materials)
    {
        _shaderName = shaderName;
        MaterialEntries = new ObservableCollection<MaterialEntryViewModel>(materials);
    }

    [ObservableProperty]
    private string _shaderName;

    public ObservableCollection<MaterialEntryViewModel> MaterialEntries { get; }
}
