using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using GUI.Diagnostics;
using GUI.Settings;
using Lib;

namespace GUI.ViewModels;

/// <summary>
/// 「贴图后缀快速导航」对话框的 ViewModel：配置<b>生成着色器类型</b>、<b>贴图根目录</b>
/// 与<b>贴图后缀规则表</b>，并在右侧给出「当前着色器下每个语义槽位会写到哪个参数键」的实时预览。
///
/// 数据流：所有改动都先落在本 ViewModel 的字段上，<b>点「保存设置」时</b>才写回
/// <see cref="MainViewModel.Settings"/> 并写入注册表 <c>HKEY_CURRENT_USER\SOFTWARE\Kashimura\VmatGenerator</c>，
/// 因此重启后仍然生效。
/// 落盘逻辑刻意不在每个键上触发 —— 否则用户敲错一个字符就会污染配置文件。
/// </summary>
public sealed partial class QuickNavViewModel : ObservableObject
{
    private readonly MainViewModel _parent;
    private readonly List<TextureSuffixRuleViewModel> _attached = new();

    /// <summary>最近一次「重新载入」的加载报告文案（由 <see cref="ReadSettingsFromDisk"/> 写入）。</summary>
    private string _lastLoadNote = string.Empty;

    public QuickNavViewModel(MainViewModel parent)
    {
        _parent = parent;
        Shaders = ShaderCatalog.All;
        KnownRoles = new ObservableCollection<string>(
            TextureRoleTokens.AllRoles.Select(r => r.ToString()));
        LoadFromSettings(_parent.Settings);

        // 「保存设置」按钮现在常驻底栏、每一步都看得见，再在状态行里重复提示
        // 「点保存设置后生效」纯属噪音；状态行只留给保存结果、扫描结果等真正的反馈。
        StatusMessage = string.Empty;
    }

    // ── 着色器 ───────────────────────────────────────────────────────────────

    /// <summary>下拉框数据源：<see cref="ShaderCatalog.All"/>。</summary>
    public IReadOnlyList<ShaderTemplate> Shaders { get; }

    /// <summary>
    /// 当前选中的「生成着色器类型」。改动会同步到
    /// <see cref="MainViewModel.SelectedShaderTemplate"/> 并刷新解析预览。
    /// </summary>
    [ObservableProperty]
    private ShaderTemplate? _selectedShader;

    // ── 配置 ────────────────────────────────────────────────────────────────

    /// <summary>新建材质 / 拖入贴图时使用的默认着色器名（跟随 <see cref="SelectedShader"/>）。</summary>
    [ObservableProperty]
    private string _defaultShaderName = string.Empty;

    /// <summary>贴图根目录：写入 VMAT 时的相对路径基准目录；为空则写原路径。</summary>
    [ObservableProperty]
    private string _textureRoot = string.Empty;

    /// <summary>拖入后是否自动按后缀写入参数（<c>autoAssignOnDrop</c>）。</summary>
    [ObservableProperty]
    private bool _autoAssignOnDrop = true;

    /// <summary>拖入贴图文件夹时是否递归枚举（<c>recurseTextureFolders</c>）。</summary>
    [ObservableProperty]
    private bool _recurseTextureFolders = true;

    /// <summary>拖入材质目录 / <c>.vmat</c> 时是否接管材质根目录。</summary>
    [ObservableProperty]
    private bool _adoptDroppedVmatFolderAsMaterialsRoot = true;

    /// <summary>后缀规则表；<b>集合顺序即优先级</b>（并列时以下标决胜）。</summary>
    public ObservableCollection<TextureSuffixRuleViewModel> Rules { get; } = new();

    /// <summary>当前选中的规则行（删除 / 上下移动均作用于它）。</summary>
    [ObservableProperty]
    private TextureSuffixRuleViewModel? _selectedRule;

    /// <summary>规则表「语义槽位」下拉的数据源：全部角色枚举名。</summary>
    public ObservableCollection<string> KnownRoles { get; }

    /// <summary>对话框底部的状态提示行。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>当前着色器下「角色 → 档位 → 参数键 → 当前值」的实时预览。</summary>
    [ObservableProperty]
    private string _previewLines = string.Empty;

    /// <summary>
    /// 右列「KeyValues 模板预览」：当前着色器的出厂基线 <c>.vmat</c> 全文，
    /// 其中<b>贴图参数行以「路径：」中文开头</b>。
    /// </summary>
    /// <remarks>
    /// <para><b>格式必须如实标注：</b>Lib 的 <see cref="KvAdapter"/> 与 <see cref="VmatFormatter"/>
    /// 都固定使用 <c>KVSerializationFormat.KeyValues1Text</c>，所以这是
    /// <b>KeyValues1 文本</b>——即应用保存 .vmat 时真正写出的内容，
    /// 而非 Source 2 原生的 KeyValues3。</para>
    /// <para><b>关于中文前缀：</b>「路径：」是为了阅读而加的标注，
    /// 因此本面板<b>不是可直接粘贴回 .vmat 的原文</b>。值本身逐字来自
    /// <see cref="ShaderTemplateEmitter.EmitDefault"/>，与真实写出一致，
    /// 变的只是行首多了一个中文标签。</para>
    /// </remarks>
    [ObservableProperty]
    private string _templatePreview = string.Empty;

    partial void OnSelectedShaderChanged(ShaderTemplate? value)
    {
        if (value is not null) DefaultShaderName = value.ShaderName;

        // 着色器类型是全局设置，改动立刻同步给主窗口（新建材质 / 拖入时的默认着色器）。
        ControlErrorRecorder.Guard("同步生成着色器类型", this,
            () => _parent.SelectedShaderTemplate = value);

        RefreshPreview();
        NotifyStepStateChanged();

        // 换着色器就要换贴图清单：同一个目录换 shader，要的贴图完全不同。
        // 规则表也可能刚被改过，一并重算，顺带刷新「哪些后缀还是新的」。
        RefreshInferredSuffixes();
    }

    // ── 命令 ────────────────────────────────────────────────────────────────

    /// <summary>浏览选择贴图根目录。</summary>
    [RelayCommand]
    public void BrowseTextureRoot()
    {
        var folder = FolderPicker.Pick(
            "选择贴图根目录（写入 VMAT 时按此换算相对路径）",
            Directory.Exists(TextureRoot) ? TextureRoot : App.PickerFallbackDirectory);
        if (folder is not null) TextureRoot = folder;
    }

    /// <summary>把当前字段写回 <see cref="MainViewModel.Settings"/> 并落盘。</summary>
    [RelayCommand]
    public void SaveSettings()
    {
        var settings = _parent.Settings;
        ApplyToSettings(settings);
        ApplyRules(settings);

        // GuardWithDialog<T> 返回的是 (Ok, Value) 元组，不是 T 本身 —— 必须解构后再取成员。
        var (ok, report) = ControlErrorRecorder.GuardWithDialog("保存配置", this,
            () => RegistrySettingsStore.Save(settings));

        // Save 内部已经把异常翻译成完整的一句话（含权限失败时该怎么做）并写了 error.log，
        // 这里原样呈现，不再二次拼接——否则会出现「保存配置失败：保存配置失败：…」这类叠句。
        StatusMessage = ok && report is not null
            ? report.Success
                ? "已保存配置。重启后仍然有效。"
                : report.ErrorMessage ?? "保存配置失败，原因未知，详见错误日志。"
            : "保存配置失败，详见错误日志。";
    }

    /// <summary>丢弃未保存的改动，从磁盘重新载入。</summary>
    [RelayCommand]
    public void ReloadSettings()
    {
        var loaded = ControlErrorRecorder.Guard(
            "重新载入配置", this, ReadSettingsFromDisk, default(VmatGeneratorSettings));
        if (loaded is null)
        {
            StatusMessage = "重新载入配置失败，已保留当前界面内容，详情见错误日志。";
            return;
        }

        CopySettings(loaded, _parent.Settings);
        LoadFromSettings(_parent.Settings);
        StatusMessage = _lastLoadNote;
    }

    /// <summary>用 §4 的 35 条种子规则替换当前规则表（尚未保存，需再点「保存设置」）。</summary>
    [RelayCommand]
    public void ResetRules()
    {
        var ok = ControlErrorRecorder.GuardWithDialog("恢复默认规则", this,
            () => VmatGeneratorSettingsStore.RestoreDefaultRules(_parent.Settings));
        if (!ok)
        {
            StatusMessage = "恢复默认规则失败，详情见错误日志。";
            return;
        }

        LoadRulesFrom(_parent.Settings.Rules);
        StatusMessage = $"已恢复 {Rules.Count} 条默认后缀规则（尚未保存，点「保存设置」后生效）。";
        RefreshPreview();
    }

    /// <summary>追加一条空规则并选中它，供用户直接编辑。</summary>
    [RelayCommand]
    public void AddRule()
    {
        var rule = new TextureSuffixRuleViewModel("_new", TextureRole.Normal, true) { Owner = this };
        Attach(rule);
        Rules.Add(rule);
        SelectedRule = rule;
        StatusMessage = $"已添加规则（第 {Rules.Count} 条），共 {Rules.Count} 条。";
        RefreshPreview();
    }

    /// <summary>删除指定规则（未指定时删除 <see cref="SelectedRule"/>）。</summary>
    [RelayCommand]
    public void RemoveRule(TextureSuffixRuleViewModel? rule)
    {
        var target = rule ?? SelectedRule;
        if (target is null)
        {
            StatusMessage = "请先在列表中选中要删除的规则。";
            return;
        }

        Detach(target);
        Rules.Remove(target);
        // 注意：这里必须用 ReferenceEquals 而不是 `SelectedRule is target`。
        // `target` 已经是本方法内的局部变量，`is target` 会被 C# 解析成
        // 「与常量 target 比较」的常量模式（CS9135 编译错误），而不是类型/声明模式。
        if (ReferenceEquals(SelectedRule, target)) SelectedRule = null;
        StatusMessage = $"已删除规则「{target.Suffix}」，剩余 {Rules.Count} 条。";
        RefreshPreview();
    }

    /// <summary>把规则上移一位（等价于 JSON 数组下标减一，即提高优先级）。</summary>
    [RelayCommand]
    public void MoveRuleUp(TextureSuffixRuleViewModel? rule)
    {
        Move(rule, -1);
    }

    /// <summary>把规则下移一位（等价于 JSON 数组下标加一，即降低优先级）。</summary>
    [RelayCommand]
    public void MoveRuleDown(TextureSuffixRuleViewModel? rule)
    {
        Move(rule, +1);
    }

    /// <summary>选一组贴图试算：后缀 → 角色 → 档位 → 参数键 → 写入值，结果显示在预览区。</summary>
    [RelayCommand]
    public void TestDrop()
    {
        if (SelectedShader is not { } shader)
        {
            StatusMessage = "请先选择「生成着色器类型」。";
            return;
        }

        // 同上：GuardWithDialog<T> 返回 (Ok, Value) 元组，值本身是 string[]?。
        var (pickedOk, picked) = ControlErrorRecorder.GuardWithDialog("测试贴图解析", this, () =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择要试算的贴图（可多选）",
                Filter = "贴图资源 (*.png;*.tga;*.jpg;*.jpeg;*.bmp;*.exr;*.hdr;*.pfm;*.dds;*.vtex;*.vtf;*.tif;*.tiff)"
                        + "|*.png;*.tga;*.jpg;*.jpeg;*.bmp;*.exr;*.hdr;*.pfm;*.dds;*.vtex;*.vtf;*.tif;*.tiff"
                        + "|所有文件 (*.*)|*.*",
                Multiselect = true,
                InitialDirectory = Directory.Exists(TextureRoot)
                    ? TextureRoot
                    : App.PickerFallbackDirectory,
            };
            return dlg.ShowDialog() == true ? dlg.FileNames : null;
        });

        if (!pickedOk || picked is not { Length: > 0 })
        {
            StatusMessage = pickedOk ? "未选择任何文件。" : "测试贴图解析失败，详情见错误日志。";
            return;
        }

        PreviewLines = ControlErrorRecorder.Guard("试算贴图解析", this,
            () => BuildTestReport(shader, picked),
            fallback: "试算失败，详情见错误日志。");
        StatusMessage = $"已试算 {picked.Length} 个文件的解析结果（不会真正写入参数）。";
    }

    /// <summary>重建「角色 → 档位 → 参数键 → 当前值」预览。</summary>
    [RelayCommand]
    public void RefreshPreview()
    {
        PreviewLines = BuildPreview();
        TemplatePreview = BuildTemplatePreview();
    }

    /// <summary>关闭本对话框。</summary>
    [RelayCommand]
    public void Close()
    {
        ControlErrorRecorder.Guard("关闭快速导航窗口", this, () =>
        {
            // ViewModel 不持有 Window 引用：按类型找回宿主窗口，避免记录过期引用。
            var host = Application.Current?.Windows
                .OfType<QuickNavWindow>()
                .FirstOrDefault();
            host?.Close();
        });
    }

    // ── 内部实现 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 从磁盘读回配置，把加载报告（是否修复过字段 / 是否从损坏中恢复）记进
    /// <see cref="_lastLoadNote"/>。读不出可用配置时抛异常，交由
    /// <see cref="ControlErrorRecorder"/> 统一记录并弹窗，<b>不会</b>让异常冒到 UI 线程。
    /// </summary>
    private VmatGeneratorSettings ReadSettingsFromDisk()
    {
        var settings = RegistrySettingsStore.Load(out var report);
        _lastLoadNote = RegistrySettingsStore.DescribeLoad(report);
        return settings ?? throw new InvalidOperationException(_lastLoadNote);
    }

    private void Move(TextureSuffixRuleViewModel? rule, int delta)
    {
        var target = rule ?? SelectedRule;
        if (target is null)
        {
            StatusMessage = "请先在列表中选中要移动的规则。";
            return;
        }

        var index = Rules.IndexOf(target);
        var to = index + delta;
        if (index < 0 || to < 0 || to >= Rules.Count)
        {
            StatusMessage = delta < 0 ? "该规则已在首位。" : "该规则已在末位。";
            return;
        }

        Rules.Move(index, to);
        SelectedRule = target;
        StatusMessage = $"已把「{target.Suffix}」移动到第 {to + 1} 位（共 {Rules.Count} 条，位置决定优先级）。";
    }

    /// <summary>把界面字段复制进配置对象（不含规则表）。</summary>
    private void ApplyToSettings(VmatGeneratorSettings settings)
    {
        settings.DefaultShaderName =
            SelectedShader?.ShaderName ?? DefaultShaderName;
        settings.TextureRoot = TextureRoot ?? string.Empty;
        settings.AssetsRoot = AssetsRoot ?? string.Empty;
        settings.ProjectRoot = ProjectRoot ?? string.Empty;
        settings.AutoAssignOnDrop = AutoAssignOnDrop;
        settings.RecurseTextureFolders = RecurseTextureFolders;
        settings.AdoptDroppedVmatFolderAsMaterialsRoot = AdoptDroppedVmatFolderAsMaterialsRoot;

        // 着色器类型同时同步给主窗口，保证「新建」与拖入用的都是同一个模板。
        ControlErrorRecorder.Guard("同步生成着色器类型", this,
            () => _parent.SelectedShaderTemplate = SelectedShader);
    }

    private void ApplyRules(VmatGeneratorSettings settings) =>
        settings.Rules = Rules.Select(r => r.ToRule()).ToList();

    /// <summary>
    /// 用磁盘上读回来的对象整体覆盖主窗口的共享配置（重新载入用）。
    /// 规则逐条复制，不与被丢弃的临时对象共享实例。
    /// </summary>
    private static void CopySettings(VmatGeneratorSettings source, VmatGeneratorSettings target)
    {
        target.DefaultShaderName = source.DefaultShaderName;
        target.TextureRoot = source.TextureRoot;
        target.AssetsRoot = source.AssetsRoot;
        target.ProjectRoot = source.ProjectRoot;
        target.AutoAssignOnDrop = source.AutoAssignOnDrop;
        target.RecurseTextureFolders = source.RecurseTextureFolders;
        target.AdoptDroppedVmatFolderAsMaterialsRoot = source.AdoptDroppedVmatFolderAsMaterialsRoot;
        target.Rules = source.Rules
            .Select(r => new TextureSuffixRule(r.Suffix, r.Role, r.Enabled))
            .ToList();
    }

    /// <summary>从配置对象整体刷新界面字段（构造时 / 重新载入时调用）。</summary>
    private void LoadFromSettings(VmatGeneratorSettings settings)
    {
        // 读回已保存的路径只是恢复现场，不该在打开窗口时自动扫一遍盘。
        _suppressAutoScan = true;
        try
        {
            SelectedShader =
                ShaderCatalog.Find(settings.DefaultShaderName) ?? Shaders.FirstOrDefault();
            TextureRoot = settings.TextureRoot ?? string.Empty;
            AssetsRoot = settings.AssetsRoot ?? string.Empty;
            ProjectRoot = settings.ProjectRoot ?? string.Empty;
            DefaultShaderName = SelectedShader?.ShaderName ?? settings.DefaultShaderName;
            AutoAssignOnDrop = settings.AutoAssignOnDrop;
            RecurseTextureFolders = settings.RecurseTextureFolders;
            AdoptDroppedVmatFolderAsMaterialsRoot = settings.AdoptDroppedVmatFolderAsMaterialsRoot;

            LoadRulesFrom(settings.Rules);

            // 上面 SelectedShader 的赋值已经触发过一次推断，但那时 Rules 还没装载，
            // 「哪些后缀已覆盖」会算错。规则表就位后必须再算一次。
            RefreshInferredSuffixes();
        }
        finally
        {
            _suppressAutoScan = false;
        }
    }

    private void LoadRulesFrom(IEnumerable<TextureSuffixRule> rules)
    {
        DetachAll();
        Rules.Clear();
        foreach (var rule in rules)
        {
            var row = TextureSuffixRuleViewModel.FromRule(rule, this);
            Attach(row);
            Rules.Add(row);
        }
        SelectedRule = Rules.FirstOrDefault();
        RefreshPreview();
    }

    private void Attach(TextureSuffixRuleViewModel rule)
    {
        if (_attached.Contains(rule)) return;
        _attached.Add(rule);
        rule.PropertyChanged += OnRulePropertyChanged;
    }

    private void Detach(TextureSuffixRuleViewModel rule)
    {
        if (!_attached.Remove(rule)) return;
        rule.PropertyChanged -= OnRulePropertyChanged;
    }

    private void DetachAll()
    {
        foreach (var rule in _attached) rule.PropertyChanged -= OnRulePropertyChanged;
        _attached.Clear();
    }

    /// <summary>规则被编辑后立即刷新预览，做到「所见即所得」。</summary>
    private void OnRulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        RefreshPreview();

    /// <summary>§8.3 预览表的「角色」列定宽（显示格）。</summary>
    private const int RoleColumnWidth = 20;

    /// <summary>§8.3 预览表的「档位」列定宽（显示格）。</summary>
    private const int TierColumnWidth = 6;

    /// <summary>
    /// §8.3 预览表的「参数键」列定宽（显示格）。
    ///
    /// <para>取 <b>42</b> 而非原先的 32：三种未解析文案里最宽的是
    /// <c>多候选并列，不写入（AmbiguousQualified）</c>，在等宽字体里占 <b>40 格</b>
    /// （18 个 CJK / 全角标点记 2 格 + 18 个 ASCII 记 1 格）。列宽若只有 32，
    /// 这一行会直接溢出并把「当前值」列黏在 <c>（AmbiguousQualified）</c> 后面。
    /// 42 = 40 + 2 格间隙。</para>
    /// </summary>
    private const int KeyColumnWidth = 42;

    /// <summary>两列之间至少保留的间隙（格），内容超宽时也不得贴死。</summary>
    private const int MinColumnGap = 2;

    /// <summary>
    /// §8.3 示例里歧义候选续行的缩进：对齐到「参数键」列起点，再右移 4 格做视觉层次
    /// （<c>角色 20 + 档位 6 + 4 = 30</c>，与规格 §8.3 示例逐格一致）。
    /// 由列宽推导而非写死，日后调整列宽时缩进自动跟随。
    /// </summary>
    private static readonly string CandidateIndent =
        new(' ', RoleColumnWidth + TierColumnWidth + 4);

    private string BuildPreview()
    {
        if (SelectedShader is not { } shader)
            return "（未选择着色器）";

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"着色器：{shader.ShaderName}");
            sb.AppendLine(PadTo("角色", RoleColumnWidth) + PadTo("档位", TierColumnWidth)
                             + PadTo("参数键", KeyColumnWidth) + "当前值");
            sb.AppendLine(new string('-', RoleColumnWidth + TierColumnWidth + KeyColumnWidth + 34));

            foreach (var role in TextureRoleTokens.AllRoles)
            {
                if (role == TextureRole.Unknown) continue;

                var res = TextureRoleResolver.Resolve(shader, role);
                var tier = res.Tier == TextureKeyTier.None ? "—" : $"P{(int)res.Tier}";
                // 歧义分支会返回「主行 + 续行」两段：只有主行进入定宽格式。
                // 若把两行拼成一个字符串交给 -32，补白会按含换行符的整串长度计算，
                // 「当前值」列会被黏到候选列表末尾、整列参差（§8.3 的表格是逐行对齐的）。
                var (key, continuation) = ParameterCell(res);
                sb.AppendLine(PadTo(role.ToString(), RoleColumnWidth)
                              + PadTo(tier, TierColumnWidth)
                              + PadTo(key, KeyColumnWidth)
                              + CurrentValueOf(res.ParameterKey));

                // 续行单独成行、不再补「当前值」列——它在规格示例里本就没有该列。
                if (continuation is not null) sb.AppendLine(continuation);
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            ErrorLog.Error("生成解析预览", "QuickNavViewModel", ex);
            return $"// 预览生成失败：{ex.Message}\n// 详细堆栈见错误日志：{ErrorLog.LogFilePath}";
        }
    }

    /// <summary>
    /// 按<b>显示列宽</b>左侧补白，返回可直接拼接的定宽单元。
    ///
    /// <para><b>为什么不用 <c>string.Format</c> 的 <c>{-32}</c>。</b>格式串按 UTF-16
    /// <b>码元数</b>算宽度，而预览渲染在等宽字体（<c>Cascadia Mono / Consolas / 微软雅黑</c>）里，
    /// 一个 CJK / 全角字符占 <b>2 格</b>。本表三种未解析文案全是中文，按码元算会算少一倍的格数，
    /// 于是「当前值」列在中文行整体左移、整列参差。</para>
    ///
    /// <para>内容超出列宽时仍补 <see cref="MinColumnGap"/> 格，保证相邻两列绝不贴死。</para>
    /// </summary>
    /// <param name="value">单元文本。</param>
    /// <param name="displayWidth">目标显示列宽（格）。</param>
    /// <returns>左侧补白后的文本。</returns>
    private static string PadTo(string value, int displayWidth)
    {
        var pad = displayWidth - DisplayWidth(value);
        if (pad < MinColumnGap) pad = MinColumnGap;
        return value + new string(' ', pad);
    }

    /// <summary>按等宽字体度量文本占用的显示格数（CJK / 全角记 2 格，其余 1 格）。</summary>
    private static int DisplayWidth(string value)
    {
        var width = 0;
        foreach (var ch in value)
        {
            width += ch >= '\u1100'
                && (ch <= '\u115F'
                    || (ch >= '\u2E80' && ch <= '\uA4CF')
                    || (ch >= '\uAC00' && ch <= '\uD7A3')
                    || (ch >= '\uF900' && ch <= '\uFAFF')
                    || (ch >= '\uFE30' && ch <= '\uFE6F')
                    || (ch >= '\uFF00' && ch <= '\uFF60')
                    || (ch >= '\uFFE0' && ch <= '\uFFE6'))
                ? 2
                : 1;
        }
        return width;
    }

    /// <summary>§8.3「参数键」列：返回 <b>(主行, 续行或 null)</b>。
    ///
    /// <para><b>为什么必须拆成两段。</b>歧义分支要展开候选列表，规格 §8.3 示例里它是<b>两行</b>。
    /// 若把两行拼成一个字符串再交给 <c>{key,-32}</c>，补白会按<b>整串</b>长度计算——
    /// 含换行符及其后全部候选文本，于是「当前值」列被黏到候选列表末尾，整列参差。
    /// 拆开后主行走定宽格式串（照常补「当前值」列），续行单独成行且不补该列。</para>
    ///
    /// <para>分流规则与文案（<b>三个分支的文案均不得改动</b>）：
    /// <c>None</c>（已解析）→ 参数键名，续行 <c>null</c>；
    /// <c>NoMatchingKey</c> → <c>无对应键（NoMatchingKey）</c>，续行 <c>null</c>；
    /// <c>AmbiguousQualified</c> → <c>多候选并列，不写入（AmbiguousQualified）</c>，
    /// 续行缩进输出 <c>候选：{k1} / {k2} / …</c>。</para>
    ///
    /// <para><b>顺序取 <see cref="TextureRoleResolution.Candidates"/> 的既有顺序，GUI 不自行排序</b>——
    /// 该顺序由 Lib 的 §5.4 全序（模板声明序）决定，GUI 重排会把诊断与真实解析结果对不上。</para>
    /// </summary>
    /// <param name="res">单个语义槽位的解析结果。</param>
    /// <returns>主行文本，以及仅歧义分支非空的续行文本。</returns>
    private static (string Main, string? Continuation) ParameterCell(TextureRoleResolution res)
    {
        // None = 已解析。正常情况下调用方已取走 ParameterKey；走到这里说明键名意外为空，
        // 此时必须如实显示「无参数键」，不能谎称「无对应键」或「多候选并列」。
        if (res.UnresolvedReason == TextureResolveFailure.None)
            return (res.ParameterKey is { Length: > 0 } k ? k : "（无参数键）", null);

        if (res.UnresolvedReason == TextureResolveFailure.AmbiguousQualified)
        {
            var keys = res.Candidates
                .Select(c => c.ParameterKey)
                .Where(k => !string.IsNullOrEmpty(k));
            var joined = string.Join(" / ", keys);
            return ("多候选并列，不写入（AmbiguousQualified）",
                joined.Length == 0 ? null : CandidateIndent + "候选：" + joined);
        }

        return ("无对应键（NoMatchingKey）", null);
    }

    private string BuildTestReport(ShaderTemplate shader, IReadOnlyList<string> files)
    {
        var matcher = new TextureSuffixMatcher(Rules.Select(r => r.ToRule()).ToList());
        var sb = new StringBuilder();
        sb.AppendLine($"着色器：{shader.ShaderName}　贴图根目录：{(TextureRoot.Length > 0 ? TextureRoot : "（未设置，写原路径）")}");
        sb.AppendLine($"文件 → 命中后缀 → 角色 → 档位 → 参数键 → 写入值");
        sb.AppendLine(new string('-', 96));

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var match = matcher.Match(file);
            if (match is null || match.Role == TextureRole.Unknown)
            {
                sb.AppendLine($"{name} → 未命中任何后缀规则 → 不写入");
                continue;
            }

            var res = TextureRoleResolver.Resolve(shader, match.Role);
            var tier = res.Tier == TextureKeyTier.None ? "—" : $"P{(int)res.Tier}";
            var key = res.IsResolved && res.ParameterKey is { Length: > 0 } k ? k : "（不写入）";
            var vmatPath = TexturePathRules.ToVmatPath(file, TextureRoot);
            sb.AppendLine($"{name} → {match.MatchedSuffix} → {match.Role} → {tier} → {key} → {vmatPath}");
            if (!res.IsResolved)
                sb.AppendLine($"    ↳ {TextureResolutionMessages.Describe(res)}");
        }

        return sb.ToString();
    }

    /// <summary>取参数行当前值，供预览与用户比对（不修改任何数据）。</summary>
    private string CurrentValueOf(string? parameterKey)
    {
        if (string.IsNullOrEmpty(parameterKey)) return "—";

        var row = _parent.Editor.ParameterRows
            .FirstOrDefault(r => string.Equals(r.Key, parameterKey, StringComparison.Ordinal));

        return row switch
        {
            null => "—",
            TextureParameter t => string.IsNullOrEmpty(t.Value) ? "（未设置）" : t.Value,
            BoolParameter b => b.Value ? "1" : "0",
            ScalarParameter s => s.Value.ToString("0.######", CultureInfo.InvariantCulture),
            VectorParameter v => !string.IsNullOrEmpty(v.RawText)
                ? v.RawText
                : $"{v.X:0.###} {v.Y:0.###} {v.Z:0.###} {v.W:0.###}",
            _ => "—",
        };
    }
}