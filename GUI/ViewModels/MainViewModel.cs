using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using GUI.Diagnostics;
using Lib;

namespace GUI.ViewModels;

/// <summary>
/// Root view-model for the main window. Owns the list of shader groups
/// (<see cref="ShaderGroups"/>), the currently selected material entry, the
/// filter string (for "filter by shader"), and exposes the commands wired into
/// the Fluent Menu / ToolBar.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Editor = new ShaderEditorViewModel(this);
    }

    public ShaderEditorViewModel Editor { get; }

    [ObservableProperty]
    private string _materialsFolder = string.Empty;

    /// <summary>
    /// 启动时应用不再自动扫描任何目录，因此初始状态必须自己说明下一步做什么。
    /// </summary>
    [ObservableProperty]
    private string _statusText = "尚未选择材质文件夹 —— 请使用「文件 → 打开文件夹」选择 materials/ 目录，或「新建」创建一个材质。";

    [ObservableProperty]
    private string _shaderFilter = string.Empty;

    [ObservableProperty]
    private ShaderGroupViewModel? _selectedShaderGroup;

    [ObservableProperty]
    private MaterialEntryViewModel? _selectedMaterial;

    [ObservableProperty]
    private ShaderTemplate? _selectedShaderTemplate;

    public ObservableCollection<ShaderGroupViewModel> ShaderGroups { get; } = new();

    [RelayCommand]
    public void Refresh()
    {
        // 没有选定目录时不做任何事 —— 不再自动扫描任何默认路径。
        if (string.IsNullOrWhiteSpace(MaterialsFolder))
        {
            StatusText = "尚未选择材质文件夹，请使用「文件 → 打开文件夹」。";
            return;
        }
        LoadFolder(MaterialsFolder);
    }

    [RelayCommand]
    public void OpenFolder()
    {
        ControlErrorRecorder.GuardWithDialog("选择材质文件夹", this, () =>
        {
            var dlg = new OpenFolderDialog
            {
                Title = "选择包含 .vmat 文件的 materials/ 文件夹",
                InitialDirectory = Directory.Exists(MaterialsFolder)
                    ? MaterialsFolder
                    : App.PickerFallbackDirectory,
            };
            if (dlg.ShowDialog() == true) LoadFolder(dlg.FolderName);
        });
    }

    [RelayCommand]
    public void FilterByShader()
    {
        var ok = ControlErrorRecorder.Guard("按着色器过滤", this, RebuildGroupsFromCache);
        StatusText = !ok
            ? "过滤失败，列表可能不完整；详情见错误日志。"
            : string.IsNullOrEmpty(ShaderFilter)
                ? "已清除过滤。"
                : $"过滤条件：'{ShaderFilter}'";
    }

    [RelayCommand]
    public void ClearFilter()
    {
        ShaderFilter = string.Empty;
        ControlErrorRecorder.Guard("清除过滤", this, RebuildGroupsFromCache);
    }

    [RelayCommand]
    public void NewVmat()
    {
        ControlErrorRecorder.GuardWithDialog("新建 VMAT", this, () =>
        {
            var shader = SelectedShaderTemplate ?? ShaderCatalog.All.First();
            Editor.LoadFromTemplate(shader, document: null);
            Editor.MaterialFilePath = string.Empty;
            StatusText = $"已新建 {shader.DisplayName} 模板。";
        });
    }

    [RelayCommand]
    public void SaveCurrent()
    {
        if (string.IsNullOrEmpty(Editor.MaterialFilePath))
        {
            SaveAs();
            return;
        }
        var path = Editor.MaterialFilePath;
        var text = Editor.GeneratedText;
        var ok = ControlErrorRecorder.Guard($"保存 {Path.GetFileName(path)}", this, () =>
        {
            File.WriteAllText(path, text);
        });
        if (ok)
        {
            Editor.IsDirty = false;
            StatusText = $"已保存 {Path.GetFileName(path)}。";
        }
        else
        {
            StatusText = $"保存失败：{Path.GetFileName(path)}，详情见错误日志。";
            ControlErrorRecorder.ReportToUser("保存", Path.GetFileName(path),
                new IOException($"写入 '{path}' 失败。"));
        }
    }

    [RelayCommand]
    public void SaveAs()
    {
        ControlErrorRecorder.GuardWithDialog("另存为", this, () =>
        {
            var shader = Editor.MaterialFilePath is { Length: > 0 } ? null : SelectedShaderTemplate?.ShaderName;
            var dlg = new SaveFileDialog
            {
                Title = "将 VMAT 另存为…",
                Filter = "Source 2 VMAT (*.vmat)|*.vmat|所有文件 (*.*)|*.*",
                FileName = (shader ?? "new") + ".vmat",
                InitialDirectory = Directory.Exists(MaterialsFolder) ? MaterialsFolder : App.PickerFallbackDirectory,
            };
            if (dlg.ShowDialog() != true) return;

            var folder = MaterialsFolder;
            File.WriteAllText(dlg.FileName, Editor.GeneratedText);
            Editor.MaterialFilePath = dlg.FileName;
            Editor.IsDirty = false;
            StatusText = $"已另存为 {Path.GetFileName(dlg.FileName)}。";
            if (Directory.Exists(folder)) LoadFolder(folder);
        });
    }

    [RelayCommand]
    public void EditSelectedMaterial()
    {
        if (SelectedMaterial is null) return;
        EditMaterial(SelectedMaterial);
    }

    public void EditMaterial(MaterialEntryViewModel entry)
    {
        // 打开单个材质是最高频的操作，失败必须被隔离，否则点一下就闪退。
        var ok = ControlErrorRecorder.Guard($"打开材质「{entry.DisplayName}」", entry, () =>
        {
            var shader = ShaderCatalog.Find(entry.ShaderName)
                ?? throw new InvalidOperationException(
                    $"未知着色器 '{entry.ShaderName}'，文件：{entry.DisplayName}。");

            SelectedShaderTemplate = shader;
            Editor.LoadFromTemplate(shader, entry.Document);
        });

        if (!ok) StatusText = $"无法打开「{entry.DisplayName}」，详情见错误日志。";
    }

    [RelayCommand]
    public void PickShader(ShaderTemplate? template)
    {
        SelectedShaderTemplate = template;
        if (template is null) return;
        if (SelectedMaterial is null)
        {
            ControlErrorRecorder.Guard($"加载着色器模板「{template.DisplayName}」", template, () =>
            {
                Editor.LoadFromTemplate(template, document: null);
                Editor.MaterialFilePath = string.Empty;
                StatusText = $"已加载 {template.DisplayName} 模板。";
            });
        }
    }

    private List<VmatDocument> _allDocs = new();

    /// <summary>
    /// 扫描并加载整个材质目录。任何一步失败（目录不存在、权限不足、某个 .vmat 损坏）
    /// 都被拦截并记录，已加载的部分仍然保留 —— 不会因为一个坏文件导致整个应用崩溃。
    /// </summary>
    private void LoadFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                StatusText = $"文件夹不存在：{folder}";
                ErrorLog.Warn("加载材质目录", folder, new DirectoryNotFoundException(folder));
                return;
            }

            MaterialsFolder = folder;
            StatusText = $"正在扫描 {folder} …";

            // Scan 内部对单个文件做了逐个 try/catch；这里再加一层，防止扫描器本身出错。
            var docs = ControlErrorRecorder.Guard(
                $"扫描材质目录「{folder}」", folder,
                () => App.Scanner.Scan(folder).ToList(),
                fallback: new List<VmatDocument>())!;

            _allDocs = docs;
            var rebuilt = ControlErrorRecorder.Guard(
                "重建着色器分组", folder, () => RebuildGroupsFromCacheCore());

            if (rebuilt)
            {
                StatusText = $"已从 {folder} 加载 {_allDocs.Count} 个 .vmat 文件。";
            }
            else
            {
                StatusText = $"已加载 {_allDocs.Count} 个 .vmat 文件，但分组重建失败，详情见错误日志。";
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Error("加载材质目录", folder, ex);
            StatusText = $"加载材质目录失败：{ex.Message}";
        }
    }

    private void RebuildGroupsFromCache() => RebuildGroupsFromCacheCore();

    private void RebuildGroupsFromCacheCore()
    {
        ShaderGroups.Clear();
        var groups = App.Scanner.GroupByShader(_allDocs);
        foreach (var (shaderName, materials) in groups)
        {
            if (!string.IsNullOrEmpty(ShaderFilter)
                && shaderName.IndexOf(ShaderFilter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var entries = materials.Select(d => new MaterialEntryViewModel(d)).ToList();
            ShaderGroups.Add(new ShaderGroupViewModel(shaderName, entries));
        }
        SelectedShaderGroup = ShaderGroups.FirstOrDefault();
    }
}
