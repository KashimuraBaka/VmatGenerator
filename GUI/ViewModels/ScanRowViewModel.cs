using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GUI.Imaging;
using Lib;

namespace GUI.ViewModels;

/// <summary>
/// 第二步扫描列表里的一行：一个被发现的图像文件，以及它被归到了哪个 <c>.vmat</c>。
/// </summary>
/// <remarks>
/// <b>为什么 <see cref="GroupName"/> 可写：</b>自动扫描按「去掉后缀后的基名」分组，
/// 但用户对「哪些贴图属于同一份材质」的判断常常与命名约定不一致
/// （例如同一张 <c>wall_diff.png</c> 被两个材质共用）。
/// 允许逐行改归属，比强迫用户去改后缀规则更直接。
/// </remarks>
public sealed partial class ScanRowViewModel : ObservableObject
{
    /// <summary>构造一行扫描结果。</summary>
    /// <param name="filePath">源文件完整路径。</param>
    /// <param name="matchedSuffix">命中的归一化后缀；未命中时为空串。</param>
    /// <param name="role">命中的语义槽位；未命中时为 <see cref="TextureRole.Unknown"/>。</param>
    /// <param name="groupName">初始归属的 .vmat 名（= 去掉后缀后的基名）。</param>
    public ScanRowViewModel(string filePath, string matchedSuffix, TextureRole role, string groupName)
    {
        FilePath = filePath;
        MatchedSuffix = matchedSuffix;
        Role = role;
        _groupName = groupName;
    }

    /// <summary>源文件完整路径。</summary>
    public string FilePath { get; }

    /// <summary>文件名（含扩展名），列表显示用。</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>所在子目录相对资产根的路径；直接位于根下时为空串。</summary>
    public string RelativeDirectory { get; init; } = string.Empty;

    /// <summary>命中的归一化后缀，例如 <c>normal</c>；未命中时为空串。</summary>
    public string MatchedSuffix { get; }

    /// <summary>命中的语义槽位。</summary>
    public TextureRole Role { get; }

    /// <summary>槽位的中文名，便于在列表里一眼看懂。</summary>
    public string RoleDisplay => Role == TextureRole.Unknown ? "（未命中）" : Role.ToString();

    /// <summary>是否命中了可用的后缀规则。</summary>
    public bool IsMatched => Role != TextureRole.Unknown;

    /// <summary>该贴图是否参与生成（未命中的默认不参与）。</summary>
    [ObservableProperty]
    private bool _include;

    private string _groupName;

    /// <summary>
    /// 归属的 <c>.vmat</c> 名（不含扩展名）。用户可手动改。
    /// </summary>
    /// <remarks>
    /// 改动会触发 <see cref="GroupChanged"/>，由 <see cref="QuickNavViewModel"/> 重新聚合分组预览。
    /// </remarks>
    [ObservableProperty]
    private string? _groupNameOverride;

    /// <summary>实际生效的分组名：用户指定优先，否则用扫描给出的基名。</summary>
    public string GroupName => string.IsNullOrWhiteSpace(GroupNameOverride) ? _groupName : GroupNameOverride.Trim();

    /// <summary>分组归属发生变化时触发。</summary>
    public event Action? GroupChanged;

    /// <summary>重设扫描给出的基名（重新扫描时用，会清掉用户的手动指定）。</summary>
    public void ResetGroup(string baseName)
    {
        _groupName = baseName;
        GroupNameOverride = string.Empty;
        OnPropertyChanged(nameof(GroupName));
    }

    /// <summary>由生成器挂接 <see cref="GroupNameOverride"/> 的属性变更通知。</summary>
    partial void OnGroupNameOverrideChanged(string? value)
    {
        OnPropertyChanged(nameof(GroupName));
        GroupChanged?.Invoke();
    }
// ─── 缩略图 ────────────────────────────────────────────────────────────

    private BitmapSource? _thumbnail;
    private int _thumbnailRequested;

    /// <summary>
    /// 该行的缩略图；还没解出来、或这个文件根本解不出来时为 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <para><b>取值即触发。</b>getter 里调 <see cref="EnsureThumbnail"/>，于是
    /// <c>Image.Source="{Binding Thumbnail}"</c> 一条绑定就够：解码完成后
    /// <see cref="LoadThumbnailAsync"/> 会抛 <c>PropertyChanged(Thumbnail)</c>，
    /// 绑定重新求值把图填上。</para>
    ///
    /// <para><b>为什么不用转换器。</b>早先写成
    /// <c>Source="{Binding FilePath, Converter=…}"</c>，有两个致命问题：
    /// 转换器拿到的是 <see cref="FilePath"/> 这个 <b>string</b>，不是本行，
    /// 类型判断直接落空、永远返回 <c>null</c>；就算改成绑本行，绑定源
    /// （文件路径）此后不会变化，异步解码完成也不会重新求值。
    /// 触发和取值必须是<b>同一个</b>属性，否则异步结果送不回去。</para>
    ///
    /// <para><b>内存不会随列表长度线性增长。</b>缩略图按 64px 解码，
    /// 再由 <see cref="ThumbnailCache"/> 按字节预算做 LRU。</para>
    ///
    /// <para>重复触发由 <see cref="EnsureThumbnail"/> 的一次性闸门兜住，
    /// 绑定被反复求值也只解一次。</para>
    /// </remarks>
    public BitmapSource? Thumbnail
    {
        get
        {
            EnsureThumbnail();
            return _thumbnail;
        }
    }

    /// <summary>是否已拿到缩略图；<c>false</c> 时界面显示占位符。</summary>
    public bool HasThumbnail => _thumbnail is not null;

    /// <summary>
    /// 请求解码本行缩略图。重复调用只生效一次。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="Thumbnail"/> 的 getter 在绑定求值时调用，因此只在行被实现
    /// （滚进可视区）时才发生——DataGrid 用 <c>Recycling</c> 回收模式，
    /// 容器复用给新行时绑定一定重新求值，这比 <c>LoadingRow</c> 事件可靠得多。
    /// </remarks>
    public void EnsureThumbnail()
    {
        if (Interlocked.Exchange(ref _thumbnailRequested, 1) == 1) return;

        // 本方法必然在 UI 线程被调用，先抓住 Dispatcher；
        // 解码跑在后台，回来时把通知切回 UI 线程——WPF 绑定要求 PropertyChanged 在 UI 线程。
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = LoadThumbnailAsync(dispatcher);
    }

    private async Task LoadThumbnailAsync(Dispatcher dispatcher)
    {
        var source = await ThumbnailCache.Shared.GetAsync(FilePath);

        await dispatcher.InvokeAsync(() =>
        {
            _thumbnail = source;
            OnPropertyChanged(nameof(Thumbnail));
            OnPropertyChanged(nameof(HasThumbnail));
        });
    }
}
