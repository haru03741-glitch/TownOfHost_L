using TownOfHostL.Modules;

namespace TownOfHostL;

/// <summary>
/// hamo の役職は速度を「今の速度を控えて書き換え、後で控えた値へ戻す」形で扱う。
/// TOH_L の PlayerSpeed (役職ごとの修飾を重ねる形) の上で、その書き方をそのまま使えるようにする。
/// 書き込みは "Hamo" の修飾 1 つにまとめ、hamo なしの速度と同じ値へ戻した時は修飾を外す。
/// </summary>
public sealed class HamoSpeedTable
{
    public static readonly HamoSpeedTable Instance = new();
    const string Key = "Hamo";

    public float this[byte playerId]
    {
        get => PlayerSpeed.Get(playerId);
        set
        {
            PlayerSpeed.Remove(playerId, Key);
            if (System.Math.Abs(PlayerSpeed.Get(playerId) - value) < 0.0001f) return;
            if (value <= Main.MinSpeed) PlayerSpeed.Freeze(playerId, Key);
            else PlayerSpeed.Override(playerId, Key, value);
        }
    }

    public bool TryGetValue(byte playerId, out float speed)
    {
        speed = PlayerSpeed.Get(playerId);
        return true;
    }
}
