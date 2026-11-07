using System;
using HarmonyLib;
using Hazel;
using InnerNet;
using TownOfHostL.Modules.Net;

namespace TownOfHostL.Patches;

// ホストが出す全パケットの最後の関所。
// 公式サーバーは 1 メッセージ 1200 バイト以上 (ヘッダ込み) を Hacking として即切断する。実測では 999 バイト以下は安全、
// 1199 バイトまでは通る。1000 バイトを超えたパケットだけを GameData(5) / GameDataTo(6) の中身の境界で 800 バイト以下に
// 分け直して送る。1000 バイト以下は長さを比べるだけで素通りさせる。
// 分割しても 1 つの中身だけで上限を超えるものは直しようがないので、公式サーバーでは捨ててログに残す
// (切断されるよりは、その 1 件が届かない方がよい)。直すべきは送る側の文字列の長さ。
// 分けた断片は connection.Send で直接送るので、このパッチには戻ってこない。
// 分けた断片同士の到着順は保証されない。Data→RPC→Data のように順序に意味がある束は 800 バイト以下に収めること。
// 謝辞: 送信の関所で分割する方式は Town Of Host-K (KYMario, GPL-3.0) に倣った。
// 本編のかくれんぼで休んでいる間も TOHL は電文を送るので、Core の束に置く
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendOrDisconnect))]
static class SendGuardPatch
{
    const int SplitThreshold = 1000;
    const int ChunkLength = 800;
    // これより長い中身は単独で送っても切断されうる。1199 からパケットとエンベロープのヘッダ分を引いた値に余裕を持たせた
    const int MaxSingleMessage = 1150;
    // WriteMessage が中身ごとに書く長さ(2)とタグ(1)
    const int MessageHeader = 3;

    static bool Prefix(InnerNetClient __instance, [HarmonyArgument(0)] MessageWriter msg)
    {
        if (msg.Length <= SplitThreshold) return true;

        bool official = OfficialServer.IsActive;
        Logger.Info($"大きいパケットを分割: {msg.Length}バイト, {msg.SendOption}, 公式={official}", "SendGuard");

        var writer = MessageWriter.Get(msg.SendOption);
        var reader = MessageReader.Get(msg.ToByteArray(false));
        bool sentAny = false;
        try
        {
            while (reader.Position < reader.Length)
            {
                var part = reader.ReadMessage();
                bool divide = part.Tag is 5 or 6 && part.Length > ChunkLength;

                if (!divide && part.Length > MaxSingleMessage)
                {
                    if (official)
                    {
                        Logger.Error($"分割できない長いメッセージを捨てた: tag={part.Tag} {part.Length}バイト", "SendGuard");
                        part.Recycle();
                        continue;
                    }
                    Logger.Warn($"分割できない長いメッセージ: tag={part.Tag} {part.Length}バイト", "SendGuard");
                }

                // 書く前に溜まりを送る (書いた後に判定すると上限の倍まで膨らむ)
                if (writer.Length > 0 && (divide || writer.Length + MessageHeader + part.Length > ChunkLength))
                {
                    Send(__instance, writer);
                    sentAny = true;
                    writer.Clear(writer.SendOption);
                }

                if (divide) sentAny |= DivideGameData(__instance, writer, part, official);
                else CopyMessage(writer, part);
                part.Recycle();
            }
            if (writer.Length > 0)
            {
                Send(__instance, writer);
                sentAny = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "SendGuard");
            writer.Recycle();
            reader.Recycle();
            // 1 つも送っていなければ本編の送信に任せる。途中まで送ったなら二重送信を避けて止める
            return !sentAny;
        }
        writer.Recycle();
        reader.Recycle();
        return false;
    }

    // GameData(5) / GameDataTo(6) を中身の境界で ChunkLength 以下に分ける。
    // 呼ぶ時 writer は空。最後のエンベロープは閉じて writer に残し、呼び出し元が送る。中身が 0 個のエンベロープは作らない。
    static bool DivideGameData(InnerNetClient client, MessageWriter writer, MessageReader part, bool official)
    {
        byte tag = part.Tag;
        int gameId = part.ReadInt32();
        int clientId = tag == 6 ? part.ReadPackedInt32() : -1;
        bool sentAny = false;
        bool open = false;
        bool hasChild = false;

        while (part.Position < part.Length)
        {
            var child = part.ReadMessage();
            if (child.Length > MaxSingleMessage)
            {
                if (official)
                {
                    Logger.Error($"分割できない長いメッセージを捨てた: tag={tag}/{child.Tag} {child.Length}バイト", "SendGuard");
                    child.Recycle();
                    continue;
                }
                Logger.Warn($"分割できない長いメッセージ: tag={tag}/{child.Tag} {child.Length}バイト", "SendGuard");
            }

            if (open && hasChild && writer.Length + MessageHeader + child.Length > ChunkLength)
            {
                writer.EndMessage();
                Send(client, writer);
                sentAny = true;
                writer.Clear(writer.SendOption);
                open = false;
            }
            if (!open)
            {
                writer.StartMessage(tag);
                writer.Write(gameId);
                if (tag == 6) writer.WritePacked(clientId);
                open = true;
                hasChild = false;
            }
            CopyMessage(writer, child);
            hasChild = true;
            child.Recycle();
        }

        if (open)
        {
            if (hasChild) writer.EndMessage();
            else writer.CancelMessage();
        }
        return sentAny;
    }

    static void CopyMessage(MessageWriter writer, MessageReader message)
    {
        writer.Write((ushort)message.Length);
        writer.Write(message.Tag);
        writer.Write(message.ReadBytes(message.Length));
    }

    static void Send(InnerNetClient client, MessageWriter writer)
    {
        var error = client.connection.Send(writer);
        if (error != SendErrors.None)
            Logger.Warn($"分割送信に失敗: {error}", "SendGuard");
    }
}
