using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using TownOfHostL.Attributes;
using static TownOfHostL.Options;

namespace TownOfHostL.Roles.AddOns.Common;

public static class Revealer
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.Revealer,
        AddOnGroup.Buff,
        "#e478ff",
        "Rv",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        chatCommand: "リヴェラー",
        assignAtRandom: true);

    private static readonly int Id = (int)offsetId.AddonBuff + 1300;
    private static List<byte> playerIdList = new();
    private static HashSet<byte> SeenIdList = new();
    private static HashSet<byte> pendingRevealList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Revealer);
    }
    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
        SeenIdList = new();
        pendingRevealList = new();
    }
    public static void OnStartMeeting()
    {
        foreach (var id in pendingRevealList)
            SeenIdList.Add(id);
        pendingRevealList.Clear();
    }
    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId))
            playerIdList.Add(playerId);
    }

    /// <param name="showOnExileScreen">追放の画面に追放者の名前が出る時だけ true。暗転対策で追放者を伏せた時は名前を書き換えない</param>
    public static void OnExiled(PlayerControl pc, bool showOnExileScreen)
    {
        if (pc == null || !pc.Is(CustomRoles.Revealer)) return;
        pendingRevealList.Add(pc.PlayerId);
        if (!showOnExileScreen) return;

        string exiledRole = Utils.GetRoleName(pc.GetCustomRole());
        string exiledName = pc.GetRealName(true);

        if (pc == PlayerControl.LocalPlayer && Main.nickName != "") exiledName = Main.nickName;

        // 追放の画面に出す名前。会議の終わりに暗転対策が PlayerInfo を送り直しても戻らないよう、PlayerInfo の名前も揃える
        string revealName = string.Format(Translator.GetString("RevealerExiledText"), exiledRole, exiledName);
        GameDataSerializePatch.SharedNameOverride[pc.PlayerId] = revealName;
        pc.RpcSetName(revealName);
        // 戻す時は覚え書きを捨てて NotifyRoles に組み直させる (素の名前を全員へ送ると、見る人ごとの名前が消える)
        byte id = pc.PlayerId;
        _ = new LateTask(() =>
        {
            GameDataSerializePatch.SharedNameOverride.Remove(id);
            ChatCommands.ForgetNotifiedNames(pc);
        }, 8f, "SetName");
    }

    public static void OverrideDisplayRoleNameAsSeen(PlayerControl seen, bool isMeeting, ref bool enabled, ref string roleText)
    {
        if (!seen.Is(CustomRoles.Revealer) || !SeenIdList.Contains(seen.PlayerId)) return;

        enabled = true;
    }


    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
}