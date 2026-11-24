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
    // 咆哮の演出の絵 (CNO の名前の絵) と、絵の下に描かれる体の色 (Palette.PlayerColors の番号・赤)
    private static readonly string[] RoarFrames =
    {
        "<size=10><color=#ffffff>●</color></size>",
        "<size=22><color=#ff1a1a>★</color></size>",
        "<size=18><color=#8b000099>●</color></size>",
    };
    private const byte RoarBodyColor = 0;

    private bool rampaging;
    // 暴走が終わる時刻 (Time.realtimeSinceStartup)。鬼の体を付け終えてから数え始める
    private float endAt = float.PositiveInfinity;
    private float lastKillAt = float.NegativeInfinity;
    private int lastShownSeconds = -1;

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
            Utils.NotifyRoles();
            // 変身の暗転が明けた所で、咆哮の演出を出す
            TownOfHostL.Core.CnoBurst.Play(Player.transform.position, RoarFrames, RoarBodyColor, 0.3f, 1.2f, 0.15f);
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

    public override string GetAbilityButtonText() => GetString("RampagerButtonText");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!rampaging || isForMeeting || seer != Player || seen != Player || float.IsPositiveInfinity(endAt)) return "";
        int seconds = Mathf.Max(0, Mathf.CeilToInt(endAt - Time.realtimeSinceStartup));
        return Utils.ColorString(RoleInfo.RoleColor, string.Format(GetString("RampagerRemaining"), seconds));
    }
}
