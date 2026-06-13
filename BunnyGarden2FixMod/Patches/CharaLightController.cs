using System.Collections.Generic;
using BunnyGarden2FixMod.Utils;
using UnityEngine;

namespace BunnyGarden2FixMod.Patches;

/// <summary>
/// バーシーンのキャスト照明 (CharaLight 配下のライト) の明るさを、
/// 設定値 Configs.CharaLightMultiplier に従って毎フレーム反映する。
///
/// 方式A (毎フレーム反映): バーのライトはシーン遷移で作り直されるため、
/// 「一度設定して終わり」ではなく常に設定値を監視して反映し続ける。
/// 対象は CharaLight 配下のみ (数個) なので毎フレーム走査でも軽量。
///
/// 倍率方式: 各ライトの「元の intensity」を初回に記録し、
/// 毎フレーム「元の値 × 設定倍率」を適用する。これにより設定を戻せば元に戻る。
/// </summary>
public class CharaLightController : MonoBehaviour
{
    public static CharaLightController Instance { get; private set; }

    /// <summary>起動時に呼ぶ。DontDestroyOnLoad なホストに本コンポーネントを載せる。</summary>
    public static void Initialize(GameObject parent)
    {
        if (Instance != null) return;
        var host = new GameObject("BG2CharaLightController");
        DontDestroyOnLoad(host);
        Instance = host.AddComponent<CharaLightController>();
        PatchLogger.LogInfo("[CharaLight] Initialized");
    }

    // 各ライトの元の intensity を記録 (Light -> 元 intensity)。
    // ライトがシーン遷移で破棄されると Light は null になるため、適用時に null チェックする。
    private readonly Dictionary<Light, float> m_originalIntensity = new Dictionary<Light, float>();

    // 最後に適用した倍率。変化が無ければ再適用をスキップして無駄を減らす。
    private float m_lastAppliedMultiplier = -1f;

    // CharaLight 配下ライトの再収集は毎フレームやると重いので一定間隔で行う。
    private float m_nextScanTime = 0f;
    private const float SCAN_INTERVAL = 1.0f; // 秒。シーン遷移を拾うのに十分な頻度

    private readonly List<Light> m_charaLights = new List<Light>();

    private void Update()
    {
        float mult = Configs.CharaLightMultiplier.Value;

        // 定期的に CharaLight 配下ライトを収集し直す (シーン遷移・ライト再生成に追従)。
        if (Time.unscaledTime >= m_nextScanTime)
        {
            m_nextScanTime = Time.unscaledTime + SCAN_INTERVAL;
            RescanCharaLights();
            // 収集し直したら倍率を再適用する (新しいライトに反映するため)。
            m_lastAppliedMultiplier = -1f;
        }

        // 倍率が変わっていなければ何もしない (毎フレームの代入を避ける)。
        if (Mathf.Approximately(mult, m_lastAppliedMultiplier)) return;
        m_lastAppliedMultiplier = mult;

        ApplyMultiplier(mult);
    }

    /// <summary>CharaLight 配下のライトを収集し、元 intensity を記録する。</summary>
    private void RescanCharaLights()
    {
        m_charaLights.Clear();
        foreach (var light in Resources.FindObjectsOfTypeAll<Light>())
        {
            if (light == null) continue;
            if (light.gameObject.hideFlags != HideFlags.None) continue;
            if (!light.gameObject.scene.IsValid()) continue;
            if (!IsCharaLight(light)) continue;

            m_charaLights.Add(light);
            if (!m_originalIntensity.ContainsKey(light))
            {
                m_originalIntensity[light] = light.intensity;
            }
        }
    }

    /// <summary>記録済みの元 intensity に倍率を掛けて適用する。</summary>
    private void ApplyMultiplier(float mult)
    {
        foreach (var light in m_charaLights)
        {
            if (light == null) continue;
            if (m_originalIntensity.TryGetValue(light, out float orig))
            {
                light.intensity = orig * mult;
            }
        }
    }

    /// <summary>あるライトが CharaLight 配下かどうか (transform 親を辿って判定)。</summary>
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
}
