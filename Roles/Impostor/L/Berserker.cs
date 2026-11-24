using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

// バーサーカー: ファントムのボタンで暴走する。暴走の間は本編のかくれんぼの鬼の体 (マスク) になって全員に正体が見えるが、
// キルクールが短く、足が速く、視界が広い。決めた時間が過ぎるか、会議が始まるか、もう一度ボタンを押す (鎮静化) と元に戻る。
// 1 回の暴走で決めた人数をキルすると忘我になり、仲間のインポスターもキルできるようになる。
// 役職の発想は Nebula on the Ship のバーサーカー (コードは自前)。
public sealed class Berserker : RoleBase, IImpostor, IUsePhantomButton, ITeammateKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Berserker),
            player => new Berserker(player),
            CustomRoles.Berserker,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpL + 0,
            SetupOptionItem,
            "bsk"
        );

    public Berserker(PlayerControl player)
    : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        cooldown = OptionCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
        rampageKillCooldown = OptionRampageKillCooldown.GetFloat();
        rampageSpeed = OptionRampageSpeed.GetFloat();
        rampageVision = OptionRampageVision.GetFloat();
        canCalmDown = OptionCanCalmDown.GetBool();
        killsForTrance = OptionKillsForTrance.GetInt();
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionCooldown;
    private static OptionItem OptionDuration;
    private static OptionItem OptionRampageKillCooldown;
    private static OptionItem OptionRampageSpeed;
    private static OptionItem OptionRampageVision;
    private static OptionItem OptionCanCalmDown;
    private static OptionItem OptionKillsForTrance;
    private static float killCooldown;
    private static float cooldown;
    private static float duration;
    private static float rampageKillCooldown;
    private static float rampageSpeed;
    private static float rampageVision;
    private static bool canCalmDown;
    private static int killsForTrance;

    private enum OptionName
    {
        BerserkerRampageKillCooldown,
        BerserkerRampageSpeed,
        BerserkerRampageVision,
        BerserkerCanCalmDown,
        BerserkerKillsForTrance,
    }

    private const string SpeedKey = nameof(Berserker);
    // 暗転が明けてから、名前の下の「背筋が凍った」を消すまで
    private const float ChillLinger = 2f;

    private bool rampaging;
    // 暴走が終わる時刻 (Time.realtimeSinceStartup)。鬼の体を付け終えてから数え始める
    private float endAt = float.PositiveInfinity;
    private float lastKillAt = float.NegativeInfinity;
    private int lastShownSeconds = -1;
    // 変身の暗転の間 (と明けた少し後) か。バーサーカー以外の人の名前の下に「背筋が凍った」を出す
    private bool chilling;
    private float chillUntil = float.PositiveInfinity;
    // 今の暴走でキルした数
    private int rampageKills;
    // 忘我か (仲間もキルできる)
    private bool trance;
    // 忘我の見え方を、窓 (SetRole を送れない間) が明けたら送る
    private bool tranceSendPending;
    // 忘我で見え方を替えた仲間と、替える前の見え方 (自分の画面で)
    private readonly Dictionary<byte, RoleTypes> tranceViews = new();

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(5f, 180f, 2.5f), 40f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, GeneralOption.Duration, new(5f, 60f, 1f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRampageKillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.BerserkerRampageKillCooldown, new(0f, 60f, 0.5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRampageSpeed = FloatOptionItem.Create(RoleInfo, OptionName.BerserkerRampageSpeed, new(1f, 3f, 0.1f), 1.5f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionRampageVision = FloatOptionItem.Create(RoleInfo, OptionName.BerserkerRampageVision, new(0.25f, 5f, 0.25f), 2f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionCanCalmDown = BooleanOptionItem.Create(RoleInfo, OptionName.BerserkerCanCalmDown, true, false);
        // 0 で忘我にならない
        OptionKillsForTrance = IntegerOptionItem.Create(RoleInfo, OptionName.BerserkerKillsForTrance, new(0, 5, 1), 2, false)
            .SetValueFormat(OptionFormat.Players);
    }

    public bool IsRampaging => rampaging;
    public bool CanKillTeammates => trance && rampaging;

    public override void Add() => CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);
    public override void OnDestroy() => CustomRoleManager.LowerOthers.Remove(GetLowerTextOthers);

    public float CalculateKillCooldown() => rampaging ? rampageKillCooldown : killCooldown;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = cooldown;
        if (rampaging) opt.SetFloat(FloatOptionNames.ImpostorLightMod, rampageVision);
    }

    // 暴走の間に押すと鎮静化する (変身中と窓の間は何もしない)。
    // ボタンを戻す SetRole は鬼の体を外すので送らない。ボタンのクールは窓の SetRole で始め直される
    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = null;
        if (!Player.IsAlive()) return;
        if (rampaging)
        {
            if (canCalmDown && !float.IsPositiveInfinity(endAt) && !SeekerBody.InWindow)
            {
                Logger.Info($"鎮静化: {Player.GetNameWithRole()}", nameof(Berserker));
                EndRampage(release: true);
            }
            return;
        }
        rampaging = true;
        rampageKills = 0;
        endAt = float.PositiveInfinity;
        lastShownSeconds = -1;
        chilling = true;
        chillUntil = float.PositiveInfinity;
        // 暗転が始まる前に名前を送る (暗い間も自分の名前の下の文字は見える)
        Utils.NotifyRoles();
        PlayerSpeed.Multiply(Player.PlayerId, SpeedKey, rampageSpeed);
        Player.MarkDirtySettings();
        SeekerBody.Apply(Player, OnApplied, () => EndRampage(release: false));
        Logger.Info($"暴走: {Player.GetNameWithRole()}", nameof(Berserker));
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
        if (tranceSendPending) SendTranceViews();
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (!rampaging || info.IsSuicide) return;
        lastKillAt = Time.realtimeSinceStartup;
        rampageKills++;
        if (trance || killsForTrance <= 0 || rampageKills < killsForTrance) return;
        trance = true;
        tranceSendPending = true;
        Logger.Info($"忘我: {Player.GetNameWithRole()}", nameof(Berserker));
        if (!SeekerBody.InWindow) SendTranceViews();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    // 自分の画面で、生きている仲間 (キルを持つ人に見えている人) を、キルの対象にできる見え方にする
    private void SendTranceViews()
    {
        tranceSendPending = false;
        if (!trance || !Player.IsAlive()) return;
        bool sent = false;
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            if (pc.PlayerId == Player.PlayerId || tranceViews.ContainsKey(pc.PlayerId)) continue;
            if (!pc.Is(CustomRoleTypes.Impostor)) continue;
            var current = RoleView.Living(pc, Player);
            if (!RoleView.IsKillerRole(current)) continue;
            tranceViews[pc.PlayerId] = current;
            RoleView.Send(pc, Player, RoleTypes.Scientist);
            sent = true;
        }
        if (sent) Revive.BeginSettling();
    }

    // 忘我の見え方を戻す。窓の間は送れないので、窓が明けてから (OnFixedUpdate) 戻す
    private void RestoreTranceViews()
    {
        if (tranceViews.Count == 0) return;
        if (SeekerBody.InWindow) return;
        foreach (var (id, role) in tranceViews)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || !pc.IsAlive()) continue;
            RoleView.Send(pc, Player, role);
        }
        tranceViews.Clear();
        Revive.BeginSettling();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!rampaging)
        {
            if (tranceViews.Count > 0) RestoreTranceViews();
            return;
        }
        if (tranceSendPending && !SeekerBody.InWindow) SendTranceViews();
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
        trance = false;
        tranceSendPending = false;
        endAt = float.PositiveInfinity;
        RestoreTranceViews();
        PlayerSpeed.Remove(Player.PlayerId, SpeedKey);
        if (release) SeekerBody.Release(Player);
        if (Player.IsAlive())
        {
            Player.ResetKillCooldown();
            Player.MarkDirtySettings();
            if (!GameStates.IsMeeting && release) Player.SetKillCooldown();
        }
        Utils.NotifyRoles();
        Logger.Info($"暴走が終わった: {Player.GetNameWithRole()}", nameof(Berserker));
    }

    public override void OnStartMeeting()
    {
        EndRampage(release: true);
        // 会議の前に戻せなかった (窓の間だった) 見え方は、会議の後の配り直しが表から引くので、表だけ戻しておく
        foreach (var (id, role) in tranceViews)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc != null) RoleView.Record(Player, pc, role);
        }
        tranceViews.Clear();
    }

    // バーサーカー以外の人の画面で、自分の名前の下に出す
    private string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!chilling || isForMeeting || seer != seen || seer == Player || !seer.IsAlive()) return "";
        return "\n<color=#a8d8ff>" + GetString("BerserkerChill") + "</color>";
    }

    public override string GetAbilityButtonText() => GetString("BerserkerButtonText");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!rampaging || isForMeeting || seer != Player || seen != Player) return "";
        if (float.IsPositiveInfinity(endAt))
            return chilling ? Utils.ColorString(RoleInfo.RoleColor, GetString("BerserkerTransform")) : "";
        int seconds = Mathf.Max(0, Mathf.CeilToInt(endAt - Time.realtimeSinceStartup));
        var text = Utils.ColorString(RoleInfo.RoleColor, string.Format(GetString("BerserkerRemaining"), seconds));
        if (trance) text += " " + GetString("BerserkerTrance");
        else if (killsForTrance > 0) text += $" ({rampageKills}/{killsForTrance})";
        return text;
    }
}
