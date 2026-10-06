namespace Lib;

/// <summary>单条自检用例的结果。</summary>
public sealed class SelfCheckCase
{
    /// <summary>构造用例结果。</summary>
    /// <param name="name">用例名（中文）。</param>
    /// <param name="passed">是否通过。</param>
    /// <param name="detail">断言详情；通过时通常为空。</param>
    public SelfCheckCase(string name, bool passed, string? detail)
    {
        Name = name;
        Passed = passed;
        Detail = detail;
    }

    /// <summary>用例名。</summary>
    public string Name { get; }

    /// <summary>是否通过。</summary>
    public bool Passed { get; }

    /// <summary>失败时的断言详情。</summary>
    public string? Detail { get; }

    /// <summary>调试用摘要。</summary>
    /// <returns>人可读的一行描述。</returns>
    public override string ToString() => Passed ? $"PASS  {Name}" : $"FAIL  {Name}：{Detail}";
}

/// <summary>自检报告（成功 / 失败用例明细）。</summary>
public sealed class SelfCheckReport
{
    /// <summary>构造报告。</summary>
    /// <param name="cases">全部用例结果，保持执行顺序。</param>
    public SelfCheckReport(IReadOnlyList<SelfCheckCase> cases)
    {
        Cases = cases;
    }

    /// <summary>全部用例结果。</summary>
    public IReadOnlyList<SelfCheckCase> Cases { get; }

    /// <summary>是否全部通过。</summary>
    public bool Passed => Cases.All(c => c.Passed);

    /// <summary>用例总数。</summary>
    public int TotalCount => Cases.Count;

    /// <summary>通过数。</summary>
    public int PassedCount => Cases.Count(c => c.Passed);

    /// <summary>失败用例。</summary>
    public IReadOnlyList<SelfCheckCase> Failures => Cases.Where(c => !c.Passed).ToList();

    /// <summary>渲染逐条结果。</summary>
    /// <returns>多行文本。</returns>
    public string Describe() =>
        string.Join(Environment.NewLine, Cases.Select(c => c.ToString()))
        + Environment.NewLine
        + $"合计 {PassedCount}/{TotalCount} 通过{(Passed ? "。" : "，存在失败用例。")}";

    /// <summary>渲染单行摘要。</summary>
    /// <returns>单行文本。</returns>
    public string Summary() => $"自检 {PassedCount}/{TotalCount} 通过{(Passed ? "" : "：失败 " + string.Join("；", Failures.Select(f => f.Name)))}";

    /// <summary>调试用摘要。</summary>
    /// <returns>单行摘要。</returns>
    public override string ToString() => Summary();
}

/// <summary>
/// 「后缀 → 语义槽位 → 着色器参数键」整条链路的<b>可执行自检</b>。
///
/// <para>纯静态、无网络、不碰用户真实配置（文件类用例一律在 <see cref="Path.GetTempPath()"/>
/// 下的独立临时目录中运行并在结束时清理）。调用 <see cref="Run"/> 即可得到逐条结果，
/// 便于 CI、调试器窗口或 GUI「关于」页调用。</para>
///
/// <para><b>覆盖范围。</b>归一化、通道名派生、P1–P5 每一档的命中、三个真实着色器的解析结果、
/// P5 多候选的不写入红线、后缀匹配与冲突裁决、路径规则、配置持久化的三种失败回退，
/// 以及「诊断建议的后缀必须真的能匹配到对应候选键」这条闭环不变量。</para>
///
/// <para><b>闭环不变量的意义。</b>多候选未解析时，诊断会建议用户把文件改名成
/// <c>_foamnormal</c> 之类。如果这个后缀不在种子表里，用户照做之后反而匹配不上任何规则，
/// 比原来的症状更糟。该用例把这条不变量固化成可执行断言：日后新增任何 P5 通道而忘记
/// 补种子规则，自检会立刻失败。</para>
/// </summary>
public static class TextureAssignmentSelfTest
{
    /// <summary>执行全部自检用例。</summary>
    /// <returns>自检报告。<b>本方法不会抛异常。</b></returns>
    public static SelfCheckReport Run()
    {
        var cases = new List<SelfCheckCase>();

        // —— 与文件系统无关的纯算法用例 ——
        Run(cases, "归一化：分隔符折叠 + 大小写不敏感 + 去扩展名", CaseNormalizeName);
        Run(cases, "通道名派生：§5.1 全表", CaseDeriveChannel);
        Run(cases, "P1 直名无缀档", CaseTierP1);
        Run(cases, "P2 直名 + 数字尾缀档（csgo_environment Normal/Color/Roughness）", CaseTierP2);
        Run(cases, "P3 Layer 前缀档（csgo_lightmappedgeneric Normal/Color/Roughness）", CaseTierP3);
        Run(cases, "P4 派生通道名精确相等档（别名生效，合成模板）", CaseTierP4);
        Run(cases, "P5 带限定词复合通道档 · 唯一候选", CaseTierP5Unique);
        Run(cases, "P5 多候选并列 → 不写入任何参数（csgo_water_fancy Normal）", CaseP5AmbiguousNoWrite);
        Run(cases, "诊断文本 §5.6 逐字一致（AmbiguousQualified / NoMatchingKey 两句整串相等）", CaseDiagnosticTextVerbatim);
        Run(cases, "P5 多候选 → 复杂度序数决胜（csgo_effects TextureMask1/2/3）", CaseP2SmallestNumber);
        Run(cases, "诊断建议后缀闭环 INV-DIAG-CLOSURE：多候选诊断的每个建议后缀都能精确命中对应键", CaseDiagnosticSuffixClosure);
        Run(cases, "P5 候选 channel 穷举比对（§5.6.2 冻结的 10 个）", CaseP5ChannelExhaustive);
        Run(cases, "后缀匹配：最长后缀优先 / 整名相等 / 下划线边界", CaseSuffixMatching);
        Run(cases, "同槽位冲突：更具体的后缀胜出（§6.5）", CaseConflictBySuffixLength);
        Run(cases, "同槽位冲突：等长后缀按文件名 CompareOrdinal 升序决胜（§6.5）", CaseConflictByOrdinal);

        // —— 需要真实临时目录的用例 ——
        var sandbox = CreateSandbox();
        try
        {
            Run(cases, "路径规则：贴图根内写相对正斜杠、根外写原路径（§6.6 / S8）", () => CasePathRules(sandbox));
            Run(cases, "端到端 S1：csgo_environment 写入 TextureNormal1 / TextureColor1", () => CaseS1(sandbox));
            Run(cases, "端到端 S2：csgo_lightmappedgeneric 写入 TextureLayer1Normal / TextureLayer1Color", () => CaseS2(sandbox));
            Run(cases, "端到端 S3：整名相等命中（normal.png）", () => CaseS3(sandbox));
            Run(cases, "端到端 S4：brick_normal 与 brick_n 冲突，只写前者", () => CaseS4(sandbox));
            Run(cases, "端到端 S5：a_normal 与 b_normal 按文件名 CompareOrdinal 升序决胜", () => CaseS5(sandbox));
            Run(cases, "端到端 S6：水面着色器 foam_normal / debris 各就各位", () => CaseS6(sandbox));
            Run(cases, "端到端 S7：水面着色器 waves_normal 不写入任何参数", () => CaseS7(sandbox));
            Run(cases, "端到端 S8：贴图根之外的文件原路径写回", () => CaseS8(sandbox));
            Run(cases, "端到端 S10：logo.png 无规则命中 → 零写入 + 未命中列表", () => CaseS10(sandbox));
            Run(cases, "§8.8 步骤 11/11b：S10 与歧义诊断同时存在时的前置状态（防 GUI 提前 return）", () => CaseS10AndAmbiguousBothPresent(sandbox));
            Run(cases, "端到端 S12：fx_mask / banner_mask 同槽位只写一项（TextureMask1）", () => CaseS12(sandbox));
            Run(cases, "配置：文件不存在 → Defaulted + 种子表", () => CaseSettingsMissing(sandbox));
            Run(cases, "配置：JSON 损坏 → RecoveredFromCorruption + 生成备份", () => CaseSettingsCorrupt(sandbox));
            Run(cases, "配置：零字节文件 → RecoveredFromCorruption", () => CaseSettingsEmpty(sandbox));
            Run(cases, "配置：字段非法 → 只回退非法字段并记录 RepairedFields", () => CaseSettingsInvalidFields(sandbox));
            Run(cases, "配置：rules 为空数组时保留为空，不回填种子表", () => CaseSettingsEmptyRulesPreserved(sandbox));
            Run(cases, "配置：JSON 写入 → 读回字段完全一致", () => CaseSettingsRoundTrip(sandbox));
            Run(cases, "拖拽矩阵 D1–D6 六类分档与两个根目录候选（§2.2）", () => CaseDropMatrix(sandbox));
            Run(cases, "§2.2 D4：贴图递归关闭时为 TopDirectoryOnly（顶层贴图仍要收）", () => CaseDropNoTextureRecursion(sandbox));
        }
        finally
        {
            TryDeleteDirectory(sandbox.Root);
        }

        return new SelfCheckReport(cases);
    }

    // ── 用例执行辅助 ────────────────────────────────────────────────────────

    private static void Run(List<SelfCheckCase> cases, string name, Func<string?> body)
    {
        try
        {
            var detail = body();
            cases.Add(new SelfCheckCase(name, detail is null, detail));
        }
        catch (Exception ex)
        {
            cases.Add(new SelfCheckCase(name, false, $"抛异常：{ex.GetType().Name}: {ex.Message}"));
        }
    }

    /// <summary>断言相等；成功返回 <c>null</c>，失败返回描述。</summary>
    private static string? Eq<T>(T expected, T actual, string what) =>
        EqualityComparer<T>.Default.Equals(expected, actual) ? null : $"{what}：期望 {expected}，实际 {actual}";

    private static string? NotNull(object? value, string what) => value is null ? $"{what}：不应为 null" : null;

    private static string? IsTrue(bool condition, string what) => condition ? null : what;

    private static string? Combine(params string?[] parts)
    {
        var joined = parts.Where(p => p is not null).ToList();
        return joined.Count == 0 ? null : string.Join("；", joined);
    }

    // ── 纯算法用例 ──────────────────────────────────────────────────────────

    private static string? CaseNormalizeName()
    {
        return Combine(
            Eq("concrete_wall_n", TextureSuffixMatcher.NormalizeName("Concrete-Wall _N.png"), "§11-1 示例"),
            Eq("ambient_occlusion", TextureSuffixMatcher.NormalizeName("_Ambient Occlusion"), "§6.2 示例"),
            Eq("normal", TextureSuffixMatcher.NormalizeName("_Normal.png"), "规则后缀同样归一化"),
            Eq("wall_wall", TextureSuffixMatcher.NormalizeName("WALL__WALL.png"), "连续下划线折叠 + 大小写"),
            Eq(string.Empty, TextureSuffixMatcher.NormalizeName(""), "空串输入"));
    }

    private static string? CaseDeriveChannel()
    {
        var table = new (string Key, string Expected)[]
        {
            ("TextureColor", "Color"),
            ("TextureColor1", "Color"),
            ("TextureLayer1Normal", "Normal"),
            ("TextureMask1", "Mask"),
            ("TextureAmbientOcclusion", "AmbientOcclusion"),
            ("TextureFoamNormal", "FoamNormal"),
            ("TextureCubeMap", "CubeMap"),
            ("TextureLowEndCubeMap", "LowEndCubeMap"),
            ("SkyTexture", ""),
        };
        var failures = new List<string>();
        foreach (var (key, expected) in table)
        {
            var actual = TextureRoleResolver.DeriveChannel(key);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                failures.Add($"{key} 期望 {expected}，实际 {actual}");
        }
        return failures.Count == 0 ? null : string.Join("；", failures);
    }

    private static string? CaseTierP1()
    {
        var shader = Require("csgo_vertexlitgeneric.vfx");
        var normal = TextureRoleResolver.Resolve(shader, TextureRole.Normal);
        var color = TextureRoleResolver.Resolve(shader, TextureRole.Color);
        return Combine(
            Eq("TextureNormal", normal.ParameterKey, "P1 法线"),
            Eq(TextureKeyTier.ExactName, normal.Tier, "P1 档位"),
            Eq("TextureColor", color.ParameterKey, "P1 颜色"),
            Eq(TextureKeyTier.ExactName, color.Tier, "P1 档位"));
    }

    private static string? CaseTierP2()
    {
        var shader = Require("csgo_environment.vfx");
        var normal = TextureRoleResolver.Resolve(shader, TextureRole.Normal);
        var color = TextureRoleResolver.Resolve(shader, TextureRole.Color);
        var roughness = TextureRoleResolver.Resolve(shader, TextureRole.Roughness);
        return Combine(
            Eq("TextureNormal1", normal.ParameterKey, "环境法线"),
            Eq(TextureKeyTier.ExactNameNumbered, normal.Tier, "环境法线档位"),
            Eq("TextureColor1", color.ParameterKey, "环境颜色"),
            Eq("TextureRoughness1", roughness.ParameterKey, "环境粗糙度（Vector/TextureOrVector 按 §5.1 条件②入选）"),
            Eq("TextureMetalness1", TextureRoleResolver.Resolve(shader, TextureRole.Metalness).ParameterKey, "环境金属度"),
            Eq("TextureAmbientOcclusion1", TextureRoleResolver.Resolve(shader, TextureRole.AmbientOcclusion).ParameterKey, "环境 AO"),
            Eq("TextureHeight1", TextureRoleResolver.Resolve(shader, TextureRole.Height).ParameterKey, "环境高度"),
            Eq(TextureResolveFailure.NoMatchingKey, TextureRoleResolver.Resolve(shader, TextureRole.Detail).UnresolvedReason, "环境模板无 Detail 键"));
    }

    private static string? CaseTierP3()
    {
        var shader = Require("csgo_lightmappedgeneric.vfx");
        return Combine(
            Eq("TextureLayer1Normal", TextureRoleResolver.Resolve(shader, TextureRole.Normal).ParameterKey, "LMG 法线"),
            Eq(TextureKeyTier.LayerPrefixed, TextureRoleResolver.Resolve(shader, TextureRole.Normal).Tier, "LMG 法线档位"),
            Eq("TextureLayer1Color", TextureRoleResolver.Resolve(shader, TextureRole.Color).ParameterKey, "LMG 颜色"),
            Eq("TextureLayer1Roughness", TextureRoleResolver.Resolve(shader, TextureRole.Roughness).ParameterKey, "LMG 粗糙度"),
            Eq("TextureLayer1AmbientOcclusion", TextureRoleResolver.Resolve(shader, TextureRole.AmbientOcclusion).ParameterKey, "LMG AO"),
            Eq("TextureLayer1Detail", TextureRoleResolver.Resolve(shader, TextureRole.Detail).ParameterKey, "LMG 细节"),
            Eq("TextureLayer1Translucency", TextureRoleResolver.Resolve(shader, TextureRole.Translucency).ParameterKey, "LMG 半透明"),
            Eq(false, TextureRoleResolver.Resolve(shader, TextureRole.Metalness).IsResolved, "LMG 金属度应为标量，不解析"));
    }

    private static string? CaseTierP4()
    {
        // 合成模板：真实目录里没有 TextureMetal / TextureSelfIllum，需要构造一个来覆盖
        // 「别名参与 P4 判定」这条规则（§5.2 注：别名仅在 P4 / P5 生效）。
        var synthetic = new ShaderTemplate(
            "selfcheck.p4.vfx",
            "自检 P4",
            "仅用于自检：验证别名 Token 在 P4 档生效。",
            new List<ShaderParamTemplate>
            {
                new("TextureMetal", "金属", ShaderParamKind.Texture, ""),
                new("TextureSelfIllum", "自发光", ShaderParamKind.Texture, ""),
                new("TextureNormal", "法线", ShaderParamKind.Texture, ""),
            },
            Array.Empty<string>(),
            Array.Empty<string>());

        var metalness = TextureRoleResolver.Resolve(synthetic, TextureRole.Metalness);
        var emissive = TextureRoleResolver.Resolve(synthetic, TextureRole.Emissive);
        var normal = TextureRoleResolver.Resolve(synthetic, TextureRole.Normal);
        return Combine(
            // Metalness 的主 Token 是 Metalness，因此 TextureMetal 只能靠别名 Metal 走 P4，
            // 而不是 P1 —— 这正是「别名仅在 P4/P5 生效」的体现。
            Eq("TextureMetal", metalness.ParameterKey, "TextureMetal 经别名 Metal 命中 Metalness"),
            Eq(TextureKeyTier.DerivedChannelEqual, metalness.Tier, "TextureMetal 的档位应为 P4"),
            Eq("TextureSelfIllum", emissive.ParameterKey, "P4 自发光别名"),
            Eq(TextureKeyTier.DerivedChannelEqual, emissive.Tier, "P4 档位"),
            Eq("TextureNormal", normal.ParameterKey, "P1 不受影响"),
            Eq(TextureKeyTier.ExactName, normal.Tier, "P1 档位"));
    }

    private static string? CaseTierP5Unique()
    {
        var environment = Require("csgo_environment.vfx");
        var water = Require("csgo_water_fancy.vfx");
        var character = Require("csgo_character.vfx");

        var mask = TextureRoleResolver.Resolve(environment, TextureRole.Mask);
        var cubeMap = TextureRoleResolver.Resolve(water, TextureRole.CubeMap);
        var rim = TextureRoleResolver.Resolve(character, TextureRole.Mask);
        var tintMask = TextureRoleResolver.Resolve(environment, TextureRole.TintMask);

        return Combine(
            Eq("TextureTintMask1", mask.ParameterKey, "环境 Mask 走 P5 唯一候选"),
            Eq(TextureKeyTier.QualifiedComposite, mask.Tier, "环境 Mask 档位"),
            Eq("TextureLowEndCubeMap", cubeMap.ParameterKey, "水面 CubeMap 走 P5 唯一候选"),
            Eq(TextureKeyTier.QualifiedComposite, cubeMap.Tier, "水面 CubeMap 档位"),
            Eq("TextureRimMask", rim.ParameterKey, "角色 Mask 走 P5 唯一候选"),
            Eq("TextureTintMask1", tintMask.ParameterKey, "显式 TintMask 槽位走 P2"),
            // SelfIllumMask 的主 Token 必须让 TextureSelfIllumMask 落在 P1/P4 而不是 P5，
            // 否则用户「改名为 _selfillummask」的出路会被多候选重新堵死。
            Eq("TextureSelfIllumMask",
               TextureRoleResolver.Resolve(Require("csgo_complex.vfx"), TextureRole.SelfIllumMask).ParameterKey,
               "SelfIllumMask 槽位按 P1 精确命中"));
    }

    private static string? CaseP5AmbiguousNoWrite()
    {
        var water = Require("csgo_water_fancy.vfx");
        var resolution = TextureRoleResolver.Resolve(water, TextureRole.Normal);

        // csgo_complex + Mask 是另一组多候选，一并确认不会写入。
        var complexMask = TextureRoleResolver.Resolve(Require("csgo_complex.vfx"), TextureRole.Mask);

        // §11-6：水面着色器没有颜色 / 粗糙度槽位，必须判定为「模板无对应键」而非乱填。
        var waterColor = TextureRoleResolver.Resolve(water, TextureRole.Color);
        var waterRoughness = TextureRoleResolver.Resolve(water, TextureRole.Roughness);

        return Combine(
            Eq(false, resolution.IsResolved, "水面法线不应解析"),
            Eq(null, resolution.ParameterKey, "水面法线不应给出参数键"),
            Eq(TextureResolveFailure.AmbiguousQualified, resolution.UnresolvedReason, "水面法线未解析原因"),
            Eq(3, resolution.Candidates.Count, "水面法线候选数"),
            Eq(false, complexMask.IsResolved, "csgo_complex 的 Mask 不应解析"),
            Eq(2, complexMask.Candidates.Count, "csgo_complex 的 Mask 候选数"),
            Eq(false, waterColor.IsResolved, "水面着色器 Color 不应解析"),
            Eq(TextureResolveFailure.NoMatchingKey, waterColor.UnresolvedReason, "水面 Color 未解析原因"),
            Eq(false, waterRoughness.IsResolved, "水面着色器 Roughness 不应解析"),
            Eq(0, waterColor.Candidates.Count, "水面 Color 无任何候选"));
    }

    /// <summary>
    /// §11 自检第 9 项 —— <c>BuildDiagnosticText</c> 的输出与 §5.6 模板<b>逐字一致</b>
    /// （含 <c>" / "</c> 连接符、声明序与句末句号）。
    ///
    /// <para><b>为什么必须是整串相等而不是「包含候选键名」。</b>
    /// 早先本项只断言诊断文本 <i>包含</i> 三个候选键名，而旧实现的句式
    /// <c>法线（Normal）：多候选并列，…（TextureFoamNormal、TextureWavesNormal、TextureDebrisNormal）；可改名为 …</c>
    /// 恰好也包含这三个键名——于是自检一路绿灯，规格 §5.6 的模板却一个字都没实现。
    /// 这正是「断言太弱导致真实缺陷逃逸」。因此本项改为把期望字符串<b>按模板字面量写死</b>后整串比较：
    /// 任何句式漂移、连接符改动、顿号混入、漏句号都会立刻失败。</para>
    /// </summary>
    private static string? CaseDiagnosticTextVerbatim()
    {
        var water = Require("csgo_water_fancy.vfx");
        var ambiguous = TextureRoleResolver.Resolve(water, TextureRole.Normal);
        var noMatch = TextureRoleResolver.Resolve(water, TextureRole.Color);

        // 期望值完全按 §5.6 模板手写，**不引用**任何被测代码的派生逻辑；
        // 候选与建议后缀的顺序取 §5.6「按 ParameterIndex 升序（模板声明序）」，
        // 对应 ShaderCatalog 中 FoamNormal(L427) → DebrisNormal(L430) → WavesNormal(L432) 的声明次序。
        var expectedAmbiguous =
            "该着色器没有通用的 法线 槽位，"
            + "候选为 TextureFoamNormal / TextureDebrisNormal / TextureWavesNormal；"
            + "请改用 _foamnormal / _debrisnormal / _wavesnormal 后缀，或改选其他着色器。";
        var expectedNoMatch = "该着色器没有 颜色 / 反照率 对应的贴图参数（已跳过）。";

        var actualAmbiguous = TextureRoleResolver.BuildDiagnosticText(
            TextureRole.Normal, TextureResolveFailure.AmbiguousQualified, ambiguous.Candidates);
        var actualNoMatch = TextureRoleResolver.BuildDiagnosticText(
            TextureRole.Color, TextureResolveFailure.NoMatchingKey, noMatch.Candidates);

        return Combine(
            Eq(expectedAmbiguous, actualAmbiguous,
                $"AmbiguousQualified 诊断文本逐字一致（§5.6）；实际为「{actualAmbiguous}」"),
            Eq(expectedAmbiguous, ambiguous.DiagnosticText,
                $"Resolve() 透传的 DiagnosticText 与 BuildDiagnosticText 逐字一致；实际为「{ambiguous.DiagnosticText}」"),
            Eq(expectedNoMatch, actualNoMatch,
                $"NoMatchingKey 诊断文本逐字一致（§5.6）；实际为「{actualNoMatch}」"),
            Eq(expectedNoMatch, noMatch.DiagnosticText,
                $"NoMatchingKey 的 Resolve() 透传文本逐字一致；实际为「{noMatch.DiagnosticText}」"),
            // §5.6：单行、不得插入 \n；连接符固定 " / "，全角顿号「、」不得使用。
            Eq(false, actualAmbiguous.Contains('\n'), "诊断文本必须单行，不得含换行"),
            Eq(false, actualAmbiguous.Contains('、'), "候选连接符不得使用全角顿号「、」"),
            Eq(false, actualNoMatch.Contains('、'), "NoMatchingKey 文本不得含全角顿号「、」"),
            Eq(true, actualAmbiguous.EndsWith("。", StringComparison.Ordinal), "歧义诊断须以句号收尾"),
            Eq(true, actualNoMatch.EndsWith("。", StringComparison.Ordinal), "无对应键诊断须以句号收尾"));
    }

    private static string? CaseP2SmallestNumber()
    {
        var effects = Require("csgo_effects.vfx");
        var resolution = TextureRoleResolver.Resolve(effects, TextureRole.Mask);
        return Combine(
            Eq("TextureMask1", resolution.ParameterKey, "TextureMask1/2/3 取 num 最小"),
            Eq(TextureKeyTier.ExactNameNumbered, resolution.Tier, "档位"),
            Eq(3, resolution.Candidates.Count, "候选数"));
    }

    /// <summary>
    /// §11 自检第 12 项 —— <b>INV-DIAG-CLOSURE</b>：
    /// 遍历 <see cref="ShaderCatalog.All"/> 中每一个出现 <see cref="TextureResolveFailure.AmbiguousQualified"/>
    /// 的 <c>(shader, role)</c>，从 <see cref="TextureRoleResolution.DiagnosticText"/> 里解析出每个
    /// 建议后缀，断言种子表存在<b>已启用</b>的对应规则，且该规则的 Role 解析出的
    /// <c>ParameterKey</c> <b>恰好等于</b>配对的候选键。
    /// </summary>
    private static string? CaseDiagnosticSuffixClosure()
    {
        var seed = VmatGeneratorSettings.CreateDefault();
        var failures = new List<string>();
        var checkedPairs = 0;

        foreach (var shader in ShaderCatalog.All)
        {
            foreach (var role in TextureRoleTokens.AllRoles)
            {
                var resolution = TextureRoleResolver.Resolve(shader, role);
                if (resolution.UnresolvedReason != TextureResolveFailure.AmbiguousQualified) continue;

                foreach (var candidate in resolution.Candidates)
                {
                    checkedPairs++;
                    var suffix = TextureRoleResolver.SuggestedSuffixFor(candidate);

                    // 建议后缀必须逐字出现在诊断文本里（GUI 直接把它显示给用户）。
                    if (!resolution.DiagnosticText.Contains(suffix, StringComparison.Ordinal))
                    {
                        failures.Add($"{shader.ShaderName} / {role}：诊断文本缺少建议后缀 {suffix}");
                        continue;
                    }

                    var rule = seed.FindRule(suffix);
                    if (rule is null)
                    {
                        failures.Add($"{shader.ShaderName} / {role} / {candidate.ParameterKey}：种子表缺少建议后缀 {suffix}");
                        continue;
                    }
                    if (!rule.Enabled)
                    {
                        failures.Add($"{shader.ShaderName} / {role} / {candidate.ParameterKey}：建议后缀 {suffix} 对应规则被停用");
                        continue;
                    }

                    // 顺着建议后缀解析出的槽位，参数键必须恰好等于这个候选键。
                    var followUp = TextureRoleResolver.Resolve(shader, rule.Role);
                    if (!followUp.IsResolved ||
                        !string.Equals(followUp.ParameterKey, candidate.ParameterKey, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"{shader.ShaderName} / {role} / {candidate.ParameterKey}：建议后缀 {suffix} 指向 {rule.Role}，"
                            + $"实际解析为 {(followUp.IsResolved ? followUp.ParameterKey : "未解析")}");
                    }
                }
            }
        }

        if (checkedPairs == 0) failures.Add("未收集到任何多候选诊断，闭环用例失去意义");
        return failures.Count == 0 ? null : string.Join("；", failures);
    }

    /// <summary>
    /// §11 自检第 13 项 —— <b>穷举比对</b>：把实现跑出来的「可作 P5 候选的 channel 全集」
    /// 与规格 §5.6.2 冻结的 10 个逐一比对，防止将来新增着色器时漏掉种子规则。
    /// </summary>
    private static string? CaseP5ChannelExhaustive()
    {
        var expected = new SortedSet<string>(StringComparer.Ordinal)
        {
            "TintMask", "SelfIllumMask", "DetailMask", "RimMask", "LowEndCubeMap",
            "FoamNormal", "DebrisNormal", "WavesNormal", "WavesHeight", "DebrisHeight",
        };

        var actual = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var shader in ShaderCatalog.All)
        {
            foreach (var role in TextureRoleTokens.AllRoles)
            {
                foreach (var candidate in TextureRoleResolver.ResolveCandidates(shader, role))
                {
                    if (candidate.Tier == TextureKeyTier.QualifiedComposite) actual.Add(candidate.DerivedChannel);
                }
            }
        }

        var missing = expected.Except(actual).ToList();
        var extra = actual.Except(expected).ToList();
        var failures = new List<string>();
        if (missing.Count > 0) failures.Add($"规格要求但实现中缺失的 P5 channel：{string.Join("、", missing)}");
        if (extra.Count > 0) failures.Add($"实现中多出的 P5 channel：{string.Join("、", extra)}");

        // 每个 channel 都必须有对应的已启用种子规则，且该规则的 Role 在某个模板上解析出这个 channel。
        var seed = VmatGeneratorSettings.CreateDefault();
        foreach (var channel in expected)
        {
            var suffix = "_" + channel.ToLowerInvariant();
            var rule = seed.FindRule(suffix);
            if (rule is null)
            {
                failures.Add($"P5 channel {channel}：种子表缺少 {suffix}");
                continue;
            }
            if (!rule.Enabled)
            {
                failures.Add($"P5 channel {channel}：种子规则 {suffix} 被停用");
                continue;
            }
            var reachable = ShaderCatalog.All.Any(shader => TextureRoleResolver.ResolveCandidates(shader, rule.Role)
                .Any(c => string.Equals(c.DerivedChannel, channel, StringComparison.Ordinal)));
            if (!reachable)
            {
                failures.Add($"P5 channel {channel}：种子规则 {suffix} 指向 {rule.Role}，"
                    + $"但该槽位在任何模板上都解析不出 channel {channel}");
            }
        }

        return failures.Count == 0 ? null : string.Join("；", failures);
    }

    private static string? CaseSuffixMatching()
    {
        var rules = VmatGeneratorSettings.CreateDefault().Rules;
        var matcher = new TextureSuffixMatcher(rules);

        var concrete = matcher.Match("D:/t/wall/concrete_normal.png");
        var whole = matcher.Match("D:/t/wall/normal.png");
        var shortForm = matcher.Match("D:/t/wall/stone_n.png");
        var masked = matcher.Match("D:/t/wall/brick.png");     // 不应命中 _mask
        var none = matcher.Match("D:/t/ui/logo.png");
        var longest = matcher.Match("D:/t/wall/x_ambientocclusion.png");

        return Combine(
            NotNull(concrete, "concrete_normal 应命中"),
            Eq(TextureRole.Normal, concrete!.Role, "concrete_normal 角色"),
            Eq("normal", concrete.MatchedSuffix, "concrete_normal 命中后缀"),
            Eq(false, concrete.IsWholeNameMatch, "concrete_normal 是边界命中"),
            Eq(true, whole!.IsWholeNameMatch, "normal.png 是整名命中"),
            Eq(TextureRole.Normal, whole.Role, "整名命中角色"),
            Eq(TextureRole.Normal, shortForm!.Role, "stone_n 命中 _n"),
            Eq(0, masked is null ? 0 : 1, "brick.png 不应命中任何规则（_mask 要求下划线边界）"),
            Eq(true, none is null, "logo.png 不应命中任何规则"),
            Eq(TextureRole.AmbientOcclusion, longest!.Role, "长后缀优先于 _ao"),
            Eq("ambientocclusion", longest.MatchedSuffix, "长后缀优先于 _ao 的命中后缀"));
    }

    private static string? CaseConflictBySuffixLength()
    {
        var matcher = new TextureSuffixMatcher(VmatGeneratorSettings.CreateDefault().Rules);
        var a = matcher.Match("D:/t/wall/brick_normal.png");
        var b = matcher.Match("D:/t/wall/brick_n.png");
        return Combine(
            Eq("normal", a!.MatchedSuffix, "brick_normal 命中后缀"),
            Eq("n", b!.MatchedSuffix, "brick_n 命中后缀"),
            Eq(true, a.MatchedSuffixLength > b.MatchedSuffixLength, "更长后缀应胜出"));
    }

    private static string? CaseConflictByOrdinal()
    {
        var matcher = new TextureSuffixMatcher(VmatGeneratorSettings.CreateDefault().Rules);
        var a = matcher.Match("D:/t/wall/a_normal.png");
        var b = matcher.Match("D:/t/wall/b_normal.png");
        return Combine(
            Eq(a!.MatchedSuffixLength, b!.MatchedSuffixLength, "同长度"),
            Eq(false, a.IsWholeNameMatch, "a 是边界命中"),
            Eq(false, b.IsWholeNameMatch, "b 是边界命中"),
            Eq(true, string.CompareOrdinal(a.FileNameWithoutExtension, b.FileNameWithoutExtension) < 0,
                "a_normal 应按 CompareOrdinal 排在 b_normal 之前"));
    }

    // ── 端到端用例 ──────────────────────────────────────────────────────────

    private static string? CasePathRules(Sandbox sandbox)
    {
        var inside = TexturePathRules.ToVmatPath(Path.Combine(sandbox.TextureRoot, "wall", "concrete_normal.png"), sandbox.TextureRoot);
        var atRoot = TexturePathRules.ToVmatPath(Path.Combine(sandbox.TextureRoot, "normal.png"), sandbox.TextureRoot);
        var outside = Path.Combine(sandbox.Outside, "wall_normal.png");
        var outsideValue = TexturePathRules.ToVmatPath(outside, sandbox.TextureRoot);
        var noRoot = TexturePathRules.ToVmatPath(outside, null);

        return Combine(
            Eq("wall/concrete_normal.png", inside, "根目录内写相对路径 + 正斜杠"),
            Eq("normal.png", atRoot, "根目录正下方不加 ./"),
            Eq(outside, outsideValue, "根目录外写原路径"),
            Eq(true, !outsideValue.Contains("..", StringComparison.Ordinal), "原路径不得含 .."),
            Eq(outside, noRoot, "贴图根为空时写原路径"),
            Eq("wall", Path.GetFileName(TexturePathRules.CommonParentDirectory(new[]
            {
                Path.Combine(sandbox.TextureRoot, "wall", "a.png"),
                Path.Combine(sandbox.TextureRoot, "wall", "b.png"),
            }))!, "公共父目录"),
            Eq(true, TexturePathRules.IsTextureFile("x.PNG"), "扩展名大小写不敏感"),
            Eq(false, TexturePathRules.IsTextureFile("x.vmat"), ".vmat 不是贴图"),
            // §11-8：GUI 的上下移动按钮改的是 Rules 的顺序（即 JSON 数组下标）。
            // 轮转整张表后，后缀→角色与角色→参数键两级解析都必须给出同样的结果。
            Eq("TextureNormal1", KeyOfRule(RotatedSeed(), "_normal"), "轮转后 _normal 仍解析到 TextureNormal1"),
            Eq("TextureAmbientOcclusion1", KeyOfRule(RotatedSeed(), "_ao"), "轮转后 _ao 仍解析到 TextureAmbientOcclusion1"),
            Eq("TextureTintMask1", KeyOfRule(RotatedSeed(), "_mask"), "轮转后 _mask 仍解析到 TextureTintMask1"),
            Eq("TextureColor1", KeyOfRule(RotatedSeed(), "_diffuse"), "轮转后 _diffuse 仍解析到 TextureColor1"));
    }

    /// <summary>把种子规则整体轮转一位，用于验证规则顺序不影响解析结果。</summary>
    private static VmatGeneratorSettings RotatedSeed()
    {
        var settings = VmatGeneratorSettings.CreateDefault();
        var last = settings.Rules[^1];
        settings.Rules.RemoveAt(settings.Rules.Count - 1);
        settings.Rules.Insert(0, last);
        return settings;
    }

    /// <summary>用规则表反查某后缀在 csgo_environment 上最终解析到哪个参数键。</summary>
    private static string? KeyOfRule(VmatGeneratorSettings settings, string suffix)
    {
        var rule = settings.FindRule(suffix);
        return rule is null ? null : TextureRoleResolver.Resolve(Require("csgo_environment.vfx"), rule.Role).ParameterKey;
    }

    private static string? CaseS1(Sandbox sandbox)
    {
        var shader = Require("csgo_environment.vfx");
        var files = new[]
        {
            Path.Combine(sandbox.TextureRoot, "wall", "concrete_wall_normal.png"),
            Path.Combine(sandbox.TextureRoot, "wall", "concrete_wall_diffuse.png"),
        };
        var result = TextureAssigner.Assign(shader, files, VmatGeneratorSettings.CreateDefault().Rules, sandbox.TextureRoot);
        return Combine(
            Eq(2, result.Assignments.Count, "S1 写入数"),
            Eq("TextureNormal1", KeyOf(result, TextureRole.Normal), "S1 法线键"),
            Eq("wall/concrete_wall_normal.png", ValueOf(result, TextureRole.Normal), "S1 法线值"),
            Eq("TextureColor1", KeyOf(result, TextureRole.Color), "S1 颜色键"),
            Eq("wall/concrete_wall_diffuse.png", ValueOf(result, TextureRole.Color), "S1 颜色值"),
            Eq(0, result.Conflicts.Count, "S1 不应有冲突"),
            Eq(0, result.UnassignedFiles.Count, "S1 不应有未命中"));
    }

    private static string? CaseS2(Sandbox sandbox)
    {
        var shader = Require("csgo_lightmappedgeneric.vfx");
        var files = new[]
        {
            Path.Combine(sandbox.TextureRoot, "wall", "concrete_wall_normal.png"),
            Path.Combine(sandbox.TextureRoot, "wall", "concrete_wall_diffuse.png"),
        };
        var result = TextureAssigner.Assign(shader, files, VmatGeneratorSettings.CreateDefault().Rules, sandbox.TextureRoot);
        return Combine(
            Eq("TextureLayer1Normal", KeyOf(result, TextureRole.Normal), "S2 法线键"),
            Eq("wall/concrete_wall_normal.png", ValueOf(result, TextureRole.Normal), "S2 法线值"),
            Eq("TextureLayer1Color", KeyOf(result, TextureRole.Color), "S2 颜色键"),
            Eq("wall/concrete_wall_diffuse.png", ValueOf(result, TextureRole.Color), "S2 颜色值"));
    }

    private static string? CaseS3(Sandbox sandbox)
    {
        var result = TextureAssigner.Assign(
            Require("csgo_environment.vfx"),
            new[] { Path.Combine(sandbox.TextureRoot, "normal.png") },
            VmatGeneratorSettings.CreateDefault().Rules,
            sandbox.TextureRoot);
        return Combine(
            Eq("TextureNormal1", KeyOf(result, TextureRole.Normal), "S3 整名相等的键"),
            Eq("normal.png", ValueOf(result, TextureRole.Normal), "S3 整名相等的值（贴图根正下方不加 ./）"));
    }

    private static string? CaseS4(Sandbox sandbox)
    {
        var normalFile = Path.Combine(sandbox.TextureRoot, "wall", "brick_normal.png");
        var shortFile = Path.Combine(sandbox.TextureRoot, "wall", "brick_n.png");
        var result = TextureAssigner.Assign(
            Require("csgo_environment.vfx"),
            new[] { normalFile, shortFile },
            VmatGeneratorSettings.CreateDefault().Rules,
            sandbox.TextureRoot);

        var conflict = result.Conflicts.FirstOrDefault();
        return Combine(
            Eq(1, result.Assignments.Count, "S4 只写一项"),
            Eq("TextureNormal1", result.Assignments[0].ParameterKey, "S4 参数键"),
            Eq("wall/brick_normal.png", result.Assignments[0].VmatPath, "S4 胜者值"),
            Eq(1, result.Conflicts.Count, "S4 冲突组数"),
            Eq(normalFile, conflict!.KeptFilePath, "S4 胜者路径"),
            Eq(shortFile, string.Join("|", conflict.DroppedFilePaths), "S4 被淘汰路径"),
            Eq("同槽位冲突（Normal），已保留 brick_normal.png", conflict.Reason, "S4 冲突文案"));
    }

    private static string? CaseS5(Sandbox sandbox)
    {
        var aFile = Path.Combine(sandbox.TextureRoot, "wall", "a_normal.png");
        var bFile = Path.Combine(sandbox.TextureRoot, "wall", "b_normal.png");
        var result = TextureAssigner.Assign(
            Require("csgo_environment.vfx"),
            new[] { aFile, bFile },
            VmatGeneratorSettings.CreateDefault().Rules,
            sandbox.TextureRoot);
        var conflict = result.Conflicts.FirstOrDefault();
        return Combine(
            Eq("wall/a_normal.png", result.Assignments[0].VmatPath, "S5 Ordinal 决胜的胜者"),
            Eq(aFile, conflict!.KeptFilePath, "S5 胜者路径"),
            Eq(bFile, string.Join("|", conflict.DroppedFilePaths), "S5 被淘汰路径"));
    }

    /// <summary>
    /// 水面着色器的槽位归属。
    ///
    /// <para><b>与 §9 S6 的偏差（有意为之）。</b>原 S6 期望 <c>foam_normal.png</c> 落到
    /// <c>TextureFoamNormal</c>，但该文件名命中的是规则 <c>_normal</c>（而非 <c>_foamnormal</c>），
    /// 于是解析出的角色是 <see cref="TextureRole.Normal"/>；而在 v1.1 之后，水面着色器的
    /// <c>Normal</c> 是 P5 多候选歧义，正确结果就是<b>不写入</b>。因此这里改用种子表真正能
    /// 路由到该槽位的文件名 <c>surf_foamnormal.png</c>，并额外断言 <c>foam_normal.png</c>
    /// 确实不写入——这正是本次修订的核心行为。</para>
    /// </summary>
    private static string? CaseS6(Sandbox sandbox)
    {
        var files = new[]
        {
            Path.Combine(sandbox.TextureRoot, "water", "surf_foamnormal.png"),
            Path.Combine(sandbox.TextureRoot, "water", "debris.png"),
            Path.Combine(sandbox.TextureRoot, "water", "foam_normal.png"),
        };
        var result = TextureAssigner.Assign(
            Require("csgo_water_fancy.vfx"),
            files,
            VmatGeneratorSettings.CreateDefault().Rules,
            sandbox.TextureRoot);

        return Combine(
            Eq("TextureFoamNormal", KeyOf(result, TextureRole.FoamNormal), "泡沫法线键"),
            Eq("water/surf_foamnormal.png", ValueOf(result, TextureRole.FoamNormal), "泡沫法线值"),
            Eq("TextureDebris", KeyOf(result, TextureRole.DebrisColor), "漂浮物颜色键"),
            Eq("water/debris.png", ValueOf(result, TextureRole.DebrisColor), "漂浮物颜色值"),
            // foam_normal.png 命中 _normal → Normal 角色 → 水面 P5 多候选 → 不写入。
            Eq(null, KeyOf(result, TextureRole.Normal), "foam_normal.png 不得写入任何参数"),
            Eq(1, result.UnresolvedRoles.Count(r => r.Role == TextureRole.Normal), "foam_normal.png 记为未解析槽位"));
    }

    private static string? CaseS7(Sandbox sandbox)
    {
        var result = TextureAssigner.Assign(
            Require("csgo_water_fancy.vfx"),
            new[] { Path.Combine(sandbox.TextureRoot, "water", "waves_normal.png") },
            VmatGeneratorSettings.CreateDefault().Rules,
            sandbox.TextureRoot);

        var unresolved = result.UnresolvedRoles.FirstOrDefault();
        return Combine(
            Eq(0, result.Assignments.Count, "S7 必须零写入"),
            Eq(1, result.UnresolvedRoles.Count, "S7 未解析槽位数"),
            Eq(TextureRole.Normal, unresolved!.Role, "S7 未解析槽位"),
            Eq(TextureResolveFailure.AmbiguousQualified, unresolved.UnresolvedReason, "S7 未解析原因"),
            Eq(3, unresolved.Candidates.Count, "S7 候选数"),
            Eq(0, result.Assignments.Count(a => a.ParameterKey is "TextureWavesNormal" or "TextureDebrisNormal"), "S7 不得写入波浪 / 漂浮物法线"));
    }

    private static string? CaseS8(Sandbox sandbox)
    {
        var outside = Path.Combine(sandbox.Outside, "wall_normal.png");
        var result = TextureAssigner.Assign(
            Require("csgo_environment.vfx"),
            new[] { outside },
            VmatGeneratorSettings.CreateDefault().Rules,
            sandbox.TextureRoot);
        return Combine(
            Eq("TextureNormal1", KeyOf(result, TextureRole.Normal), "S8 参数键"),
            Eq(outside, ValueOf(result, TextureRole.Normal), "S8 根目录外写原路径"));
    }

    private static string? CaseS10(Sandbox sandbox)
    {
        var logo = Path.Combine(sandbox.TextureRoot, "ui", "logo.png");
        var result = TextureAssigner.Assign(
            Require("csgo_environment.vfx"),
            new[] { logo },
            VmatGeneratorSettings.CreateDefault().Rules,
            sandbox.TextureRoot);
        return Combine(
            Eq(0, result.Assignments.Count, "S10 零写入"),
            Eq(true, result.HasNoWrites, "S10 HasNoWrites"),
            Eq(1, result.UnassignedFiles.Count, "S10 未命中数"),
            Eq(logo, string.Join("|", result.UnassignedFiles), "S10 未命中文件"),
            Eq(0, result.Conflicts.Count, "S10 不应有冲突"));
    }

    /// <summary>
    /// §8.8 步骤 11/11b 的<b>前置状态</b>回归：一次拖拽里同时出现「未命中任何后缀规则」（S10）
    /// 与「命中规则但多候选歧义」（11b）时，分配器必须给出
    /// <c>assigned == 0</c> 且 <c>unassigned ≥ 1</c> 且 <c>unresolved ≥ 1</c> 的状态。
    ///
    /// <para><b>为什么这要单独锁一条。</b><c>MainViewModel.DescribeDropResult</c> 曾在此状态下
    /// <b>提前 return</b>，把 S10 基础文案当作最终文案返回，从而整段吞掉 11b 的歧义诊断——
    /// 用户看到「未匹配到任何后缀规则」，完全不知道自己拖进来的
    /// <c>water_normal.png</c> 其实还触发了另一个需要处理的问题。
    /// GUI 侧的拼接逻辑不在 Lib 可达范围内，但这里的四项前置条件正是该提前 return 分支的
    /// 触发开关：一旦分配器行为变化、使这四项不再同时成立，本用例即失效并报警。</para>
    /// </summary>
    private static string? CaseS10AndAmbiguousBothPresent(Sandbox sandbox)
    {
        var ambiguous = Path.Combine(sandbox.TextureRoot, "water", "water_normal.png");
        var noRule = Path.Combine(sandbox.TextureRoot, "ui", "logo.png");
        var result = TextureAssigner.Assign(
            Require("csgo_water_fancy.vfx"),
            new[] { ambiguous, noRule },
            VmatGeneratorSettings.CreateDefault().Rules,
            sandbox.TextureRoot);

        var unresolvedAmbiguous = result.UnresolvedRoles
            .Where(r => r.UnresolvedReason == TextureResolveFailure.AmbiguousQualified)
            .ToList();

        return Combine(
            Eq(0, result.Assignments.Count, "S10+11b：零写入"),
            Eq(true, result.HasNoWrites, "S10+11b：HasNoWrites"),
            Eq(1, result.UnassignedFiles.Count, "S10+11b：未命中数（logo.png）"),
            Eq(true, result.UnresolvedRoles.Count >= 1, "S10+11b：存在未解析角色"),
            Eq(1, unresolvedAmbiguous.Count, "S10+11b：歧义角色数"),
            // 歧义角色必须原样带上 Lib 的逐字诊断，供 GUI 无条件 ⚠ 透传。
            Eq(TextureResolveFailure.AmbiguousQualified, unresolvedAmbiguous[0].UnresolvedReason,
                "S10+11b：歧义角色的原因"),
            Eq(true, unresolvedAmbiguous[0].DiagnosticText.Contains("请改用", StringComparison.Ordinal)
                       && unresolvedAmbiguous[0].DiagnosticText.EndsWith("。", StringComparison.Ordinal),
                "S10+11b：歧义诊断已含可操作建议且以句号收尾"));
    }

    /// <summary>
    /// 同槽位多贴图：只写 <c>TextureMask1</c>，且 P2 取 <c>num</c> 最小。
    ///
    /// <para><b>输入文件集与规格 §9 S12 一致（v1.5 起）。</b>S12 早期写法
    /// <c>fx_mask1.png</c> / <c>fx_mask2.png</c> <b>不可满足</b>：§4 明文「<c>_mask1/2/3</c> 不单列规则」，
    /// 而 §6.3 序 3 的尾部边界匹配要求文件名以 <c>_mask</c> 结尾——<c>fx_mask1</c> 结尾是
    /// <c>mask1</c>，匹配不上任何规则、按 §6.4 落入 <c>UnassignedFiles</c>。
    /// 这是规格自洽的<b>正确行为</b>，错的是原验收行。规格 v1.5 已改用
    /// <c>fx_mask.png</c> + <c>banner_mask.png</c>，本用例随即对齐，端到端验证
    /// 「同槽位只写一项 + 按文件名 CompareOrdinal 升序决胜」。</para>
    ///
    /// <para>P2「<c>TextureMask1/2/3</c> 取 <c>num</c> 最小」由
    /// <see cref="CaseP2SmallestNumber"/> 在解析层直接断言，§9 不重复覆盖。</para>
    /// </summary>
    private static string? CaseS12(Sandbox sandbox)
    {
        var fxMask = Path.Combine(sandbox.TextureRoot, "fx", "fx_mask.png");
        var bannerMask = Path.Combine(sandbox.TextureRoot, "fx", "banner_mask.png");
        var result = TextureAssigner.Assign(
            Require("csgo_effects.vfx"),
            new[] { fxMask, bannerMask },
            VmatGeneratorSettings.CreateDefault().Rules,
            sandbox.TextureRoot);
        return Combine(
            Eq(1, result.Assignments.Count, "S12 只写一项"),
            Eq("TextureMask1", result.Assignments[0].ParameterKey, "S12 取 num 最小的 TextureMask1"),
            Eq("fx/banner_mask.png", result.Assignments[0].VmatPath, "S12 Ordinal 决胜的胜者"),
            Eq(1, result.Conflicts.Count, "S12 冲突组数"),
            Eq(fxMask, string.Join("|", result.Conflicts[0].DroppedFilePaths), "S12 被淘汰路径"),
            Eq(0, result.Assignments.Count(a => a.ParameterKey is "TextureMask2" or "TextureMask3"),
                "S12 不得写入 TextureMask2 / TextureMask3"));
    }

    // ── 配置持久化用例 ──────────────────────────────────────────────────────

    private static string? CaseSettingsMissing(Sandbox sandbox)
    {
        var path = Path.Combine(sandbox.Settings, "does-not-exist.json");
        var settings = VmatGeneratorSettingsStore.LoadFromFile(path, out var report);
        return Combine(
            NotNull(settings, "文件不存在时应返回默认配置"),
            Eq(SettingsLoadOutcome.Defaulted, report.Outcome, "Outcome"),
            Eq(false, report.LoadedFromDisk, "LoadedFromDisk"),
            Eq(string.Empty, settings!.TextureRoot, "默认贴图根为空"),
            Eq(VmatGeneratorSettings.DefaultShaderNameValue, settings.DefaultShaderName, "默认着色器"),
            Eq(true, settings.Rules.Count >= 10, $"种子表至少 10 条，实际 {settings.Rules.Count} 条"),
            Eq(true, settings.Rules.All(r => r.Enabled), "种子规则应全部启用"),
            Eq(false, File.Exists(path), "文件不存在时不得写盘"));
    }

    private static string? CaseSettingsCorrupt(Sandbox sandbox)
    {
        var dir = Path.Combine(sandbox.Settings, "corrupt");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{ this is not json ]]");

        var settings = VmatGeneratorSettingsStore.LoadFromFile(path, out var report);
        var backupOk = report.BackupFilePath is not null && File.Exists(report.BackupFilePath);
        return Combine(
            NotNull(settings, "损坏时应返回默认配置"),
            Eq(SettingsLoadOutcome.RecoveredFromCorruption, report.Outcome, "Outcome"),
            Eq(false, report.LoadedFromDisk, "LoadedFromDisk"),
            Eq(true, NotNull(report.ErrorMessage, "ErrorMessage") is null, "ErrorMessage 非空"),
            Eq(true, backupOk, $"应生成备份文件（实际 {report.BackupFilePath}）"),
            Eq(true, settings!.Rules.Count >= 10, "损坏后回退到种子表"));
    }

    private static string? CaseSettingsEmpty(Sandbox sandbox)
    {
        var dir = Path.Combine(sandbox.Settings, "zero-byte");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllBytes(path, Array.Empty<byte>());

        var settings = VmatGeneratorSettingsStore.LoadFromFile(path, out var report);
        return Combine(
            NotNull(settings, "零字节文件应返回默认配置"),
            Eq(SettingsLoadOutcome.RecoveredFromCorruption, report.Outcome, "Outcome"),
            Eq(true, settings!.Rules.Count >= 10, "回退到种子表"));
    }

    private static string? CaseSettingsInvalidFields(Sandbox sandbox)
    {
        var dir = Path.Combine(sandbox.Settings, "invalid");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """
        {
          "schemaVersion": 99,
          "defaultShaderName": "no_such_shader.vfx",
          "textureRoot": "D:/keep/me",
          "autoAssignOnDrop": "yes-please",
          "recurseTextureFolders": false,
          "adoptDroppedVmatFolderAsMaterialsRoot": true,
          "rules": [
            { "suffix": "_normal",    "role": "Normal",    "enabled": true },
            { "suffix": "",           "role": "Color",     "enabled": true },
            { "suffix": "_color",     "role": "NotARole",  "enabled": true },
            { "suffix": "_Normal.png","role": "normal",    "enabled": true },
            { "suffix": "_ao",        "role": "Roughness", "enabled": "maybe" },
            { "suffix": "_ao2",       "role": "AO",        "enabled": true }
          ]
        }
        """);

        var settings = VmatGeneratorSettingsStore.LoadFromFile(path, out var report);

        var repaired = report.RepairedFields.ToList();
        return Combine(
            NotNull(settings, "字段非法不应导致返回 null"),
            Eq(SettingsLoadOutcome.Repaired, report.Outcome, "Outcome"),
            Eq(true, report.LoadedFromDisk, "LoadedFromDisk"),
            // 合法字段必须保留
            Eq("D:/keep/me", settings!.TextureRoot, "合法的 textureRoot 应保留"),
            Eq(false, settings.RecurseTextureFolders, "合法的 recurseTextureFolders 应保留"),
            Eq(true, settings.AdoptDroppedVmatFolderAsMaterialsRoot, "合法的 adopt 开关应保留"),
            // 非法字段必须被回退
            Eq(VmatGeneratorSettings.CurrentSchemaVersion, settings.SchemaVersion, "schemaVersion 回退为 1"),
            Eq(VmatGeneratorSettings.DefaultShaderNameValue, settings.DefaultShaderName, "defaultShaderName 回退"),
            Eq(true, settings.AutoAssignOnDrop, "autoAssignOnDrop 类型非法 → 回退为默认 true"),
            // 规则表：空后缀丢弃、非法角色置 Unknown 并停用、重复后缀只留第一条、enabled 类型非法置 true。
            // 注一：§3.5 要求 role 必须满足 Enum.TryParse<TextureRole>，"AO" 是别名不是枚举名，
            //      因此判为非法是符合规格的——别名只参与 P4/P5 判定，不是配置里的合法角色名。
            // 注二：角色非法优先于 enabled 修复（角色读不懂的规则必须停用），
            //      所以「enabled 类型非法」这条单独用一条合法角色的规则来验。
            Eq(4, settings.Rules.Count, $"规则条数（空后缀丢弃 + 重复丢弃，实际 {settings.Rules.Count}）"),
            Eq("_normal", settings.Rules[0].Suffix, "第 1 条规则"),
            Eq(TextureRole.Normal, settings.Rules[0].Role, "角色名大小写不敏感解析"),
            Eq(TextureRole.Unknown, settings.Rules[1].Role, "非法角色名 → Unknown"),
            Eq(false, settings.Rules[1].Enabled, "非法角色 → 停用"),
            Eq(TextureRole.Roughness, settings.Rules[2].Role, "合法角色保留"),
            Eq(true, settings.Rules[2].Enabled, "enabled 类型非法 → 置 true"),
            Eq(TextureRole.Unknown, settings.Rules[3].Role, "别名 AO 不是合法角色名 → Unknown"),
            Eq(false, settings.Rules[3].Enabled, "别名 AO 规则被停用"),
            Eq(true, repaired.Contains("schemaVersion"), "RepairedFields 含 schemaVersion"),
            Eq(true, repaired.Contains("defaultShaderName"), "RepairedFields 含 defaultShaderName"),
            Eq(true, repaired.Contains("autoAssignOnDrop"), "RepairedFields 含 autoAssignOnDrop"),
            Eq(true, repaired.Contains("rules[1].suffix"), "RepairedFields 含 rules[1].suffix"),
            Eq(true, repaired.Contains("rules[2].role"), "RepairedFields 含 rules[2].role"),
            Eq(true, repaired.Contains("rules[4].enabled"), "RepairedFields 含 rules[4].enabled"),
            Eq(true, repaired.Contains("rules[5].role"), "RepairedFields 含 rules[5].role"),
            Eq(true, repaired.Any(f => f.StartsWith("rules[3].suffix(重复)", StringComparison.Ordinal)), "RepairedFields 含重复后缀"));
    }

    private static string? CaseSettingsEmptyRulesPreserved(Sandbox sandbox)
    {
        var dir = Path.Combine(sandbox.Settings, "empty-rules");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """{ "rules": [] }""");

        var settings = VmatGeneratorSettingsStore.LoadFromFile(path, out var report);
        return Combine(
            NotNull(settings, "应返回配置"),
            Eq(0, settings!.Rules.Count, "显式空规则表必须保留为空，不回填"),
            Eq(SettingsLoadOutcome.Loaded, report.Outcome, "空规则表不算损坏也不算非法"));
    }

    private static string? CaseSettingsRoundTrip(Sandbox sandbox)
    {
        var dir = Path.Combine(sandbox.Settings, "roundtrip");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");

        var original = VmatGeneratorSettings.CreateDefault();
        original.DefaultShaderName = "csgo_complex.vfx";
        original.TextureRoot = "D:/art/textures";
        original.AutoAssignOnDrop = false;
        original.Rules.Insert(0, new TextureSuffixRule("_weird.Suffix.png", TextureRole.FoamNormal, false));

        var save = VmatGeneratorSettingsStore.SaveToFile(original, path);
        if (!save.Success) return $"保存失败：{save.ErrorMessage}";

        var reloaded = VmatGeneratorSettingsStore.LoadFromFile(path, out var report);
        if (reloaded is null) return "读回失败：返回 null";

        return Combine(
            Eq(save.FilePath, path, "保存路径"),
            Eq(SettingsLoadOutcome.Loaded, report.Outcome, "往返后应判定为 Loaded"),
            Eq(0, report.RepairedFields.Count, "往返不应触发修复"),
            Eq(original.SchemaVersion, reloaded.SchemaVersion, "schemaVersion"),
            Eq(original.DefaultShaderName, reloaded.DefaultShaderName, "defaultShaderName"),
            Eq(original.TextureRoot, reloaded.TextureRoot, "textureRoot"),
            Eq(original.AutoAssignOnDrop, reloaded.AutoAssignOnDrop, "autoAssignOnDrop"),
            Eq(original.Rules.Count, reloaded.Rules.Count, "规则条数"),
            Eq("_weird.Suffix.png", reloaded.Rules[0].Suffix, "首条规则后缀原样保留"),
            Eq(TextureRole.FoamNormal, reloaded.Rules[0].Role, "枚举按名称往返"),
            Eq(false, reloaded.Rules[0].Enabled, "停用状态往返"),
            Eq(true, File.Exists(path), "配置文件已落盘"),
            Eq(false, File.Exists(path + ".tmp"), "临时文件应已被移动掉"));
    }

    // ── 拖拽分档用例 ────────────────────────────────────────────────────────

    /// <summary>
    /// §2.2 的 D1–D6 六类分档 + 两个根目录候选。
    ///
    /// <para>在沙箱里造一个同时含「材质目录（.vmat 在子目录里）」与「贴图目录（顶层 + 子目录）」的
    /// drop 树，逐类拖入并断言分类与根目录候选。重点覆盖两条最容易被改坏的规则：
    /// 混合内容时材质根取「第一个含 .vmat 的目录」，以及 .vmat 探测<b>始终</b>递归、
    /// 不受 <c>recurseTextureFolders</c> 影响（后者由 <see cref="CaseDropNoTextureRecursion"/> 覆盖）。</para>
    /// </summary>
    private static string? CaseDropMatrix(Sandbox sandbox)
    {
        var drop = Path.Combine(sandbox.Root, "drop");
        var mats = Path.Combine(drop, "materials");
        var tex = Path.Combine(drop, "textures");
        Directory.CreateDirectory(Path.Combine(mats, "sub"));
        Directory.CreateDirectory(Path.Combine(tex, "sub"));
        var vmat1 = Path.Combine(mats, "a.vmat");
        var vmat2 = Path.Combine(mats, "sub", "b.vmat");
        var topTexture = Path.Combine(tex, "wall_normal.png");
        var deepTexture = Path.Combine(tex, "sub", "wall_diffuse.png");
        var readme = Path.Combine(drop, "readme.txt");
        File.WriteAllBytes(vmat1, Array.Empty<byte>());
        File.WriteAllBytes(vmat2, Array.Empty<byte>());
        File.WriteAllBytes(topTexture, Array.Empty<byte>());
        File.WriteAllBytes(deepTexture, Array.Empty<byte>());
        File.WriteAllText(readme, "not importable");

        var svc = new DropImportService(new MaterialScanner(), recurseTextureFolders: true);

        var m1 = svc.Analyze(new[] { mats });
        var m2 = svc.Analyze(new[] { tex });
        var m3 = svc.Analyze(new[] { vmat1 });
        var m4 = svc.Analyze(new[] { topTexture });
        var m5 = svc.Analyze(new[] { vmat1, topTexture });
        var m6 = svc.Analyze(new[] { readme });
        var m7 = svc.Analyze(Array.Empty<string>());

        return Combine(
            // D1 材质文件夹
            Eq(DropCategory.MaterialFolder, m1.Category, "D1 材质文件夹分类"),
            Eq(mats, m1.MaterialRootCandidate, "D1 材质根候选 = 被拖目录"),
            Eq(null, m1.TextureRootCandidate, "D1 不给贴图根候选"),
            Eq(2, m1.VmatFiles.Count, "D1 .vmat 探测递归到子目录"),
            // D4 只含贴图的文件夹
            Eq(DropCategory.TextureOnlyFolder, m2.Category, "D4 贴图文件夹分类"),
            Eq(null, m2.MaterialRootCandidate, "D4 不改材质根目录"),
            Eq(tex, m2.TextureRootCandidate, "D4 贴图根候选 = 被拖目录"),
            Eq(2, m2.TextureFiles.Count, "D4 递归开启时收全部 2 张"),
            // D2 / D3
            Eq(DropCategory.VmatFile, m3.Category, "D2 .vmat 文件分类"),
            Eq(mats, m3.MaterialRootCandidate, "D2 材质根候选 = .vmat 父目录"),
            Eq(DropCategory.TextureFiles, m4.Category, "D3 贴图文件分类"),
            Eq(tex, m4.TextureRootCandidate, "D3 贴图根候选 = 公共父目录"),
            // D5 混合
            Eq(DropCategory.Mixed, m5.Category, "D5 混合分类"),
            Eq(mats, m5.MaterialRootCandidate, "D5 材质根候选 = 第一个含 .vmat 的目录"),
            Eq(tex, m5.TextureRootCandidate, "D5 贴图根候选 = 贴图的公共父目录"),
            // D6 / 空
            Eq(DropCategory.Unsupported, m6.Category, "D6 无关内容分类"),
            Eq(0, m6.TextureFiles.Count, "D6 零贴图"),
            Eq(0, m6.VmatFiles.Count, "D6 零 .vmat"),
            Eq(DropCategory.None, m7.Category, "空拖入分类"),
            // §2.3 CanAccept
            Eq(true, DropImportService.CanAccept(new[] { vmat1 }), "CanAccept(.vmat)"),
            Eq(true, DropImportService.CanAccept(new[] { topTexture }), "CanAccept(贴图)"),
            Eq(false, DropImportService.CanAccept(null), "CanAccept(null)"),
            Eq(false, DropImportService.CanAccept(Array.Empty<string>()), "CanAccept(空数组)"),
            Eq(false, DropImportService.CanAccept(new[] { "   " }), "CanAccept(全空白)"));
    }

    /// <summary>
    /// §2.2 D4 把「贴图是否递归」锁为「true → AllDirectories，false → TopDirectoryOnly」。
    ///
    /// <para><b>回归用例。</b>实现早先在 <c>recurseTextureFolders == false</c> 时把顶层贴图
    /// 也一起丢掉，于是用户一取消勾选这个开关，拖入贴图文件夹就会得到 0 张贴图、分档退化成
    /// <see cref="DropCategory.Unsupported"/>——配置开关等于把功能整个关死。
    /// 这里断言关闭递归后顶层贴图仍然被收集，只有子目录里的被跳过并在摘要中说明。</para>
    /// </summary>
    private static string? CaseDropNoTextureRecursion(Sandbox sandbox)
    {
        var drop = Path.Combine(sandbox.Root, "drop-norecurse");
        var mats = Path.Combine(drop, "materials");
        var tex = Path.Combine(drop, "textures");
        Directory.CreateDirectory(Path.Combine(mats, "sub"));
        Directory.CreateDirectory(Path.Combine(tex, "sub"));
        File.WriteAllBytes(Path.Combine(mats, "sub", "a.vmat"), Array.Empty<byte>());
        var topTexture = Path.Combine(tex, "wall_normal.png");
        File.WriteAllBytes(topTexture, Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(tex, "sub", "wall_diffuse.png"), Array.Empty<byte>());

        var svc = new DropImportService(new MaterialScanner(), recurseTextureFolders: false);

        var texResult = svc.Analyze(new[] { tex });
        var matResult = svc.Analyze(new[] { mats });

        return Combine(
            Eq(DropCategory.TextureOnlyFolder, texResult.Category, "关闭递归时贴图文件夹仍应判为 TextureOnlyFolder"),
            Eq(1, texResult.TextureFiles.Count, "关闭递归 = TopDirectoryOnly，顶层贴图仍要收"),
            Eq(topTexture, texResult.TextureFiles.Count > 0 ? texResult.TextureFiles[0] : null, "收到的正是顶层那张"),
            Eq(0, texResult.TextureFiles.Count(t => !IsDirectChildOf(tex, t)), "子目录贴图不得被计入"),
            Eq(true, texResult.Summary.Contains("子目录贴图未计入", StringComparison.Ordinal), "摘要应说明跳过了子目录贴图"),
            Eq(false, texResult.RequiresRecursion, "关闭递归时 RequiresRecursion = false"),
            // 独立开关：.vmat 探测始终 AllDirectories，不受 recurseTextureFolders 影响
            Eq(1, matResult.VmatFiles.Count, "关闭贴图递归不影响 .vmat 的 AllDirectories 探测"),
            Eq(DropCategory.MaterialFolder, matResult.Category, "材质文件夹分类不受贴图递归开关影响"));
    }

    private static bool IsDirectChildOf(string root, string file)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty),
                Path.GetFullPath(root),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ── 沙箱与辅助 ──────────────────────────────────────────────────────────

    private static ShaderTemplate Require(string shaderName) =>
        ShaderCatalog.Find(shaderName)
        ?? throw new InvalidOperationException($"着色器 {shaderName} 不在 ShaderCatalog 中");

    private static string? ValueOf(TextureAssignResult result, TextureRole role) =>
        result.Assignments.FirstOrDefault(a => a.Role == role)?.VmatPath;

    private static string? KeyOf(TextureAssignResult result, TextureRole role) =>
        result.Assignments.FirstOrDefault(a => a.Role == role)?.ParameterKey;

    private static Sandbox CreateSandbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "VmatGenerator.SelfCheck." + Guid.NewGuid().ToString("N"));
        var textureRoot = Path.Combine(root, "tex");
        var outside = Path.Combine(root, "outside");
        var settings = Path.Combine(root, "settings");
        foreach (var relative in new[]
        {
            "wall", "ui", "fx", "water",
        })
        {
            Directory.CreateDirectory(Path.Combine(textureRoot, relative));
        }
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(settings);

        foreach (var relative in new[]
        {
            "wall/concrete_wall_normal.png", "wall/concrete_wall_diffuse.png",
            "wall/brick_normal.png", "wall/brick_n.png",
            "wall/a_normal.png", "wall/b_normal.png",
            "normal.png",
            "ui/logo.png",
            "water/foam_normal.png", "water/surf_foamnormal.png", "water/waves_normal.png", "water/debris.png",
            "fx/fx_mask.png", "fx/banner_mask.png",
        })
        {
            File.WriteAllBytes(Path.Combine(textureRoot, relative.Replace('/', Path.DirectorySeparatorChar)), Array.Empty<byte>());
        }
        File.WriteAllBytes(Path.Combine(outside, "wall_normal.png"), Array.Empty<byte>());

        return new Sandbox(root, textureRoot, outside, settings);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception)
        {
            // 临时目录删不掉不影响自检结论。
        }
    }

    private sealed record Sandbox(string Root, string TextureRoot, string Outside, string Settings);
}