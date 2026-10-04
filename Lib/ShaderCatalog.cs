namespace Lib;

/// <summary>
/// Catalogue of every shader observed in the supplied <c>materials/</c> directory,
/// paired with the parameter / feature-flag set that the parser encountered in the
/// workspace samples. Used by the generator to populate the editor and to emit new
/// VMATs that match the convention already established by the input files.
///
/// The fields below reflect the <em>union</em> of every key present in the
/// workspace samples (see <c>VmatGenerator/docs/shader-analysis.md</c> for the
/// provenance of every addition). Optional subgroup keys are flagged via
/// <see cref="ShaderParamTemplate.RequiredFeatureFlag"/> so the editor can hide
/// them when the controlling feature flag is off.
///
/// 界面标签（每个 ShaderParamTemplate 的第二个参数）已汉化。Shader 内部键名 / feature
/// flag 名 / 物理材质字符串保留英文以保证与 .vmat 文件互通的稳定性。
/// </summary>
public static class ShaderCatalog
{
    public static IReadOnlyList<ShaderTemplate> All { get; } = Build();

    public static ShaderTemplate? Find(string shaderName) =>
        All.FirstOrDefault(t => string.Equals(t.ShaderName, shaderName, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ShaderTemplate> Build() => new List<ShaderTemplate>
    {
        // ─────────────────────────────────────────────────────────────────────
        // csgo_environment.vfx  (48 samples)
        //   - added snow / wetness / UV-set parameters per §2.1 of the analysis.
        //   - SystemAttributes now supports PhysicsSurfaceProperties / LightMapTextureName.
        // ─────────────────────────────────────────────────────────────────────
        new(
            "csgo_environment.vfx",
            "CS:GO 环境材质",
            "用于接受图像光照（IBL）的实体几何 PBR 材质。本 Backrooms 资源中最常用的着色器，支持漫反射 / 法线 / 粗糙度 / 金属度 / AO / 高度 / 染色遮罩。",
            new List<ShaderParamTemplate>
            {
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "1"),
                new("g_bSnowLayer1", "第一层积雪", ShaderParamKind.Bool, "0") { RequiredFeatureFlag = null },
                new("g_flWetnessDarkeningStrength1", "湿润变暗强度 1", ShaderParamKind.Float, "1"),
                new("g_nScaleTexCoordUByModelScaleAxis", "U 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nScaleTexCoordVByModelScaleAxis", "V 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeU", "U 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeV", "V 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nUVSet1", "UV 集 1", ShaderParamKind.Int, "1"),
                new("g_flModelTintAmount", "模型染色强度", ShaderParamKind.Float, "1"),
                new("g_flTexCoordRotation1", "纹理坐标旋转 1", ShaderParamKind.Float, "0"),
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vTexCoordCenter1", "纹理坐标中心 1", ShaderParamKind.Vector, "[0.500000 0.500000 0.000000 0.000000]"),
                new("g_vTexCoordOffset1", "纹理坐标偏移 1", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vTexCoordScale1", "纹理坐标缩放 1", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("TextureRoughness1", "粗糙度常量", ShaderParamKind.Vector, "[0.500000 0.500000 0.500000 0.000000]") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureColor1", "颜色 / 反照率", ShaderParamKind.Texture, "materials/_placeholder/color.png"),
                new("TextureAmbientOcclusion1", "环境光遮蔽", ShaderParamKind.Texture, "materials/default/default_ao.tga"),
                new("TextureHeight1", "高度贴图", ShaderParamKind.Texture, "materials/default/default_height.tga"),
                new("TextureTintMask1", "染色遮罩", ShaderParamKind.Texture, "materials/default/default_height_821f45e2_mask.png") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureMetalness1", "金属度", ShaderParamKind.Texture, "materials/default/default_height_821f45e2_metal.png"),
                new("TextureNormal1", "法线贴图", ShaderParamKind.Texture, "materials/default/default_normal.tga"),
            },
            featureFlags: Array.Empty<string>(),
            attributeFlags: new[] { "mapbuilder.bakedlighting" },
            systemAttributeDefaults: new Dictionary<string, string>
            {
                ["PhysicsSurfaceProperties"] = "plaster",
            },
            compiledTextureKeys: new[] { "g_tColor1", "g_tNormal1", "g_tRoughness1", "g_tMetalness1", "g_tAmbientOcclusion1", "g_tHeight1", "g_tTintMask1" }),

        // ─────────────────────────────────────────────────────────────────────
        // csgo_vertexlitgeneric.vfx  (61 samples)
        //   - added F_ADDITIVE_BLEND / F_ALPHA_TEST / F_RENDER_BACKFACES / F_TRANSLUCENT
        //     / F_SPECULAR_DIRECT / F_SPECULAR_INDIRECT feature flags (§2.2).
        //   - added g_flAlphaTestReference / g_flAntiAliasedEdgeStrength / g_flOpacityScale.
        //   - added TextureTranslucency as a Texture|Vector subgroup.
        // ─────────────────────────────────────────────────────────────────────
        new(
            "csgo_vertexlitgeneric.vfx",
            "CS:GO 顶点光照通用",
            "廉价顶点光照材质，用于自发光贴图类（照片、标识、UI 贴花）。支持漫反射 / 法线 / 粗糙度 / 金属度 / AO / 自发光。",
            new List<ShaderParamTemplate>
            {
                new("F_SELF_ILLUM", "自发光", ShaderParamKind.Bool, "1"),
                new("F_ADDITIVE_BLEND", "加法混合", ShaderParamKind.Bool, "0"),
                new("F_ALPHA_TEST", "Alpha 测试", ShaderParamKind.Bool, "0"),
                new("F_RENDER_BACKFACES", "渲染背面", ShaderParamKind.Bool, "0"),
                new("F_SPECULAR_DIRECT", "直接高光", ShaderParamKind.Bool, "1"),
                new("F_SPECULAR_INDIRECT", "间接高光", ShaderParamKind.Bool, "1"),
                new("F_TRANSLUCENT", "半透明", ShaderParamKind.Bool, "0"),
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "1"),
                new("g_nScaleTexCoordUByModelScaleAxis", "U 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nScaleTexCoordVByModelScaleAxis", "V 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeU", "U 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeV", "V 寻址模式", ShaderParamKind.Int, "0"),
                new("g_flAlphaTestReference", "Alpha 测试阈值", ShaderParamKind.Float, "0.5") { RequiredFeatureFlag = "F_ALPHA_TEST" },
                new("g_flAntiAliasedEdgeStrength", "抗锯齿边缘强度", ShaderParamKind.Float, "1"),
                new("g_flAmbientOcclusionScale", "AO 缩放", ShaderParamKind.Float, "0"),
                new("g_flModelTintAmount", "模型染色强度", ShaderParamKind.Float, "1"),
                new("g_flNormalTexCoordRotation", "法线纹理坐标旋转", ShaderParamKind.Float, "0"),
                new("g_flOpacityScale", "不透明度缩放", ShaderParamKind.Float, "1") { RequiredFeatureFlag = "F_TRANSLUCENT" },
                new("g_flSelfIllumAlbedoFactor", "自发光反照率系数", ShaderParamKind.Float, "1"),
                new("g_flSelfIllumBrightness", "自发光亮度", ShaderParamKind.Float, "0"),
                new("g_flSelfIllumScale", "自发光缩放", ShaderParamKind.Float, "1"),
                new("g_flTexCoordRotation", "基础纹理坐标旋转", ShaderParamKind.Float, "0"),
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vNormalTexCoordCenter", "法线纹理坐标中心", ShaderParamKind.Vector, "[0.500000 0.500000 0.000000 0.000000]"),
                new("g_vNormalTexCoordOffset", "法线纹理坐标偏移", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vNormalTexCoordScale", "法线纹理坐标缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vSelfIllumScrollSpeed", "自发光滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vSelfIllumTint", "自发光染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vTexCoordCenter", "纹理坐标中心", ShaderParamKind.Vector, "[0.500000 0.500000 0.000000 0.000000]"),
                new("g_vTexCoordOffset", "纹理坐标偏移", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vTexCoordScale", "纹理坐标缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vTexCoordScrollSpeed", "纹理坐标滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("TextureSelfIllumMask", "自发光遮罩常量", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureTranslucency", "半透明度", ShaderParamKind.Texture, "materials/_placeholder/trans.png") { RequiredFeatureFlag = "F_TRANSLUCENT", Shape = ShaderValueShape.TextureOrVector },
                new("TextureAmbientOcclusion", "环境光遮蔽", ShaderParamKind.Texture, "materials/default/default_ao.tga"),
                new("TextureColor", "颜色 / 反照率", ShaderParamKind.Texture, "materials/_placeholder/color.png"),
                new("TextureMetalness", "金属度", ShaderParamKind.Texture, "materials/default/default_metal.tga"),
                new("TextureNormal", "法线贴图", ShaderParamKind.Texture, "materials/default/default_normal.tga"),
                new("TextureRoughness", "粗糙度", ShaderParamKind.Texture, "materials/default/default_7be61377_rough.png"),
            },
            featureFlags: new[] { "F_SELF_ILLUM", "F_ADDITIVE_BLEND", "F_ALPHA_TEST", "F_RENDER_BACKFACES", "F_SPECULAR_DIRECT", "F_SPECULAR_INDIRECT", "F_TRANSLUCENT" },
            attributeFlags: Array.Empty<string>(),
            systemAttributeDefaults: new Dictionary<string, string>
            {
                ["PhysicsSurfaceProperties"] = "metal",
            }),

        // ─────────────────────────────────────────────────────────────────────
        // csgo_complex.vfx  (26 samples)
        //   - added F_DETAIL_TEXTURE (note the underscore — different from
        //     csgo_lightmappedgeneric) and F_SPECULAR (§2.3).
        //   - added detail-blend and phong-spec subgroups.
        // ─────────────────────────────────────────────────────────────────────
        new(
            "csgo_complex.vfx",
            "CS:GO 复合材质",
            "用于半透明 / 自发光道具（灯具、黏液、防化服）的 PBR 材质。在环境材质基础上增加金属度、半透明、自发光控制。",
            new List<ShaderParamTemplate>
            {
                new("F_TRANSLUCENT", "半透明", ShaderParamKind.Bool, "0"),
                new("F_SELF_ILLUM", "自发光", ShaderParamKind.Bool, "0"),
                new("F_DETAIL_TEXTURE", "细节贴图", ShaderParamKind.Bool, "0"),
                new("F_SPECULAR", "高光", ShaderParamKind.Bool, "0"),
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "1"),
                new("g_nScaleTexCoordUByModelScaleAxis", "U 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nScaleTexCoordVByModelScaleAxis", "V 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeU", "U 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeV", "V 寻址模式", ShaderParamKind.Int, "0"),
                new("g_flDetailBlendFactor", "细节混合系数", ShaderParamKind.Float, "0.5") { RequiredFeatureFlag = "F_DETAIL_TEXTURE" },
                new("g_flDetailBlendToFull", "细节混合至全覆盖", ShaderParamKind.Float, "0") { RequiredFeatureFlag = "F_DETAIL_TEXTURE" },
                new("g_flDetailTexCoordRotation", "细节纹理坐标旋转", ShaderParamKind.Float, "0") { RequiredFeatureFlag = "F_DETAIL_TEXTURE" },
                new("g_flMetalness", "金属度常量", ShaderParamKind.Float, "0"),
                new("g_flModelTintAmount", "模型染色强度", ShaderParamKind.Float, "1"),
                new("g_flOpacityScale", "不透明度缩放", ShaderParamKind.Float, "1"),
                new("g_flPhongBoost", "Phong 增强", ShaderParamKind.Float, "1") { RequiredFeatureFlag = "F_SPECULAR" },
                new("g_flSelfIllumAlbedoFactor", "自发光反照率系数", ShaderParamKind.Float, "1"),
                new("g_flSelfIllumBrightness", "自发光亮度", ShaderParamKind.Float, "0"),
                new("g_flSelfIllumScale", "自发光缩放", ShaderParamKind.Float, "1"),
                new("g_flSpecularExponent", "高光指数", ShaderParamKind.Float, "90") { RequiredFeatureFlag = "F_SPECULAR" },
                new("g_flTexCoordRotation", "纹理坐标旋转", ShaderParamKind.Float, "0"),
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vDetailTexCoordOffset", "细节纹理坐标偏移", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]") { RequiredFeatureFlag = "F_DETAIL_TEXTURE" },
                new("g_vDetailTexCoordScale", "细节纹理坐标缩放", ShaderParamKind.Vector, "[6.283185 6.283185 0.000000 0.000000]") { RequiredFeatureFlag = "F_DETAIL_TEXTURE" },
                new("g_vSelfIllumScrollSpeed", "自发光滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vSelfIllumTint", "自发光染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vTexCoordCenter", "纹理坐标中心", ShaderParamKind.Vector, "[0.500000 0.500000 0.000000 0.000000]"),
                new("g_vTexCoordOffset", "纹理坐标偏移", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vTexCoordScale", "纹理坐标缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vTexCoordScrollSpeed", "纹理坐标滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("TextureSelfIllumMask", "自发光遮罩常量", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureTranslucency", "半透明度常量", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureDetail", "细节贴图", ShaderParamKind.Texture, "materials/detail/_placeholder_detail.png") { RequiredFeatureFlag = "F_DETAIL_TEXTURE" },
                new("TextureDetailMask", "细节遮罩", ShaderParamKind.Texture, "materials/default/default_detailmask.tga") { RequiredFeatureFlag = "F_DETAIL_TEXTURE" },
                new("TextureAmbientOcclusion", "环境光遮蔽", ShaderParamKind.Texture, "materials/default/default_ao.tga"),
                new("TextureColor", "颜色 / 反照率", ShaderParamKind.Texture, "materials/_placeholder/color.png"),
                new("TextureNormal", "法线贴图", ShaderParamKind.Texture, "materials/default/default_normal.tga"),
                new("TextureRoughness", "粗糙度", ShaderParamKind.Texture, "materials/default/default_7be61377_rough.png"),
            },
            featureFlags: new[] { "F_TRANSLUCENT", "F_SELF_ILLUM", "F_DETAIL_TEXTURE", "F_SPECULAR" },
            attributeFlags: Array.Empty<string>(),
            systemAttributeDefaults: new Dictionary<string, string>
            {
                ["PhysicsSurfaceProperties"] = "metal",
            },
            compiledTextureKeys: new[] { "g_tDetail", "g_tDetailMask" }),

        // ─────────────────────────────────────────────────────────────────────
        // csgo_static_overlay.vfx  (11 samples, decal)
        //   - added F_LIT, self-illum and colour-correction subgroups (§2.4).
        //   - TextureRoughness/AO/Metalness/Normal/SelfIllumMask all gain
        //     Texture|Vector shape.
        // ─────────────────────────────────────────────────────────────────────
        new(
            "csgo_static_overlay.vfx",
            "CS:GO 静态覆盖",
            "用于贴花的混合材质，支持喷溅、模板等需要非标准混合模式（加法、半透明等）的覆盖层。",
            new List<ShaderParamTemplate>
            {
                new("F_BLEND_MODE", "混合模式 (0..3)", ShaderParamKind.Int, "3"),
                new("F_LIT", "受光", ShaderParamKind.Bool, "0"),
                new("F_SELF_ILLUM", "自发光", ShaderParamKind.Bool, "0"),
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "1"),
                new("g_nScaleTexCoordUByModelScaleAxis", "U 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nScaleTexCoordVByModelScaleAxis", "V 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeU", "U 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeV", "V 寻址模式", ShaderParamKind.Int, "0"),
                new("g_flAlphaTestReference", "Alpha 测试阈值", ShaderParamKind.Float, "0.5"),
                new("g_flAntiAliasedEdgeStrength", "抗锯齿边缘强度", ShaderParamKind.Float, "1"),
                new("g_flModelTintAmount", "模型染色强度", ShaderParamKind.Float, "1"),
                new("g_flOpacityScale", "不透明度缩放", ShaderParamKind.Float, "1"),
                new("g_flSelfIllumAlbedoFactor", "自发光反照率系数", ShaderParamKind.Float, "1"),
                new("g_flSelfIllumBrightness", "自发光亮度", ShaderParamKind.Float, "0"),
                new("g_flSelfIllumScale", "自发光缩放", ShaderParamKind.Float, "1"),
                new("g_flTexCoordRotation", "纹理坐标旋转", ShaderParamKind.Float, "0"),
                new("g_fTextureColorBrightness", "颜色亮度", ShaderParamKind.Float, "1") { RequiredFeatureFlag = "F_LIT" },
                new("g_fTextureColorContrast", "颜色对比度", ShaderParamKind.Float, "1") { RequiredFeatureFlag = "F_LIT" },
                new("g_fTextureColorSaturation", "颜色饱和度", ShaderParamKind.Float, "1") { RequiredFeatureFlag = "F_LIT" },
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vSelfIllumScrollSpeed", "自发光滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vSelfIllumTint", "自发光染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vTexCoordCenter", "纹理坐标中心", ShaderParamKind.Vector, "[0.500000 0.500000 0.000000 0.000000]"),
                new("g_vTexCoordOffset", "纹理坐标偏移", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vTexCoordScale", "纹理坐标缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vTexCoordScrollSpeed", "纹理坐标滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vTextureColorCorrectionTint", "颜色校正染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]") { RequiredFeatureFlag = "F_LIT" },
                new("TextureColor", "颜色 / 反照率", ShaderParamKind.Texture, "materials/_placeholder/color.png"),
                new("TextureAmbientOcclusion", "环境光遮蔽", ShaderParamKind.Texture, "materials/default/default_ao.tga") { RequiredFeatureFlag = "F_LIT", Shape = ShaderValueShape.TextureOrVector },
                new("TextureMetalness", "金属度", ShaderParamKind.Texture, "materials/default/default_metal.tga") { RequiredFeatureFlag = "F_LIT", Shape = ShaderValueShape.TextureOrVector },
                new("TextureNormal", "法线贴图", ShaderParamKind.Texture, "materials/default/default_normal.tga") { RequiredFeatureFlag = "F_LIT", Shape = ShaderValueShape.TextureOrVector },
                new("TextureRoughness", "粗糙度", ShaderParamKind.Texture, "materials/default/default_7be61377_rough.png") { RequiredFeatureFlag = "F_LIT", Shape = ShaderValueShape.TextureOrVector },
                new("TextureSelfIllumMask", "自发光遮罩", ShaderParamKind.Texture, "materials/default/default_white.png") { RequiredFeatureFlag = "F_SELF_ILLUM", Shape = ShaderValueShape.TextureOrVector },
                new("TextureTranslucency", "半透明度", ShaderParamKind.Texture, "materials/_placeholder/trans.png"),
            },
            featureFlags: new[] { "F_LIT", "F_SELF_ILLUM" },
            attributeFlags: new[] { "mapbuilder.decal" },
            compiledTextureKeys: new[] { "g_tAmbientOcclusion", "g_tColor", "g_tMetalness", "g_tNormal", "g_tSelfIllumMask" }),

        // ─────────────────────────────────────────────────────────────────────
        // csgo_lightmappedgeneric.vfx  (6 samples, brush)
        //   - added F_DETAILTEXTURE (no underscore — different spelling from
        //     csgo_complex) plus detail-blend parameters (§2.5).
        //   - SystemAttributes now supports LightMapTextureName.
        // ─────────────────────────────────────────────────────────────────────
        new(
            "csgo_lightmappedgeneric.vfx",
            "CS:GO 光照贴图通用",
            "仅限笔刷的 PBR 材质，使用烘焙光照贴图。常用于玻璃、泳池砖等地图几何。支持在顶部叠加第一层细节。",
            new List<ShaderParamTemplate>
            {
                new("F_DETAILTEXTURE", "细节贴图", ShaderParamKind.Bool, "0"),
                new("F_SPECULAR_DIRECT", "直接高光", ShaderParamKind.Bool, "1"),
                new("F_SPECULAR_INDIRECT", "间接高光", ShaderParamKind.Bool, "1"),
                new("F_TRANSLUCENT", "半透明", ShaderParamKind.Bool, "0"),
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "1"),
                new("g_nTextureAddressModeU", "U 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeV", "V 寻址模式", ShaderParamKind.Int, "0"),
                new("g_flMetalness", "金属度常量", ShaderParamKind.Float, "0"),
                new("g_flModelTintAmount", "模型染色强度", ShaderParamKind.Float, "1"),
                new("g_flOpacityScale", "不透明度缩放", ShaderParamKind.Float, "1"),
                new("g_flVertexColorOpacityScale", "顶点色不透明度缩放", ShaderParamKind.Float, "1"),
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vLayer1DetailScale", "第一层细节缩放", ShaderParamKind.Vector, "[4.000000 4.000000 0.000000 0.000000]") { RequiredFeatureFlag = "F_DETAILTEXTURE" },
                new("g_vLayer1DetailTintAndBlend", "第一层细节染色 + 混合", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 1.000000]") { RequiredFeatureFlag = "F_DETAILTEXTURE" },
                new("g_vLayer1Tint", "第一层染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("TextureLayer1Color", "第一层颜色", ShaderParamKind.Texture, "materials/_placeholder/color.png"),
                new("TextureLayer1AmbientOcclusion", "第一层 AO", ShaderParamKind.Texture, "materials/default/default_ao.tga") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureLayer1Detail", "第一层细节", ShaderParamKind.Texture, "materials/default/default_detail.tga") { RequiredFeatureFlag = "F_DETAILTEXTURE" },
                new("TextureLayer1Normal", "第一层法线", ShaderParamKind.Texture, "materials/default/default_normal.tga"),
                new("TextureLayer1Roughness", "第一层粗糙度", ShaderParamKind.Texture, "materials/default/default_7be61377_rough.png") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureLayer1Translucency", "第一层半透明度", ShaderParamKind.Texture, "materials/default/default_white.png") { Shape = ShaderValueShape.TextureOrVector },
            },
            featureFlags: new[] { "F_DETAILTEXTURE", "F_SPECULAR_DIRECT", "F_SPECULAR_INDIRECT", "F_TRANSLUCENT" },
            attributeFlags: new[] { "mapbuilder.lightmap" },
            systemAttributeDefaults: new Dictionary<string, string>
            {
                ["PhysicsSurfaceProperties"] = "tile",
                ["LightMapTextureName"] = "materials/_placeholder/lightmap.vtex",
            },
            compiledTextureKeys: new[] { "g_tColor", "g_tLayer1AmbientOcclusion", "g_tLayer1Detail", "g_tLayer1NormalRoughness" }),

        // ─────────────────────────────────────────────────────────────────────
        // csgo_character.vfx  (2 samples — hazmat)
        //   - widened Texture* keys to Texture|Vector (§2.6).
        // ─────────────────────────────────────────────────────────────────────
        new(
            "csgo_character.vfx",
            "CS:GO 角色",
            "玩家 / NPC 模型的皮肤着色器。在标准 PBR 基础上增加边缘光、布料遮罩、金属度调节。",
            new List<ShaderParamTemplate>
            {
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "1"),
                new("g_nScaleTexCoordUByModelScaleAxis", "U 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nScaleTexCoordVByModelScaleAxis", "V 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeU", "U 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeV", "V 寻址模式", ShaderParamKind.Int, "0"),
                new("g_flAmbientOcclusionMasking", "AO 遮蔽", ShaderParamKind.Float, "0.4"),
                new("g_flModelTintAmount", "模型染色强度", ShaderParamKind.Float, "1"),
                new("g_flTexCoordRotation", "纹理坐标旋转", ShaderParamKind.Float, "0"),
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vTexCoordCenter", "纹理坐标中心", ShaderParamKind.Vector, "[0.500000 0.500000 0.000000 0.000000]"),
                new("g_vTexCoordOffset", "纹理坐标偏移", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vTexCoordScale", "纹理坐标缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vTexCoordScrollSpeed", "纹理坐标滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("TextureRimMask", "边缘光遮罩常量", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureAmbientOcclusion", "环境光遮蔽", ShaderParamKind.Texture, "materials/default/default_ao.tga") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureColor", "颜色 / 反照率", ShaderParamKind.Texture, "materials/_placeholder/color.png"),
                new("TextureMetalness", "金属度", ShaderParamKind.Texture, "materials/default/default_metal.tga") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureCloth", "布料遮罩", ShaderParamKind.Texture, "materials/_placeholder/cloth.png"),
                new("TextureNormal", "法线贴图", ShaderParamKind.Texture, "materials/default/default_normal.tga"),
                new("TextureRoughness", "粗糙度", ShaderParamKind.Texture, "materials/default/default_7be61377_rough.png") { Shape = ShaderValueShape.TextureOrVector },
            },
            featureFlags: Array.Empty<string>(),
            attributeFlags: new[] { "mapbuilder.character" }),

        // ─────────────────────────────────────────────────────────────────────
        // csgo_water_fancy.vfx  (1 sample — fancy_water.vmat)
        //   - added the full 52 missing keys (Caustics / Debris / Edge / Foam /
        //     SSR / Specular / Bloom / Waves / Water Fog / Env / Sky / MapUV /
        //     TextureFoamNormal / TextureDebrisHeight) per §2.7.
        // ─────────────────────────────────────────────────────────────────────
        new(
            "csgo_water_fancy.vfx",
            "CS:GO 高级水面",
            "带泡沫、漂浮物与双层涟漪法线贴图的专用折射水面着色器。所有已观测到的控制项均暴露为标量浮点。",
            new List<ShaderParamTemplate>
            {
                new("F_BLUR_REFRACTION", "折射模糊", ShaderParamKind.Bool, "1"),
                new("F_CAUSTICS", "焦散", ShaderParamKind.Bool, "1"),
                new("F_REFLECTION_TYPE", "反射类型", ShaderParamKind.Int, "2"),
                new("F_REFRACTION", "折射", ShaderParamKind.Bool, "1"),
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "1"),
                new("g_bUseTriplanarCaustics", "三平面焦散", ShaderParamKind.Bool, "0"),
                // 焦散
                new("g_flCausticDepthFallOffDistance", "焦散深度衰减距离", ShaderParamKind.Float, "256") { RequiredFeatureFlag = "F_CAUSTICS" },
                new("g_flCausticDistortion", "焦散扭曲", ShaderParamKind.Float, "0.5") { RequiredFeatureFlag = "F_CAUSTICS" },
                new("g_flCausticShadowCutOff", "焦散阴影截止", ShaderParamKind.Float, "0.2") { RequiredFeatureFlag = "F_CAUSTICS" },
                new("g_flCausticSharpness", "焦散锐度", ShaderParamKind.Float, "0.9") { RequiredFeatureFlag = "F_CAUSTICS" },
                new("g_flCausticUVScaleMultiple", "焦散 UV 缩放倍数", ShaderParamKind.Float, "3") { RequiredFeatureFlag = "F_CAUSTICS" },
                new("g_flCausticsStrength", "焦散强度", ShaderParamKind.Float, "16") { RequiredFeatureFlag = "F_CAUSTICS" },
                // 漂浮物
                new("g_flDebrisEdgeSharpness", "漂浮物边缘锐度", ShaderParamKind.Float, "10"),
                new("g_flDebrisMax", "漂浮物最大", ShaderParamKind.Float, "0"),
                new("g_flDebrisMin", "漂浮物最小", ShaderParamKind.Float, "0"),
                new("g_flDebrisNormalStrength", "漂浮物法线强度", ShaderParamKind.Float, "1"),
                new("g_flDebrisOilyness", "漂浮物油感", ShaderParamKind.Float, "0"),
                new("g_flDebrisReflectance", "漂浮物反射率", ShaderParamKind.Float, "0.1"),
                new("g_flDebrisScale", "漂浮物缩放", ShaderParamKind.Float, "200"),
                new("g_flDebrisWobble", "漂浮物摆动", ShaderParamKind.Float, "0.25"),
                // 边缘
                new("g_flEdgeHardness", "边缘硬度", ShaderParamKind.Float, "100.68"),
                new("g_flEdgeShapeEffect", "边缘形状效果", ShaderParamKind.Float, "1"),
                // 环境 / 天空
                new("g_flEnvironmentMapBrightness", "环境贴图亮度", ShaderParamKind.Float, "1"),
                new("g_flLowEndCubeMapIntensity", "低端Cube Map强度", ShaderParamKind.Float, "1"),
                new("g_flRainStrength", "雨强度", ShaderParamKind.Float, "0"),
                new("g_flReflectionDistanceEffect", "反射距离效果", ShaderParamKind.Float, "0.5") { RequiredFeatureFlag = "F_REFRACTION" },
                new("g_flRefractionLimit", "折射上限", ShaderParamKind.Float, "0.1") { RequiredFeatureFlag = "F_REFRACTION" },
                new("g_flRefractSampleOffset", "折射采样偏移", ShaderParamKind.Float, "1") { RequiredFeatureFlag = "F_REFRACTION" },
                new("g_flSkyBoxFadeRange", "天空盒渐隐范围", ShaderParamKind.Float, "0"),
                new("g_flSkyBoxScale", "天空盒缩放", ShaderParamKind.Float, "16"),
                // 泡沫
                new("g_flFoamMax", "泡沫最大", ShaderParamKind.Float, "0"),
                new("g_flFoamMin", "泡沫最小", ShaderParamKind.Float, "0"),
                new("g_flFoamScale", "泡沫缩放", ShaderParamKind.Float, "120"),
                new("g_flFoamWobble", "泡沫摆动", ShaderParamKind.Float, "1.504"),
                // 高光 / 泛光
                new("g_flSpecularBloomBoostStrength", "高光泛光增强强度", ShaderParamKind.Float, "100"),
                new("g_flSpecularBloomBoostThreshold", "高光泛光增强阈值", ShaderParamKind.Float, "0.7"),
                new("g_flSpecularNormalMultiple", "高光法线倍数", ShaderParamKind.Float, "2"),
                new("g_flSpecularPower", "高光强度", ShaderParamKind.Float, "300"),
                // 屏幕空间反射 SSR
                new("g_flSSRBoost", "SSR 增强", ShaderParamKind.Float, "0"),
                new("g_flSSRBoostThreshold", "SSR 增强阈值", ShaderParamKind.Float, "1"),
                new("g_flSSRBrightness", "SSR 亮度", ShaderParamKind.Float, "1"),
                new("g_flSSRMaxThickness", "SSR 最大厚度", ShaderParamKind.Float, "1.37"),
                new("g_flSSRSampleJitter", "SSR 采样抖动", ShaderParamKind.Float, "0.003"),
                new("g_flSSRStepSize", "SSR 步长", ShaderParamKind.Float, "0.517"),
                // 基础标量
                new("g_flFresnelExponent", "菲涅尔指数", ShaderParamKind.Float, "7"),
                new("g_flGlossiness", "光泽度", ShaderParamKind.Float, "0.75"),
                new("g_flReflectance", "反射率", ShaderParamKind.Float, "0.255"),
                new("g_flRefractChromaticSeparation", "折射色散分离", ShaderParamKind.Float, "0.5") { RequiredFeatureFlag = "F_REFRACTION" },
                new("g_flTexCoordRotation", "纹理坐标旋转", ShaderParamKind.Float, "0"),
                new("g_flWaterMaxDepth", "水面最大深度", ShaderParamKind.Float, "1.463"),
                // 波浪
                new("g_flHighFreqWeight", "高频权重", ShaderParamKind.Float, "0.3"),
                new("g_flLowFreqWeight", "低频权重", ShaderParamKind.Float, "0.2"),
                new("g_flMedFreqWeight", "中频权重", ShaderParamKind.Float, "0.4"),
                new("g_flWavesHeightOffset", "波浪高度偏移", ShaderParamKind.Float, "1.3"),
                new("g_flWavesNormalJitter", "波浪法线抖动", ShaderParamKind.Float, "0.05"),
                new("g_flWavesNormalStrength", "波浪法线强度", ShaderParamKind.Float, "1"),
                new("g_flWavesPhaseOffset", "波浪相位偏移", ShaderParamKind.Float, "0.5"),
                new("g_flWavesSharpness", "波浪锐度", ShaderParamKind.Float, "0.51"),
                new("g_flWavesSpeed", "波浪速度", ShaderParamKind.Float, "1"),
                // 水雾 / 衰减
                new("g_flUnderwaterDarkening", "水下变暗", ShaderParamKind.Float, "1"),
                new("g_flWaterDecayStrength", "水面衰减强度", ShaderParamKind.Float, "3.659"),
                new("g_flWaterFogShadowStrength", "水雾阴影强度", ShaderParamKind.Float, "0.572"),
                new("g_flWaterFogStrength", "水雾强度", ShaderParamKind.Float, "0"),
                new("g_flWaterInitialDirection", "水面初始方向", ShaderParamKind.Float, "1.5"),
                new("g_flWaterPlaneOffset", "水面平面偏移", ShaderParamKind.Float, "4"),
                new("g_flWaterRoughnessMax", "水面粗糙度最大", ShaderParamKind.Float, "0.6"),
                new("g_flWaterRoughnessMin", "水面粗糙度最小", ShaderParamKind.Float, "0.622"),
                // 整数
                new("g_nScaleTexCoordUByModelScaleAxis", "U 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nScaleTexCoordVByModelScaleAxis", "V 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nSSRMaxForwardSteps", "SSR 最大前向步数", ShaderParamKind.Int, "40"),
                new("g_nTextureAddressModeU", "U 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeV", "V 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nWaveIterations", "波浪迭代次数", ShaderParamKind.Int, "4"),
                // 向量
                new("g_vCausticsTint", "焦散染色", ShaderParamKind.Vector, "[0.501961 0.501961 0.501961 1.000000]") { RequiredFeatureFlag = "F_CAUSTICS" },
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vDebrisTint", "漂浮物染色", ShaderParamKind.Vector, "[0.529412 0.807843 0.921569 1.000000]"),
                new("g_vFoamColor", "泡沫颜色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 1.000000]"),
                new("g_vMapUVMax", "地图 UV 上限", ShaderParamKind.Vector, "[20000.000000 20000.000000 0.000000 0.000000]"),
                new("g_vMapUVMin", "地图 UV 下限", ShaderParamKind.Vector, "[-20000.000000 -20000.000000 0.000000 0.000000]"),
                new("g_vTexCoordCenter", "纹理坐标中心", ShaderParamKind.Vector, "[0.500000 0.500000 0.000000 0.000000]"),
                new("g_vTexCoordOffset", "纹理坐标偏移", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vTexCoordScale", "纹理坐标缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vTexCoordScrollSpeed", "纹理坐标滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vWaterDecayColor", "水面衰减颜色", ShaderParamKind.Vector, "[0.792157 0.960784 0.960784 1.000000]"),
                new("g_vWaterFogColor", "水雾颜色", ShaderParamKind.Vector, "[0.364706 0.486275 0.541176 1.000000]"),
                new("g_vWaveScale", "波浪缩放", ShaderParamKind.Vector, "[40.000000 25.000000 0.000000 0.000000]"),
                // 贴图
                new("TextureFoam", "泡沫遮罩", ShaderParamKind.Texture, "materials/water/water_foam_mask.png"),
                new("TextureFoamNormal", "泡沫法线", ShaderParamKind.Vector, "[0.501961 0.501961 1.000000 0.000000]") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureDebris", "漂浮物颜色", ShaderParamKind.Texture, "materials/water/water_debris_ancient_color.png"),
                new("TextureDebrisHeight", "漂浮物高度", ShaderParamKind.Texture, "materials/water/water_debris_ancient_height.png"),
                new("TextureDebrisNormal", "漂浮物法线", ShaderParamKind.Texture, "materials/water/water_debris_ancient_normal.png"),
                new("TextureLowEndCubeMap", "低端 Cube Map", ShaderParamKind.Texture, "materials/default/default_cube.pfm"),
                new("TextureWavesNormal", "波浪法线", ShaderParamKind.Texture, "materials/water/water_substance_soft_normal.png"),
                new("TextureWavesHeight", "波浪高度", ShaderParamKind.Texture, "materials/water/water_substance_soft_height.png"),
            },
            featureFlags: new[] { "F_BLUR_REFRACTION", "F_CAUSTICS", "F_REFRACTION" },
            attributeFlags: new[] { "mapbuilder.water" },
            compiledTextureKeys: new[]
            {
                "g_tDebris", "g_tDebrisNormal", "g_tDebrisHeight",
                "g_tFoam", "g_tFoamNormal",
                "g_tLowEndCubeMap",
                "g_tWavesNormalHeight", "g_tWavesHeight",
            }),

        // ─────────────────────────────────────────────────────────────────────
        // csgo_moondome.vfx  (1 sample — fake_sky.vmat)
        //   - widened TextureColor to Texture|Vector (§2.8).
        // ─────────────────────────────────────────────────────────────────────
        new(
            "csgo_moondome.vfx",
            "CS:GO 模拟天穹",
            "将颜色染色与天空 Cube Map 以及可选视差偏移进行混合的天穹着色器，用于伪造 / 室内天空。",
            new List<ShaderParamTemplate>
            {
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "0"),
                new("g_nScaleTexCoordUByModelScaleAxis", "U 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nScaleTexCoordVByModelScaleAxis", "V 坐标按模型缩放", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeU", "U 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeV", "V 寻址模式", ShaderParamKind.Int, "0"),
                new("g_flCubeParallax", "Cube 视差", ShaderParamKind.Float, "0"),
                new("g_flTexCoordRotation", "纹理坐标旋转", ShaderParamKind.Float, "0"),
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vTexCoordCenter", "纹理坐标中心", ShaderParamKind.Vector, "[0.500000 0.500000 0.000000 0.000000]"),
                new("g_vTexCoordOffset", "纹理坐标偏移", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vTexCoordScale", "纹理坐标缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vTexCoordScrollSpeed", "纹理坐标滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("TextureColor", "颜色染色常量", ShaderParamKind.Vector, "[0.819608 0.866667 1.000000 0.000000]") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureCubeMap", "Cube 贴图", ShaderParamKind.Texture, "materials/skybox/_placeholder.exr"),
            },
            featureFlags: Array.Empty<string>(),
            attributeFlags: new[] { "mapbuilder.sky" }),

        // ─────────────────────────────────────────────────────────────────────
        // sky.vfx  (3 samples)
        //   - added F_TEXTURE_FORMAT2 (§2.9).
        // ─────────────────────────────────────────────────────────────────────
        new(
            "sky.vfx",
            "天空盒",
            "轻量 HDR 天空盒着色器。仅需一张 Cube 贴图外加曝光偏移。",
            new List<ShaderParamTemplate>
            {
                new("F_TEXTURE_FORMAT2", "纹理格式 2", ShaderParamKind.Bool, "0"),
                new("g_flBrightnessExposureBias", "亮度曝光偏置", ShaderParamKind.Float, "0"),
                new("g_flRenderOnlyExposureBias", "仅渲染曝光偏置", ShaderParamKind.Float, "0"),
                new("SkyTexture", "天空贴图", ShaderParamKind.Texture, "materials/skybox/_placeholder.exr"),
            },
            featureFlags: new[] { "F_TEXTURE_FORMAT2" },
            attributeFlags: new[] { "mapbuilder.sky" }),

        // ─────────────────────────────────────────────────────────────────────
        // csgo_effects.vfx  (1 sample — white_pure2.vmat)
        //   - added TextureTranslucency, widened TextureMask* / TextureColor
        //     to Texture|Vector (§2.10).
        // ─────────────────────────────────────────────────────────────────────
        new(
            "csgo_effects.vfx",
            "CS:GO 特效",
            "面向粒子 / 发光的着色器，含颜色染色、三组独立遮罩缩放与菲涅尔衰减。用于加法发光覆盖层。",
            new List<ShaderParamTemplate>
            {
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "1"),
                new("g_nTextureAddressModeU", "U 寻址模式", ShaderParamKind.Int, "0"),
                new("g_nTextureAddressModeV", "V 寻址模式", ShaderParamKind.Int, "0"),
                new("g_flColorBoost", "颜色增强", ShaderParamKind.Float, "1"),
                new("g_flFadeDistance", "渐隐距离", ShaderParamKind.Float, "1"),
                new("g_flFadeFalloff", "渐隐衰减", ShaderParamKind.Float, "1"),
                new("g_flFadeMax", "渐隐最大", ShaderParamKind.Float, "1"),
                new("g_flFadeMin", "渐隐最小", ShaderParamKind.Float, "0"),
                new("g_flFresnelExponent", "菲涅尔指数", ShaderParamKind.Float, "0.001"),
                new("g_flFresnelFalloff", "菲涅尔衰减", ShaderParamKind.Float, "1"),
                new("g_flFresnelMax", "菲涅尔最大", ShaderParamKind.Float, "1"),
                new("g_flFresnelMin", "菲涅尔最小", ShaderParamKind.Float, "0"),
                new("g_flOpacityScale", "不透明度缩放", ShaderParamKind.Float, "1"),
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("g_vMask1PanSpeed", "遮罩 1 平移速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vMask1Scale", "遮罩 1 缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vMask2PanSpeed", "遮罩 2 平移速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vMask2Scale", "遮罩 2 缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vMask3PanSpeed", "遮罩 3 平移速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("g_vMask3Scale", "遮罩 3 缩放", ShaderParamKind.Vector, "[1.000000 1.000000 0.000000 0.000000]"),
                new("g_vTexCoordScrollSpeed", "纹理坐标滚动速度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]"),
                new("TextureColor", "颜色常量", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureMask1", "遮罩 1", ShaderParamKind.Texture, "materials/default/default_mask.tga") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureMask2", "遮罩 2", ShaderParamKind.Texture, "materials/default/default_mask.tga") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureMask3", "遮罩 3", ShaderParamKind.Texture, "materials/default/default_mask.tga") { Shape = ShaderValueShape.TextureOrVector },
                new("TextureTranslucency", "半透明度", ShaderParamKind.Vector, "[0.000000 0.000000 0.000000 0.000000]") { Shape = ShaderValueShape.TextureOrVector },
            },
            featureFlags: Array.Empty<string>(),
            attributeFlags: new[] { "mapbuilder.effect" }),

        // ─────────────────────────────────────────────────────────────────────
        // generic.vfx  (1 sample — toolsskyboxfix.vmat)
        // ─────────────────────────────────────────────────────────────────────
        new(
            "generic.vfx",
            "通用",
            "回退无光照着色器。多用于工具材质（天空盒遮挡体、裁切笔刷、不绘制表面）与编辑器占位。",
            new List<ShaderParamTemplate>
            {
                new("F_UNLIT", "无光照", ShaderParamKind.Bool, "1"),
                new("g_bFogEnabled", "启用雾效", ShaderParamKind.Bool, "1"),
                new("g_vColorTint", "颜色染色", ShaderParamKind.Vector, "[1.000000 1.000000 1.000000 0.000000]"),
                new("TextureColor", "颜色 / 反照率", ShaderParamKind.Texture, "materials/tools/_placeholder.png"),
            },
            featureFlags: new[] { "F_UNLIT" },
            attributeFlags: new[] { "mapbuilder.nodraw", "mapbuilder.playerclip", "mapbuilder.sky", "mapbuilder.visblocker", "tools.toolsmaterial" }),
    };
}