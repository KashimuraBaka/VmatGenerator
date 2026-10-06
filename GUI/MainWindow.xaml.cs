using System.Windows;
using GUI.Diagnostics;
using GUI.ViewModels;
using Lib;

namespace GUI;

/// <summary>
/// Code-behind for <see cref="MainWindow"/>.
///
/// 每个事件处理器都用 <see cref="ControlErrorRecorder.GuardWithDialog"/> 包住，
/// 这样即使某个控件的逻辑抛异常，也只是记录 + 提示，不会让整个应用退出
/// （修复前：任意未处理异常都会经 Dispatcher 冒泡并终止进程）。
///
/// 参数编辑器的输入过滤事件位于
/// <see cref="GUI.Resources.ParameterTemplates"/>。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 注意：这里<b>不</b>自动扫描任何目录。启动后保持空状态，由用户显式选择
        // 「打开文件夹」—— 曾硬编码开发机路径自动加载，换台机器就完全不可用。
        // 拖拽导入同样只响应用户的显式操作，不会在启动时读配置并自行扫描。

        // 非阻塞的错误角标：任何控件出错都在状态栏提示，不弹模态框。
        ErrorLog.EntryLogged += OnErrorLogged;
        Closed += (_, _) => ErrorLog.EntryLogged -= OnErrorLogged;
    }

    /// <summary>主 ViewModel。公开是为了让「工具栏按钮」等绑定入口也能拿到它。</summary>
    public MainViewModel ViewModel => (MainViewModel)DataContext;

    /// <summary>错误发生时更新状态栏角标（非阻塞，避免连续弹窗淹没界面）。</summary>
    private void OnErrorLogged(ErrorEntry entry)
    {
        try
        {
            if (entry.Severity is not (ErrorSeverity.Error or ErrorSeverity.Fatal)) return;

            ErrorBanner.Content = $"⚠ {ErrorLog.ErrorCount} 个错误";
            ErrorBanner.ToolTip = App.BuildErrorBanner(ErrorLog.ErrorCount);
            ErrorBanner.Visibility = Visibility.Visible;
        }
        catch
        {
            // 状态栏更新失败不影响错误记录本身。
        }
    }

    /// <summary>左侧树中材质条目的点击处理。</summary>
    private void OnMaterialEntryClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MaterialEntryViewModel entry }) return;
        var file = entry.DisplayName;
        ControlErrorRecorder.GuardWithDialog($"打开材质「{file}」", sender,
            () => ViewModel.EditMaterial(entry));
    }

    /// <summary>
    /// 拖拽<b>进入</b>窗口：与 <see cref="OnWindowDragOver"/> 同样需要立刻给出
    /// 「可接收 / 不可接收」的光标反馈，否则首次进入时鼠标指针仍是禁止样式。
    /// </summary>
    private void OnWindowDragEnter(object sender, DragEventArgs e) =>
        ControlErrorRecorder.Guard("拖拽处理", this, () => UpdateDropHover(e, FileDropOf(e.Data)));

    /// <summary>
    /// 拖拽<b>悬停</b>：只判断「能不能接收」，不枚举文件、不做任何写入 ——
    /// 枚举很贵，DragOver 在一次拖拽中会连续触发几十次。
    /// </summary>
    private void OnWindowDragOver(object sender, DragEventArgs e) =>
        ControlErrorRecorder.Guard("拖拽处理", this, () => UpdateDropHover(e, FileDropOf(e.Data)));

    /// <summary>拖拽<b>离开</b>窗口：仅清除悬停视觉反馈，不改任何状态。</summary>
    private void OnWindowDragLeave(object sender, DragEventArgs e) =>
        ControlErrorRecorder.Guard("拖拽处理", this, () => SetDropHover(false));

    /// <summary>
    /// 放下：把 <c>DragEventArgs</c> 里的路径数组原样交给
    /// <see cref="MainViewModel.HandleDroppedPathsCommand"/>，分类与写入逻辑全在 ViewModel 里。
    /// </summary>
    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        var ok = ControlErrorRecorder.Guard("拖拽处理", this, () =>
        {
            // 无论成功与否都要收起覆盖层，否则窗口上会永久留着一层高亮。
            SetDropHover(false);

            var paths = FileDropOf(e.Data);
            if (paths is null)
            {
                // 非文件拖放（窗口内文字等）不拦截，交给默认行为。
                e.Effects = DragDropEffects.None;
                e.Handled = false;
                return;
            }

            e.Effects = DragDropEffects.Copy;
            ViewModel.HandleDroppedPathsCommand.Execute(paths);
            e.Handled = true;
        });

        if (!ok)
        {
            ControlErrorRecorder.Guard("拖拽失败提示", this,
                () => ViewModel.StatusText = "拖拽导入失败，已跳过本次导入；详情见错误日志。");
        }
    }

    /// <summary>
    /// 悬停反馈的统一判定：拖拽数据里确实是文件时才允许复制光标，
    /// 否则把事件放行给潜在的其它拖放源（不劫持非文件拖放）。
    /// </summary>
    private void UpdateDropHover(DragEventArgs e, string[]? fileDrop)
    {
        if (DropImportService.CanAccept(fileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            SetDropHover(true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = false;
            SetDropHover(false);
        }
    }

    /// <summary>
    /// 从拖拽数据里取文件路径；不是文件拖放（或取不到）时返回 <c>null</c>。
    /// 这里刻意自己取一次而不是用 <c>e.Data.GetDataPresent</c> 反复探测，
    /// 避免在高频的 DragOver 里做多余的 COM 数据封送。
    /// </summary>
    private static string[]? FileDropOf(System.Windows.IDataObject? data)
    {
        try
        {
            return data?.GetData(DataFormats.FileDrop) as string[];
        }
        catch
        {
            // 某些外部拖放源在 GetData 时会抛 —— 视为不可接收。
            return null;
        }
    }

    /// <summary>切换覆盖层可见性；视觉反馈失败绝不影响拖拽本身。</summary>
    private void SetDropHover(bool hovering)
    {
        try
        {
            DropOverlay.Visibility = hovering ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ErrorLog.Warn("拖拽悬停反馈", nameof(SetDropHover), ex);
        }
    }

    /// <summary>工具 → 贴图后缀快速导航。</summary>
    private void OnOpenQuickNavClicked(object sender, RoutedEventArgs e)
    {
        ControlErrorRecorder.GuardWithDialog("打开贴图后缀快速导航", sender,
            () => new QuickNavWindow(ViewModel.QuickNav) { Owner = this }.ShowDialog());
    }

    /// <summary>文件 → 退出。</summary>
    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        ControlErrorRecorder.Guard("关闭窗口", sender, () => Close());
    }

    /// <summary>视图 → 按着色器过滤。</summary>
    private void OnFilterMenuClicked(object sender, RoutedEventArgs e)
    {
        ControlErrorRecorder.GuardWithDialog("按着色器过滤", sender,
            () => ViewModel.FilterByShaderCommand.Execute(null));
    }

    /// <summary>帮助 → 贴图后缀自检。</summary>
    ///
    /// <para>这是规格 §11 那 13 项契约（以及另外 18 项扩展用例）唯一的<b>永久可重跑入口</b>：
    /// 在此之前它们只能靠一个一次性的 %TEMP% console 工程跑，而那个工程已被清理，
    /// 导致规格里的自检清单事实上无人能验证。</para>
    ///
    /// <para><b>安全性：</b><see cref="TextureAssignmentSelfTest.Run"/> 内部的全部文件用例都在
    /// <c>Path.GetTempPath()</c> 下的独立沙箱中执行并在 finally 清理，
    /// <b>不碰用户真实的配置</b>（注册表项
    /// <c>HKEY_CURRENT_USER\SOFTWARE\Kashimura\VmatGenerator</c>），
    /// 因此在已配置好规则的环境里点这一项也不会污染自己的配置。</para>
    private void OnRunTextureSuffixSelfTest(object sender, RoutedEventArgs e)
    {
        ControlErrorRecorder.GuardWithDialog("贴图后缀自检", sender, () =>
        {
            var report = TextureAssignmentSelfTest.Run();

            // 逐条结果始终留档，这样用户报 bug 时可以直接给日志文件。
            ErrorLog.Info($"贴图后缀自检：{report.Summary()}", nameof(MainWindow));

            var body = report.Passed
                ? report.Summary()
                  + "\n\n全部用例通过。本机设置未被改动（自检在临时目录中运行）。"
                  + $"\n\n逐条结果已写入：\n{ErrorLog.LogFilePath}"
                : report.Summary() + "\n\n" + report.Describe()
                  + $"\n\n逐条结果已写入：\n{ErrorLog.LogFilePath}";

            MessageBox.Show(
                body,
                report.Passed ? "贴图后缀自检：全部通过" : "贴图后缀自检：存在失败用例",
                MessageBoxButton.OK,
                report.Passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        });
    }

    /// <summary>帮助 → 打开错误日志。</summary>
    private void OnShowErrorLogClicked(object sender, RoutedEventArgs e)
    {
        ControlErrorRecorder.GuardWithDialog("打开错误日志窗口", sender,
            () => new ErrorLogWindow { Owner = this }.ShowDialog());
    }

    /// <summary>帮助 → 打开日志文件所在目录。</summary>
    private void OnOpenLogFolderClicked(object sender, RoutedEventArgs e)
    {
        ControlErrorRecorder.GuardWithDialog("打开日志目录", sender, () =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ErrorLog.LogDirectory,
                UseShellExecute = true,
            });
        });
    }

    /// <summary>帮助 → 关于。</summary>
    private void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        ControlErrorRecorder.GuardWithDialog("显示关于对话框", sender, () =>
            MessageBox.Show(
                "VMAT 生成器 — 基于 WPF / .NET 10 Fluent UI 的 Source 2 VMAT 编辑工具。\n\n" +
                "由 Lib + CommunityToolkit.Mvvm 构建，\n" +
                "解析采用 ValveKeyValue 三方库，单文件 framework-dependent 发布。\n" +
                "界面主题使用 .NET 10 Desktop Runtime 内置的 Fluent 资源。\n\n" +
                $"错误日志：{ErrorLog.LogFilePath}",
                "关于 VMAT 生成器",
                MessageBoxButton.OK,
                MessageBoxImage.Information));
    }
}