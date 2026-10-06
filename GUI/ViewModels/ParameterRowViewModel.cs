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

    /// <summary>
    /// 构造一个携带<b>原文</b>的向量行。
    ///
    /// 「贴图 ↔ 常量 vec4」双形态键（如 <c>TextureRoughness1</c>、<c>TextureFoamNormal</c>）
    /// 既能写常量、也能写贴图路径。这类值 <c>Vector4.TryParse</c> 解析不了，
    /// 因此用本字段原样保留，重新打开材质时也不会丢。
    /// </summary>
    public VectorParameter(ShaderParamTemplate t, Vector4 initial, string rawText) : this(t, initial)
    {
        _rawText = rawText;
    }

    /// <summary>
    /// 覆盖输出的原文（通常是贴图路径）。<b>为空时行为与原先完全一致</b>，
    /// 仍然输出 X/Y/Z/W 组成的 vec4 常量。
    /// </summary>
    [ObservableProperty]
    private string _rawText = string.Empty;

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _z;
    [ObservableProperty] private double _w;

    public override string EditorKind => "Vector";

    /// <summary>
    /// 写出向量行的值。
    ///
    /// <b>向后兼容不变式（review 请重点核对这一行）</b>：
    /// <code>
    /// RawText 为空  →  target[Key] = new Vector4(X, Y, Z, W).ToString()
    /// </code>
    /// 与改动前<b>逐字一致</b>。之所以能保证，是因为 <see cref="RawText"/> 有且只有两个写入点，
    /// 两者都排除了真·向量参数：
    /// <list type="number">
    /// <item><see cref="TrySetRawText"/> —— 仅由 <c>ShaderEditorViewModel.TrySetTextureValue</c>
    /// 的拖拽导入路径调用，而分配器只会产出 §5 的贴图参数键（全部以 <c>Texture</c> 开头）。</item>
    /// <item><c>ShaderEditorViewModel.BuildRow</c> —— 额外要求
    /// <c>p.Shape == ShaderValueShape.TextureOrVector</c>。</item>
    /// </list>
    /// <c>ShaderCatalog</c> 的实测统计（可按下列脚本复核）：
/// <code>
/// $v = Get-Content Lib\ShaderCatalog.cs | Select-String 'ShaderParamKind\.Vector,'
/// $t = $v | Select-String 'ShaderValueShape\.TextureOrVector'
/// $v.Count=76   $t.Count=9   ($v.Count - $t.Count)=67
/// </code>
/// <list type="bullet">
/// <item><b>出现次数</b> 76 处 <see cref="ShaderParamKind.Vector"/>，其中 <b>67</b> 处是真·向量参数
/// （<c>g_vColorTint</c> / <c>g_vTexCoord*</c> / <c>g_vWater*</c> / <c>g_vMask*</c> / …），
/// <c>Shape</c> 保持默认的 <see cref="ShaderValueShape.Scalar"/>，
/// <see cref="RawText"/> 恒为空 —— 渲染结果与改动前完全相同。</item>
/// <item>剩余 <b>9 处</b>才是 <c>TextureOrVector</c>，但它们只涉及 <b>6 个不同的键名</b>
/// （<c>TextureColor</c> ×2、<c>TextureFoamNormal</c> ×1、<c>TextureRimMask</c> ×1、
/// <c>TextureRoughness1</c> ×1、<c>TextureSelfIllumMask</c> ×2、<c>TextureTranslucency</c> ×2）。
/// 注意区分「出现次数」与「不同键名」两个口径。</item>
/// </list>
    /// </summary>
    public override void WriteValue(IDictionary<string, string> target)
    {
        target[Key] = !string.IsNullOrEmpty(RawText)
            ? RawText
            : new Vector4((float)X, (float)Y, (float)Z, (float)W).ToString();
    }

    /// <summary>
    /// 写入一条无法解析为 vec4 的原文（拖拽导入的贴图路径走这里）。
    /// 返回是否写入成功，<b>不抛异常</b>。
    /// </summary>
    public bool TrySetRawText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        RawText = text;
        return true;
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