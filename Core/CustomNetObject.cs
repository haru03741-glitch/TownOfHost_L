using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Hazel;
using InnerNet;
using TownOfHostL.Core.Modes;
using TownOfHostL.Modules.Net;
using UnityEngine;

namespace TownOfHostL.Core;

/// <summary>
/// 名前の文字で絵を描く、偽の PlayerControl (CNO)。客の mod が無くても見える物 (ポータル・罠など) を置くのに使う。
/// ホストだけが作り、どのモードでも動く (Core の束)。役職は <see cref="Create"/> で作り、<see cref="MoveTo"/> と
/// <see cref="ChangeSprite"/> で動かし、<see cref="Despawn"/> で消す。試合が終われば全部消える。
///
/// 出し方 (公式鯖の本編のかくれんぼで客の画面に映る手順):
/// 1. PlayerPrefab を作って spawn (持ち主 -2)。公式鯖では子の 3 つの netId を空の spawn で埋める。
/// 2. 0.1 秒後、客ごとに「CNO の PlayerId をその客の ID に差し替え → MurderPlayer(FailedError) → 戻す」を送る (4 人/包み)。
/// 3. 0.15 秒後、ホストの外見を一瞬だけ「絵の名前・体の色・装飾なし」にして Data を送り、CNO をホストに変身させ、外見を戻す。
/// 4. 0.3 秒後に SnapTo をもう一度送る。客の側は spawn で通し番号が 1 つ進んでいるので、これが無いと映らない。
///
/// 公式鯖のキックを避ける決まり:
/// - イントロが終わってから 13 秒、会議 (追放の演出を含む) が明けてから 10 秒は出さない。
/// - 出すのは 1 体ずつ。次は前の可視化から max(0.2 秒, 客の数 / 12) 空ける (可視化は 1 秒に 12 人分まで)。
/// - 客が 0 人なら可視化の包みは送らない (空の包みは切断される)。
/// - SnapTo は <see cref="SnapToBudget"/> から払う。見た目の包みは順番に意味があるので、分割されない 1000 バイト以下に収める。
/// - 客の操作が CNO を指すことがある (客の手元の一覧には残る)。キルや変身の対象が CNO なら受け付けない。
///
/// 客の画面では体も描かれる。本編 (2026 年版) では体だけを消す手段が無い (死んだ扱いにすると、生きている人からは
/// 名前ごと隠れる)。体は Create で渡す色で描かれるので、絵の色に合わせ、絵を体の大きさまで描いて覆う。
///
/// 謝辞: 名前で絵を描く CNO は MoreGamemodes (Rabek009, GPL-3.0) の AllObjects が元で、
/// Endless Host Roles (Gurge44, GPL-3.0) の CustomNetObject に倣い、
/// 公式鯖で通る形 (空の spawn・ID の循環・開始と会議明けの待ち) は End K not に倣った。
/// </summary>
[PatchBundle(PatchBundles.Core)]
public sealed class CustomNetObject
{
    private const string Tag = "CNO";
    private const string SpriteNamePrefix = "<size=14><br></size>";
    // 本物のプレイヤーは 0〜14。CNO は 200〜254 を回して使う (本編は同じ ID を消してすぐ出すと 2 回目を無視する)
    public const byte MinPlayerId = 200;
    private const byte MaxPlayerId = 254;
    /// <summary>本編の色の番号 (Palette.PlayerColors)。</summary>
    public const byte ColorBlack = 6, ColorPurple = 8;
    // spawn 前の置き場 (誰にも見えない所)
    private static readonly Vector2 Hidden = new(50f, 50f);

    private const float AfterIntro = 13f;
    private const float AfterMeeting = 10f;
    private const float MinSpawnGap = 0.2f;
    private const float VisibilityPerSecond = 12f;
    private const int VisibilityTargetsPerEnvelope = 4;
    private const float VisibilityDelay = 0.1f;
    private const float SpriteDelay = 0.15f;
    private const float ResnapDelay = 0.3f;
    // 見た目の包みの間隔 (全 CNO で)
    private const float SpritePacketGap = 0.2f;
    // 動かしている間の SnapTo の間隔と、止まってから最後の位置を確かに送り直すまでの間
    private const float MoveSendGap = 0.1f;
    private const float SettleDelay = 0.5f;
    // 包みの残りはホストの名前と装飾の ID で約 330 バイト。分割の境 (1000) に届かないよう、絵はここまでにする
    private const int MaxSpriteBytes = 600;
    private const int MaxPacketBytes = 1000;

    private enum State { Waiting, Spawned, Visible, Sprited, Live, Gone }

    private static readonly List<CustomNetObject> all = new();
    private static readonly HashSet<byte> usedIds = new();
    private static byte nextId = MaxPlayerId;
    private static float? introEndedAt;
    private static float lastMeetingAt = float.NegativeInfinity;
    private static float nextSpawnAt;
    private static float nextSpritePacketAt;

    private State state = State.Waiting;
    private PlayerControl pc;
    private byte playerId;
    private float stepAt;
    private string pendingSprite;
    private bool moved;
    private float lastSnapAt = float.NegativeInfinity;
    private float settleAt = float.PositiveInfinity;

    public Vector2 Position { get; private set; }
    public string Sprite { get; private set; }
    private readonly byte bodyColorId;
    /// <summary>客の画面に出し終えた。</summary>
    public bool IsLive => state == State.Live;
    public bool IsGone => state == State.Gone;

    private CustomNetObject(string sprite, Vector2 position, byte bodyColorId)
    {
        Sprite = ClampSprite(sprite);
        Position = position;
        this.bodyColorId = bodyColorId;
    }

    /// <summary>
    /// CNO を作る。出すのは決まりを満たした時 (イントロ明け 13 秒など) なので、すぐには映らない。
    /// ホストで試合中でなければ null。客の画面では絵の下に体が <paramref name="bodyColorId"/> の色で見える。
    /// </summary>
    public static CustomNetObject Create(string sprite, Vector2 position, byte bodyColorId = ColorBlack)
    {
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || GameSession.Current == null || !PlayerControl.LocalPlayer) return null;
        var obj = new CustomNetObject(sprite, position, bodyColorId);
        all.Add(obj);
        return obj;
    }

    /// <summary>この PlayerControl は CNO か。客から来た操作の対象を確かめるのに使う。</summary>
    public static bool Is(PlayerControl pc) => pc && pc.PlayerId >= MinPlayerId;

    public void MoveTo(Vector2 position)
    {
        if (state == State.Gone) return;
        Position = position;
        moved = true;
    }

    public void ChangeSprite(string sprite)
    {
        if (state == State.Gone) return;
        sprite = ClampSprite(sprite);
        if (sprite == Sprite && pendingSprite == null) return;
        Sprite = sprite;
        // 出す前なら、出す時の絵が変わるだけ
        if (state == State.Live) pendingSprite = sprite;
    }

    public void Despawn() => Despawn(sendToServer: true);

    private void Despawn(bool sendToServer)
    {
        if (state == State.Gone) return;
        var was = state;
        state = State.Gone;
        all.Remove(this);
        if (was == State.Waiting) return;
        try
        {
            if (pc)
            {
                uint netId = pc.NetId;
                var client = AmongUsClient.Instance;
                if (sendToServer && client && client.AmConnected)
                {
                    var msg = MessageWriter.Get(SendOption.Reliable);
                    msg.StartMessage(5);
                    msg.Write(client.GameId);
                    msg.StartMessage(5);
                    msg.WritePacked(netId);
                    msg.EndMessage();
                    msg.EndMessage();
                    client.SendOrDisconnect(msg);
                    msg.Recycle();
                }
                if (client) client.RemoveNetObject(pc);
                UnityEngine.Object.Destroy(pc.gameObject);
                Log.Info(Tag, $"消した netId={netId} id={playerId}");
            }
        }
        catch (Exception ex)
        {
            Log.Exception(Tag, ex);
        }
        finally
        {
            pc = null;
            usedIds.Remove(playerId);
        }
    }

    private static void DespawnAll(bool sendToServer)
    {
        foreach (var obj in all.ToArray()) obj.Despawn(sendToServer);
        all.Clear();
        usedIds.Clear();
        nextId = MaxPlayerId;
        introEndedAt = null;
        lastMeetingAt = float.NegativeInfinity;
        nextSpawnAt = nextSpritePacketAt = 0f;
    }

    // ---- 毎フレーム ----

    private static void Update()
    {
        if (all.Count == 0) return;
        var client = AmongUsClient.Instance;
        if (!client || !client.AmHost || GameSession.Current == null || !PlayerControl.LocalPlayer) return;
        float now = Time.realtimeSinceStartup;
        if (MeetingHud.Instance || ExileController.Instance) lastMeetingAt = now;

        bool spawning = false;
        // Tick の中で消えると all から抜けるので、後ろから回す
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (i >= all.Count) continue;
            var obj = all[i];
            try
            {
                obj.Tick(now, ref spawning);
            }
            catch (Exception ex)
            {
                Log.Exception(Tag, ex);
                obj.Despawn();
            }
        }
    }

    private void Tick(float now, ref bool spawning)
    {
        switch (state)
        {
            case State.Waiting:
                // 出すのは 1 体ずつ。前の CNO が出し終わるまで (と決まりの間) 次は待つ
                if (spawning || !CanStartSpawn(now)) return;
                Spawn(now);
                spawning = true;
                return;
            case State.Spawned:
            case State.Visible:
            case State.Sprited:
                spawning = true;
                if (now < stepAt) return;
                if (!pc) { Despawn(); return; }
                if (state == State.Spawned) SendVisibility(now);
                else if (state == State.Visible)
                {
                    if (now < nextSpritePacketAt) return;
                    SendSprite(now);
                }
                else Resnap(now);
                return;
            case State.Live:
                if (!pc) { Despawn(); return; }
                if (pendingSprite != null && now >= nextSpritePacketAt) { SendSprite(now); pendingSprite = null; }
                UpdatePosition(now);
                return;
        }
    }

    private static bool CanStartSpawn(float now)
    {
        if (introEndedAt is not { } intro || now - intro < AfterIntro) return false;
        if (now - lastMeetingAt < AfterMeeting) return false;
        return now >= nextSpawnAt;
    }

    // ---- 1. spawn ----

    private void Spawn(float now)
    {
        var client = AmongUsClient.Instance;
        playerId = AllocateId();
        pc = UnityEngine.Object.Instantiate(client.PlayerPrefab, Vector2.zero, Quaternion.identity);
        pc.PlayerId = playerId;
        pc.isNew = false;
        pc.notRealPlayer = true;
        pc.NetTransform.SnapTo(Hidden);
        client.NetIdCnt += 1U;

        var msg = MessageWriter.Get(SendOption.Reliable);
        msg.StartMessage(5);
        msg.Write(client.GameId);
        msg.StartMessage(4);
        client.CreateSpawnMessage(pc, -2, SpawnFlags.None).SerializeValues(msg);
        msg.EndMessage();
        if (OfficialServer.IsActive)
        {
            // PlayerControl の子 (NetTransform など) 3 つの netId を空の spawn で埋めておく
            for (uint i = 1; i <= 3; ++i)
            {
                msg.StartMessage(4);
                msg.WritePacked(2U);
                msg.WritePacked(-2);
                msg.Write((byte)SpawnFlags.None);
                msg.WritePacked(1);
                msg.WritePacked(client.NetIdCnt - i);
                msg.StartMessage(1);
                msg.EndMessage();
                msg.EndMessage();
            }
        }
        msg.EndMessage();
        client.SendOrDisconnect(msg);
        msg.Recycle();

        HideFromHostList();
        pc.cosmetics.colorBlindText.color = Color.clear;
        pc.cosmetics.currentBodySprite.BodySprite.color = Color.clear;
        state = State.Spawned;
        stepAt = now + VisibilityDelay;
        Log.Info(Tag, $"出した netId={pc.NetId} id={playerId} at ({Position.x:F2}, {Position.y:F2})");
    }

    // ホストの一覧に入っていると、TOHL も本編もプレイヤーとして扱ってしまう
    private void HideFromHostList()
    {
        if (!PlayerControl.AllPlayerControls.Contains(pc)) return;
        PlayerControl.AllPlayerControls.Remove(pc);
        Players.Invalidate();
    }

    private static byte AllocateId()
    {
        for (int i = 0; i <= MaxPlayerId - MinPlayerId; i++)
        {
            byte candidate = nextId;
            nextId = nextId <= MinPlayerId ? MaxPlayerId : (byte)(nextId - 1);
            if (usedIds.Add(candidate)) return candidate;
        }
        // 55 体が同時に出ていることは無いはずだが、出ていたら重ねて使う (見た目が混ざるだけ)
        Log.Error(Tag, "CNO の ID が足りません");
        return nextId;
    }

    // ---- 2. 客ごとの可視化 ----

    private void SendVisibility(float now)
    {
        HideFromHostList();
        var client = AmongUsClient.Instance;
        var targets = Players.All.Where(p => p && !p.AmOwner && p.PlayerId < MinPlayerId).ToList();
        for (int start = 0; start < targets.Count; start += VisibilityTargetsPerEnvelope)
        {
            var stream = MessageWriter.Get(SendOption.Reliable);
            stream.StartMessage(26);
            stream.WritePacked(client.GameId);
            foreach (var target in targets.Skip(start).Take(VisibilityTargetsPerEnvelope))
            {
                stream.StartMessage(6);
                stream.Write(client.GameId);
                stream.WritePacked(target.OwnerId);
                stream.StartMessage(1);
                stream.WritePacked(pc.NetId);
                stream.Write(target.PlayerId);
                stream.EndMessage();
                stream.StartMessage(2);
                stream.WritePacked(pc.NetId);
                stream.Write((byte)RpcCalls.MurderPlayer);
                stream.WriteNetObject(pc);
                stream.Write((int)MurderResultFlags.FailedError);
                stream.EndMessage();
                stream.StartMessage(1);
                stream.WritePacked(pc.NetId);
                stream.Write(pc.PlayerId);
                stream.EndMessage();
                stream.EndMessage();
            }
            stream.EndMessage();
            client.SendOrDisconnect(stream);
            stream.Recycle();
        }
        Log.Info(Tag, $"可視化 netId={pc.NetId} 客={targets.Count}");
        nextSpawnAt = now + Mathf.Max(MinSpawnGap, targets.Count / VisibilityPerSecond);
        pc.CachedPlayerData = PlayerControl.LocalPlayer.Data;
        state = State.Visible;
        stepAt = now + SpriteDelay;
    }

    // ---- 3. 見た目 ----

    // ホストの外見を一瞬だけ絵の名前と体の色に書き換えて Data を送り、CNO をホストに変身させてから外見を戻す (1 つの包みで)
    private void SendSprite(float now)
    {
        var client = AmongUsClient.Instance;
        var lp = PlayerControl.LocalPlayer;
        var outfit = lp.Data.Outfits[PlayerOutfitType.Default];
        string name = outfit.PlayerName, hat = outfit.HatId, skin = outfit.SkinId, pet = outfit.PetId, visor = outfit.VisorId;
        int color = outfit.ColorId;

        var w = MessageWriter.Get(SendOption.Reliable);
        try
        {
            w.StartMessage(5);
            w.Write(client.GameId);
            outfit.PlayerName = SpriteNamePrefix + Sprite;
            outfit.ColorId = bodyColorId;
            outfit.HatId = outfit.SkinId = outfit.PetId = outfit.VisorId = "";
            w.StartMessage(1);
            w.WritePacked(lp.Data.NetId);
            lp.Data.Serialize(w, false);
            w.EndMessage();
            try
            {
                pc.Shapeshift(lp, false);
            }
            catch (Exception ex)
            {
                Log.Warn(Tag, $"手元の変身: {ex.Message}");
            }
            w.StartMessage(2);
            w.WritePacked(pc.NetId);
            w.Write((byte)RpcCalls.Shapeshift);
            w.WriteNetObject(lp);
            w.Write(false);
            w.EndMessage();
        }
        finally
        {
            outfit.PlayerName = name;
            outfit.ColorId = color;
            outfit.HatId = hat;
            outfit.SkinId = skin;
            outfit.PetId = pet;
            outfit.VisorId = visor;
        }
        w.StartMessage(1);
        w.WritePacked(lp.Data.NetId);
        lp.Data.Serialize(w, false);
        w.EndMessage();
        bool firstSprite = state == State.Visible;
        if (firstSprite)
        {
            pc.NetTransform.SnapTo(Position);
            w.StartMessage(2);
            w.WritePacked(pc.NetTransform.NetId);
            w.Write((byte)RpcCalls.SnapTo);
            NetHelpers.WriteVector2(Position, w);
            w.Write(pc.NetTransform.lastSequenceId);
            w.EndMessage();
        }
        w.EndMessage();

        nextSpritePacketAt = now + SpritePacketGap;
        if (w.Length > MaxPacketBytes)
        {
            // 分割されると順番が崩れて、ホストの外見が絵のまま残りうる。送らずに捨てる (ホストの名前が長すぎる)
            Log.Error(Tag, $"見た目の包みが長すぎるので送りません ({w.Length} バイト)");
            w.Recycle();
            if (firstSprite) { state = State.Sprited; stepAt = now + ResnapDelay; }
            return;
        }
        Log.Info(Tag, $"見た目 netId={pc.NetId} {w.Length} バイト");
        client.SendOrDisconnect(w);
        w.Recycle();
        if (firstSprite)
        {
            SnapToBudget.Record();
            state = State.Sprited;
            stepAt = now + ResnapDelay;
        }
    }

    // ---- 4. 位置 ----

    private void Resnap(float now)
    {
        SendSnap(now, SendOption.Reliable);
        moved = false;
        state = State.Live;
    }

    private void UpdatePosition(float now)
    {
        if (moved)
        {
            if (now - lastSnapAt < MoveSendGap || !SnapToBudget.CanSend()) return;
            // 動かしている間は取りこぼしてよい送り方で、止まったら最後の位置を確かな送り方で送り直す
            SendSnap(now, SendOption.None);
            moved = false;
            settleAt = now + SettleDelay;
            return;
        }
        if (now >= settleAt && SnapToBudget.CanSend())
        {
            SendSnap(now, SendOption.Reliable);
            settleAt = float.PositiveInfinity;
        }
    }

    // 手元は通し番号 +1、電文は +2 (客の側は spawn の時に 1 つ進んでいる)。
    // StartRpcImmediately を通すと通常モードの送信のパッチが SnapTo を数えるので、ここで包みを組んで自分で数える
    private void SendSnap(float now, SendOption option)
    {
        var client = AmongUsClient.Instance;
        var nt = pc.NetTransform;
        nt.SnapTo(Position, (ushort)(nt.lastSequenceId + 1U));
        var w = MessageWriter.Get(option);
        w.StartMessage(5);
        w.Write(client.GameId);
        w.StartMessage(2);
        w.WritePacked(nt.NetId);
        w.Write((byte)RpcCalls.SnapTo);
        NetHelpers.WriteVector2(Position, w);
        w.Write((ushort)(nt.lastSequenceId + 2U));
        w.EndMessage();
        w.EndMessage();
        client.SendOrDisconnect(w);
        w.Recycle();
        SnapToBudget.Record();
        lastSnapAt = now;
    }

    // ---- 絵の長さ ----

    private static string ClampSprite(string sprite)
    {
        sprite ??= "";
        if (Encoding.UTF8.GetByteCount(sprite) <= MaxSpriteBytes) return sprite;
        Log.Error(Tag, $"絵が長すぎるので切ります ({Encoding.UTF8.GetByteCount(sprite)} バイト)");
        var sb = new StringBuilder();
        int bytes = 0;
        foreach (var rune in sprite.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MaxSpriteBytes) break;
            sb.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        var s = sb.ToString();
        // タグの途中で切ると、その後ろの描き方が崩れる
        int open = s.LastIndexOf('<');
        return open >= 0 && s.IndexOf('>', open) < 0 ? s[..open] : s;
    }

    // ---- パッチ ----

    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    private static class DrivePatch
    {
        public static void Postfix() => Update();
    }

    // 開始直後の窓はイントロが終わった時から数える
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    private static class IntroEndPatch
    {
        public static void Postfix()
        {
            if (GameSession.Current != null) introEndedAt = Time.realtimeSinceStartup;
        }
    }

    // 試合が終わったら、まだつながっているうちに全部消す
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    private static class GameEndPatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Postfix() => DespawnAll(sendToServer: true);
    }

    // 客の手元の一覧には CNO が残るので、客のキルボタンが CNO を指すことがある。
    // 通すと本編は CNO を殺そうとする (CNO の Data はホストの Data なので、ホストが死ぬ)。受け付けずに捨てる
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
    private static class CheckMurderPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
        {
            if (!Is(target) && !Is(__instance)) return true;
            Log.Info(Tag, $"CNO へのキルを捨てた: {__instance?.Data?.PlayerName}");
            return false;
        }
    }

    static CustomNetObject()
    {
        GameSession.Closed += _ => DespawnAll(sendToServer: false);
    }
}
