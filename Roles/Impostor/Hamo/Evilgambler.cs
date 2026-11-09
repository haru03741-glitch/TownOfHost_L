using AmongUs.GameOptions;



using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;



namespace TownOfHostL.Roles.Impostor;



public sealed class Evilgambler : RoleBase, IImpostor

{

    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

            SimpleRoleInfo.Create(
            typeof(Evilgambler),
            player => new Evilgambler(player),
            CustomRoles.Evilgambler,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 3900,
            SetupOptionItem,
            "evg"
        );

    public Evilgambler(PlayerControl player)

    : base(

    RoleInfo,

    player

    )

    {

        gamblecollect = OptionGamblecollect.GetInt();

        collectkillCooldown = OptionCollectkillCooldown.GetFloat();

        notcollectkillCooldown = OptionNotcollectkillCooldown.GetFloat();

        spcount = 0;

        l1flug = true;

    }



    private static OptionItem OptionGamblecollect;

    private static OptionItem OptionCollectkillCooldown;

    private static OptionItem OptionNotcollectkillCooldown;

    enum OptionName

    {

        Evillgamblergamblecollect,

        EvillgamblercollectkillCooldown,

        EvillgamblernotcollectkillCooldown,

    }



    private static float gamblecollect;

    private static float collectkillCooldown;

    private static float notcollectkillCooldown;



    public bool CanBeLastImpostor { get; } = false;

    private static void SetupOptionItem()

    {

        OptionGamblecollect = FloatOptionItem.Create(RoleInfo, OptionName.Evillgamblergamblecollect, new(0, 100, 5), 50, false)

            .SetValueFormat(OptionFormat.Percent);

        OptionCollectkillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.EvillgamblercollectkillCooldown, new(0f, 180f, 0.5f), 2.5f, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionNotcollectkillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.EvillgamblernotcollectkillCooldown, new(0f, 180f, 0.5f), 50.0f, false)

            .SetValueFormat(OptionFormat.Seconds);

    }

    public void OnCheckMurderAsKiller(MurderInfo info)

    {

        if (!info.IsSuicide)

        {

            (var killer, var target) = info.AttemptTuple;

            int chance = IRandom.Instance.Next(1, 101);

            if (chance < gamblecollect)

            {//gamble成功

                Logger.Info($"{killer?.Data?.PlayerName}:${chance}成功", "Evilgamble");

                Main.AllPlayerKillCooldown[killer.PlayerId] = collectkillCooldown;

                killer.SyncSettings();//キルクール処理を同期

                spcount++;



                l1flug = false;

            }

            else

            {

                Logger.Info($"{killer?.Data?.PlayerName}:${chance}失敗", "Evilgamble");

                Main.AllPlayerKillCooldown[killer.PlayerId] = notcollectkillCooldown;

                killer.SyncSettings();//キルクール処理を同期

                spcount = -30;

            }

        }

    }

    public override void AfterMeetingTasks()

    {

        spcount = 0;

    }

    public override void CheckWinner(GameOverReason reason)

    {


    }

    int spcount;

    bool l1flug;


}