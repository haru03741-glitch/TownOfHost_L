using AmongUs.GameOptions;

using TownOfHostL.Roles;
using TownOfHostL.Roles.Core;

namespace TownOfHostL;

/// <summary>かくれんぼ専用の役職 (狐・荒らし) で遊ぶ時の割り当て。</summary>
static class HideAndSeekRoleAssign
{
    public static void Assign()
    {
        //CustomRpcSenderとRpcSetRoleReplacerの初期化
        RpcSetRoleReplacer.StartReplace();

        RoleAssignManager.SelectAssignRoles();

        //バニラの役職割り当て
        SelectRolesPatch.AssignRolesNormal(null, 0);

        //MODの役職割り当て
        RpcSetRoleReplacer.Release(); //保存していたSetRoleRpcを一気に書く

        // GetRoleTypePlayers が本編の役職から既定の役職 (Impostor / Crewmate) を入れるので、色分けはその後に行う。
        // 狐と荒らしの色は、この後の割り当ての中で塗り直す
        var roleTypePlayers = SelectRolesPatch.GetRoleTypePlayers();
        SetColorPatch.IsAntiGlitchDisabled = true;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.Is(CustomRoleTypes.Impostor))
                pc.RpcSetColor(0);
            else if (pc.Is(CustomRoleTypes.Crewmate))
                pc.RpcSetColor(1);
        }

        //役職設定処理
        if (roleTypePlayers.TryGetValue(RoleTypes.Crewmate, out var list))
        {
            SelectRolesPatch.AssignCustomRolesFromList(CustomRoles.HASFox, list);
            SelectRolesPatch.AssignCustomRolesFromList(CustomRoles.HASTroll, list);
        }
        foreach (var pair in PlayerState.AllPlayerStates)
        {
            //RPCによる同期
            ExtendedPlayerControl.RpcSetCustomRole(pair.Key, pair.Value.GetNowMainRole());
        }
        //色設定処理
        SetColorPatch.IsAntiGlitchDisabled = true;

        GameEndChecker.SetPredicateToHideAndSeek();

        Utils.CountAlivePlayers(true);
        Utils.SyncAllSettings();
        SetColorPatch.IsAntiGlitchDisabled = false;
    }
}