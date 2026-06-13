using BunnyGarden2FixMod.Utils;
using System.Text;
using UnityEngine;
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
