# 拖拽导入 + 贴图后缀快速导航 —— 设计规格（唯一权威契约）

> 适用范围：`Lib`（算法与配置）与 `GUI`（交互与绑定）两条**并行**实现线
> 规格版本：v1.6（v1.0 首版 → v1.1 P5 多候选降级 → v1.2 闭合诊断不变量 → v1.3 对齐 t3 实做 → v1.4 事实性口径更正 → v1.5 对齐 t5 审查 R8/R9 → v1.6 统一 §5.6 `{槽位}` 口径（R12），见 §12 修订记录）
> 依据源码（编写时已逐字核对）：
> - `Lib/ShaderCatalog.cs`（**11** 个 `ShaderTemplate`，纹理键命名不统一）
> - `Lib/ShaderTemplate.cs`（`ShaderParamTemplate.Key / Kind / Shape`）
> - `Lib/MaterialScanner.cs`（`MaterialScanner.Scan` / `VmatGenerator.BuildDocument`）
> - `GUI/MainWindow.xaml`（**无** `AllowDrop` / `DragOver` / `Drop`，拖拽从零新增）
> - `GUI/MainWindow.xaml.cs`（现有 `Click` 处理器命名风格）
> - `GUI/ViewModels/MainViewModel.cs`（`LoadFolder` / `EditMaterial` / `StatusText`）
> - `GUI/ViewModels/ShaderEditorViewModel.cs`（`ParameterRows`、`LoadFromTemplate`、`RebuildText`）
> - `GUI/ViewModels/ParameterRowViewModel.cs`（`TextureParameter.Value` 可公开读写）
> - `docs/shader-analysis.md`、`docs/REPORT.md`（行文与表格风格）

> **契约效力**：本文档第 3 / 4 / 5 / 7 / 8 章为**逐字锁定**内容。lib-dev 与 ui-dev 必须逐字实现，
> 任何偏离都视为缺陷。凡本文未写明的细节，实现方**不得**自行发挥，须先回到 captain 确认。

---

## 0. 并行实现速查（TL;DR）

| 契约面 | 归属 | 落点 |
|---|---|---|
| 后缀→角色匹配、角色→参数键解析、路径换算、配置读写 | **Lib**（`namespace Lib`） | `Lib/TextureRole.cs` 等 8 个新文件 |
| 拖拽事件接线、快速导航窗口、规则表编辑 | **GUI**（`namespace GUI` / `GUI.ViewModels`） | `GUI/QuickNavWindow.xaml` 等 3 个新文件 + 3 个修改文件 |
| 配置存储位置 | 两者共享 | `%APPDATA%\VmatGenerator\settings.json` |

数据流（唯一主链路）：

```
拖拽 / 快速导航
   ↓  DropImportService.Analyze(paths)      → DropAnalysis { 类别, vmatFiles, textureFiles, otherFiles }
   ↓  (材质根目录变更时) MainViewModel.LoadFolder(root)
   ↓  TextureAssigner.Assign(shader, textureFiles, rules, textureRoot)
   ↓      TextureSuffixMatcher.Match(file)   → TextureSuffixMatch { role, matchedSuffix }
   ↓      TextureRoleResolver.Resolve(shader, role) → TextureKeyCandidate { parameterKey, tier }
   ↓      TexturePathRules.ToVmatPath(file, textureRoot) → "wall/concrete_normal.png"
   ↓  TextureAssignResult { assignments, conflicts, unassigned, unresolvedRoles }
   ↓  ShaderEditorViewModel.TrySetTextureValue(key, vmatPath)   → 触发 RebuildText() → KV 预览刷新
```

---

## 1. 术语表

| 术语 | 定义 |
|---|---|
| **角色 / Role**（语义槽位） | 一个**与具体着色器无关**的贴图语义槽位，如 `Normal` / `Color` / `Roughness`。由 `TextureRole` 枚举封闭定义。 |
| **后缀规则 / Rule** | `文件名词干后缀 → 角色` 的用户可配置映射，如 `_normal → Normal`。 |
| **通道名 / channel** | 由参数键 `Key` 派生的裸通道名（见 §5.1）。 |
| **分档 / Tier** | 角色→参数键候选的优先级层级 `TextureKeyTier`（P1…P5，见 §5.2）。 |
| **贴图根目录 / textureRoot** | 配置字段，贴图路径换算的基准目录。 |
| **材质根目录 / MaterialsFolder** | 现有 `MainViewModel.MaterialsFolder`，`.vmat` 扫描基准目录。 |

---

## 2. 拖拽行为矩阵

### 2.1 识别前置条件

| 条件 | 定义 |
|---|---|
| 路径来源 | `Drop` 事件中 `(string[])e.Data.GetData(DataFormats.FileDrop)`，未命中则 `e.Handled = false` 直接返回。 |
| `.vmat` 文件 | `Directory.Exists(p) == false` 且 `Path.GetExtension(p)` 等于 `.vmat`（`OrdinalIgnoreCase`）。 |
| 贴图文件 | `TexturePathRules.IsTextureFile(p) == true`（扩展名见 §6.1）。 |
| 无关文件 | 既非目录、非 `.vmat`、非贴图（`.txt` / `.zip` / `.exe` / `.md` 等）。 |
| 目录判定 | 对目录先按 §2.2 的递归探测拆分为「贴图 / `.vmat` / 无关」，再据此定类别。 |

### 2.2 主行为矩阵（六类，逐格锁定）

| # | 拖入内容 | 判定条件 | 处理动作 | 贴图是否递归 | `.vmat` 是否递归探测 | 是否改变**材质根目录** | 是否改变**贴图根目录** | `StatusText` 状态栏文案（模板） |
|---|---|---|---|---|---|---|---|---|
| **D1** | **材质文件夹** | 目录 且 递归枚举到 `.vmat` 数量 ≥ 1 | ① 若 `Settings.AdoptDroppedVmatFolderAsMaterialsRoot == true`，将该目录设为 `MaterialsFolder` 并 `LoadFolder(...)`；② 若编辑器当前无材质，按 `Settings.DefaultShaderName` 调用 `NewVmat` 载入模板 | 不适用（无贴图处理） | **是**（`AllDirectories`） | **是**（`= 拖入目录`） | 否 | `已载入材质目录「{dir}」（{n} 个 .vmat）。` |
| **D2** | **`.vmat` 文件** | 单个或多个文件，全部为 `.vmat` | ① 取第一个 `.vmat` 的父目录 `d`；② 若 `d != MaterialsFolder` 且 `AdoptDroppedVmatFolderAsMaterialsRoot == true`，先 `LoadFolder(d)`；③ 扫描结果中定位该文件并 `EditMaterial(entry)`（等价点击左树条目）；④ 若 `AutoAssignOnDrop == true`，再把 `d` 下的贴图交给 `TextureAssigner`（递归 = `RecurseTextureFolders`） | 由 `Settings.RecurseTextureFolders` | 否（已给定文件） | **是**（当 `d != MaterialsFolder` 且开关为 true） | 否 | `已打开材质「{file}」（{shader}），自动写入 {k} 个贴图参数。` / 未命中时：`已打开材质「{file}」（{shader}），未匹配到贴图。` |
| **D3** | **贴图文件**（≥1，全部是贴图） | 全部条目为贴图文件 | ① 若 `Editor.MaterialFilePath` 为空且无选中材质 → 按 `Settings.DefaultShaderName` `NewVmat`；② 对 `CurrentShader` 执行 `TextureAssigner.Assign(...)`；③ 逐条 `TrySetTextureValue` | 否（文件列表即全集） | 否 | 否 | **是**，仅当 `Settings.TextureRoot` 为空 → 设为这些文件的 `TexturePathRules.CommonParentDirectory(...)` | `已按后缀自动写入 {k} 个贴图参数（{n} 个文件）：{key1}←{file1}，…` |
| **D4** | **只含贴图的文件夹** | 目录 且 递归枚举 `.vmat` == 0 且 贴图 ≥ 1 | ① **不**改材质根目录；② 按 `RecurseTextureFolders` 收集贴图；③ 无当前材质时按 `DefaultShaderName` `NewVmat`；④ 执行 `Assign` | **是**（`RecurseTextureFolders == true` 时 `AllDirectories`，否则 `TopDirectoryOnly`） | **是**（仅用于判定 `.vmat == 0`） | **否** | **是**，仅当 `Settings.TextureRoot` 为空 → 设为该目录 | `已从贴图文件夹「{dir}」导入 {n} 张贴图（递归：{on/off}），写入 {k} 个参数。` |
| **D5** | **混合内容** | 展开后 `.vmat` ≥ 1 且 贴图 ≥ 1（可含无关文件） | ① 先执行 D2 的 ①②③（用第一个 `.vmat`）；② 再执行 D3 的 ④（对贴图集合）；③ 无关文件忽略并计数 | 由 `RecurseTextureFolders` | **是** | **是**（若含目录，根目录 = **第一个含 `.vmat` 的目录**；若只有文件，根目录 = 该 `.vmat` 的父目录） | **是**，仅当 `TextureRoot` 为空 → 设为贴图集合的公共父目录 | `已导入 {n} 个材质、{m} 张贴图，写入 {k} 个参数；忽略 {x} 个无关文件。` |
| **D6** | **无关文件 / 空拖入** | 展开后 `.vmat` == 0 且 贴图 == 0（或 `FileDrop` 为空） | **无任何写入**、不改任何根目录、仅提示 | 否 | 否 | 否 | 否 | `未识别可导入的内容（{ext 列表}），已忽略。` |

> `ext 列表` = 无关文件扩展名去重、去点、升序、`OrdinalIgnoreCase` 拼接，用 `、` 连接；为空时文案为 `未识别可导入的内容，已忽略。`

### 2.3 `DragOver` / `DragLeave` 契约

| 事件 | 方法名 | 行为 |
|---|---|---|
| `DragOver` | `OnWindowDragOver` | 先在 GUI 层取路径：`var paths = FileDropOf(e.Data)`（`e.Data.GetDataPresent(DataFormats.FileDrop)` 为假时返回 `null`）。再判 `DropImportService.CanAccept(paths) == true` → `e.Effects = DragDropEffects.Copy; e.Handled = true;`；否则 `e.Effects = DragDropEffects.None; e.Handled = false;`（不拦截其他拖放源）。 |
| `Drop` | `OnWindowDrop` | 取 `string[]`（同一个 `FileDropOf`）；空数组 → 按 D6 提示并 `e.Handled = true` 返回。否则 `ViewModel.HandleDroppedPathsCommand.Execute(paths)` 并 `e.Handled = true`。 |
| `DragLeave` | `OnWindowDragLeave` | 仅清除悬停视觉反馈（若有），不改任何状态。 |

> **`IDataObject → string[]` 的转换只发生在 GUI 层**：`Lib` 是 `net10.0` 纯类库，不得出现 `System.Windows.IDataObject`（详见 §7.8 签名约束）。
> GUI 侧统一走私有辅助方法 `private static string[]? FileDropOf(IDataObject data)`，由 `OnWindowDragOver` 与 `OnWindowDrop` 共用，
> 保证两个处理器对「什么算可接受拖放」的判定完全一致。

### 2.4 枚举预算与失败隔离

| 项 | 规定 |
|---|---|
| 单次拖拽枚举上限 | 每个目录最多枚举 **5000** 个文件条目；超出即截断并在 `StatusText` 追加 `（枚举已达 5000 项上限，结果可能不完整）`。 |
| 权限拒绝 | 任一目录 `EnumerateFiles` 抛 `UnauthorizedAccessException` → 跳过该目录，计入「无关/不可访问」，不中断整体处理。 |
| 异常兜底 | `OnWindowDrop` / `OnWindowDragOver` / `OnWindowDragLeave` 三个方法体必须整体包在 `ControlErrorRecorder.Guard("拖拽处理", this, ...)` 中（与既有 `OnMaterialEntryClicked` 风格一致）。 |

---

## 3. 配置数据模型与 JSON 结构

### 3.1 存储位置

| 项 | 值 |
|---|---|
| 目录 | `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` → `%APPDATA%` |
| 完整路径 | `%APPDATA%\VmatGenerator\settings.json`（例：`C:\Users\<user>\AppData\Roaming\VmatGenerator\settings.json`） |
| 目录创建 | `Save` 时若目录不存在 → `Directory.CreateDirectory(...)`（`createDirectory: true`）。 |
| 原子写 | 先写 `settings.json.tmp`（UTF-8 无 BOM，`WriteIndented = true`，行尾 `\n`），再 `File.Move(tmp, path, overwrite: true)`。 |
| 编码 | `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` |
| 损坏备份 | `%APPDATA%\VmatGenerator\settings.corrupt-<yyyyMMdd-HHmmss>.json`（同目录，拷贝原始字节） |

### 3.2 数据模型（Lib 精确类型见 §7.1）

| 字段 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `schemaVersion` | `int` | `1` | 仅接受 `1`；其余值触发字段级修复。 |
| `defaultShaderName` | `string` | `"csgo_environment.vfx"` | 必须是 `ShaderCatalog.Find(name) != null`。 |
| `textureRoot` | `string` | `""`（空） | 贴图路径换算基准目录；空或不存在时全部写原路径。 |
| `autoAssignOnDrop` | `bool` | `true` | 拖入后是否自动按后缀写入参数。 |
| `recurseTextureFolders` | `bool` | `true` | 拖入贴图文件夹时是否递归。 |
| `adoptDroppedVmatFolderAsMaterialsRoot` | `bool` | `true` | 拖入材质目录 / `.vmat` 时是否接管材质根目录。 |
| `rules` | `TextureSuffixRule[]` | §3.4 种子表 | 顺序即优先级（并列时以下标决胜）。 |

### 3.3 JSON 结构（示例）

> 下例仅节选 6 条以便阅读；**首次运行**落盘时 `rules` 必须是 §4 的全部 **35 条**种子规则。

```json
{
  "schemaVersion": 1,
  "defaultShaderName": "csgo_environment.vfx",
  "textureRoot": "D:/art/backrooms/textures",
  "autoAssignOnDrop": true,
  "recurseTextureFolders": true,
  "adoptDroppedVmatFolderAsMaterialsRoot": true,
  "rules": [
    { "suffix": "_normal",   "role": "Normal",           "enabled": true },
    { "suffix": "_n",        "role": "Normal",           "enabled": true },
    { "suffix": "_diffuse",  "role": "Color",            "enabled": true },
    { "suffix": "_rough",    "role": "Roughness",        "enabled": true },
    { "suffix": "_ao",       "role": "AmbientOcclusion", "enabled": true },
    { "suffix": "_height",   "role": "Height",           "enabled": true }
  ]
}
```

序列化选项（**锁定**）：

| 项 | 值 |
|---|---|
| `PropertyNamingPolicy` | `JsonNamingPolicy.CamelCase` |
| `DefaultIgnoreCondition` | `JsonIgnoreCondition.Never`（全字段落盘，便于人工编辑） |
| 枚举转换 | `JsonStringEnumConverter`（写入枚举名；读取时**大小写不敏感**） |
| `WriteIndented` | `true` |
| `ReadCommentHandling` | `JsonCommentHandling.Skip` |
| `AllowTrailingCommas` | `true` |
| 未知字段 | 默认忽略（不报错） |

### 3.4 加载回退策略（三种失败必须分别处理）

| 情形 | 判定 | 行为 | `Outcome` |
|---|---|---|---|
| **① 文件不存在** | `!File.Exists(path)` | 返回 `VmatGeneratorSettings.CreateDefault()`（含 §4 全部种子规则，`textureRoot = ""`）。**不**写盘，等用户首次保存时再落盘。 | `Defaulted` |
| **② 文件损坏** | 零字节 / 空白字符 / `JsonException` / 读 `IOException` / `UnauthorizedAccessException` | ① 将原始字节备份为 `settings.corrupt-<ts>.json`（备份失败也不阻断）；② 返回 `CreateDefault()`；③ `report.ErrorMessage` 记录异常摘要，`report.BackupFilePath` 记录备份路径；④ 状态栏显示 `设置文件损坏，已备份为 {backup} 并恢复默认设置。` | `RecoveredFromCorruption` |
| **③ 字段非法** | 逐字段校验（见 §3.5） | **只回退非法字段，保留其余合法字段**；每个被修复的字段名加入 `report.RepairedFields`；状态栏显示 `已修复 {n} 个非法设置字段（{字段1}、{字段2} …）。` | `Repaired` |
| ④ 加载成功 | 无异常且校验通过 | 原样返回（`rules` 顺序保持 JSON 中的顺序）。 | `Loaded` |

> 任何加载异常都**不得**冒泡到 UI 线程；`VmatGeneratorSettingsStore.Load` 返回 `null` + `report`（`ErrorMessage` 非空）表示彻底失败，此时调用方应使用 `LoadOrDefault()`。

### 3.5 字段级校验表

| 字段 | 合法条件 | 非法处理 |
|---|---|---|
| `schemaVersion` | `== 1` | 置 `1`，记入 `RepairedFields` |
| `defaultShaderName` | 非空且 `ShaderCatalog.Find(v) != null` | 置 `"csgo_environment.vfx"`，记录 |
| `textureRoot` | 字符串；`Directory.Exists` **不作为合法性判据**（网络盘掉线不得丢失配置） | 不修复；仅在 `TexturePathRules.ToVmatPath` 中按「目录不存在 → 写原路径」处理 |
| `autoAssignOnDrop` / `recurseTextureFolders` / `adoptDroppedVmatFolderAsMaterialsRoot` | JSON 布尔；若类型不符（如字符串） | 置默认 `true`，记录 |
| `rules == null` 或键缺失 | — | 填充 §4 种子表，**不**记入 `RepairedFields`（视为首次运行） |
| `rules == []` | — | **保留为空**（用户显式清空），**不回填**默认。GUI「恢复默认规则」按钮才回填。 |
| `rules[i].suffix` | `TextureSuffixMatcher.NormalizeName(suffix)` 后非空 | 丢弃该条规则，记录 `rules[i].suffix` |
| `rules[i].role` | `Enum.TryParse<TextureRole>(v, ignoreCase: true)` 成功 | 该条 `role` 置 `TextureRole.Unknown` 并 `Enabled = false`，记录 |
| `rules` 内归一化 suffix 重复 | 唯一 | **保留数组中第一条**，其余整条丢弃，记录 `rules[i].suffix(重复)` |
| `rules[i].enabled` | 布尔 | 置 `true`，记录 |

---

## 4. 默认种子后缀规则表（35 条）

> 「语义槽位」列即 `TextureRole`；**所有条目 `enabled = true`**。
> 优先级不在此处决定，而由 §5.3 的「最长后缀优先 + 数组下标」在运行时裁定。

| # | 后缀（原文） | 归一化形式 | **语义槽位（Role）** | 槽位中文名 | 常见于 / 说明 |
|---:|---|---|---|---|---|
| 1 | `_normal` | `normal` | `Normal` | 法线 | 切线空间法线，最通用 |
| 2 | `_n` | `n` | `Normal` | 法线 | 短后缀；长度短，仅在没有更长命中时生效 |
| 3 | `_normals` | `normals` | `Normal` | 法线 | 复数写法 |
| 4 | `_diffuse` | `diffuse` | `Color` | 颜色 / 反照率 | PBR 通用 |
| 5 | `_albedo` | `albedo` | `Color` | 颜色 / 反照率 | PBR 通用 |
| 6 | `_color` | `color` | `Color` | 颜色 / 反照率 | Source 2 生态最常见 |
| 7 | `_col` | `col` | `Color` | 颜色 / 反照率 | 短后缀 |
| 8 | `_rough` | `rough` | `Roughness` | 粗糙度 | Source 2 常见 |
| 9 | `_roughness` | `roughness` | `Roughness` | 粗糙度 | PBR 通用 |
| 10 | `_metal` | `metal` | `Metalness` | 金属度 | Source 2 常见 |
| 11 | `_metalness` | `metalness` | `Metalness` | 金属度 | PBR 通用 |
| 12 | `_ao` | `ao` | `AmbientOcclusion` | 环境光遮蔽 | 短后缀 |
| 13 | `_ambientocclusion` | `ambientocclusion` | `AmbientOcclusion` | 环境光遮蔽 | PBR 通用 |
| 14 | `_height` | `height` | `Height` | 高度 | 视差 / 位移 |
| 15 | `_trans` | `trans` | `Translucency` | 半透明 | 短后缀 |
| 16 | `_translucency` | `translucency` | `Translucency` | 半透明 | 全称 |
| 17 | `_detail` | `detail` | `Detail` | 细节 | 细节层漫反射 |
| 18 | `_detailmask` | `detailmask` | `DetailMask` | 细节遮罩 | P4 精确命中 `TextureDetailMask`。**必须**指向 `DetailMask` 而非 `Mask` —— 若指向 `Mask`，在 `csgo_complex` 上会再次落进 P5 双候选歧义（§5.6 不变量） |
| 19 | `_mask` | `mask` | `Mask` | 遮罩 | 通用遮罩；在多候选着色器上会触发 `AmbiguousQualified` |
| 20 | `_tintmask` | `tintmask` | `TintMask` | 染色遮罩 | 对应 `TextureTintMask1` |
| 21 | `_emissive` | `emissive` | `Emissive` | 自发光 | **预留项：当前 11 个模板均无 `TextureEmissive` 键，解析结果恒为 `NoMatchingKey`（不写入）。**将来新增含该键的模板后即自动生效 |
| 22 | `_selfillum` | `selfillum` | `Emissive` | 自发光 | **预留项：当前 11 个模板均无 `TextureEmissive` 键，解析结果恒为 `NoMatchingKey`（不写入）。**Source 2 命名 |
| 23 | `_foam` | `foam` | `FoamMask` | 泡沫遮罩 | `csgo_water_fancy` |
| 24 | `_foamnormal` | `foamnormal` | `FoamNormal` | 泡沫法线 | P4 精确命中 `TextureFoamNormal` |
| 25 | `_waves` | `waves` | `WavesMask` | 波浪遮罩 | **预留项：当前 11 个模板均无 `TextureWaves` 键，解析结果恒为 `NoMatchingKey`（不写入）。**注意 `csgo_water_fancy` 实际只有 `TextureWavesNormal` / `TextureWavesHeight`，两者各由第 26 / 34 条专用规则覆盖 |
| 26 | `_wavesnormal` | `wavesnormal` | `WavesNormal` | 波浪法线 | P4 精确命中 `TextureWavesNormal`；§5.6 诊断文本的建议后缀之一 |
| 27 | `_debris` | `debris` | `DebrisColor` | 漂浮物颜色 | `csgo_water_fancy` |
| 28 | `_debrisnormal` | `debrisnormal` | `DebrisNormal` | 漂浮物法线 | P4 精确命中 `TextureDebrisNormal`；§5.6 诊断文本的建议后缀之一 |
| 29 | `_lightmap` | `lightmap` | `Lightmap` | 光照贴图 | **预留项：当前 11 个模板均无可用槽位，解析结果恒为 `NoMatchingKey`（不写入）。**`LightMapTextureName` 虽在 3 个模板中出现，但**不以 `Texture` 开头**，不满足 §5.1 候选条件①，永不成为候选 |
| 30 | `_cube` | `cube` | `CubeMap` | Cube 贴图 | 天空盒 / 环境 |
| 31 | `_rimmask` | `rimmask` | `RimMask` | 边缘光遮罩 | **§5.6 不变量兜底**：P4 精确命中 `csgo_character` 的 `TextureRimMask` |
| 32 | `_lowendcubemap` | `lowendcubemap` | `LowEndCubeMap` | 低端 Cube Map | **§5.6 不变量兜底**：P4 精确命中 `csgo_water_fancy` 的 `TextureLowEndCubeMap` |
| 33 | `_selfillummask` | `selfillummask` | `SelfIllumMask` | 自发光遮罩 | **§5.6 不变量兜底**：P4 精确命中 `TextureSelfIllumMask`（csgo_vertexlitgeneric / csgo_complex / csgo_static_overlay） |
| 34 | `_wavesheight` | `wavesheight` | `WavesHeight` | 波浪高度 | **§5.6 不变量兜底**：`csgo_water_fancy` 的 `Height` 角色有两个 P5 候选，诊断文本会建议本后缀 |
| 35 | `_debrisheight` | `debrisheight` | `DebrisHeight` | 漂浮物高度 | **§5.6 不变量兜底**：同上 |

> **第 31–35 条与第 18 条的改指，全部由 §5.6 不变量驱动**：
> 诊断文本中的每一个建议后缀，**必须**能在默认种子表中找到规则，且该规则**必须**能把文件路由到对应的候选键。
> 缺任一条，用户照诊断提示改名后仍会写不进去 —— 这比原始症状更糟，因为用户已经「按提示操作过」了。
> 完整闭合校验见 §5.6.2 的闭合表与 §11 自检清单第 12 项。

#### 关于 4 条「预留规则」（第 21 / 22 / 25 / 29 条）

经核对 `Lib/ShaderCatalog.cs`：**`TextureEmissive` 出现 0 次、`TextureWaves` 出现 0 次**；
`LightMapTextureName` 虽出现 3 次，但**不以 `Texture` 开头**，不满足 §5.1 候选条件①。
因此 `_emissive` / `_selfillum` / `_waves` / `_lightmap` 这 **4 条规则在当前全部 11 个模板上都解析不出任何参数键**。

| 项 | 说明 |
|---|---|
| 当前行为 | 恒为 `NoMatchingKey` —— **不写入任何参数**，并在状态栏显示 `ℹ 有 1 个槽位在该着色器中没有对应贴图参数，已跳过。` |
| **为何保留** | 删掉等于**过拟合今天的着色器目录**：将来新增含 `TextureEmissive` / `TextureWaves` 的模板，这些规则立刻可用。 |
| 为何不构成静默错误 | 规则不会「猜错槽位」，只会**诚实报告无对应键** —— 用户拿到的是明确的未解析诊断，而不是一个看起来正常、实则贴错的 `.vmat`。这正是 §5.4「无歧义优先」原则的同向结果。 |
| 与 §5.6 不变量的关系 | **不受** INV-DIAG-CLOSURE 约束 —— 不变量只保证「诊断**建议**的后缀可执行」，而这 4 条规则从不产生建议（它们的结果是 `NoMatchingKey`，不是 `AmbiguousQualified`）。 |

> **第 26 / 28 条是为 §5.6 诊断文本兜底而设**：当某着色器的通用槽位（如 `Normal`）因多候选被判为 `AmbiguousQualified` 时，
> 状态栏会建议用户「改用 `_foamnormal` / `_debrisnormal` / `_wavesnormal` 后缀」。这三条规则**必须**存在于默认种子表中，
> 否则诊断建议将无法执行 —— 用户照做后仍然匹配不到任何规则。

> **不写入的常见后缀**：`_specular`（无对应槽位）、`_bump`（本项目按 `_height` 语义并入 Height，故**不**设 `_bump` 规则）、`_gloss`（与 `_rough` 互为补数，刻意**不**设，避免误写）、`_mask1/2/3`（层级遮罩靠 §5.3 的 P2 自动覆盖，不单列规则）。

---

## 5. 角色 → 参数键 的派生与解析

### 5.1 通道名（channel）派生算法

对 `ShaderTemplate.Parameters` 中每个 `p`，先判定是否为**候选参数**：

| 候选条件（全部满足） | 说明 |
|---|---|
| ① `p.Key` 以 `"Texture"` 开头（`Ordinal`） | `SkyTexture` / `g_tNormal1` 等一律**不**参与自动分配 |
| ② `p.Kind == ShaderParamKind.Texture` **或** `p.Shape == ShaderValueShape.TextureOrVector` | 后者允许「常量 vec4 ↔ 贴图路径」互换（如 `TextureRoughness1`、`TextureFoamNormal`） |

通过后按下列步骤派生：

```
rest       = Key.Substring("Texture".Length)            // "Normal1" / "Layer1Color" / "FoamNormal" / "CubeMap"
layer      = 0
若 rest 匹配 ^Layer(\d+)(.+)$ ：  layer = g1，rest = g2 // "Layer1Color" → layer=1, rest="Color"
num        = 0
若 rest 匹配 ^(.*?)(\d+)$ 且 g1 非空： base = g1，num = int(g2)   // "Normal1" → base="Normal", num=1
否则       base = rest                                     // "Mask1" → base="Mask", num=1
channel    = base                                         // 派生通道名
qualifier  = base 中去掉末尾 RoleToken 后的前缀             // "FoamNormal" vs Normal → "Foam"
```

| 输入键 | layer | num | **channel** | 备注 |
|---|---:|---:|---|---|
| `TextureColor` | 0 | 0 | `Color` | |
| `TextureColor1` | 0 | 1 | `Color` | 尾号被剥掉 |
| `TextureLayer1Normal` | 1 | 0 | `Normal` | Layer 前缀被剥掉 |
| `TextureMask1` | 0 | 1 | `Mask` | |
| `TextureAmbientOcclusion` | 0 | 0 | `AmbientOcclusion` | |
| `TextureFoamNormal` | 0 | 0 | `FoamNormal` | 复合通道 |
| `TextureCubeMap` | 0 | 0 | `CubeMap` | 数字只在末尾才算尾号 |
| `TextureLowEndCubeMap` | 0 | 0 | `LowEndCubeMap` | 复合通道 |
| `SkyTexture` | — | — | **不参与** | 条件 ① 不满足 |

### 5.2 角色 → Token 表（闭合定义）

| Role（枚举名） | 中文名 | **主 Token** | **别名 Token**（参与相等判定） |
|---|---|---|---|
| `Unknown` | 未识别 | `—` | `—` |
| `Color` | 颜色 / 反照率 | `Color` | `Albedo`, `Diffuse`, `Base` |
| `Normal` | 法线 | `Normal` | `Normals`, `Norm` |
| `Roughness` | 粗糙度 | `Roughness` | `Rough` |
| `Metalness` | 金属度 | `Metalness` | `Metal`, `Metallic` |
| `AmbientOcclusion` | 环境光遮蔽 | `AmbientOcclusion` | `AO`, `Occlusion` |
| `Height` | 高度 | `Height` | `Displacement` |
| `Translucency` | 半透明 | `Translucency` | `Trans`, `Transmission` |
| `Detail` | 细节 | `Detail` | `DetailAlbedo` |
| `Mask` | 遮罩 | `Mask` | （无） |
| `Emissive` | 自发光 | `Emissive` | `SelfIllum`, `SelfIllumination`, `Emission` |
| `CubeMap` | Cube 贴图 | `CubeMap` | `Cube`, `Sky`, `Env` |
| `Lightmap` | 光照贴图 | `Lightmap` | `LightMap`, `LightMapTexture` |
| `FoamMask` | 泡沫遮罩 | `Foam` | `FoamMask` |
| `FoamNormal` | 泡沫法线 | `FoamNormal` | （无） |
| `WavesMask` | 波浪遮罩 | `Waves` | `WavesMask` |
| `WavesNormal` | 波浪法线 | `WavesNormal` | （无） |
| `WavesHeight` | 波浪高度 | `WavesHeight` | （无） |
| `DebrisColor` | 漂浮物颜色 | `Debris` | `DebrisColor` |
| `DebrisNormal` | 漂浮物法线 | `DebrisNormal` | （无） |
| `DebrisHeight` | 漂浮物高度 | `DebrisHeight` | （无） |
| `TintMask` | 染色遮罩 | `TintMask` | （无） |
| `DetailMask` | 细节遮罩 | `DetailMask` | （无） |
| `SelfIllumMask` | 自发光遮罩 | `SelfIllumMask` | （无） |
| `RimMask` | 边缘光遮罩 | `RimMask` | （无） |
| `LowEndCubeMap` | 低端 Cube Map | `LowEndCubeMap` | （无） |

> Token 比较一律 `OrdinalIgnoreCase`；**别名仅在 P4 / P5 判定中生效**（P1–P3 只认主 Token，保证「直名」档位稳定）。
>
> **最后 4 个角色（`DetailMask` / `SelfIllumMask` / `RimMask` / `LowEndCubeMap`）是为闭合 §5.6 不变量而设的**，
> 它们的 channel 全部是「限定词 + 通用 Token」的复合名，若没有同名角色，用户按诊断文本改名后仍会掉回 P5 的多候选歧义。
>
> ⚠️ **主 Token 必须取完整复合名，不得取其内部通用 Token。** 反例：若 `SelfIllumMask` 的主 Token 写成 `SelfIllum`，
> 则 `TextureSelfIllumMask` 的 channel `SelfIllumMask` 需**以 `SelfIllum` 结尾**才能 P5 命中 —— 而它并不以 `SelfIllum` 结尾
> （多出 `Mask`），于是既落不进 P4 精确相等、也落不进 P5，只能掉进 `Mask` 角色的双候选歧义。
> 取完整名 `SelfIllumMask` 才能让 P4 的 `channel == 主Token` 精确命中。`RimMask` / `DetailMask` / `LowEndCubeMap` 同理。

### 5.3 解析优先级分档（`TextureKeyTier`，P1 → P5）

按顺序求值，**首个产出结果的档位即决定结果，后续档位不再考虑**。
P1–P4 的产出是「一个命中的参数键」；**P5 的产出是「唯一命中的参数键」或「`None` + `AmbiguousQualified`」**。

| 档位 | 名称 | **键形态（`{R}` = 角色主 Token，`{N}` = 1..99）** | 判定谓词 | `csgo_water_fancy` 命中示例 |
|---|---|---|---|---|
| **P1** | `ExactName` 直名无缀 | **`Texture{R}`** | `layer == 0 && num == 0 && Key == "Texture" + 主Token` | `TextureColor`（对 `Color`） |
| **P2** | `ExactNameNumbered` 直名 + 数字尾缀 | **`Texture{R}{N}`** | `layer == 0 && num >= 1 && base == 主Token` | `TextureNormal1`（对 `Normal`） |
| **P3** | `LayerPrefixed` Layer 前缀 | **`TextureLayer{N}{R}`** | `layer >= 1 && base == 主Token`（`num` 任意） | `TextureLayer1Normal`（对 `Normal`） |
| **P4** | `DerivedChannelEqual` 派生通道名精确相等 | **`{R}`（等于 Token 或别名）** | `channel ∈ (主Token ∪ 别名)`（大小写不敏感），且该键不满足 P1–P3 的**字面**形态 | `TextureAmbientOcclusion`（对 `AO`）、`TextureMetal`（对 `Metalness`）、`TextureSelfIllum`（对 `Emissive`） |
| **P5** | `QualifiedComposite` 带限定词的复合通道 | **`Texture{Q}{R}`**（`{Q}` = 非空限定词，如 `Foam` / `Debris` / `Waves` / `Tint`） | `channel` 以 `主Token` 或任一别名结尾且剩余前缀 `qualifier` 非空，**且合格候选唯一**；合格候选 ≥ 2 时**一律不命中** | 唯一候选时：`TextureTintMask1`（对 `Mask`，`q = Tint`）、`TextureLowEndCubeMap`（对 `CubeMap`）。<br>**多候选时**：对 `Normal` 命中 `TextureFoamNormal` / `TextureDebrisNormal` / `TextureWavesNormal` 三条 → `None` 的 `AmbiguousQualified` |
| — | `None` | — | **两类未解析，均不写入任何参数**，并记入 `UnresolvedRoles`：<br>① `NoMatchingKey` —— P1–P5 皆无合格候选（该模板没有对应槽位）；<br>② `AmbiguousQualified` —— **仅 P5 专有**：合格候选 ≥ 2 且互相并列，在不引入 per-shader 特判的前提下无法确定取舍 | — |

**P4 / P5 的边界**：P4 要求「整条 `channel` 等于某个 Token」，P5 要求「`channel` 以某个 Token 结尾且前缀非空」。二者互斥，故不存在同档重复计入。

**P1–P4 的多候选一律正常命中**（用 §5.4 的排序键确定取舍）；**只有 P5 的多候选被降级为 `None`**。这是本规格唯一的特例，理由见 §5.4 设计依据。

### 5.4 同档多命中的确定性取舍（**总排序，锁定**）

在命中档位内，对候选按以下键**依次升序**排序，取第一个：

| 序 | 排序键 | 适用档位 | 说明 |
|---:|---|---|---|
| 1 | **档内附加序** | P2 → `num` 升序<br>P3 → `layer` 升序<br>P4 → `num` 升序、`layer` 升序 | 数字越小越「基础」。**P5 不使用任何排序键**（见下） |
| 2 | `ParameterIndex` | P1–P4 | 该参数在 `ShaderTemplate.Parameters` 中的**声明下标**升序 |
| 3 | `string.CompareOrdinal(ParameterKey)` | P1–P4 | 键名序数比较升序 |

> 上表三键构成**全序**，因此任意输入下 P1–P4 的结果唯一、可复现、可单测。
>
> **P5 唯一候选时无需排序**：合格候选只有 1 条，排序键无处施加，结果由该候选本身唯一确定。
> **P5 多候选时不排序也不命中**：直接按 §5.3 返回 `None` / `AmbiguousQualified`，候选全量进入诊断文本。

**设计依据（为何 P5 多候选必须放弃命中）**：

本工具的产物是**进入游戏的 `.vmat`**。对 `csgo_water_fancy` 的 `Normal` 槽位，模板同时存在 `TextureFoamNormal`、`TextureWavesNormal`、`TextureDebrisNormal` 三条**语义等价且互不包含**的复合通道。任何「取限定词最短」「取声明序第一」「取字母序第一」的规则，选取结果都由 Foam/Waves/Debris 这几个词的**字母长度或声明位置这类偶然属性**决定，与「哪个才是玩家真正想要的法线图」毫无关系。

而两类错误对用户的代价高度不对称：

| 结果 | 用户可感知性 | 后果 |
|---|---|---|
| **未命中（不写入）** | 立刻可见 —— 状态栏与预览区明确提示该槽位无对应参数 | 用户改名后缀或换着色器即可，零隐性风险 |
| **猜中一个（错）** | **不可见** —— 生成出一个「看起来正常、实则贴错槽位」的 `.vmat` | 文件合法、渲染不报错，直到进游戏才发现贴错，排查成本高 |

一句话概括：**静默猜错法线图的代价比「没填上」高得多** —— 后者用户一眼看得到，前者会生成一个看起来正常、实则贴错槽位的文件。因此 P5 采用「**唯一候选才写、多候选如实报告不写**」。该规则**不含任何 per-shader 特判**（只看候选数量），既保住 6 个候选唯一的正确场景，又让水面这类多候选场景如实暴露，而不是把任意性固化成契约。

> **已知连带后果（本次裁决的直接结果，lib-dev 须知）**：`csgo_complex.vfx` 的 `Mask` 角色同时命中
> `TextureSelfIllumMask` 与 `TextureDetailMask` 两条 P5 候选 → 同样按本规则判为 `None` / `AmbiguousQualified`，**不再写入**。
> 这是「通用规则优先于个别偏好」的必然取舍：若要为它开特例，就必须引入按着色器配置的限定词优先级表，与本裁决「不含 per-shader 特判」的要求冲突。
> 用户可通过后缀规则绕过（见 §5.5 水面表下方的诊断文本示例）。

### 5.5 三个真实着色器的解析结果示例（锁定）

#### `csgo_environment.vfx`

| 角色 | 命中档位 | 候选（按 §5.4 排序） | **最终参数键** |
|---|---|---|---|
| `Normal` | P2 | `TextureNormal1`(num=1, idx) | **`TextureNormal1`** |
| `Color` | P2 | `TextureColor1` | **`TextureColor1`** |
| `Roughness` | P2 | `TextureRoughness1`（Vector / `TextureOrVector`，按 §5.1 条件② 入选） | **`TextureRoughness1`** |
| `Metalness` | P2 | `TextureMetalness1` | **`TextureMetalness1`** |
| `AmbientOcclusion` | P2 | `TextureAmbientOcclusion1` | **`TextureAmbientOcclusion1`** |
| `Height` | P2 | `TextureHeight1` | **`TextureHeight1`** |
| `Mask` | P5（**候选唯一**） | `TextureTintMask1`（`qualifier = Tint`） | **`TextureTintMask1`** |
| `Detail` / `Translucency` / `Emissive` / `CubeMap` | — | 该模板无对应键（`NoMatchingKey`） | **未解析（不写入）** |

#### `csgo_lightmappedgeneric.vfx`

| 角色 | 命中档位 | 候选 | **最终参数键** |
|---|---|---|---|
| `Normal` | P3 | `TextureLayer1Normal`(layer=1) | **`TextureLayer1Normal`** |
| `Color` | P3 | `TextureLayer1Color` | **`TextureLayer1Color`** |
| `Roughness` | P3 | `TextureLayer1Roughness`（`TextureOrVector`） | **`TextureLayer1Roughness`** |
| `AmbientOcclusion` | P3 | `TextureLayer1AmbientOcclusion` | **`TextureLayer1AmbientOcclusion`** |
| `Detail` | P3 | `TextureLayer1Detail` | **`TextureLayer1Detail`** |
| `Translucency` | P3 | `TextureLayer1Translucency` | **`TextureLayer1Translucency`** |
| `Metalness` / `Emissive` / `CubeMap` | — | 该模板无对应键（金属度是标量 `g_flMetalness`）→ `NoMatchingKey` | **未解析（不写入）** |

#### `csgo_water_fancy.vfx`

| 角色 | 命中档位 | 候选 | **最终参数键** |
|---|---|---|---|
| `Normal` | P5（**多候选并列**） | `TextureFoamNormal`、`TextureDebrisNormal`、`TextureWavesNormal` —— 三者互不包含、语义等价 | **未解析（`AmbiguousQualified`，不写入）** |
| `Color` | — | 无任何 `channel` 等于或以 `Color` 结尾（`NoMatchingKey`） | **未解析（不写入）** |
| `Roughness` | — | 该着色器粗糙度是标量 `g_flWaterRoughnessMax/Min`，无对应纹理键（`NoMatchingKey`） | **未解析（不写入）** |
| `FoamMask` | P4 | `TextureFoam` | **`TextureFoam`** |
| `DebrisColor` | P4 | `TextureDebris` | **`TextureDebris`** |
| `DebrisHeight` / `DebrisNormal` | P4 | `TextureDebrisHeight` / `TextureDebrisNormal` | 同名键 |
| `WavesNormal` / `WavesHeight` | P4 | `TextureWavesNormal` / `TextureWavesHeight` | 同名键 |
| `CubeMap` | P5（**候选唯一**） | `TextureLowEndCubeMap`（`qualifier = LowEnd`） | **`TextureLowEndCubeMap`** |
| `Height` | P5（**多候选并列**） | `TextureDebrisHeight`、`TextureWavesHeight` —— 两条互不包含 | **未解析（`AmbiguousQualified`，不写入）**；诊断建议 `_debrisheight` / `_wavesheight`（§4 第 34 / 35 条已提供） |

> **该着色器没有通用的 法线 槽位。** 把 `wall_normal.png` 拖到 `csgo_water_fancy.vfx` 上**不会**写入任何参数；
> 状态栏按 §5.6 组装出如下诊断文本，完整候选列在提示中，不做任何猜测：
>
> ```
> 该着色器没有通用的 法线 槽位，候选为 TextureFoamNormal / TextureDebrisNormal / TextureWavesNormal；
> 请改用 _foamnormal / _debrisnormal / _wavesnormal 后缀，或改选其他着色器。
> ```
>
> 用户按提示改名为 `_foamnormal` / `_debrisnormal` / `_wavesnormal` 后，后缀规则把它们分别导向
> `FoamNormal` / `DebrisNormal` / `WavesNormal` 角色，由上表的 **P4** 精确命中各自同名键。
> **不允许在代码里对 `csgo_water_fancy` 特判。**

#### 其余 8 个模板的派生总览（同一算法，供参考校验）

> `ShaderCatalog.All` 共 **11** 个模板，本节列出除前述 `csgo_environment` / `csgo_lightmappedgeneric` / `csgo_water_fancy` 三个以外的**全部 8 个**，无遗漏。

| 模板 | P1 直名 | P2 尾号 | P3 Layer | P5 复合（唯一候选才命中） | 不会参与分配（原因） |
|---|---|---|---|---|---|
| `csgo_vertexlitgeneric.vfx` | `TextureColor` / `TextureNormal` / `TextureRoughness` / `TextureMetalness` / `TextureAmbientOcclusion` / `TextureTranslucency` | — | — | `TextureSelfIllumMask` → P4 `SelfIllumMask`（也作 `Mask` 的唯一 P5 候选） | — |
| `csgo_complex.vfx` | `TextureColor` / `TextureNormal` / `TextureRoughness` / `TextureDetail` / `TextureAmbientOcclusion` / `TextureTranslucency` | — | — | `TextureSelfIllumMask` → P4 `SelfIllumMask`；`TextureDetailMask` → P4 `DetailMask`；二者**同时**构成 `Mask` 的 2 条 P5 候选 → **`Mask` 未解析** | — |
| `csgo_static_overlay.vfx` | `TextureColor` / `TextureNormal` / `TextureRoughness` / `TextureMetalness` / `TextureAmbientOcclusion` / `TextureTranslucency` | — | — | `TextureSelfIllumMask` → P4 `SelfIllumMask`（也作 `Mask` 的唯一 P5 候选） | — |
| `csgo_character.vfx` | `TextureColor` / `TextureNormal` / `TextureRoughness` / `TextureMetalness` / `TextureAmbientOcclusion` | — | — | `TextureRimMask` → P4 `RimMask`（也作 `Mask` 的唯一 P5 候选） | `TextureCloth`（channel `Cloth` 不以任何角色 Token 结尾，既非 P4 也非 P5 → 无关，见 §5.6.2 说明） |
| `csgo_moondome.vfx` | `TextureColor`(Vector) / `TextureCubeMap` | — | — | — | — |
| `sky.vfx` | — | — | — | — | `SkyTexture` 不以 `Texture` 开头（§5.1 条件①） |
| `csgo_effects.vfx` | `TextureColor`(Vector) / `TextureTranslucency`(Vector) | `TextureMask1` / `TextureMask2` / `TextureMask3` → `Mask`（**P2**，取 `num` 最小 ⇒ **`TextureMask1`**） | — | — | — |
| `generic.vfx` | `TextureColor` | — | — | — | — |

### 5.6 未解析诊断文本的组装规则（**锁定，GUI 直接显示**）

`TextureRoleResolver` 对每个未解析角色产出 `DiagnosticText`（见 §7.5）。组装规则按 `UnresolvedReason` 分支：

| `UnresolvedReason` | 模板（`{槽位}` = `TextureRoleTokens.Describe(role)`，`{候选}` / `{后缀}` 见下方连接符规则） | 示例（`csgo_water_fancy` / `Normal`） |
|---|---|---|
| `AmbiguousQualified` | `该着色器没有通用的 {槽位} 槽位，候选为 {候选}；请改用 {后缀} 后缀，或改选其他着色器。` | `该着色器没有通用的 法线 槽位，候选为 TextureFoamNormal / TextureDebrisNormal / TextureWavesNormal；请改用 _foamnormal / _debrisnormal / _wavesnormal 后缀，或改选其他着色器。` |
| `NoMatchingKey` | `该着色器没有 {槽位} 对应的贴图参数（已跳过）。` | `该着色器没有 颜色 / 反照率 对应的贴图参数（已跳过）。` |

组装细则：

| 项 | 规定 |
|---|---|
| `{槽位}` | **`TextureRoleTokens.Describe(role)` —— 一律输出中文语义名（如 `Normal` → `法线`、`Color` → `颜色 / 反照率`），绝不输出枚举名。**依据：该文本**直接呈现在状态栏与预览区**，须与界面其余文案同语言；`Normal` / `Color` 这类枚举名是**程序内部标识**，泄漏给终端用户既无信息量也不可读。**本口径对 `AmbiguousQualified` 与 `NoMatchingKey` 两个分支一致**，示例与断言均须写中文名 |
| `{候选}` | 取 `TextureRoleResolution.Candidates` 中所有 `ParameterKey`，**按 §5.4 第 2 排序键 `ParameterIndex` 升序**排列（即模板声明序），用 `" / "`（斜杠两侧各一个空格）连接。**不**使用旧的限定词长度序。 |
| `{后缀}` | 对 `{候选}` 中每个键去掉 `Texture` 前缀后 `ToLowerInvariant()`，前置下划线 `_`，同样按候选顺序、用 `" / "` 连接。即 `TextureFoamNormal → _foamnormal`。 |
| 换行 | 模板为**单行**，UI 中由容器自动折行；不得插入 `\n`。 |
| 分隔符 | 候选/后缀连接符固定为 `" / "`（ASCII 斜杠 + 两侧各一空格），全角顿号 `、` **不得**使用（会被误读为顿号列表）。 |
| 多角色未解析 | 逐角色生成一行；GUI 汇总时按角色在 `rules` 中的出现顺序排列，不去重、不合并。 |

> 该文本**直接显示在 GUI 状态栏**（见 §8.9），用户据提示改名或换着色器即可，无需查阅本文档。

#### 5.6.1 不变量：**「诊断建议 → 可执行」是闭环契约**（**不得**靠逐条手工核对维持）

> **INV-DIAG-CLOSURE**
>
> §5.6 诊断文本中出现的**每一个**建议后缀 `{后缀}`，**必须**同时满足：
>
> 1. 默认种子表（§4）中**存在**一条 `enabled == true`、归一化后缀等于 `{后缀}` 的规则；
> 2. 该规则的 `Role` 经 §5.3 解析后，`ParameterKey` **恰好等于**诊断文本中与它配对的那个候选键。
>
> 等价地：**默认种子表必须覆盖 `ShaderCatalog.All` 中全部可作为 P5 候选的 channel**，
> 即每个 P5 候选 channel 都存在一个「主 Token 与之精确相等」的角色，且该角色有一条种子后缀规则。
>
> **为什么这是不变量而不是一条待办**：P5 多候选降级（§5.4）把「猜错」转成了「如实不写」，
> 于是**诊断文本成为唯一的补救路径**。一旦某条建议后缀不存在或路由不到对应键，用户就陷入
> 「按提示改名 → 依然匹配不到任何规则 → 完全没写进去」的退化状态，**比原始症状更糟**，
> 因为用户已经相信自己做对了操作。这类缺口必须在 Lib 层用单测自动抓出（§11 第 12 项），不能等用户在界面上撞上。

#### 5.6.2 P5 候选 channel 闭合表（**v1.2 全集，逐行穷举，lib-dev 照此校验**）

下表枚举 `ShaderCatalog.All` 中**每一个**能被 P5 命中的 channel，并给出闭合路径。
「角色」列为该 channel 的**专用角色**（P4 精确命中）；「并入 P5 的通用角色」列说明它同时还是哪个通用角色的 P5 候选。

| # | channel（= 派生通道名） | 出现的参数键 | 所属着色器 | 专用角色（**P4 精确**） | 种子后缀 | 并入 P5 的通用角色 |
|---:|---|---|---|---|---|---|
| 1 | `TintMask` | `TextureTintMask1` | `csgo_environment` | `TintMask` | `_tintmask`（第 20 条） | `Mask`（唯一候选 → 正常命中） |
| 2 | `SelfIllumMask` | `TextureSelfIllumMask` | `csgo_vertexlitgeneric` / `csgo_complex` / `csgo_static_overlay` | `SelfIllumMask` | `_selfillummask`（第 33 条） | `Mask` |
| 3 | `DetailMask` | `TextureDetailMask` | `csgo_complex` | `DetailMask` | `_detailmask`（第 18 条） | `Mask` |
| 4 | `RimMask` | `TextureRimMask` | `csgo_character` | `RimMask` | `_rimmask`（第 31 条） | `Mask`（唯一候选 → 正常命中） |
| 5 | `LowEndCubeMap` | `TextureLowEndCubeMap` | `csgo_water_fancy` | `LowEndCubeMap` | `_lowendcubemap`（第 32 条） | `CubeMap`（唯一候选 → 正常命中） |
| 6 | `FoamNormal` | `TextureFoamNormal` | `csgo_water_fancy` | `FoamNormal` | `_foamnormal`（第 24 条） | `Normal`（3 候选 → 歧义） |
| 7 | `DebrisNormal` | `TextureDebrisNormal` | `csgo_water_fancy` | `DebrisNormal` | `_debrisnormal`（第 28 条） | `Normal`（3 候选 → 歧义） |
| 8 | `WavesNormal` | `TextureWavesNormal` | `csgo_water_fancy` | `WavesNormal` | `_wavesnormal`（第 26 条） | `Normal`（3 候选 → 歧义） |
| 9 | `WavesHeight` | `TextureWavesHeight` | `csgo_water_fancy` | `WavesHeight` | `_wavesheight`（第 34 条） | `Height`（2 候选 → 歧义） |
| 10 | `DebrisHeight` | `TextureDebrisHeight` | `csgo_water_fancy` | `DebrisHeight` | `_debrisheight`（第 35 条） | `Height`（2 候选 → 歧义） |

**本表即 INV-DIAG-CLOSURE 的判定集**：任何 `ShaderCatalog` 条目变更后，lib-dev 必须重跑 §11 第 12 项的单测；
若出现新的 P5 候选 channel 而本表无对应行，即为违反不变量。

> **`Height` 歧义是本次穷举新发现的第 4 个缺口**（captain 点名的 3 个之外）：
> `csgo_water_fancy` 的 `Height` 角色同时命中 `TextureWavesHeight` 与 `TextureDebrisHeight` 两条 P5 候选 → `AmbiguousQualified`，
> 诊断文本会建议 `_wavesheight` / `_debrisheight`，而原种子表两条都没有。已由第 34 / 35 条补齐。

> **明确不在本表范围内**：`TextureCloth`（`csgo_character`）的 channel 是 `Cloth`，
> **不以任何角色 Token 结尾**，因此它既不是 P5 候选、也不产生诊断建议，不受 INV-DIAG-CLOSURE 约束。
> 当前不为其建角色，故 `_cloth.png` 在该着色器上不写入任何参数（属 `NoMatchingKey`，与 `TextureDebrisHeight` 无关）。

---

## 6. 后缀匹配算法与路径规则

### 6.1 贴图扩展名集合（锁定）

`TexturePathRules.TextureExtensions` = `.png` `.tga` `.jpg` `.jpeg` `.bmp` `.exr` `.hdr` `.pfm` `.dds` `.vtex` `.vtf` `.tif` `.tiff`（比较用 `OrdinalIgnoreCase`）。
`.vmat` **不在**其中（否则 D2 会被误判为 D3）。

### 6.2 归一化

```
NormalizeName(s) =
  1. Path.GetFileNameWithoutExtension(s)        // 去文件类型扩展名
  2. ToLowerInvariant()                          // 大小写不敏感
  3. 依次把 '_'、'-'、' '、'.' 替换为 '_'        // 分隔符归一化
  4. 把连续多个 '_' 折叠为一个 '_'
  5. Trim('_')                                  // 去首尾下划线
```
规则后缀同样走 `NormalizeName`（`_Normal.png` 之类用户输入也安全）。

示例：`Concrete-Wall _N.png` → `concrete_wall_n`；`_Ambient Occlusion` → `ambient_occlusion`。

### 6.3 匹配规则

对归一化文件名 `n` 与每条 `enabled == true` 的规则（后缀归一化为 `s`）：

| 序 | 规则 | 判定 | 命中标记 |
|---:|---|---|---|
| 1 | `s` 为空 | `string.IsNullOrEmpty(s)` | **跳过该规则**（非法规则） |
| 2 | **整名相等** | `n == s` | `IsWholeNameMatch = true` |
| 3 | **尾部边界匹配** | `n.Length > s.Length && n.EndsWith("_" + s)` | `IsWholeNameMatch = false` |

> 边界必须是下划线：`concrete_normal` **不**会命中规则 `n`（因为它的末尾不是 `_n`），
> `stone_n` 才会命中规则 `n`。这保证短后缀不会误吞长单词。

### 6.4 多规则命中裁定（最长后缀优先）

| 序 | 判据 |
|---:|---|
| 1 | **命中后缀归一化长度降序**（`normal` > `n`，`ambient_occlusion` > `ao`） |
| 2 | 命中类型：`IsWholeNameMatch == true` 优先于边界匹配 |
| 3 | 规则在 `rules` 数组中的**原始下标升序** |
| 4 | `string.CompareOrdinal(MatchedSuffix)` 升序 |

取第一条 → `TextureSuffixMatch.Role`。**全部规则均未命中** → v1.x：`Role = TextureRole.Unknown`，文件进入 `UnassignedFiles`，不写入任何参数。**v2.0 起（用户裁定「未命中材质默认使用 color 槽位」）**：未命中的**贴图**默认按 `TextureRole.Color` 参与分配，合成条目 `MatchedSuffix = ""`、`MatchedSuffixLength = 0`，因而在 §6.5 冲突裁决中必然让位给真后缀命中者（补位不抢位）；`UnassignedFiles` 只剩非贴图文件与 `roleOverrides` 显式排除（`Unknown`）两类。目标着色器无 Color 可解析键时按 §5.6 `NoMatchingKey` 落 `UnresolvedRoles`（已跳过）。非贴图文件与显式排除的行为不变。

### 6.5 多贴图命中同一角色的冲突处理

一批输入内多个文件解析出同一 `TextureRole` 时，按下列全序取**唯一胜者**，其余记入 `Conflicts`：

| 序 | 判据 |
|---:|---|
| 1 | 命中后缀归一化长度降序（更具体的规则胜） |
| 2 | `IsWholeNameMatch == true` 优先 |
| 3 | `Path.GetFileNameWithoutExtension(file)` 的 `string.CompareOrdinal` **升序** |
| 4 | 完整路径的 `string.CompareOrdinal` **升序** |

被淘汰文件的 `Reason` 固定为 `"同槽位冲突（{role}），已保留 {胜者文件名}"`。
**冲突绝不覆盖已写入的值**；只有胜者会被写入。

### 6.6 最终写入 VMAT 的路径规则（锁定）

```
ToVmatPath(filePath, textureRoot):
  if textureRoot 非空 且 Directory.Exists(textureRoot):
      rootFull = Path.GetFullPath(textureRoot)
      fileFull = Path.GetFullPath(filePath)
      rel      = Path.GetRelativePath(rootFull, fileFull)
      若 rel 不是根路径 且 不以 ".." 开头 且 !Path.IsPathRooted(rel):
          return rel.Replace('\\', '/')          // ← 相对路径 + 统一正斜杠
  return filePath                                 // ← 根目录外：原样写回
```

| 情形 | 写入值 |
|---|---|
| `textureRoot = D:/art/backrooms/textures`，文件 `D:/art/backrooms/textures/wall/concrete_normal.png` | `wall/concrete_normal.png` |
| 同上，文件 `D:/art/backrooms/textures/normal.png`（贴图根目录**正下方**） | `normal.png`（`GetRelativePath` 不加 `./`） |
| 同上，文件 `D:/art/backrooms/materials/concrete/concrete_normal.png` | `D:/art/backrooms/materials/concrete/concrete_normal.png`（**原路径**，反斜杠原样保留） |
| `textureRoot` 为空 / 目录不存在 / 访问被拒 | 一律 `filePath`（原路径） |
| 文件恰在 `textureRoot` 本身（等于根目录） | 视为目录，不在贴图扩展名集合内，不会进入本流程 |

> **备注**：只有「根目录内」的相对路径才做 `\` → `/` 归一；根目录外的原路径按验收要求**不做**斜杠改写，用户可据此一眼看出路径不在贴图根下。
> `Path.GetRelativePath` 返回的 `..\..\` 开头即判定为「根目录外」，从而避免把 `..` 写进 VMAT。

---

## 7. Lib 公共 API 精确签名（逐字锁定）

> 命名空间统一 `Lib`；文件作用域命名空间（`namespace Lib;`）与现有代码一致；
> `Lib.csproj` 目标 `net10.0` + `Nullable enable` + `ImplicitUsings enable`，**无需新增 PackageReference**
> （`System.Text.Json` 为 net10.0 内置）。

### 7.1 `Lib/TextureRole.cs`

```csharp
namespace Lib;

/// <summary>与具体着色器无关的贴图语义槽位（详见 docs/feature-dragdrop-suffix-spec.md §5.2）。</summary>
public enum TextureRole
{
    Unknown = 0,
    Color, Normal, Roughness, Metalness, AmbientOcclusion, Height,
    Translucency, Detail, Mask, Emissive, CubeMap, Lightmap,
    FoamMask, FoamNormal, WavesMask, WavesNormal, WavesHeight,
    DebrisColor, DebrisNormal, DebrisHeight,
    TintMask, DetailMask, SelfIllumMask, RimMask, LowEndCubeMap,
}
```

### 7.2 `Lib/TextureRoleTokens.cs`

```csharp
namespace Lib;

public static class TextureRoleTokens
{
    public static IReadOnlyList<TextureRole> AllRoles { get; }

    public static IReadOnlyList<string> TokensOf(TextureRole role);
    public static string PrimaryTokenOf(TextureRole role);
    public static string Describe(TextureRole role);
    public static bool TryParseRole(string? text, out TextureRole role);
}
```

### 7.3 `Lib/TextureSuffixRule.cs`

```csharp
namespace Lib;

public sealed class TextureSuffixRule
{
    public TextureSuffixRule() { }
    public TextureSuffixRule(string suffix, TextureRole role, bool enabled = true);

    public string Suffix { get; set; }
    public TextureRole Role { get; set; }
    public bool Enabled { get; set; }
}
```

### 7.4 `Lib/TextureSuffixMatcher.cs`

```csharp
namespace Lib;

public sealed class TextureSuffixMatch
{
    public string FilePath { get; }
    public string FileNameWithoutExtension { get; }
    public string NormalizedName { get; }
    public TextureRole Role { get; }
    public string MatchedSuffix { get; }
    public int MatchedSuffixLength { get; }
    public bool IsWholeNameMatch { get; }
}

public sealed class TextureSuffixMatcher
{
    public TextureSuffixMatcher(IEnumerable<TextureSuffixRule> rules);

    public IReadOnlyList<TextureSuffixRule> Rules { get; }

    public TextureSuffixMatch? Match(string filePath);
    public IReadOnlyList<TextureSuffixMatch> MatchAll(IEnumerable<string> filePaths);

    public static string NormalizeName(string name);
}
```

### 7.5 `Lib/TextureRoleResolver.cs`

```csharp
namespace Lib;

public enum TextureKeyTier
{
    None = 0,
    ExactName = 1,
    ExactNameNumbered = 2,
    LayerPrefixed = 3,
    DerivedChannelEqual = 4,
    QualifiedComposite = 5,
}

/// <summary>角色未解析的原因（详见 §5.3 / §5.6）。</summary>
public enum TextureResolveFailure
{
    /// <summary>已命中（P1–P5 任一档）。</summary>
    None = 0,
    /// <summary>P1–P5 皆无合格候选 —— 该模板没有对应槽位。</summary>
    NoMatchingKey,
    /// <summary>仅 P5：合格候选 ≥ 2 且互相并列，拒绝猜测，不写入。</summary>
    AmbiguousQualified,
}

public sealed class TextureKeyCandidate
{
    public string ParameterKey { get; }
    public TextureRole Role { get; }
    public TextureKeyTier Tier { get; }
    public string DerivedChannel { get; }
    public string? Qualifier { get; }
    public int LayerNumber { get; }
    public int TrailingNumber { get; }
    public int ParameterIndex { get; }
}

public sealed class TextureRoleResolution
{
    public TextureRole Role { get; }

    /// <summary>等价于 <c>UnresolvedReason == TextureResolveFailure.None</c>。</summary>
    public bool IsResolved { get; }

    /// <summary>命中时的参数键；未解析时为 null。</summary>
    public string? ParameterKey { get; }

    public TextureKeyTier Tier { get; }

    /// <summary>
    /// 该角色的全部合格候选。
    /// · IsResolved == true → 长度为 1（仅 P5 唯一候选场景）或已按 §5.4 排序的完整候选集（P1–P4）。
    /// · UnresolvedReason == AmbiguousQualified → 长度 ≥ 2，全量保留供诊断文本使用，<b>不得</b>截断。
    /// · UnresolvedReason == NoMatchingKey → 长度为 0。
    /// </summary>
    public IReadOnlyList<TextureKeyCandidate> Candidates { get; }

    public TextureResolveFailure UnresolvedReason { get; }

    /// <summary>
    /// 未解析时的诊断文本，按 §5.6 的模板组装；已解析时为空串。
    /// 由本类型内部完成组装，GUI 只负责原样显示，<b>不得</b>在 GUI 侧二次拼接。
    /// </summary>
    public string DiagnosticText { get; }
}

public static class TextureRoleResolver
{
    public static IReadOnlyList<string> TextureParameterKeys(ShaderTemplate shader);
    public static string DeriveChannel(string parameterKey);
    public static IReadOnlyList<TextureKeyCandidate> ResolveCandidates(ShaderTemplate shader, TextureRole role);
    public static TextureRoleResolution Resolve(ShaderTemplate shader, TextureRole role);
    public static IReadOnlyDictionary<TextureRole, TextureRoleResolution> ResolveAll(ShaderTemplate shader, IEnumerable<TextureRole> roles);

    /// <summary>按 §5.6 组装诊断文本；供单测与 GUI 复用。</summary>
    public static string BuildDiagnosticText(TextureRole role, TextureResolveFailure reason, IReadOnlyList<TextureKeyCandidate> candidates);
}
```

> **`AmbiguousQualified` 的类型承载**：多候选列表由 `TextureRoleResolution.Candidates`（`IReadOnlyList<TextureKeyCandidate>`）承载，
> 失败分类由 `TextureRoleResolution.UnresolvedReason`（`TextureResolveFailure` 枚举）承载，
> 成句后的提示由 `TextureRoleResolution.DiagnosticText`（`string`）承载。三者均已在上述签名中给出，**无需**新增类型。
> `TextureAssignResult.UnresolvedRoles`（`IReadOnlyList<TextureRoleResolution>`）直接把整个对象透传给 GUI。

### 7.6 `Lib/TexturePathRules.cs`

```csharp
namespace Lib;

public static class TexturePathRules
{
    public static IReadOnlyList<string> TextureExtensions { get; }
    public static bool IsTextureFile(string path);
    public static string? TryGetRelativeVmatPath(string filePath, string? textureRoot);
    public static string ToVmatPath(string filePath, string? textureRoot);
    public static string CommonParentDirectory(IEnumerable<string> filePaths);
}
```

### 7.7 `Lib/TextureAssignment.cs`

```csharp
namespace Lib;

public sealed class TextureAssignment
{
    public string FilePath { get; }
    public string VmatPath { get; }
    public TextureRole Role { get; }
    public string ParameterKey { get; }
    public string MatchedSuffix { get; }
}

public sealed class TextureConflict
{
    public TextureRole Role { get; }
    public string KeptFilePath { get; }
    public IReadOnlyList<string> DroppedFilePaths { get; }
    public string Reason { get; }
}

public sealed class TextureAssignResult
{
    public IReadOnlyList<TextureAssignment> Assignments { get; }
    public IReadOnlyList<TextureConflict> Conflicts { get; }
    public IReadOnlyList<string> UnassignedFiles { get; }
    public IReadOnlyList<TextureRoleResolution> UnresolvedRoles { get; }
    public int TotalFiles { get; }
    public string? SuggestedTextureRoot { get; }
}

public static class TextureAssigner
{
    public static TextureAssignResult Assign(
        ShaderTemplate shader,
        IEnumerable<string> textureFilePaths,
        IReadOnlyList<TextureSuffixRule> rules,
        string? textureRoot);
}
```

### 7.8 `Lib/DropImportService.cs`

```csharp
namespace Lib;

public enum DropCategory
{
    None = 0,
    MaterialFolder,
    VmatFile,
    TextureFiles,
    TextureOnlyFolder,
    Mixed,
    Unsupported,
}

public sealed class DropAnalysis
{
    public DropCategory Category { get; }
    public IReadOnlyList<string> VmatFiles { get; }
    public IReadOnlyList<string> TextureFiles { get; }
    public IReadOnlyList<string> OtherFiles { get; }
    public string? MaterialRootCandidate { get; }
    public string? TextureRootCandidate { get; }
    public bool RequiresRecursion { get; }
    public string Summary { get; }
}

public sealed class DropImportService
{
    public DropImportService(MaterialScanner scanner, bool recurseTextureFolders);

    public DropAnalysis Analyze(IReadOnlyList<string> droppedPaths);

    /// <summary>
    /// 判断一次拖放是否可导入。<paramref name="fileDropPaths"/> 是 GUI 层
    /// 从拖放数据中取出的路径数组；GUI 侧的提取与转换见规格 §2.3 的 FileDropOf。
    /// 本方法只接收纯字符串路径，不感知任何 GUI / 拖放框架类型。
    /// </summary>
    public static bool CanAccept(string[]? fileDropPaths);
}
```

> ⚠️ **签名约束（v1.3 修正）**：`CanAccept` **只接受 `string[]?`，不得引入 `System.Windows.IDataObject`**。
> `IDataObject` 是 WPF 类型，而 `Lib` 的 TFM 为 `net10.0`（非 `-windows`），引入即违反 t2 的 outOfScope
> 「不得引入任何 WPF / Windows 依赖」。**`IDataObject → string[]` 的转换只发生在 GUI 层**（`MainWindow.FileDropOf`）。
> v1.2 曾把该参数误写为 `IDataObject?`，已更正；lib-dev 当前落盘的 `CanAccept(string[]? fileDropPaths)` 是**正确**的，
> **不得要求其改回**。

### 7.9 `Lib/VmatGeneratorSettings.cs` + `Lib/VmatGeneratorSettingsStore.cs`

```csharp
namespace Lib;

public sealed class VmatGeneratorSettings
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultShaderNameValue = "csgo_environment.vfx";

    public VmatGeneratorSettings() { }

    public static string SettingsDirectoryPath { get; }
    public static string SettingsFilePath { get; }

    public int SchemaVersion { get; set; }
    public string DefaultShaderName { get; set; }
    public string TextureRoot { get; set; }
    public bool AutoAssignOnDrop { get; set; }
    public bool RecurseTextureFolders { get; set; }
    public bool AdoptDroppedVmatFolderAsMaterialsRoot { get; set; }

    public List<TextureSuffixRule> Rules { get; set; }

    public static VmatGeneratorSettings CreateDefault();
    public TextureSuffixRule? FindRule(string suffix);
}

public enum SettingsLoadOutcome
{
    Defaulted,
    Loaded,
    Repaired,
    RecoveredFromCorruption,
}

public sealed class SettingsLoadReport
{
    public SettingsLoadOutcome Outcome { get; }
    public bool LoadedFromDisk { get; }
    public string? ErrorMessage { get; }
    public string? BackupFilePath { get; }
    public IReadOnlyList<string> RepairedFields { get; }
}

public sealed class SettingsSaveReport
{
    public bool Success { get; }
    public string FilePath { get; }
    public string? ErrorMessage { get; }
}

public static class VmatGeneratorSettingsStore
{
    public static VmatGeneratorSettings? Load(out SettingsLoadReport report);
    public static VmatGeneratorSettings LoadOrDefault();
    public static VmatGeneratorSettings Sanitize(VmatGeneratorSettings raw, out IReadOnlyList<string> repairedFields);
    public static void RestoreDefaultRules(VmatGeneratorSettings settings);
    public static SettingsSaveReport Save(VmatGeneratorSettings settings);
}
```

---

## 8. GUI 契约（逐字锁定）

### 8.1 新增文件清单

| 路径 | 内容 |
|---|---|
| `GUI/QuickNavWindow.xaml` | 快速导航与后缀规则配置窗口；`Title="贴图后缀快速导航"`，`Width="860" Height="620"`，`WindowStartupLocation="CenterOwner"`，DataContext 由代码设置（非 XAML 声明）。 |
| `GUI/QuickNavWindow.xaml.cs` | `public partial class QuickNavWindow : Window`；唯一构造函数 `public QuickNavWindow(GUI.ViewModels.QuickNavViewModel viewModel)`，内部 `DataContext = viewModel;` |
| `GUI/ViewModels/QuickNavViewModel.cs` | 见 §8.3 |
| `GUI/ViewModels/TextureSuffixRuleViewModel.cs` | 见 §8.4 |

### 8.2 修改文件清单

| 路径 | 必须改动 |
|---|---|
| `GUI/MainWindow.xaml` | ① `<Window>` 根节点加 `AllowDrop="True" DragOver="OnWindowDragOver" DragLeave="OnWindowDragLeave" Drop="OnWindowDrop"`；② 新增「工具(_T)」菜单项 `Click="OnOpenQuickNavClicked" InputGestureText="Ctrl+Q"`，**并**新增工具栏按钮 `Command="{Binding OpenQuickNavCommand}"`（两者分属**不同**控件，见下方 WPF 约束）；③ 状态栏 `TextBlock Text="{Binding StatusText}"` **保持不变**（拖拽提示直接复用该属性）。 |
| `GUI/MainWindow.xaml.cs` | ① `private MainViewModel ViewModel` → **`public MainViewModel ViewModel`**；② 新增 `OnWindowDragOver` / `OnWindowDrop` / `OnWindowDragLeave` / `OnOpenQuickNavClicked` 四个方法（签名见 §8.5）；③ 新增 `private static string[]? FileDropOf(IDataObject data)` 辅助方法（见 §2.3）。 |
| `GUI/ViewModels/MainViewModel.cs` | ① `private void LoadFolder(string folder)` → **`public void LoadFolder(string folder)`**；② 新增 `Settings` / `QuickNav` / `LastDropResult` 三个成员与 `OpenQuickNav` / `HandleDroppedPaths` 两个 `[RelayCommand]` 方法（见 §8.6）。 |
| `GUI/ViewModels/ParameterRowViewModel.cs` | ① `VectorParameter` 新增 `RawText` 成员与「RawText 非空则改走原文输出」的 `WriteValue` 分支（契约见 §8.10，**已批准范围内**）。 |
| `GUI/ViewModels/ShaderEditorViewModel.cs` | ① 新增 `public ShaderTemplate? CurrentShader { get; }`（返回 `_shader`）；② 新增 `public bool TrySetTextureValue(string parameterKey, string vmatPath)`；③ `BuildRow` 的 `case ShaderParamKind.Vector:` 改为「`Vector4.TryParse` 失败时把 `raw` 原文存入 `VectorParameter.RawText`」，修复既有丢值缺陷（见 §8.10）。 |
| `GUI/GUI.csproj` | **无需改动**（`System.Text.Json` 由 net10.0 内置提供）。 |

#### WPF 约束：同一控件**不得**同时设置 `Click` 与 `Command`（**v1.3 新增，锁定**）

`ButtonBase`（含 `Button` / `MenuItem`）在**同一个控件上**同时设置 `Click` 处理器与 `Command` 时，
**两者会各自独立触发** —— 命令执行一次、`Click` 事件再冒泡执行一次，表现为「一次点击弹出两个窗口 / 执行两遍动作」。
WPF 不会因为其中一个已处理就跳过另一个。

因此快速导航的**两个入口必须分属不同控件**：

| 入口 | 控件 | 绑定方式 | 理由 |
|---|---|---|---|
| 菜单项 | `<MenuItem Header="贴图后缀快速导航(_Q)…" Click="OnOpenQuickNavClicked" InputGestureText="Ctrl+Q" />` | **`Click`**（code-behind） | 与既有菜单项风格一致（现有菜单项多用 `Click`） |
| 工具栏按钮 | `<Button Command="{Binding OpenQuickNavCommand}" …>` | **`Command`**（ViewModel） | 与既有工具栏按钮风格一致（现有按钮全用 `Command`） |

> **本约束适用于本项目所有控件**，不限于快速导航：任何控件上 `Click` 与 `Command` **二选一**。
> 后续维护者**不得**为了「统一风格」把两者加到同一控件上。

#### 快捷键 `Ctrl+Q`（v1.3 授权新增）

| 项 | 值 |
|---|---|
| 绑定 | 「工具(_T)」菜单项的 `InputGestureText="Ctrl+Q"` |
| 冲突核对 | 与现有手势 `Ctrl+N`（新建）/ `Ctrl+O`（打开文件夹）/ `Ctrl+S`（保存）/ `Ctrl+Shift+S`（另存为）/ `Ctrl+C`（复制）/ `F5`（刷新）**均不冲突** |
| 归属 | 快速导航（打开 `QuickNavWindow`） |

> 现有手势全部是**仅显示**的 `InputGestureText`（未注册 `InputBindings`），因此 `Ctrl+Q` 同样只需显示，
> 不额外注册全局快捷键。

### 8.3 `QuickNavViewModel` 公开成员（XAML 绑定名 = 下表成员名）

```csharp
namespace GUI.ViewModels;

public sealed partial class QuickNavViewModel : ObservableObject
{
    public QuickNavViewModel(MainViewModel parent);

    // ── 着色器 ────────────────────────────────────────────────
    public IReadOnlyList<ShaderTemplate> Shaders { get; }          // = ShaderCatalog.All
    public ShaderTemplate? SelectedShader { get; set; }            // [ObservableProperty] SelectedShader

    // ── 配置 ──────────────────────────────────────────────────
    public string DefaultShaderName { get; set; }
    public string TextureRoot { get; set; }
    public bool AutoAssignOnDrop { get; set; }
    public bool RecurseTextureFolders { get; set; }
    public bool AdoptDroppedVmatFolderAsMaterialsRoot { get; set; }

    public ObservableCollection<TextureSuffixRuleViewModel> Rules { get; }
    public TextureSuffixRuleViewModel? SelectedRule { get; set; }

    public ObservableCollection<string> KnownRoles { get; }        // = TextureRoleTokens.AllRoles 的枚举名字符串
    public string SettingsPath { get; }                            // 只读 = VmatGeneratorSettings.SettingsFilePath
    public string StatusMessage { get; set; }
    public string PreviewLines { get; set; }                       // 只读预览

    // ── 命令（CommunityToolkit [RelayCommand] 生成物）──────────────
    IRelayCommand                BrowseTextureRootCommand   { get; } // BrowseTextureRoot()
    IRelayCommand                SaveSettingsCommand        { get; } // SaveSettings()
    IRelayCommand                ReloadSettingsCommand      { get; } // ReloadSettings()
    IRelayCommand                ResetRulesCommand          { get; } // ResetRules()
    IRelayCommand                AddRuleCommand             { get; } // AddRule()
    IRelayCommand<TextureSuffixRuleViewModel?> RemoveRuleCommand     { get; } // RemoveRule(TextureSuffixRuleViewModel? rule)
    IRelayCommand<TextureSuffixRuleViewModel?> MoveRuleUpCommand     { get; } // MoveRuleUp(TextureSuffixRuleViewModel? rule)
    IRelayCommand<TextureSuffixRuleViewModel?> MoveRuleDownCommand   { get; } // MoveRuleDown(TextureSuffixRuleViewModel? rule)
    IRelayCommand                TestDropCommand            { get; } // TestDrop()
    IRelayCommand                RefreshPreviewCommand      { get; } // RefreshPreview()
    IRelayCommand                CloseCommand               { get; } // Close()
}
```

| XAML 绑定目标 | 绑定表达式 |
|---|---|
| 着色器下拉 | `ItemsSource="{Binding Shaders}" SelectedItem="{Binding SelectedShader, Mode=TwoWay}"` |
| 规则表 | `ItemsSource="{Binding Rules}" SelectedItem="{Binding SelectedRule, Mode=TwoWay}"` |
| 每行角色下拉 | `ItemsSource="{Binding DataContext.KnownRoles, RelativeSource={RelativeSource AncestorType=Window}}"` |
| 保存按钮 | `Command="{Binding SaveSettingsCommand}"` |

`PreviewLines` 内容格式（每行一条角色解析结果，用于「所见即所得」校验）。
**「参数键」列必须区分两种未解析原因**（对应 `TextureResolveFailure`），禁止一律写成「未解析」：

```
角色                档位  参数键                      当前着色器
Normal              P2    TextureNormal1              D:/tex/wall/concrete_normal.png
Color               P2    TextureColor1               （未设置）
Roughness           —     无对应键（NoMatchingKey）    —
Detail              —     多候选并列，不写入（AmbiguousQualified）
                              候选：TextureFoamNormal / TextureDebrisNormal / TextureWavesNormal
```

| `UnresolvedReason` | 「参数键」列显示 | 是否换行展开候选 |
|---|---|---|
| `None`（已命中） | 参数键名（如 `TextureNormal1`） | 否 |
| `NoMatchingKey` | `无对应键（NoMatchingKey）` | 否 |
| `AmbiguousQualified` | `多候选并列，不写入（AmbiguousQualified）` | **是**，次行缩进显示 `候选：{key1} / {key2} / …` |

候选顺序与 §5.6 一致（`ParameterIndex` 升序、用 `" / "` 连接），由 `TextureRoleResolver.BuildDiagnosticText` 产出，GUI 不得自行排序。

### 8.4 `TextureSuffixRuleViewModel` 公开成员

```csharp
namespace GUI.ViewModels;

public sealed partial class TextureSuffixRuleViewModel : ObservableObject
{
    public TextureSuffixRuleViewModel();
    public TextureSuffixRuleViewModel(string suffix, TextureRole role, bool enabled);

    public string Suffix { get; set; }        // [ObservableProperty]
    public string Role { get; set; }          // [ObservableProperty] —— 存枚举名字符串
    public bool Enabled { get; set; }         // [ObservableProperty]

    public string NormalizedSuffix { get; }   // 只读 = TextureSuffixMatcher.NormalizeName(Suffix)
    public string RoleDisplay { get; }        // 只读 = TextureRoleTokens.Describe(...)

    public TextureSuffixRule ToRule();
    public static TextureSuffixRuleViewModel FromRule(TextureSuffixRule rule);
}
```

### 8.5 `MainWindow.xaml.cs` 拖拽处理方法名（锁定）

```csharp
private void OnWindowDragOver(object sender, DragEventArgs e);
private void OnWindowDrop(object sender, DragEventArgs e);
private void OnWindowDragLeave(object sender, DragEventArgs e);
private void OnOpenQuickNavClicked(object sender, RoutedEventArgs e);
```

`OnOpenQuickNavClicked` 方法体契约（**v1.3：仅由菜单项 `Click` 绑定，工具栏按钮走 `Command`，二者不得同控件**）：

```csharp
ControlErrorRecorder.GuardWithDialog("打开贴图后缀快速导航", sender,
    () => new QuickNavWindow(ViewModel.QuickNav) { Owner = this }.ShowDialog());
```

| 入口控件 | 绑定 | 触达路径 |
|---|---|---|
| 菜单项 `<MenuItem Header="贴图后缀快速导航(_Q)…" InputGestureText="Ctrl+Q" Click="OnOpenQuickNavClicked" />` | `Click` → `OnOpenQuickNavClicked` | 直接调本方法体 |
| 工具栏 `<Button Command="{Binding OpenQuickNavCommand}" …>` | `Command` → `MainViewModel.OpenQuickNav`（`[RelayCommand]`） | ViewModel 层打开，**不经过** `OnOpenQuickNavClicked` |

> 两条路径最终都打开同一个 `QuickNavWindow(ViewModel.QuickNav)`，但**同一控件上不得同时使用 `Click` 与 `Command`**
> （原因见 §8.2 的 WPF 约束）。`OpenQuickNavCommand` 仍须保留在 §8.6 的公开契约中，供工具栏按钮绑定。

### 8.6 `MainViewModel` 新增成员（锁定）

```csharp
public VmatGeneratorSettings Settings { get; }          // 构造时 = VmatGeneratorSettingsStore.LoadOrDefault()
public QuickNavViewModel QuickNav { get; }
public TextureAssignResult? LastDropResult { get; set; }

[RelayCommand] public void OpenQuickNav();
[RelayCommand] public void HandleDroppedPaths(IReadOnlyList<string> paths);   // → HandleDroppedPathsCommand
```

> `[RelayCommand] public void HandleDroppedPaths(IReadOnlyList<string> paths)` 生成
> `IRelayCommand<IReadOnlyList<string>> HandleDroppedPathsCommand`。

### 8.7 `ShaderEditorViewModel` 新增成员（锁定）

```csharp
/// <summary>当前载入的着色器模板；未载入时为 null。</summary>
public ShaderTemplate? CurrentShader { get; }

/// <summary>
/// 在 ParameterRows 中查找 parameterKey 对应行并写入 vmatPath。
/// 仅当该行存在、键名匹配且行类型为 TextureParameter 或 VectorParameter 时写入。
/// 写入成功返回 true；键不存在 / 类型不匹配 / 值为空返回 false（不抛异常）。
/// </summary>
public bool TrySetTextureValue(string parameterKey, string vmatPath);
```

> `TrySetTextureValue` 内部直接给 `TextureParameter.Value` 赋值，触发既有
> `ParameterRowViewModel.PropertyChanged` → `ShaderEditorViewModel.RebuildText()` → KV 预览刷新，
> **无需新增任何刷新代码**。

### 8.8 `HandleDroppedPaths` 实现契约（逐条）

```
1. if paths 为空或全为空白 → StatusText = D6 文案；return
2. analysis = new DropImportService(App.Scanner, Settings.RecurseTextureFolders).Analyze(paths)
3. switch (analysis.Category)
     Unsupported / None → StatusText = D6 文案；return
     MaterialFolder / VmatFile / Mixed →
         root = analysis.MaterialRootCandidate
         if root != null 且 Settings.AdoptDroppedVmatFolderAsMaterialsRoot 且 root != MaterialsFolder:
             LoadFolder(root)                       // 公开方法，内部已 Guard
         if analysis.VmatFiles.Count > 0:
             在 _allDocs / ShaderGroups 中定位该文件 → EditMaterial(entry)
     TextureFiles / TextureOnlyFolder / Mixed → 继续第 4 步
4. if Settings.AutoAssignOnDrop == false:
         StatusText = "已按设置跳过贴图自动写入（autoAssignOnDrop = false）。"; return
5. if analysis.TextureFiles.Count == 0 → return
6. shader = Editor.CurrentShader ?? ShaderCatalog.Find(Settings.DefaultShaderName)
   if shader == null → StatusText = "未找到着色器 '{DefaultShaderName}'，无法自动写入贴图。"; return
   if Editor.CurrentShader == null → Editor.LoadFromTemplate(shader, document: null)
7. result = TextureAssigner.Assign(shader, analysis.TextureFiles, Settings.Rules, Settings.TextureRoot)
8. foreach (a in result.Assignments): Editor.TrySetTextureValue(a.ParameterKey, a.VmatPath)
9. if string.IsNullOrEmpty(Settings.TextureRoot) 且 analysis.TextureRootCandidate != null:
       Settings.TextureRoot = analysis.TextureRootCandidate      // §2.2 的「改变贴图根目录」
10. LastDropResult = result
11. StatusText = 按 §2.2 对应行的文案模板组装
    11a. if result.Conflicts.Count > 0 → 追加 " ⚠ {n} 张贴图冲突未写入。"
    11b. if result.UnresolvedRoles 中存在 UnresolvedReason == AmbiguousQualified →
         追加 " " + 该角色集合的诊断摘要（见 §8.9），用 "；" 连接
```

### 8.9 未解析提示的状态栏展示（**锁定，区分两种原因**）

`StatusText` **必须**按 `TextureResolveFailure` 区分两类未解析，不得合并成一句话：

| 情形 | 状态栏追加文本 | 前缀 |
|---|---|---|
| 存在 `AmbiguousQualified`（P5 多候选，拒绝猜测） | 该角色逐个输出 `TextureRoleResolution.DiagnosticText`（§5.6 模板），多个角色用 `"；"` 连接 | `⚠ ` |
| 仅存在 `NoMatchingKey`（模板无此槽位） | `有 {n} 个槽位在该着色器中没有对应贴图参数，已跳过。` | `ℹ ` |
| 两者都有 | 先 `AmbiguousQualified` 行，再 `NoMatchingKey` 行，`\n` 换行 | 各自前缀 |

判定口径：

| 项 | 规定 |
|---|---|
| 取值来源 | **只**从 `TextureAssignResult.UnresolvedRoles` 读取；GUI 不得自行调用 `TextureRoleResolver` 重新判定 |
| 文本来源 | `AmbiguousQualified` 分支**必须**原样使用 `DiagnosticText`；**禁止**在 GUI 侧二次拼接、截断或重新排序候选 |
| 顺序 | 按 `result.UnresolvedRoles` 的原始顺序（即 §5.6 规定的角色在 `rules` 中的出现顺序） |
| 换行 | 状态栏 `TextBlock` 保持单行（现有布局），`\n` 会被 WPF 折叠为空格 —— 故 §5.6 的诊断模板**不得**含 `\n` |

示例（`csgo_water_fancy` 拖入 `_normal` / `_color` 两张贴图）：

```
⚠ 该着色器没有通用的 法线 槽位，候选为 TextureFoamNormal / TextureDebrisNormal / TextureWavesNormal；请改用 _foamnormal / _debrisnormal / _wavesnormal 后缀，或改选其他着色器。
ℹ 有 1 个槽位在该着色器中没有对应贴图参数，已跳过。
```

### 8.10 `VectorParameter.RawText` 契约（**v1.3 批准 · 显式范围决策**）

> **本节是已批准的范围内改动，不是越界。** integrator 与 reviewer 据此评估：
> `ParameterRowViewModel.cs` / `ShaderEditorViewModel.BuildRow` 的下列改动**在范围内**，不应按「修改既有行为」质疑。

#### 8.10.1 为什么必须做（三条理由，任一即成立）

| # | 理由 | 说明 |
|---:|---|---|
| 1 | **验收场景必需** | `TextureRoughness1`（`csgo_environment`）、`TextureFoamNormal`（`csgo_water_fancy`）是 `Kind == ShaderParamKind.Vector` + `Shape == TextureOrVector`。它们在参数编辑器里走 `VectorParameter`（四个数字 `TextBox`），**无法承载路径字符串**。没有 `RawText`，§9 的 **S1 / S6** 拖拽写入会**静默失效**。 |
| 2 | **修复既有数据丢失缺陷** | 既有 `ShaderEditorViewModel.BuildRow` 中 `case ShaderParamKind.Vector: return new VectorParameter(p, ParseVector(raw));`，而 `ParseVector` 在解析失败时返回 `default`（`[0 0 0 0]`）。**修复前的历史事实**：打开任何既有 `.vmat`，只要该键存的是贴图路径，值就会被丢弃并改写成 `[0 0 0 0]`。<br>**本修复的范围**（v1.5 依实测收窄）：该丢值缺陷发生在 **`Shape == TextureOrVector`** 的键上 —— 拖拽导入唯一会写入贴图路径的键类型，也正是本功能实际触及的那一类。守卫条件与修复范围**严格一致**。 |
| 3 | **兑现早已声明却从未实现的契约** | `ShaderValueShape.TextureOrVector` 此前在 `Lib/ShaderTemplate.cs` 已声明、在 `Lib/ShaderCatalog.cs` 已赋值，但**在 GUI 侧从未被任何代码读取**——是个死标记。`RawText` 让它第一次真正生效。 |

> **修复范围声明（勿读作「该缺陷从未存在」）**：理由 2 陈述的是**修复前真实发生过的行为**，该事实不因收窄范围而消失。
>
> **不在本修复范围内**：**真·向量参数**（`Shape != TextureOrVector`，如 `g_vColorTint` / `g_vTexCoord*` / `g_vWater*` 等约 67 个键）。
> 它们的值**本就应当是 vec4**，写入路径字符串属于误用而非本功能场景；这些键在修复后仍按 `ParseVector` 的行为处理。
> 若将来需要为真·向量参数也提供「路径原文」承载能力，应另开契约，**不得**通过删掉下方 §8.10.2 的守卫条件来顺带达成 —— 那会**放宽**行为，把所有 vec4 参数都暴露在原文透传语义下。

#### 8.10.2 契约（逐条锁定）

```csharp
// GUI/ViewModels/ParameterRowViewModel.cs —— VectorParameter 内
/// <summary>
/// 非空时，本行按「原文透传」语义工作：<see cref="WriteValue"/> 原样输出该文本，
/// 且 X / Y / Z / W 四个分量的修改不再回写 Value。
/// 为空（默认）时行为与既有实现完全一致。
/// </summary>
[ObservableProperty]
private string _rawText = string.Empty;

public override void WriteValue(IDictionary<string, string> target)
{
    if (!string.IsNullOrEmpty(RawText))
    {
        target[Key] = RawText;          // 原样输出，不格式化
        return;
    }
    target[Key] = new Vector4((float)X, (float)Y, (float)Z, (float)W).ToString();
}
```

```csharp
// GUI/ViewModels/ShaderEditorViewModel.cs —— BuildRow 内
case ShaderParamKind.Vector:
    if (Vector4.TryParse(raw, out var v4))
        return new VectorParameter(p, v4);          // 正常路径：既有效率行为

    // 守卫条件：只有「常量 vec4 ↔ 贴图路径」这类键才走原文透传。
    // ⚠ 删掉这个条件会【放宽行为】——所有 Shape != TextureOrVector 的真·向量参数
    //    也会被纳入原文透传范围（详见 §8.10.1 修复范围声明）。
    if (p.Shape != ShaderValueShape.TextureOrVector)
        return new VectorParameter(p, default);     // 真·向量参数：维持既有行为

    // 解析失败且键类型为 TextureOrVector（存的是贴图路径）→ 原文保留，绝不静默丢值
    return new VectorParameter(p, default) { RawText = raw };
```

| 项 | 规定 |
|---|---|
| 默认值 | `RawText = string.Empty` |
| `RawText` 为空 | `WriteValue` 行为与改动前**逐字一致**（输出 `Vector4.ToString()`），`X/Y/Z/W` 变更照常回写 |
| `RawText` 非空 | `WriteValue` **原样输出** `RawText`，不做 trim / 不做浮点格式化 / 不加引号 |
| `RawText` 与分量的优先级 | `RawText` 非空时**优先**，忽略四个分量（避免「文本存在但仍被 vec4 覆盖」） |
| `BuildRow` 兜底 | **守卫条件（锁定，勿删）**：仅当 `Vector4.TryParse` 失败**且** `p.Shape == ShaderValueShape.TextureOrVector` 时才落到 `RawText`；`TryParse` 成功时不设置 `RawText`（保证纯数值参数不受影响）；`Shape != TextureOrVector` 的**真·向量参数**维持既有行为（解析失败即 `default`），**不进入原文透传**。<br>**删掉该守卫 = 放宽行为**：会把全部 `Shape != TextureOrVector` 的向量参数（约 67 个 `g_v*` 键）一并暴露在原文透传语义下 —— 这不是本契约的目标（见 §8.10.1）。 |
| `TrySetTextureValue` 交互 | 对 `VectorParameter` 行写入路径时，**同时**设置 `RawText`（而非 `X/Y/Z/W`），否则路径无处承载 |
| 重开材质 | `BuildRow` 从 `.vmat` 读回该键时，路径文本经 `TryParse` 失败 → 原文进 `RawText` → **保存时不丢值**（修复理由 2） |

#### 8.10.3 与 `Lib` 侧的关系（**Lib 无需任何改动**）

`VmatGenerator.NormalizeValue` 对 `ShaderParamKind.Vector` 走 `NormalizeVector`，其内部 `Vector4.TryParse` 失败即回退 `raw`，
因此路径字符串在 `BuildDocument` 阶段**本就能原样透传**。本节全部改动**局限于 GUI 层**，`Lib` 无需改动，§9 的相关注释保持不变。

---

## 9. 验收场景（18 条，逐条可判定）

> 通用前置：`textureRoot = "D:/art/backrooms/textures"`（配置中已存在且目录存在），
> `rules` = §4 默认种子表，`autoAssignOnDrop = true`，`recurseTextureFolders = true`。
> 判定口径：**拖放处理结束、`RebuildText()` 之后**，检查对应 `ParameterRowViewModel.Value` 的精确字符串。

| # | 输入文件集 | 当前选中着色器 | 期望写入的参数键 | 期望的最终值 |
|---|---|---|---|---|
| **S1** | `textures/wall/concrete_wall_normal.png`、`textures/wall/concrete_wall_diffuse.png` | `csgo_environment.vfx` | `TextureNormal1`、`TextureColor1` | `wall/concrete_wall_normal.png`、`wall/concrete_wall_diffuse.png` |
| **S2** | 同 S1 | `csgo_lightmappedgeneric.vfx` | `TextureLayer1Normal`、`TextureLayer1Color` | `wall/concrete_wall_normal.png`、`wall/concrete_wall_diffuse.png` |
| **S3** | `textures/wall/normal.png`（整名相等） | `csgo_environment.vfx` | `TextureNormal1` | `wall/normal.png` |
| **S4** | `textures/wall/brick_normal.png` + `textures/wall/brick_n.png`（同一批） | `csgo_environment.vfx` | 仅 `TextureNormal1`（冲突见 §6.5） | `wall/brick_normal.png`；`brick_n.png` 出现在 `LastDropResult.Conflicts[0].DroppedFilePaths`；`Reason` 文本为 `同槽位冲突（Normal），已保留 brick_normal.png` |
| **S5** | `textures/wall/a_normal.png` + `textures/wall/b_normal.png`（同长度后缀，Ordinal 决胜） | `csgo_environment.vfx` | `TextureNormal1` | `wall/a_normal.png`（`b_normal.png` 进 Conflicts） |
| **S6** | `water/water_foam.png`、`water/water_foamnormal.png`、`water/water_debris.png`（各走**专用**后缀，避开 `_normal`） | `csgo_water_fancy.vfx` | `TextureFoam`、`TextureFoamNormal`、`TextureDebris`（均为 **P4** 精确通道命中） | `water_foam.png`、`water_foamnormal.png`、`water_debris.png`；注意 `water_foamnormal.png` 按「最长后缀优先」命中 `_foamnormal`（10 字符）而非 `_normal`（6 字符），故角色是 `FoamNormal` 而非 `Normal` |
| **S7** | `water/water_normal.png`（普通 `_normal`） | `csgo_water_fancy.vfx` | **不写入任何参数**（P5 多候选并列 → `AmbiguousQualified`） | `TextureFoamNormal` / `TextureWavesNormal` / `TextureDebrisNormal` 三者值**全部保持模板默认**，不被覆盖；`LastDropResult.UnresolvedRoles` 含 `Normal`，其 `UnresolvedReason == TextureResolveFailure.AmbiguousQualified`，`Candidates.Count == 3`；`Assignments.Count == 0`；`StatusText` 含 `⚠ 该着色器没有通用的 法线 槽位，候选为 TextureFoamNormal / TextureDebrisNormal / TextureWavesNormal；请改用 _foamnormal / _debrisnormal / _wavesnormal 后缀，或改选其他着色器。` |
| **S8** | `D:/other/wall_normal.png`（位于 `textureRoot` **之外**） | `csgo_environment.vfx` | `TextureNormal1` | `D:/other/wall_normal.png`（**原路径**，无正斜杠改写，无 `..`） |
| **S9** | 拖入目录 `D:/art/backrooms/materials/concrete/`（内含 3 个 `.vmat`）+ 文件 `textures/concrete/concrete_normal.png` | 无（编辑器为空） | `MaterialsFolder` = `D:/art/backrooms/materials/concrete`；载入 3 个 `.vmat`；选中首个 `.vmat`；写入 `TextureNormal1` | `MaterialsFolder == "D:/art/backrooms/materials/concrete"`；`ShaderGroups` 中该 shader 组 `MaterialEntries.Count == 3`；`TextureNormal1 == "concrete/concrete_normal.png"` |
| **S10** | `textures/ui/logo.png`（无规则命中） | `csgo_environment.vfx` | **无任何参数被修改** | 全部 `ParameterRows.Value` 与拖放前完全一致；`LastDropResult.Assignments.Count == 0`；`UnassignedFiles` 含 `logo.png`；`Editor.IsDirty == false`；`StatusText == "未匹配到任何后缀规则（1 个文件），未写入任何参数。"` |
| **S11** | `D:/downloads/readme.txt` | 任意 | **无任何写入** | `DropAnalysis.Category == DropCategory.Unsupported`；`StatusText == "未识别可导入的内容（txt），已忽略。"` |
| **S12** | `fx/fx_mask.png` + `fx/banner_mask.png`（同一批，均经 `_mask` → 角色 `Mask`） | `csgo_effects.vfx` | 仅 `TextureMask1`（角色 `Mask` 在该模板走 **P2**，`num` 取最小 ⇒ `TextureMask1`） | `fx/banner_mask.png`；`TextureMask2` / `TextureMask3` **保持原默认值**（`materials/default/default_mask.tga`）；`fx_mask.png` 落入 `LastDropResult.Conflicts[0].DroppedFilePaths`，`Reason` 为 `同槽位冲突（Mask），已保留 banner_mask.png`（两文件命中后缀同为 `mask`、同为边界匹配，故由 §6.5 序 3 文件名 `CompareOrdinal` 升序决定：`banner_mask` < `fx_mask`） |

> **原写法 `fx_mask1.png` + `fx_mask2.png` 不可满足**（reviewer R8，已修订）：
> §4 明文「`_mask1/2/3` **不单列规则**」（层级遮罩靠 §5.3 的 P2 自动覆盖），
> 而 §6.3 序 3 的尾部边界匹配要求文件名以 `_mask` **结尾** ——
> `fx_mask1` 的结尾是 `mask1` 而非 `_mask`，**匹配不上任何规则**，按 §6.4 落入 `UnassignedFiles`。
> **这是规格自洽的正确行为，原验收行本身写错了**，实现无需改动（lib-dev 早前已如实上报）。
>
> 替代用例覆盖的规则：① §5.3 **P2** —— 角色 `Mask` → `TextureMask1`（`num` 取最小），这是 `TextureMask1/2/3` 三个键**唯一可达**的路径；② §6.5 **同槽位冲突** —— 两个文件解析出同一角色，只写一项、另一项记入 `Conflicts`。
>
> **不在本场景覆盖**：`TextureMask2` / `TextureMask3` 的「保持默认值」由上表「期望的最终值」一列断言；
> 而 P2 的 `num` 取最小在**解析层**另有断言（`Resolve(csgo_effects, Mask).ParameterKey == "TextureMask1"`），§9 不重复。
| **S13** | `fx/fx_mask.png`（普通 `_mask`，P5 **唯一**候选） | `csgo_environment.vfx` | `TextureTintMask1`（P5 唯一候选 → 正常命中） | `fx_mask.png`；证明 P5 唯一候选路径未被 S7 的降级规则误伤 |
| **S14** | `prop/prop_mask.png`（普通 `_mask`，P5 **多**候选） | `csgo_complex.vfx` | **不写入任何参数**（`Mask` 并列命中 `TextureSelfIllumMask` 与 `TextureDetailMask` → `AmbiguousQualified`） | 两个键的值**全部保持模板默认**；`UnresolvedRoles` 中 `Mask` 的 `UnresolvedReason == AmbiguousQualified`，`Candidates.Count == 2`；`StatusText` 含 `候选为 TextureSelfIllumMask / TextureDetailMask` |
| **S15** | `prop/prop_selfillummask.png`（**按 S14 诊断建议改名**） | `csgo_complex.vfx` | `TextureSelfIllumMask`（角色 `SelfIllumMask` 走 **P4** 精确命中） | `prop_selfillummask.png`。**这是 INV-DIAG-CLOSURE 的端到端验证**：S14 的诊断建议必须真的可执行 |
| **S16** | `prop/prop_detailmask.png`（按 S14 诊断建议改名） | `csgo_complex.vfx` | `TextureDetailMask`（角色 `DetailMask` 走 **P4**） | `prop_detailmask.png`；验证 §4 第 18 条 `_detailmask` 改指 `DetailMask` 后仍可闭合 |
| **S17** | `water/water_height.png`（P5 多候选） | `csgo_water_fancy.vfx` | **不写入任何参数**（`Height` 并列命中 `TextureDebrisHeight` + `TextureWavesHeight`） | 两键保持模板默认；诊断文本含 `_debrisheight` / `_wavesheight`，且这两个后缀按 S18 可执行 |
| **S18** | `water/water_wavesheight.png`（按 S17 诊断建议改名） | `csgo_water_fancy.vfx` | `TextureWavesHeight`（角色 `WavesHeight` 走 **P4**） | `water_wavesheight.png`；`water_wavesheight` 归一化后长度为 12，长于 `_height`（6），最长后缀优先确保命中 `WavesHeight` 而非 `Height` |

---

## 10. 与既有代码的交互约定（防回归）

| 既有行为 | 本功能的影响 | 约束 |
|---|---|---|
| `VmatGenerator.BuildDocument` 为每个 `Kind == Texture` 且有值的参数自动追加 `<path>.vtex` 并写 `Compiled Textures → g_tXxx` | 自动写入贴图后，Compiled Textures 会随之刷新 | **不得**新增任何手工写 `g_tXxx` 的代码。注：`Kind == Vector` 的参数（如 `TextureRoughness1`、`TextureFoamNormal`）不参与自动推导，其 `g_t*` 保持模板值 —— 这是既有行为，本功能不改。 |
| `VmatGenerator.NormalizeValue` 对 `ShaderParamKind.Vector` 会尝试 `Vector4.TryParse` | 向 `TextureOrVector` 参数写入贴图路径字符串 | `Vector4.TryParse("wall/concrete.png")` 失败 → 原样返回，路径不失真。**无需**新增分支。 |
| `ShaderEditorViewModel.OnRowPropertyChanged` 触发 `RebuildText()` 并置 `IsDirty = true` | 每次自动写入都会置脏 | 符合预期；`S10` 中无写入 → 不置脏。 |
| `MaterialScanner.Scan` 对单个坏 `.vmat` 已做 try/catch | 拖入目录时复用 | 不新增解析逻辑。 |
| `ControlErrorRecorder.GuardWithDialog` 包裹全部事件处理器 | 新增 4 个方法 | 全部必须包裹（§2.4）。 |
| 启动时**不**自动扫描任何目录 | `defaultShaderName` 仅在需要时按需 `NewVmat` | **不得**在 `OnStartup` 里读配置并自动 `LoadFolder`。 |

---

## 11. 实现自检清单（两条线各自勾选）

| # | lib-dev 自检 | ui-dev 自检 |
|---:|---|---|
| 1 | `TextureSuffixMatcher.NormalizeName("Concrete-Wall _N.png") == "concrete_wall_n"` | `MainWindow.xaml` 根节点含 `AllowDrop="True"` 及 3 个事件名 |
| 2 | §9 S1–S18 全部通过（可用内存假目录构造） | `MainWindow.xaml.cs` 含 §8.5 四个方法名 |
| 3 | `TextureRoleResolver.Resolve(csgo_environment, Normal).ParameterKey == "TextureNormal1"` | `MainViewModel.LoadFolder` 为 `public` |
| 4 | `… Resolve(csgo_lightmappedgeneric, Roughness).ParameterKey == "TextureLayer1Roughness"` | `ShaderEditorViewModel.CurrentShader` / `TrySetTextureValue` 存在 |
| 5 | `… Resolve(csgo_water_fancy, Normal).IsResolved == false` 且 `UnresolvedReason == TextureResolveFailure.AmbiguousQualified` 且 `Candidates.Count == 3` | `QuickNavViewModel` 15 个公开成员齐备（§8.3） |
| 6 | `… Resolve(csgo_water_fancy, Color).IsResolved == false` 且 `UnresolvedReason == TextureResolveFailure.NoMatchingKey` 且 `Candidates.Count == 0` | `TextureSuffixRuleViewModel` 6 个公开成员齐备（§8.4） |
| 7 | `… Resolve(csgo_water_fancy, CubeMap).ParameterKey == "TextureLowEndCubeMap"`（P5 唯一候选仍正常命中） | 快速导航窗口的「恢复默认规则」调用 `VmatGeneratorSettingsStore.RestoreDefaultRules` |
| 8 | `… Resolve(csgo_complex, Mask).UnresolvedReason == AmbiguousQualified` 且 `Candidates.Count == 2`（P5 多候选降级规则通用生效，无 per-shader 特判） | `PreviewLines` 按 §8.3 区分 `NoMatchingKey` / `AmbiguousQualified` 两种显示 |
| 9 | `BuildDiagnosticText(water, AmbiguousQualified, 3 候选)` 的输出与 §5.6 模板逐字一致（含 `" / "` 连接符与声明序） | 状态栏按 §8.9 区分 `⚠`（AmbiguousQualified）/ `ℹ`（NoMatchingKey）两行 |
| 10 | 设置文件损坏 → `Outcome == RecoveredFromCorruption` 且生成备份 | — |
| 11 | `ToVmatPath("D:/t/wall/a.png", "D:/t") == "wall/a.png"`；`ToVmatPath("D:/x/a.png", "D:/t") == "D:/x/a.png"` | 规则表上下移动按钮改的是 `Rules` 的**顺序**（即 JSON 数组下标） |
| 12 | **INV-DIAG-CLOSURE 自动校验**：遍历 `ShaderCatalog.All`，对每个产生 `AmbiguousQualified` 的 `(shader, role)`，解析 `DiagnosticText` 中的**每个**建议后缀，断言 ① `VmatGeneratorSettings.CreateDefault().Rules` 中存在归一化后缀等于它的 enabled 规则；② 该规则的 `Role` 经 `TextureRoleResolver.Resolve` 得到的 `ParameterKey` **恰好等于**诊断文本中与它配对的候选键。**任一断言失败即测试失败。** | — |
| 13 | 穷举比对：`ShaderCatalog.All` 中每个可作 P5 候选的 channel，**必须**出现在 §5.6.2 闭合表中，且有「主 Token 与之精确相等」的角色与对应种子规则（防止新增 shader 后遗漏） | — |

---

## 12. 修订记录

| 版本 | 变更 | 来源 |
|---|---|---|
| v1.0 | 首版规格（拖拽矩阵 / 配置模型 / 种子规则 / P1–P5 分档 / Lib API / GUI 契约 / 12 条验收场景） | t1 交付 |
| **v1.1** | **P5 多候选由「限定词最短取胜」改为「整体放弃命中」**：删除 `qualifier.Length` 排序键；P5 新增「候选唯一」限定条件；`None` 拆分为 `NoMatchingKey` / `AmbiguousQualified` 两类；新增 §5.6 诊断文本组装规则；§7.5 新增 `TextureResolveFailure` 枚举与 `TextureRoleResolution.UnresolvedReason` / `DiagnosticText`；新增 `TextureRoleResolver.BuildDiagnosticText`；§8.3 预览区分两种未解析；新增 §8.9 状态栏两行式提示；验收场景 S7 反转为「不写入」，新增 S13 / S14。**连带修正**：S6 原用 `foam_normal.png`，该名在新规则下命中的是 `_normal` → `Normal` 角色而非 `FoamNormal`，已改为 `water_foamnormal.png` 等专用后缀；§4 补入 `_wavesnormal` / `_debrisnormal`（28 → 30） | **captain 裁决**（接受「无歧义优先」，要求 P5 多候选不排序、不命中） |
| **v1.2** | **闭合 §5.6 不变量（INV-DIAG-CLOSURE）**：§5.2 新增 4 个角色 `DetailMask` / `SelfIllumMask` / `RimMask` / `LowEndCubeMap`（主 Token 均取完整复合名，并附「主 Token 不得取内部通用 Token」的反例说明）；§4 新增 5 条规则 `_rimmask`(31) / `_lowendcubemap`(32) / `_selfillummask`(33) / `_wavesheight`(34) / `_debrisheight`(35)，并把第 18 条 `_detailmask` 由 `Mask` 改指 `DetailMask`（30 → **35 条**）；新增 §5.6.1 不变量定义 + §5.6.2 P5 候选 channel 穷举闭合表（10 行）；§7.1 枚举同步 +4；§5.5 water 表新增 `Height` 歧义行、其余模板表改按 P4 专用角色描述；验收场景 14 → **18 条**（新增 S15/S16/S18 为「诊断建议可执行」正例，S17 为 `Height` 歧义）；§11 自检清单新增第 12 项（INV-DIAG-CLOSURE 自动校验）与第 13 项（P5 候选穷举比对） | **captain 复核**（指出 `_selfillummask` 缺口，要求把「诊断建议 → 可执行」写成不变量而非逐条核对） |

> **v1.2 穷举新发现的第 4 个缺口**：`csgo_water_fancy` 的 `Height` 角色同样有两个 P5 候选
> （`TextureDebrisHeight` + `TextureWavesHeight`），诊断文本会建议 `_debrisheight` / `_wavesheight`，
> 而原种子表两条皆无。已由第 34 / 35 条补齐，并由 S17 / S18 覆盖。

| **v1.3** | **对齐 t3 实做反馈（captain 复核确认三处均为规格错）**：① §7.8 `CanAccept(IDataObject?)` → **`CanAccept(string[]?)`** —— `IDataObject` 是 WPF 类型，`Lib` 为 `net10.0` 纯类库，引入即违反 t2 outOfScope；`IDataObject → string[]` 的转换只发生在 GUI 层 `FileDropOf`（§2.3 同步）。② §8.2 / §8.5 消除自相矛盾：v1.2 同时要求工具栏绑 `Command` 与 code-behind `OnOpenQuickNavClicked`，而 WPF `ButtonBase` 在同控件上 `Click` 与 `Command` **各自独立触发**，照字面实现会每次点击弹两个窗口；现拆分为**菜单项绑 `Click`、工具栏绑 `Command`**，并新增通用 WPF 约束「同一控件不得同时设置 `Click` 与 `Command`」。③ 新增快捷键 **`Ctrl+Q`**（与 Ctrl+N/O/S、Ctrl+Shift+S、Ctrl+C、F5 均不冲突）。④ 新增 **§8.10 `VectorParameter.RawText` 契约**（已批准范围内）：`TextureRoughness1` / `TextureFoamNormal` 走 `VectorParameter` 无法承载路径，无此则 S1/S6 静默失效；同时修复既有丢值缺陷（`BuildRow` 的 `ParseVector` 失败返回 `default`，会把路径改写成 `[0 0 0 0]`），并让死标记 `ShaderValueShape.TextureOrVector` 首次生效。§4 / §5.2 / §5.6 / §5.6.1 / §5.6.2 / §11 本轮**未改动一字**（captain 已独立复核 v1.2 闭合表完备） | **captain 裁决**（依 t3 实做反馈） |

| **v1.4** | **事实性口径更正（不改任何契约数字）**：① `ShaderCatalog` 实为 **11** 个模板（原文档按 10 推算），§0 文件清单与 §5.5 小节标题据此重算 —— 后者原写「其余 7 个模板」但表内实际列了 8 行（11 − 已单列的 3 个），一并更正为「其余 8 个」并加「无遗漏」说明。② 第 21 / 22 / 25 / 29 条（`_emissive` / `_selfillum` / `_waves` / `_lightmap`）备注改为准确陈述：**当前 11 个模板均无对应槽位，结果恒为 `NoMatchingKey`**（原 `_lightmap` 备注「通常不自动写入」暗示「有时能写」，属误导）；新增 §4 预留规则小节说明**保留理由**（删掉等于过拟合今天的着色器目录）、**为何不构成静默错误**（只诚实报告无对应键，不猜错槽位）、以及**不受 INV-DIAG-CLOSURE 约束**的理由（不变量只管 `AmbiguousQualified` 的建议后缀，这 4 条产生的是 `NoMatchingKey`）。**规则本身按裁决保留**，条数仍 **35 条**、编号仍连续 1..35、角色仍 **26** 个、§5.6.2 闭合表仍 **10** 行、§11 契约仍 **13** 项 —— 六章一字未动 | **captain 更正**（integrator 走查事实反馈） |

| **v1.5** | **对齐 t5 审查 R8 / R9 两条规格侧问题（契约数字不变）**：① **§9 S12 修订** —— 原写法 `fx_mask1.png` + `fx_mask2.png` **不可满足**：§4 明文「`_mask1/2/3` 不单列规则」，§6.3 序 3 的尾部边界匹配要求文件名以 `_mask` 结尾，而 `fx_mask1` 结尾是 `mask1`，**匹配不上任何规则**、按 §6.4 落入 `UnassignedFiles`。这是规格自洽的**正确行为**，**原验收行本身写错，实现无需改动**。改用 `fx_mask.png` + `banner_mask.png`，并在行下注明原写法为何不可满足、替代用例覆盖 §5.3 **P2**（`TextureMask1/2/3` 的唯一可达路径）与 §6.5 **同槽位冲突**，以及 P2 `num` 取最小在解析层另有断言、§9 不重复。**场景总数仍 18 条**。② **§8.10 范围收窄（保留实现，改规格）** —— 实现多出的 `Shape == TextureOrVector` 守卫条件**保留**；§8.10.1 理由 2 改为准确陈述：丢值缺陷发生在 `Shape == TextureOrVector` 的键上，修复范围与之一致；**不**改写为「本缺陷从未存在」——该历史事实保留。明确 **不在范围**内者为真·向量参数（`g_vColorTint` / `g_vTexCoord*` / `g_vWater*` 等约 67 个键），其值本就应为 vec4。§8.10.2 代码块与契约表同步写入守卫条件，并**显式警告「删掉该守卫会放宽行为」**，防止后续维护者为图省事移除。§4 / §5.2 / §5.6 / §5.6.1 / §5.6.2 / §11 一字未动 | **captain 裁决**（t5 审查 needs_revision 的两条规格侧根因） |

| **v1.6** | **R12 修复：统一 §5.6 `{槽位}` 口径**。§5.6 表头以规范性措辞规定 `{槽位}` = `TextureRoleTokens.Describe(role)`（`Normal` → `法线`），同节 `NoMatchingKey` 示例亦用中文名，但四处示例/断言仍写枚举名 `Normal`，与规范性定义互斥 —— 实现遵循表头（输出 `法线`），故原 §9 S7 的逐字断言**永远不可能成立**。取 reviewer 建议方案 (a)：**四处示例/断言统一改为中文槽位名 `法线`**（§5.6 `AmbiguousQualified` 示例、§5.5、§8.9、§9 S7），另统一 §5.5 短句中的第五处同源写法；**实现不需要改动，§5.6 表头的规范性定义逐字保持不变**（严禁反向改成枚举名）。§5.6 组装细则新增 `{槽位}` 条目写明判定依据：文本直接呈现于状态栏与预览区，须与界面其余文案同语言，枚举名是程序内部标识、对终端用户既无信息量也不可读；**两个分支口径一致**。保留 `UnresolvedRoles` 含枚举成员 `Normal` 等 API 侧标识不变。六个契约数字一个未动 | **captain 裁定**（t7 复审 R12 [medium]，规格内部口径互斥） |

---

## 13. 变更清单（一次性汇总）

| 类型 | 路径 |
|---|---|
| 新增（Lib） | `Lib/TextureRole.cs`、`Lib/TextureRoleTokens.cs`、`Lib/TextureSuffixRule.cs`、`Lib/TextureSuffixMatcher.cs`、`Lib/TextureRoleResolver.cs`、`Lib/TexturePathRules.cs`、`Lib/TextureAssignment.cs`、`Lib/DropImportService.cs`、`Lib/VmatGeneratorSettings.cs`、`Lib/VmatGeneratorSettingsStore.cs` |
| 新增（GUI） | `GUI/QuickNavWindow.xaml`、`GUI/QuickNavWindow.xaml.cs`、`GUI/ViewModels/QuickNavViewModel.cs`、`GUI/ViewModels/TextureSuffixRuleViewModel.cs` |
| 修改（GUI） | `GUI/MainWindow.xaml`、`GUI/MainWindow.xaml.cs`、`GUI/ViewModels/MainViewModel.cs`、`GUI/ViewModels/ShaderEditorViewModel.cs` |
| 修改（工程） | 无（`GUI.csproj` / `Lib.csproj` 均不需新增依赖） |
| 新增（文档） | `docs/feature-dragdrop-suffix-spec.md`（本文件） |
