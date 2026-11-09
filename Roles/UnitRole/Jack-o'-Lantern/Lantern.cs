using System.Collections.Generic;
using System.Linq;

using AmongUs.GameOptions;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Roles.Unit.JackOLantern;

namespace TownOfHostL.Roles.Crewmate;
public sealed class Lantern : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Lantern),
            player => new Lantern(player),
            CustomRoles.Lantern,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.UnitMix + 100,//使用しない
            null,
            "ランタン",
            "#e5a323",
            flags: RoleFlags.DontShowOption
        );
    public Lantern(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        visionDuringFixLight = OptionLanternVisionDuringFixLight.GetInt();
        roleReceivedExcludeNormalCrewmate = OptionLanternRoleReceivedExcludeNormalCrewmate.GetBool();

        fixLightVision = (Main.DefaultCrewmateVision * 5) * (visionDuringFixLight / 100f);
        Logger.Info($"{Player.GetNameWithRole()} : 停電時の視界率({fixLightVision / 5})", "Lantern");
    }

    static int visionDuringFixLight;
    static bool roleReceivedExcludeNormalCrewmate;

    static float fixLightVision; 

    public override void Add()
    {
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetFloat(FloatOptionNames.CrewLightMod, Main.DefaultCrewmateVision);

        // 停電時視界の緩和
        if (Utils.IsActive(SystemTypes.Electrical))
            opt.SetFloat(FloatOptionNames.CrewLightMod, fixLightVision);
    }

    public override bool OnCompleteTask()
    {
        if (Player.IsAlive() && IsTaskFinished)
        {
            /* タスク完了で死亡プレイヤーが持つクルー役職を付与する */
            // 死亡プレイヤーの役職をリストに一旦登録
            List<CustomRoles> roleList = new(15);

            foreach (var pc in Main.AllDeadPlayerControls)
            {
                // クルー陣営じゃないなら除く
                if (!pc.Is(CustomRoleTypes.Crewmate)) continue;

                var role = pc.GetCustomRole();
                // 通常クルーを除くなら除く
                if (roleReceivedExcludeNormalCrewmate && role == CustomRoles.Crewmate) continue;

                // 登録
                roleList.Add(role);
            }
            Logger.Info($"死亡者役職リスト：{string.Join(", ", roleList)}", "Lantern");

            // 登録数が0なら通常クルーを割り当てる
            if (roleList.Count == 0)
            {
                Player.RpcSetCustomRole(CustomRoles.Crewmate);
                Logger.Info($"{Player.GetNameWithRole()}：死亡者役職リストが0の為通常クルーを割り当てます", "Lantern");
            }
            else
            {
                var nextRole = roleList[IRandom.Instance.Next(roleList.Count)];
                SetNextCustomRole(Player, nextRole);
            }
        }
        return true;
    }

    // 今後移植？
    void SetNextCustomRole(PlayerControl player, CustomRoles nextRole)
    {
        Logger.Info($"{player.GetNameWithRole()}：死亡者役職リストから{nextRole}を割り当てます", "Lantern");
        var nextRoleBaseTypes = nextRole.GetRoleTypes();
        // 役職を替えてから、見え方を規則どおりに送る
        player.RpcSetCustomRole(nextRole);
        switch (nextRoleBaseTypes)
        {
            case RoleTypes.Crewmate:
                // なにもしない
                break;
            case RoleTypes.Scientist:
            case RoleTypes.Engineer:
            case RoleTypes.Tracker:
            case RoleTypes.Noisemaker:
                // 全員の画面で本人の役職だけを替える (本人の画面の他の人は変えない)
                RoleView.ApplyToEveryone(player);
                break;
            case RoleTypes.Impostor:
            case RoleTypes.Shapeshifter:
            case RoleTypes.Phantom:
                // ホストの手元では変身・消失のボタンに問題があるので、ホストだけ一旦インポスターにする
                RoleView.ApplyBasisChange(player, player.PlayerId == PlayerControl.LocalPlayer.PlayerId ? RoleTypes.Impostor : null);
                break;
        }

        //色表示
        NameColorManager.RemoveAll(player.PlayerId);

        PlayerGameOptionsSender.SetDirty(player.PlayerId);
        Utils.NotifyRoles(SpecifySeer: player);
    }

}
