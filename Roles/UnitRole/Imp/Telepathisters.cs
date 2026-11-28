using System.Linq;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;
using static TownOfHostL.Utils;

namespace TownOfHostL.Roles.Impostor;

public sealed class Telepathisters : RoleBase, IImpostor, IKillFlashSeeable
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Telepathisters),
            player => new Telepathisters(player),
            CustomRoles.Telepathisters,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.UnitImp + 0,
            SetupOptionItem,
            "テレパシスターズ",
            tab: TabGroup.UnitRoles,
            assignInfo: new RoleAssignInfo(CustomRoles.Telepathisters, CustomRoleTypes.Impostor)
            {
                AssignCountRule = new(2, 3, 1),
            }
        );

    public Telepathisters(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        CanSeeKillFlash = OptionCanSeeKillFlash.GetBool();
        CanSeeLastRoomInMeeting = OptionCanSeeLastRoomInMeeting.GetBool();
        VentMaxCount = OptionVentMaxCount.GetInt();

        VentCountLimit = VentMaxCount;
        //ImpostorsIdはEvilTracker内で共有
        TelepathistersId.Clear();
        var playerId = player.PlayerId;
        foreach (var target in Main.AllAlivePlayerControls)
        {
            var targetId = target.PlayerId;
            if (targetId != playerId && target.Is(CustomRoles.Telepathisters))
            {
                TelepathistersId.Add(targetId);
                TargetArrow.Add(playerId, targetId);
            }
        }
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionCanSeeKillFlash;
    private static OptionItem OptionCanSeeLastRoomInMeeting;
    private static OptionItem OptionVentMaxCount;

    enum OptionName
    {
        TelepathistersCanSeeKillFlash,
        TelepathistersCanSeeLastRoomInMeeting,
        TelepathistersOptionVentCount
    }
    public static float KillCooldown;
    public static bool CanSeeKillFlash;
    public static bool CanSeeLastRoomInMeeting;
    public static int VentMaxCount;

    public bool CanSetTarget;
    private HashSet<byte> TelepathistersId = new(3);
    public static int VentCountLimit;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCanSeeKillFlash = BooleanOptionItem.Create(RoleInfo, OptionName.TelepathistersCanSeeKillFlash, true, false);
        OptionCanSeeLastRoomInMeeting = BooleanOptionItem.Create(RoleInfo, OptionName.TelepathistersCanSeeLastRoomInMeeting, true, false);
        OptionVentMaxCount = IntegerOptionItem.Create(RoleInfo, OptionName.TelepathistersOptionVentCount, new(1, 20, 1), 2, false)
            .SetValueFormat(OptionFormat.Times);
    }

    public float CalculateKillCooldown() => KillCooldown;
    public bool CanUseImpostorVentButton() => VentCountLimit > 0;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        // 死んだ後や会議中に届いたベントの電文では回数を減らさない
        if (VentCountLimit <= 0 || !Player.IsAlive() || GameStates.IsMeeting) return false;

        VentCountLimit--;
        //テレパシスターズのみ呼び出し
        TelepathistersId.Do(id => NotifyRoles(SpecifySeer: GetPlayerById(id)));
        return true;
    }

    public bool CheckKillFlash(MurderInfo info) // IKillFlashSeeable
    {
        if (!CanSeeKillFlash) return false;

        PlayerControl killer = info.AppearanceKiller, target = info.AttemptTarget;

        //シスターズによるキルかどうかの判別
        var realKiller = target.GetRealKiller() ?? killer;
        return realKiller.Is(CustomRoles.Telepathisters) && realKiller != target;
    }

    // 値取得の関数
    private bool IsTrackTarget(PlayerControl target)
        => Player.IsAlive() && target.IsAlive() && !Is(target)
        && target.Is(CustomRoles.Telepathisters);

    // 表示系の関数群
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (isForMeeting)
        {
            return GetLastRoom(seen);
        }
        else
        {
            return GetArrows(seen);
        }
    }
    private string GetArrows(PlayerControl seen)
    {
        if (!Is(seen)) return "";

        var trackerId = Player.PlayerId;

        // 死んだ仲間は飛ばすだけで一覧からは外さない (外すと生き返った後に矢印が戻らない)
        var aliveIds = TelepathistersId.Where(id => !PlayerState.GetByPlayerId(id).IsDead).ToArray();

        var sb = new StringBuilder(80);
        if (aliveIds.Length > 0)
        {
            sb.Append($"<color={GetRoleColorCode(CustomRoles.Impostor)}>");
            foreach (var impostorId in aliveIds)
            {
                sb.Append(TargetArrow.GetArrows(Player, impostorId));
            }
            sb.Append($"</color>");
        }
        return sb.ToString();
    }
    public string GetLastRoom(PlayerControl seen)
    {
        if (!(CanSeeLastRoomInMeeting && IsTrackTarget(seen))) return "";

        string text = ColorString(Palette.ImpostorRed, TargetArrow.GetArrows(Player, seen.PlayerId));
        var room = PlayerState.GetByPlayerId(seen.PlayerId).LastRoom;
        if (room == null) text += ColorString(Color.gray, "@" + GetString("FailToTrack"));
        else
        {
            text += ColorString(Palette.ImpostorRed, "@" + GetString(room.RoomId.ToString()));
        }

        return text;
    }

    public override string GetProgressText(bool comms = false)
    {
        int count;
        if (VentCountLimit < 0) count = 0;
        else count = VentCountLimit;
        return ColorString(count > 0 ? Palette.ImpostorRed : Color.gray, $"[{count}]");
    }

}