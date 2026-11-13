using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using InnerNet;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 自分の部屋で遊んだ人の戦績 (試合数・勝ち・キル・死亡・役職ごとの試合と勝ち)。ホストの手元の TOHL_DATA/PlayerStats.json にだけ持つ (外へは送らない)。
/// 人はフレンドコード (無ければ PUID) で見分ける。廃村の試合は数えない。/me で自分の戦績を、操作盤から部屋の全員の戦績を出す。
/// </summary>
/// <remarks>試合後に戦績を数えて /me で見せる形は TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/PlayerStatsSync.cs・Statistics.cs) を元に、保存をホストの手元だけにした。</remarks>
public static class PlayerStats
{
    const string FilePath = "./TOHL_DATA/PlayerStats.json";

    public sealed class Record
    {
        public string Name { get; set; } = "";
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public DateTime LastPlayed { get; set; }
        /// <summary>役職の内部名 → [試合数, 勝ち]</summary>
        public Dictionary<string, int[]> Roles { get; set; } = new();
    }

    static Dictionary<string, Record> data;

    static Dictionary<string, Record> Data
    {
        get
        {
            if (data != null) return data;
            data = new();
            try
            {
                if (File.Exists(FilePath))
                    data = JsonSerializer.Deserialize<Dictionary<string, Record>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(PlayerStats)); data = new(); }
            return data;
        }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory("TOHL_DATA");
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data));
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(PlayerStats)); }
    }

    static string KeyOf(ClientData client)
    {
        if (client == null) return null;
        if (!string.IsNullOrWhiteSpace(client.FriendCode)) return client.FriendCode.Trim();
        if (!string.IsNullOrWhiteSpace(client.ProductUserId)) return client.ProductUserId.Trim();
        return null;
    }

    /// <summary>試合の終わりに呼ぶ (ホストのみ)</summary>
    public static void OnGameEnd()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (CustomWinnerHolder.WinnerTeam is CustomWinner.Draw or CustomWinner.None) return;
        var winners = CustomWinnerHolder.WinnerIds;
        var now = DateTime.Now;
        var changed = false;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc == null) continue;
            var state = PlayerState.GetByPlayerId(pc.PlayerId);
            var role = state?.GetNowMainRole() ?? CustomRoles.NotAssigned;
            if (role is CustomRoles.NotAssigned or CustomRoles.GM) continue;
            // 試合の終わりには接続の情報 (ClientData.Character) が外れているので、試合の始めに控えた鍵を使う
            if (!keysAtStart.TryGetValue(pc.PlayerId, out var key)) continue;
            if (!Data.TryGetValue(key, out var r)) Data[key] = r = new Record();
            var win = winners.Contains(pc.PlayerId);
            r.Name = Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(pc.PlayerId, out var n) ? n : pc.Data?.PlayerName ?? r.Name;
            r.Games++;
            if (win) r.Wins++;
            r.Kills += state.GetKillCount(true);
            if (state.IsDead) r.Deaths++;
            r.LastPlayed = now;
            if (!r.Roles.TryGetValue(role.ToString(), out var rs) || rs is not { Length: 2 }) r.Roles[role.ToString()] = rs = new int[2];
            rs[0]++;
            if (win) rs[1]++;
            changed = true;
        }
        if (changed) Save();
        keysAtStart.Clear();
    }

    // 試合の始めの、PlayerId → 見分ける鍵
    static readonly Dictionary<byte, string> keysAtStart = new();

    [GameModuleInitializer]
    public static void OnGameStart()
    {
        keysAtStart.Clear();
        if (!AmongUsClient.Instance.AmHost) return;
        foreach (var client in AmongUsClient.Instance.allClients)
        {
            if (client?.Character == null) continue;
            var key = KeyOf(client);
            if (key != null) keysAtStart[client.Character.PlayerId] = key;
        }
    }

    /// <summary>その人の戦績の文。記録が無ければ null</summary>
    public static string Describe(PlayerControl pc, bool detail)
    {
        var key = KeyOf(pc?.GetClient());
        if (key == null || !Data.TryGetValue(key, out var r) || r.Games == 0) return null;
        var sb = new StringBuilder();
        var name = pc.Data?.PlayerName ?? r.Name;
        sb.Append(string.Format(GetString("PlayerStats.Line"), name, r.Games, r.Wins, r.Wins * 100 / r.Games, r.Kills, r.Deaths));
        if (detail)
        {
            var top = r.Roles
                .Where(kv => kv.Value is { Length: 2 } && Enum.TryParse<CustomRoles>(kv.Key, out _))
                .OrderByDescending(kv => kv.Value[0]).ThenByDescending(kv => kv.Value[1]).Take(3)
                .Select(kv => $"{Utils.GetRoleName(Enum.Parse<CustomRoles>(kv.Key))} {kv.Value[1]}/{kv.Value[0]}");
            var roles = string.Join(" / ", top);
            if (roles.Length > 0) sb.Append('\n').Append(GetString("PlayerStats.Roles")).Append(roles);
        }
        return sb.ToString();
    }

    /// <summary>/me: 自分の戦績</summary>
    public static void Command(ChatCommandContext c)
    {
        var text = Describe(c.Player, detail: true) ?? GetString("PlayerStats.None");
        if (c.Player == null || c.Player.AmOwner) HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, text);
        else Utils.SendMessageAutoSplit(text, true, GetString("PlayerStats.Title"), sendTo: c.Player.PlayerId);
    }

    /// <summary>部屋にいる全員の戦績を全員へ出す</summary>
    public static void PostAll()
    {
        var lines = Main.AllPlayerControls.Select(pc => Describe(pc, detail: false)).Where(t => t != null).ToList();
        Utils.SendMessageAutoSplit(lines.Count > 0 ? string.Join("\n", lines) : GetString("PlayerStats.NoneAll"), true, GetString("PlayerStats.Title"));
    }
}
