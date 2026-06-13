using BunnyGarden2FixMod.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BunnyGarden2FixMod.Patches;

/// <summary>
/// バーシーンの「部屋全体の明るさ」を、ポストプロセス (Post Process Global Volume の
/// LiftGammaGain) 経由で調整する。設定値 Configs.RoomBrightnessOffset を毎フレーム反映する。
///
/// 仕組み: LiftGammaGain の gain は (R, G, B, w) の 4 成分で、w がマスターオフセット。
/// 元の w を記録し、「元 w + 設定オフセット」を適用する。設定 0 で元のまま。
///
/// 方式A (毎フレーム反映): Volume はシーン遷移で作り直される可能性があるため、
/// 定期的に探し直して反映し続ける。対象は 1 つの Volume なので軽量。
/// </summary>
public class RoomLightingController : MonoBehaviour
{
    public static RoomLightingController Instance { get; private set; }

    public static void Initialize(GameObject parent)
    {
        if (Instance != null) return;
        var host = new GameObject("BG2RoomLightingController");
        DontDestroyOnLoad(host);
        Instance = host.AddComponent<RoomLightingController>();
        PatchLogger.LogInfo("[RoomLighting] Initialized");
    }

    // 対象の LiftGammaGain (見つかったら保持)。
    private LiftGammaGain m_liftGammaGain;

    // 対象の Bloom (オレンジのにじみの元)。
    private Bloom m_bloom;
    private float m_originalBloomTintR, m_originalBloomTintG, m_originalBloomTintB;
    private bool m_originalBloomRecorded;

    // 元の gain.w (マスターオフセット)。初回に記録する。
    private float m_originalGainW;
    private bool m_originalRecorded;

    // 元の gamma の RGB (オレンジ感の元)。初回に記録する。
    private float m_originalGammaR, m_originalGammaG, m_originalGammaB, m_originalGammaW;
    private bool m_originalGammaRecorded;

    // 元の gain の RGB (オレンジ感の元、その2)。初回に記録する。w は明るさ用に別管理。
    private float m_originalGainR, m_originalGainG, m_originalGainB;
    private bool m_originalGainRgbRecorded;

    private float m_lastAppliedOffset = float.NaN;
    private float m_lastAppliedWarmth = float.NaN;

    private float m_nextScanTime = 0f;
    private const float SCAN_INTERVAL = 1.0f;

    private void Update()
    {
        float offset = Configs.RoomBrightnessOffset.Value;
        float warmth = Configs.RoomWarmthReduction.Value;

        // 定期的に Volume / LiftGammaGain を探し直す (シーン遷移追従)。
        if (Time.unscaledTime >= m_nextScanTime)
        {
            m_nextScanTime = Time.unscaledTime + SCAN_INTERVAL;
            RescanVolume();
            m_lastAppliedOffset = float.NaN; // 再取得したら再適用させる
            m_lastAppliedWarmth = float.NaN;
        }

        if (m_liftGammaGain == null || !m_originalRecorded) return;

        // 明るさオフセットの反映 (変化があれば)。
        if (float.IsNaN(m_lastAppliedOffset) || !Mathf.Approximately(offset, m_lastAppliedOffset))
        {
            m_lastAppliedOffset = offset;
            ApplyOffset(offset);
        }

        // オレンジ抑制の反映 (変化があれば)。
        if (m_originalGammaRecorded &&
            (float.IsNaN(m_lastAppliedWarmth) || !Mathf.Approximately(warmth, m_lastAppliedWarmth)))
        {
            m_lastAppliedWarmth = warmth;
            ApplyWarmth(warmth);
        }
    }

    /// <summary>Post Process Global Volume の LiftGammaGain を探す。</summary>
    private void RescanVolume()
    {
        foreach (var vol in Resources.FindObjectsOfTypeAll<Volume>())
        {
            if (vol == null) continue;
            if (vol.gameObject.hideFlags != HideFlags.None) continue;
            if (!vol.gameObject.scene.IsValid()) continue;

            var profile = vol.sharedProfile;
            if (profile == null) continue;

            if (profile.TryGet<LiftGammaGain>(out var lgg))
            {
                m_liftGammaGain = lgg;
                if (!m_originalRecorded)
                {
                    // gain は Vector4Parameter。value.w がマスターオフセット。
                    m_originalGainW = lgg.gain.value.w;
                    m_originalRecorded = true;
                    PatchLogger.LogInfo($"[RoomLighting] Found LiftGammaGain, original gain.w={m_originalGainW:F3}");
                }
                if (!m_originalGainRgbRecorded)
                {
                    var gn = lgg.gain.value;
                    m_originalGainR = gn.x;
                    m_originalGainG = gn.y;
                    m_originalGainB = gn.z;
                    m_originalGainRgbRecorded = true;
                }
                if (!m_originalGammaRecorded)
                {
                    // gamma も Vector4。RGB がオレンジ感の元。
                    var gm = lgg.gamma.value;
                    m_originalGammaR = gm.x;
                    m_originalGammaG = gm.y;
                    m_originalGammaB = gm.z;
                    m_originalGammaW = gm.w;
                    m_originalGammaRecorded = true;
                    PatchLogger.LogInfo($"[RoomLighting] original gamma=({gm.x:F3},{gm.y:F3},{gm.z:F3},{gm.w:F3})");
                }
                // 同じ profile から Bloom も取得 (オレンジのにじみの元)。
                if (!m_originalBloomRecorded && profile.TryGet<Bloom>(out var bloom))
                {
                    m_bloom = bloom;
                    var t = bloom.tint.value;
                    m_originalBloomTintR = t.r;
                    m_originalBloomTintG = t.g;
                    m_originalBloomTintB = t.b;
                    m_originalBloomRecorded = true;
                    PatchLogger.LogInfo($"[RoomLighting] original bloom.tint=({t.r:F3},{t.g:F3},{t.b:F3})");
                }
                return;
            }
        }
    }

    /// <summary>元の gain.w にオフセットを加えて適用する。</summary>
    private void ApplyOffset(float offset)
    {
        if (m_liftGammaGain == null) return;

        var g = m_liftGammaGain.gain.value;
        g.w = m_originalGainW + offset;
        m_liftGammaGain.gain.value = g;
        // overrideState を有効にしておく (元から override=False だった場合に効かせるため)。
        m_liftGammaGain.gain.overrideState = true;
        m_liftGammaGain.active = true;
    }

    /// <summary>
    /// オレンジ感を抑える。元の gamma と gain の RGB から (1,1,1) へ warmth の割合でブレンドする。
    /// warmth=0 で元のオレンジ、warmth=1 でニュートラル (色補正なし)。
    /// gamma.w / gain.w (明るさ寄与) は変えない (gain.w は明るさスライダー管理)。
    /// </summary>
    private void ApplyWarmth(float warmth)
    {
        if (m_liftGammaGain == null) return;

        // gamma の RGB をニュートラルへ
        var gm = m_liftGammaGain.gamma.value;
        gm.x = Mathf.Lerp(m_originalGammaR, 1f, warmth);
        gm.y = Mathf.Lerp(m_originalGammaG, 1f, warmth);
        gm.z = Mathf.Lerp(m_originalGammaB, 1f, warmth);
        gm.w = m_originalGammaW; // w は据え置き
        m_liftGammaGain.gamma.value = gm;
        m_liftGammaGain.gamma.overrideState = true;

        // gain の RGB もニュートラルへ (w は明るさスライダーが管理するので現在値を保持)
        if (m_originalGainRgbRecorded)
        {
            var gn = m_liftGammaGain.gain.value;
            gn.x = Mathf.Lerp(m_originalGainR, 1f, warmth);
            gn.y = Mathf.Lerp(m_originalGainG, 1f, warmth);
            gn.z = Mathf.Lerp(m_originalGainB, 1f, warmth);
            // gn.w は触らない (ApplyOffset が設定した値のまま)
            m_liftGammaGain.gain.value = gn;
            m_liftGammaGain.gain.overrideState = true;
        }

        // Bloom の tint もニュートラル (白) へ。オレンジのにじみを抑える主役。
        if (m_originalBloomRecorded && m_bloom != null)
        {
            var t = m_bloom.tint.value;
            t.r = Mathf.Lerp(m_originalBloomTintR, 1f, warmth);
            t.g = Mathf.Lerp(m_originalBloomTintG, 1f, warmth);
            t.b = Mathf.Lerp(m_originalBloomTintB, 1f, warmth);
            m_bloom.tint.value = t;
            m_bloom.tint.overrideState = true;
        }

        m_liftGammaGain.active = true;
    }
}
