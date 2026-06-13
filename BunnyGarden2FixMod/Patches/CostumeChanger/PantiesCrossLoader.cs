using BunnyGarden2FixMod.Utils;
using Cysharp.Threading.Tasks;
using GB.Game;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BunnyGarden2FixMod.Patches.CostumeChanger;

/// <summary>
/// 別キャラの下着マテリアルを直接ロードして target の mesh_skin_lower に貼るための
/// シンプルなヘルパー。Bottoms/Tops と異なり SMR 操作・ボーン処理は不要で、
/// マテリアル 1 枚の差し替えだけで済む。
///
/// 設計:
///   - パス組み立ては GB.Game.CharacterHandle.PANTIES_FILE_PATH の再実装。
///   - ロードは Addressables.LoadAssetAsync&lt;Material&gt; を UniTask 化。
///   - Material は session 内でキャッシュ。
///   - スロット検出は CharacterHandle.findPantiesMaterialIndex の再実装。
///     正規表現は通常衣装 (m_panties_a_00_kana 形式) と水着 (m_panties_skin_swimwear_kana) の
///     両方を検出する。
///   - スナップショット: cross 適用時に target SMR の元マテリアル参照を保存しておき、
///     解除時にそれを書き戻す。これで水着の「何もつけてない」状態など、ロード直後の
///     見た目を正しく復元できる。
/// </summary>
internal static class PantiesCrossLoader
{
    private static readonly string[] MODEL_FILE_PATH_ARRAY = new[]
    {
        "PC01_Kana", "PC02_Rin", "PC03_Miuka", "PC04_Erisa", "PC05_Kuon", "PC06_Luna"
    };
    private static readonly string[] MATERIAL_SUFFIX_ARRAY = new[]
    {
        "kana", "rin", "miuka", "erisa", "kuon", "luna"
    };

    private static readonly Dictionary<string, Material> s_cache = new();

    // 通常 (m_panties_a_00_kana) と水着 (m_panties_skin_swimwear_kana) の両方を検出。
    private static readonly Regex s_pantiesRegex = new(
        "m_panties_(?:[a-g]_[0-9]+_|skin_swimwear)", RegexOptions.Compiled);
    private static readonly Regex s_sensitiveRegex = new(
        "m_panties_sensitive", RegexOptions.Compiled);

    /// <summary>
    /// target ごとに「cross 適用前の元マテリアル」を 1 件だけ保持する。
    /// 同じ target で複数回 cross 適用しても最初の 1 回だけ snapshot を捕獲し、解除時に戻す。
    /// </summary>
    private static readonly Dictionary<CharID, Material> s_snapshots = new();

    public static string BuildPath(CharID donor, int type, int color)
    {
        int idx = (int)donor;
        if (idx < 0 || idx >= MODEL_FILE_PATH_ARRAY.Length) return null;
        string modelPath = MODEL_FILE_PATH_ARRAY[idx];
        string fileName = string.Format(
            "m_panties_{0}_{1:00}_{2}.mat",
            (char)('a' + type), color, MATERIAL_SUFFIX_ARRAY[idx]);
        return $"Character/{modelPath}/00_Common/Materials/panties/{fileName}";
    }

    public static async UniTask<Material> LoadMaterialAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (s_cache.TryGetValue(path, out var cached) && cached != null)
            return cached;

        var handle = Addressables.LoadAssetAsync<Material>(path);
        await handle.ToUniTask();
        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            PatchLogger.LogWarning($"[PantiesCrossLoader] Addressables ロード失敗: path={path}, status={handle.Status}");
            return null;
        }
        var mat = handle.Result;
        if (mat != null) s_cache[path] = mat;
        return mat;
    }

    public static int FindPantiesMaterialIndex(Material[] mats)
    {
        if (mats == null) return -1;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null && s_pantiesRegex.IsMatch(mats[i].name))
                return i;
        }
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null && s_sensitiveRegex.IsMatch(mats[i].name))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// target の現在の下着スロットマテリアルをスナップショット保存する。
    /// 既に保存済みなら何もしない (最初の cross 適用前の状態を保持)。
    /// 同キャラ経路 (env.ReloadPanties) でこの関数を経由せず差し替えても、その後 cross 適用すると
    /// 「そのとき貼られている下着」をスナップショットとして取ってしまう点に注意。
    /// この MOD の用途では「ロード直後の素状態」または「同キャラ経路の最新状態」のどちらかが
    /// 取れていれば実害は無い (解除時に「想定された下着状態」に戻る)。
    /// </summary>
    public static void CaptureSnapshotIfFirst(CharID target, Material[] mats, int idx)
    {
        if (s_snapshots.ContainsKey(target)) return;
        if (mats == null || idx < 0 || idx >= mats.Length) return;
        s_snapshots[target] = mats[idx];
        PatchLogger.LogDebug($"[PantiesCrossLoader] snapshot 捕獲: target={target}, mat={mats[idx]?.name ?? "<null>"}");
    }

    /// <summary>
    /// target のスナップショットを書き戻す。スナップショット未保存なら false を返す
    /// (呼出し側で env.ReloadPanties 経由の fallback を試みる)。
    /// </summary>
    public static bool TryRestoreSnapshot(CharID target, GameObject charObj)
    {
        if (!s_snapshots.TryGetValue(target, out var origMat)) return false;
        if (charObj == null)
        {
            s_snapshots.Remove(target);
            return false;
        }
        var smr = charObj.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(x => x != null && x.name == "mesh_skin_lower");
        if (smr == null)
        {
            // SMR が消えていたら snapshot も無効化
            s_snapshots.Remove(target);
            return false;
        }
        var mats = smr.materials;
        int idx = FindPantiesMaterialIndex(mats);
        if (idx < 0)
        {
            // 戻すスロットが見つからない (cross で貼ったマテリアル名が変わって検出不可)
            // この場合は本体経路に任せる
            s_snapshots.Remove(target);
            return false;
        }
        mats[idx] = origMat;
        smr.materials = mats;
        s_snapshots.Remove(target);
        PatchLogger.LogInfo($"[PantiesCrossLoader] snapshot 復元: target={target}, mat={origMat?.name ?? "<null>"}");
        return true;
    }

    /// <summary>
    /// target のスナップショットを破棄する (本体経路で正しく上書きされた場合など)。
    /// </summary>
    public static void DropSnapshot(CharID target)
    {
        s_snapshots.Remove(target);
    }
}
