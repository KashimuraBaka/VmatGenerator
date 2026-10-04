using System.Windows;
using GUI.Diagnostics;
using GUI.ViewModels;

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

        // 非阻塞的错误角标：任何控件出错都在状态栏提示，不弹模态框。
        ErrorLog.EntryLogged += OnErrorLogged;
        Closed += (_, _) => ErrorLog.EntryLogged -= OnErrorLogged;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

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