using BunnyGarden2FixMod.ExSave;
using BunnyGarden2FixMod.Patches.CostumeChanger.Internal;
using BunnyGarden2FixMod.Utils;
using GB.Game;
using MessagePack;
using System.Collections.Generic;

namespace BunnyGarden2FixMod.Patches.CostumeChanger;

/// <summary>
/// キャラごとのパンツ override（donor キャラ + type 0-6 + color 0-4）を保持する
/// プロセス内セッションストア。永続化は ExSave (CommonData) の <c>panties.override.all</c> キーに行う。
///
/// 制約:
///   - target == donor は許可（旧来挙動: 自キャラの type/color を選ぶ）
///   - パンツはマテリアル 1 枚の差し替えで実現されるため、donor キャラの体型差は無関係
///
/// 投入経路: Wardrobe (F7) Panties タブの apply ハンドラ
/// (<see cref="UI.CostumePickerController.ApplyPanties"/>) から <see cref="Set"/> /
/// <see cref="Clear"/> を呼ぶ。シーン遷移をまたいで保持される（pickerHost が DontDestroyOnLoad）。
/// </summary>
public static class PantiesOverrideStore
{
    private const string ExSaveKey = "panties.override.all";

    public readonly struct Entry
    {
        public Entry(CharID donorChar, int type, int color)
        {
            DonorChar = donorChar;
            Type = type;
            Color = color;
        }

        public CharID DonorChar { get; }
        public int Type { get; }
        public int Color { get; }
    }

    private static readonly Dictionary<CharID, Entry> s_overrides = new();

    /// <summary>
    /// rehydrate が例外で失敗したことを記録するフラグ。
    /// true の間は WriteToExSave を抑止し、破損データによる旧データ上書きを防ぐ。
    /// </summary>
    private static bool s_rehydrateFailed = false;

    public const int TypeCount = 7;   // A-G
    public const int ColorCount = 5;  // 0-4
    public const int TotalCount = TypeCount * ColorCount; // 35 (per donor)

    // =========================================================================
    // 新シグネチャ (donor 対応). UI/適用ロジックを donor 対応に変更後はこちらを使う。
    // =========================================================================

    /// <summary>
    /// 無効な組み合わせは false を返す（target/donor 範囲外、type/color 範囲外）。
    /// donor == target も許可。
    /// </summary>
    public static bool Set(CharID target, CharID donor, int type, int color)
    {
        bool ok = SetValidatedNoMirror(target, donor, type, color);
        if (ok) WriteToExSave();
        return ok;
    }

    public static bool TryGet(CharID target, out Entry entry) => s_overrides.TryGetValue(target, out entry);

    /// <summary>登録済み全 override を target ごとに列挙する (BottomsOverrideStore.EnumerateOverrides と対称)。</summary>
    public static IEnumerable<KeyValuePair<CharID, Entry>> EnumerateOverrides() => s_overrides;

    /// <summary>
    /// 登録済み override から (donor, type, color) のユニーク列を返す。
    /// 将来的に preload を行う場合の重複排除用。
    /// </summary>
    public static IEnumerable<Entry> EnumerateUniqueDonors()
    {
        var seen = new HashSet<(CharID, int, int)>();
        foreach (var e in s_overrides.Values)
        {
            var key = (e.DonorChar, e.Type, e.Color);
            if (seen.Add(key)) yield return e;
        }
    }

    // =========================================================================
    // 旧シグネチャ (donor なし). 既存呼出し箇所との互換 shim. donor=target で動作する.
    // 段階 B (UI 改造) 完了後に旧シグネチャは削除予定.
    // =========================================================================

    /// <summary>旧シグネチャ互換: donor = target として登録する。</summary>
    public static void Set(CharID id, int type, int color)
    {
        if (SetValidatedNoMirror(id, id, type, color))
            WriteToExSave();
    }

    /// <summary>旧シグネチャ互換: donor 情報を捨てて type/color のみ取り出す。</summary>
    public static bool TryGet(CharID id, out int type, out int color)
    {
        if (s_overrides.TryGetValue(id, out var v))
        {
            type = v.Type;
            color = v.Color;
            return true;
        }
        type = 0;
        color = 0;
        return false;
    }

    // =========================================================================
    // 共通
    // =========================================================================

    public static void Clear(CharID id)
    {
        if (s_overrides.Remove(id))
            WriteToExSave();
    }

    /// <summary>
    /// ExSave から override 状態を読み込み、s_overrides を再構築する。
    /// ExSaveStore.LoadFromPath 後に呼ばれる。
    /// </summary>
    public static void RehydrateFromExSave()
    {
        s_overrides.Clear();
        if (!OverrideStorePersistence.TryReadFromExSave<PantiesOverrideExSaveEntry>(
                ExSaveKey, "[PantiesOverrideStore]", Configs.PersistCostumeOverrides.Value,
                out var dict, out s_rehydrateFailed))
        {
            return;
        }

        int restored = 0;
        foreach (var kv in dict)
        {
            // 旧データ (DonorChar == 0 になっているもの) は target をそのまま donor とみなす互換読み出し.
            // 新規データは DonorChar フィールドを持つ.
            var target = (CharID)kv.Key;
            var donor = kv.Value.DonorChar < (byte)CharID.NUM ? (CharID)kv.Value.DonorChar : target;
            if (SetValidatedNoMirror(target, donor, kv.Value.Type, kv.Value.Color))
                restored++;
            else
                PatchLogger.LogWarning($"[PantiesOverrideStore] rehydrate skip: target={target}, donor={donor}, type={kv.Value.Type}, color={kv.Value.Color}");
        }
        PatchLogger.LogInfo($"[PantiesOverrideStore] rehydrate: {restored} 個復元");
    }

    /// <summary>in-memory の s_overrides をクリアする（Reset 時に呼ばれる）。</summary>
    public static void ClearMemory()
    {
        s_overrides.Clear();
        s_rehydrateFailed = false;
    }

    /// <summary>バリデーション後に dict へ投入する（ExSave mirror を行わない）。無効入力は false を返す。</summary>
    private static bool SetValidatedNoMirror(CharID target, CharID donor, int type, int color)
    {
        if (target >= CharID.NUM || donor >= CharID.NUM) return false;
        if (type < 0 || type >= TypeCount) return false;
        if (color < 0 || color >= ColorCount) return false;
        s_overrides[target] = new Entry(donor, type, color);
        return true;
    }

    /// <summary>s_overrides の全内容を ExSave の CommonData に書き込む。</summary>
    private static void WriteToExSave()
    {
        OverrideStorePersistence.WriteToExSave(
            ExSaveKey, "[PantiesOverrideStore]", Configs.PersistCostumeOverrides.Value,
            s_rehydrateFailed, BuildSerializableDict);
    }

    /// <summary>s_overrides を Dictionary&lt;int, PantiesOverrideExSaveEntry&gt; に変換する（MessagePack 直列化用）。</summary>
    private static Dictionary<int, PantiesOverrideExSaveEntry> BuildSerializableDict()
    {
        var dict = new Dictionary<int, PantiesOverrideExSaveEntry>(s_overrides.Count);
        foreach (var kv in s_overrides)
            dict[(int)kv.Key] = new PantiesOverrideExSaveEntry
            {
                DonorChar = (byte)kv.Value.DonorChar,
                Type = (byte)kv.Value.Type,
                Color = (byte)kv.Value.Color,
            };
        return dict;
    }
}

// MessagePack-CSharp の DynamicObjectResolver は public 型のみ解決するため public class 必須
/// <summary>パンツ override の ExSave 直列化用 POCO。</summary>
[MessagePackObject]
public class PantiesOverrideExSaveEntry
{
    [Key(0)]
    public byte Type { get; set; }

    [Key(1)]
    public byte Color { get; set; }

    /// <summary>
    /// 下着の donor キャラ. 段階 A で追加.
    /// 旧データには存在しないため MessagePack は既定値 (0 = KANA) で復元するが,
    /// RehydrateFromExSave 側で「DonorChar == 0 の場合は target を donor とみなす」互換読み出しを行う.
    /// (Note: KANA を真に donor として指定した新規エントリと旧データの区別がつかなくなるが,
    /// 旧データに対する自然な解釈は target=donor であり, 段階 B 以降は新規データのみ生成されるため
    /// 実害は段階 A の移行期間のみで終わる.)
    /// </summary>
    [Key(2)]
    public byte DonorChar { get; set; }
}