# 拖拽导入 + 贴图后缀快速导航 · 使用说明

> 本文是面向**使用者**的说明。契约级细节（解析分档判据、排序全序、Lib API 签名）见
> [`feature-dragdrop-suffix-spec.md`](feature-dragdrop-suffix-spec.md)；本轮功能的实现走查与验收记录见
> [`REPORT.md`](REPORT.md) 的「拖拽导入 + 贴图后缀快速导航」一节。

---

## 1. 这两个功能解决什么问题

以前往 VMAT 生成器里塞贴图，只能一个一个双击选文件，再手动把路径敲进参数框。
现在可以直接**把文件 / 文件夹拖进主窗口**，程序按文件名后缀自动判断「这是法线还是漫反射」，
然后写进正确的参数键。

配套的「贴图后缀快速导航」对话框（`工具 → 贴图后缀快速导航(_Q)`，快捷键 **Ctrl+Q**，
或工具栏最右边的「快速导航」按钮）让你：

- 指定**生成着色器类型**（拖入时用哪个模板写入）；
- 指定**贴图根目录**（决定写进 VMAT 的是相对路径还是原路径）；
- **增删改排**后缀规则表 —— 换掉 `_d` 还是 `_diffuse`、把某个后缀停用、把自定义后缀加进来；
- 在写入之前，**先看清楚**「这张贴图会被写到哪个参数键」，不想要就点「测试贴图」试算。

---

## 2. 配置方法

### 2.1 打开与保存

打开：**工具 → 贴图后缀快速导航**（Ctrl+Q），或工具栏的「快速导航」按钮。
两个入口各开一次窗口，不会重复弹窗。

**所有改动都先停在对话框里，点「保存配置」才写盘**——这样你敲错一个字符不会立刻污染配置文件。
对话框底部会显示配置文件路径：

```
%APPDATA%\VmatGenerator\settings.json
```

写盘是原子的（先写 `.tmp` 再原子替换），写到一半掉电不会留下半截 JSON。

| 按钮 | 作用 |
| --- | --- |
| **重新载入** | 丢弃对话框里未保存的改动，回到磁盘上的值 |
| **保存配置** | 把当前所有字段写回 `settings.json`，重启后仍然生效 |
| **恢复默认规则** | 把规则表还原成出厂的 **35 条**种子规则（其余字段保留；同样需再点「保存配置」才落盘） |

### 2.2 五个配置项

| 字段 | 配置键 | 默认 | 含义 |
| --- | --- | --- | --- |
| 生成着色器类型 | `defaultShaderName` | `csgo_environment.vfx` | 新建材质 / 拖入贴图时用哪个着色器模板 |
| 贴图根目录 | `textureRoot` | 空 | 贴图在此目录**之内** → 写相对路径（正斜杠）；在**之外** → 写原路径 |
| 拖入后自动写入 | `autoAssignOnDrop` | `true` | 关掉则拖入只分类不写参数，状态栏会明说 |
| 贴图递归枚举 | `recurseTextureFolders` | `true` | 拖入贴图文件夹时是否下钻子目录（关掉 = 只收顶层，子目录贴图会在状态栏说明） |
| 拖入材质目录时接管根目录 | `adoptDroppedVmatFolderAsMaterialsRoot` | `true` | 是否把拖入的 `.vmat` 所在目录设为材质根目录 |

> **注意**：`.vmat` 的探测**始终**是递归的（`AllDirectories`），与 `recurseTextureFolders` **无关**。
> 两者是两个独立开关，别把它们当成一个。

### 2.3 规则表怎么读

规则表每一行是「**后缀名 → 语义槽位**」：

| 列 | 说明 |
| --- | --- |
| 后缀名 | 你自己写的，可以带 `_`、可以不带、可以带扩展名。写 `_diffuse`、`diffuse`、`_Diffuse.PNG` 归一化后都是同一条 |
| 归一化后缀 | 只读回显，由程序算出，让你确认它会拿什么去匹配 |
| 语义槽位 | 该后缀代表的**语义**（Normal / Color / Roughness / …），下拉里是全部角色名 |
| 启用 | 取消勾选即停用该规则（行保留，方便随时开回来） |
| 当前着色器解析预览 | **关键列**：在你当前选的着色器下，这个槽位最终会落到哪个参数键。显示「（未解析）」说明这张着色器没有对应参数 —— 详见 [§5.2](#52-为什么有些贴图不写入) |

**顺序说明**：表里行的先后顺序**不代表优先级**。真正的优先级是运行时算出来的：
**最长后缀优先 → 整名相等优先 → 文件名词数少者优先 → 数组下标小者优先**。
所以「上移 / 下移」按钮只是在改 JSON 数组下标（即最后一条判据），
把 `_normal` 移到第 1 位并不会让它压过 `_n`。

---

## 3. 每条后缀规则的语义（出厂 35 条）

下表**每一格都是实测结果**（对 `ShaderCatalog` 全部 11 个模板跑 `TextureRoleResolver.Resolve` 得出），
不是按键名推测的。`N/11` = 11 个着色器里有几个能解析出参数键。

| 语义槽位 | 默认后缀 | 实测落点（`着色器`=`参数键`(档位)） | 可解析 |
| --- | --- | --- | --- |
| 法线 Normal | **`_normal`**、_n、_normals | `csgo_environment`=`TextureNormal1`(P2) · `csgo_lightmappedgeneric`=`TextureLayer1Normal`(P3) · `csgo_vertexlitgeneric`/`complex`/`static_overlay`/`character`=`TextureNormal`(P1) | 6/11 |
| 泡沫法线 FoamNormal | **_foamnormal** | `csgo_water_fancy`=`TextureFoamNormal`(P1) | 1/11 |
| 波浪法线 WavesNormal | **_wavesnormal** | `csgo_water_fancy`=`TextureWavesNormal`(P1) | 1/11 |
| 漂浮物法线 DebrisNormal | **_debrisnormal** | `csgo_water_fancy`=`TextureDebrisNormal`(P1) | 1/11 |
| 颜色 Color | **`_diffuse`**、_albedo、_color、_col | `csgo_environment`=`TextureColor1`(P2) · `csgo_lightmappedgeneric`=`TextureLayer1Color`(P3) · `csgo_vertexlitgeneric`/`complex`/`static_overlay`/`character`/`moondome`/`effects`/`generic`=`TextureColor`(P1) | 9/11 |
| 粗糙度 Roughness | **_roughness**、_rough | `csgo_environment`=`TextureRoughness1`(P2) · `csgo_lightmappedgeneric`=`TextureLayer1Roughness`(P3) · 其余 4 个=`TextureRoughness`(P1) | 6/11 |
| 金属度 Metalness | **_metalness**、_metal | `csgo_environment`=`TextureMetalness1`(P2) · 其余 3 个=`TextureMetalness`(P1) | 4/11 |
| 环境光遮蔽 AmbientOcclusion | **_ao**、_ambientocclusion | `csgo_environment`=`TextureAmbientOcclusion1`(P2) · `csgo_lightmappedgeneric`=`TextureLayer1AmbientOcclusion`(P3) · 其余 4 个=`TextureAmbientOcclusion`(P1) | 6/11 |
| 高度 Height | **_height** | `csgo_environment`=`TextureHeight1`(P2) | 1/11 |
| 漂浮物高度 DebrisHeight | **_debrisheight** | `csgo_water_fancy`=`TextureDebrisHeight`(P1) | 1/11 |
| 波浪高度 WavesHeight | **_wavesheight** | `csgo_water_fancy`=`TextureWavesHeight`(P1) | 1/11 |
| 半透明 Translucency | **_translucency**、_trans | `csgo_lightmappedgeneric`=`TextureLayer1Translucency`(P3) · `csgo_vertexlitgeneric`/`complex`/`static_overlay`/`effects`=`TextureTranslucency`(P1) | 5/11 |
| 细节 Detail | **_detail** | `csgo_lightmappedgeneric`=`TextureLayer1Detail`(P3) · `csgo_complex`=`TextureDetail`(P1) | 2/11 |
| 细节遮罩 DetailMask | **_detailmask** | `csgo_complex`=`TextureDetailMask`(P1) | 1/11 |
| 染色遮罩 TintMask | **_tintmask** | `csgo_environment`=`TextureTintMask1`(P2) | 1/11 |
| 自发光遮罩 SelfIllumMask | **_selfillummask** | `csgo_vertexlitgeneric`/`complex`/`static_overlay`=`TextureSelfIllumMask`(P1) | 3/11 |
| 边缘光遮罩 RimMask | **_rimmask** | `csgo_character`=`TextureRimMask`(P1) | 1/11 |
| 泡沫遮罩 FoamMask | **_foam** | `csgo_water_fancy`=`TextureFoam`(P1) | 1/11 |
| 漂浮物颜色 DebrisColor | **_debris** | `csgo_water_fancy`=`TextureDebris`(P1) | 1/11 |
| 低端 Cube Map | **_lowendcubemap** | `csgo_water_fancy`=`TextureLowEndCubeMap`(P1) | 1/11 |
| **通用遮罩 Mask** ⚠ | **_mask** | `csgo_effects`=`TextureMask1`(P2) · `csgo_environment`=`TextureTintMask1`(P5) · `csgo_character`=`TextureRimMask`(P5) · `csgo_vertexlitgeneric`/`static_overlay`=`TextureSelfIllumMask`(P5) | 5/11 |
| **Cube Map** ⚠ | **_cube** | `csgo_moondome`=`TextureCubeMap`(P1) · `csgo_water_fancy`=`TextureLowEndCubeMap`(P5) | 2/11 |

### ⚠️ 三条「预留规则」：当前一个着色器也用不上

出厂种子表里有 **4 条规则指向 3 个语义槽位**（`Emissive` ×2、`WavesMask`、`Lightmap`），
而这 3 个槽位在**当前全部 11 个着色器模板上都没有对应参数键**：

| 后缀 | 语义槽位 | 当前 11 个模板为何写不进 | 状态 |
| --- | --- | --- | --- |
| `_emissive`、`_selfillum` | Emissive | 全部模板中 `TextureEmissive` 出现 **0** 次，没有任何模板暴露自发光贴图键 | **预留** |
| `_waves` | WavesMask | 全部模板中 `TextureWaves` 出现 **0** 次（水面的遮罩键叫 `TextureFoam`） | **预留** |
| `_lightmap` | Lightmap | 相关键名为 `LightMapTextureName`，不以 `Texture` 开头，**不满足 §5.1 条件①**，永远不会成为候选 | **预留** |

拖入这 4 类后缀结尾的贴图时，程序**不会写入任何参数**，状态栏给出中性提示
`ℹ 有 N 个槽位在该着色器中没有对应贴图参数，已跳过。`——这是**准确陈述，不是静默错误**：
程序不会猜一个相近的键写进去，而且这条 `ℹ` 提示表示**你无需做任何处理**。

**为什么规则还留着**：它们不是死规则，而是**面向未来的预留项**。将来若新增带
`TextureEmissive` / `TextureWaves` 的模板，这几条规则立刻可用，无需用户重新配置。
删掉它们等于过拟合今天这份着色器目录。

> ⚠️ 如果某个来源告诉你「`_lightmap` 通常能写入」——那是**错的**。在当前 11 个模板下它是
> 永远写不进，不是「大多数情况下写不进」。

若你知道自己着色器里的真实键名，可以在规则表里把对应后缀改指到正确的语义槽位。

### ⚠️ `_mask` 与 `_cube` 是「跟着着色器走」的

- `_mask` → 贴到 `csgo_effects` 是 `TextureMask1`；贴到 `csgo_environment` 变成染色遮罩 `TextureTintMask1`；贴到 `csgo_character` 变成边缘光遮罩 `TextureRimMask`。
- `_cube` → 贴到 `csgo_moondome` 是 `TextureCubeMap`；贴到 `csgo_water_fancy` 变成 `TextureLowEndCubeMap`。

所以**拖入前一定先确认「生成着色器类型」选对了**，或用「测试贴图」先试算。

### 自定义规则怎么加

1. 点对话框里的 **添加**，新行会出现在表末并被选中；
2. **后缀名**填你要匹配的后缀（`_` 开头更不容易误伤，但 `_mask` 这类已经要求下划线边界）；
3. **语义槽位**从下拉里选（填的是**枚举名**，比如 `Normal`、`FoamNormal`；别名 `AO`、`Rough` 不是合法值，会被判为非法并自动停用该规则）；
4. 点 **保存配置**。

加完建议点一次 **刷新预览** 确认「当前着色器解析预览」列出现的是你预期的参数键。

---

## 4. 拖入各类内容会发生什么

把文件 / 文件夹拖进主窗口，鼠标悬停时窗口会盖一层高亮表示「可以放」。程序按下表分档处理：

| 拖入的内容 | 分类 | 会做什么 |
| --- | --- | --- |
| **含 `.vmat` 的文件夹** | D1 材质文件夹 | 递归探测所有 `.vmat`（**始终递归**）→ 视 `adoptDroppedVmatFolderAsMaterialsRoot` 决定是否接管材质根目录 → 打开第一个 `.vmat`；若同时有贴图，继续按后缀写入 |
| **若干 `.vmat` 文件** | D2 | 材质根候选 = 这些文件的父目录；打开第一个；不涉及贴图写入 |
| **若干贴图文件** | D3 | 按后缀写入参数；贴图根候选 = 这些贴图的**公共父目录** |
| **只含贴图的文件夹** | D4 | 按 `recurseTextureFolders` 收贴图（关掉则只收**顶层**，子目录贴图会在状态栏说明）→ 有贴图时按后缀写入；贴图根候选 = 该目录 |
| **混合内容**（既有 `.vmat` 又有贴图） | D5 | 材质根候选 = **第一个含 `.vmat` 的目录**；贴图根候选 = 贴图的公共父目录；两边都处理 |
| **无关内容**（`.txt` 之类） | D6 | **零写入**，状态栏说明忽略了哪些扩展名 |

### 写入时的路径怎么算

- 贴图在**贴图根目录之内** → 写相对路径，用**正斜杠**，例如 `wall/concrete_wall_normal.png`（根目录正下方不加 `./`）；
- 贴图在**贴图根目录之外** → 写**原路径**，绝不出现 `..`；
- **贴图根目录为空** → 全部写原路径；拖入第一批贴图后，程序会用这批贴图的公共父目录兜底填上，方便下次写相对路径。

### 同一个槽位有多张贴图怎么办

只写**一张**，其余进「冲突」列表，状态栏会说明保留了谁、淘汰了谁。决胜顺序：

1. **更长的后缀**胜出 —— `brick_normal.png` 打赢 `brick_n.png`；
2. 后缀一样长时，**文件名词数少**的胜出 —— `a_normal.png` 打赢 `b_normal.png`；
3. 还一样则比**数组下标**（规则表顺序）。

### 写入前先试算（不落盘）

对话框里的 **测试贴图** 可以选一组贴图试算，逐行列出「文件名 → 命中后缀 → 语义 → 档位 → 参数键 → 写入值」，
**不会真的改任何参数**。想确认某张图到底会写到哪，先用它。

---

## 5. 常见问题

### 5.1 为什么这张贴图没写进去？

状态栏会给出三种不同的原因，对应三种不同处理。**注意前缀不同**：`ℹ` 表示**你无需做任何处理**，`⚠` 表示**需要你改名或换着色器**。

| 状态栏提示 | 前缀 | 含义 | 怎么办 |
| --- | --- | --- | --- |
| 「未匹配到任何后缀规则（N 个文件），未写入任何参数。」 | — | 文件名末尾没有匹配到启用的规则 | 加一条规则，或把文件改名成带已知后缀 |
| 「该着色器没有 {槽位} 对应的贴图参数（已跳过）。」 | `ℹ` | 语义槽位有了，但**当前着色器根本没有这个参数** | **无需处理**；若确实需要它，换生成着色器类型（见 [§3 的零命中清单](#️-三个目前落不到任何着色器的后缀)） |
| 「该着色器没有通用的 {槽位} 槽位，候选为 …；请改用 … 后缀，或改选其他着色器。」 | `⚠` | 当前着色器里这个语义有**多个**候选键，程序**不猜**，一律不写 | **必须处理**：按提示改名，或在规则表里换一个语义槽位（见 [§5.3](#53-水面着色器上的-_normal-不会被写入)） |

> 例：`csgo_water_fancy` 上拖入 `water_normal.png`，状态栏给出
> `⚠ 该着色器没有通用的 法线 槽位，候选为 TextureFoamNormal / TextureDebrisNormal / TextureWavesNormal；请改用 _foamnormal / _debrisnormal / _wavesnormal 后缀，或改选其他着色器。`
> 而同样的着色器上某个「无对应键」的槽位只会给
> `ℹ 有 1 个槽位在该着色器中没有对应贴图参数，已跳过。` —— 后者**不需要你做任何事**。

### 5.2 为什么有些贴图不写入？

着色器模板里的键名不是 `Texture法线` 就能对上的。程序按五档依次尝试（细节见规格 §5.3）：

```
P1  Texture{R}            直名             例：csgo_vertexlitgeneric → TextureNormal
P2  Texture{R}{N}         直名 + 数字       例：csgo_environment     → TextureNormal1（N 取最小）
P3  TextureLayer{N}{R}    Layer 前缀        例：csgo_lightmappedgeneric → TextureLayer1Normal
P4  派生通道名精确相等      别名生效
P5  Texture{Q}{R}         带限定词的复合通道；**候选唯一才命中**
```

**五档全部落空就不写入**。这是刻意的：宁可少写一张，也不要写错一个键把材质搞坏。

### 5.3 水面着色器上的 `_normal` 不会被写入

这是**已知且刻意保留**的行为，不是 bug。`csgo_water_fancy` 里法线有三个候选键
（`TextureFoamNormal` / `TextureWavesNormal` / `TextureDebrisNormal`），
一个普通的 `water_normal.png` 在语义上无法区分是哪一张 —— 所以程序**不写**，
并提示你改名。`csgo_complex` 的「通用遮罩」也是同类情况（2 个候选，一律不写）。

**规避办法**：

- 把文件改名为 `surf_foamnormal.png`（对应出厂规则 `_foamnormal`），程序就会精确写入 `TextureFoamNormal`；
- 或者用 `ripple_wavesnormal.png` / `flotsam_debrisnormal.png`，分别写入 `TextureWavesNormal` / `TextureDebrisNormal`。

### 5.4 为什么我的两个贴图只写进去一个？

同一个语义槽位只能有一个值。见 [§4](#同一个槽位有多张贴图怎么办) 的决胜顺序。
被淘汰的文件会列在状态栏里，不会静默丢失。

### 5.5 配置改坏了 / 手写错了 JSON

程序**不会**因为一个字段非法就丢掉整份配置：只回退**那一个**字段，其余全部保留，
被修复的字段名会列在状态栏（例如「已修复 3 个非法设置字段（schemaVersion、rules[2].role…）」）。

如果文件整个坏掉（不是合法 JSON / 是空文件），程序会：

1. 先把原始内容备份成同目录下的 `settings.corrupt-<时间戳>.json`；
2. 回到默认配置并继续启动 —— **不会**因为配置坏掉就打不开软件。

### 5.6 「贴图后缀自检」是什么？

**帮助 → 贴图后缀自检(_S)** 会重跑「后缀 → 语义槽位 → 着色器参数键」整条链路的
**33 项可执行自检**，逐条列出 PASS / FAIL。

- 它**不会读写你的 `settings.json`** —— 所有涉及文件的用例都在临时目录的独立沙箱里跑，结束时清理；
- 逐条结果会写进错误日志，方便你贴给别人看；
- 改完着色器目录或规则表之后跑一次，如果全绿说明这条链路没被改坏。

---

## 6. 命令与绑定速查

| 操作 | 入口 |
| --- | --- |
| 打开配置对话框 | `工具 → 贴图后缀快速导航`（**Ctrl+Q**）· 工具栏「快速导航」按钮 |
| 写入 | 把文件 / 文件夹拖进主窗口 |
| 预演 | 配置对话框 →「测试贴图」 |
| 自检 | `帮助 → 贴图后缀自检` |
| 配置文件 | `%APPDATA%\VmatGenerator\settings.json` |