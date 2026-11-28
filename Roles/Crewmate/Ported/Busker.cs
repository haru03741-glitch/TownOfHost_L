using AmongUs.GameOptions;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Crewmate;

// バスカー: ベント (設定でペットにもできる) で、決めた時間だけ幽霊になる。時間が来るか会議が始まると元に戻る。タスクで使える回数が増える。
// 役職の元: Endless Host Roles のアストラル (案は SuperNewRoles のバスカー)
public sealed class Busker : RoleBase, IUsePetButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Busker),
            player => new Busker(player),
            CustomRoles.Busker,
            () => OptionUsePet?.GetBool() == true ? RoleTypes.Crewmate : RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewPorted + 0,
            SetupOptionItem,
            "bk",
            "#ffac75",
            introSound: () => GetIntroSound(RoleTypes.Crewmate)
        );

    public Busker(PlayerControl player)
    : base(RoleInfo, player)
    {
        cooldown = OptionCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
        tasksPerUse = OptionTasksPerUse.GetInt();
        uses = OptionUseCount.GetInt();
        if (OptionUsePet.GetBool()) pet = new BuskerPet(cooldown, TryBecomeGhost);
    }

    private static OptionItem OptionCooldown;
    private static OptionItem OptionUsePet;
    private static OptionItem OptionDuration;
    private static OptionItem OptionUseCount;
    private static OptionItem OptionTasksPerUse;
    /// <summary>バスカー系 (イビルバスカー・ジャッカルバスカーも) の設定の名前</summary>
    public enum OptionName
    {
        BuskerGhostDuration,
        BuskerUseCount,
        BuskerTasksPerUse,
        BuskerKillsPerUse,
        BuskerUsePet,
    }
    private static float cooldown;
    private static float duration;
    private static int tasksPerUse;

    private int uses;
    private readonly BuskerPet pet;
    // ベントから押し出してから幽霊にするまでの間 (ベントの中で役職を替えると出られなくなる)。
    // 押し出された客がベントから出ても、ホストの手元の inVent は立ったまま残るので、出たかどうかは見ずに時間で待つ
    private const float AfterBootDelay = 1.5f;

    private static void SetupOptionItem()
    {
        CreateGhostOptions(RoleInfo, out OptionCooldown, out OptionDuration, out OptionUseCount, out OptionUsePet, 1);
        OptionTasksPerUse = IntegerOptionItem.Create(RoleInfo, OptionName.BuskerTasksPerUse, new(0, 10, 1), 3, false)
            .SetValueFormat(OptionFormat.Pieces);
    }

    /// <summary>バスカー系で共通の設定 (能力のクール・幽霊でいる時間・使える回数・ペットで発動するか)</summary>
    public static void CreateGhostOptions(SimpleRoleInfo info, out OptionItem cooldown, out OptionItem duration, out OptionItem useCount, out OptionItem usePet, int defaultUses)
    {
        cooldown = FloatOptionItem.Create(info, GeneralOption.Cooldown, new(0f, 120f, 2.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        duration = FloatOptionItem.Create(info, OptionName.BuskerGhostDuration, new(1f, 30f, 1f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
        useCount = IntegerOptionItem.Create(info, OptionName.BuskerUseCount, new(0, 20, 1), defaultUses, false)
            .SetValueFormat(OptionFormat.Times);
        usePet = BooleanOptionItem.Create(info, OptionName.BuskerUsePet, false, false);
    }

    /// <summary>本人の名前の下に出す「あと何秒で戻るか」</summary>
    public static string CountdownText(PlayerControl owner, PlayerControl seer, PlayerControl seen, bool isForMeeting)
    {
        seen ??= seer;
        if (isForMeeting || seer != owner || seen != owner || !TempGhost.IsGhost(owner)) return "";
        return string.Format(GetString("BuskerCountdown"), TempGhost.RemainingSeconds(owner.PlayerId));
    }

    public override void Add() => pet?.Restart();
    bool IUsePetButton.UsesPet => pet != null;
    void IUsePetButton.OnPet() => pet?.OnPet();
    public override void AfterMeetingTasks() => pet?.Restart();

    private bool TryBecomeGhost(System.Action<PlayerControl> onReturned)
    {
        if (uses <= 0 || !TempGhost.Begin(Player, duration, onReturned)) return false;
        uses--;
        return true;
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = uses > 0 ? cooldown : 255f;
        AURoleOptions.EngineerInVentMaxTime = 1f;
        AURoleOptions.GuardianAngelCooldown = TempGhost.LockedGuardianAngelCooldown;
    }

    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (pet != null || uses <= 0 || !TempGhost.CanBeginIgnoringVent(Player)) return true;
        physics.RpcBootFromVent(ventId);
        _ = After(() =>
        {
            if (uses <= 0 || !TempGhost.Begin(Player, duration, ignoreVent: true)) return;
            uses--;
            Player.MarkDirtySettings();
        }, AfterBootDelay, "Busker_BecomeGhost");
        return false;
    }

    public override bool OnCompleteTask()
    {
        if (tasksPerUse > 0 && Player.IsAlive() && MyTaskState.CompletedTasksCount % tasksPerUse == 0)
        {
            uses++;
            // 0 回の間はベントの待ち時間を 255 秒にしてあり、客の手元ではそれを数え続けている。
            // 使えるようになった時は、元の待ち時間の設定が届いてから数え直す
            if (uses == 1) Player.RpcResetAbilityCooldown(sync: true);
            else Player.MarkDirtySettings();
        }
        return true;
    }

    public override int? UsesRemaining => pet == null ? uses : null;
    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(uses > 0 ? RoleInfo.RoleColor : UnityEngine.Color.gray, $"[{uses}]");
    public override string GetAbilityButtonText() => GetString("BuskerGhostButtonText");
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
        => CountdownText(Player, seer, seen, isForMeeting);
}
