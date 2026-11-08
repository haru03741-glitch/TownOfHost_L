using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Class;

namespace TownOfHostL.Roles.Crewmate;
public sealed class NiceGuesser : VoteGuesser
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(NiceGuesser),
            player => new NiceGuesser(player),
            CustomRoles.NiceGuesser,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 1800,
            SetupOptionItem,
            "ナイスゲッサー",
            "#ffff00"
        );
    public NiceGuesser(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        NumOfGuess = OptionNumOfGuess.GetInt();
        MultipleInMeeting = OptionMultipleInMeeting.GetBool();
        HideMisfire = OptionHideMisfire.GetBool();
        GuessAfterVote = OptionGuessAfterVote.GetBool();
    }
    private static OptionItem OptionNumOfGuess;
    private static OptionItem OptionMultipleInMeeting;
    private static OptionItem OptionHideMisfire;
    private static OptionItem OptionGuessAfterVote;
    enum OptionName
    {
        GuesserNumOfGuess,
        GuesserMultipleInMeeting,
        GuesserHideMisfire,
        GuesserGuessAfterVote,
    }
    public static void SetupOptionItem()
    {
        OptionNumOfGuess = IntegerOptionItem.Create(RoleInfo, OptionName.GuesserNumOfGuess, new(1, 15, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionMultipleInMeeting = BooleanOptionItem.Create(RoleInfo, OptionName.GuesserMultipleInMeeting, false, false);
        OptionHideMisfire = BooleanOptionItem.Create(RoleInfo, OptionName.GuesserHideMisfire, false, false);
        OptionGuessAfterVote = BooleanOptionItem.Create(RoleInfo, OptionName.GuesserGuessAfterVote, false, false);
    }
}