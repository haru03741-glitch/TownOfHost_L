using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Linq;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Modules.Net;
namespace TownOfHostL;

static class PlayerOutfitExtension
{
    public static NetworkedPlayerInfo.PlayerOutfit Set(this NetworkedPlayerInfo.PlayerOutfit instance, string playerName, int colorId, string hatId, string skinId, string visorId, string petId, string namePlateId = "")
    {
        instance.PlayerName = playerName;
        instance.ColorId = colorId;
        instance.HatId = hatId;
        instance.SkinId = skinId;
        instance.VisorId = visorId;
        instance.PetId = petId;
        if (namePlateId != "")
        {
            instance.NamePlateId = namePlateId;
        }
        return instance;
    }
    public static bool Compare(this NetworkedPlayerInfo.PlayerOutfit instance, NetworkedPlayerInfo.PlayerOutfit targetOutfit)
    {
        return instance.ColorId == targetOutfit.ColorId &&
                instance.HatId == targetOutfit.HatId &&
                instance.SkinId == targetOutfit.SkinId &&
                instance.VisorId == targetOutfit.VisorId &&
                instance.PetId == targetOutfit.PetId;

    }
    public static string GetString(this NetworkedPlayerInfo.PlayerOutfit instance)
    {
        return $"{instance.PlayerName} Color:{instance.ColorId} {instance.HatId} {instance.SkinId} {instance.VisorId} {instance.PetId}";
    }
}
public static partial class Camouflage
{
    public static NetworkedPlayerInfo.PlayerOutfit CamouflageOutfit = new NetworkedPlayerInfo.PlayerOutfit().Set("", 15, "", "", "", "");

    public static bool IsCamouflage;
    public static Dictionary<byte, NetworkedPlayerInfo.PlayerOutfit> PlayerSkins = new();

    [GameModuleInitializer]
    public static void Init()
    {
        IsCamouflage = false;
        PlayerSkins.Clear();
    }
    public static void CheckCamouflage()
    {
        if (!Options.CommsCamouflage.GetBool() || Options.IsSyncColorMode) return;

        var oldIsCamouflage = IsCamouflage;

        IsCamouflage = Utils.IsActive(SystemTypes.Comms);

        if (oldIsCamouflage != IsCamouflage)
        {
            foreach (var pc in Main.AllPlayerControls)
            {
                if (!pc.Is(Roles.Core.CustomRoles.Rainbow))
                    RpcSetSkin(IsCamouflage, pc, CamouflageOutfit);

                // バニラのペットバグの対応
                if (!IsCamouflage && !pc.IsAlive()) pc.RpcSetPet("");
            }
            Utils.NotifyRoles(NoCache: true);
        }
    }
    public static void RpcSetSkin(bool isCamouflage, PlayerControl target, NetworkedPlayerInfo.PlayerOutfit camouflageOutfit = null, bool ForceRevert = false)
    {
        if (!(Options.CommsCamouflage.GetBool() || Roles.Core.CustomRoles.EvilDyer.IsEnable()) || Options.IsSyncColorMode) return;
        if (target == null) return;

        var id = target.PlayerId;

        if (isCamouflage)
        {
            //コミュサボ中

            //死んでいたら処理しない
            if (PlayerState.GetByPlayerId(id).IsDead) return;
        }

        var newOutfit = camouflageOutfit;
        if (newOutfit == null) newOutfit = CamouflageOutfit;

        if (!isCamouflage || ForceRevert)
        {
            //コミュサボ解除または強制解除
            // 既定の外見は変身中でも本人の物に戻す。変身の見た目は本編の Shapeshift が持ち、解除した時に既定の外見へ戻る
            if (!PlayerSkins.TryGetValue(id, out newOutfit)) return;
        }
        if (newOutfit == null) return;
        if (OutfitSync.Apply(target, newOutfit))
            Logger.Info($"newOutfit={newOutfit.GetString()}", "RpcSetSkin");
    }
}

public static class SkinChangeMode
{
    // 全員を同じ外見にする間は、レベルからも見分けが付かないように 49 にそろえ、元に戻す時に返す
    const uint HiddenLevel = 49;
    static readonly Dictionary<byte, uint> OriginalLevels = new();

    [GameModuleInitializer]
    public static void Init() => OriginalLevels.Clear();

    public static void RpcSetSkin(PlayerControl target, PlayerControl Changed)
    {
        if (!Options.IsSyncColorMode || target == null || Changed == null) return;
        if (!Camouflage.PlayerSkins.TryGetValue(Changed.PlayerId, out var newOutfit)) return;
        Logger.Info("変更先：" + target.GetRealName() + " / 変身：" + Changed.GetRealName(), "RpcSetSkin");

        var data = target.Data;
        if (target == Changed)
        {
            if (OriginalLevels.Remove(target.PlayerId, out var level)) data.PlayerLevel = level;
        }
        else
        {
            OriginalLevels.TryAdd(target.PlayerId, data.PlayerLevel);
            data.PlayerLevel = HiddenLevel;
        }

        OutfitSync.Apply(target, newOutfit);

        target.SetName(newOutfit.PlayerName);
        if (target == PlayerControl.LocalPlayer)
            Main.nickName = newOutfit.PlayerName;
    }
    public static void ChangeSkin()
    {
        if (!Options.IsSyncColorMode) return;

        List<PlayerControl> changePlayers = new();
        Main.AllPlayerControls.Do(pc => changePlayers.Add(pc));
        changePlayers = changePlayers.OrderBy(a => Guid.NewGuid()).ToList();

        int selectCount = 0;
        switch (Options.GetSyncColorMode())
        {
            case SyncColorMode.Twin:
                selectCount = (Main.AllPlayerControls.Count() + 1) / 2;
                break;
            default:
                selectCount = (int)Options.GetSyncColorMode();
                break;
        }

        var selects = new PlayerControl[selectCount];
        for (int i = 0; i < Main.AllPlayerControls.Count(); i++)
        {
            if (i < selectCount)
            {
                selects[i] = changePlayers[i];
                Logger.Info($"選定先{i}：{selects[i].GetRealName()}", "ChangeSkin");
            }

            RpcSetSkin(changePlayers[i], selects[i % selectCount]);
        }
    }
}
