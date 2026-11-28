using System.Linq;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHostL.Roles.Impostor;
public sealed class Chaser : RoleBase, IImpostor, ISidekickable
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Chaser),
            player => new Chaser(player),
            CustomRoles.Chaser,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 1800,
            SetUpOptionItem,
            "チェイサー"
        );
    public Chaser(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        ChaseCooldown = OptionChaseCooldown.GetFloat();
        ChaseMaxCount = OptionChaseMaxCount.GetInt();
        ReturnPosition = OptionReturnPosition.GetBool();
        PositionTime = OptionPositionTime.GetFloat();
    }
    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionChaseCooldown;
    private static OptionItem OptionChaseMaxCount;
    private static OptionItem OptionReturnPosition;
    private static OptionItem OptionPositionTime;
    private static float KillCooldown;
    private static float ChaseCooldown;
    private static int ChaseMaxCount;
    public static bool ReturnPosition;
    private static float PositionTime;

    private Vector2 lastTransformPosition;
    // 戻りの予約の世代。戻る前に続けて追った時は、最後の予約だけが戻す
    private int returnGeneration;
    private bool returnPending;
    private int chaseLimitCount;
    enum OptionName
    {
        ChaserChaseCooldown,
        ChaserChaseMaxCount,
        ChaserReturnPosition,
        ChaserPositionTime,
    }
    private static void SetUpOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 30f, false)
                .SetValueFormat(OptionFormat.Seconds);
        OptionChaseCooldown = FloatOptionItem.Create(RoleInfo, OptionName.ChaserChaseCooldown, new(1f, 180f, 1f), 30f, false)
                .SetValueFormat(OptionFormat.Seconds);
        OptionChaseMaxCount = IntegerOptionItem.Create(RoleInfo, OptionName.ChaserChaseMaxCount, new(1, 50, 1), 3, false)
                .SetValueFormat(OptionFormat.Pieces);
        OptionReturnPosition = BooleanOptionItem.Create(RoleInfo, OptionName.ChaserReturnPosition, false, false);
        OptionPositionTime = FloatOptionItem.Create(RoleInfo, OptionName.ChaserPositionTime, new(1f, 99f, 1f), 10f, false, OptionReturnPosition)
            .SetValueFormat(OptionFormat.Seconds);
    }
    public override void Add()
    {
        chaseLimitCount = ChaseMaxCount;
    }

    public float CalculateKillCooldown() => KillCooldown;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = ChaseCooldown;
    }
    public override void AfterMeetingTasks()
    {
        if (Player.IsAlive()) Player.RpcResetAbilityCooldown();
    }

    public override bool OnCheckShapeshift(PlayerControl target, ref bool animate)
    {
        // 変身アニメーションを起こさない
        animate = false;

        // 回数制限,自身以外の変身かどうか
        var shapeshifting = !Is(target);
        if (chaseLimitCount <= 0 || !shapeshifting) return false;

        // 変身前の位置を記録する (戻る前に続けて追った時は、最初の位置へ戻すので上書きしない)
        if (!ReturnPosition || !returnPending) lastTransformPosition = Player.transform.position;
        // ベントの位置へ飛ばす。
        Player.MyPhysics.RpcExitVent(GetNearestVentId(target));
        // 変身クールダウンのリセット
        Player.RpcResetAbilityCooldown();

        // 回数消化
        chaseLimitCount--;
        Logger.Info($"{Player.GetNameWithRole()} : 残り{chaseLimitCount}発", "Chaser");
        Utils.NotifyRoles(SpecifySeer: Player);

        // 以下、元の場所に戻れる時の処理
        if (!ReturnPosition) return false;

        // 元の位置に戻る時はぬーんを使わせない。
        MyState.CanUseMovingPlatform = false;
        returnPending = true;
        int generation = ++returnGeneration;

        // 戻る時間後の処理
        // 戻りの後始末 (ぬーんを使えるように戻す) なので役職が外れても走らせる
        _ = new LateTask(() =>
        {
            // 後から追った分の予約が戻すので、古い予約は何もしない
            if (generation != returnGeneration) return;
            returnPending = false;
            // 戻れなかった時も、ぬーんは使えるように戻す
            if (Player == null || !Player.IsAlive() || !GameStates.IsInTask)
            {
                MyState.CanUseMovingPlatform = true;
                lastTransformPosition = default;
                return;
            }
            // もし梯子を使っている場合
            if (Player.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
            {
                Logger.Info($"梯子を使っていたため元の位置に戻れませんでした。", "Chaser");
                MyState.CanUseMovingPlatform = true;
                return;
            }

            // 元居た場所に近いベントの位置にプレイヤーを戻す
            Player.MyPhysics.RpcExitVent(GetReturnNearestVentId());
            // 位置をリセットする
            lastTransformPosition = default;
            // 元通りぬーんを使えるようにする
            MyState.CanUseMovingPlatform = true;

        }, PositionTime, "ReturnPosition");

        return false;
    }
    // ターゲットから一番近いベントを探す
    int GetNearestVentId(PlayerControl target)
    {
        var vents = ShipStatus.Instance.AllVents.OrderBy(v => (target.transform.position - v.transform.position).magnitude);
        return vents.First().Id;
    }
    // 戻る時の一番近いベントを探す
    int GetReturnNearestVentId()
    {
        var vents = ShipStatus.Instance.AllVents.OrderBy(v => (lastTransformPosition - (Vector2)v.transform.position).magnitude);
        return vents.First().Id;
    }

    public override int? UsesRemaining => chaseLimitCount;
    public override string GetProgressText(bool comms = false) => Utils.ColorString(chaseLimitCount > 0 ? RoleInfo.RoleColor : Color.gray, $"[{chaseLimitCount}]");
    public override string GetAbilityButtonText() => Translator.GetString("ChaserChase");
}