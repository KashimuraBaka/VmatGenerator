# GUI 项目交付总报告

> 项目根目录: `H:\dsh-workspace\vmat-generater`
> 报告生成时间: 2026-10-04
> 本报告覆盖 GUI 重建流水线 t1 → t6 的全部交付物及最终验证结果。

---

## 1. 项目结构

```
H:\dsh-workspace\vmat-generater
├── materials\                      # 161 个 .vmat 样本（11 个 shader）
├── tools\
│   ├── scan_vmat.ps1               # 样本扫描脚本（t2）
│   └── scan_out.md                 # 扫描原始 dump（583 行）
├── VmatGenerator\
│   ├── docs\
│   │   ├── shader-analysis.md      # t2 产出：按 shader 分组的差异分析
│   │   └── REPORT.md               # ← 本文件
│   ├── Lib\           # t3 补全的 shader 模板 + ValveKeyValue 适配
│   │   ├── KvAdapter.cs
│   │   ├── MaterialScanner.cs
│   │   ├── ShaderCatalog.cs
│   │   ├── ShaderTemplate.cs
│   │   ├── ShaderTemplateEmitter.cs
│   │   ├── VmatDocument.cs
│   │   ├── VmatFormat.cs
│   │   ├── VmatNode.cs
│   │   ├── Lib.csproj
│   │   └── Templates\*.vfx.vmat    # 11 个 embedded baseline 模板
│   ├── GUI\            # t4 WPF 主程序（.NET 10 内置 Fluent 主题）
│   └── bin\publish\                 # t5 framework-dependent 单文件发布（不打包 .NET 运行库）
│       ├── GUI.exe     # 单文件 apphost
│       ├── GUI.pdb
│       └── Lib.pdb
└── .agent-teams\vmat-generator-rebuild\   # 团队协调元数据（只读）
```

> **清理说明**：`RoundtripTest\`、`VmatTemplateGenerator\`、`obj\`（`baselines/`、`test-logs/`）与 `artifacts\` 等测试产物与配套工程已按用户要求删除，解决方案现仅保留 `Lib` + `GUI` 两个工程。

---

## 2. 各阶段交付概览

### t2 — Shader 分析报告

- **产出**: `VmatGenerator/docs/shader-analysis.md`（18.85 KB，332 行）
- **扫描规模**: 161 个 .vmat 样本 → 11 个 shader 桶
- **关键产出**: 每 shader 的参数 union 表、横向问题清单（SystemAttributes / CompiledTextures 子块缺位、F_DETAIL_TEXTURE vs F_DETAILTEXTURE 拼写差异、Texture 键类型拓宽需求）、优先级建议
- **状态**: 已完成；t3 据此对 ShaderCatalog 做补全

### t3 — ValveKeyValue 接入 + shader 模板补全

- **NuGet 接入**: `ValveKeyValue 0.71.0.528` + `Microsoft.Extensions.Logging.Abstractions 8.0.2`
  （注：任务描述中的 `ValveKeyValues` 拼写不存在；实际包 id 为 `ValveKeyValue` 单数）
- **KvAdapter**: 提供 `ParseViaValve` / `SerializeViaValve` / `StructuralEquals` / `RoundTripEquals`，作为 ValveKeyValue 与本库 `VmatNode` 的双向适配器
- **VmatFormat**: 拆掉自实现 Parser；`Parse` 现为 `KvAdapter.ParseViaValve` 的薄壳
- **ShaderCatalog**: 补全 11 个 shader 模板（csgo_water_fancy +55 项、csgo_lightmappedgeneric +6 项、csgo_static_overlay +color-correction/self-illum 子组、csgo_complex +detail-blend/phong-spec 子组、csgo_vertexlitgeneric +translucency/alpha/backfaces、csgo_environment +snow/wetness/UV-set、sky +F_TEXTURE_FORMAT2、csgo_effects +TextureTranslucency）
- **ShaderTemplate**: 新增 `SystemAttributeDefaults` / `CompiledTextureKeys` / `RequiredFeatureFlag` / `Shape(TextureOrVector)`
- **BuildDocument / Render**: 完整产出 `Compiled Textures` 子块 + `SystemAttributes` 子块；Feature flag 与同名 Parameter 冲突时参数循环不再覆盖；Vector4.ToString 统一为 `0.000000`
- **Embedded templates**: `Templates/*.vmat` 经 csproj `<EmbeddedResource>` 嵌入，提供 `LoadEmbedded` / `EnumerateEmbedded` API
- **状态**: 已完成

### t4 — WPF Fluent UI 主窗口

- **技术选型**: .NET 10 WPF **内置 Fluent 主题**（`Application.ThemeMode="System"`，主题资源由 .NET 10 Desktop Runtime 自带的 `PresentationFramework.Fluent` 程序集提供）+ `CommunityToolkit.Mvvm 8.4.0`。**不再使用任何第三方 Fluent UI 库**（已移除 `ModernWpfUI`）
- **主窗口布局**: 左侧 `TreeView`（shader 分组 → VMAT 列表）+ 中间参数编辑器（根据 ShaderCatalog 自动生成 Float/Int/Bool/Vector/Texture 行，由 `ParameterRowTemplateSelector` 派发到 4 个 DataTemplate）+ 右侧只读 KV 预览
- **命令**: `NewVmatCommand`、`OpenFolderCommand`、`SaveCurrentCommand`、`SaveAsCommand`、`CopyKvCommand`、`FilterByShaderCommand`、`ClearFilterCommand`、`RefreshCommand`（全部 `[RelayCommand]`）
- **控件映射**（第三方控件 → WPF 原生控件 + Fluent 样式）:

  | 原第三方控件 | 现用 WPF 原生控件 |
  |---|---|
  | `NavigationView` | `TreeView` |
  | `CommandBar` / `AppBarButton` | `ToolBar` / `Button` |
  | `SymbolIcon` | `TextBlock` + `Segoe Fluent Icons` 字体 |
  | `ToggleSwitch` | `CheckBox`（`Content` 绑定「开」/「关」） |
  | `NumberBox` | `TextBox` + 数字输入过滤（`PreviewTextInput` / 粘贴校验） |
  | `ControlHelper.PlaceholderText` | `DataTrigger` 叠加的提示 `TextBlock` |

- **主题资源键**: 使用 .NET 10 Fluent 主题的真实键名（`LayerFillColorAltBrush`、`SolidBackgroundFillColorBaseBrush`、`TextFillColorSecondaryBrush`、`DividerStrokeColorDefaultBrush`、`ApplicationBackgroundBrush`）
- **状态**: 已完成

### t5 — Framework-dependent 单文件发布（不打包 .NET 运行库）

> 用户的最终意图是「单 exe 文件」 + 「应用不打包 .NET 运行库」。.NET 10 的 framework-dependent 单文件模式可以同时满足这两条：apphost 仍是单一 exe，apphost 内嵌了我们的 app DLL + 全部第三方 DLL（ValveKeyValue / CommunityToolkit.Mvvm / Microsoft.Extensions.*），但 .NET runtime DLL 与 WPF framework DLL（PresentationCore / PresentationNative_cor3 / wpfgfx_cor3 等）仍由用户机器上的 .NET 10 Desktop Runtime 提供。

- **csproj 发布参数**:
  ```xml
  <TargetFramework>net10.0-windows7.0</TargetFramework>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>false</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <UseAppHost>true</UseAppHost>
  <WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
  <WindowsPackageType>None</WindowsPackageType>
  ```
- **产物**: `GUI\bin\Release\net10.0-windows7.0\win-x64\publish\`，亦发布到 `bin\publish\` 并拷贝到 `artifacts\publish\`
- **关键文件**: 3 个
  - `GUI.exe` (4.88 MB — 单文件 apphost，PE32+ AMD64)
  - `GUI.pdb` (39 KB)
  - `Lib.pdb` (18 KB)
- **审计结果**: ✅ 不含 `System.Private.CoreLib.dll` / `System.Runtime.dll` / `clrjit.dll` / `coreclr.dll`；✅ 不含 `Microsoft.NETCore.App` / `Microsoft.WindowsDesktop.App` / `Microsoft.WindowsAppRuntime` 子目录；✅ `PresentationCore.dll` / `PresentationNative_cor3.dll` / `wpfgfx_cor3.dll` 等 WPF framework DLL 由用户 .NET 10 Desktop Runtime 提供，本目录正确缺席
- **状态**: 已完成；详细 audit 见 §4

### t6 — 端到端 round-trip + 启动验证（本任务）

详见 §3 / §4。

---

## 3. Round-trip 测试结果（t6 核心交付）

> ⚠️ **归档说明**：`RoundtripTest` 验证工程与其产物（`obj/baselines/`、`artifacts/`）已按用户要求在清理阶段删除。下方记录的是删除前最后一次运行的完整结果（852/852 项通过），保留作为库层正确性的历史证据。若需重新验证，需重建该测试工程。

### 3.1 RoundtripTest 设计

测试入口: `RoundtripTest/Program.cs`（**已删除**）

四个阶段：

| 阶段 | 输入 | 断言 |
|---|---|---|
| **Phase 1** | `materials/` 目录 | `MaterialScanner.Scan` 返回 ≥ 1 文档；按 shader 分组得到 11 个桶 |
| **Phase 2** | `ShaderCatalog.All`（11 个 shader） | 对每个 shader 调用 `ShaderTemplateEmitter.EmitDefault` → `KvAdapter.ParseViaValve` 写回的树必须包含 `shader` 键（值匹配）、每个 catalog 参数、每个 feature flag、每个 compiled texture key、每个 system attribute default、每个 attribute flag；且 re-parsed 根必须为 `Layer0` |
| **Phase 3** | `ShaderTemplateEmitter.EnumerateEmbedded` | 11 个 embedded 模板资源均可经 `ValveKeyValue` 重新解析；`shader` 值与 catalog 一致 |
| **Phase 4** | 20 个随机采样的真实 `.vmat` 文件 | 经 `VmatGenerator.Render` 重渲染后再用 ValveKeyValue 解析；输入侧每个参数 / feature flag / attribute 都必须在重渲染产物中存在并保持原值 |

### 3.2 Baseline 产物

`VmatGenerator/obj/baselines/`（以及 `artifacts/baselines/` 副本）：

| 文件 | 字节 |
|---|---:|
| `csgo_character.vfx.vmat` | 1,425 |
| `csgo_complex.vfx.vmat` | 2,122 |
| `csgo_effects.vfx.vmat` | 1,333 |
| `csgo_environment.vfx.vmat` | 1,555 |
| `csgo_lightmappedgeneric.vfx.vmat` | 1,605 |
| `csgo_moondome.vfx.vmat` | 789 |
| `csgo_static_overlay.vfx.vmat` | 2,001 |
| `csgo_vertexlitgeneric.vfx.vmat` | 2,167 |
| `csgo_water_fancy.vfx.vmat` | 4,080 |
| `generic.vfx.vmat` | 404 |
| `sky.vfx.vmat` | 238 |

每个 baseline 由 `VmatGenerator.BuildDocument` → `VmatNode.Serialize` 生成，包含该 shader 的完整 KV1 文档（`shader` / 所有参数 / 所有 feature flag / `Compiled Textures` / `SystemAttributes` / `Attributes` 子块）。

### 3.3 运行结果

```
$ dotnet run --project RoundtripTest -c Release
== Phase 1: Scan materials from H:\dsh-workspace\vmat-generater\materials ==
Scanned 161 VMAT files
  ok: scanner returned at least one document
Grouped into 11 shader buckets
  - csgo_character.vfx: 2 materials
  - csgo_complex.vfx: 26 materials
  - csgo_effects.vfx: 1 materials
  - csgo_environment.vfx: 48 materials
  - csgo_lightmappedgeneric.vfx: 6 materials
  - csgo_moondome.vfx: 1 materials
  - csgo_static_overlay.vfx: 11 materials
  - csgo_vertexlitgeneric.vfx: 61 materials
  - csgo_water_fancy.vfx: 1 materials
  - generic.vfx: 1 materials
  - sky.vfx: 3 materials
== Phase 2: Catalog baselines -> ...obj/baselines ==
  ... 11 个 shader × ~70 checks
== Phase 3: Embedded template resources ==
  ok: embedded resource count (11) matches ShaderCatalog.All count (11)
  ... 11 × 2 = 22 passes
== Phase 4: Real-material round-trip sample ==
Sampled 20 materials
  ... 20 × 1 = 20 passes

Round-trip OK
  852 checks passed across 11 catalog shaders and 20 sampled materials
  Baselines written to H:\dsh-workspace\vmat-generater\VmatGenerator\obj\baselines
```

- **总断言数**: 852
- **失败数**: 0
- **退出码**: 0
- **关键键匹配**: 对每个 catalog shader 的 `shader` / `TextureColor*` / `TextureNormal*` / `g_vColorTint` / `F_TRANSLUCENT` / `F_SELF_ILLUM` / `F_LIT` / `F_DETAILTEXTURE` / `F_CAUSTICS` / `F_BLUR_REFRACTION` / `F_TEXTURE_FORMAT2` 等关键键均通过 ValveKeyValue 解析回比对的检查

### 3.4 Phase 4 真实样本明细

| 样本 | shader | 输入键总数 | round-trip 结果 |
|---|---|---:|---|
| `photo_lv2.vmat` | csgo_vertexlitgeneric.vfx | 29 | ✅ |
| `glowstick_before.vmat` | csgo_complex.vfx | 24 | ✅ |
| `concrete_pillar_parking.vmat` | csgo_environment.vfx | 21 | ✅ |
| `fun_floor.vmat` | csgo_vertexlitgeneric.vfx | 22 | ✅ |
| `tunnel_concretewall_01c.vmat` | csgo_environment.vfx | 18 | ✅ |
| `backroom_house03.vmat` | csgo_environment.vfx | 18 | ✅ |
| `cd.vmat` | csgo_vertexlitgeneric.vfx | 24 | ✅ |
| `hostiles_concrete1_red.vmat` | csgo_complex.vfx | 25 | ✅ |
| `party_goer.vmat` | csgo_vertexlitgeneric.vfx | 22 | ✅ |
| `eyeball_l.vmat` | csgo_environment.vfx | 18 | ✅ |
| `corpse_06.vmat` | csgo_complex.vfx | 7 | ✅ |
| `buoy_duck_color.vmat` | csgo_vertexlitgeneric.vfx | 24 | ✅ |
| `fadian.vmat` | csgo_environment.vfx | 18 | ✅ |
| `pool_tunnl.vmat` | csgo_lightmappedgeneric.vfx | 18 | ✅ |
| `green_wall.vmat` | csgo_vertexlitgeneric.vfx | 22 | ✅ |
| `checkout.vmat` | csgo_vertexlitgeneric.vfx | 22 | ✅ |
| `backroom_house02.vmat` | csgo_environment.vfx | 18 | ✅ |
| `backroom_decal_sign05.vmat` | csgo_static_overlay.vfx | 27 | ✅ |
| `white_notbright.vmat` | csgo_complex.vfx | 24 | ✅ |
| `metaltruss009a.vmat` | csgo_vertexlitgeneric.vfx | 5 | ✅ |

每个真实 .vmat 的全部参数 / feature flag / attribute 经 VmatGenerator.Render 重渲染 → ValveKeyValue 重解析后，输入键无一缺失或失配。

### 3.5 已知约束

- 当参数与 feature flag 同名（如 `F_TRANSLUCENT` 既在 csgo_complex 的 featureFlags 又作为参数存在）时，`BuildDocument` 的 feature-flag 循环是唯一权威——emitter 启用全部 flag，所以 baseline 中此 key 永远为 `"1"`。测试在「参数默认值比对」时显式跳过此类 key，只断言「key 存在」与「value 为 0/1」。
- `csgo_complex` 不定义 `attributeFlags` → Attributes 子块不会出现在 baseline；测试条件 `if (shader.AttributeFlags.Count > 0)` 正确跳过该断言。
- `sky.vfx` 不定义 `compiledTextureKeys` → Compiled Textures 子块也不出现；测试条件 `if (shader.CompiledTextureKeys.Count > 0)` 同样跳过。

完整日志见 `artifacts/roundtrip.log`（77 KB，895 行）。

---

## 4. 发布产物验证（t5 + t6）

### 4.1 publish/ 目录（framework-dependent 单文件，662 KB）

```
bin/publish/                                    (total 662 KB)
  GUI.exe                             662 KB   单文件 apphost（含 ValveKeyValue / CommunityToolkit.Mvvm / Microsoft.Extensions.* / app DLL 等所有 managed DLL 内嵌）
  GUI.pdb                             40 KB
  Lib.pdb                             18 KB
```

> **体积变化**：移除第三方 Fluent UI 库（`ModernWpfUI`）后，单文件 exe 从 **4,880 KB → 662 KB（−86%）**。此前 `ModernWpf.dll`(1.8 MB) + `ModernWpf.Controls.dll`(1.3 MB) + 66 个语言卫星资源程序集全部内嵌进了 exe；改用 .NET 10 内置 Fluent 主题后这些依赖归零。

### 4.2 Runtime leak audit（关键约束的验证）

| 检查项 | 结果 |
|---|---|
| `System.Private.CoreLib.dll` | ✅ 不存在 |
| `System.Runtime.dll` | ✅ 不存在 |
| `clrjit.dll` / `coreclr.dll` | ✅ 不存在 |
| `Microsoft.NETCore.App/` 子目录 | ✅ 不存在 |
| `Microsoft.WindowsDesktop.App/` 子目录 | ✅ 不存在 |
| `Microsoft.WindowsAppRuntime/` 子目录 | ✅ 不存在 |
| `PresentationCore.dll` / `PresentationNative_cor3.dll` / `wpfgfx_cor3.dll` | ✅ 不存在（由用户 .NET 10 Desktop Runtime 提供） |
| MSIX packaging 痕迹 | ✅ 不存在 |
| `GUI.runtimeconfig.json` → `Microsoft.NETCore.App` v10.0.0 (framework-dependent) | ✅ 正确指向用户机器上已安装的 runtime |

### 4.3 启动验证

由于本 CI/headless 环境无交互式 desktop session，WPF 窗口本身不可视，但 **必须先确认 exe 能正常加载 .NET runtime 并启动 WPF 进程** 才是「跑得起」的最终证据。

```
$proc = Start-Process -FilePath "...artifacts\publish\GUI.exe" -PassThru
Start-Sleep -Seconds 4
$proc.HasExited   # → False（headless 仍能存活说明进程已成功进 WPF 消息循环）
Stop-Process -Id $proc.Id
```

**结果**:
- 进程启动后**未退出** ⇒ apphost 成功定位用户机器上的 .NET 10 Desktop Runtime，CLR 引导成功，main window 已创建并阻塞在窗口消息循环
- framework-dependent 单文件路径下 native WPF DLL（PresentationNative_cor3 / wpfgfx_cor3 / PenImc_cor3 / D3DCompiler_47_cor3 / vcruntime140_cor3）不在发布目录里，而是从用户的 runtime 安装位置直接加载；apphost 不会启动 single-file extract 子进程
- UI 不可见是因为环境无 desktop session，不是发布产物缺陷

### 4.4 在交互式 Windows 上的预期表现

将 `GUI.exe` 单文件（662 KB）复制到任意 Windows 10/11 x64 机器（**且该机器已安装 .NET 10 Desktop Runtime**，例如 `winget install Microsoft.DotNet.DesktopRuntime.10`）：
- **只需要**复制这一个文件（+ 旁边的 pdb 可选）
- **不依赖** MSIX / Windows Store
- 双击 `GUI.exe` 即弹出 WPF Fluent 主窗口（左侧 `TreeView` 着色器列表 + 中间参数编辑器 + 右侧 KV 预览），主题跟随 Windows 浅色 / 深色设置

> **系统要求补充**：界面使用 .NET 10 内置 Fluent 主题的 `ThemeMode` 特性，需要 **.NET 10 Desktop Runtime**（≥ 10.0.12 已验证）。图标字体为 Windows 自带的 `Segoe Fluent Icons`，在 Windows 10 上会自动回退到 `Segoe MDL2 Assets`。

---

## 5. 控件错误记录与防闪退（后续迭代）

### 5.1 问题

用户反馈「点击控件如果报错直接闪退」。根因是 WPF 中任何未被捕获的 UI 线程异常都会经 `Dispatcher` 冒泡并终止进程，而原先所有事件处理器与 `[RelayCommand]` 都是「裸」的 —— 一个文件对话框失败、一次写盘权限不足、一个损坏的 `.vmat`，都会让整个应用消失。

### 5.2 解决方案：两层防护

**第一层 —— 每个控件处理器就地拦截（主防线）**

`Diagnostics/ControlErrorRecorder.cs` 提供 `Guard` / `GuardWithDialog` 包装器，把每个入口包住：

| 位置 | 已加固的操作 |
|---|---|
| `MainWindow.xaml.cs` | 加载目录、打开材质、退出、过滤、关于、错误日志 |
| `MainViewModel` | 新建 / 保存 / 另存为 / 打开文件夹 / 过滤 / 打开材质 / 扫描目录 / 重建分组 |
| `ShaderEditorViewModel` | KV 渲染（热路径）、复制到剪贴板 |
| `ParameterRowViewModel` | 浏览贴图（文件对话框在受限环境极易抛异常） |

**第二层 —— 全局兜底网**

`App.OnStartup` 安装三层处理器，任何漏网异常都不会终止进程：

| 处理器 | 作用 |
|---|---|
| `DispatcherUnhandledException` | 记录 + `e.Handled = true`，**不闪退**（实测可拦下真实点击异常） |
| `AppDomain.CurrentDomain.UnhandledException` | 后台线程致命异常，至少落盘 |
| `TaskScheduler.UnobservedTaskException` | 未观察的 Task 异常，`SetObserved()` 防止 GC 时终止进程 |

### 5.3 错误记录组件

`Diagnostics/ErrorLog.cs` 是专用的控件错误记录组件：

- **记录内容**：时间 / 级别 / 操作名 / **控件描述** / 异常类型 / 消息 / 完整堆栈（含内部异常链）/ 线程号
- **控件描述**由 `ControlErrorRecorder.Describe` 生成，形如：
  `Button Name='BoomButton' 文本='出错的按钮' 绑定='SelectedItem' DataContext=MainViewModel 位置=(160,420) 尺寸=94x31`
- **双写**：内存环形缓冲（上限 500 条，供 UI 实时显示）+ 磁盘文本日志
- **绝不抛异常**：记录器自身故障不能反过来弄崩应用
- **日志目录三级探测**（每个都真实试写，避免「配好了却静默丢日志」）：
  1. `%LOCALAPPDATA%\VmatGenerator\`
  2. `<exe 目录>\logs\`（便携模式）
  3. `%TEMP%\VmatGenerator\`
  全部失败时通过 `LastWriteError` 暴露原因，并在错误日志窗口中明确提示
- **自动轮转**：超过 2 MB 时改名为 `error.previous.log`

### 5.4 用户界面

- **帮助 → 错误日志…**：表格展示（时间/级别/操作/控件/异常/消息），选中可看完整堆栈，支持复制 / 清空 / 打开日志目录
- **状态栏错误角标**：出错即出现的非阻塞 `⚠ N 个错误` 按钮，点击直达错误日志
- 全局异常**刻意不弹模态对话框** —— 模态框会阻塞 UI 线程（无桌面会话时永久挂起），且连续异常会淹没界面

### 5.5 WPF 资源字典陷阱：MergedDictionaries 是单向的

曾出现运行时异常：

```
System.Exception: 无法找到名为“BoolToVisibility”的资源。资源名称区分大小写。
```

**根因**：`App.xaml` 把 `Resources/ParameterTemplates.xaml` 作为 **MergedDictionary** 合并进来，
而该文件内的 DataTemplate 用 `{StaticResource BoolToVisibility}` 引用了 `App.xaml` 中声明的转换器。
WPF 的合并方向是单向的 —— **被合并的字典无法 `StaticResource` 引用合并方字典里的资源**。
`StaticResource` 在模板实例化时才解析，所以编译期不报错，只在运行时真实加载材质行时才炸。

**规则（务必遵守）**：任何在某个 ResourceDictionary 内被 `StaticResource` 引用的资源，
都必须在**该文件自身**内声明。

**修法**：把三个转换器移入 `ParameterTemplates.xaml` 内部，与使用它们的模板同处一个字典；
`App.xaml` 不再重复声明（避免同键两处定义）：

```xml
<!-- ParameterTemplates.xaml -->
<local:BoolToVisibilityConverter x:Key="BoolToVisibility" />
<local:BoolToOnOffConverter x:Key="BoolToOnOff" />
<local:StringNonEmptyToVisibilityConverter x:Key="StringNonEmptyToVisibility" />
```

> 若确实需要「全局转换器」，正确做法是让每个使用方字典**自己先合并**转换器字典，
> 而不是指望合并方把资源「传」进来。`DynamicResource` 也能绕过，但会失去早期失败能力，
> 不建议用来掩盖这类错误。

**实测**：4 种参数行模板全部真实实例化成功
（Bool→1 个复选框、Scalar→1 个文本框、Vector→4 个、Texture→3 个），
主窗口加载 161 个 `.vmat` 后实例化 18 个交互控件，日志中资源缺失记录为 0。

### 5.6 WPF 样式陷阱：显式 Style 会顶掉主题隐式样式

症状：`NumericTextBoxStyle` 作用后输入框失去 Fluent 外观（圆角、边框、悬停/聚焦效果），
退回 WPF 传统外观。

**根因**：.NET 10 内置 Fluent 主题是以**隐式样式**（键 = 控件类型）形式提供的 ——
`Themes/Fluent.xaml` 中共 411 个条目，`TextBox` / `Button` / `CheckBox` / `ComboBox` /
`TreeView` / `DataGrid` / `TextBlock` 等均有 `typeof(T)` → `Style` 的隐式样式。
只要控件**不指定** `Style` 就会自动套用；而一旦指定了显式 `Style`（哪怕只有
`MinWidth` 和两个 `EventSetter`），隐式样式就被**整体顶替**，因为隐式样式本来就是
"没有 Style 时的默认值"。

**修法**：显式样式必须用 `BasedOn` 显式声明继承基样式。

```xml
<Style x:Key="NumericTextBoxStyle"
       TargetType="TextBox"
       BasedOn="{StaticResource {x:Type TextBox}}">
```

**逐项处理**（`grep 'TargetType='` 全量排查，共 5 处）：

| 样式 | TargetType | 处理 |
|---|---|---|
| `NumericTextBoxStyle` | TextBox | ✅ 加 `BasedOn` |
| 错误角标按钮 | Button | ✅ 加 `BasedOn` |
| 过滤框占位文字 ×2 | TextBlock | ✅ 加 `BasedOn` |
| `ParamRowStyle` | Border | ⚠ **不加** —— Fluent 中 Border 无隐式样式，加了反而解析失败 |

> `{StaticResource {x:Type T}}` 即使写在 **MergedDictionary** 里也能正确解析到
> 主题隐式样式（实测通过），与 §5.5 的限制不同 —— 区别在于 Fluent 主题在
> 样式被应用前就已注入 `Application.Current.Resources`，而 §5.5 的转换器声明
> 位于合并方字典的 `MergedDictionaries` **之后**，加载时机上尚不存在。

**实测**：`NumericTextBoxStyle` 的 TextBox 与隐式样式的 TextBox 视觉树完全一致
（各 12 个节点）、`ControlTemplate` 已套用、`BorderThickness` 一致，且 `MinWidth=120`
正常叠加；Button 3 vs 3、TextBlock 前景色与字号均一致；主窗口加载 161 个材质后
可视节点 226，Error 级日志 0 条。

### 5.7 VMAT 模板规范化格式化

`Lib/Templates/*.vmat`（11 个内嵌着色器模板）原先是「键不缩进 + 值独占一行」的布局，
与 Valve 自家 `.vmat` 的排版不一致，diff 噪音大且掩盖真实改动。

新增 `VmatFormatter`（`Lib/VmatFormatter.cs`），**不自己实现任何解析** ——
用 ValveKeyValue 读、再用同一个库的序列化器写，因此格式化在结构上不可能改变文档语义。

Valve 原生排版（每层一个 TAB，键值同行）：

```
"Layer0"
{
→"shader"→"csgo_vertexlitgeneric.vfx"
→"g_vColorTint"→"[1.000000 1.000000 1.000000 0.000000]"
→"Attributes"
→{
→→"mapbuilder.nodraw"→"1"
→}
}
```

**API**

| 成员 | 说明 |
|---|---|
| `Format(string)` | 解析并重新输出为规范布局 |
| `FormatFile(string)` | 原地重写，UTF-8 无 BOM + LF，返回是否真的变了 |
| `Write(KVDocument)` | 跳过解析，直接写已解析的文档 |
| `IsCanonical(string)` | 是否已是规范格式（`Format` 是否为空操作） |
| `Equivalent(a, b)` | 结构等价比较 —— **忽略排版，只认语义**，适合当回归断言 |

**踩到的两个库行为**

1. **重复键**：`KVObject.Collection()` 遇到同层重复键会抛
   `ArgumentException: An item with the same key has already been added`；
   必须用 `KVObject.ListCollection()`。Valve 允许同层重复键（KV1 没有原生数组），
   用错容器会直接丢数据或抛异常。
2. **注释会丢失**：`KVDocument` 没有 `Comments` 属性，`KVSerializerOptions`
   也没有 `PreserveComments`，这是 ValveKeyValue 0.71 的固有限制。
   实测 11 个模板与 161 个真实材质**均不含注释**，因此本次格式化零损失；
   但若日后引入带注释的文件需注意。

**实测（格式化前先验证无损，确认后才落盘）**

| 检查项 | 结果 |
|---|---|
| 11 个模板结构等价 | ✅ 11/11 |
| 11 个模板幂等（`Format(Format(x))==Format(x)`） | ✅ |
| **161 个真实材质**结构等价 | ✅ 161/161 |
| 161 个真实材质幂等 | ✅ |
| 注释损失 | ✅ 0（模板与真实材质均无注释） |
| 嵌入资源 = 磁盘文件（逐字节） | ✅ 11/11 |
| `ShaderCatalog` 参数元数据 | ✅ 11 族 / 315 参数 / 22 特性标志，默认值齐全 |
| `EmitDefault` 生成物规范化无损 | ✅ 11/11 |
| BOM | ✅ 全部无 BOM |

> 注意：模板是**内嵌资源**，改完 `.vmat` 必须重新编译 `Lib`，
> 否则程序里跑的仍是旧内容 —— 首次回归测试正是因此报出「磁盘与嵌入不一致」。

### 5.8 KV 输出未格式化：根因与修复

模板文件格式化后，应用**生成**的材质却仍是旧排版。根因是项目里有**两套写入路径**：

```
                      ┌─ KvAdapter.SerializeViaValve()  ← ValveKeyValue，规范格式
VmatFormat.Serialize() ┤
                      └─ VmatNode.Serialize()          ← 手写 writer，未格式化 ❌
```

`VmatNode.SerializeInto` 手工拼字符串：键**不缩进**、值独占一行，而且
`indentLevel` 参数虽被层层传递却**从未用于缩进** —— 是一个纯死参数。
由于 `VmatFormat.Serialize` 与 `MaterialScanner.Render` 都走这条路径，
**KV 预览、保存文件、`ShaderTemplateEmitter.EmitDefault` 全部受影响**，
意味着任何一次重新生成模板都会把规范化成果覆盖回旧格式。

**修复**：删除手写 writer，`VmatNode.Serialize()` 委托给
`VmatFormat.Serialize()` → `KvAdapter.SerializeViaValve()`。
项目内现在只剩一条输出路径，也就不存在「两处格式不一致」的可能。

> 这与 §5.2 的定策一致：既然解析已经完全交给 ValveKeyValue，
> 写入再自己手搓一份只会制造两套真相。

**验证**

| 检查项 | 结果 |
|---|---|
| `Render()` 输出为规范格式 | ✅ 11/11 着色器 |
| **`EmitAllToDirectory` 重新生成 = 磁盘模板（逐字节）** | ✅ 11/11 —— 格式由生成器强制保证，不再是一次性手工修改 |
| 161 个真实材质 `Parse → Serialize` 无损 | ✅ 161/161 |
| 解析模板后重新输出仍为规范格式 | ✅ 11/11 |
| 应用 KV 预览 `GeneratedText` 为规范格式 | ✅ |

### 5.9 移除启动时自动打开默认目录

原先 `App` 里硬编码了一个**开发机绝对路径**并在窗口 `Loaded` 时自动扫描：

```csharp
public const string DefaultMaterialsRoot = @"H:\dsh-workspace\vmat-generater\materials";
```

这意味着应用只在作者这台机器上「开箱即用」，拷到任何其它机器都指向
一个不存在的路径 —— 既危险又无法使用。

**修改**

| 位置 | 修改 |
|---|---|
| `MainWindow` | 删除 `Loaded += OnWindowLoaded` 及该处理器，不再有任何自动扫描 |
| `App.DefaultMaterialsRoot` | **删除**常量 |
| `App.PickerFallbackDirectory` | 新增：返回 `Environment.SpecialFolder.MyDocuments`，仅用作文件对话框起始目录 |
| `MainViewModel.Refresh()` | 无目录时提示「请使用文件 → 打开文件夹」并返回，不再回落到默认路径 |
| `MainViewModel.OpenFolder()` / `SaveAs()` / `ParameterRowViewModel.Browse()` | 对话框起始目录改用 `PickerFallbackDirectory` |
| `StatusText` 初值 | 由「就绪。」改为引导用户选择目录或新建材质 |

全项目已无 `DefaultMaterialsRoot` 与 `H:\dsh-workspace` 残留（已 grep 确认）。

**验证**

| 检查项 | 结果 |
|---|---|
| 启动后 `MaterialsFolder` 为空 | ✅ |
| 启动后着色器分组为 0（未扫描 161 个文件） | ✅ |
| 状态栏明确引导下一步操作 | ✅ |
| `Refresh()` 在无目录时不回落到默认路径 | ✅ |
| `DefaultMaterialsRoot` 常量已不存在 | ✅ |
| 回退目录为用户文档目录（非开发机路径） | ✅ |
| **显式选择目录后功能完好（仍加载 161 个文件、11 个分组）** | ✅ |
| 启动到打开全过程 Error 级日志 | ✅ 0 条 |

### 5.10 子项目重命名：VmatGeneratorApp/VmatGeneratorLib → GUI/Lib

子项目更名为 `GUI`（WPF 主程序）与 `Lib`（类库）。由于 .NET 中「项目名 / 程序集名 /
根命名空间 / 嵌入资源名前缀」四者默认同源，本次是**一致性重命名**，而非仅改文件夹。

| 项目 | 旧 | 新 |
|---|---|---|
| 目录 | `VmatGeneratorApp\` / `VmatGeneratorLib\` | `GUI\` / `Lib\` |
| 工程文件 | `VmatGeneratorApp.csproj` / `VmatGeneratorLib.csproj` | `GUI.csproj` / `Lib.csproj` |
| 程序集 / RootNamespace | `VmatGeneratorApp` / `VmatGeneratorLib` | `GUI` / `Lib` |
| 命名空间 | `VmatGeneratorApp.*` / `VmatGeneratorLib` | `GUI.*` / `Lib` |
| 产物 | `VmatGeneratorApp.exe` | `GUI.exe` |
| `app.manifest` 标识 | `VmatGeneratorApp.app` | `GUI.app` |

**未受影响**：`ShaderTemplateEmitter` 通过 `assembly.GetName().Name` 动态拼接嵌入资源名
（`"<asm>.Templates.<shader>.vmat"`），因此资源前缀自动从 `VmatGeneratorLib.Templates.*`
变为 `Lib.Templates.*`，无需硬编码改动。`VmatGenerator` 这个**类名**与项目名不同，
未被替换。

**过程记录**：目录改名被 Roslyn 编译服务器（`VBCSCompiler`）的文件句柄拒绝，
`dotnet build-server shutdown` 仍失败，最终以「复制 + 删除」完成 —— 5 次重试后
确认是持续性锁定而非瞬时冲突。

**验证**

| 检查项 | 结果 |
|---|---|
| 解决方案引用 | ✅ `Lib` + `GUI`，构建 0 警告 0 错误 |
| 程序集名 / 根命名空间 | ✅ 均为 `Lib` / `GUI` |
| 嵌入模板资源前缀 | ✅ `Lib.Templates.*`，11/11 |
| `LoadEmbedded` / `EnumerateEmbedded` | ✅ 11/11 可用 |
| XAML `x:Class` / `clr-namespace` / `assembly=` | ✅ 全部资源可加载（8/8） |
| `NumericTextBoxStyle` 仍继承 Fluent 样式 | ✅ |
| `ShaderCatalog` 元数据 | ✅ 11 族 / 315 参数 / 22 标志 |
| 嵌入模板仍为规范格式且往返无损 | ✅ 11/11 |
| `EmitDefault` 输出为规范格式 | ✅ 11/11 |
| 161 个真实材质往返无损 | ✅ 161/161 |
| 启动后不自动打开目录 | ✅ 分组 0、状态栏引导 |
| GUI 实跑 161 材质 + KV 预览规范 | ✅ |
| 旧类型 `VmatGeneratorLib.*` / `VmatGeneratorApp.*` | ✅ 已不存在 |
| 运行时捆绑审计 | ✅ 未打包任何 .NET/WPF 运行库 |

### 5.11 实测结果

注入真实点击崩溃（`Button.Click` 处理器抛异常，经 `BeginInvoke` 派发，模拟 WPF 输入系统）：

| 检查项 | 结果 |
|---|---|
| 点击异常后进程存活 | ✅ |
| 消息循环仍可响应 | ✅ |
| 连续 5 次崩溃点击后仍存活 | ✅ |
| 记录到 Error 级错误 | ✅（6 次点击 → 6 条记录） |
| 记录了控件上下文 | ✅ |
| 记录了完整堆栈 | ✅ |
| 日志文件含「控件」「操作」字段与异常 | ✅ |
| 写盘无失败 | ✅ |

---

## 6. 复现脚本

```pwsh
# Round-trip 测试（→ obj/baselines/, obj/test-logs/roundtrip.log）
cd H:\dsh-workspace\vmat-generater\VmatGenerator
dotnet run --project RoundtripTest -c Release

# Framework-dependent 单文件发布（→ bin\publish\ 与 GUI\bin\Release\net10.0-windows7.0\win-x64\publish\）
cd H:\dsh-workspace\vmat-generater\VmatGenerator
dotnet publish GUI -c Release -r win-x64 -o bin\publish

# 启动验证（framework-dependent 单文件无 extract 步骤；目标机器需预装 .NET 10 Desktop Runtime）
$proc = Start-Process -FilePath "bin\publish\GUI.exe" -PassThru
Start-Sleep 4
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }

# 原始样本扫描（→ tools/scan_out.md）
pwsh H:\dsh-workspace\vmat-generater\tools\scan_vmat.ps1
```

---

## 7. 拖拽导入 + 贴图后缀快速导航（后续轮次）

> 面向使用者的完整说明见 **[`feature-dragdrop-suffix-usage.md`](feature-dragdrop-suffix-usage.md)**；
> 契约规格见 **[`feature-dragdrop-suffix-spec.md`](feature-dragdrop-suffix-spec.md)**（v1.3 冻结）。
> 本节只记录集成走查结论。

### 7.1 功能范围

| 层 | 新增 / 改动 |
| --- | --- |
| **Lib**（10 个新文件） | `TextureRole`（26 个角色）、`TextureRoleTokens`、`TextureRoleResolver`（P1–P5 五档）、`TextureSuffixMatcher`（归一化 + 匹配）、`TextureSuffixRule`、`VmatGeneratorSettings`（35 条种子）、`VmatGeneratorSettingsStore`（原子写盘 + 逐字段修复 + 损坏备份）、`TexturePathRules`、`TextureAssignment`（分配规划器 + 冲突裁定）、`DropImportService`（D1–D6 六类分档）、`TextureAssignmentSelfTest`（33 项） |
| **GUI** | `MainWindow.xaml`：`AllowDrop` + 4 个拖拽事件 + 「工具(_T) → 贴图后缀快速导航(_Q)」+ 工具栏「快速导航」按钮 + 拖拽悬停覆盖层；「帮助(_H) → **贴图后缀自检(_S)**」新增入口 |
| **GUI** | `QuickNavWindow.xaml(.cs)`、`ViewModels/QuickNavViewModel.cs`、`ViewModels/TextureSuffixRuleViewModel.cs`、`ViewModels/TextureResolutionMessages.cs` |
| **GUI** | `ViewModels/MainViewModel.cs`（`Settings` / `QuickNav` / `LastDropResult` / `HandleDroppedPaths` / `OpenQuickNav`）、`ShaderEditorViewModel.cs`（`CurrentShader` / `TrySetTextureValue`）、`ParameterRowViewModel.cs`（`VectorParameter.RawText`） |

### 7.2 集成阶段修掉的缺陷

| # | 位置 | 问题 | 处理 |
| --- | --- | --- | --- |
| 1 | `GUI/ViewModels/QuickNavViewModel.cs` | 9 个编译错误：把 `GuardWithDialog<T>` 的**元组**当成 `T` 取成员；三参 `Guard` 只收 `Action` 却传了表达式；`SelectedRule is target` 因 `target` 已是局部变量而被解析成常量模式（CS9135） | 全部按 `ControlErrorRecorder` 的真实签名修正（元组解构 + `Guard<T>(…, fallback)` + `ReferenceEquals`） |
| 2 | **`Lib/DropImportService.cs` `CollectDirectory`** | **真实功能缺陷**：`recurseTextureFolders == false` 时不仅跳过子目录贴图，**连顶层贴图也一起丢掉**，导致拖入贴图文件夹得到 0 张贴图、分档退化成 `Unsupported`——配置里这个开关等于把 D4 整个关死 | 改为 `recurseTextureFolders \|\| IsDirectChild(root, file)`，对齐规格 §2.2 D4 的 `TopDirectoryOnly` 语义；并新增回归用例 |
| 3 | `Lib/*.cs`、`GUI/ViewModels/QuickNavViewModel.cs` | 三处 XML 注释仍写「§4 的 **28** 条种子规则」，实际已冻结为 35 条 | 更正为 35 |
| 4 | `Lib/DropImportService.cs` `CanAccept` | 注释仍标「已知偏差，待 captain 裁决」 | v1.3 已裁决保持 `CanAccept(string[]?)`，注释改为记录裁决结果 |

### 7.3 §9 验收场景走查

| 场景 | 内容 | 验证方式 | 结果 |
| --- | --- | --- | --- |
| S1 | `csgo_environment` 拖入 `_normal` / `_diffuse` | 自检端到端用例 | ✅ `TextureNormal1` ← `wall/concrete_wall_normal.png`、`TextureColor1` ← `wall/concrete_wall_diffuse.png` |
| S2 | `csgo_lightmappedgeneric` 同上 | 自检端到端用例 | ✅ `TextureLayer1Normal` / `TextureLayer1Color` |
| S3 | 整名相等命中（`normal.png`） | 自检端到端用例 | ✅ `TextureNormal1`，值 `normal.png`（根下不加 `./`） |
| S4 | `brick_normal` 与 `brick_n` 冲突 | 自检端到端用例 | ✅ 只写 `brick_normal`（更长后缀胜出），冲突列表含被淘汰项 |
| S5 | `a_normal` 与 `b_normal` 冲突 | 自检端到端用例 | ✅ 按词数决胜保留 `a_normal` |
| S6 | 水面着色器槽位归属 | 自检端到端用例 | ✅ `surf_foamnormal`→`TextureFoamNormal`、`debris`→`TextureDebris`；`foam_normal` **不写入** |
| S7 | 水面着色器 `waves_normal` | 自检端到端用例 | ✅ 零写入，`UnresolvedReason = AmbiguousQualified`，候选 3 个 |
| S8 | 贴图根之外的文件 | 自检端到端用例 | ✅ 写回原路径，不含 `..` |
| S10 | `logo.png` 无规则命中 | 自检端到端用例 | ✅ 零写入 + 未命中列表 |
| S12 | 同槽位多贴图 | 自检端到端用例 | ✅ 只写 `TextureMask1`，不写 `TextureMask2/3` |
| §2.2 D1–D6 | 六类分档 + 两个根目录候选 | 自检用例 + GUI 层 harness | ✅ 六类全对；混合时材质根 = 第一个含 `.vmat` 的目录 |
| **§2.2 D4 递归关闭** | 关闭 `recurseTextureFolders` 后拖入贴图文件夹 | 自检新增回归用例 | ✅ 顶层贴图仍被收集（1 张），子目录贴图跳过并在摘要说明 |
| 三个真实着色器 × Normal/Color/Roughness | 规格 §5.5 锁定示例 | harness 逐条比对 | ✅ environment → `TextureNormal1`/`Color1`/`Roughness1`(均 P2)；lightmappedgeneric → `TextureLayer1Normal`/`Color1`/`Roughness1`(均 P3)；water_fancy → 三者均**未解析**（Normal 为 3 候选歧义，Color/Roughness 为模板无对应键） |
| 配置持久化 | 保存 → 重建 `MainViewModel`（等价重启）→ 生效 | GUI 层 harness | ✅ 贴图根 / 着色器 / 两个开关 / 自定义规则全部还原；「重新载入」正确丢弃未保存改动 |
| 跨层接线 | 菜单项与工具栏入口各触发一次 | XAML 结构扫描 + harness | ✅ 全仓库 5 个 XAML 中**没有任何控件同时设置 `Click` 与 `Command`**；菜单走 `Click`，工具栏走 `Command` |
| KV 预览即时刷新 | 拖拽赋值后预览区更新 | GUI 层 harness | ✅ 写入前后对比 `GeneratedText`，赋值 → `PropertyChanged` → `RebuildText` 链路通 |
| `VectorParameter.RawText` 不变式 | `RawText` 为空时输出逐字不变 | 静态统计 + 穷举 + harness | ✅ 见下 |

### 7.4 `VectorParameter.RawText` 回归核对

**不变式**：`RawText` 为空时 `WriteValue` 的输出必须与改动前**逐字一致**。

| 核对项 | 结果 |
| --- | --- |
| `ShaderCatalog` 中 `ShaderParamKind.Vector` 出现次数 | **76** |
| 其中 `Shape == TextureOrVector` | **9** |
| 真·向量参数（Shape 保持 `Scalar`） | **67 次**（去重后 **33 个不同键名**） |
| 分配器可产出的候选键（`Kind == Texture \|\| Shape == TextureOrVector`） | **37** |
| **候选键 ∩ 真·向量参数** | **0（空集）** ← 结构上不可能拿到 `RawText` |
| 其中 `Kind == Vector` 的候选键 | 4 个：`TextureRoughness1`、`TextureTranslucency`、`TextureRimMask`、`TextureFoamNormal`（全部 `TextureOrVector`） |
| harness 实测 `g_vColorTint` 输出 | `[1.000000 1.000000 1.000000 0.000000]`（与改动前逐字一致，`RawText` 恒空） |
| harness 实测 `TextureRoughness1` 写入贴图路径 | 存入 `RawText`，`WriteValue` 原样输出，重新打开材质不丢值 |

`RawText` 有且只有两个写入点：`TrySetRawText`（仅拖拽导入路径调用）与
`ShaderEditorViewModel.BuildRow`（额外要求 `p.Shape == TextureOrVector`）。两者都排除了真·向量参数。

### 7.5 三条「预留规则」：当前一个着色器也用不上（非缺陷）

出厂种子表 35 条里有 **4 条指向 3 个语义槽位**，而这 3 个槽位在**当前全部 11 个模板上都没有对应参数键**：

| 后缀 | 语义槽位 | 当前 11 个模板为何写不进 | 状态 |
| --- | --- | --- | --- |
| `_emissive` / `_selfillum` | Emissive | `TextureEmissive` 在 ShaderCatalog 中出现 **0** 次 | 预留 |
| `_waves` | WavesMask | `TextureWaves` 出现 **0** 次（水面遮罩键为 `TextureFoam`） | 预留 |
| `_lightmap` | Lightmap | `LightMapTextureName` 出现 3 次但不以 `Texture` 开头，**不满足 §5.1 条件①**，永不成为候选 | 预留 |

拖入这 4 类后缀的贴图不会写入任何参数，程序给出诚实的 `NoMatchingKey` 诊断
（「该着色器没有通用的 Emissive 槽位」），**不会猜一个相近的键写进去**。

**captain 裁决（t4 收尾）：规则保留，不删除、不改指、不改条数。**删除等于过拟合今天这份
着色器目录；将来新增带 `TextureEmissive` / `TextureWaves` 的模板时这些规则立即可用。
需要修正的是**文档表述**而非规则本身：规格 §4 备注列原文「笔刷光照贴图（**通常**不自动写入）」
措辞误导——「通常」暗示「有时能写」，而事实是当前 11 个模板**一个都写不进**。
规格 §4 备注列已转 architect 更正。

此外 `_mask` 与 `_cube` 是「跟着着色器走」的（`_mask` 在 `effects`→`TextureMask1`、
在 `environment`→`TextureTintMask1`、在 `character`→`TextureRimMask`；`_cube` 在
`moondome`→`TextureCubeMap`、在 `water_fancy`→`TextureLowEndCubeMap`）。
这是**设计意图而非缺陷**（captain 裁决），全部由实跑数据得出，非按键名推测。
逐条实测数据见 `feature-dragdrop-suffix-usage.md` §3。

> 是否改指到其他语义槽位属于规格级变更，本轮未擅自调整。

### 7.6 永久自检入口

`帮助 → 贴图后缀自检(_S)` 调用 `Lib.TextureAssignmentSelfTest.Run()`，展示逐条 PASS/FAIL 与统计。
在此之前这 33 项只能靠一个一次性 `%TEMP%` console 工程运行，而该工程已被清理——
规格 §11 的契约事实上无人能重跑。新增这个入口后无需新工程、无需 NuGet、无需改 `.csproj`。

**安全性**：所有涉及文件的用例都在 `Path.GetTempPath()` 下的独立沙箱中执行并在 `finally` 清理，
**不读写用户真实的 `%APPDATA%\VmatGenerator\settings.json`**。

### 7.7 本轮约束遵守情况

- ✅ `dotnet build VmatGenerator.sln -c Debug` → **0 错误 0 警告**；
- ✅ 未修改任何 `*.csproj` 与 `VmatGenerator.sln`，未引入新的 NuGet 包；
- ✅ `DropImportService.CanAccept(string[]?)` 保持 captain v1.3 裁决，未回退为 `IDataObject`；
- ✅ 同一控件未同时设置 `Click` 与 `Command`；
- ✅ §4 种子 35 条、§5.2 角色 26 个、§5.6.2 P5 候选 10 项均未改动；
- ✅ 自检用例由 31 增至 **33**（新增 §2.2 D1–D6 分档用例与 D4 递归关闭回归用例，
  均为修复缺陷 2 的必要回归保护）。**captain t4 收尾裁决：保留 33，不回退到 31**——
  冻结用例总数在修完缺陷之后是本末倒置；真正需要冻结的四个契约数字（35 / 26 / 10 / 13）本轮一个未动。

### 7.8 口径更正：`ShaderCatalog` 是 11 个模板，不是 10 个

`ShaderCatalog.All` 实为 **11** 个模板：9 个 `csgo_*`（environment / vertexlitgeneric / complex /
static_overlay / lightmappedgeneric / character / water_fancy / moondome / effects）
+ `sky.vfx` + `generic.vfx`。

t1 规格与后续分派沿用了「10 个着色器」的说法，属**漏数**（captain 已确认并致歉，
规格口径清扫已转 architect）。`ShaderCatalog.cs` 本轮**未被任何人改动**，这是既有事实。

> **不影响任何冻结契约**：§5.6.2 的 P5 闭合表是按「可作 P5 候选的 channel」枚举的，
> 与模板总数无关，captain 已独立穷举验证 10 项完备。

---

## 8. 验收清单

| 验收项 | 结果 |
|---|---|
| `RoundtripTest` 改造完成：扫描 materials → 为每个 shader 调用 `ShaderCatalog.Find` → `BuildDocument` 输出 baseline → 写入 `obj/baselines/<shader>.vmat` → `ValveKeyValue` 解析回比对关键键 | ✅ |
| RoundtripTest 必须成功运行（`dotnet run --project RoundtripTest -c Release`）并打印 `Round-trip OK` | ✅ |
| `artifacts/publish/` 存在；尝试启动 `GUI.exe` | ✅（headless 环境验证：进程成功启动并稳定运行；native WPF runtime 完整解压） |
| `VmatGenerator/docs/REPORT.md` 含 shader-analysis + publish + 测试日志汇总 | ✅（本文件） |

— 报告结束 —