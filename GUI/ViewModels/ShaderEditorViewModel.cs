using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using GUI.Diagnostics;
using Lib;

namespace GUI.ViewModels;

/// <summary>
/// The central view-model that owns the active parameter rows, the generated
/// KeyValues preview, and the commands invoked from the XAML. One instance is
/// created by <see cref="MainViewModel"/> and shared with the editor pane.
///
/// The row collection is rebuilt every time a different shader is selected.
/// The KV preview is re-rendered whenever a row's value changes (or whenever
/// a feature flag is toggled) so the preview always matches the editor.
/// </summary>
public sealed partial class ShaderEditorViewModel : ObservableObject
{
    private readonly MainViewModel _parent;
    private ShaderTemplate? _shader;
    private VmatDocument? _document;

    public ShaderEditorViewModel(MainViewModel parent)
    {
        _parent = parent;
        ParameterRows.CollectionChanged += (_, _) => OnRowsChanged();
        // Each row's PropertyChanged bubbles up so we can re-render the preview.
        ParameterRows.CollectionChanged += OnCollectionChanged;
        foreach (var row in ParameterRows) AttachRow(row);
    }

    public ObservableCollection<ParameterRowViewModel> ParameterRows { get; } = new();

    [ObservableProperty]
    private string _generatedText = string.Empty;

    [ObservableProperty]
    private string _statusText = "已就绪。";

    [ObservableProperty]
    private string _selectedShaderDisplay = "(未选择着色器)";

    [ObservableProperty]
    private string _materialFilePath = string.Empty;

    /// <summary>True when the current document has unsaved changes.</summary>
    [ObservableProperty]
    private bool _isDirty;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (ParameterRowViewModel r in e.NewItems) AttachRow(r);
        if (e.OldItems is not null)
            foreach (ParameterRowViewModel r in e.OldItems) DetachRow(r);
    }

    private void AttachRow(ParameterRowViewModel row)
    {
        row.PropertyChanged += OnRowPropertyChanged;
    }

    private void DetachRow(ParameterRowViewModel row)
    {
        row.PropertyChanged -= OnRowPropertyChanged;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParameterRowViewModel.IsVisible)) return;
        IsDirty = true;
        RebuildText();
    }

    private void OnRowsChanged() => RebuildText();

    /// <summary>
    /// Populate the editor for a given shader + optional document. When
    /// <paramref name="document"/> is null we start from the template defaults.
    /// Feature flag values are stored as <see cref="BoolParameter"/> rows at
    /// the top of the row collection; parameters gated on a flag become
    /// invisible when the flag is off.
    /// </summary>
    public void LoadFromTemplate(ShaderTemplate shader, VmatDocument? document)
    {
        _shader = shader;
        _document = document;

        // Detach event handlers on the rows that are about to be replaced.
        foreach (var row in ParameterRows) DetachRow(row);
        ParameterRows.Clear();

        SelectedShaderDisplay = $"{shader.DisplayName}  ({shader.ShaderName})";
        MaterialFilePath = document?.FilePath ?? string.Empty;

        // Compute the starting value map: document wins over template defaults.
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document is not null)
        {
            foreach (var child in document.Layers)
            {
                if (child.IsContainer) continue;
                values[child.Key] = child.Value ?? string.Empty;
            }
        }

        // Feature flags first so they appear at the top of the editor.
        foreach (var flag in shader.FeatureFlags)
        {
            var enabled = false;
            if (values.TryGetValue(flag, out var fv))
                enabled = fv == "1" || fv.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (!string.IsNullOrEmpty(defaultFlagFromTemplate(shader, flag)))
                enabled = defaultFlagFromTemplate(shader, flag) == "1";
            var row = new BoolParameter(
                new ShaderParamTemplate(flag, flag, ShaderParamKind.Bool, enabled ? "1" : "0"),
                enabled);
            ParameterRows.Add(row);
        }

        // Parameters: skip the ones already represented by feature flags above.
        var flagSet = new HashSet<string>(shader.FeatureFlags, StringComparer.Ordinal);
        foreach (var p in shader.Parameters)
        {
            if (flagSet.Contains(p.Key)) continue;
            ParameterRows.Add(BuildRow(p, values));
        }

        RebuildText();
        IsDirty = false;
        StatusText = $"已加载 {shader.DisplayName}{(document is null ? " 模板" : "：" + document.DisplayName)}。";

        string defaultFlagFromTemplate(ShaderTemplate s, string key)
        {
            // Search the Parameters collection for a default with the same key
            // (a few shaders define F_TRANSLUCENT as both a flag and a bool param).
            foreach (var pp in s.Parameters)
                if (string.Equals(pp.Key, key, StringComparison.Ordinal) && !string.IsNullOrEmpty(pp.DefaultValue))
                    return pp.DefaultValue;
            return string.Empty;
        }
    }

    private static ParameterRowViewModel BuildRow(ShaderParamTemplate p, IDictionary<string, string> values)
    {
        var raw = values.TryGetValue(p.Key, out var v) && !string.IsNullOrEmpty(v)
            ? v
            : p.DefaultValue;

        switch (p.Kind)
        {
            case ShaderParamKind.Bool:
                return new BoolParameter(p, ParseBool(raw));
            case ShaderParamKind.Int:
                return new ScalarParameter(p, ParseDouble(raw));
            case ShaderParamKind.Float:
                return new ScalarParameter(p, ParseDouble(raw));
            case ShaderParamKind.Vector:
                return new VectorParameter(p, ParseVector(raw));
            case ShaderParamKind.Texture:
                return new TextureParameter(p, raw ?? string.Empty);
            case ShaderParamKind.String:
                return new TextureParameter(p, raw ?? string.Empty);
            default:
                return new TextureParameter(p, raw ?? string.Empty);
        }
    }

    /// <summary>
    /// Re-render the KeyValues preview by running the current rows + the
    /// active feature flags through <see cref="Lib.VmatGenerator.Render"/>.
    ///
    /// 该方法在每次参数变更时触发，是最容易抛异常的热路径。渲染失败不会中断应用，
    /// 而是把错误写入 <see cref="ErrorLog"/> 并在预览区显示提示。
    /// </summary>
    public void RebuildText()
    {
        if (_shader is null)
        {
            GeneratedText = string.Empty;
            return;
        }

        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var enabledFlags = new List<string>();
            foreach (var row in ParameterRows)
            {
                row.WriteValue(values);
                if (row is BoolParameter b && row.Template.Kind == ShaderParamKind.Bool
                    && _shader.FeatureFlags.Contains(row.Key) && b.Value)
                {
                    enabledFlags.Add(row.Key);
                }
            }

            // Subgroup gating: hide rows whose controlling feature flag is off.
            foreach (var row in ParameterRows)
            {
                row.IsVisible = row.Template.RequiredFeatureFlag is null
                                || enabledFlags.Contains(row.Template.RequiredFeatureFlag);
            }

            GeneratedText = App.Generator.Render(_shader, values, enabledFeatureFlags: enabledFlags);
        }
        catch (Exception ex)
        {
            // 记录到错误日志（包含是哪个参数行出的问题），预览区显示提示而不是崩溃。
            ErrorLog.Error("渲染 KV 预览", DescribeRowsNeedingContext(), ex);
            GeneratedText = $"// 渲染失败：{ex.Message}\n// 详细堆栈见错误日志：{ErrorLog.LogFilePath}";
            StatusText = "渲染失败，已记录到错误日志。";
        }
    }

    /// <summary>渲染失败时，描述当前处于激活状态的参数行以便定位。</summary>
    private string DescribeRowsNeedingContext()
    {
        try
        {
            var shader = _shader?.ShaderName ?? "(无着色器)";
            var visible = ParameterRows.Count(r => r.IsVisible);
            return $"ShaderEditorViewModel shader='{shader}' 行数={ParameterRows.Count} 可见={visible}";
        }
        catch
        {
            return "ShaderEditorViewModel";
        }
    }

    [RelayCommand]
    private void CopyKv()
    {
        if (string.IsNullOrEmpty(GeneratedText)) return;
        var text = GeneratedText;
        var ok = ControlErrorRecorder.Guard("复制 KV 文本", this, () => Clipboard.SetText(text));
        StatusText = ok ? "已复制 KV 文本到剪贴板。" : "剪贴板操作失败，详情见错误日志。";
    }

    private static bool ParseBool(string? raw) =>
        raw == "1" || (raw is not null && raw.Equals("true", StringComparison.OrdinalIgnoreCase));

    private static double ParseDouble(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static Vector4 ParseVector(string? raw) =>
        Vector4.TryParse(raw ?? string.Empty, out var v) ? v : default;
}
