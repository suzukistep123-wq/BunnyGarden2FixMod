using BunnyGarden2FixMod.Patches.CostumeChanger.UI;
using BunnyGarden2FixMod.Utils;
using GB;
using GB.Game;
using GB.Scene;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BunnyGarden2FixMod.Patches;

/// <summary>
/// F7 (ウォードローブ) で選択中のキャラの位置を、キー操作で前後左右上下に動かす。
/// J/L=左右, I/K=奥手前, U/O=上下, P=リセット。
///
/// 対象は CostumePickerController.ActiveChar (F7 で選択中のキャラ) に追従する。
/// 各キャラのオフセットは個別に記憶する (6人を別々に配置できる)。
///
/// 移動の基準ベクトルは「初期画面のカメラ向き (水平成分)」を初回に固定して使う。
/// 以降カメラ (F5 等) が動いても移動方向は変わらない。
///
/// 毎フレーム「原位置 + オフセット」を適用し、ゲームロジックに位置を戻されないようにする。
/// </summary>
public class CharMoveController : MonoBehaviour
{
    public static CharMoveController Instance { get; private set; }

    public static void Initialize(GameObject parent)
    {
        if (Instance != null) return;
        var host = new GameObject("BG2CharMoveController");
        DontDestroyOnLoad(host);
        Instance = host.AddComponent<CharMoveController>();
        PatchLogger.LogInfo("[CharMove] Initialized");
    }

    // キャラごとのオフセット (移動量)。キャラを切り替えても各自の位置を保持。
    private readonly Dictionary<CharID, Vector3> m_offsets = new();
    // キャラごとの原位置 (初回捕捉時に記録)。
    private readonly Dictionary<CharID, Vector3> m_originalPos = new();

    // 移動基準ベクトル (初期画面の向き、初回に固定)。
    private Vector3 m_axisRight = Vector3.right;
    private Vector3 m_axisForward = Vector3.forward;
    private bool m_axisFixed;

    // バー空間がワールド軸に対して傾いているぶんの補正角度 (度)。
    // I(奥) が左奥にずれる = 基準が左に傾いている → 時計回りに回して補正。
    // ずれが残る場合はこの値を調整する (符号を変えると逆回転)。
    private const float AXIS_CORRECTION_DEG = 30f;

    // 押しっぱなし時の移動速度 (1 秒あたりの移動量)。大きいほど速い。
    private const float SPEED = 0.5f;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // 移動対象 = F7 で選択中のキャラ
        var picker = CostumePickerController.Instance;
        if (picker == null) return;
        CharID target = picker.ActiveChar;
        if (target >= CharID.NUM) return;

        // 対象キャラの GameObject を取得
        var sys = GBSystem.Instance;
        if (sys == null || !sys.IsIngame) return;
        var env = sys.GetActiveEnvScene();
        if (env == null) return;
        var charObj = env.FindCharacter(target);
        if (charObj == null || !charObj.activeInHierarchy) return;

        // 基準ベクトルを初回に固定する。
        // ワールド軸 (X=右, Z=奥) を基準に、バー空間の傾きぶん回転補正する。
        // Camera.main は BG2 では null のことがあるため使わない (確実に効かせる)。
        if (!m_axisFixed)
        {
            var rot = Quaternion.AngleAxis(AXIS_CORRECTION_DEG, Vector3.up);
            m_axisForward = rot * Vector3.forward;
            m_axisRight = rot * Vector3.right;
            m_axisFixed = true;
            PatchLogger.LogInfo($"[CharMove] axis fixed (corrected {AXIS_CORRECTION_DEG}deg): fwd={m_axisForward}, right={m_axisRight}");
        }

        // 原位置を初回記録
        if (!m_originalPos.ContainsKey(target))
        {
            m_originalPos[target] = charObj.transform.position;
        }
        if (!m_offsets.ContainsKey(target))
        {
            m_offsets[target] = Vector3.zero;
        }

        // キー入力でオフセット更新。
        // 移動キーは押しっぱなし対応 (isPressed + deltaTime で一定速度)。
        // リセット (@) は瞬間 (wasPressedThisFrame)。
        var off = m_offsets[target];
        bool moved = false;
        float d = SPEED * Time.unscaledDeltaTime; // このフレームの移動量

        if (kb[Key.J].isPressed) { off += m_axisRight * d; moved = true; }
        if (kb[Key.L].isPressed) { off += -m_axisRight * d; moved = true; }
        if (kb[Key.I].isPressed) { off += -m_axisForward * d; moved = true; }
        if (kb[Key.K].isPressed) { off += m_axisForward * d; moved = true; }
        if (kb[Key.U].isPressed) { off += Vector3.up * d; moved = true; }
        if (kb[Key.O].isPressed) { off += Vector3.down * d; moved = true; }
        if (kb[Key.P].wasPressedThisFrame)
        {
            off = Vector3.zero; moved = true;
            PatchLogger.LogInfo($"[CharMove] reset offset for {target}");
        }
        if (moved)
        {
            m_offsets[target] = off;
        }

        // 原位置 + オフセットを適用
        charObj.transform.position = m_originalPos[target] + m_offsets[target];
    }
}
