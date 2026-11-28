using System.Text;
using UnityEngine;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;
public sealed class GrudgeCharger : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(GrudgeCharger),
            player => new GrudgeCharger(player),
            CustomRoles.GrudgeCharger,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 1600,
            SetUpOptionItem,
            "グラージチャージャー"
        );
    public GrudgeCharger(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        selectTargetCooldown = OptionSelectTargetCooldown.GetFloat();
        chargeKillCooldown = OptionChargeKillCooldown.GetFloat();
        oneGaugeChargeCount = OptionOneGaugeChargeCount.GetInt();
        oneGaugeAddKillCount = OptionOneGaugeAddKillCount.GetInt();
        killCountAtStartGame = OptionKillCountAtStartGame.GetInt();
    }
    private static OptionItem OptionSelectTargetCooldown;
    private static OptionItem OptionChargeKillCooldown;
    private static OptionItem OptionOneGaugeChargeCount;
    private static OptionItem OptionOneGaugeAddKillCount;
    private static OptionItem OptionKillCountAtStartGame;
    enum OptionName
    {
        GrudgeChargerSelectTargetCooldown,
        GrudgeChargerChargeKillCooldown,
        GrudgeChargerOneGaugeChargeCount,
        GrudgeChargerOneGaugeAddKillCount,
        GrudgeChargerKillCountAtStartGame,
    }
    private static float selectTargetCooldown;
    private static float chargeKillCooldown;
    private static int oneGaugeChargeCount;
    private static int oneGaugeAddKillCount;
    private static int killCountAtStartGame;

    int killLimit;
    bool killThisTurn;
    /// <summary> チャージ回数 </summary>
    int chargeCount;
    PlayerControl KillWaitPlayer;
    byte killWaitId = byte.MaxValue;

    private static void SetUpOptionItem()
    {
        OptionSelectTargetCooldown = FloatOptionItem.Create(RoleInfo, OptionName.GrudgeChargerSelectTargetCooldown, new(2.5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionChargeKillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.GrudgeChargerChargeKillCooldown, new(0.5f, 180f, 0.5f), 2f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionOneGaugeChargeCount = IntegerOptionItem.Create(RoleInfo, OptionName.GrudgeChargerOneGaugeChargeCount, new(1, 30, 1), 10, false)
            .SetValueFormat(OptionFormat.Times);
        OptionOneGaugeAddKillCount = IntegerOptionItem.Create(RoleInfo, OptionName.GrudgeChargerOneGaugeAddKillCount, new(1, 15, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionKillCountAtStartGame = IntegerOptionItem.Create(RoleInfo, OptionName.GrudgeChargerKillCountAtStartGame, new(0, 2, 1), 0, false)
            .SetValueFormat(OptionFormat.Times);
    }
    public float CalculateKillCooldown() => chargeKillCooldown;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = killThisTurn ? 0f : selectTargetCooldown;
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    public override void Add()
    {
        killThisTurn = false;
        killLimit = killCountAtStartGame;
        chargeCount = 0;
        KillWaitPlayer = null;
    }
    // キルボタンはチャージに使う (キルでないので、守りより前に振り分ける)
    public bool OnCheckMurderBeforeGuards(MurderInfo info)
    {
        var killer = info.AttemptKiller;
        chargeCount++;
        if (chargeCount >= oneGaugeChargeCount)
        {
            killLimit += oneGaugeAddKillCount;
            chargeCount = 0;
        }
        Logger.Info($"{Player.GetNameWithRole()} : チャージ({chargeCount}/{oneGaugeChargeCount})", "GrudgeCharger");
        Utils.NotifyRoles(SpecifySeer: Player);

        killer.SetKillCooldown();
        return false;
    }
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        ClearTarget();
    }
    // 狙いを外す時は矢印も消す (残すと会議の後やキルの後も矢印の計算が回り続ける)
    private void ClearTarget()
    {
        // 壊された後の PlayerControl からは番号を読まず、選んだ時に控えた番号で矢印を外す
        if (KillWaitPlayer is not null) TargetArrow.Remove(Player.PlayerId, killWaitId);
        KillWaitPlayer = null;
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!GameStates.IsInTask) return;
        // 抜けた人の PlayerControl は壊されて Unity の null になる。それも下の「死んだり抜けたりしたら外す」へ通す
        if (KillWaitPlayer is null) return;
        if (!Player.IsAlive()) return;

        // 狙いが他の理由で死んだり抜けたりしたら外す (残すと会議まで次の狙いを選べない)
        if (!KillWaitPlayer.IsAlive())
        {
            ClearTarget();
            Player.MarkDirtySettings();
            Player.RpcResetAbilityCooldown();
            Utils.NotifyRoles(SpecifySeer: Player);
            return;
        }

        if (killLimit <= 0) return;

        Vector2 GCpos = Player.transform.position; //GCの位置

        var target = KillWaitPlayer;
        float targetDistance = Vector2.Distance(GCpos, target.transform.position);

        // 鬼の体の差し替えの窓の間は GameManager が無い
        if (!GameManager.Instance) return;
        var KillRange = GameManager.Instance.LogicOptions.GetKillDistance();
        if (targetDistance <= KillRange && Player.CanMove && target.CanMove)
        {
            ClearTarget();
            if (CustomRoleManager.OnCheckMurder(Player, target, true))
            {
                killLimit--;
                target.SetRealKiller(Player);
                Logger.Info($"{Player.GetNameWithRole()} : 残り{killLimit}発", "GrudgeCharger");
                Utils.NotifyRoles(SpecifySeer: Player);
            }

            // クール 0 の設定を送り、それが届いてから数え直す。数え直すまで killThisTurn を立てておく
            // (間に別の設定の送りが入っても 0 のまま送る)
            killThisTurn = true;
            Player.SyncSettings();
            _ = After(() =>
            {
                killThisTurn = false;
                if (Player.IsAlive()) Player.RpcResetAbilityCooldown();
            }, 0.2f, "GrudgeCharger.ResetAfterKill");
        }
    }

    public override void AfterMeetingTasks()
    {
        ClearTarget();
        Player.MarkDirtySettings();
        Player.RpcResetAbilityCooldown();
    }

    public override bool OnCheckShapeshift(PlayerControl target, ref bool animate)
    {
        // 自身または相方インポスター、死亡しているターゲットは選択できない
        if (target.Is(CustomRoleTypes.Impostor) || !target.IsAlive()) return false;
        if (KillWaitPlayer != null) return false;

        KillWaitPlayer = target;
        killWaitId = target.PlayerId;
        TargetArrow.Add(Player.PlayerId, target.PlayerId);

        Logger.Info($"{Player.GetNameWithRole()}のターゲットを{target.GetNameWithRole()}に設定", "GrudgeCharger");
        Player.MarkDirtySettings();
        Utils.NotifyRoles(SpecifySeer: Player);
        return false;
    }

    public bool OverrideKillButtonText(out string text)
    {
        text = GetString("GrudgeChargerCharge");
        return true;
    }
    public override string GetAbilityButtonText() => GetString("GrudgeChargerSelectTarget");
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        //seenが省略の場合seer
        seen ??= seer;
        //seerおよびseenが自分である場合以外は関係なし
        if (!Is(seer) || !Is(seen)) return "";

        //矢印表示する必要がなければ無し
        if (KillWaitPlayer == null || isForMeeting) return string.Empty;

        return TargetArrow.GetArrows(seer, KillWaitPlayer.PlayerId);
    }

    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        //seenが省略の場合seer
        seen ??= seer;
        //seerおよびseenが自分である場合以外は関係なし
        if (!Is(seer) || !Is(seen) || !Player.IsAlive() || isForMeeting) return "";

        var str = new StringBuilder();
        int charge = chargeCount;
        int empty = oneGaugeChargeCount - chargeCount;

        int newLine = 0;
        int count = 1;
        if (oneGaugeChargeCount > 15)
        {
            newLine = oneGaugeChargeCount / 2;
        }

        str.Append("<size=80%><line-height=85%><color=#ff6347>");
        for (int i = 0; i < charge; i++, count++)
        {
            str.Append('█');
            if (count == newLine) str.Append('\n');
        }
        str.Append("</color><color=#888888>");
        for (int i = 0; i < empty; i++, count++)
        {
            str.Append('■');
            if (count == newLine) str.Append('\n');
        }
        str.Append("</color></line-height></size>");

        return str.ToString();
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        //seenが省略の場合seer
        seen ??= seer;
        //seerおよびseenが自分である場合以外は関係なし
        if (!Is(seer) || !Is(seen) || !Player.IsAlive() || isForMeeting) return string.Empty;

        var str = new StringBuilder();
        if (KillWaitPlayer == null)
            str.Append(GetString(isForHud ? "ShapeSelectPlayerTagBefore" : "ShapeSelectPlayerTagMiniBefore"));
        else
        {
            str.Append(GetString(isForHud ? "SelectPlayerTag" : "SelectPlayerTagMini"));
            str.Append(KillWaitPlayer.GetRealName(Options.GetNameChangeModes() == NameChange.Crew));
        }
        return str.ToString();
    }
    public override int? UsesRemaining => killLimit;
    public override bool UsesOnKillButton => true;
    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(Color.yellow, $"〈{killLimit}〉");
}