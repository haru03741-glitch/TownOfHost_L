using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

// 人形使い: 変身のボタンで選んだ人を、決めた時間だけ操る。操っている間、相手は人形使いが歩いたのと同じ向き・同じ距離を歩く
// (本人の操作は効かない)。歩かせる仕組みは PlayerWalker。壁や机があれば相手はその手前で止まる。
public sealed class Dollmaster : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Dollmaster),
            player => new Dollmaster(player),
            CustomRoles.Dollmaster,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpL + 300,
            SetupOptionItem,
            "dlm"
        );

    public Dollmaster(PlayerControl player)
    : base(RoleInfo, player)
    {
        cooldown = OptionCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
    }

    private static OptionItem OptionCooldown;
    private static OptionItem OptionDuration;
    private static float cooldown;
    private static float duration;

    private enum OptionName
    {
        DollmasterDuration,
    }

    // 行き先を出し直す間隔 (PlayerWalker の送る間隔の下限と同じ)
    private const float StepInterval = 0.25f;
    // 人形使いがこれより動いたら、相手の行き先を出し直す
    private const float ResendDistance = 0.3f;
    // 1 回の見直しでこれより動いたら、歩きではなく飛び (ベント・テレポート) として相手には写さない
    private const float JumpDistance = 2f;

    private PlayerControl puppet;
    private Vector2 masterOrigin, puppetOrigin, lastMasterPos, lastTarget;
    private float endAt, nextStepAt;
    private int shownSeconds;
    private float failNoticeUntil;

    private static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, OptionName.DollmasterDuration, new(2f, 20f, 1f), 6f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void Add() => CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);

    public override void OnDestroy()
    {
        CustomRoleManager.LowerOthers.Remove(GetLowerTextOthers);
        // 試合の終わりの後始末では送らない (相手の歩きは次の試合に持ち越されず、相手が先に壊れていることもある)
        Release(send: GameStates.IsInTask && !GameEndChecker.EndingStarted);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = cooldown;
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    public override string GetAbilityButtonText() => GetString("DollmasterButtonText");

    public override bool OnCheckShapeshift(PlayerControl target, ref bool animate)
    {
        animate = false;
        if (!Player.IsAlive() || puppet != null || Is(target) || !target.IsAlive()) return false;
        // ほかの人形使いが操っている人は選べない (後から終わった方の止めが、もう片方の歩きまで止めるため)
        if ((PlayerWalker.IsWalking(target.PlayerId) ? "walking" : PlayerWalker.BlockReason(target)) is { } reason)
        {
            Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: 操れない ({reason})", nameof(Dollmaster));
            var showing = failNoticeUntil > 0f;
            failNoticeUntil = Time.time + 3f;
            if (!showing) Utils.NotifyRoles(SpecifySeer: Player);
            return false;
        }

        puppet = target;
        masterOrigin = lastMasterPos = Player.GetTruePosition();
        puppetOrigin = lastTarget = target.GetTruePosition();
        endAt = Time.time + duration;
        nextStepAt = 0f;
        shownSeconds = -1;
        Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: {duration} 秒操る", nameof(Dollmaster));
        Player.RpcResetAbilityCooldown();
        Utils.NotifyRoles(SpecifySeer: target);
        return false;
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (failNoticeUntil > 0f && Time.time >= failNoticeUntil)
        {
            failNoticeUntil = 0f;
            Utils.NotifyRoles(SpecifySeer: Player);
        }
        if (puppet == null) return;

        if (Time.time >= endAt || !Player.IsAlive() || !puppet.IsAlive() || puppet.Data == null || puppet.Data.Disconnected)
        {
            Release(send: true);
            return;
        }

        int seconds = Mathf.CeilToInt(endAt - Time.time);
        if (seconds != shownSeconds)
        {
            shownSeconds = seconds;
            Utils.NotifyRoles(SpecifySeer: Player);
        }

        if (Time.time < nextStepAt) return;
        nextStepAt = Time.time + StepInterval;

        var masterPos = Player.GetTruePosition();
        // ベントやテレポートで飛んだ分は写さない (相手が壁を越えて一直線に歩こうとしないように)
        if ((masterPos - lastMasterPos).sqrMagnitude > JumpDistance * JumpDistance)
            masterOrigin += masterPos - lastMasterPos;
        lastMasterPos = masterPos;

        var target = puppetOrigin + (masterPos - masterOrigin);
        if ((target - lastTarget).sqrMagnitude < ResendDistance * ResendDistance) return;
        if (PlayerWalker.WalkTo(puppet, target)) lastTarget = target;
    }

    public override void OnStartMeeting()
    {
        failNoticeUntil = 0f;
        // 会議の始めに本編が相手の歩きを止めるので、電文は送らない
        Release(send: false);
    }

    private void Release(bool send)
    {
        if (puppet == null) return;
        var target = puppet;
        puppet = null;
        if (send) PlayerWalker.Stop(target);
        else PlayerWalker.Forget(target);
        Logger.Info($"{Player?.GetNameWithRole()} → {target.GetNameWithRole()}: 操るのをやめた", nameof(Dollmaster));
        if (send)
        {
            Utils.NotifyRoles(SpecifySeer: Player);
            Utils.NotifyRoles(SpecifySeer: target);
        }
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (isForMeeting || puppet == null || seer != Player || seen != puppet) return "";
        return Utils.ColorString(RoleInfo.RoleColor, "◈");
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer != Player || seen != Player) return "";
        if (puppet != null) return Utils.ColorString(RoleInfo.RoleColor, string.Format(GetString("DollmasterControlling"), Mathf.Max(0, shownSeconds)));
        if (failNoticeUntil > 0f) return Utils.ColorString(RoleInfo.RoleColor, GetString("DollmasterFailed"));
        return "";
    }

    // 操られている人の画面に、自分が操られていることを出す
    private string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || puppet == null || seer != puppet || seen != puppet) return "";
        return Utils.ColorString(RoleInfo.RoleColor, GetString("DollmasterControlled"));
    }
}
