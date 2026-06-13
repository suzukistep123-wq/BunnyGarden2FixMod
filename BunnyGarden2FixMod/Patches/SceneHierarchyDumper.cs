using BunnyGarden2FixMod.Utils;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BunnyGarden2FixMod.Patches;

/// <summary>
/// 診断用: 現在 active なシーンの GameObject 階層を BepInEx ログに出力する。
/// 用途: バーシーン内のカウンター・棚など、トグル対象オブジェクトを特定するため。
/// 一時的なツール。対象特定後は削除予定。
/// </summary>
internal static class SceneHierarchyDumper
{
    /// <summary>active シーン全 root の階層をダンプする (深さ無制限)。</summary>
    public static void DumpActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        PatchLogger.LogInfo($"[SceneDumper] === Active Scene: '{scene.name}' (rootCount={scene.rootCount}) ===");
        var sb = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects())
        {
            sb.Clear();
            BuildHierarchy(root.transform, 0, sb);
            // 1 root ごとに 1 ログエントリ。ログが長くなりすぎないようにし、root 単位で切り分けやすくする
            PatchLogger.LogInfo($"[SceneDumper]\n{sb}");
        }
        PatchLogger.LogInfo($"[SceneDumper] === end ===");
    }

    /// <summary>
    /// すべてのロード済みシーンをダンプする (Additive ロードされた追加シーンも含める)。
    /// バーシーンなどは EnvScene として additive にロードされている可能性があるため。
    /// </summary>
    public static void DumpAllLoadedScenes()
    {
        int count = SceneManager.sceneCount;
        PatchLogger.LogInfo($"[SceneDumper] === Loaded Scenes: {count} ===");
        for (int i = 0; i < count; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            PatchLogger.LogInfo($"[SceneDumper] --- Scene [{i}]: '{scene.name}' (rootCount={scene.rootCount}, isActive={scene == SceneManager.GetActiveScene()}) ---");
            var sb = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects())
            {
                sb.Clear();
                BuildHierarchy(root.transform, 0, sb);
                PatchLogger.LogInfo($"[SceneDumper]\n{sb}");
            }
        }
        PatchLogger.LogInfo($"[SceneDumper] === end ===");
    }

    /// <summary>
    /// 軽量版: トップレベルから 2 階層下までだけダンプ。最初の見当をつける用。
    /// </summary>
    public static void DumpAllLoadedScenesShallow()
    {
        int count = SceneManager.sceneCount;
        PatchLogger.LogInfo($"[SceneDumper] === Shallow dump (depth<=2) ===");
        for (int i = 0; i < count; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            PatchLogger.LogInfo($"[SceneDumper] --- Scene [{i}]: '{scene.name}' ---");
            var sb = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects())
            {
                sb.Clear();
                BuildHierarchy(root.transform, 0, sb, maxDepth: 2);
                PatchLogger.LogInfo($"[SceneDumper]\n{sb}");
            }
        }
        PatchLogger.LogInfo($"[SceneDumper] === end ===");
    }

    /// <summary>
    /// 全ロード済みシーンから指定の名前にマッチする GameObject を探し、その配下を深さ無制限でダンプする。
    /// 名前は部分一致 (Contains)。複数マッチした場合は全部出す。
    /// 例: FindAndDump("ENV_Birthday") で ENV_Birthday の全配下を表示。
    /// </summary>
    public static void FindAndDump(string nameContains)
    {
        if (string.IsNullOrEmpty(nameContains)) return;
        PatchLogger.LogInfo($"[SceneDumper] === Find and dump: '{nameContains}' ===");
        int hitCount = 0;
        int count = SceneManager.sceneCount;
        for (int i = 0; i < count; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                FindAndDumpRecurse(root.transform, nameContains, scene.name, ref hitCount);
            }
        }
        PatchLogger.LogInfo($"[SceneDumper] === end (hits={hitCount}) ===");
    }

    private static void FindAndDumpRecurse(Transform t, string nameContains, string sceneName, ref int hitCount)
    {
        if (t == null) return;
        if (t.name != null && t.name.IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            hitCount++;
            var sb = new StringBuilder();
            sb.AppendLine($"--- Hit [{hitCount}] in scene '{sceneName}': path={GetPath(t)} ---");
            BuildHierarchy(t, 0, sb);
            PatchLogger.LogInfo($"[SceneDumper]\n{sb}");
        }
        for (int i = 0; i < t.childCount; i++)
        {
            FindAndDumpRecurse(t.GetChild(i), nameContains, sceneName, ref hitCount);
        }
    }

    private static string GetPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        var p = t.parent;
        while (p != null)
        {
            sb.Insert(0, "/");
            sb.Insert(0, p.name);
            p = p.parent;
        }
        return sb.ToString();
    }

    /// <summary>
    /// シーン内の全 Light コンポーネントの情報 (名前/種類/色/強さ/有効状態/範囲) をログ出力する。
    /// 無効 (INACTIVE) なライトも含めるため Resources.FindObjectsOfTypeAll を使う。
    /// 加えて環境光 (Ambient) など RenderSettings の情報も出す。
    /// 用途: バーのオレンジ系ライティングを作っているライトを特定するため。
    /// 一時的なツール。対象特定後は削除予定。
    /// </summary>
    public static void DumpAllLights()
    {
        PatchLogger.LogInfo($"[LightDumper] === All Lights ===");

        PatchLogger.LogInfo($"[LightDumper] RenderSettings: ambientMode={RenderSettings.ambientMode}, "
            + $"ambientLight={ColorStr(RenderSettings.ambientLight)}, ambientIntensity={RenderSettings.ambientIntensity:F3}");
        PatchLogger.LogInfo($"[LightDumper] RenderSettings: ambientSky={ColorStr(RenderSettings.ambientSkyColor)}, "
            + $"ambientEquator={ColorStr(RenderSettings.ambientEquatorColor)}, ambientGround={ColorStr(RenderSettings.ambientGroundColor)}");
        PatchLogger.LogInfo($"[LightDumper] RenderSettings: fog={RenderSettings.fog}, fogColor={ColorStr(RenderSettings.fogColor)}");

        var lights = Resources.FindObjectsOfTypeAll<Light>();
        PatchLogger.LogInfo($"[LightDumper] Light count = {lights.Length}");

        var sb = new StringBuilder();
        int idx = 0;
        foreach (var light in lights)
        {
            if (light == null) continue;
            if (light.gameObject.hideFlags != HideFlags.None) continue;
            if (!light.gameObject.scene.IsValid()) continue;

            idx++;
            bool activeInHier = light.gameObject.activeInHierarchy;
            sb.Clear();
            sb.AppendLine($"[Light {idx}] path={GetPath(light.transform)}");
            sb.AppendLine($"  type={light.type}, color={ColorStr(light.color)}, intensity={light.intensity:F3}");
            sb.AppendLine($"  enabled={light.enabled}, activeInHierarchy={activeInHier}, range={light.range:F2}, spotAngle={light.spotAngle:F1}");
            sb.AppendLine($"  shadows={light.shadows}, renderMode={light.renderMode}, cullingMask={light.cullingMask}");
            PatchLogger.LogInfo($"[LightDumper]\n{sb}");
        }

        PatchLogger.LogInfo($"[LightDumper] === end (valid lights={idx}) ===");
    }

    /// <summary>Color を読みやすい RGBA 文字列にする (0-1 と 0-255 両方表示)。</summary>
    private static string ColorStr(Color c)
    {
        return $"RGBA({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2}) [255: {(int)(c.r * 255)},{(int)(c.g * 255)},{(int)(c.b * 255)}]";
    }

    /// <summary>
    /// 診断: シーン内の Volume (URP ポストプロセス) と ReflectionProbe を列挙する。
    /// 用途: バー環境の明るさ・オレンジ感がポストプロセス由来か環境マップ由来かを切り分けるため。
    /// 各 Volume にどんなオーバーライド (Color Adjustments / Tonemapping / Bloom 等) が
    /// 載っているかを出す。中身の数値までは出さず、まず「何があるか」を把握する。
    /// </summary>
    public static void DumpPostProcessing()
    {
        PatchLogger.LogInfo($"[VolumeDumper] === Post Processing / Volumes ===");

        // --- Volume 一覧 ---
        var volumes = Resources.FindObjectsOfTypeAll<Volume>();
        int volCount = 0;
        foreach (var vol in volumes)
        {
            if (vol == null) continue;
            if (vol.gameObject.hideFlags != HideFlags.None) continue;
            if (!vol.gameObject.scene.IsValid()) continue;

            volCount++;
            var sb = new StringBuilder();
            sb.AppendLine($"[Volume {volCount}] path={GetPath(vol.transform)}");
            sb.AppendLine($"  enabled={vol.enabled}, activeInHierarchy={vol.gameObject.activeInHierarchy}, "
                + $"isGlobal={vol.isGlobal}, priority={vol.priority}, weight={vol.weight}");

            var profile = vol.sharedProfile;
            if (profile == null)
            {
                sb.AppendLine($"  profile=null");
            }
            else
            {
                sb.AppendLine($"  profile='{profile.name}', overrides={profile.components.Count}");
                // 各オーバーライド (VolumeComponent) の型名と active 状態を出す
                foreach (var comp in profile.components)
                {
                    if (comp == null) continue;
                    sb.AppendLine($"    - {comp.GetType().Name} (active={comp.active})");
                }
            }
            PatchLogger.LogInfo($"[VolumeDumper]\n{sb}");
        }
        PatchLogger.LogInfo($"[VolumeDumper] Volume count = {volCount}");

        // --- ReflectionProbe 一覧 (Cubemap 環境マップ候補) ---
        var probes = Resources.FindObjectsOfTypeAll<ReflectionProbe>();
        int probeCount = 0;
        foreach (var probe in probes)
        {
            if (probe == null) continue;
            if (probe.gameObject.hideFlags != HideFlags.None) continue;
            if (!probe.gameObject.scene.IsValid()) continue;

            probeCount++;
            PatchLogger.LogInfo($"[VolumeDumper] [ReflectionProbe {probeCount}] path={GetPath(probe.transform)}, "
                + $"enabled={probe.enabled}, mode={probe.mode}, intensity={probe.intensity:F2}, "
                + $"boxProjection={probe.boxProjection}");
        }
        PatchLogger.LogInfo($"[VolumeDumper] ReflectionProbe count = {probeCount}");

        PatchLogger.LogInfo($"[VolumeDumper] === end ===");
    }

    /// <summary>
    /// 診断: Volume の各オーバーライド (VolumeComponent) のパラメータ値を読み出す。
    /// VolumeParameter 派生フィールドをリフレクションで総当たりし、overrideState と値を出す。
    /// 用途: バーのオレンジ・暗さを作っている具体的なパラメータ (LiftGammaGain の gain 等) を特定するため。
    /// 型を名指ししないため URP のバージョン差に強い。
    /// </summary>
    public static void DumpVolumeParameters()
    {
        PatchLogger.LogInfo($"[VolumeParam] === Volume Parameter Details ===");

        var volumes = Resources.FindObjectsOfTypeAll<Volume>();
        foreach (var vol in volumes)
        {
            if (vol == null) continue;
            if (vol.gameObject.hideFlags != HideFlags.None) continue;
            if (!vol.gameObject.scene.IsValid()) continue;

            var profile = vol.sharedProfile;
            if (profile == null) continue;

            PatchLogger.LogInfo($"[VolumeParam] Volume '{GetPath(vol.transform)}' profile='{profile.name}'");

            foreach (var comp in profile.components)
            {
                if (comp == null) continue;
                var sb = new StringBuilder();
                sb.AppendLine($"  [{comp.GetType().Name}] active={comp.active}");

                // VolumeComponent の各 public フィールドのうち VolumeParameter 派生のものを読む
                var fields = comp.GetType().GetFields(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var f in fields)
                {
                    if (!typeof(VolumeParameter).IsAssignableFrom(f.FieldType)) continue;
                    var param = f.GetValue(comp) as VolumeParameter;
                    if (param == null) continue;

                    // overrideState (このパラメータが有効か) と値を取得
                    bool over = param.overrideState;
                    string valStr = DescribeParameterValue(param);
                    sb.AppendLine($"    {f.Name}: override={over}, value={valStr}");
                }
                PatchLogger.LogInfo($"[VolumeParam]\n{sb}");
            }
        }
        PatchLogger.LogInfo($"[VolumeParam] === end ===");
    }

    /// <summary>VolumeParameter の値を文字列化する。型に応じて見やすく出す。</summary>
    private static string DescribeParameterValue(VolumeParameter param)
    {
        // VolumeParameter<T> の値は GetValue<T> ではなくリフレクションで "value" プロパティを読む
        try
        {
            var prop = param.GetType().GetProperty("value");
            if (prop == null) return "(no value prop)";
            var v = prop.GetValue(param);
            if (v == null) return "null";
            if (v is Color c) return ColorStr(c);
            if (v is Vector4 v4) return $"({v4.x:F3},{v4.y:F3},{v4.z:F3},{v4.w:F3})";
            if (v is Vector3 v3) return $"({v3.x:F3},{v3.y:F3},{v3.z:F3})";
            if (v is float fl) return fl.ToString("F3");
            return v.ToString();
        }
        catch (System.Exception e)
        {
            return $"(err: {e.Message})";
        }
    }

    // ===== 段階2: ライト明るさ調整 (仮実装、効果体感用) =====
    // グループ単位で intensity に倍率を掛ける。元の値を記録しておき、リセットで戻せる。
    // キャラ照明 (CharaLight 配下) と 環境ライト (それ以外) で別々の倍率を持つ。

    // 元の intensity を覚えておく辞書 (Light インスタンス -> 元の intensity)
    private static readonly System.Collections.Generic.Dictionary<Light, float> s_originalIntensity
        = new System.Collections.Generic.Dictionary<Light, float>();

    private static float s_charaMultiplier = 1.0f;
    private static float s_envMultiplier = 1.0f;

    private const float STEP = 0.1f; // 1 回 10% 増減

    /// <summary>あるライトがキャラ照明グループ (CharaLight 配下) かどうか。</summary>
    private static bool IsCharaLight(Light light)
    {
        var t = light.transform;
        while (t != null)
        {
            if (t.name == "CharaLight") return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>有効でシーンに属する全ライトを列挙 (調整対象)。</summary>
    private static System.Collections.Generic.List<Light> CollectAdjustableLights()
    {
        var result = new System.Collections.Generic.List<Light>();
        foreach (var light in Resources.FindObjectsOfTypeAll<Light>())
        {
            if (light == null) continue;
            if (light.gameObject.hideFlags != HideFlags.None) continue;
            if (!light.gameObject.scene.IsValid()) continue;
            result.Add(light);
        }
        return result;
    }

    /// <summary>元の intensity を (まだ記録していなければ) 記録する。</summary>
    private static void EnsureOriginalRecorded(Light light)
    {
        if (!s_originalIntensity.ContainsKey(light))
        {
            s_originalIntensity[light] = light.intensity;
        }
    }

    /// <summary>キャラ照明グループの明るさ倍率を変更する。delta は +STEP / -STEP。</summary>
    public static void AdjustCharaBrightness(float delta)
    {
        s_charaMultiplier = Mathf.Max(0f, s_charaMultiplier + delta);
        ApplyMultipliers();
        PatchLogger.LogInfo($"[LightAdjust] CharaLight multiplier = {s_charaMultiplier:F2}");
    }

    /// <summary>環境ライトグループの明るさ倍率を変更する。delta は +STEP / -STEP。</summary>
    public static void AdjustEnvBrightness(float delta)
    {
        s_envMultiplier = Mathf.Max(0f, s_envMultiplier + delta);
        ApplyMultipliers();
        PatchLogger.LogInfo($"[LightAdjust] Env multiplier = {s_envMultiplier:F2}");
    }

    /// <summary>現在の倍率を全ライトに適用する (元の値 × 倍率)。</summary>
    private static void ApplyMultipliers()
    {
        foreach (var light in CollectAdjustableLights())
        {
            EnsureOriginalRecorded(light);
            float orig = s_originalIntensity[light];
            float mult = IsCharaLight(light) ? s_charaMultiplier : s_envMultiplier;
            light.intensity = orig * mult;
        }
    }

    /// <summary>全ライトを元の明るさに戻し、倍率を 1.0 にリセットする。</summary>
    public static void ResetBrightness()
    {
        foreach (var kv in s_originalIntensity)
        {
            if (kv.Key != null) kv.Key.intensity = kv.Value;
        }
        s_charaMultiplier = 1.0f;
        s_envMultiplier = 1.0f;
        PatchLogger.LogInfo($"[LightAdjust] Reset to original. chara=1.00, env=1.00");
    }
    // ===== 段階2 ここまで =====

    // ===== お試し: 環境ライトの色を一括で白に寄せる (効くか確認用) =====
    // 環境ライト (CharaLight 配下でない) の color を、元の色から白へブレンドする。
    // intensity は効かなかったが color なら効くか、を検証するためのお試し実装。

    private static readonly System.Collections.Generic.Dictionary<Light, Color> s_originalColor
        = new System.Collections.Generic.Dictionary<Light, Color>();

    private static float s_envWhiteBlend = 0f; // 0=元の色, 1=白

    /// <summary>環境ライトの色を白へ寄せる度合いを変更する。delta は +/-。</summary>
    public static void AdjustEnvColorWhite(float delta)
    {
        s_envWhiteBlend = Mathf.Clamp01(s_envWhiteBlend + delta);
        foreach (var light in CollectAdjustableLights())
        {
            if (IsCharaLight(light)) continue; // 環境ライトだけ
            if (!s_originalColor.ContainsKey(light))
            {
                s_originalColor[light] = light.color;
            }
            light.color = Color.Lerp(s_originalColor[light], Color.white, s_envWhiteBlend);
        }
        PatchLogger.LogInfo($"[LightColor] Env white blend = {s_envWhiteBlend:F2}");
    }

    /// <summary>環境ライトの色を元に戻す。</summary>
    public static void ResetEnvColor()
    {
        foreach (var kv in s_originalColor)
        {
            if (kv.Key != null) kv.Key.color = kv.Value;
        }
        s_envWhiteBlend = 0f;
        PatchLogger.LogInfo($"[LightColor] Env color reset");
    }
    // ===== お試し ここまで =====

    private static void BuildHierarchy(Transform t, int depth, StringBuilder sb, int maxDepth = -1)
    {
        if (t == null) return;
        string indent = new string(' ', depth * 2);
        // active 状態 / コンポーネント概要 / 子の数
        string activeMark = t.gameObject.activeSelf ? "" : " [INACTIVE]";
        sb.Append(indent);
        sb.Append(t.name);
        sb.Append(activeMark);
        sb.Append($" (children={t.childCount})");
        sb.AppendLine();

        if (maxDepth >= 0 && depth >= maxDepth) return;

        for (int i = 0; i < t.childCount; i++)
        {
            BuildHierarchy(t.GetChild(i), depth + 1, sb, maxDepth);
        }
    }
}
