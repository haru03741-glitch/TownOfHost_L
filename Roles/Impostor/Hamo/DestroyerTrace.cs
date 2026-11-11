using UnityEngine;

using TownOfHostL.Modules;

namespace TownOfHostL.Roles.Impostor;

// デストロイヤーがつぶした場所に残す痕跡。CNO の名前の絵で、血の飛び散りと印を出す
// (体は暗い赤で絵の下に重なる)。MOD を入れている人には床の血だまりを描いて、CNO は隠す
public sealed class DestroyerTrace
{
    private const byte BodyColor = 12; // マルーン (血の色)
    private readonly TownOfHostL.Core.CustomNetObject cno;
    private int _turnsLeft;

    public bool IsAlive { get; private set; } = true;

    public DestroyerTrace(Vector2 spawnPos, int turns)
    {
        _turnsLeft = turns;
        var sprite = "<size=420%><color=#5c0000>●</color></size>\n<size=60%><color=#b00000>" + Translator.GetString("DestroyerTraceMark") + "</color></size>";
        cno = TownOfHostL.Core.CustomNetObject.Create(sprite, spawnPos, BodyColor);
        if (cno == null) { IsAlive = false; return; }
        cno.Spawned = c => ClientEffects.BloodStain(c.NetId, spawnPos);
        Logger.Info($"DestroyerTrace: position={spawnPos} turns={_turnsLeft}", "Destroyer");
    }

    // 1 ターン経過。消えたら true
    public bool PassTurn()
    {
        if (!IsAlive) return true;
        // 出す途中で CNO が消えた (例外など) 時は、痕跡も消えた扱いにする
        if (cno.IsGone) { IsAlive = false; return true; }
        if (--_turnsLeft > 0) return false;
        Remove();
        return true;
    }

    public void Remove()
    {
        if (!IsAlive) return;
        IsAlive = false;
        cno?.Despawn();
    }
}
