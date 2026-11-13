using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InnerNet;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// モデレーターの名簿。ホストが選んだ人は、ホストと同じ一部のコマンド (ChatCommand.ForModerator) をチャットから使える。
/// 名簿は TOHL_DATA/Moderators.txt に「フレンドコードか PUID,名前」を 1 行ずつ持つ。どちらで書いても、入室した人のどちらかに合えば当てる。
/// </summary>
/// <remarks>モデレーターに一部のコマンドを渡す仕組みは EndlessHostRoles (Modules/BanManager.cs Moderators.txt・Command.UsageLevels) と TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/Moderator.cs) を元にした。</remarks>
public static class Moderators
{
    const string ListPath = "./TOHL_DATA/Moderators.txt";

    // 名簿の鍵 (フレンドコードか PUID) → 名前。読むのは初回と書き換えた時だけ
    static Dictionary<string, string> list;

    static Dictionary<string, string> List
    {
        get
        {
            if (list != null) return list;
            list = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(ListPath))
                    foreach (var raw in File.ReadAllLines(ListPath))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith('#')) continue;
                        var comma = line.IndexOf(',');
                        var key = (comma < 0 ? line : line[..comma]).Trim();
                        if (key.Length > 0) list[key] = comma < 0 ? "" : line[(comma + 1)..].Trim();
                    }
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(Moderators)); }
            return list;
        }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory("TOHL_DATA");
            File.WriteAllLines(ListPath, List.Select(kv => $"{kv.Key},{kv.Value}"));
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(Moderators)); }
    }

    static IEnumerable<string> Keys(ClientData client)
    {
        if (client == null) yield break;
        if (!string.IsNullOrWhiteSpace(client.FriendCode)) yield return client.FriendCode.Trim();
        if (!string.IsNullOrWhiteSpace(client.ProductUserId)) yield return client.ProductUserId.Trim();
    }

    public static bool Is(ClientData client) => Keys(client).Any(List.ContainsKey);

    public static bool Is(PlayerControl pc) => pc != null && !pc.AmOwner && Is(pc.GetClient());

    /// <summary>名簿に入れる / 外す。入れたら true</summary>
    public static bool Toggle(PlayerControl pc)
    {
        var client = pc?.GetClient();
        var keys = Keys(client).ToArray();
        if (keys.Length == 0) return false;
        bool added;
        if (keys.Any(List.ContainsKey))
        {
            foreach (var k in keys) List.Remove(k);
            added = false;
        }
        else
        {
            // フレンドコードがあればそれで、無ければ PUID で持つ
            List[keys[0]] = client.PlayerName ?? "";
            added = true;
        }
        Save();
        // ロビーでは名前の表 (Main.AllPlayerNames) がまだ無いことがある
        var name = Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(pc.PlayerId, out var n) ? n : client.PlayerName;
        Utils.SendMessageAutoSplit(string.Format(GetString(added ? "Moderator.Added" : "Moderator.Removed"), name), true);
        return added;
    }

    /// <summary>/mod &lt;ID&gt;: モデレーターにする / 外す。ID が無ければ今いるモデレーターを出す</summary>
    public static void Command(ChatCommandContext c)
    {
        if (c.Args.Length > 1 && byte.TryParse(c.Args[1], out var id) && Utils.GetPlayerById(id) is { } target && !target.AmOwner)
        {
            Toggle(target);
            return;
        }
        var now = Main.AllPlayerControls.Where(Is).Select(pc => pc.Data?.PlayerName).ToArray();
        HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer,
            GetString("Moderator.Usage") + "\n" + (now.Length > 0 ? string.Join(", ", now) : GetString("Moderator.None")));
    }
}
