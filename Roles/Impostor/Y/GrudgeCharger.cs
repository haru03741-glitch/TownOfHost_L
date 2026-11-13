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
    public void OnCheckMurderAsKiller(MurderInfo info)
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
        info.DoKill = false;
    }
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        ClearTarget();
    }
    // 狙いを外す時は矢印も消す (残すと会議の後やキルの後も矢印の計算が回り続ける)
    private void ClearTarget()
    {
        if (KillWaitPlayer != null) TargetArrow.Remove(Player.PlayerId, KillWaitPlayer.PlayerId);
        KillWaitPlayer = null;
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!GameStates.IsInTask) return;
        if (KillWaitPlayer == null) return;
        if (!Player.IsAlive()) return;

        if (killLimit <= 0) return;

        Vector2 GCpos = Player.transform.position; //GCの位置

        var target = KillWaitPlayer;
        float targetDistance = Vector2.Distance(GCpos, target.transform.position);

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

            killThisTurn = true;
            // 印だけ立てると、送る時には killThisTurn が戻っているので、ここで送る
            Player.SyncSettings();
            Player.RpcResetAbilityCooldown();
            killThisTurn = false;
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
    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(Color.yellow, $"〈{killLimit}〉");
}