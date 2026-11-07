using System.Text.RegularExpressions;
using HarmonyLib;

namespace TownOfHostL;

/// <summary>
/// 客が名乗った名前を、ホストが受け付ける前に整える。
/// 名前にタグ (&lt;size&gt; など) や改行が入っていると、名前に役職などを足して送る表示が全員の画面で崩れる。
/// </summary>
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckName))]
class CheckNamePatch
{
    // 本編の名前の入力欄と同じ長さ
    private const int MaxNameLength = 10;
    private static readonly Regex TagPattern = new("<[^>]*>", RegexOptions.Compiled);

    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] ref string name)
    {
        // ホスト自身の名前は整えない (ホストの名前の飾りは別に送っている)
        if (!AmongUsClient.Instance.AmHost || __instance.AmOwner || name == null) return;
        var cleaned = Sanitize(name);
        if (cleaned == name) return;
        Logger.Info($"名前を整えました: \"{name}\" → \"{cleaned}\" (client {__instance.OwnerId})", "CheckName");
        name = cleaned;
    }

    public static string Sanitize(string name)
    {
        var cleaned = TagPattern.Replace(name, "");
        // 閉じていないタグ (<size=999 など) も、後ろに足す閉じタグと繋がって効くので、山括弧そのものを除く
        cleaned = cleaned.Replace("<", "").Replace(">", "")
            .Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();
        if (cleaned.Length > MaxNameLength)
        {
            int cut = MaxNameLength;
            // 絵文字などの 2 単位の文字を途中で切らない
            if (char.IsHighSurrogate(cleaned[cut - 1])) cut--;
            cleaned = cleaned[..cut];
        }
        return cleaned.Length > 0 ? cleaned : "Player";
    }
}
