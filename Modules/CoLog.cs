using System;
using System.Collections.Generic;
using System.Linq;
using TownOfHostL.Roles.Core;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL;

public static class CoLog
{
    private static readonly string[] VentCategory = { "CoCategory.Vent", "ベント系" };
    private static readonly string[] SeerCategory = { "CoCategory.Seer", "占い系" };

    private static readonly Dictionary<byte, (string PlayerName, string Text, Color Color)> RoleEntries = new();
    private static readonly Dictionary<byte, (string PlayerName, List<CustomRoles> Roles)> AddonEntries = new();

    public static void Reset()
    {
        RoleEntries.Clear();
        AddonEntries.Clear();
    }

    private static string Join(ChatCommandContext c) => string.Join(" ", c.Args.Skip(1).Where(a => a.Length > 0)).Trim();

    private static CustomRoles? FindByName(string input, Func<CustomRoles, bool> allowed)
    {
        input = input.Trim();
        if (input == "") return null;
        CustomRoles? prefix = null;
        int prefixCount = 0;
        foreach (CustomRoles role in Enum.GetValues(typeof(CustomRoles)))
        {
            if (role <= CustomRoles.NotAssigned || role.IsNotAssignedRoles() || !allowed(role)) continue;
            if (string.Equals(role.ToString(), input, StringComparison.OrdinalIgnoreCase)) return role;
            var name = Utils.GetRoleName(role).RemoveHtmlTags();
            if (string.Equals(name, input, StringComparison.OrdinalIgnoreCase)) return role;
            if (name.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                prefix = role;
                prefixCount++;
            }
        }
        return prefixCount == 1 ? prefix : null;
    }

    private static string FindCategory(string input)
    {
        input = input.Trim();
        if (input == "") return null;
        foreach (var cat in new[] { VentCategory, SeerCategory })
        {
            var name = GetString(cat[0]);
            if (name.Contains(input) || input.Contains(name) || cat[1].Contains(input) || input.Contains(cat[1])) return name;
        }
        return null;
    }

    private static bool CheckCanCo(PlayerControl player)
    {
        if (player == null) return false;
        if (GameStates.IsLobby || !GameStates.IsInGame)
        {
            Utils.SendMessage(GetString("Co.OnlyInGame"), player.PlayerId);
            return false;
        }
        if (player.Data == null || player.Data.IsDead)
        {
            Utils.SendMessage(GetString("Co.Dead"), player.PlayerId);
            return false;
        }
        return true;
    }

    private static string NameOf(PlayerControl player) => player.Data?.PlayerName ?? player.name;

    public static void Co(ChatCommandContext c)
    {
        var player = c.Player;
        var input = Join(c);
        if (input == "")
        {
            Utils.SendMessage(GetString("CommandUsage.Co"), player.PlayerId);
            return;
        }
        if (!CheckCanCo(player)) return;
        var role = FindByName(input, r => !r.IsAddOn());
        string text;
        Color color;
        if (role != null)
        {
            text = Utils.GetRoleName(role.Value);
            color = Utils.GetRoleColor(role.Value);
        }
        else
        {
            var cat = FindCategory(input);
            if (cat == null)
            {
                Utils.SendMessage(string.Format(GetString("Co.NotFound"), input), player.PlayerId);
                return;
            }
            text = cat;
            color = Color.white;
        }
        RoleEntries[player.PlayerId] = (NameOf(player), text, color);
        Logger.Info($"{NameOf(player)} CO {text}", "CoLog");
        Utils.SendMessage(string.Format(GetString("Co.Done"), NameOf(player), Utils.ColorString(color, text)), title: GetString("Co.Title"));
    }

    public static void Aco(ChatCommandContext c)
    {
        var player = c.Player;
        var input = Join(c);
        if (input == "")
        {
            Utils.SendMessage(GetString("CommandUsage.Aco"), player.PlayerId);
            return;
        }
        if (!CheckCanCo(player)) return;
        var role = FindByName(input, r => r.IsAddOn());
        if (role == null)
        {
            Utils.SendMessage(string.Format(GetString("Co.NotFound"), input), player.PlayerId);
            return;
        }
        if (!AddonEntries.TryGetValue(player.PlayerId, out var entry))
        {
            entry = (NameOf(player), new List<CustomRoles>());
            AddonEntries[player.PlayerId] = entry;
        }
        if (!entry.Roles.Contains(role.Value)) entry.Roles.Add(role.Value);
        Logger.Info($"{NameOf(player)} ACO {role.Value}", "CoLog");
        Utils.SendMessage(string.Format(GetString("Co.Done"), NameOf(player), Utils.ColorString(Utils.GetRoleColor(role.Value), Utils.GetRoleName(role.Value))), title: GetString("Co.AddonTitle"));
    }

    public static void List(ChatCommandContext c)
    {
        if (RoleEntries.Count == 0)
        {
            Utils.SendMessage(GetString("CoListEmpty"), c.Player.PlayerId);
            return;
        }
        var sb = new System.Text.StringBuilder(GetString("CoListHeader"));
        foreach (var e in RoleEntries.Values) sb.Append($"\n{e.PlayerName} : {Utils.ColorString(e.Color, e.Text)}");
        Utils.SendMessage(sb.ToString(), c.Player.PlayerId);
    }

    public static void AddonList(ChatCommandContext c)
    {
        if (AddonEntries.Count == 0)
        {
            Utils.SendMessage(GetString("CoListEmpty"), c.Player.PlayerId);
            return;
        }
        var sb = new System.Text.StringBuilder(GetString("AcoListHeader"));
        foreach (var e in AddonEntries.Values)
            sb.Append($"\n{e.PlayerName} : {string.Join(", ", e.Roles.Select(r => Utils.ColorString(Utils.GetRoleColor(r), Utils.GetRoleName(r))))}");
        Utils.SendMessage(sb.ToString(), c.Player.PlayerId);
    }
}
