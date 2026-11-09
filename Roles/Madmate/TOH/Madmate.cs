using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Madmate;
public sealed class Madmate : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Madmate),
            player => new Madmate(player),
            CustomRoles.Madmate,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.MadTOH + 0,
            SetupOptionItem,
            "マッドメイト",
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            flags: RoleFlags.FakeTasksLabel
        );
    /// <summary>タスクで目覚めるマッドメイト系の短い説明: タスクが終わるまではマッドメイトの「Before」の説明。
    /// タスクを持たない設定 (マッドゲッサーなど) では終わる時が来ないので、初めからマッドメイトの説明にする</summary>
    public static string TaskMadmateRoleKey(PlayerControl player, bool infoLong)
    {
        if (infoLong) return null;
        if (player.GetRoleClass() is { } role && role.HasTasks == HasTask.False) return "Madmate";
        return player.GetPlayerTaskState().IsTaskFinished ? "Madmate" : "BeforeMadmate";
    }
    public Madmate(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }

    public static void SetupOptionItem()
    {
        Options.SetUpAddOnOptions(RoleInfo.RoleName, RoleInfo.Tab);
    }
}
