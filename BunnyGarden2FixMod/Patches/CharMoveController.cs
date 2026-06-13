using BunnyGarden2FixMod.Utils;
using GB;
using GB.Game;
using GB.Scene;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BunnyGarden2FixMod.Patches;

/// <summary>
/// お試し: 表示中の最初のキャラの位置をずらして、動くか・ロジックに戻されないかを検証する。
/// Insert: 最初のキャラを右に 0.1 ずらす (押すたび加算)
/// Delete: 元の位置に戻す
/// 検証用。成立したら本機能 (6人を XYZ で動かす UI) に発展させる。
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

    private GameObject m_targetChar;
    private Vector3 m_originalPos;
    private bool m_recorded;
    private Vector3 m_offset = Vector3.zero;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // Insert: 最初の表示キャラを右にずらす
        if (kb[Key.Insert].wasPressedThisFrame)
        {
            EnsureTarget();
            if (m_targetChar != null)
            {
                m_offset += new Vector3(0.1f, 0f, 0f);
                PatchLogger.LogInfo($"[CharMove] offset={m_offset}, target={m_targetChar.name}");
            }
            else
            {
                PatchLogger.LogInfo($"[CharMove] target not found");
            }
        }

        // Delete: 元に戻す
        if (kb[Key.Delete].wasPressedThisFrame)
        {
            m_offset = Vector3.zero;
            PatchLogger.LogInfo($"[CharMove] reset offset");
        }

        // 毎フレーム、元位置 + オフセットを適用 (ロジックに戻されるか検証するため毎フレーム上書き)
        if (m_recorded && m_targetChar != null)
        {
            m_targetChar.transform.position = m_originalPos + m_offset;
        }
    }

    /// <summary>表示中の最初のキャラを探して保持する。</summary>
    private void EnsureTarget()
    {
        var sys = GBSystem.Instance;
        if (sys == null || !sys.IsIngame) return;
        var env = sys.GetActiveEnvScene();
        if (env == null) return;

        for (int i = (int)CharID.KANA; i < (int)CharID.NUM; i++)
        {
            var id = (CharID)i;
            if (env.FindCharacterIndex(id) < 0) continue;
            var charObj = env.FindCharacter(id);
            if (charObj == null || !charObj.activeInHierarchy) continue;

            // 最初に見つかった表示キャラを対象にする
            m_targetChar = charObj;
            m_originalPos = charObj.transform.position;
            m_recorded = true;
            PatchLogger.LogInfo($"[CharMove] target set: id={id}, name={charObj.name}, pos={m_originalPos}");
            return;
        }
    }
}
