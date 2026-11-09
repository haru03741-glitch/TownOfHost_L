using AmongUs.GameOptions;

using System.Linq;
using System.Collections.Generic;

using UnityEngine;

using Hazel;



using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;



namespace TownOfHostL.Roles.Impostor;



public sealed class Magician : RoleBase, IImpostor, IUsePhantomButton

{

    // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(Magician),
            player => new Magician(player),
            CustomRoles.Magician,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 5400,
            SetupOptionItem,
            "mgc"
        );

    public Magician(PlayerControl player)

    : base(

        RoleInfo,

        player

    )

    {

        DefaultCooldown = OptionMagicCooldown.GetFloat();

        Maximum = OptionMaximum.GetFloat();

        Radius = OptionRadius.GetFloat();

        ShowDeadbody = OptionShowDeadbody.GetBool();

        MagicUseKillCount = OptionMagicUseKillCount.GetInt();

        ResetKillCount = OptionResetKillCount.GetBool();

        ResetMagicTarget = OptionResetMagicTarget.GetBool();

        MagicCooldown = DefaultCooldown;

        MagicCount = 0;

        HaveKillCount = 0;

        MagicTarget = new();

        magiccount = 0;



        if (OptionMettingMark.GetBool())

        {

            CustomRoleManager.MarkOthers.Add(GetMarkOthers);

        }

    }



    static OptionItem OptionMagicCooldown;

    static OptionItem OptionMaximum;

    static OptionItem OptionRadius;

    static OptionItem OptionShowDeadbody;

    static OptionItem OptionMagicUseKillCount;

    static OptionItem OptionResetKillCount;

    static OptionItem OptionResetMagicTarget;

    static OptionItem OptionMettingMark;



    static float DefaultCooldown;

    static float Maximum;

    static float Radius;

    static bool ShowDeadbody; //会議で死亡してるかを表示するか

    static int MagicUseKillCount; //必要なキル数

    static bool ResetKillCount; //←↓会議後リセットするかの設定 | キル数



    float MagicCooldown;

    bool ResetMagicTarget;// マジックで消す予定の人



    int MagicCount;

    int HaveKillCount;

    List<byte> MagicTarget;



    enum Option

    {

        MagicianMaximum,

        MagicianRadius,

        MagicianShowDeadbody,

        MagicianMagicUseKillCount,

        MagicianResetKillCount,

        MagicianResetMagicTarget,

        MagicianMeetingMark,

    }



    public static void SetupOptionItem()

    {

        OptionMagicCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new FloatValueRule(0, 120, 1), 15, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionMaximum = IntegerOptionItem.Create(RoleInfo, Option.MagicianMaximum, new(0, 99, 1), 3, false)

            .SetValueFormat(OptionFormat.Times).SetZeroNotation(OptionZeroNotation.Infinity);

        OptionRadius = FloatOptionItem.Create(RoleInfo, Option.MagicianRadius, new(0.5f, 3f, 0.5f), 1.5f, false)

            .SetValueFormat(OptionFormat.Multiplier);

        OptionMagicUseKillCount = IntegerOptionItem.Create(RoleInfo, Option.MagicianMagicUseKillCount, new(0, 99, 1), 1, false)

        .SetValueFormat(OptionFormat.Players);

        OptionShowDeadbody = BooleanOptionItem.Create(RoleInfo, Option.MagicianShowDeadbody, false, false);

        OptionResetKillCount = BooleanOptionItem.Create(RoleInfo, Option.MagicianResetKillCount, true, false);

        OptionResetMagicTarget = BooleanOptionItem.Create(RoleInfo, Option.MagicianResetMagicTarget, false, false);

        OptionMettingMark = BooleanOptionItem.Create(RoleInfo, Option.MagicianMeetingMark, false, false);

    }



    public override void OnDestroy()

    {

        CustomRoleManager.MarkOthers.Remove(GetMarkOthers);

    }



    public override void OnStartMeeting()

    {

        if (Player.IsAlive()) return;

        MagicTarget.Clear();

    }



    public override void AfterMeetingTasks()

    {

        if (ResetKillCount) HaveKillCount = 0;

        if (ResetMagicTarget) MagicTarget.Clear(); //↓死亡している対象を削除

        else MagicTarget.RemoveAll(id => PlayerState.GetByPlayerId(id).IsDead);



        //クールダウンリセット

        if (Maximum <= MagicCount && Maximum > 0)

            MagicCooldown = 999;

        else

            MagicCooldown = DefaultCooldown;

    }



    public void OnMurderPlayerAsKiller(MurderInfo info)

    {

        HaveKillCount++;

        SendRPC_SetKillCount();

    }



    bool IUsePhantomButton.IsPhantomRole => MagicCount < Maximum || Maximum is 0;

    bool IUsePhantomButton.IsresetAfterKill => false;

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)

    {

        AdjustKillCooldown = true;

        ResetCooldown = false;

        if (Maximum <= MagicCount && Maximum > 0) return;

        ResetCooldown = true;



        bool check = false;

        float minDis = Radius * Radius;

        PlayerControl target = null;



        var myPos = Player.transform.position;

        foreach (var pc in Main.AllAlivePlayerControls)

        {

            //自分と相方以外で、近くにいて既にターゲットにされていないか

            if (pc.Is(CustomRoles.King) || pc.Is(CustomRoles.Autocrat)) continue;

            if (Is(pc) || MagicTarget.Contains(pc.PlayerId)) continue;

            if (pc.IsTeammate(Player)) continue;



            var dis = Vector2.SqrMagnitude(myPos - pc.transform.position);

            if (dis < minDis)

            {

                target = pc;

                minDis = dis;

            }

        }

        if (target != null)

        {

            MagicCount++;

            SendRPC_AddTarget(target.PlayerId);

            check = (Maximum <= MagicCount && Maximum > 0) || MagicCooldown != DefaultCooldown;

            MagicTarget.Add(target.PlayerId);

            Player.SetKillCooldown(ForceProtect: true, target: target);

            MagicCooldown = (Maximum <= MagicCount && Maximum > 0) ? 999 : DefaultCooldown;

            _ = new LateTask(() => Utils.NotifyRoles(), 0.1f);

        }

        else

        {

            check = MagicCooldown != 0;

            MagicCooldown = 0;

        }

        if (check)

        {

            Player.MarkDirtySettings();

            Player.RpcResetAbilityCooldown();

        }

    }



    public override void OnFixedUpdate(PlayerControl player)

    {

        if (!AmongUsClient.Instance.AmHost) return;

        if (MagicTarget.Count == 0 || HaveKillCount < MagicUseKillCount) return;

        // キルクールの始め直しは 1 回だけ (相手ごとに送ると、本人宛ての同じ通信が人数ぶん重なる)
        Player.SetKillCooldown(ForceProtect: true, target: MagicTarget.Select(Utils.GetPlayerById).FirstOrDefault(pc => pc.IsAlive()));

        foreach (var id in MagicTarget)

        {

            var target = Utils.GetPlayerById(id);

            if (!target.IsAlive()) continue;

            var state = PlayerState.GetByPlayerId(target.PlayerId);


            if (ShowDeadbody)

            {

                var position = target.transform.position;

                // 飛ばしと戻しは対で送る (予算で戻しだけ落ちると、マップの外に取り残される)
                target.RpcSnapTo(new Vector2(9999f, 9999f), force: true);

                target.RpcMurderPlayer(target, true);

                _ = new LateTask(() => target.RpcSnapTo(position, force: true), 0.5f);

            }

            else

            {

                target.RpcExileV3();

            }

            if (false)

            {


            }

            magiccount++;

            state.DeathReason = CustomDeathReason.Magic;

            state.SetDead();

            var roomName = target.GetShipRoomName();


            Utils.AddGameLog($"Magic", $"{(target.PlayerId).ColoredName()}({RoleText.GetRoleNameText(target.PlayerId, showSubRole: false).RemoveSizeTags()}) [{Utils.GetVitalText(target.PlayerId, true)}]〔{roomName}〕");

            Utils.AddGameLog("", $"\n\t⇐ {(Player.PlayerId).ColoredName()}({RoleText.GetRoleNameText(Player.PlayerId, showSubRole: false)})");

        }

        MagicTarget.Clear();

        HaveKillCount -= MagicUseKillCount;

        SendRPC_MagicKill();

        Player.SetKillCooldown(ForceProtect: true);

        _ = new LateTask(() => Utils.NotifyRoles(), 0.1f);

    }



    public override string GetAbilityButtonText() => GetString("MagicButtonText");




    public override string GetProgressText(bool comms = false)

    {

        if (Maximum == 0 && MagicUseKillCount == 0) return "";

        var text = "(";

        if (Maximum != 0) text += Maximum - MagicCount;

        if (MagicUseKillCount > 0) text += (Maximum > 0 ? " | " : "") + $"{HaveKillCount}/{MagicUseKillCount}";

        var color = HaveKillCount < MagicUseKillCount ? Color.yellow : MagicCount < Maximum && Maximum > 0 ? Color.red : Color.gray;

        return Utils.ColorString(color, text + ")");

    }

    public override void ApplyGameOptions(IGameOptions opt)

    {

        AURoleOptions.PhantomCooldown = MagicCooldown;

    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (seen.PlayerId != seer.PlayerId || isForMeeting || !Player.IsAlive()) return "";



        if (isForHud) return GetString("PhantomButtonLowertext");

        return $"<size=50%>{GetString("PhantomButtonLowertext")}</size>";

    }



    public string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)

    {

        seen ??= seer;

        if (isForMeeting && Player.IsAlive() && MagicTarget.Contains(seen.PlayerId))

        {

            return Utils.ColorString(Palette.ImpostorRed, "♢");

        }

        return "";

    }



    public void SendRPC_AddTarget(byte targetId)

    {

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.WritePacked((int)RPC_Types.AddMagicTarget);

        sender.Writer.Write(targetId);

        sender.Writer.Write(MagicCount);

    }



    public void SendRPC_SetKillCount()

    {

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.WritePacked((int)RPC_Types.SetKillCount);

        sender.Writer.Write(HaveKillCount);

    }



    public void SendRPC_MagicKill()

    {

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.WritePacked((int)RPC_Types.MagicKill);

        sender.Writer.Write(HaveKillCount);

    }



    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)

    {
        if (rpcType != CustomRPC.CustomRoleSync) return;

        switch ((RPC_Types)reader.ReadPackedInt32())

        {

            case RPC_Types.AddMagicTarget:

                byte targetId = reader.ReadByte();

                if (!MagicTarget.Contains(targetId))

                    MagicTarget.Add(targetId);

                MagicCount = reader.ReadInt32();

                break;

            case RPC_Types.SetKillCount:

                HaveKillCount = reader.ReadInt32();

                break;

            case RPC_Types.MagicKill:

                MagicTarget.Clear();

                HaveKillCount = reader.ReadInt32();

                break;

        }

    }



    enum RPC_Types

    {

        AddMagicTarget,

        SetKillCount,

        MagicKill

    }



    public override void CheckWinner(GameOverReason reason)

    {



    }

    int magiccount;

}

