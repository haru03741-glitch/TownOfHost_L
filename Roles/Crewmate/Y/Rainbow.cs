using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL.Roles.Crewmate;
public sealed class Rainbow : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Rainbow),
            player => new Rainbow(player),
            CustomRoles.Rainbow,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 600,
            SetupOptionItem,
            "にじいろスター",
            "#ffff00"
        );
    public Rainbow(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        DontSeeTaskTurn = OptionDontSeeTaskTurn.GetBool();
    }

    private static OptionItem OptionDontSeeTaskTurn;
    enum OptionName
    {
        RainbowDontSeeTaskTurn,
    }

    private static bool DontSeeTaskTurn;

    float colorchange;

    private static void SetupOptionItem()
    {
        OptionDontSeeTaskTurn = BooleanOptionItem.Create(RoleInfo, 10, OptionName.RainbowDontSeeTaskTurn, false, false);
    }

    public override void Add()
    {
        int chance = IRandom.Instance.Next(0, 17);
        colorchange = chance;
        Player.SetNamePlate("nameplate_flagRainbow");
        CustomRpcSender.Create(name: $"RpcSetNamePlate({Player.Data.PlayerName})").AutoStartRpc(Player.NetId, (byte)RpcCalls.SetNamePlateStr)
            .Write("nameplate_flagRainbow")
            .Write(Player.GetNextRpcSequenceId(RpcCalls.SetNamePlateStr))
            .EndRpc()
            .SendMessage();
    }
    private static readonly int[] RainbowColors = { 8, 1, 10, 2, 11, 14, 5, 4, 17, 0, 3, 13, 7, 15, 6, 12, 9, 16 };
    private int lastSentColor = -1;

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInTask || !player.IsAlive()) return;

        colorchange %= 18;
        // 色が変わった時だけ送る (毎フレーム送ると同種 Reliable の連射で公式鯖に切断される)
        int color = RainbowColors[(int)colorchange];
        if (color != lastSentColor)
        {
            lastSentColor = color;
            player.RpcSetColor((byte)color);
        }
        colorchange += Time.fixedDeltaTime;
    }

    public override bool ShowsRoleColorToAll(bool isMeeting) => true;
    public override bool KeepsOwnNameInSyncColorMeeting => true;

    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, bool isMeeting, ref bool enabled, ref string roleText)
    {
        if (!(!isMeeting && DontSeeTaskTurn)) enabled = true;
        if (!isMeeting) roleText = Translator.GetString("RainbowResize");
    }
}