using AmongUs.GameOptions;
using Hazel;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

public sealed class Reloader : RoleBase, IImpostor, IUsePhantomButton
{
    // 役職の元: Revolutionary Host Roles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Reloader),
            player => new Reloader(player),
            CustomRoles.Reloader,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 6500,
            SetupOptionItem,
            "rd"
        );
    public Reloader(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Cooldown = OptionCooldown.GetFloat();
        KillCooldown = OptionKillCooldown.GetFloat();
        ReloadKillCooldown = OptionReloadKillCooldown.GetFloat();
        Count = OptionCount.GetInt();
    }
    private static OptionItem OptionCooldown;
    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionReloadKillCooldown;
    private static OptionItem OptionCount;
    enum OptionName
    {
        ReloaderKillCooldown,
        ReloaderCount
    }
    private static float Cooldown;
    private static float KillCooldown;
    private static float ReloadKillCooldown;
    private int Count;
    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
                .SetValueFormat(OptionFormat.Seconds);
        OptionReloadKillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.ReloaderKillCooldown, new(0f, 180f, 0.5f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCount = IntegerOptionItem.Create(RoleInfo, OptionName.ReloaderCount, new(1, 15, 1), 2, false);
    }
    public bool UseOneclickButton => true;
    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.PhantomCooldown = Count > 0 ? Cooldown : 200f;
    public float CalculateKillCooldown() => KillCooldown;
    public override bool CanUseAbilityButton() => Count > 0;
    bool IUsePhantomButton.IsPhantomRole => Count > 0;
    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = true;
        if (Count <= 0) return;

        AdjustKillCooldown = false;
        Count--;
        SendRPC();
        Player.SetKillCooldown(ReloadKillCooldown, ForceProtect: true);
        Utils.NotifyRoles(SpecifySeer: Player);
    }
    public override string GetProgressText(bool comms = false) => Utils.ColorString(Count > 0 ? RoleInfo.RoleColor : Palette.DisabledGrey, $"({Count})");

    public override string GetAbilityButtonText()
    {
        return GetString("ReloaderAbilitytext");
    }
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != seer.PlayerId || isForMeeting || !(Count > 0) || !Player.IsAlive()) return "";

        if (isForHud) return GetString("PhantomButtonLowertext");
        return $"<size=50%>{GetString("PhantomButtonLowertext")}</size>";
    }

    public void SendRPC()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.Write(Count);
    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        Count = reader.ReadInt32();
    }

    bool IUsePhantomButton.IsresetAfterKill => false;
}
