using UnityEngine;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;
public sealed class EvilHacker : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilHacker),
            player => new EvilHacker(player),
            CustomRoles.EvilHacker,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpSpecial + 300,
            //(int)Options.offsetId.ImpY + 2000,
            SetUpOptionItem,
            "イビルハッカー",
            assignInfo: new(CustomRoles.EvilHacker, CustomRoleTypes.Impostor)
            {
                IsInitiallyAssignableCallBack = () => (MapNames)Main.NormalOptions.MapId is not MapNames.Fungle
            }
        );
    public EvilHacker(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        playerId = player.PlayerId;
        KillCooldown = OptionKillCooldown.GetFloat();
        AdminCooldown = OptionAdminCooldown.GetFloat();
    }
    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionAdminCooldown;
    private static float KillCooldown;
    private static float AdminCooldown;
    // 切断の後も足止めを外せるよう、PlayerControl を経ずに引く
    private readonly byte playerId;
    public float CalculateKillCooldown() => KillCooldown;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = AdminCooldown;
        // 戻ってから少しして姿が出るようにし、アドミンの前で姿が出ないようにする
        AURoleOptions.PhantomDuration = WarpDelay + StayTime + 0.5f;
    }
    enum OptionName
    {
        EvilHackerAdminCooldown,
    }
    private static void SetUpOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 30f, false)
                .SetValueFormat(OptionFormat.Seconds);
        OptionAdminCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.EvilHackerAdminCooldown, new(2.5f, 180f, 2.5f), 30f, false)
                .SetValueFormat(OptionFormat.Seconds);
    }
    // アドミンの前の位置。Dleks はスケルドを左右に反転した形なので x を反転する。
    // ファングルは IsInitiallyAssignableCallBack で配らない。
    private static Vector2? GetAdminPosition()
    {
        return (MapNames)Main.NormalOptions.MapId switch
        {
            MapNames.Skeld => new Vector2(2.96f, -8.62f),
            MapNames.Dleks => new Vector2(-2.96f, -8.62f),
            MapNames.MiraHQ => new Vector2(22.22f, 18.77f),
            MapNames.Polus => new Vector2(22.63f, -21.75f),
            MapNames.Airship => new Vector2(-22.13f, 0.56f),
            _ => null,
        };
    }
    const float WarpDelay = 2.5f;
    const float StayTime = 2.5f;
    // ワープの最中に会議・死亡・試合の終わりが来たら、この番号を進めて残りの LateTask を捨てる
    private int warpId;
    // 続けてよいか。死亡や試合の終わりで止まる時は、ここで足止めを外す
    private bool CanContinueWarp(int id)
    {
        if (id != warpId) return false;
        if (Player != null && Player.IsAlive() && GameStates.IsInTask) return true;
        warpId++;
        EndWarp();
        return false;
    }

    public override bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown)
    {
        if (GetAdminPosition() is not Vector2 adminPosition) return true;

        var id = ++warpId;
        // SnapTo は transform の位置で受けるので、足元 (GetTruePosition) ではなく transform の位置で戻す
        var lastPosition = (Vector2)Player.transform.position;
        Freeze();

        _ = new LateTask(() =>
        {
            if (!CanContinueWarp(id)) return;
            if (!Player.SnapToTeleport(adminPosition))
            {
                EndWarp();
                return;
            }
            _ = new LateTask(() =>
            {
                if (!CanContinueWarp(id)) return;
                Player.SnapToTeleport(lastPosition, force: true);
                EndWarp();
                Player.RpcResetAbilityCooldown();
            }, StayTime, "EvilHacker.Return");
        }, WarpDelay, "EvilHacker.Warp");
        return true;
    }
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => CancelWarp();
    public override void OnStartMeeting() => CancelWarp();
    // 会議の後は全員が出現位置へ戻るので、元の位置へは戻さず足止めだけ外す
    private void CancelWarp()
    {
        warpId++;
        EndWarp();
    }
    private void Freeze()
    {
        TownOfHostL.Modules.PlayerSpeed.Freeze(playerId, "EvilHacker");
        Player.MarkDirtySettings();
    }
    private void EndWarp()
    {
        if (!TownOfHostL.Modules.PlayerSpeed.Has(playerId, "EvilHacker")) return;
        TownOfHostL.Modules.PlayerSpeed.Remove(playerId, "EvilHacker");
        if (Player != null) Player.MarkDirtySettings();
    }
}