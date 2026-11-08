// Based on https://github.com/haru03741-glitch/More-Roles-Plus src/Commands/ChatInput.cs, which is based on
// https://github.com/Gurge44/EndlessHostRoles and https://github.com/waffle-ful/Aeterna-End-K-not Patches/TextBoxPatch.cs・ChatControlPatch.cs
// (command suggestion, paste) and https://github.com/KARPED1EM/TownOfNext Patches/TextBoxPatch.cs (character limit) (GPL-3.0)
using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 自分のチャットの入力欄を広げる (どの端末でも自分の欄だけ。客の欄は触れない)。
/// - 記号や全角を含む全部の文字・1 回に 300 文字まで。300 文字は全部が 3 バイトの文字でも 900 バイトに収まる
///   (公式サーバーは 1 通がおよそ 1000 バイトを超えると送った人を切断する)。
/// - Ctrl+V で貼り付け (カーソルの所へ・改行は空白にする。改行は送信になる)。Ctrl+C で写す・Ctrl+X で切り取る。
/// - / で始めると、合うコマンドと引数の書き方を入力欄に薄く出し、Tab で補う。
/// - ↑ / ↓ で前に送った文を呼び戻す (新しい順に 30 件)。打ちかけの文は ↓ で一番下まで戻ると元に戻る。
/// 毎フレームの問い合わせは「何かキーが押されたか」の 1 つだけ。欄の文字を読むのは文字が変わった時とキーを押した時だけ
/// </summary>
public static class ChatInput
{
    public const int Limit = 300;
    private static readonly Color GhostColor = new(0.45f, 0.45f, 0.45f, 0.75f);

    private static TextMeshPro ghost;     // 入力欄の文字の複製 (入力欄の子なので欄と一緒に消える)
    private static IntPtr ghostOwner;     // 複製を作った入力欄
    private static string complete = "";  // Tab で入れる文字 (空 = 補うものなし)
    internal static IntPtr ChatArea;      // チャット欄の入力欄 (文字を全部通す相手)

    private const int HistoryMax = 30;
    private static readonly List<string> history = new(); // 送った文 (古い順)
    private static int historyAt;                          // 呼び戻している位置 (history.Count = 打ちかけの文)
    private static string draft = "";                      // 呼び戻す前に打っていた文

    /// <summary>送った文を覚える (コマンドは送られないので送る処理の入口で呼ぶ)。同じ文を続けて送った時は 1 つにまとめる</summary>
    public static void Remember(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (history.Count == 0 || history[^1] != text)
        {
            history.Add(text);
            if (history.Count > HistoryMax) history.RemoveAt(0);
        }
        historyAt = history.Count;
        draft = "";
    }

    public static void Apply(FreeChatInputField field)
    {
        var area = field ? field.textArea : null;
        if (!area) return;
        ChatArea = area.Pointer;
        area.allowAllCharacters = true;
        area.AllowSymbols = true;
        area.AllowEmail = true;
        area.characterLimit = Limit;
        // < > も打てるので、入力欄では <b> などを装飾として読まない (読むと見える文字数と文字列の長さがずれ、
        // 本編のカーソル位置の計算が範囲の外を読んで例外を出し続ける)
        if (area.outputText) area.outputText.richText = false;
    }

    /// <summary>入力欄の文字が変わった (本編の SetText の後)</summary>
    internal static void OnSetText(TextBoxTMP box, string compo)
    {
        if (box.Pointer != ChatArea || !HudManager.InstanceExists) return;
        var field = HudManager.Instance.Chat ? HudManager.Instance.Chat.freeChatField : null;
        if (!field || !field.textArea || field.textArea.Pointer != box.Pointer) return;
        string text = box.text ?? "";
        // 日本語入力の変換中の文字を、本編は常に末尾に描く (確定すると本編がカーソルの所へ入れる)。カーソルの所に描き直す
        if (!string.IsNullOrEmpty(compo) && box.caretPos >= 0 && box.caretPos < text.Length && box.outputText)
            box.outputText.text = text.Insert(box.caretPos, compo);
        if (field.charCountText) field.charCountText.text = $"{text.Length}/{Limit}";
        ShowSuggestion(box, text);
    }

    private static void ShowSuggestion(TextBoxTMP box, string text)
    {
        string s = "";
        complete = "";
        if (text.Length >= 2 && text[0] == '/' && text[1] != ' ')
        {
            int sp = text.IndexOf(' ');
            if (sp < 0)
            {
                if (ChatCommandTable.Suggest(text, out string name, out string usage))
                {
                    // 打った分は本編の文字が上に重なるので、薄い文字は打った分の綴りのまま続きを足す
                    s = text + name[text.Length..] + (usage.Length > 0 ? " " + usage : "");
                    complete = name + " ";
                }
            }
            else if (sp == text.Length - 1 && ChatCommandTable.Suggest(text[..sp], out string name, out string usage)
                     && name.Equals(text[..sp], StringComparison.OrdinalIgnoreCase) && usage.Length > 0)
                s = text + usage;
        }
        SetGhost(box, s);
    }

    private static void SetGhost(TextBoxTMP box, string s)
    {
        if (s.Length == 0)
        {
            if (ghost) ghost.enabled = false;
            return;
        }
        if (!ghost || ghostOwner != box.Pointer)
        {
            if (ghost) UnityEngine.Object.Destroy(ghost.gameObject);
            var src = box.outputText;
            if (!src) return;
            ghost = UnityEngine.Object.Instantiate(src, src.transform.parent);
            ghost.name = "TOHLChatSuggestion";
            ghost.color = GhostColor;
            var p = src.transform.localPosition;
            ghost.transform.localPosition = new Vector3(p.x, p.y, p.z + 0.001f); // 本編の文字の奥
            ghostOwner = box.Pointer;
        }
        ghost.text = s;
        ghost.enabled = true;
    }

    /// <summary>チャット欄の毎フレームの処理の後に呼ぶ</summary>
    public static void Tick(ChatController chat)
    {
        if (!Input.anyKeyDown) return;
        if (!chat || !chat.IsOpenOrOpening) return;
        var area = chat.freeChatField ? chat.freeChatField.textArea : null;
        if (!area || !area.hasFocus) return;
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (complete.Length > 0) SetText(area, complete, complete.Length);
            return;
        }
        if (Input.GetKeyDown(KeyCode.UpArrow)) { Recall(area, -1); return; }
        if (Input.GetKeyDown(KeyCode.DownArrow)) { Recall(area, 1); return; }
        if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
        if (Input.GetKeyDown(KeyCode.V)) Paste(area, GUIUtility.systemCopyBuffer);
        else if (Input.GetKeyDown(KeyCode.C)) ClipboardHelper.PutClipboardString(area.text);
        else if (Input.GetKeyDown(KeyCode.X))
        {
            ClipboardHelper.PutClipboardString(area.text);
            SetText(area, "", 0);
        }
    }

    private static void Recall(TextBoxTMP area, int delta)
    {
        if (history.Count == 0) return;
        if (historyAt > history.Count) historyAt = history.Count;
        int to = Math.Clamp(historyAt + delta, 0, history.Count);
        if (to == historyAt) return;
        if (historyAt == history.Count) draft = area.text ?? "";
        historyAt = to;
        string s = to == history.Count ? draft : history[to];
        SetText(area, s, s.Length);
    }

    private static void Paste(TextBoxTMP area, string clip)
    {
        if (string.IsNullOrEmpty(clip)) return;
        clip = clip.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        if (clip.Length > Limit) clip = clip[..Limit]; // 巨大な貼り付けで文字列を作り過ぎない (残りは上限で切る)
        string cur = area.text ?? "";
        int at = Math.Clamp(area.caretPos, 0, cur.Length);
        SetText(area, cur.Insert(at, clip), at + clip.Length);
    }

    // 文字を丸ごと入れ替えて、カーソルを caret へ。
    // 本編の SetText は増えた文字数だけカーソルを進めてから描画の文字情報で位置を測るので、一度に何文字も増やすと
    // 古い文字情報の範囲の外を読んで例外になる。先に入力欄の描画を新しい文字で作り直しておく
    internal static void SetText(TextBoxTMP area, string text, int caret)
    {
        if (text.Length > Limit) text = text[..Limit];
        var output = area.outputText;
        if (output)
        {
            output.text = text;
            output.ForceMeshUpdate(true, true);
        }
        area.SetText(text, "");
        area.caretPos = Math.Clamp(caret, 0, text.Length);
        try
        {
            area.MoveCaret();
            area.SetPipePosition();
        }
        catch (Exception e) { Logger.Warn($"カーソルを動かせませんでした: {e.Message}", "ChatInput"); }
    }
}

[HarmonyPatch]
internal static class ChatInputPatches
{
    [HarmonyPatch(typeof(FreeChatInputField), nameof(FreeChatInputField.Awake)), HarmonyPostfix]
    public static void Awake(FreeChatInputField __instance)
    {
        try { ChatInput.Apply(__instance); }
        catch (Exception e) { Logger.Error($"入力欄の設定に失敗: {e.Message}", "ChatInput"); }
    }

    // 本編がチャットの種類の切り替えなどで入力欄を作り直した時のため、開くたびにも立て直す
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Toggle)), HarmonyPostfix]
    public static void Toggle(ChatController __instance)
    {
        try { ChatInput.Apply(__instance.freeChatField); }
        catch (Exception e) { Logger.Error($"入力欄の設定に失敗: {e.Message}", "ChatInput"); }
    }

    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.SetText)), HarmonyPrefix]
    public static void SetTextPrefix([HarmonyArgument(0)] string input, [HarmonyArgument(1)] string inputCompo)
    {
        inSetText = true;
        pending = string.IsNullOrEmpty(inputCompo) ? input : input + inputCompo;
        pendingAt = 0;
    }

    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.SetText)), HarmonyPostfix]
    public static void SetTextPostfix(TextBoxTMP __instance, [HarmonyArgument(1)] string inputCompo)
    {
        try { ChatInput.OnSetText(__instance, inputCompo); }
        catch (Exception e) { Logger.Error($"入力欄の文字の処理に失敗: {e.Message}", "ChatInput"); }
    }

    // 後始末は本編の SetText が例外を出した時も走らせる (残ると、次のキー入力で古い文字列と照らして字を落とす)
    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.SetText)), HarmonyFinalizer]
    public static void SetTextFinalizer()
    {
        inSetText = false;
        pending = null;
    }

    // 本編は allowAllCharacters を立てても < > や全角の数字などを落とす。落とすと本編の SetText はカーソルを
    // 落とした分も進めたまま位置を測って例外になるので、チャット欄では見える文字を全部通す。
    // Backspace などの制御文字は本編の判定のまま (通すと □ として入る)。
    // この関数の char の引数は下位 1 バイトに切られて届く (一 U+4E00 → U+0000・会 U+4F1A → U+001A・下位バイトが 0x80 以上なら U+FFFD)。
    // 届いた値だけで制御文字を見ると、一・三・上・会・言 などが落ちる。そこで本物の文字を別に持つ:
    // SetText の中では渡された文字列、キー入力の処理ではその時の Input.inputString。
    // 本編は文字を先頭から順に 1 つずつ尋ねるので、届いた値と切った値を照らしながら位置を進める。合わない時は届いた値で判定する。
    // チャット欄以外の入力欄は本編の判定のまま
    private static string pending;   // 本編が今 1 文字ずつ尋ねている文字列 (null = 分からない)
    private static int pendingAt;
    private static bool inSetText;
    private static int inputFrame = -1;

    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.IsCharAllowed)), HarmonyPostfix]
    public static void IsCharAllowed(TextBoxTMP __instance, [HarmonyArgument(0)] char c, ref bool __result)
    {
        if (__result || __instance.Pointer != ChatInput.ChatArea) return;
        // キー入力の処理から尋ねられた時は、本編が断った時だけその回の入力の文字列を読む (毎フレームは読まない)
        if (!inSetText && inputFrame != Time.frameCount)
        {
            inputFrame = Time.frameCount;
            pending = Input.inputString;
            pendingAt = 0;
        }
        if (!char.IsControl(Advance(c))) __result = true;
    }

    // 届いた値 c に合う本物の文字を、尋ねている文字列の今の位置から探して返す (見つからなければ c のまま)
    private static char Advance(char c)
    {
        var s = pending;
        if (s == null) return c;
        for (int i = pendingAt; i < s.Length; i++)
        {
            if (!Matches(s[i], c)) continue;
            pendingAt = i + 1;
            return s[i];
        }
        return c;
    }

    // 本物の文字 real が、1 バイトに切られて c として届いたものか (切られずに届く環境ならそのまま一致する)
    private static bool Matches(char real, char c)
    {
        if (real == c) return true;
        int low = real & 0xFF;
        return low < 0x80 ? c == low : c == (char)0xFFFD;
    }
}
