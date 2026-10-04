using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using GUI.Diagnostics;
using Lib;

namespace GUI.ViewModels;

/// <summary>
/// One row in the dynamic parameter editor. Each row knows its shader parameter
/// template plus a strongly-typed view-model branch (<see cref="BoolParameter"/>,
/// <see cref="ScalarParameter"/>, <see cref="VectorParameter"/>, <see cref="TextureParameter"/>)
/// that drives the matching Fluent control.
/// </summary>
public abstract partial class ParameterRowViewModel : ObservableObject
{
    protected ParameterRowViewModel(ShaderParamTemplate template)
    {
        Template = template;
    }

    public ShaderParamTemplate Template { get; }
    public string Key => Template.Key;
    public string Display => Template.Display;
    public ShaderParamKind Kind => Template.Kind;

    /// <summary>True when this row should be visible. Subgroup-gated rows hide
    /// themselves when the controlling feature flag is off.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>True when this row represents a feature flag that doubles as
    /// a parameter — used to hide the duplicate parameter row that follows it.</summary>
    public bool IsFeatureFlagMirror => false;

    /// <summary>Tag used by the XAML <c>DataTemplate</c> selector to pick the
    /// right editor template (Bool / Scalar / Vector / Texture).</summary>
    public abstract string EditorKind { get; }

    /// <summary>Push the current row value back into the supplied dictionary.</summary>
    public abstract void WriteValue(IDictionary<string, string> target);
}

/// <summary>
/// Boolean parameter — driven by a Fluent-styled <c>CheckBox</c>.
/// </summary>
public sealed partial class BoolParameter : ParameterRowViewModel
{
    public BoolParameter(ShaderParamTemplate t, bool initial) : base(t)
    {
        _value = initial;
    }

    [ObservableProperty]
    private bool _value;

    public override string EditorKind => "Bool";

    public override void WriteValue(IDictionary<string, string> target)
    {
        target[Key] = Value ? "1" : "0";
    }
}

/// <summary>
/// Float or Int parameter — driven by a numeric-filtered <c>TextBox</c>.
/// </summary>
public sealed partial class ScalarParameter : ParameterRowViewModel
{
    public ScalarParameter(ShaderParamTemplate t, double initial) : base(t)
    {
        _value = initial;
    }

    [ObservableProperty]
    private double _value;

    /// <summary>Spinner step size based on whether this is a Float or Int parameter.</summary>
    public double Step => Kind == ShaderParamKind.Int ? 1 : 0.01;

    /// <summary>Number format string — integers print without decimals.</summary>
    public string FormatString => Kind == ShaderParamKind.Int ? "0" : "0.######";

    public override string EditorKind => "Scalar";

    public override void WriteValue(IDictionary<string, string> target)
    {
        if (Kind == ShaderParamKind.Int)
        {
            target[Key] = ((int)Math.Round(Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            target[Key] = Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>
/// Vector4 parameter — driven by 4 numeric <c>TextBox</c> editors.
/// </summary>
public sealed partial class VectorParameter : ParameterRowViewModel
{
    public VectorParameter(ShaderParamTemplate t, Vector4 initial) : base(t)
    {
        _x = initial.X;
        _y = initial.Y;
        _z = initial.Z;
        _w = initial.W;
    }

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _z;
    [ObservableProperty] private double _w;

    public override string EditorKind => "Vector";

    public override void WriteValue(IDictionary<string, string> target)
    {
        target[Key] = new Vector4((float)X, (float)Y, (float)Z, (float)W).ToString();
    }
}

/// <summary>
/// Texture path parameter — driven by a TextBox + Browse Button.
/// </summary>
public sealed partial class TextureParameter : ParameterRowViewModel
{
    public TextureParameter(ShaderParamTemplate t, string initial) : base(t)
    {
        _value = initial;
    }

    [ObservableProperty]
    private string _value = string.Empty;

    public override string EditorKind => Kind == ShaderParamKind.Texture ? "Texture" : "Vector";

    public override void WriteValue(IDictionary<string, string> target)
    {
        target[Key] = Value ?? string.Empty;
    }

    /// <summary>Browse command: shows an OpenFileDialog seeded from the row's
    /// initial path and writes the selected path back into <see cref="Value"/>.
    ///
    /// 文件对话框在受限环境（无桌面会话 / COM 不可用）下极易抛异常，
    /// 因此整体包在 Guard 里：失败只记录 + 提示，不闪退。</summary>
    [RelayCommand]
    private void Browse()
    {
        var display = Display;
        var key = Key;
        var seed = Value;

        var (ok, picked) = ControlErrorRecorder.GuardWithDialog($"浏览贴图「{display}」", this, () =>
        {
            var seedDir = App.PickerFallbackDirectory;
            if (!string.IsNullOrEmpty(seed))
            {
                try
                {
                    var dir = Path.GetDirectoryName(seed);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) seedDir = dir;
                }
                catch
                {
                    // 路径本身非法时退回默认目录。
                }
            }

            var dlg = new OpenFileDialog
            {
                Title = $"为「{display}」选择贴图",
                Filter = "贴图资源 (*.png;*.tga;*.vtex;*.exr;*.jpg)|*.png;*.tga;*.vtex;*.exr;*.jpg|所有文件 (*.*)|*.*",
                CheckFileExists = false,
                InitialDirectory = seedDir,
            };

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        });

        if (ok)
        {
            // 用户可能直接关闭了对话框 —— 这不算错误。
            if (!string.IsNullOrEmpty(picked)) Value = picked;
        }
        else
        {
            ErrorLog.Info("浏览贴图失败", $"参数行 '{key}'");
        }
    }
}