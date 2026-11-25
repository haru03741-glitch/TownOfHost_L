using AmongUs.GameOptions;
using UnityEngine;

namespace TownOfHostL;

/// <summary>
/// 今のマップの入口。マップで分ける処理はここを通す。
/// 反転スケルド (dlekS・MapId 3) は本編がスケルドの船の横の倍率を負にしただけの物なので、
/// 部屋・装置・サボタージュの分岐は <see cref="Base"/> (反転スケルドならスケルド) で引き、
/// スケルドで測った世界の座標は <see cref="Fix(Vector2)"/> で x を鏡に映して使う。
/// 船の子 (ベント・部屋・ドア・装置) から読んだ値はもともと反転しているので通さない。
/// </summary>
public static class GameMap
{
    /// <summary>今のマップ。本編のかくれんぼの間は、かくれんぼの設定のマップを読む</summary>
    public static MapNames Current
    {
        get
        {
            var gm = GameOptionsManager.Instance;
            if (gm == null) return MapNames.Skeld;
            if (gm.currentGameMode == GameModes.HideNSeek)
            {
                var hns = gm.currentHideNSeekGameOptions;
                if (hns == null) return MapNames.Skeld;
                var hmap = (MapNames)hns.MapId;
                return hmap == MapNames.Skeld && DleksMap.Pending ? MapNames.Dleks : hmap;
            }
            var normal = gm.currentNormalGameOptions;
            if (normal == null) return MapNames.Skeld;
            var map = (MapNames)normal.MapId;
            // ロビーの間、反転スケルドは MapId 0 と印で持つ (DleksMap)
            return map == MapNames.Skeld && DleksMap.Pending ? MapNames.Dleks : map;
        }
    }

    /// <summary>形の上のマップ。反転スケルドはスケルドとして返す</summary>
    public static MapNames Base => ToBase(Current);

    public static MapNames ToBase(MapNames map) => map == MapNames.Dleks ? MapNames.Skeld : map;

    /// <summary>今のマップが左右に反転している (反転スケルド)</summary>
    public static bool IsMirrored => Current == MapNames.Dleks;

    /// <summary>スケルドか反転スケルド</summary>
    public static bool IsSkeld => Base == MapNames.Skeld;

    public static bool IsAirship => Current == MapNames.Airship;

    /// <summary>反転前の船で測った世界の座標を、今のマップの座標にする</summary>
    public static Vector2 Fix(Vector2 pos) => IsMirrored ? new Vector2(-pos.x, pos.y) : pos;

    public static Vector3 Fix(Vector3 pos) => IsMirrored ? new Vector3(-pos.x, pos.y, pos.z) : pos;

    /// <summary>反転前の船で測った x を、今のマップの x にする (範囲の判定などに)</summary>
    public static float FixX(float x) => IsMirrored ? -x : x;
}
