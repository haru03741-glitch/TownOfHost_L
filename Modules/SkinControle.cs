using System.Collections.Generic;
using System.Linq;
using TownOfHostL.Modules.Net;
using static TownOfHostL.Translator;

namespace TownOfHostL;

// ロビーで、設定で禁じた帽子・スキン・バイザー・ペットと、他の人と同じ帽子・スキンを外す。
// 帽子などの RPC は本人だけが送るものなので、ホストからは送らず、OutfitSync (PlayerInfo の serialize) で着せ替える。
public static class SkinControle
{
    const string NoHat = "hat_NoHat";
    const string NoSkin = "skin_None";
    const string NoVisor = "visor_EmptyVisor";
    const string NoPet = "pet_EmptyPet";

    // 顔を覆う帽子。手で並べた一覧なので、新しい帽子は入っていないことがある
    static readonly HashSet<string> FullFaceHat = new()
    {
        "hat_caiatl", "hat_erisMorn", "hat_hunter", "hat_maraSov", "hat_osiris", "hat_saint14", "hat_shaxx", "hat_titan",
        "hat_warlock", "hat_mareLwyd", "hat_schnapp", "hat_hl_fubuki", "hat_hl_gura", "hat_hl_korone", "hat_hl_marine",
        "hat_hl_mio", "hat_hl_moona", "hat_hl_okayu", "hat_hl_pekora", "hat_hl_risu", "hat_hl_watson", "hat_caitlin",
        "hat_enforcer", "hat_jinx", "hat_vi", "hat_Prototype", "hat_Rupert", "hat_ToppatHair", "hat_pk05_Ellie",
        "hat_pk05_Svenhat", "hat_AbominalHat", "hat_pk04_Vagabond",
    };

    static bool IsWorn(string id, string none) => !string.IsNullOrEmpty(id) && id != "missing" && id != none;

    // 確かめ待ちの人。入室した時は帽子・スキン・バイザー・ペットの RPC が続けて届くので、揃うのを待って 1 回だけ確かめる
    static readonly HashSet<byte> waiting = new();
    const float CheckDelay = 0.5f;

    // 客が帽子などを変えた時 (入室した時も含む) にホストで呼ぶ
    public static void Check(PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsLobby || !Options.SkinControle.GetBool()) return;
        if (target == null || target.AmOwner || !waiting.Add(target.PlayerId)) return;
        var id = target.PlayerId;
        _ = new LateTask(() =>
        {
            waiting.Remove(id);
            CheckNow(Utils.GetPlayerById(id));
        }, CheckDelay, "SkinControle.Check");
    }

    static void CheckNow(PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsLobby || !Options.SkinControle.GetBool()) return;
        if (target == null || target.Data == null || target.Data.Disconnected) return;
        var outfit = target.Data.DefaultOutfit;
        if (outfit == null) return;

        bool hat = (Options.NoHat.GetBool() && IsWorn(outfit.HatId, NoHat))
            || (Options.NoFullFaceHat.GetBool() && FullFaceHat.Contains(outfit.HatId));
        bool skin = Options.NoSkin.GetBool() && IsWorn(outfit.SkinId, NoSkin);
        bool visor = Options.NoVisor.GetBool() && IsWorn(outfit.VisorId, NoVisor);
        bool pet = Options.NoPet.GetBool() && IsWorn(outfit.PetId, NoPet);
        if (hat || skin || visor || pet)
        {
            Clear(target, hat, skin, visor, pet);
            Utils.SendMessageAutoSplit(string.Format(GetString("SkinControleProhibited"), PartNames(hat, skin, visor, pet)), true, sendTo: target.PlayerId);
            return;
        }

        bool dupHat = Options.NoDuplicateHat.GetBool() && IsWorn(outfit.HatId, NoHat) && WornByOthers(target, o => o.HatId == outfit.HatId);
        bool dupSkin = Options.NoDuplicateSkin.GetBool() && IsWorn(outfit.SkinId, NoSkin) && WornByOthers(target, o => o.SkinId == outfit.SkinId);
        if (dupHat || dupSkin)
        {
            Clear(target, dupHat, dupSkin, false, false);
            Utils.SendMessageAutoSplit(string.Format(GetString("SkinControleDuplicate"), PartNames(dupHat, dupSkin, false, false)), true, sendTo: target.PlayerId);
        }
    }

    static bool WornByOthers(PlayerControl target, System.Func<NetworkedPlayerInfo.PlayerOutfit, bool> same)
        => Main.AllPlayerControls.Any(pc => pc != null && pc.PlayerId != target.PlayerId && pc.Data?.DefaultOutfit is { } o && same(o));

    static string PartNames(bool hat, bool skin, bool visor, bool pet)
    {
        var parts = new List<string>();
        if (hat) parts.Add(GetString("SkinPartHat"));
        if (skin) parts.Add(GetString("SkinPartSkin"));
        if (visor) parts.Add(GetString("SkinPartVisor"));
        if (pet) parts.Add(GetString("SkinPartPet"));
        return string.Join(GetString("SkinPartDelimiter"), parts);
    }

    // /offhat などのコマンドからも呼ぶ。外した物の名前を返す (外す物が無ければ空)
    public static string Clear(PlayerControl target, bool hat, bool skin, bool visor, bool pet)
    {
        if (!AmongUsClient.Instance.AmHost || target == null || target.Data?.DefaultOutfit == null) return "";
        var current = target.Data.DefaultOutfit;
        hat &= IsWorn(current.HatId, NoHat);
        skin &= IsWorn(current.SkinId, NoSkin);
        visor &= IsWorn(current.VisorId, NoVisor);
        pet &= IsWorn(current.PetId, NoPet);
        if (!hat && !skin && !visor && !pet) return "";

        var outfit = new NetworkedPlayerInfo.PlayerOutfit().Set(current.PlayerName, current.ColorId,
            hat ? NoHat : current.HatId, skin ? NoSkin : current.SkinId, visor ? NoVisor : current.VisorId, pet ? NoPet : current.PetId);
        Logger.Info($"{current.PlayerName}: hat={hat} skin={skin} visor={visor} pet={pet} を外す", "SkinControle");
        OutfitSync.Apply(target, outfit);
        return PartNames(hat, skin, visor, pet);
    }

    // /offhat <色> など
    public static void ClearCommand(ChatCommandContext c, bool hat, bool skin, bool visor, bool pet)
    {
        if (!GameStates.IsLobby) return;
        var target = Utils.GetPlayerByColorName(c.Arg(1));
        if (target == null) return;
        var parts = Clear(target, hat, skin, visor, pet);
        if (parts != "") Utils.SendMessageAutoSplit(string.Format(GetString("SkinControleByHost"), parts), true, sendTo: target.PlayerId);
    }
}
