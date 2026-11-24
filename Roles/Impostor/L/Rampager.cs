using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

// 暴走鬼: ファントムのボタンで暴走する。暴走の間は本編のかくれんぼの鬼の体 (マスク) になって全員に正体が見えるが、
// キルクールが短く、足が速く、視界が広い。決めた時間が過ぎるか会議が始まると元に戻る。
public sealed class Rampager : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Rampager),
            player => new Rampager(player),
            CustomRoles.Rampager,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpL + 0,
            SetupOptionItem,
            "rmp"
        );

    public Rampager(PlayerControl player)
    : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        cooldown = OptionCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
        rampageKillCooldown = OptionRampageKillCooldown.GetFloat();
        rampageSpeed = OptionRampageSpeed.GetFloat();
        rampageVision = OptionRampageVision.GetFloat();
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionCooldown;
    private static OptionItem OptionDuration;
    private static OptionItem OptionRampageKillCooldown;
    private static OptionItem OptionRampageSpeed;
    private static OptionItem OptionRampageVision;
    private static float killCooldown;
    private static float cooldown;
    private static float duration;
    private static float rampageKillCooldown;
    private static float rampageSpeed;
    private static float rampageVision;

    private enum OptionName
    {
        RampagerRampageKillCooldown,
        RampagerRampageSpeed,
        RampagerRampageVision,
    }

    private const string SpeedKey = nameof(Rampager);
    // 暗転が明けてから、名前の下の「背筋が凍った」を消すまで
    private const float ChillLinger = 2f;

    private bool rampaging;
    // 暴走が終わる時刻 (Time.realtimeSinceStartup)。鬼の体を付け終えてから数え始める
    private float endAt = float.PositiveInfinity;
    private float lastKillAt = float.NegativeInfinity;
    private int lastShownSeconds = -1;
    // 変身の暗転の間 (と明けた少し後) か。暴走鬼以外の人の名前の下に「背筋が凍った」を出す
    private bool chilling;
    private float chillUntil = float.PositiveInfinity;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(5f, 180f, 2.5f), 40f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, GeneralOption.Duration, new(5f, 60f, 1f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRampageKillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.RampagerRampageKillCooldown, new(0f, 60f, 0.5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRampageSpeed = FloatOptionItem.Create(RoleInfo, OptionName.RampagerRampageSpeed, new(1f, 3f, 0.1f), 1.5f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionRampageVision = FloatOptionItem.Create(RoleInfo, OptionName.RampagerRampageVision, new(0.25f, 5f, 0.25f), 2f, false)
            .SetValueFormat(OptionFormat.Multiplier);
    }

    public bool IsRampaging => rampaging;

    public override void Add() => CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);
    public override void OnDestroy() => CustomRoleManager.LowerOthers.Remove(GetLowerTextOthers);

    public float CalculateKillCooldown() => rampaging ? rampageKillCooldown : killCooldown;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = cooldown;
        if (rampaging) opt.SetFloat(FloatOptionNames.ImpostorLightMod, rampageVision);
    }

    // 暴走の間に押しても何もしない。ボタンのクールは暴走が終わった時の送り直しで始め直す
    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = null;
        if (rampaging || !Player.IsAlive()) return;
        rampaging = true;
        endAt = float.PositiveInfinity;
        lastShownSeconds = -1;
        chilling = true;
        chillUntil = float.PositiveInfinity;
        // 暗転が始まる前に名前を送る (暗い間も自分の名前の下の文字は見える)
        Utils.NotifyRoles();
        PlayerSpeed.Multiply(Player.PlayerId, SpeedKey, rampageSpeed);
        Player.MarkDirtySettings();
        SeekerBody.Apply(Player, OnApplied, () => EndRampage(release: false));
        Logger.Info($"暴走: {Player.GetNameWithRole()}", nameof(Rampager));
    }

    // 鬼の体を付けた窓の後 (暴走の始め・キルの後の付け直し)。窓で SetRole を送り直したので、キルクールを付け直す
    private void OnApplied()
    {
        if (!rampaging || !Player.IsAlive()) return;
        float now = Time.realtimeSinceStartup;
        if (float.IsPositiveInfinity(endAt))
        {
            endAt = now + duration;
            chillUntil = now + ChillLinger;
            Utils.NotifyRoles();
        }
        float remaining = Mathf.Max(0f, rampageKillCooldown - (now - lastKillAt));
        Player.SetKillCooldown(remaining);
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (rampaging && !info.IsSuicide) lastKillAt = Time.realtimeSinceStartup;
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!rampaging || !AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) { EndRampage(release: true); return; }
        if (chilling && Time.realtimeSinceStartup >= chillUntil)
        {
            chilling = false;
            Utils.NotifyRoles();
        }
        float left = endAt - Time.realtimeSinceStartup;
        if (left <= 0f) { EndRampage(release: true); return; }
        if (float.IsPositiveInfinity(left)) return;
        int seconds = (int)System.Math.Ceiling(left);
        if (seconds == lastShownSeconds) return;
        lastShownSeconds = seconds;
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    private void EndRampage(bool release)
    {
        if (!rampaging) return;
        rampaging = false;
        chilling = false;
        endAt = float.PositiveInfinity;
        PlayerSpeed.Remove(Player.PlayerId, SpeedKey);
        if (release) SeekerBody.Release(Player);
        if (Player.IsAlive())
        {
            Player.ResetKillCooldown();
            Player.MarkDirtySettings();
            if (!GameStates.IsMeeting && release) Player.SetKillCooldown();
        }
        Utils.NotifyRoles();
        Logger.Info($"暴走が終わった: {Player.GetNameWithRole()}", nameof(Rampager));
    }

    public override void OnStartMeeting() => EndRampage(release: true);

    // 暴走鬼以外の人の画面で、自分の名前の下に出す
    private string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!chilling || isForMeeting || seer != seen || seer == Player || !seer.IsAlive()) return "";
        return "\n<color=#a8d8ff>" + GetString("RampagerChill") + "</color>";
    }

    public override string GetAbilityButtonText() => GetString("RampagerButtonText");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!rampaging || isForMeeting || seer != Player || seen != Player) return "";
        if (float.IsPositiveInfinity(endAt))
            return chilling ? Utils.ColorString(RoleInfo.RoleColor, GetString("RampagerTransform")) : "";
        int seconds = Mathf.Max(0, Mathf.CeilToInt(endAt - Time.realtimeSinceStartup));
        return Utils.ColorString(RoleInfo.RoleColor, string.Format(GetString("RampagerRemaining"), seconds));
    }
}
