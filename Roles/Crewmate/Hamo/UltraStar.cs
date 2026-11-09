using TownOfHostL.Roles.Neutral;
using AmongUs.GameOptions;

using UnityEngine;



using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;



namespace TownOfHostL.Roles.Crewmate;



public sealed class UltraStar : RoleBase, IKiller, ISchrodingerCatOwner

{

    // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(UltraStar),
            player => new UltraStar(player),
            CustomRoles.UltraStar,
            () => OptionCanseeKillcooltime.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 36300,
            SetupOptionItem,
            "us",
            "#ffff8e"
        );

    public UltraStar(PlayerControl player)

    : base(

        RoleInfo,

        player

    )

    {
        CustomRoleManager.EveryoneNeedsSeenNamesOthers.Add(() => CanseeAllplayer);

        Speed = OptionSpeed.GetFloat();

        cankill = Optioncankill.GetBool();

        KillCool = Optionkillcool.GetFloat();

        PlayerColor = player.Data.DefaultOutfit.ColorId;

        CanseeAllplayer = OptionCanseeAllplayer.GetBool();

    }

    private static OptionItem OptionSpeed;

    private static OptionItem Optioncankill;

    private static OptionItem Optionkillcool;

    public static OptionItem OptionCheckKill;

    static OptionItem OptionCanseeKillcooltime;

    static OptionItem OptionCanseeAllplayer;

    enum OptionName

    {

        AddSpeed,

        UltraStarCankill,

        UltraStarcheckkill,

        UltraStarVentCanseekillcool,

        UltraStarCanseeallplayer

    }

    float colorchange;

    int PlayerColor;

    static bool CanseeAllplayer;

    private static float Speed;

    private static bool cankill;

    float KillCool;



    private static void SetupOptionItem()

    {

        OptionSpeed = FloatOptionItem.Create(RoleInfo, OptionName.AddSpeed, new(0f, 5f, 0.25f), 2.0f, false)

            .SetValueFormat(OptionFormat.Multiplier);

        OptionCanseeAllplayer = BooleanOptionItem.Create(RoleInfo, OptionName.UltraStarCanseeallplayer, false, false);

        Optioncankill = BooleanOptionItem.Create(RoleInfo, OptionName.UltraStarCankill, false, false);

        Optionkillcool = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false, Optioncankill)

            .SetValueFormat(OptionFormat.Seconds);

        OptionCheckKill = BooleanOptionItem.Create(RoleInfo, OptionName.UltraStarcheckkill, false, false, Optioncankill);

        OptionCanseeKillcooltime = BooleanOptionItem.Create(RoleInfo, OptionName.UltraStarVentCanseekillcool, false, false, Optioncankill);

    }



    public override void OnFixedUpdate(PlayerControl player)

    {

        //ホストじゃない or タスクターンじゃない or 生存していない ならブロック

        if (!AmongUsClient.Instance.AmHost || GameStates.Intro || !GameStates.IsInTask || !player.IsAlive() || GameStates.CalledMeeting || GameStates.ExiledAnimate || !MyState.HasSpawned) return;

        {//参考→https://github.com/Yumenopai/TownOfHost_Y/releases/tag/v514.20.3

            colorchange %= 18;

            if (colorchange is >= 0 and < 1) player.RpcSetColor(8);

            else if (colorchange is >= 1 and < 2) player.RpcSetColor(1);

            else if (colorchange is >= 2 and < 3) player.RpcSetColor(10);

            else if (colorchange is >= 3 and < 4) player.RpcSetColor(2);

            else if (colorchange is >= 4 and < 5) player.RpcSetColor(11);

            else if (colorchange is >= 5 and < 6) player.RpcSetColor(14);

            else if (colorchange is >= 6 and < 7) player.RpcSetColor(5);

            else if (colorchange is >= 7 and < 8) player.RpcSetColor(4);

            else if (colorchange is >= 8 and < 9) player.RpcSetColor(17);

            else if (colorchange is >= 9 and < 10) player.RpcSetColor(0);

            else if (colorchange is >= 10 and < 11) player.RpcSetColor(3);

            else if (colorchange is >= 11 and < 12) player.RpcSetColor(13);

            else if (colorchange is >= 12 and < 13) player.RpcSetColor(7);

            else if (colorchange is >= 13 and < 14) player.RpcSetColor(15);

            else if (colorchange is >= 14 and < 15) player.RpcSetColor(6);

            else if (colorchange is >= 15 and < 16) player.RpcSetColor(12);

            else if (colorchange is >= 16 and < 17) player.RpcSetColor(9);

            else if (colorchange is >= 17 and < 18) player.RpcSetColor(16);

            colorchange += Time.fixedDeltaTime * 1.5f;

        }

        if (cankill)

        {

            KillCool -= Time.fixedDeltaTime;

            Vector2 GSpos = player.transform.position;



            PlayerControl target = null;

            var KillRange = 0.4;



            foreach (var pc in Main.AllAlivePlayerControls)

            {

                if (pc.PlayerId != player.PlayerId)

                {

                    float targetDistance = Vector2.Distance(GSpos, pc.transform.position);

                    if (targetDistance <= KillRange && player.CanMove && pc.CanMove)

                    {

                        target = pc;

                        break;

                    }

                }

            }

            if (target != null && cankill && KillCool <= 0)

            {

                KillCool = Optionkillcool.GetFloat();

                CustomRoleManager.OnCheckMurder(Player, target, Player, target, force: false, Killpower: OptionCheckKill.GetBool() ? 1 : 2);

                Utils.MarkEveryoneDirtySettings();

                Player.RpcResetAbilityCooldown(sync: true);

            }

        }

    }

    public override void OnMurderPlayerAsTarget(MurderInfo info)

    {


    }

    public override bool CanClickUseVentButton => false;

    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => false;

    public override void ApplyGameOptions(IGameOptions opt)

    {

        AURoleOptions.EngineerCooldown = KillCool;

    }

    public override void OnSpawn(bool initialState = false)

    {

        if (cankill)

        {

            KillCool = Optionkillcool.GetFloat() + 1.5f;

            Player.RpcResetAbilityCooldown(sync: true);

        }

    }

    public override void OnReportDeadBody(PlayerControl _, NetworkedPlayerInfo __) => Player.RpcSetColor((byte)PlayerColor);

    public override bool ShowsRoleTo(PlayerControl seer, bool isMeeting) => CanseeAllplayer;

    public override void OnIntroEnd() => TownOfHostL.Modules.PlayerSpeed.Add(Player.PlayerId, nameof(UltraStar), Speed);



    // 試合の途中でウルトラスターになった時も速さを足す。役職が替わる時は OnDestroy で外す
    public override void OnAddedMidGame() => TownOfHostL.Modules.PlayerSpeed.Add(Player.PlayerId, nameof(UltraStar), Speed);
    public override void OnDestroy() => TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(UltraStar));



    public override string GetAbilityButtonText() => GetString(StringNames.KillLabel);



    public bool CanUseSabotageButton() => false;

    bool IKiller.CanUseImpostorVentButton() => false;

    bool IKiller.CanKill => false;

    bool IKiller.IsKiller => true;

    public SchrodingerCat.TeamType SchrodingerCatChangeTo => SchrodingerCat.TeamType.Crew;

    public override void CheckWinner(GameOverReason reason)

    {


    }


}