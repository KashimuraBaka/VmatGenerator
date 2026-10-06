using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using GUI.Diagnostics;
using GUI.Settings;
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

        // 拖拽导入 / 快速导航共用的用户配置，保存在
        // HKEY_CURRENT_USER\SOFTWARE\Kashimura\VmatGenerator。
        // 启动时只读取，**不**据此自动扫描任何目录 —— 目录仍由用户显式选择。
        Settings = RegistrySettingsStore.LoadOrDefault();
        QuickNav = new QuickNavViewModel(this);
    }

    public ShaderEditorViewModel Editor { get; }

    /// <summary>
    /// 拖拽导入与快速导航共用的用户配置（生成着色器类型、贴图根目录、后缀规则）。
    /// 整个进程共用同一个实例，因此「快速导航」里保存的设置会立刻对拖拽导入生效。
    /// </summary>
    public VmatGeneratorSettings Settings { get; }

    /// <summary>「贴图后缀快速导航」对话框的 ViewModel（单例，随主窗口一起创建）。</summary>
    public QuickNavViewModel QuickNav { get; }

    /// <summary>最近一次拖拽导入的分配结果；尚未拖拽时为 null（供测试与调试查看）。</summary>
    public TextureAssignResult? LastDropResult { get; set; }

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
        var folder = FolderPicker.Pick(
            "选择包含 .vmat 文件的 materials/ 文件夹",
            Directory.Exists(MaterialsFolder) ? MaterialsFolder : App.PickerFallbackDirectory);
        if (folder is not null) LoadFolder(folder);
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
    /// <para>
    /// 公开是因为拖拽导入也要复用它（拖入材质文件夹时按开关接管材质根目录）。
    /// </para>
    /// </summary>
    public void LoadFolder(string folder)
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

    // ── 拖拽导入（规格 §2 行为矩阵 / §8.8 实现契约）─────────────────────────────

    /// <summary>
    /// 处理一次拖拽：按 <see cref="DropAnalysis.Category"/> 分发到「打开材质」/
    /// 「接管材质根目录」/「按后缀自动写入贴图参数」，并刷新状态栏文案。
    ///
    /// code-behind 只负责从 <c>DragEventArgs</c> 里取 <c>string[]</c>，
    /// 全部业务判断都在这里；整个方法体包在 <see cref="ControlErrorRecorder.Guard"/> 里，
    /// 任何异常都只会写入 <see cref="ErrorLog"/> 并提示，不会闪退。
    /// </summary>
    [RelayCommand]
    public void HandleDroppedPaths(IReadOnlyList<string> paths)
    {
        var ok = ControlErrorRecorder.Guard("拖拽处理", this, () => ProcessDroppedPaths(paths));
        if (!ok) SafeSetStatus("拖拽导入失败，已跳过本次导入；详情见错误日志。");
    }

    private void ProcessDroppedPaths(IReadOnlyList<string> paths)
    {
        // 1. 空白路径直接按「未识别」处理。
        var dropped = (paths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();
        if (dropped.Count == 0)
        {
            StatusText = DescribeUnsupported(Array.Empty<string>());
            return;
        }

        // 2. 分类：目录会在 Lib 侧按 recurseTextureFolders 递归探测 .vmat / 贴图。
        var analysis = new DropImportService(App.Scanner, Settings.RecurseTextureFolders)
            .Analyze(dropped);

        // 3. 材质侧：接管材质根目录 + 打开第一个 .vmat。
        if (analysis.Category is DropCategory.MaterialFolder
            or DropCategory.VmatFile
            or DropCategory.Mixed)
        {
            var root = analysis.MaterialRootCandidate;
            if (root is not null
                && Settings.AdoptDroppedVmatFolderAsMaterialsRoot
                && !string.Equals(root, MaterialsFolder, StringComparison.OrdinalIgnoreCase))
            {
                LoadFolder(root);
            }

            if (analysis.VmatFiles.Count > 0)
            {
                var entry = FindMaterialEntry(analysis.VmatFiles[0]);
                if (entry is not null) EditMaterial(entry);
            }
        }

        // D6：既没有 .vmat 也没有贴图 —— 无任何写入，仅提示。
        if (analysis.Category is DropCategory.Unsupported or DropCategory.None
            || (analysis.VmatFiles.Count == 0 && analysis.TextureFiles.Count == 0))
        {
            StatusText = DescribeUnsupported(analysis.OtherFiles);
            return;
        }

        // 4~5. 是否自动写入 + 是否有贴图。
        if (!Settings.AutoAssignOnDrop)
        {
            StatusText = "已按设置跳过贴图自动写入（autoAssignOnDrop = false）。";
            return;
        }
        if (analysis.TextureFiles.Count == 0)
        {
            StatusText = DescribeWithoutTextures(analysis);
            return;
        }

        // 6. 确定目标着色器：优先当前编辑器的模板，否则用配置里的默认着色器。
        var shader = Editor.CurrentShader ?? ShaderCatalog.Find(Settings.DefaultShaderName);
        if (shader is null)
        {
            StatusText = $"未找到着色器 '{Settings.DefaultShaderName}'，无法自动写入贴图。";
            return;
        }
        if (Editor.CurrentShader is null)
        {
            // 编辑器里没有材质 —— 视为「新建材质」意图：按基名分组把 .vmat 直接写到贴图旁边，
            // 而不是把参数填进一个不会被保存的内存文档。
            CreateMaterialsFromTextures(analysis, shader);
            return;
        }

        // 7. 交给 Lib 规划器：后缀 → 角色 → 参数键 → 写入路径（含冲突裁定）。
        var result = TextureAssigner.Assign(
            shader, analysis.TextureFiles, Settings.Rules, Settings.TextureRoot);

        // 8. 逐条写回参数行；赋值即触发 RebuildText，KV 预览立即刷新。
        var written = 0;
        foreach (var assignment in result.Assignments)
        {
            if (Editor.TrySetTextureValue(assignment.ParameterKey, assignment.VmatPath))
                written++;
            else
                ErrorLog.Info("拖拽写入跳过",
                    $"键 '{assignment.ParameterKey}' 在当前参数行中不存在或类型不匹配。");
        }

        // 9. 贴图根目录为空时，用这批贴图的公共父目录兜底（下次拖入写相对路径）。
        if (string.IsNullOrEmpty(Settings.TextureRoot) && result.SuggestedTextureRoot is { Length: > 0 } suggested)
        {
            Settings.TextureRoot = suggested;
            QuickNav.TextureRoot = suggested;
        }

        // 10~11. 记录结果 + 组装状态栏文案。
        LastDropResult = result;
        StatusText = DescribeDropResult(analysis, result, shader, written);
    }

    /// <summary>
    /// 编辑器中没有打开任何材质时，把拖入的贴图按基名分组生成新的 <c>.vmat</c>，
    /// 并把第一个生成结果载入编辑器。
    /// </summary>
    /// <remarks>
    /// <para><b>触发条件刻意收窄</b>：仅在「没有打开材质」时触发。有材质在手时拖入
    /// 仍然走写入已有材质的老路，不改变既有行为。</para>
    /// <para><b>不覆盖已有文件</b>：<c>overwriteExisting</c> 传 <c>false</c>，
    /// 同名 <c>.vmat</c> 已存在就跳过并在状态栏点名，拖放不会静默毁掉用户手写的材质。</para>
    /// <para>进度条只存在于快速导航窗口，主窗口这条路径没有进度 UI，
    /// 只能在状态栏看最终结果——生成量通常在几十个文件级，耗时很短。</para>
    /// </remarks>
    private void CreateMaterialsFromTextures(DropAnalysis analysis, ShaderTemplate shader)
    {
        var result = ControlErrorRecorder.Guard(
            "拖入贴图生成材质", this,
            () => VmatBuildService.Build(
                shader, analysis.TextureFiles, Settings.Rules, Settings.TextureRoot,
                overwriteExisting: false),
            fallback: null);

        if (result is null)
        {
            StatusText = "生成失败，详情见错误日志。";
            return;
        }

        StatusText = DescribeGenerated(result);

        // 把第一个写出的材质载入编辑器，让用户立刻看到结果并可继续改。
        if (result.WrittenFiles.Count == 0) return;
        var first = result.WrittenFiles[0];
        var directory = Path.GetDirectoryName(first);
        if (string.IsNullOrEmpty(directory)) return;

        var doc = App.Scanner.Scan(directory!)
            .FirstOrDefault(d => string.Equals(d.FilePath, first, StringComparison.OrdinalIgnoreCase));
        if (doc is not null) Editor.LoadFromTemplate(shader, doc);
    }

    /// <summary>把生成结果汇总成一行状态栏文案，未写入与跳过的清单必须点名。</summary>
    private static string DescribeGenerated(VmatBuildResult result)
    {
        if (result.WrittenFiles.Count == 0 && result.SkippedExisting.Count == 0)
            return "没有可写入的材质：贴图均未命中任何启用的后缀规则。";

        var sb = new System.Text.StringBuilder();
        sb.Append($"已新建 {result.WrittenFiles.Count} 个 .vmat");
        sb.Append($"　{string.Join("、", result.WrittenFiles.Select(Path.GetFileName).Take(3))}");
        if (result.SkippedExisting.Count > 0)
            sb.Append($"　同名文件已存在、跳过 {result.SkippedExisting.Count} 个（未覆盖）");
        if (result.UnassignedFiles.Count > 0)
            sb.Append($"　未命中规则未写入 {result.UnassignedFiles.Count} 个");
        return sb.ToString();
    }

    /// <summary>
    /// 在已加载的左侧列表里定位某个 <c>.vmat</c> 对应的条目；
    /// 找不到（例如拖入的材质不在当前列表里）时返回 <c>null</c>，不抛异常。
    /// </summary>
    private MaterialEntryViewModel? FindMaterialEntry(string vmatFile)
    {
        string full;
        try
        {
            full = Path.GetFullPath(vmatFile);
        }
        catch
        {
            full = vmatFile;
        }

        foreach (var group in ShaderGroups)
        foreach (var entry in group.MaterialEntries)
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(entry.Document.FilePath);
            }
            catch
            {
                candidate = entry.Document.FilePath;
            }

            if (string.Equals(candidate, full, StringComparison.OrdinalIgnoreCase)) return entry;
        }

        return null;
    }

    /// <summary>D6 文案：列出无关文件的扩展名（去点、去重、升序）。</summary>
    private static string DescribeUnsupported(IReadOnlyList<string> otherFiles)
    {
        var extensions = otherFiles
            .Select(p => Path.GetExtension(p))
            .Where(ext => !string.IsNullOrEmpty(ext))
            .Select(ext => ext.TrimStart('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ext => ext, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return extensions.Count == 0
            ? "未识别可导入的内容，已忽略。"
            : $"未识别可导入的内容（{string.Join("、", extensions)}），已忽略。";
    }

    /// <summary>D1 / D2：没有贴图可写时的状态栏文案。</summary>
    private string DescribeWithoutTextures(DropAnalysis analysis) => analysis.Category switch
    {
        DropCategory.MaterialFolder =>
            $"已载入材质目录「{analysis.MaterialRootCandidate ?? MaterialsFolder}」"
            + $"（{analysis.VmatFiles.Count} 个 .vmat）。",
        DropCategory.VmatFile =>
            $"已打开材质「{Path.GetFileName(analysis.VmatFiles.FirstOrDefault() ?? string.Empty)}」"
            + $"（{SelectedShaderTemplate?.ShaderName ?? "未知着色器"}），未匹配到贴图。",
        _ => analysis.Summary,
    };

    /// <summary>D3 / D4 / D5 / S10：按类别组装最终状态栏文案。</summary>
    private string DescribeDropResult(
        DropAnalysis analysis,
        TextureAssignResult result,
        ShaderTemplate shader,
        int written)
    {
        // §8.8 步骤 11：先按 S10 生成基础文案（一个都没写、且存在「未命中任何后缀规则」的文件），
        // 但**不要**在此提前返回 —— 同一次拖拽里还可能同时存在 11a 冲突与 11b 未解析诊断，
        // §8.8 步骤 11b 要求它们一并呈现给用户。提前 return 会把歧义诊断整段吞掉。
        var baseText = (written == 0 && result.UnassignedFiles.Count > 0)
            ? $"未匹配到任何后缀规则（{result.UnassignedFiles.Count} 个文件），未写入任何参数。"
            : null;

        // 只有在「没有冲突也没有未解析」时，基础文案就是最终文案。
        if (baseText is not null
            && result.UnresolvedRoles.Count == 0
            && result.Conflicts.Count == 0)
        {
            return baseText;
        }

        // 零写入时，分类句会退化成「已按后缀自动写入 0 个贴图参数（2 个文件）：」这种带悬空冒号、
        // 且与 S10 句语义重复的话。此时由 S10 句独占「结果」这一层，只保留 11a / 11b 的追加。
        var text = baseText is not null ? string.Empty : analysis.Category switch
        {
            DropCategory.TextureFiles =>
                "已按后缀自动写入 {k} 个贴图参数（{n} 个文件）：{detail}",
            DropCategory.TextureOnlyFolder =>
                "已从贴图文件夹「{dir}」导入 {n} 张贴图（递归：{recurse}），写入 {k} 个参数。",
            _ => // Mixed（含 .vmat 与贴图）
                "已导入 {m} 个材质、{n} 张贴图，写入 {k} 个参数；忽略 {x} 个无关文件。",
        };

        text = text
            .Replace("{dir}", analysis.MaterialRootCandidate ?? analysis.TextureRootCandidate ?? string.Empty)
            .Replace("{recurse}", Settings.RecurseTextureFolders ? "开" : "关")
            .Replace("{m}", analysis.VmatFiles.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("{n}", analysis.TextureFiles.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("{x}", analysis.OtherFiles.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("{k}", written.ToString(CultureInfo.InvariantCulture))
            .Replace("{detail}", DescribeAssignments(result));

        // 冲突与未解析都是「拒绝写入」，必须让用户看见原因（§8.8 步骤 11a / 11b）。
        if (result.Conflicts.Count > 0)
        {
            text += text.Length > 0
                ? $"{Environment.NewLine}⚠ {result.Conflicts.Count} 张贴图冲突未写入。"
                : $"⚠ {result.Conflicts.Count} 张贴图冲突未写入。";
        }

        // 11b：诊断文本由 Lib 逐字给出，GUI 原样透传并按 ℹ / ⚠ 两类分流。
        var unresolved = TextureResolutionMessages.Summarize(result.UnresolvedRoles);
        if (unresolved.Length > 0)
        {
            text += text.Length > 0 ? Environment.NewLine + unresolved : unresolved;
        }

        // S10 基础文案置于最前；无论它是否成立，11a / 11b 都必须一并出现。
        return baseText is null ? text : baseText + (text.Length > 0 ? Environment.NewLine + text : string.Empty);
    }

    /// <summary>D3 文案里的明细：<c>参数键←文件</c>（最多列 5 条）。</summary>
    private static string DescribeAssignments(TextureAssignResult result)
    {
        var shown = result.Assignments.Take(5)
            .Select(a => $"{a.ParameterKey}←{Path.GetFileName(a.FilePath)}");
        var detail = string.Join("，", shown);
        return result.Assignments.Count > 5
            ? detail + "，…"
            : detail;
    }

    private void SafeSetStatus(string text)
    {
        try
        {
            StatusText = text;
        }
        catch
        {
            // 连状态栏都写不进去时静默忽略 —— 错误已经由 Guard 记过了。
        }
    }

    // ── 快速导航窗口 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 打开「贴图后缀快速导航」窗口（工具栏按钮绑定此命令）。
    ///
    /// 主窗口的菜单项走 code-behind 的 <c>OnOpenQuickNavClicked</c>（需要 <c>Owner</c>），
    /// 工具栏按钮绑定本命令 —— 两者<b>只</b>各开一次窗口，刻意不在同一控件上同时挂
    /// <c>Click</c> 与 <c>Command</c>，否则 WPF 会把两者都执行一遍。
    /// </summary>
    [RelayCommand]
    public void OpenQuickNav()
    {
        ControlErrorRecorder.GuardWithDialog("打开贴图后缀快速导航", this, () =>
        {
            // Owner 只用于居中定位，取不到（无主窗口 / 测试宿主）时留空即可。
            var owner = Application.Current?.MainWindow;
            new QuickNavWindow(QuickNav)
            {
                Owner = owner is Window w && w.IsVisible ? w : null,
            }.ShowDialog();
        });
    }
}
