using BunnyGarden2FixMod.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BunnyGarden2FixMod.Patches;

/// <summary>
/// バーシーンの「カウンター周辺の小物」と「背後の棚」を一括で非表示にするトグル機能。
/// F1 キーでトグル。キャラを見やすくする用途。
///
/// 対象パス: HoleSceneProxy/HoleScene/CubemapMakeOffUseOn/ENV
///   - NearProps (カウンター本体、カウンタ上の小物、棚のボトル、椅子、ライト群)
///   - Shelf (背後の棚本体)
///
/// 注意: 物理的に消えるわけではなく Unity の SetActive(false) で見えなくしているだけ。
/// 内部のロジックは普通に動き続ける。
///
/// シーン遷移対応: シーン切替で GameObject 参照が無効になる可能性があるため、
/// SceneManager.sceneLoaded で再解決し、トグル状態を新シーンにも適用する。
/// </summary>
internal static class EnvObjectToggle
{
    /// <summary>
    /// 対象 GameObject の path リスト。HoleSceneProxy ルートからの相対パス。
    /// </summary>
    private static readonly string[] s_targetPaths = new[]
    {
        "HoleScene/CubemapMakeOffUseOn/ENV/NearProps",
        "HoleScene/CubemapMakeOffUseOn/ENV/Shelf",
        "HoleScene/Mob",  // 背後のモブキャラ 2 人 (椅子と一緒に消す)
    };

    private static bool s_hidden = false;
    private static readonly List<GameObject> s_cached = new();
    private static bool s_subscribed = false;

    /// <summary>F1 押下時に呼ぶ。現在の状態を反転して両 GameObject に SetActive を適用。</summary>
    public static void Toggle()
    {
        EnsureSubscribed();
        s_hidden = !s_hidden;
        ApplyToActiveScene();
        PatchLogger.LogInfo($"[EnvToggle] バー環境表示: {(s_hidden ? "OFF (非表示)" : "ON (表示)")}");
    }

    /// <summary>現在のトグル状態を全 active シーンに対して再適用する。</summary>
    private static void ApplyToActiveScene()
    {
        s_cached.Clear();
        // 全 active シーンの全 root から HoleSceneProxy を探す。
        // HoleScene は additive ロードされる可能性があるため、特定シーンに固定しない。
        int sceneCount = SceneManager.sceneCount;
        for (int i = 0; i < sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                if (root.name == "HoleSceneProxy")
                {
                    ResolveAndApplyUnder(root.transform);
                }
            }
        }
        if (s_cached.Count == 0)
            PatchLogger.LogWarning("[EnvToggle] 対象 GameObject が見つかりませんでした (HoleSceneProxy がロード中?)");
    }

    private static void ResolveAndApplyUnder(Transform root)
    {
        foreach (var path in s_targetPaths)
        {
            var t = root.Find(path);
            if (t == null)
            {
                PatchLogger.LogDebug($"[EnvToggle] パス未解決: {root.name}/{path}");
                continue;
            }
            t.gameObject.SetActive(!s_hidden);
            s_cached.Add(t.gameObject);
        }
    }

    /// <summary>SceneManager.sceneLoaded を購読し、新シーンに対しても現在のトグル状態を適用する。</summary>
    private static void EnsureSubscribed()
    {
        if (s_subscribed) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        s_subscribed = true;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 新シーンで HoleSceneProxy が存在するか確認、あればトグル状態を適用する。
        // 起動直後など s_hidden=false のときは、SetActive(true) を強制適用するだけなので no-op に近い。
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null) continue;
            if (root.name == "HoleSceneProxy")
            {
                ResolveAndApplyUnder(root.transform);
                if (s_hidden)
                    PatchLogger.LogDebug($"[EnvToggle] 新シーン '{scene.name}' に hidden 状態を再適用");
                return;
            }
        }
    }
}
