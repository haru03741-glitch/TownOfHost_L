// Based on TownOfHost-hamo Modules/Utils/Utils.cs SetChatVisible / SetChatVisibleForAll と OptionGameChatSetting
// (元は EHR のバニラ向けの見せ方)
using System;
using HarmonyLib;
using Hazel;
using InnerNet;

namespace TownOfHostL.Modules;

/// <summary>
/// タスクターン中もチャットを使えるようにする (設定)。
/// 本編はタスク中に生きている人のチャット欄を閉じるので、試合の始めと会議の後に、生きている人ごとに
/// 「見えない会議を出し、その人を一瞬だけ死んだ扱いにして会議を閉じ、生死を戻して会議を消す」を送る。
/// 会議を閉じた時に死んでいた扱いなので、客の手元でチャット欄が開いたまま残る。
/// </summary>
public static class TaskTurnChat
{
    public static OptionItem Enabled;

    public static void SetupCustomOption()
    {
        Enabled = StringOptionItem.Create("TaskTurnChat", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(new UnityEngine.Color32(0, 193, 255, 255))
            .SetGameMode(CustomGameMode.Standard);
    }

    public static bool IsOn => Enabled != null && Enabled.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard;

    // 画面の切り替えの暗転が終わる頃に開く (hamo と同じ 0.5 秒)
    private const float Delay = 0.5f;

    /// <summary>会議の後 (その日の終わりの処理から 1 回だけ呼ぶ)</summary>
    public static void AfterMeeting()
    {
        if (!IsOn || !AmongUsClient.Instance.AmHost) return;
        // 暗転対策の身代わりを戻す前に生死を送ると、客の画面の生死が崩れるので、戻した後にする
        _ = new LateTask(() => AntiBlackout.RunAfterRevert(OpenForAll), Delay, "TaskTurnChat.AfterMeeting");
    }

    /// <summary>生きている人全員のチャット欄を開く (ホストだけ)</summary>
    public static void OpenForAll()
    {
        if (!IsOn || !AmongUsClient.Instance.AmHost || !GameStates.IsInTask) return;
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            try { Open(pc); }
            catch (Exception ex) { Logger.Exception(ex, "TaskTurnChat"); }
        }
    }

    private static void Open(PlayerControl pc)
    {
        if (pc == null || pc.Data == null || pc.Data.Disconnected) return;
        if (pc.AmOwner)
        {
            var hud = HudManager.InstanceExists ? HudManager.Instance : null;
            if (hud && hud.Chat) hud.Chat.SetVisible(true);
            return;
        }
        if (TownOfHostL.Core.CustomNetObject.Is(pc) || !HudManager.InstanceExists || HudManager.Instance.MeetingPrefab == null) return;
        int clientId = pc.GetClientId();
        if (clientId < 0) return;

        var client = AmongUsClient.Instance;
        bool dead = pc.Data.IsDead;
        var writer = MessageWriter.Get(SendOption.Reliable);
        // 生死を書き出す間は、ホストの手元の名前 (役職入り) でなく本当の名前で書き出す
        GameDataSerializePatch.SerializeMessageCount++;
        try
        {
            writer.StartMessage(6);
            writer.Write(client.GameId);
            writer.WritePacked(clientId);

            // 見えない会議を出す
            uint hudId = client.NetIdCnt;
            client.NetIdCnt = hudId + 1U;
            writer.StartMessage(4);
            writer.WritePacked(HudManager.Instance.MeetingPrefab.SpawnId);
            writer.WritePacked(-2);
            writer.Write((byte)SpawnFlags.None);
            writer.WritePacked(1);
            writer.WritePacked(hudId);
            writer.StartMessage(1);
            writer.WritePacked(0);
            writer.EndMessage();
            writer.EndMessage();

            // 一瞬だけ死んだ扱い (チャットを使える) にする
            pc.Data.IsDead = true;
            writer.StartMessage(1);
            writer.WritePacked(pc.Data.NetId);
            pc.Data.Serialize(writer, true);
            writer.EndMessage();

            // 会議を閉じる (閉じた時に死んでいる扱いなので、チャット欄が開いたまま残る)
            writer.StartMessage(2);
            writer.WritePacked(hudId);
            writer.Write((byte)RpcCalls.CloseMeeting);
            writer.EndMessage();

            // 生死を戻す
            pc.Data.IsDead = dead;
            writer.StartMessage(1);
            writer.WritePacked(pc.Data.NetId);
            pc.Data.Serialize(writer, true);
            writer.EndMessage();

            // 会議を消す (残すと次の本物の会議の画面が壊れる)
            writer.StartMessage(5);
            writer.WritePacked(hudId);
            writer.EndMessage();

            writer.EndMessage();
            client.SendOrDisconnect(writer);
        }
        finally
        {
            pc.Data.IsDead = dead;
            GameDataSerializePatch.SerializeMessageCount--;
            writer.Recycle();
        }
    }

    // 試合の始め (イントロの後)
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    static class IntroEndPatch
    {
        public static void Postfix()
        {
            if (!IsOn || !AmongUsClient.Instance.AmHost) return;
            _ = new LateTask(OpenForAll, Delay, "TaskTurnChat.Intro");
        }
    }
}
