using System.Collections.Generic;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.AddOns.Crewmate;
using TownOfHostL.Roles.AddOns.Impostor;

namespace TownOfHostL.Roles.AddOns;

/// <summary>
/// 属性の登録の一覧。並びは設定のタブに出る順。属性を足す時はクラスに Info を持たせ、ここに 1 行足す。
/// 属性のクラスの static 初期化は、この一覧を初めて引いた時に走る。そこで役職の色や分類 (RoleCatalog) を引かないこと。
/// </summary>
public static class AddOnList
{
    private static AddOnInfo[] all;
    private static Dictionary<CustomRoles, AddOnInfo> byRole;
    private static bool building;

    public static AddOnInfo[] All { get { EnsureBuilt(); return all ?? System.Array.Empty<AddOnInfo>(); } }

    public static AddOnInfo Get(CustomRoles role)
    {
        EnsureBuilt();
        return byRole != null && byRole.TryGetValue(role, out var info) ? info : null;
    }

    private static void EnsureBuilt()
    {
        if (all != null) return;
        if (building)
        {
            // 組んでいる途中に属性のクラスの static 初期化から引かれた。ここで投げると一覧ごと使えなくなるので、その引きだけを空振りにする
            Logger.Error("属性の一覧を組んでいる途中で引かれました。属性のクラスの static 初期化で役職の色や分類を引かないでください", "AddOnList");
            return;
        }
        building = true;
        try
        {
            var list = new AddOnInfo[]
            {
                LastImpostor.Info,

                CompreteCrew.Info,
                Workhorse.Info,

                Lovers.Info,
                ChainShifterAddon.Info,

                AddLight.Info,
                Management.Info,
                AddWatch.Info,
                AddSeer.Info,
                Autopsy.Info,
                VIP.Info,
                Revenger.Info,
                Sending.Info,
                TieBreaker.Info,
                PlusVote.Info,
                Guarding.Info,
                AddBait.Info,
                Refusing.Info,
                Revealer.Info,
                Loyalty.Info,
                Guesser.Info,

                Sunglasses.Info,
                Clumsy.Info,
                InfoPoor.Info,
                NonReport.Info,
            };
            var map = new Dictionary<CustomRoles, AddOnInfo>(list.Length);
            foreach (var info in list)
                if (!map.TryAdd(info.Role, info))
                    Logger.Error($"属性 {info.Role} が二重に登録されています。後の方を捨てます", "AddOnList");
            byRole = map;
            all = list;
        }
        finally
        {
            building = false;
        }
    }

    /// <summary>設定のタブの、ある見出しの下に並ぶ属性の項目を作る</summary>
    public static void SetupOptions(AddOnGroup group)
    {
        foreach (var info in All)
            if (info.Group == group) info.SetupOptions?.Invoke();
    }
}
