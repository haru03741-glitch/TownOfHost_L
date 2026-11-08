using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Class;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;
public sealed class EvilGuesser : VoteGuesser, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(EvilGuesser),
            player => new EvilGuesser(player),
            CustomRoles.EvilGuesser,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 1400,
            SetupOptionItem,
            "イビルゲッサー"
        );
    public EvilGuesser(PlayerControl player)
    : base(
        RoleInfo,
        player)
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