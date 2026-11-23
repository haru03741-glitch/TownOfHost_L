using Hazel;
using InnerNet;
using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;

// フレーマー: キルボタンを1回押すと「姿の元」にする人を選び、ファントムボタンで近くの人をその姿に変える。
// 姿を変えられた本人には変わった姿を送らないので、本人は気づかない。ダブルクリックで普通にキルできる。
public sealed class Framer : RoleBase, IImpostor, IUsePhantomButton, IDoubleTrigger
{
    // 役職の元: TownOfHost-hamo
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Framer),
            player => new Framer(player),
            CustomRoles.Framer,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 992500,
            SetupOptionItem,
            "frm"
        );

    public Framer(PlayerControl player)
    : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        abilityCooldown = OptionAbilityCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
        maxCount = OptionMaxCount.GetInt();
        meetingNotice = OptionMeetingNotice.GetBool();
        impostorAware = OptionImpostorAware.GetBool();
        sourceId = byte.MaxValue;
        // 姿を変えた人の一覧は全員の共有なので、ここでは消さない (試合の途中で誰かがこの役職になった時に、ほかの人が姿を変えさせた相手が元に戻らなくなる)。試合の始めは Init が空にする
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionAbilityCooldown;
    private static OptionItem OptionDuration;
    private static OptionItem OptionMaxCount;
    private static OptionItem OptionMeetingNotice;
    private static OptionItem OptionImpostorAware;
    enum OptionName
    {
        FramerDuration,
        FramerMeetingNotice,
        FramerImpostorAware,
    }
    private static float killCooldown;
    private static float abilityCooldown;
    private static float duration;
    private static int maxCount;
    private static bool meetingNotice;
    private static bool impostorAware;

    private int remaining;
    // 姿の元にする人
    private byte sourceId;
    // 姿を変えている人 → 元に戻す時刻
    public static readonly Dictionary<byte, float> framed = new();
    [Attributes.GameModuleInitializer]
    public static void Init() => framed.Clear();
    private bool usedSinceMeeting;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 1f), 35f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAbilityCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0f, 255f, 1f), 40f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, OptionName.FramerDuration, new(0f, 999f, 1f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMaxCount = IntegerOptionItem.Create(RoleInfo, GeneralOption.OptionCount, new(0, 999, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionMeetingNotice = BooleanOptionItem.Create(RoleInfo, OptionName.FramerMeetingNotice, true, false);
        OptionImpostorAware = BooleanOptionItem.Create(RoleInfo, OptionName.FramerImpostorAware, true, false);
    }

    public float CalculateKillCooldown() => killCooldown;
    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.PhantomCooldown = abilityCooldown;

    public override void Add()
    {
        Player.AddDoubleTrigger();
        remaining = maxCount;
    }

    public bool CheckAction => remaining > 0;

    public bool SingleAction(PlayerControl killer, PlayerControl target)
    {
        sourceId = target.PlayerId;
        Logger.Info($"{killer.GetNameWithRole().RemoveHtmlTags()} → {target.GetNameWithRole().RemoveHtmlTags()}：姿の元に選択", "Framer");
        Utils.NotifyRoles(SpecifySeer: killer);
        return false;
    }

    public bool DoubleAction(PlayerControl killer, PlayerControl target) => true;

    public bool IsPhantomRole => remaining > 0;

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = false;
        if (!AmongUsClient.Instance.AmHost || remaining <= 0 || Camouflage.IsCamouflage) return;
        var source = Utils.GetPlayerById(sourceId);
        var target = Player.GetKillTarget(true);
        if (source == null || source.Data == null || target == null || target == source || !target.IsAlive()) return;

        ResetCooldown = true;
        remaining--;
        usedSinceMeeting = true;
        framed[target.PlayerId] = Time.time + duration;
        // インポスターが対象で、自覚できる設定なら本人にも見せる
        var targetSees = impostorAware && target.GetCustomRole().IsImpostor();
        SendOutfit(target, source, targetSees);
        Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} を {source.GetNameWithRole().RemoveHtmlTags()} の姿に変えました(残り{remaining}回)", "Framer");
        Utils.NotifyRoles();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || player != Player || framed.Count == 0) return;
        if (GameStates.Intro || GameStates.CalledMeeting) return;
        List<byte> ended = null;
        foreach (var (id, until) in framed)
            if (Time.time >= until) (ended ??= new()).Add(id);
        if (ended == null) return;
        foreach (var id in ended) Revert(id);
        Utils.NotifyRoles();
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => RevertAll();
    public override void OnDestroy() => RevertAll();

    private void RevertAll()
    {
        if (!AmongUsClient.Instance.AmHost || framed.Count == 0) return;
        foreach (var id in new List<byte>(framed.Keys)) Revert(id);
    }

    private static void Revert(byte id)
    {
        framed.Remove(id);
        var target = Utils.GetPlayerById(id);
        if (target == null || Camouflage.IsCamouflage) return;
        // 自分の姿へ変身させると元の姿に戻る
        SendOutfit(target, target, true);
    }
    // target を pc の姿にする。includeTarget が false なら本人には送らない(本人の画面では元のまま)
    private static void SendOutfit(PlayerControl target, PlayerControl pc, bool includeTarget)
    {
        if (target == null || target.Data == null || target.Data.Disconnected || !AmongUsClient.Instance.AmHost) return;
        if (pc == null || pc.Data == null) return;

        if (includeTarget)
        {
            target.RpcShapeshift(pc, false);
            return;
        }

        // 宛先つきの Shapeshift は客ごとに別のパケットで送る
        foreach (var seer in Main.AllPlayerControls)
        {
            if (seer == null || seer == target) continue;

            if (seer == PlayerControl.LocalPlayer)
            {
                target.Shapeshift(pc, false);
                continue;
            }

            var clientId = seer.GetClientId();
            if (clientId == -1) continue;

            var writer = AmongUsClient.Instance.StartRpcImmediately(target.NetId, (byte)RpcCalls.Shapeshift, SendOption.Reliable, clientId);
            writer.WriteNetObject(pc);
            writer.Write(false);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }

    public override string MeetingAddMessage()
    {
        if (!meetingNotice || !usedSinceMeeting) return "";
        usedSinceMeeting = false;
        return Utils.ColorString(RoleInfo.RoleColor, Translator.GetString("FramerMeetingMessage"));
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen, bool isForMeeting = false)
    {
        seen ??= seer;
        var mark = "";

        // 選んでいる姿の元は自分にだけ見える
        if (seer == Player && seen.PlayerId == sourceId) mark += Utils.ColorString(RoleInfo.RoleColor, "◎");

        // 姿を変えている人には、フレーマーとインポスターにだけ印を出す
        if (framed.ContainsKey(seen.PlayerId) && (seer == Player || seer.GetCustomRole().IsImpostor()))
            mark += Utils.ColorString(RoleInfo.RoleColor, "◇");

        return mark;
    }

    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(remaining > 0 ? RoleInfo.RoleColor : Color.gray, $"[{remaining}]");

    public override string GetAbilityButtonText() => Translator.GetString("FramerAbilityButtonText");
}
