using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TownOfHostL.Roles.Core;
using UnityEngine;

using static TownOfHostL.Translator;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼの間のチャットコマンド。TOHL のコマンド表は休んでいるので、かくれんぼの役職の説明と設定だけをここで答える。
/// ホストが打てば自分のチャット欄に出し、客が打てば本人宛てのチャットで返す (客は mod が無くても /h のまま打てる)。
/// 試合中はチャットが無いので、使うのはロビーだけ。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsCommands
{
    // 同じ客への返事の間隔。続けて打たれても送る数を抑える
    private const float ReplyInterval = 2f;
    private static readonly Dictionary<byte, float> lastReplyAt = new();

    /// <summary>ロビーに戻った時に呼ぶ。返事の間隔は部屋や試合をまたいで持ち越さない。</summary>
    public static void Reset() => lastReplyAt.Clear();

    /// <summary>コマンドなら答えを返す。コマンドでなければ null。</summary>
    private static string Answer(string text)
    {
        var args = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0) return null;
        // TOHL の客は /cmd を付けて送ってくる
        if (args[0].Equals("/cmd", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2) return null;
            args = args.Skip(1).ToArray();
            if (!args[0].StartsWith('/')) args[0] = "/" + args[0];
        }
        switch (args[0].ToLowerInvariant())
        {
            case "/h":
            case "/help":
                // /h r <役職> (通常モードの書き方) も受ける
                var rest = args.Skip(1).ToList();
                if (rest.Count > 0 && rest[0] is "r" or "roles") rest.RemoveAt(0);
                return rest.Count == 0 ? Help() : Describe(string.Join(" ", rest));
            case "/n":
            case "/now":
                return Settings();
            default:
                return null;
        }
    }

    private static string RoleName(CustomRoles role) => GetString(role.ToString());

    private static string Help()
        => string.Format(GetString("VanillaHns.Help"), string.Join(", ", CustomRolesHelper.AllHASRoles.Select(RoleName)));

    private static string Describe(string name)
    {
        foreach (var role in CustomRolesHelper.AllHASRoles)
        {
            var key = role.ToString();
            if (!name.Equals(RoleName(role), StringComparison.OrdinalIgnoreCase)
                && !name.Equals(key, StringComparison.OrdinalIgnoreCase)
                && !name.Equals(key["HAS".Length..], StringComparison.OrdinalIgnoreCase)) continue;
            return $"{RoleName(role)}\n{GetString(key + "InfoLong")}";
        }
        return Help();
    }

    private static string Settings()
    {
        var lines = CustomRolesHelper.AllHASRoles
            .Where(role => role.IsEnable())
            .Select(role => $"{RoleName(role)}: {role.GetChance()}% ×{role.GetCount()}")
            .ToList();
        return lines.Count == 0 ? GetString("VanillaHns.NoRoles") : string.Join("\n", lines);
    }

    // ホストが打った時。/mode は常駐の側が先に拾う
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    private static class HostPatch
    {
        public static bool Prefix(ChatController __instance)
        {
            var field = __instance.freeChatField;
            if (!field || !field.textArea || !AmongUsClient.Instance.AmHost) return true;
            var answer = Answer(field.textArea.text ?? "");
            if (answer == null) return true;
            field.textArea.Clear();
            __instance.AddChat(PlayerControl.LocalPlayer, answer);
            return false;
        }
    }

    // 客の発言はホストの画面に AddChat で出る。そこで拾って本人にだけ返す
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    private static class RemotePatch
    {
        public static void Postfix([HarmonyArgument(0)] PlayerControl sourcePlayer, [HarmonyArgument(1)] string chatText)
        {
            if (!AmongUsClient.Instance.AmHost || !sourcePlayer || sourcePlayer.AmOwner || CustomNetObject.Is(sourcePlayer)) return;
            if (string.IsNullOrEmpty(chatText) || !chatText.StartsWith('/')) return;
            try
            {
                var answer = Answer(chatText);
                if (answer == null) return;
                float now = Time.realtimeSinceStartup;
                if (lastReplyAt.TryGetValue(sourcePlayer.PlayerId, out var last) && now - last < ReplyInterval) return;
                lastReplyAt[sourcePlayer.PlayerId] = now;
                VanillaHideAndSeekRoles.SendChatTo(sourcePlayer.OwnerId, answer);
            }
            catch (Exception ex) { Logger.Exception(ex, "VanillaHnsCommands"); }
        }
    }
}
