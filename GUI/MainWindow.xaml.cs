using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using GUI.Diagnostics;
using GUI.ViewModels;
using Lib;

namespace GUI;

/// <summary>
/// 主窗口 = <b>贴图快速导航</b>三步向导本体。
///
/// <para>旧主窗口的材质树 / 参数编辑器 / KV 预览 / 打开保存等全部删除后，
/// 快速导航从模态对话框升格为唯一窗口；DataContext 由 XAML 直接建立
/// （<c>Window.DataContext</c>），构造函数因此不再接受 ViewModel 参数。</para>
///
/// <para>每个事件处理器都用 <see cref="ControlErrorRecorder"/> 包住：
/// 即使某个控件的逻辑抛异常，也只是记录 + 提示，不会让整个应用退出。</para>
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 非阻塞的错误角标：任何控件出错都在底栏提示，不弹模态框。
        ErrorLog.EntryLogged += OnErrorLogged;
        Closed += (_, _) =>
        {
            ErrorLog.EntryLogged -= OnErrorLogged;
            // 唯一窗口关闭即进程退出；顺手取消并释放构建取消令牌源。
            try { ViewModel.Dispose(); } catch { /* 退出路径上的失败不值得再记一笔 */ }
        };
    }

    /// <summary>主 ViewModel（由 XAML 建立，这里只作类型化入口）。</summary>
    public MainViewModel ViewModel => (MainViewModel)DataContext;

    /// <summary>错误发生时更新底栏角标（非阻塞，避免连续弹窗淹没界面）。</summary>
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
            // 角标更新失败不影响错误记录本身。
        }
    }
    /// <summary>缩略图双击：用系统默认程序打开原图。</summary>
    /// <remarks>
    /// <para><b>为什么不用 <c>MouseDoubleClick</c>。</b>那是 <see cref="System.Windows.Controls.Control"/>
    /// 的事件，而 <see cref="System.Windows.Controls.Image"/> 直接继承
    /// <see cref="FrameworkElement"/>，拿不到它。只能收
    /// <see cref="MouseButtonEventArgs.ClickCount"/> 自己判定。</para>
    ///
    /// <para>文件可能在扫描之后被删掉或移走，因此先确认存在再交给外壳，
    /// 否则 <see cref="Process.Start(ProcessStartInfo)"/> 抛出的异常只会在日志里留一行，用户什么都看不到。</para>
    /// </remarks>
    private void OnThumbnailMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement { DataContext: ScanRowViewModel row }) return;

        ControlErrorRecorder.GuardWithDialog("打开贴图", sender, () =>
        {
            if (!File.Exists(row.FilePath))
            {
                throw new FileNotFoundException(
                    $"贴图已不存在，可能在扫描之后被移动或删除。\n\n{row.FilePath}", row.FilePath);
            }

            Process.Start(new ProcessStartInfo(row.FilePath) { UseShellExecute = true });
        });

        // 已消费：避免 DataGrid 再把这次双击当成「打开编辑」之类的默认行为。
        e.Handled = true;
    }

    /// <summary>
    /// 槽位组合框展开：先按当前槽位把候选项对齐一次，再让用户看见面板。
    /// </summary>
    /// <remarks>
    /// <para>换过一次着色器之后，某行可能带着一项「为保住旧归类而补的、不受支持」的候选。
    /// 用户后来把这行改选成别的槽位，那一项就失去来由了，但它还留在列表里，
    /// 展开时会读成「这个着色器支持它，只是它有问题」。</para>
    ///
    /// <para>之所以等到展开才对齐，而不是在槽位变更的回调里做：那时组合框正处在
    /// 选中回写的中间态，此时换 ItemsSource 可能把槽位直接清空。
    /// 详见 <see cref="ScanRowViewModel.ReapplyRoleOptions"/>。</para>
    /// </remarks>
    private void OnRoleDropDownOpened(object sender, EventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ScanRowViewModel row }) return;
        row.ReapplyRoleOptions();
    }

    /// <summary>拖拽<b>进入</b>与<b>悬停</b>共用：判定能否接收，并在合法时给出 Copy 光标。</summary>
    /// <remarks>
    /// 一次拖拽中 <c>DragOver</c> 会连续触发几十次，因此这里只读取
    /// <see cref="DataFormats.FileDrop"/> 的存在性，不枚举内容、不调
    /// <see cref="DropImportService.CanAccept"/>——后者要碰文件系统。
    /// </remarks>
    private void OnWindowDragOver(object sender, DragEventArgs e) => ControlErrorRecorder.Guard("拖入生成来源", this, () =>
                                                                          {
                                                                              e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                                                                                  ? DragDropEffects.Copy
                                                                                  : DragDropEffects.None;
                                                                              e.Handled = true;
                                                                          });

    /// <summary>拖拽离开窗口：清除悬停态。</summary>
    private void OnWindowDragLeave(object sender, DragEventArgs e) => ControlErrorRecorder.Guard("拖入生成来源", this, () =>
                                                                           {
                                                                               e.Effects = DragDropEffects.None;
                                                                               e.Handled = true;
                                                                           });

    /// <summary>
    /// 放下：把文件 / 文件夹路径并入 <see cref="MainViewModel.AssetsRoot"/>。
    /// </summary>
    /// <remarks>
    /// 这里<b>只收集来源，不触发生成</b>。拖入即开始写盘属于破坏性动作：
    /// 用户可能只是想换一个扫描目录，或者还没挑好着色器。
    /// 走到第三步点「开始生成」才是提交。
    /// </remarks>
    private void OnWindowDrop(object sender, DragEventArgs e) => ControlErrorRecorder.Guard("拖入资产文件夹", this, () =>
                                                                      {
                                                                          if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                                                                          {
                                                                              ViewModel?.AddDroppedPaths(paths);
                                                                              e.Effects = DragDropEffects.Copy;
                                                                          }
                                                                          else
                                                                          {
                                                                              e.Effects = DragDropEffects.None;
                                                                          }
                                                                          e.Handled = true;
                                                                      });

    /// <summary>点击步骤条回到第一步。生成中不允许跳走。</summary>
    private void OnStep1Clicked(object sender, MouseButtonEventArgs e) => ViewModel?.GoToStep1Command.Execute(null);

    /// <summary>点击步骤条跳到第二步；第一步没填齐时不放行。</summary>
    private void OnStep2Clicked(object sender, MouseButtonEventArgs e) => ViewModel?.GoToStep2Command.Execute(null);

    /// <summary>点击步骤条跳到第三步；前两步没满足时不放行。</summary>
    private void OnStep3Clicked(object sender, MouseButtonEventArgs e) => ViewModel?.GoToStep3Command.Execute(null);


    // ── 帮助菜单：诊断入口 ───────────────────────────────────────────────────

    /// <summary>帮助 → 贴图后缀自检。</summary>
    ///
    /// <para>这是规格 §11 那 13 项契约（以及另外 18 项扩展用例）唯一的<b>永久可重跑入口</b>。
    /// 旧主窗口删除后若不把它搬过来，规格里的自检清单就事实上无人能验证。</para>
    ///
    /// <para><b>安全性：</b><see cref="TextureAssignmentSelfTest.Run"/> 内部的全部文件用例都在
    /// <c>Path.GetTempPath()</c> 下的独立沙箱中执行并在 finally 清理，
    /// <b>不碰用户真实的配置</b>（注册表项
    /// <c>HKEY_CURRENT_USER\SOFTWARE\Kashimura\VmatGenerator</c>），
    /// 因此在已配置好规则的环境里点这一项也不会污染自己的配置。</para>
    private void OnRunTextureSuffixSelfTest(object sender, RoutedEventArgs e) => ControlErrorRecorder.GuardWithDialog("贴图后缀自检", sender, () =>
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

    /// <summary>帮助 → 打开错误日志（底栏错误角标同样走这里）。</summary>
    private void OnShowErrorLogClicked(object sender, RoutedEventArgs e) => ControlErrorRecorder.GuardWithDialog("打开错误日志窗口", sender,
            () => new ErrorLogWindow { Owner = this }.ShowDialog());

    /// <summary>帮助 → 打开日志文件所在目录。</summary>
    private void OnOpenLogFolderClicked(object sender, RoutedEventArgs e) => ControlErrorRecorder.GuardWithDialog("打开日志目录", sender, () =>
                                                                                  {
                                                                                      Process.Start(new ProcessStartInfo
                                                                                      {
                                                                                          FileName = ErrorLog.LogDirectory,
                                                                                          UseShellExecute = true,
                                                                                      });
                                                                                  });

    /// <summary>帮助 → 关于。</summary>
    private void OnAboutClicked(object sender, RoutedEventArgs e) => ControlErrorRecorder.GuardWithDialog("显示关于对话框", sender, () =>
                                                                              MessageBox.Show(
                                                                                  "VMAT 生成器 — 基于 WPF / .NET 10 Fluent UI 的 Source 2 贴图→.vmat 快速导航生成器。\n\n" +
                                                                                  "由 Lib + CommunityToolkit.Mvvm 构建，\n" +
                                                                                  "解析采用 ValveKeyValue 三方库，单文件 framework-dependent 发布。\n" +
                                                                                  "界面主题使用 .NET 10 Desktop Runtime 内置的 Fluent 资源。\n\n" +
                                                                                  $"错误日志：{ErrorLog.LogFilePath}",
                                                                                  "关于 VMAT 生成器",
                                                                                  MessageBoxButton.OK,
                                                                                  MessageBoxImage.Information));
}
