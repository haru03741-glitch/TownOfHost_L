using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using InnerNet;
using UnityEngine;
using TownOfHostL.Attributes;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 入室した人を設定で振り分ける (ホストのみ。どれも本編の KickPlayer で、外部の通信は無い)。
/// ・フレンドでない人・スマホの人・本編が付ける初期名のままの人をキック
/// ・同じ人がキックされても 5 回入り直してきたら BAN
/// ・前の試合に居た人が続けて入ってきたら、知らせるかキックする (/kp で一括キック、/aj でこのロビーだけ許す)
/// </summary>
/// <remarks>
/// TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Patches/PlayerJoinAndLeftPatch.cs の入室の確認・
/// Patches/ConsecutiveJoinKickPatch.cs) と、スマホのキックは EndlessHostRoles (KickAndroidPlayer) を元にした。
/// </remarks>
public static class JoinGuard
{
    public static OptionItem KickNotFriend;
    public static OptionItem KickMobile;
    public static OptionItem KickInitialName;
    public static OptionItem BanRepeatedJoin;
    public static OptionItem PreviousPlayers;
    public static OptionItem PreviousPlayersSkipDraw;

    enum PreviousMode { Off, Notify, Kick }
    static readonly string[] previousModes = { "ColoredOff", "JoinGuard.Notify", "JoinGuard.Kick" };

    /// <summary>キックされた後に入り直してきた回数がこれに達したら BAN</summary>
    const int RepeatBanCount = 5;

    public static void SetupCustomOption()
    {
        KickNotFriend = StringOptionItem.Create("KickNotFriend", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.All);
        KickMobile = StringOptionItem.Create("KickMobilePlayer", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.All);
        KickInitialName = StringOptionItem.Create("KickInitialName", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.All);
        BanRepeatedJoin = StringOptionItem.Create("BanRepeatedJoin", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.All);
        PreviousPlayers = StringOptionItem.Create("PreviousPlayersJoin", previousModes, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.All);
        PreviousPlayersSkipDraw = StringOptionItem.Create("PreviousPlayersSkipDraw", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false).SetParent(PreviousPlayers)
            .SetGameMode(CustomGameMode.All);
    }

    // ===== 入室 =====

    /// <summary>OnPlayerJoined の後で呼ぶ。キックしたら true</summary>
    public static bool OnPlayerJoined(ClientData client)
    {
        if (!AmongUsClient.Instance.AmHost || client == null || client.Id == AmongUsClient.Instance.ClientId) return false;
        var online = AmongUsClient.Instance.NetworkMode == NetworkModes.OnlineGame;

        // PUID がまだ届いていない時は、フレンドかどうかを決められないので見送る
        if (online && KickNotFriend.GetBool() && !string.IsNullOrEmpty(client.ProductUserId) && !IsFriend(client))
            return Kick(client, "Message.KickedByNoFriend");
        if (KickMobile.GetBool() && client.PlatformData?.Platform is Platforms.Android or Platforms.IPhone)
            return Kick(client, "Message.KickedByMobile");
        if (KickInitialName.GetBool() && IsInitialName(client.PlayerName))
            return Kick(client, "Message.KickedByInitialName");

        CheckPreviousPlayer(client);
        return false;
    }

    static bool IsFriend(ClientData client)
    {
        var friends = DestroyableSingleton<FriendsListManager>.Instance;
        return friends != null && friends.IsPlayerFriend(client.ProductUserId);
    }

    // キックした人の PUID と、キックした回数
    static readonly Dictionary<string, int> kickCounts = new();

    static bool Kick(ClientData client, string messageKey)
    {
        var puid = client.ProductUserId ?? "";
        var count = string.IsNullOrEmpty(puid) ? 1 : kickCounts[puid] = kickCounts.GetValueOrDefault(puid) + 1;
        var ban = BanRepeatedJoin.GetBool() && count >= RepeatBanCount;
        AmongUsClient.Instance.KickPlayer(client.Id, ban);
        Logger.SendInGame(string.Format(GetString(ban ? "Message.KickedByKickPlayer" : messageKey), client.PlayerName));
        Logger.Info($"{client.PlayerName}({client.FriendCode}) を{(ban ? "BAN" : "キック")} ({messageKey}・{count} 回目)", "JoinGuard");
        return true;
    }

    // ===== 初期名 =====

    // 本編の乱数名の単語 (RandomNameGenerator.Parse で読む)
    static readonly HashSet<string> nameWords = new();
    static readonly Regex InitialNameShape = new("^[A-Z][a-z]+$", RegexOptions.Compiled);

    [HarmonyPatch(typeof(RandomNameGenerator), nameof(RandomNameGenerator.Parse))]
    static class RandomNameGeneratorParsePatch
    {
        public static void Postfix(RandomNameGenerator __instance)
        {
            foreach (var group in __instance.WordGroups.Values)
                foreach (var word in group)
                    if (!string.IsNullOrEmpty(word) && !word.Contains(' ')) nameWords.Add(word.ToLowerInvariant());
        }
    }

    /// <summary>本編の乱数名 (大文字 1 字 + 小文字の単語 2 つをつないだ形) か</summary>
    static bool IsInitialName(string name)
    {
        if (string.IsNullOrEmpty(name) || nameWords.Count == 0 || !InitialNameShape.IsMatch(name)) return false;
        var lower = name.ToLowerInvariant();
        for (var i = 1; i < lower.Length; i++)
            if (nameWords.Contains(lower[..i]) && nameWords.Contains(lower[i..])) return true;
        return false;
    }

    // ===== 前の試合に居た人 =====

    static readonly HashSet<string> previousKeys = new();
    static readonly HashSet<string> currentKeys = new();
    // 見つけた人 (/kp の対象)
    static readonly Dictionary<int, string> detected = new();
    static bool allowThisLobby;

    static string KeyOf(ClientData client)
    {
        if (client == null) return "";
        var fc = client.FriendCode?.Trim();
        return !string.IsNullOrEmpty(fc) ? fc.ToUpperInvariant() : client.ProductUserId?.Trim() ?? "";
    }

    [GameModuleInitializer]
    public static void OnGameStart()
    {
        detected.Clear();
        allowThisLobby = false;
        if (!AmongUsClient.Instance.AmHost) return;
        currentKeys.Clear();
        foreach (var client in AmongUsClient.Instance.allClients)
        {
            if (client.Id == AmongUsClient.Instance.ClientId) continue;
            var key = KeyOf(client);
            if (key != "") currentKeys.Add(key);
        }
    }

    /// <summary>試合の終わりに呼ぶ。この試合の人を「前の試合の人」として覚える (廃村は数えない設定なら忘れる)</summary>
    public static void OnGameEnd()
    {
        previousKeys.Clear();
        if (!(PreviousPlayersSkipDraw.GetBool() && CustomWinnerHolder.WinnerTeam == CustomWinner.Draw))
            previousKeys.UnionWith(currentKeys);
        currentKeys.Clear();
    }

    static void CheckPreviousPlayer(ClientData client)
    {
        var mode = (PreviousMode)PreviousPlayers.GetValue();
        if (mode == PreviousMode.Off || allowThisLobby || previousKeys.Count == 0) return;
        if (!previousKeys.Contains(KeyOf(client))) return;
        var name = client.PlayerName ?? "???";
        if (mode == PreviousMode.Notify)
        {
            detected[client.Id] = name;
            Utils.SendMessage(string.Format(GetString("JoinGuard.PreviousNotify"), name), PlayerControl.LocalPlayer.PlayerId);
            return;
        }
        Kick(client, "Message.KickedByPreviousGame");
    }

    /// <summary>/kp: 見つけた人をまとめてキック</summary>
    public static void KickDetected()
    {
        var targets = detected.ToArray();
        detected.Clear();
        var kicked = 0;
        foreach (var (clientId, _) in targets)
        {
            if (AmongUsClient.Instance.GetClient(clientId) == null) continue;
            AmongUsClient.Instance.KickPlayer(clientId, false);
            kicked++;
        }
        Utils.SendMessage(string.Format(GetString("JoinGuard.KickedDetected"), kicked), PlayerControl.LocalPlayer.PlayerId);
    }

    /// <summary>/aj: このロビーの間は前の試合の人も入れる</summary>
    public static void AllowThisLobby()
    {
        allowThisLobby = true;
        detected.Clear();
        Utils.SendMessage(GetString("JoinGuard.AllowedThisLobby"), PlayerControl.LocalPlayer.PlayerId);
    }
}
