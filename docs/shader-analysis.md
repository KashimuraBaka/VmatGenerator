# VMAT Shader 参数差异分析报告

> 扫描源: `H:\dsh-workspace\vmat-generater\materials\**\*.vmat`
> 扫描脚本: `tools/scan_vmat.ps1`（递归遍历 161 个 .vmat，按 `shader` 字段分组，统计 `Layer0` 顶层 KV / `Compiled Textures` / `SystemAttributes` / `Attributes` 子块的所有键，并对每个键记录唯一取值与样本数）
> 对照目标: `VmatGenerator\Lib\ShaderCatalog.cs`（11 个 shader 模板）
> 报告生成日期: 2026-10-04

## 1. 总体统计

| Shader | 样本数 | 模板参数数 | 实际 union(参数) | 差异 (实际 - 模板) |
|---|---:|---:|---:|---:|
| `csgo_environment.vfx`        | 48 | 17 | 23 | +6 (漏掉 `g_bSnowLayer1`、`g_flWetnessDarkeningStrength1`、`g_nUVSet1`、`TextureTintMask1` 等的字段计数差异) |
| `csgo_vertexlitgeneric.vfx`   | 61 | 28 | 38 | +10 |
| `csgo_complex.vfx`            | 26 | 26 | 41 | +15 |
| `csgo_static_overlay.vfx`     | 11 | 16 | 32 | +16 |
| `csgo_lightmappedgeneric.vfx` | 6  | 20 | 22 | +2 (`F_DETAILTEXTURE`、`g_vLayer1DetailScale`、`g_vLayer1DetailTintAndBlend`、`TextureLayer1Detail`、`TextureLayer1Roughness`、`TextureLayer1Translucency` 的取值差异) |
| `csgo_character.vfx`          | 2  | 21 | 20 | -1 (`g_flAmbientOcclusionMasking` 在 catalog 里有，样本里也有；`g_flRimMask` 没在 catalog 但样本里以常量出现) |
| `csgo_water_fancy.vfx`        | 1  | 24 | 76 | **+52** |
| `csgo_moondome.vfx`           | 1  | 14 | 14 | 0 |
| `sky.vfx`                     | 3  | 3  | 4  | +1 (`F_TEXTURE_FORMAT2`) |
| `csgo_effects.vfx`            | 1  | 25 | 26 | +1 (`TextureTranslucency`) |
| `generic.vfx`                 | 1  | 4  | 4  | 0 |

总计: **161** 个 VMAT 样本，扫描得到 **25 个唯一顶层参数**（含纹理），**14 个 Feature flag**、**9 个 Compiled Texture 键**、**1 个 SystemAttributes 键**、**5 个 Attributes 键**。

> 详细原始 dump 见 `tools/scan_out.md`（每 shader 一节，列出每个参数的出现次数、若干示例值、Compiled Textures / SystemAttributes / Attributes 子块键名，以及样本路径示例）。

## 2. 各 Shader 详细差异清单

> 加粗 = 模板缺失但在样本中真实存在的键；斜体 = 模板里分类错误（类型不对或描述错位）；下划线 = 模板已有的键但是默认取值与样本不符。

### 2.1 `csgo_environment.vfx`（样本 48，最常用）

模板 (`csgo_environment.vfx`) 实际只有 **17** 个参数键；实际样本 union **23** 个。

样本里有但模板缺失的键：
- `g_bSnowLayer1` (Bool, 0) — 出现在 `autumn_flashlight01.vmat` / `redleaves.vmat`（雪层覆盖强度）。
- `g_flWetnessDarkeningStrength1` (Float, 1) — 与雪层配对。
- `g_nUVSet1` (Int, 1) — UV 集选择。
- `TextureTintMask1` 在模板里是 Texture 类型但样本里也用 `[vec4]` 作为常量覆盖 (部分材质如 `backroom_sign.vmat` 没写入)。
- `g_flTexCoordRotation1` / `TextureColor1` / `TextureNormal1` / `TextureRoughness1` / `TextureAmbientOcclusion1` / `TextureHeight1` / `TextureMetalness1` 等的尾号 `1` 已收录于模板，但模板把它们叫做 “Color / Albedo” 时未体现这是 Layer1。
- 样本里 `SystemAttributes → PhysicsSurfaceProperties` 出现: `ceiling_tile, carpet, plaster, plaster_drywall, concrete, panel` — 模板没列。

> 补全建议：在 ShaderCatalog 把 snow/wetness/UVSet1 三项纳入 `csgo_environment.vfx` 的 Param 列表；把 `TextureTintMask1` 的 default 从 `_mask.png` 改成 `[1 1 1 0]`（多数样本未填，依赖默认值）；attribute flags 加 `mapbuilder.bakedlighting` 之外，应允许 `PhysicsSurfaceProperties` 子块的透传。

### 2.2 `csgo_vertexlitgeneric.vfx`（样本 61，第二常用）

模板 28 项；实际 38 项。

样本里有但模板缺失的键：
- **Feature flag**:
  - `F_ADDITIVE_BLEND` (1) — 仅 `glowstick_use.vmat`。
  - `F_ALPHA_TEST` (1) — 仅 1 个透明模型。
  - `F_RENDER_BACKFACES` (1) — `particles/fluid` 一类（3 样本）。
  - 模板只声明 `F_SELF_ILLUM`；实际 `F_SPECULAR_DIRECT` (21 样本)、`F_SPECULAR_INDIRECT` (22)、`F_TRANSLUCENT` (8) 也高频出现。
- **Param**:
  - `g_flAlphaTestReference` (Float, 0.5)
  - `g_flAntiAliasedEdgeStrength` (Float, 1)
  - `g_flOpacityScale` (Float, 0.851/0.393/0.913/1/0.814) — 仅当 `F_TRANSLUCENT=1` 时出现。
- **Texture**:
  - `TextureTranslucency` — 仅当 `F_TRANSLUCENT=1` 时出现；样本里有 9 个非零透明纹理（`liquidpain`, `replaceskip`, `royalrations`, `triangles_1`, `hairhed`, `warehouse_shelves`, `t_diamond_iridescent` 等）。
- **SystemAttributes**: 出现 `foliage, carpet, rubbertire, metal`（4 种）。

> 补全建议：在 catalog 里把 `F_SPECULAR_DIRECT / F_SPECULAR_INDIRECT / F_TRANSLUCENT / F_RENDER_BACKFACES` 加为可选 feature flags；`g_flOpacityScale`、`TextureTranslucency` 应仅在 `F_TRANSLUCENT=1` 时显示。

### 2.3 `csgo_complex.vfx`（样本 26，最多样本类型）

模板 26 项；实际 41 项。

样本里有但模板缺失的键：
- **Feature flag**:
  - `F_DETAIL_TEXTURE` (1) — 5 样本 (`backroom_lamplight`, `backroom_levelrun_celling*`, `concretewall011c`, `glowstick_before.vmat`)。
  - `F_SPECULAR` (1) — 1 样本（极少数高光需求）。
- **Param (F_DETAIL_TEXTURE 配套)**:
  - `g_flDetailBlendFactor` (Float, 0.536/0.7/0.8)
  - `g_flDetailBlendToFull` (Float, 0)
  - `g_flDetailTexCoordRotation` (Float, 0)
  - `g_vDetailTexCoordOffset` (Vec4, 0)
  - `g_vDetailTexCoordScale` (Vec4, 6.283×6.283 或 7×7)
- **Param (F_SPECULAR 配套)**:
  - `g_flPhongBoost` (Float, 1)
  - `g_flSpecularExponent` (Float, 90)
- **Texture (F_DETAIL_TEXTURE 配套)**:
  - `TextureDetail` (Texture, `materials/detail/bm_*_detail_*.png`)
  - `TextureDetailMask` (Texture, `materials/default/default_detailmask.tga`)
- **Compiled Textures 新增**: `g_tDetail`, `g_tDetailMask` (模板未列)。
- **SystemAttributes**: 出现 `concrete, plastic, glassbottle, metal`。

> 补全建议：把 detail-blend 与 phong-spec 两组键作为可选 subgroup 加入 catalog（feature flag 控显隐）。

### 2.4 `csgo_static_overlay.vfx`（样本 11，decal 用）

模板 16 项；实际 32 项。

样本里有但模板缺失的键：
- **Feature flag**:
  - `F_LIT` (1) — 5 样本（受光 overlay，默认未点亮）。
- **Param**:
  - `g_flAlphaTestReference` (Float, 0.5/0.804/0.175/0.01)
  - `g_flAntiAliasedEdgeStrength` (Float, 1/0.672)
  - `g_flSelfIllumAlbedoFactor` (Float, 1)
  - `g_flSelfIllumBrightness` (Float, 0)
  - `g_flSelfIllumScale` (Float, 1)
  - `g_flOpacityScale` (Float, 1) — 6 样本。
  - `g_fTextureColorBrightness` / `g_fTextureColorContrast` / `g_fTextureColorSaturation` (Float, 1) — 1 样本（颜色校正组）。
  - `g_vSelfIllumScrollSpeed` (Vec4, 0)
  - `g_vSelfIllumTint` (Vec4, 1)
  - `g_vTextureColorCorrectionTint` (Vec4, 1)
- **Texture**:
  - `TextureAmbientOcclusion` (常量或贴图)
  - `TextureMetalness` (常量或贴图)
  - `TextureNormal` (常量或贴图)
  - `TextureRoughness` (常量或贴图)
  - `TextureSelfIllumMask` (常量或贴图)
- **Compiled Textures**: 模板未列；样本里出现 `g_tAmbientOcclusion, g_tColor, g_tMetalness, g_tNormal, g_tSelfIllumMask`。

> 补全建议：把 `F_LIT` 加进 feature flags；按 `F_LIT=1` 或 `F_BLEND_MODE>=4` 分组暴露 AO/Metalness/Normal/Roughness/SelfIllumMask 五张可选贴图；颜色校正四元组仅在 `F_LIT=1` + `g_fTextureColorBrightness` 等出现时显示。

### 2.5 `csgo_lightmappedgeneric.vfx`（样本 6）

模板 20 项；实际 22 项（数量看似差距小，但漏了关键 group）。

样本里有但模板缺失的键：
- **Feature flag**:
  - `F_DETAILTEXTURE` (1) — 4 样本（注意拼写不同于 `csgo_complex` 的 `F_DETAIL_TEXTURE`）。
- **Param**:
  - `g_vLayer1DetailScale` (Vec4, 4×4 / 1×1 / 4.283×4.283)
  - `g_vLayer1DetailTintAndBlend` (Vec4, `[1 1 1 1]` 或 `[1 1 1 0.8]`)
- **Texture**:
  - `TextureLayer1Detail` (Texture, `materials/default/default_detail.tga` 或 `materials/detail/bm_*.png`)
  - `TextureLayer1Roughness` (Texture, `tiles133a_2k-jpg_normalgl_dd12047d_rough.png` 或 vec4 常量)
  - `TextureLayer1Translucency` (Texture, `[0.470000 0.470000 0.470000 0.000000]`)
- **SystemAttributes**: `tile, concrete, metal`。
- 模板漏掉 `Compiled Textures` 中的 `g_tLayer1Detail` / `g_tLayer1NormalRoughness`（样本里存在）。
- 模板里有但实际样本里没用到：`TextureLayer1AmbientOcclusion` 模板标注 Texture，但样本里多数用 vec4 常量 `[0 0 0 0]`。

> 补全建议：补 `F_DETAILTEXTURE` feature flag；补 detail scale / tint+blend 两组参数；补 `TextureLayer1Detail` / `TextureLayer1Roughness` 贴图；把 `TextureLayer1AmbientOcclusion` 与 `TextureLayer1Translucency` 标注为 “可选 — 未填时使用 vec4 常量”。

### 2.6 `csgo_character.vfx`（样本 2 — 仅 `hazmat1` / `hazmat2`）

模板 21 项；实际 20 项。差距小，但样本里:
- 模板的 `TextureRoughness` 标注 Texture，样本里既出现 Texture (`hazmat2n_3082b0d2_rough.png`) 也允许 vec4 常量。
- 模板的 `TextureMetalness` 样本里也允许 vec4 常量；`TextureAmbientOcclusion` 同理。
- 模板的 `TextureRimMask` 既是 Texture 也可是 vec4 常量（样本里是 vec4 `[1 1 1 0]`）。
- 模板 `attributeFlags` 标 `mapbuilder.character`，但样本里这俩 .vmat 没有任何 `Attributes` 子块。

> 补全建议：把 `TextureRoughness/Metalness/AmbientOcclusion/RimMask` 标记 “Texture 或 Vec4 常量”；attribute flags 标为可选。

### 2.7 `csgo_water_fancy.vfx`（样本 1 — `materials\water\fancy_water.vmat`）⚠️ **最大缺口**

模板 24 项；实际 76 项。**漏掉 52 项**：

#### 缺失参数（按类别分组）

**Caustics 焦散**
- `g_flCausticDepthFallOffDistance` (Float, 256)
- `g_flCausticDistortion` (Float, 0.5)
- `g_flCausticShadowCutOff` (Float, 0.2)
- `g_flCausticSharpness` (Float, 0.9)
- `g_flCausticsStrength` (Float, 16)
- `g_flCausticUVScaleMultiple` (Float, 3)
- `g_vCausticsTint` (Vec4, `[0.501961 0.501961 0.501961 1.000000]`)
- 模板仅存 `F_CAUSTICS` + `g_bUseTriplanarCaustics`，其它六组 + tint 全缺。

**Debris 碎片**
- `g_flDebrisEdgeSharpness` (Float, 10)
- `g_flDebrisMax` / `g_flDebrisMin` (Float, 0)
- `g_flDebrisNormalStrength` (Float, 1)
- `g_flDebrisOilyness` (Float, 0)
- `g_flDebrisReflectance` (Float, 0.1)
- `g_flDebrisScale` (Float, 200)
- `g_flDebrisWobble` (Float, 0.25)
- `g_vDebrisTint` (Vec4, `[0.529412 0.807843 0.921569 1.000000]`)
- 模板里压根没列 debris 这一组。

**Edge 边缘**
- `g_flEdgeHardness` (Float, 100.68)
- `g_flEdgeShapeEffect` (Float, 1)

**Foam 泡沫**
- `g_flFoamMax` / `g_flFoamMin` (Float, 0)
- `g_flFoamScale` (Float, 120)
- `g_flFoamWobble` (Float, 1.504)
- 模板已有 `g_vFoamColor` 与 `TextureFoam`，缺其它。

**SSR 屏幕空间反射**
- `g_flSSRBoost` (Float, 0)
- `g_flSSRBoostThreshold` (Float, 1)
- `g_flSSRBrightness` (Float, 1)
- `g_flSSRMaxThickness` (Float, 1.37)
- `g_flSSRSampleJitter` (Float, 0.003)
- `g_flSSRStepSize` (Float, 0.517)
- 模板仅有 `g_nSSRMaxForwardSteps` (Int, 40)，其余 5 项漏。

**Specular / Bloom 高光**
- `g_flSpecularBloomBoostStrength` (Float, 100)
- `g_flSpecularBloomBoostThreshold` (Float, 0.7)
- `g_flSpecularNormalMultiple` (Float, 2)
- `g_flSpecularPower` (Float, 300)
- 模板里只 `g_flGlossiness` 与 `g_flReflectance`，缺 specular/power/bloom 三组。

**Waves 波形**
- `g_flHighFreqWeight` (Float, 0.3)
- `g_flLowFreqWeight` (Float, 0.2)
- `g_flMedFreqWeight` (Float, 0.4)
- `g_flWavesHeightOffset` (Float, 1.3)
- `g_flWavesNormalJitter` (Float, 0.05)
- `g_flWavesNormalStrength` (Float, 1)
- `g_flWavesPhaseOffset` (Float, 0.5)
- `g_flWavesSharpness` (Float, 0.51)
- `g_flWavesSpeed` (Float, 1)
- `g_vWaveScale` (Vec4, `[40 25 0 0]`)
- 模板里 `g_vWaveScale` 在 union 里没出现（脚本已抓到）；`g_nWaveIterations` (Int, 4) 已有。

**Water Fog / Decay 水雾与衰变**
- `g_flUnderwaterDarkening` (Float, 1)
- `g_flWaterDecayStrength` (Float, 3.659)
- `g_flWaterFogShadowStrength` (Float, 0.572)
- `g_flWaterFogStrength` (Float, 0)
- `g_flWaterInitialDirection` (Float, 1.5)
- `g_flWaterPlaneOffset` (Float, 4)
- `g_flWaterRoughnessMax` (Float, 0.6)
- `g_flWaterRoughnessMin` (Float, 0.622)
- `g_vWaterDecayColor` (Vec4, `[0.792157 0.960784 0.960784 1.000000]`)
- 模板已有 `g_flWaterMaxDepth` 与 `g_vWaterFogColor`，其余 8 项缺。

**Env / Sky Box 反射**
- `g_flEnvironmentMapBrightness` (Float, 1)
- `g_flLowEndCubeMapIntensity` (Float, 1)
- `g_flReflectionDistanceEffect` (Float, 0.5)
- `g_flRefractionLimit` (Float, 0.1)
- `g_flRefractSampleOffset` (Float, 1)
- `g_flSkyBoxFadeRange` (Float, 0)
- `g_flSkyBoxScale` (Float, 16)
- 模板已有 `g_flRefractChromaticSeparation`、`g_flFresnelExponent`、`TextureLowEndCubeMap`，其余缺。

**Map UV 范围**
- `g_vMapUVMax` / `g_vMapUVMin` (Vec4, ±20000)

**Texture 贴图**
- 模板缺:
  - `TextureDebrisHeight` (`materials/water/water_debris_ancient_ab24789a-A.png`)
  - `TextureFoamNormal`（样本里以 vec4 常量 `[0.501961 0.501961 1.000000 0.000000]` 出现）
- 模板里 `TextureFoam` 默认是 PNG；样本里确认是 `materials/water/water_foam_mask.png`，OK。

**Compiled Textures 键**
- 模板未列；样本实际有 `g_tDebris, g_tDebrisNormal, g_tFoam, g_tLowEndCubeMap, g_tWavesNormalHeight`。

**SystemAttributes / Attributes**
- 样本里没有这两块。

> **总补全建议**：把上面 52 项作为可选 subgroup 全部纳入 `csgo_water_fancy.vfx`；按 `F_BLUR_REFRACTION / F_CAUSTICS / F_REFRACTION` 三组 feature flag 控制 subgroup 的可见性。

### 2.8 `csgo_moondome.vfx`（样本 1 — `materials\fake_sky.vmat`）

模板 14 项；实际 14 项。一致。

但样本里 `TextureColor` 用 vec4 常量 `[0.819608 0.866667 1.000000 0.000000]`（非 Texture），模板把它标成 Vec4，OK。`TextureCubeMap` 用 `materials/table_mountain_2_puresky_1k.png`（带 PNG 后缀的 EXR），模板里默认 `materials/skybox/_placeholder.exr`，建议补 `.png` placeholder。

### 2.9 `sky.vfx`（样本 3）

模板 3 项；实际 4 项。

样本里有但模板缺失的键：
- `F_TEXTURE_FORMAT2` (Bool/Int, 0) — `starrynight.vmat` 用了。

样本里 `SkyTexture` 真实值：
- `materials/table_mountain_2_puresky_1k.exr`
- `materials/skybox/mr_53_cube.exr`
- `materials/skybox/starrynight_cube.exr`

模板的 `SkyTexture` 默认 `materials/skybox/_placeholder.exr`，OK；建议增加 `F_TEXTURE_FORMAT2`（默认值 0）作为 feature flag。

### 2.10 `csgo_effects.vfx`（样本 1 — `white_pure2.vmat`）

模板 25 项；实际 26 项。

样本里有但模板缺失的键：
- `TextureTranslucency` (Vec4 常量 `[0 0 0 0]`)

其余与模板完全一致。模板里 `TextureMask1/2/3` 默认 `default_mask.tga`，样本里是 vec4 常量。建议把 `TextureMask*` / `TextureColor` 标为 “Texture 或 Vec4 常量”。

### 2.11 `generic.vfx`（样本 1 — `tools\toolsskyboxfix.vmat`）

模板 4 项；实际 4 项。一致。

样本里 `Attributes` 子块存在 5 个键：
- `mapbuilder.nodraw` (1)
- `mapbuilder.playerclip` (1)
- `mapbuilder.sky` (1)
- `mapbuilder.visblocker` (1)
- `tools.toolsmaterial` (1)

模板 `attributeFlags` 数组已列出全部 5 个，OK。

## 3. 跨 Shader 横向问题

1. **`SystemAttributes` 子块未被任何模板支持**: 实际 4 个 shader（`csgo_environment`, `csgo_complex`, `csgo_vertexlitgeneric`, `csgo_lightmappedgeneric`）的样本都含 `PhysicsSurfaceProperties`。模板当前只能列 attributeFlags（`mapbuilder.*`），缺少对 `SystemAttributes` 块的描述与生成逻辑。建议在 `ShaderTemplate` 上新增 `SystemAttributes` 默认子块（默认 `PhysicsSurfaceProperties = <空>`）。

2. **`Compiled Textures` 键未在模板中体现**: 实际 9 个 Compiled 键出现 (`g_tColor1`, `g_tHeight1`, `g_tNormal1`, `g_tColor`, `g_tLayer1AmbientOcclusion`, `g_tLayer1Detail`, `g_tLayer1NormalRoughness`, `g_tDebris`, `g_tDebrisNormal`, `g_tFoam`, `g_tLowEndCubeMap`, `g_tWavesNormalHeight`, `g_tSkyTexture` 等)。这些键由 Source 2 编译器自动回写，无需编辑；但模板应当提供 placeholder，避免手动编辑器误删。

3. **特征位拼写**: `csgo_complex` 用 `F_DETAIL_TEXTURE`（下划线），`csgo_lightmappedgeneric` 用 `F_DETAILTEXTURE`（无下划线）—— 模板必须按真实 shader 拼写分别收录，否则 Source 2 编译器会忽略。

4. **类型混用**: `Texture*` 键在样本里既可能指向 `.png/.tga/.exr` 路径，也可能是 vec4 常量（如 `TextureRoughness1 [r r r 0]`）。模板应把 Texture 键的类型拓宽为 `Texture | Vector`，并在编辑器中提供 “Use constant” 开关。

5. **样本数 1 的 shader 极易漏字段**: `csgo_water_fancy`、`csgo_moondome`、`csgo_complex`(一部分)、`csgo_environment`(一部分) 的参数 union 仍然可能被现有样本覆盖不全。建议在 catalog 文档里注明 “基于社区泄露 Shader source 反推剩余参数”。

## 4. 补全优先级建议

| 优先级 | Shader | 工作量估计 | 关键补全 |
|---|---|---:|---|
| 🔴 P0 | `csgo_water_fancy.vfx`  | 大（+52 项） | 焦散 / debris / foam / waves / SSR / fog 全套 |
| 🔴 P0 | `csgo_lightmappedgeneric.vfx` | 小（+5 项 + 1 feature） | `F_DETAILTEXTURE`、`g_vLayer1DetailScale`、`g_vLayer1DetailTintAndBlend`、`TextureLayer1Detail` |
| 🟠 P1 | `csgo_static_overlay.vfx` | 中（+15 项 + 1 feature） | 颜色校正组、自发光组、`F_LIT` |
| 🟠 P1 | `csgo_complex.vfx`        | 中（+11 项 + 2 feature） | detail-blend 组、phong-spec 组 |
| 🟡 P2 | `csgo_vertexlitgeneric.vfx` | 中（+10 项 + 4 feature） | 自发光 / 透明 / 双面 / 加色 feature 与配套参数 |
| 🟡 P2 | `csgo_environment.vfx`   | 小（+3 项 + 0 feature） | `g_bSnowLayer1`、`g_flWetnessDarkeningStrength1`、`g_nUVSet1` |
| 🟢 P3 | `sky.vfx`                | 微 | `F_TEXTURE_FORMAT2` |
| 🟢 P3 | `csgo_effects.vfx`       | 微 | `TextureTranslucency` |
| 🟢 P3 | `csgo_moondome.vfx`      | 微 | placeholder 用 `.png` |
| 🟢 P3 | `csgo_character.vfx`     | 微 | 把多张 Texture 标为 “Texture|Vector” |
| 🟢 P3 | `generic.vfx`            | 0 | OK |

## 5. 复现 / 验证脚本

```powershell
cd H:\dsh-workspace\vmat-generater
pwsh -File tools/scan_vmat.ps1     # 生成 tools/scan_out.md
diff (Get-Content tools/scan_out.md) ...   # 比对
```

`tools/scan_out.md` 是本报告的原始数据底稿，按 shader 分组列出 union(params) / features / compiled / systemattributes / attributes 五张表，以及样本路径示例。本报告所列 “差异项” 即从中对照 `ShaderCatalog.cs` 人工核对得出。
