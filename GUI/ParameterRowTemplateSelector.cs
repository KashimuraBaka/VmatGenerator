using System.Windows;
using System.Windows.Controls;
using GUI.ViewModels;

namespace GUI;

/// <summary>
/// Selects the right XAML <see cref="DataTemplate"/> for each
/// <see cref="ParameterRowViewModel"/>. The row's <see cref="ParameterRowViewModel.EditorKind"/>
/// ("Bool", "Scalar", "Vector", "Texture") drives the choice so every shader
/// param is rendered with the appropriate WPF control (CheckBox,
/// numeric TextBox, Vector4 editor, or TextBox for textures).
/// </summary>
public sealed class ParameterRowTemplateSelector : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ParameterRowViewModel row || container is not FrameworkElement fe)
            return base.SelectTemplate(item, container);

        var key = row.EditorKind switch
        {
            "Bool" => "BoolTemplate",
            "Scalar" => "ScalarTemplate",
            "Vector" => "VectorTemplate",
            "Texture" => "TextureTemplate",
            _ => "ScalarTemplate",
        };
        if (fe.TryFindResource(key) is DataTemplate dt) return dt;
        return base.SelectTemplate(item, container);
    }
}
