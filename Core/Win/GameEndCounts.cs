using System;

namespace TownOfHostL.Core.Win;

/// <summary>
/// 人数による終了判定で使う、生きている人の陣営 (CountTypes) ごとの数。
/// 役職の補正 (CustomRoleManager.GameEndCountAdjustersOthers) が数を動かす。毎フレーム使い回すので割り当てない。
/// </summary>
public sealed class GameEndCounts
{
    private static readonly int Length = (int)Max() + 1;
    private readonly int[] alive = new int[Length];

    private static CountTypes Max()
    {
        var max = default(CountTypes);
        foreach (CountTypes type in Enum.GetValues(typeof(CountTypes)))
            if (type > max) max = type;
        return max;
    }

    public int this[CountTypes type]
    {
        get => alive[(int)type];
        set => alive[(int)type] = value;
    }

    /// <summary>クルー側として数える人 (妖狐もクルーとして数える)</summary>
    public int CrewSide => this[CountTypes.Crew] + this[CountTypes.Fox];

    /// <summary>キルする側として数える人 (キル陣営と、独自の陣営 (パブロフ・スタンド・イーターなど) の合計)</summary>
    public int KillerSide
    {
        get
        {
            var sum = 0;
            for (var i = 0; i < alive.Length; i++)
                if (IsKillerSide((CountTypes)i)) sum += alive[i];
            return sum;
        }
    }

    /// <summary>クルー・妖狐・数えない人以外の陣営は、生きている間クルーの勝ちを止める</summary>
    public static bool IsKillerSide(CountTypes type)
        => type is not (CountTypes.OutOfGame or CountTypes.None or CountTypes.Crew or CountTypes.Fox);

    internal void Clear() => Array.Clear(alive, 0, alive.Length);
}
