using System.Collections.Generic;

using System.Linq;

using AmongUs.GameOptions;

using HarmonyLib;

using UnityEngine;

using Hazel;



using TownOfHostL.Patches.ISystemType;

using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;



namespace TownOfHostL.Roles.Impostor;



public sealed class EvilBlender : RoleBase, IImpostor, IUsePhantomButton

{

    // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(EvilBlender),
            player => new EvilBlender(player),
            CustomRoles.EvilBlender,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 3800,
            SetupOptionItem,
            "Eb"
        );

    public EvilBlender(PlayerControl player)

    : base(

        RoleInfo,

        player

    )

    {

        PlayerRooms = new();

        IsUsed = false;

        UseingId = byte.MaxValue;

        IsLeft = false;

        sendtimer = 0;

        limittimer = 0;

        CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);

        CustomRoleManager.OnFixedUpdateOthers.Add(OtherFixUpdates);



        KillCooldown = OptionKillCoolDown.GetFloat();

        SabotageCooldown = OptionSabotageCoolDown.GetFloat();

        SabotageLimittime = OptionSabotageLimittime.GetFloat();

        flashtimer = 0;

        evilBlender = null;

    }

    static byte UseingId;//誰かが仕様中か(全員/Id)

    bool IsUsed;//使用済みか否か(個人)

    static bool IsLeft; static EvilBlender evilBlender;//途中で落ちたか

    float limittimer; float sendtimer; float flashtimer;

    static Dictionary<byte, SystemTypes?> PlayerRooms = new();

    static OptionItem OptionKillCoolDown; static float KillCooldown;

    static OptionItem OptionSabotageCoolDown; static float SabotageCooldown;

    static OptionItem OptionSabotageLimittime; static float SabotageLimittime;

    enum OptionName

    {

        EvilBlenderSabotageLimittime

    }



    static void SetupOptionItem()

    {

        OptionKillCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0, 180, 0.5f), 20f, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionSabotageCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0, 180, 0.5f), 30, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionSabotageLimittime = FloatOptionItem.Create(RoleInfo, OptionName.EvilBlenderSabotageLimittime, new(10, 90, 1), 45, false)

            .SetValueFormat(OptionFormat.Seconds);

    }

    bool IUsePhantomButton.IsPhantomRole => IsUsed is false;

    public override bool CanUseAbilityButton() => !IsUsed;

    float IKiller.CalculateKillCooldown() => KillCooldown;

    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.PhantomCooldown = SabotageCooldown;



    public override bool CancelReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, ref DontReportReason reason)

    {

        if (target == null && UseingId is not byte.MaxValue)

        {

            reason = DontReportReason.Other;

            return true;

        }

        return false;

    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)

    {

        if (UseingId is not byte.MaxValue)

        {

            UseingId = byte.MaxValue;

            Main.AllPlayerControls.DoIf(pc => pc.IsModClient(), pc => SendPublicRpc(pc.PlayerId, 0, null));

        }

        UseingId = byte.MaxValue;

        PlayerRooms.Clear();

        limittimer = 0;

        IsLeft = false;

    }

    public override bool OnSabotage(PlayerControl player, SystemTypes systemType) => UseingId is byte.MaxValue;

    public override void AfterSabotage(SystemTypes systemType) => Player.RpcResetAbilityCooldown();



    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)

    {

        AdjustKillCooldown = true;

        ResetCooldown = false;



        if (UseingId is not byte.MaxValue)

        {

            return;

        }

        if (IsUsed)

        {

            ResetCooldown = null;

            return;

        }

        if (GameStates.CalledMeeting)

        {

            return;

        }



        if (Utils.IsActive(SystemTypes.Reactor)

        || Utils.IsActive(SystemTypes.Electrical)

        || Utils.IsActive(SystemTypes.Laboratory)

        || Utils.IsActive(SystemTypes.Comms)

        || Utils.IsActive(SystemTypes.LifeSupp)

        || Utils.IsActive(SystemTypes.HeliSabotage))

        {

            return;

        }



        // サボタージュ発生!!

        UseingId = Player.PlayerId;

        IsUsed = true;

        Main.IsActiveSabotage = true;

        Main.SabotageType = SystemTypes.Hallway;

        Main.LastSab = Player.PlayerId;

        Main.SabotageActivetimer = 0;

        limittimer = 0;

        sendtimer = 0;

        Utils.AddGameLog($"Sabotage", string.Format(Translator.GetString("Log.Sabotage"), Player.ColoredName(), GetString("EvilBlender_Sabotage")));



        foreach (var player in Main.AllPlayerControls)

        {

            if (player.IsModClient())

            {

                SendPublicRpc(player.PlayerId, 0, null);

            }

            if (player.IsAlive() is false) continue;



            List<SystemTypes> rooms = new();

            ShipStatus.Instance.AllRooms.Where(room => room?.RoomId is not null and not SystemTypes.Hallway)

            .Do(r => rooms.Add(r.RoomId));



            var nowroom = player.GetPlainShipRoom();

            if (nowroom is not null)

            {

                rooms.Remove(nowroom.RoomId);

            }



            var rand = IRandom.Instance;

            var Room = rooms[rand.Next(0, rooms.Count)];



            if (PlayerRooms.TryAdd(player.PlayerId, Room) is false)

            {

                Logger.Error($"{player.PlayerId}が既に追加済み", "EvilBlender");

            }

            Logger.Info($"{player.Data?.PlayerName} => {Room}", "EvilBlender");

            if (player.IsModClient()) SendPublicRpc(player.PlayerId, 1, Room);

        }

        Utils.AllPlayerKillFlash();

        Utils.NotifyRoles(true);

    }

    public static void OtherFixUpdates(PlayerControl player)

    {

        if (UseingId == byte.MaxValue) return;

        if (AmongUsClient.Instance.AmHost)

        {

            if (IsLeft && player.PlayerId == PlayerControl.LocalPlayer.PlayerId)

            {

                evilBlender?.SabotageCheck();

            }

            return;

        }

        if (player.PlayerId == PlayerControl.LocalPlayer.PlayerId)

        {

            if (Utils.GetPlayerById(UseingId).GetRoleClass() is EvilBlender EvilBlender)

            {

                EvilBlender.limittimer += Time.fixedDeltaTime;

            }

        }

    }

    public override void OnPlayerLeft(byte playerId)

    {

        if (playerId == Player.PlayerId && UseingId == playerId)

        {

            IsLeft = true;

            evilBlender = this;

        }

    }

    public override void OnFixedUpdate(PlayerControl player)

    {

        if (AmongUsClient.Instance.AmHost is false) return;



        //自視点が使用中

        if (UseingId != Player.PlayerId) return;



        SabotageCheck();

    }

    void SabotageCheck()

    {

        var i = 0;

        ICollection<byte> dellist = [];

        foreach (var data in PlayerRooms)

        {

            var pc = Utils.GetPlayerById(data.Key);



            if (pc.IsAlive() is false)//死亡済みなら消す

            {

                dellist.Add(data.Key);

                if (pc.IsModClient()) SendPublicRpc(pc.PlayerId, 2, null);

                continue;

            }



            var nowroom = pc.GetPlainShipRoom();

            if (nowroom?.RoomId != null)

            {

                if (nowroom.RoomId == data.Value)

                {

                    dellist.Add(data.Key);



                    Logger.Info($"Complete:{pc.Data?.PlayerName}", "EvilBlender");

                    if (pc.IsModClient()) SendPublicRpc(pc.PlayerId, 2, null);

                    continue;

                }

            }

            if (SabotageLimittime < limittimer)//時間切れ

            {

                if (pc.inVent) pc.MyPhysics.RpcBootFromVent(RoleHooks.NowVentId.TryGetValue(pc.PlayerId, out var ventid) ? ventid : 0);

                CustomRoleManager.OnCheckMurder(Player, pc, pc, pc, true, true, 10, CustomDeathReason.Suffocation);

                i++;



                continue;

            }

        }

        dellist.Do(id => PlayerRooms.Remove(id));



        if (SabotageLimittime < limittimer)

        {

            UseingId = byte.MaxValue;

            PlayerRooms.Clear();

            limittimer = 0;

            IsLeft = false;

            Main.IsActiveSabotage = false;

            Main.SabotageType = SystemTypes.Hallway;

            Main.LastSab = byte.MaxValue;

            Main.SabotageActivetimer = 0;

            Utils.NotifyRoles();

            Main.AllPlayerControls.DoIf(p => p.IsModClient(), p => SendPublicRpc(p.PlayerId, 0, null));

            return;

        }



        limittimer += Time.fixedDeltaTime;

        sendtimer += Time.fixedDeltaTime;

        flashtimer += Time.fixedDeltaTime;

        if (1 < sendtimer)

        {

            Utils.NotifyRoles();

            sendtimer = 0;

        }

        if (5 < flashtimer)

        {

            Utils.AllPlayerKillFlash();

            flashtimer = 0;

        }

    }



    public string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (seen != seer) return "";

        if (isForMeeting) return "";



        if (UseingId == (Player?.PlayerId ?? 100))

        {

            if (PlayerRooms.TryGetValue(seer.PlayerId, out var room))

                return string.Format(GetString("EvilBlender_SabotageLowerAlive"), (int)(SabotageLimittime - limittimer), GetString($"{room}"));



            return string.Format(GetString("EvilBlender_SabotageLower"), (int)(SabotageLimittime - limittimer));

        }

        return "";

    }



    //flug 1 : playerroomの追加　2 : playerroomの削除

    void SendPublicRpc(byte playerid, int flug, SystemTypes? room)

    {

        MessageWriter writer = RPC.RpcPublicRoleSync(playerid, CustomRoles.EvilBlender);

        writer.Write(flug);

        switch (flug)

        {

            case 0:

                writer.Write(UseingId);

                break;

            case 1:

                if (room.HasValue is false) return;

                writer.Write((byte)room.Value);

                break;

        }

        AmongUsClient.Instance.FinishRpcImmediately(writer);

    }

    public static void ReceivePublickRpc(MessageReader reader)

    {

        switch (reader.ReadInt32())

        {

            case 0:

                UseingId = reader.ReadByte();

                if (UseingId == byte.MaxValue) break;

                if (Utils.GetPlayerById(UseingId)?.GetRoleClass() is EvilBlender EvilBlender)

                {

                    EvilBlender.limittimer = 0;

                }

                break;

            case 1:

                var roombyte = reader.ReadByte();

                PlayerRooms.Add(PlayerControl.LocalPlayer.PlayerId, (SystemTypes)roombyte);

                break;

            case 2:

                PlayerRooms.Remove(PlayerControl.LocalPlayer.PlayerId);

                break;

        }

    }

    void IKiller.OnMurderPlayerAsKiller(MurderInfo info)

    {


    }

    public override string GetAbilityButtonText()

    {

        return GetString("EvilBlender_AbilityButton"); ;

    }


}

