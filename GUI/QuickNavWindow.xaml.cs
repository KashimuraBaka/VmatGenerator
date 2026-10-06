using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using GUI.Diagnostics;
using GUI.ViewModels;
using Lib;

namespace GUI;

/// <summary>
/// 「贴图后缀快速导航」配置窗口：生成着色器类型、贴图根目录与贴图后缀规则表。
///
/// DataContext 由 <see cref="MainWindow"/> 通过 <see cref="MainWindow.OnOpenQuickNavClicked"/>
/// 或 <see cref="ViewModels.MainViewModel.OpenQuickNav"/> 传入，因此构造函数只接受
/// ViewModel —— 这样窗口永远有明确的宿主数据源，不会读到 <c>Application.Current.MainWindow</c>。
/// </summary>
public partial class QuickNavWindow : Window
{
    public QuickNavWindow(QuickNavViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>拖拽<b>进入</b>与<b>悬停</b>共用：判定能否接收，并在合法时给出 Copy 光标。</summary>
    /// <remarks>
    /// 一次拖拽中 <c>DragOver</c> 会连续触发几十次，因此这里只读取
    /// <see cref="DataFormats.FileDrop"/> 的存在性，不枚举内容、不调
    /// <see cref="DropImportService.CanAccept"/>——后者要碰文件系统。
    /// </remarks>
    private void OnWindowDragOver(object sender, DragEventArgs e) =>
        ControlErrorRecorder.Guard("拖入生成来源", this, () =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        });

    /// <summary>拖拽离开窗口：清除悬停态。</summary>
    private void OnWindowDragLeave(object sender, DragEventArgs e) =>
        ControlErrorRecorder.Guard("拖入生成来源", this, () =>
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        });

    /// <summary>
    /// 放下：把文件 / 文件夹路径并入 <see cref="QuickNavViewModel.AssetsRoot"/>。
    /// </summary>
    /// <remarks>
    /// 这里<b>只收集来源，不触发生成</b>。拖入即开始写盘属于破坏性动作：
    /// 用户可能只是想换一个扫描目录，或者还没挑好着色器。
    /// 走到第三步点「开始生成」才是提交。
    /// </remarks>
    private void OnWindowDrop(object sender, DragEventArgs e) =>
        ControlErrorRecorder.Guard("拖入资产文件夹", this, () =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                (DataContext as QuickNavViewModel)?.AddDroppedPaths(paths);
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        });

    /// <summary>点击步骤条回到第一步。生成中不允许跳走。</summary>
    private void OnStep1Clicked(object sender, MouseButtonEventArgs e) =>
        (DataContext as QuickNavViewModel)?.GoToStep1Command.Execute(null);

    /// <summary>点击步骤条跳到第二步；第一步没填齐时不放行。</summary>
    private void OnStep2Clicked(object sender, MouseButtonEventArgs e) =>
        (DataContext as QuickNavViewModel)?.GoToStep2Command.Execute(null);

    /// <summary>点击步骤条跳到第三步；前两步没满足时不放行。</summary>
    private void OnStep3Clicked(object sender, MouseButtonEventArgs e) =>
        (DataContext as QuickNavViewModel)?.GoToStep3Command.Execute(null);

    /// <summary>关闭窗口。</summary>
    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}

/// <summary>
/// 规则表「当前着色器解析预览」列的转换器：把一条规则的
/// <b>「语义槽位 + 是否启用 + 当前生成着色器类型」</b>三元组，渲染成该规则在当前着色器下的
/// 实际落点预览。
///
/// <para><b>输入</b>（顺序与 XAML 里的 <c>MultiBinding</c> 一致，共 3 个值）：
/// <list type="number">
/// <item><c>Role</c> —— 语义槽位的枚举名字符串（可为空或非法）；</item>
/// <item><c>Enabled</c> —— 规则是否启用；</item>
/// <item><c>ShaderTemplate</c> —— 当前选中的生成着色器类型（可为 <c>null</c>）。</item>
/// </list></para>
///
/// <para><b>输出</b>为只读显示文本，不参与任何写入决策：
/// <list type="bullet">
/// <item>启用且已解析：<c>P2 → TextureNormal1</c>（档位 → 参数键）；</item>
/// <item>未启用：<c>（已停用）</c>；槽位为空 / 非法 / 未选着色器：对应的括号占位文案；</item>
/// <item>未解析时按 <see cref="TextureResolveFailure"/> 分流，<b>与
/// <see cref="GUI.ViewModels.QuickNavViewModel"/> 的预览区使用同一组字面量</b>：
/// <see cref="TextureResolveFailure.NoMatchingKey"/> → <c>— 无对应键（NoMatchingKey）</c>；
/// <see cref="TextureResolveFailure.AmbiguousQualified"/> → <c>— 多候选并列，不写入（AmbiguousQualified）</c>，
/// 并在换行后缩进给出 <c>候选：{k1} / {k2} / …</c>。</item>
/// </list></para>
///
/// <para><b>候选顺序取 <see cref="TextureRoleResolution.Candidates"/> 的既有顺序</b>——
/// 该顺序由 Lib 的 §5.4 全序（模板声明序）决定，转换器不重排、不去重，
/// 以免预览与真实解析结果对不上。</para>
///
/// <para><b>不抛异常保证</b>：本转换器处于数据绑定热路径，异常会打断整棵可视化树的渲染。
/// 因此方法整体 try/catch：任何异常只写一条 <see cref="ErrorLog"/> 警告并返回 <c>（预览失败）</c>，
/// 绝不冒泡成界面崩溃。</para>
/// </summary>
// ─────────────────────────────────────────────────────────────────────────────
// 可空标注说明（CS8767）：
// WPF 的 IMultiValueConverter 在各 .NET 版本的 ref 程序集里可空标注并不一致
// （有的标成 object[]，有的标成 object[]?，有的整体未标注），而本项目 Nullable=enable。
// 把这一段切到 oblivious 上下文，无论 ref 程序集怎么标都不会产生 CS8767/8766 警告，
// 项目其余部分仍保持 Nullable=enable。代价是要自己判空（见下）。
#nullable disable
public sealed class RoleResolutionPreviewConverter : IMultiValueConverter
{
    /// <summary>把三元组渲染为预览列文本；任何异常都被吞掉并降级为占位文案。</summary>
    /// <param name="values">
    /// <c>MultiBinding</c> 传入的 3 个值，依次为 <c>Role</c>（字符串）、<c>Enabled</c>（布尔）、
    /// <c>ShaderTemplate</c>（可为 null）。长度不足 3 时返回空串。
    /// </param>
    /// <param name="targetType">目标类型（本转换器只产出字符串，故不使用）。</param>
    /// <param name="parameter">转换器参数（未使用）。</param>
    /// <param name="culture">区域性（未使用，不做数字格式化）。</param>
    /// <returns>
    /// 预览文本：已解析时形如 <c>P2 → TextureNormal1</c>；未解析时为对应的未解析原因说明。
    /// </returns>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 3) return string.Empty;

        var roleText = values[0] as string;
        var enabled = values[1] is true;
        var shader = values[2] as ShaderTemplate;

        if (!enabled) return "（已停用）";
        if (string.IsNullOrWhiteSpace(roleText)) return "（未指定槽位）";
        if (shader is null) return "（未选择着色器）";

        try
        {
            var role = TextureRoleTokens.TryParseRole(roleText, out var parsed) ? parsed : TextureRole.Unknown;
            if (role == TextureRole.Unknown) return "（未识别槽位）";

            var res = TextureRoleResolver.Resolve(shader, role);
            if (res.IsResolved && res.ParameterKey is { Length: > 0 } key)
                return $"P{(int)res.Tier} → {key}";

            // 未写入时按 §5.4 分流，并复用与 QuickNavViewModel 预览区相同的字面量，
            // 保证两处 UI 对同一状态的描述不会出现第二种说法。
            // 候选顺序沿用 Candidates 的既有顺序（Lib 的 §5.4 全序），此处不重排、不去重。
            if (res.UnresolvedReason == TextureResolveFailure.AmbiguousQualified)
            {
                var joined = string.Join(" / ", res.Candidates
                    .Select(c => c.ParameterKey)
                    .Where(k => !string.IsNullOrEmpty(k)));
                return joined.Length == 0
                    ? "— 多候选并列，不写入（AmbiguousQualified）"
                    : $"— 多候选并列，不写入（AmbiguousQualified）{Environment.NewLine}      候选：{joined}";
            }

            return "— 无对应键（NoMatchingKey）";
        }
        catch (Exception ex)
        {
            // 绑定期异常会打断整个渲染，转换器里只记一条日志并显示占位文案。
            ErrorLog.Warn("规则解析预览", $"role='{roleText}' shader='{shader.ShaderName}'", ex);
            return "（预览失败）";
        }
    }

    /// <summary>不支持反向转换——预览列是由三元组只读派生的。</summary>
    /// <param name="value">写回值（未使用）。</param>
    /// <param name="targetTypes">目标类型数组（未使用）。</param>
    /// <param name="parameter">转换器参数（未使用）。</param>
    /// <param name="culture">区域性（未使用）。</param>
    /// <returns>永不返回。</returns>
    /// <exception cref="NotSupportedException">无条件抛出：预览列不可编辑。</exception>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException("解析预览列是只读的，不支持反向转换。");
}
#nullable restore