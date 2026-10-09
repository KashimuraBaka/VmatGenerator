# VmatGenerator · 贴图快速导航

面向 **Source 2（CS:GO / CS2）`.vmat` 材质** 的桌面生成器：把一堆按后缀命名的贴图
（`_diffuse` / `_normal` / `_roughness` / …）自动归类、分配语义槽位，一键写出可直接进引擎的
`.vmat` 文件。WPF + .NET 10 内置 Fluent 主题，无任何第三方 UI 库。

## 它能做什么

- **三步向导**：① 选择着色器 + 资产文件夹（贴图来源）+ 项目文件夹（`.vmat` 输出位置）
  → ② 扫描贴图、按「去掉后缀的基名」自动归组成材质、逐行核对归属，右侧实时预览最终
  KeyValues1 文本（预览与写盘走同一条渲染路径，所见即所得）→ ③ 写出 `.vmat`，
  可取消、有进度、「打开输出文件夹」在生成成功后才可点。
- **拖拽导入**：把贴图 / 材质文件夹 / `.vmat` 直接拖进窗口，自动识别内容类别
  （材质文件夹、`.vmat` 文件、贴图、混合、无关），并按后缀规则表把贴图写进正确的参数键。
- **后缀 → 语义槽位规则表**：出厂 35 条种子规则（`_d`、`_normal`、`_ao`、`_mask` …），
  可增删改排、可停用；多规则命中时按确定性优先级裁定：
  **最长后缀 → 整名相等 → 文件名词数少者 → 数组下标小者**。
- **语义槽位 → 参数键解析（P1–P5 分档）**：同一语义在不同着色器上落到不同参数键
  （如 Normal 在 `csgo_environment` 是 `TextureNormal1`，在 `csgo_vertexlitgeneric` 是
  `TextureNormal`），歧义时按档位取唯一候选，无法定档则**宁可不写**并给出诊断。
  未命中任何规则的贴图会兜底进 Color 槽位（显式排除贴图与非贴图文件除外）。
- **颜色是底线**：组内没有 Color 贴图就不产出那份 `.vmat`——拒绝「打开只剩白底、
  引擎里报错」的空壳材质。
- **贴图随材质走**：输出目录 = 项目根 + basecolor 贴图相对资产根的子目录，
  输出布局与资产布局一一对应，用到的贴图一并复制进同一文件夹。
- **设置持久化**：所有配置只在点「保存设置」时落盘到注册表
  `HKCU\SOFTWARE\Kashimura\VmatGenerator`（首次运行会自动迁移旧的
  `%APPDATA%\VmatGenerator\settings.json`），敲错一个字符不会污染配置。
- **不闪退**：UI 线程 /后台线程 / Task 三层全局异常拦截，出错只记日志
  （`%LOCALAPPDATA%\VmatGenerator\`），底栏出现「⚠ N 个错误」角标，点击即可查看明细。

## 支持的着色器模板（11 个）

| 模板 | 典型用途 |
| --- | --- |
| `csgo_environment` | 场景环境（多层纹理、雪 / 湿润、细节层） |
| `csgo_lightmappedgeneric` | 光照贴图静态几何 |
| `csgo_vertexlitgeneric` | 顶点光照通用 |
| `csgo_character` | 角色（边缘光 / 瞳彩等） |
| `csgo_complex` | 复杂混合（detail blend、phong spec） |
| `csgo_static_overlay` | 静态贴花 |
| `csgo_water_fancy` | 水面（泡沫 / 波浪 / 漂浮物多层） |
| `csgo_effects` | 特效 |
| `csgo_moondome` | 天空立方 |
| `sky` | 天空 |
| `generic` | 通用 / 工具材质（nodraw、clip 等） |

模板是 161 个官方 `.vmat` 样本扫描分析后的出厂基线（见
[docs/shader-analysis.md](docs/shader-analysis.md)），以 `EmbeddedResource` 编入
`Lib.dll`（[Lib/Templates/](Lib/Templates/)，**冻结，勿改**）。

## 快速开始

### 环境要求

- 构建：Windows + [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 运行：Windows x64 + **.NET 10 Desktop Runtime**（发布产物是框架依赖单文件，不打包运行时）

### 开发构建

```powershell
dotnet build VmatGenerator.sln -c Debug
dotnet run --project GUI
```

构建口径是**严格 0 警告 0 错误**（`EnforceCodeStyleInBuild` 打开，IDE 风格提示参与构建报告）。

### 发布单文件 exe

根目录提供 [build-single-app.bat](build-single-app.bat)，双击即可：

```
build-single-app.bat          # 人工使用（结束时 pause）
build-single-app.bat /auto    # 自动化 / CI
```

产物为 `bin\publish\GUI.exe`（约 0.85 MB 单文件：应用与三方托管库全部内嵌；
WPF 与 .NET 运行时来自机器上已装的 Desktop Runtime）。脚本会先清空发布目录、
发布后删除 XML 文档工件，目录里只剩一个 exe。

### 跑 Lib 自检（无需打开界面）

`Lib` 内置 41 条无头自检用例，覆盖后缀匹配、P1–P5 分档、规则往返读写、
生成写盘与拖放分类，不触碰真实配置目录：

```powershell
dotnet build VmatGenerator.sln -c Debug
Add-Type -Path "<输出目录>\Lib.dll"
[Lib.TextureAssignmentSelfTest]::Run().ToString()   # → 自检 41/41 通过
```

## 项目结构

```
VmatGenerator/
├── VmatGenerator.sln
├── build-single-app.bat          # 单文件发布脚本（纯 ASCII，任意代码页可跑）
├── Lib/                          # 核心库（net10.0，无 UI 依赖）
│   ├── Templates/*.vmat          #   11 个出厂着色器基线模板（嵌入式资源，冻结）
│   ├── ShaderCatalog.cs          #   着色器模板目录（参数 / Feature / 属性叠加）
│   ├── TextureSuffixMatcher.cs   #   后缀匹配与优先级裁定
│   ├── TextureRoleResolver.cs    #   语义槽位 → 参数键（P1–P5 分档）
│   ├── DropImportService.cs      #   拖拽内容分类（D1–D6）
│   ├── VmatBuildService.cs       #   按组写出 .vmat + 贴图复制（预览同源）
│   ├── VmatFormat / KvAdapter /… #   ValveKeyValue 解析 / 序列化适配
│   └── TextureAssignmentSelfTest.cs  # 41 条无头自检
├── GUI/                          # WPF 主程序（net10.0-windows7.0）
│   ├── MainWindow.xaml(.cs)      #   三步向导 + 无边框自定义标题栏（WindowChrome）
│   ├── ViewModels/               #   MainViewModel（主 + Wizard + TemplatePreview 分部）
│   ├── Settings/                 #   注册表配置存储（含旧 JSON 迁移）
│   └── Diagnostics/              #   错误日志 + 控件级异常护栏
└── docs/                         # 分析报告 / 交付总报告 / 功能规格与使用说明
```

## 技术选型

| 方面 | 选择 | 说明 |
| --- | --- | --- |
| UI | WPF **.NET 10 内置 Fluent 主题**（`ThemeMode="System"`） | 明暗随系统；图标用 Segoe Fluent Icons；已移除 ModernWpfUI |
| 窗口 | `WindowStyle="None"` + `WindowChrome` | 自绘标题栏（最小化 / 最大化⇄还原 / 关闭），保留系统拖拽、贴边快照与 Win11 圆角；最大化经 `WM_GETMINMAXINFO` 修正不遮任务栏 |
| MVVM | CommunityToolkit.Mvvm 8.4 | `[ObservableProperty]` / `[RelayCommand]` |
| KV 解析 | ValveKeyValue 0.71 | 与官方 `.vmat`（KV1 文本）双向往返 |
| 发布 | framework-dependent 单文件 | 见 `GUI/GUI.csproj` 注释与发布脚本 |

## 文档

| 文档 | 内容 |
| --- | --- |
| [docs/feature-dragdrop-suffix-usage.md](docs/feature-dragdrop-suffix-usage.md) | 面向使用者：拖拽 + 后缀规则怎么配、35 条出厂规则的实测落点 |
| [docs/feature-dragdrop-suffix-spec.md](docs/feature-dragdrop-suffix-spec.md) | 契约级规格：匹配判据、排序全序、Lib API 签名 |
| [docs/shader-analysis.md](docs/shader-analysis.md) | 161 样本 → 11 着色器的差异分析（模板补全的依据） |
| [docs/REPORT.md](docs/REPORT.md) | 交付总报告（各阶段实现走查与验收记录） |

## 已知边界

- `Emissive` / `WavesMask` / `Lightmap` 三个语义槽位在当前 11 个模板上没有对应参数键，
  对应 4 条出厂规则是**预留规则**：命中不会写入任何参数，状态栏会明确说明。
- `.vmat` 的探测始终递归，与「贴图递归枚举」开关无关——这是两个独立开关。
- 生成同名文件已存在时直接覆盖（向导内），且已写出的 `.vmat` 不随「取消生成」回滚。
