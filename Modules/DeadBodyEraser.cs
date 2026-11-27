using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TownOfHostL.Attributes;
using TownOfHostL.Patches;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Modules;

public static class DeadBodyEraser
{
    private static readonly HashSet<byte> noBodyTargets = new();
    private static readonly HashSet<byte> erasedBodies = new();
    // 消した死体ごとの覆い (持ち主ごと)
    private static readonly Dictionary<byte, CoverState> covers = new();
    // 死体そのものを消す電文を送った客 (死体の持ち主ごと)。この客には覆いを出さない
    private static readonly Dictionary<byte, HashSet<int>> erasedOnClients = new();

    private const int CoverColumns = 6;
    private const int CoverRows = 6;
    private const string CoverPrefix = "<size=70%><line-height=97%><cspace=0.16em>";
    // 本編の客の画面の死体そのものを消せる見込みがある間は、覆いを出さずに待つ。その上限 (消してから)。
    // 過ぎても消せていない客にだけ覆いを出す (キルの演出が終わるのを待つ分を含める)
    private const float CoverGrace = 2f;
    // 覆いを出す所 (地図の外)。CNO は全員の画面に出してから要らない客の画面で消すので、その間は地図の外に置き、
    // 出し終えてから死体の所へ動かす (要らない客の画面に一瞬映らないように)
    private static readonly Vector2 CoverPark = new(50f, 50f);
    private const float CoverParkPoll = 0.1f;
    private const float CoverParkTimeout = 40f;

    private sealed class CoverState
    {
        public byte PlayerId;
        public Vector2 Pos;
        public float Since;
        public readonly List<Core.CustomNetObject> Objects = new();
        // 覆いを出した客と、画面の死体そのものを消した客 (どちらも、この死体の覆いはもう要らない)
        public readonly HashSet<int> Done = new();
    }

    [GameModuleInitializer]
    public static void Init()
    {
        noBodyTargets.Clear();
        erasedBodies.Clear();
        covers.Clear();
        erasedOnClients.Clear();
    }

    public static bool KillWithoutBody(PlayerControl killer, PlayerControl target, CustomDeathReason reason, bool force = true)
    {
        if (!AmongUsClient.Instance.AmHost || killer == null || target == null || !target.IsAlive()) return false;
        noBodyTargets.Add(target.PlayerId);
        try
        {
            CustomRoleManager.OnCheckMurder(killer, target, target, target, force, deathReason: reason);
        }
        finally
        {
            noBodyTargets.Remove(target.PlayerId);
        }
        return !target.IsAlive();
    }

    public static bool TakeNoBody(PlayerControl target)
        => target != null && noBodyTargets.Remove(target.PlayerId);

    public static void ExileInsteadOfMurder(PlayerControl appearanceKiller, PlayerControl target)
    {
        target.Exiled();
        var writer = AmongUsClient.Instance.StartRpcImmediately(target.NetId, (byte)RpcCalls.Exiled, Hazel.SendOption.Reliable, -1);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
        CustomRoleManager.OnMurderPlayer(appearanceKiller, target);
        RemovePet(target);
        Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} を死体を残さずにキルしました", "DeadBodyEraser");
    }

    public static bool IsErased(byte playerId) => erasedBodies.Contains(playerId);

    public static DeadBody FindNearestBody(Vector2 from, float range)
    {
        DeadBody best = null;
        var bestDist = range;
        foreach (var body in Object.FindObjectsOfType<DeadBody>())
        {
            if (body == null || erasedBodies.Contains(body.ParentId)) continue;
            var d = Vector2.Distance(from, body.TruePosition);
            if (d > bestDist) continue;
            bestDist = d;
            best = body;
        }
        return best;
    }

    public static void Erase(byte playerId)
    {
        var body = Object.FindObjectsOfType<DeadBody>().FirstOrDefault(b => b != null && b.ParentId == playerId);
        Vector2? bodyPos = body != null ? BodyCenter(body) : null;
        erasedBodies.Add(playerId);
        HideLocal(playerId);
        EraseOnModClients(playerId);
        if (bodyPos.HasValue)
        {
            Cover(playerId, bodyPos.Value);
            // 本編の客は、送れる客から画面の死体そのものを消す (消せた客からは覆いを外す)
            ClientBodySync.Request();
        }
        RemovePet(Utils.GetPlayerById(playerId));
        Logger.Info($"{playerId} の死体を消しました", "DeadBodyEraser");
    }

    // キルの直後に消す時、死体が出るのを待つ上限と間隔
    private const float AfterKillWait = 2f;
    private const float AfterKillPoll = 0.1f;

    /// <summary>
    /// キルしたばかりの人の死体を、ホストの手元に死体が出てから消す (キルと同じ時に呼んでよい)。
    /// 待つ間に会議になった・生き返った時はやめる
    /// </summary>
    public static void EraseAfterKill(byte playerId)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        WaitBodyThenErase(playerId, AfterKillWait);
    }

    private static void WaitBodyThenErase(byte playerId, float left)
        => _ = new LateTask(() =>
        {
            if (!GameStates.IsInTask || erasedBodies.Contains(playerId)) return;
            var pc = Utils.GetPlayerById(playerId);
            if (pc == null || pc.IsAlive()) return;
            if (Object.FindObjectsOfType<DeadBody>().Any(b => b != null && b.ParentId == playerId))
            {
                Erase(playerId);
                return;
            }
            if (left > AfterKillPoll) WaitBodyThenErase(playerId, left - AfterKillPoll);
            else Logger.Warn($"{playerId} の死体が出ないので、消すのをやめました", "DeadBodyEraser");
        }, AfterKillPoll, "");

    /// <summary>
    /// 生き返った人の残った死体をホストの手元で消し、死体の真ん中を返す (無ければ null)。
    /// 同じ版の TOHL の客の手元でも消す。本編の客の画面は CoverLeftover で覆う。
    /// その人がまた死んだ時の新しい死体は消さない (erasedBodies に入れない)・ペットも外さない
    /// </summary>
    public static Vector2? HideLeftover(byte playerId)
    {
        // 前に消された死体の印を外す (また死んだ時の新しい死体を拾えるように)。消された死体は非アクティブなので下では拾わない
        erasedBodies.Remove(playerId);
        Vector2? pos = null;
        foreach (var b in Object.FindObjectsOfType<DeadBody>())
        {
            if (b == null || b.ParentId != playerId) continue;
            pos ??= BodyCenter(b);
            b.gameObject.SetActive(false);
        }
        if (pos.HasValue)
        {
            EraseOnModClients(playerId);
            ClientBodySync.Request();
            Logger.Info($"{playerId} の残った死体を隠しました", "DeadBodyEraser");
        }
        return pos;
    }

    /// <summary>本編の客の画面に残る死体を床の色で覆う (会議の通報で外れる)</summary>
    public static void CoverLeftover(byte playerId, Vector2 pos)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        // 同じ人の前の覆い (復活してまた死んで、また復活した時など) は外して、今の死体の所に作り直す
        if (covers.Remove(playerId, out var old)) DespawnAll(old);
        Cover(playerId, pos);
    }

    public static void HideLocal(byte playerId)
    {
        erasedBodies.Add(playerId);
        foreach (var body in Object.FindObjectsOfType<DeadBody>().Where(b => b != null && b.ParentId == playerId).ToArray())
            body.gameObject.SetActive(false);
    }

    // 同じ版の TOHL の客の手元の死体を消し、消した客を覚える (覆いを出さない相手)
    private static void EraseOnModClients(byte playerId)
    {
        // 覆いは出し終えた時にこの一覧を見るので、消すたびに新しい一覧にする (前の覆いが見ている一覧を書き換えない)
        var sentTo = new HashSet<int>();
        erasedOnClients[playerId] = sentTo;
        Net.ModClientSync.SendBodyErase(playerId, sentTo);
    }

    // 本編のキルの演出は、終わりに死体へ触る。演出の途中で壊すと演出が例外で止まり、キルした人が動けないままになるので、
    // 先に隠し、演出が終わる頃に壊す
    private const float ClientDestroyDelay = 5f;

    /// <summary>
    /// 同じ版の TOHL の客の手元で、ホストが消した死体を消す。今ある死体だけが対象で、その人が後でまた死んだ時の死体は残る。
    /// </summary>
    public static void EraseOnClient(byte playerId)
    {
        int n = 0;
        foreach (var body in Object.FindObjectsOfType<DeadBody>())
        {
            if (body == null || body.ParentId != playerId) continue;
            var go = body.gameObject;
            go.SetActive(false);
            Object.Destroy(go, ClientDestroyDelay);
            n++;
        }
        if (n > 0) Logger.Info($"{playerId} の死体を消しました ({n} 個)", "DeadBodyEraser");
        else Logger.Warn($"{playerId} の死体を消すよう届きましたが、手元に死体がありません", "DeadBodyEraser");
    }

    private static Vector2 BodyCenter(DeadBody body)
    {
        Vector2 pos = body.transform.position;
        try
        {
            var renderers = body.bodyRenderers;
            if (renderers == null) return pos;
            Bounds? bounds = null;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (bounds == null) bounds = r.bounds;
                else { var b = bounds.Value; b.Encapsulate(r.bounds); bounds = b; }
            }
            if (bounds.HasValue) pos = bounds.Value.center;
        }
        catch { }
        return pos;
    }

    private static void Cover(byte playerId, Vector2 pos)
    {
        if (!AmongUsClient.Instance.AmHost || covers.ContainsKey(playerId)) return;
        if (!(Options.ErasedBodyCover?.GetBool() ?? true)) return;
        var state = new CoverState { PlayerId = playerId, Pos = pos, Since = Time.realtimeSinceStartup };
        covers[playerId] = state;
        // 画面の死体そのものを消せる客には、覆いを出すと消える前に一瞬映るだけになる。合わせ直しが回るなら、
        // 役職などで待っても消せない客にだけすぐ出し、ほかの客は合わせ直せなかった時に後から出す (ClientBodySync が CoverUnsynced を呼ぶ)
        if (ClientBodySync.WillSync)
        {
            CoverClients(state, null, ClientBodySync.CannotSync);
            return;
        }
        CoverClients(state, null);
    }

    /// <summary>
    /// 画面の死体そのものをまだ消せていない本編の客に、待ちの上限を過ぎた死体の覆いを出す。
    /// <paramref name="exclude"/> の客 (これから合わせ直す客) には出さない。<paramref name="force"/> なら上限を待たない
    /// </summary>
    public static void CoverUnsynced(HashSet<int> exclude, bool force = false)
    {
        if (covers.Count == 0) return;
        float now = Time.realtimeSinceStartup;
        foreach (var state in covers.Values)
            if (force || now - state.Since >= CoverGrace) CoverClients(state, exclude);
    }

    // まだ覆いも消す手当ても無い本編の客へ、その客たちにだけ見える覆いを 1 つ出す
    private static void CoverClients(CoverState state, HashSet<int> exclude, System.Func<PlayerControl, bool> only = null)
    {
        HashSet<int> targets = null;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (!pc || pc.AmOwner || Core.CustomNetObject.Is(pc) || pc.Data == null || pc.Data.Disconnected || pc.OwnerId < 0) continue;
            int id = pc.OwnerId;
            if (state.Done.Contains(id) || (exclude != null && exclude.Contains(id))) continue;
            if (only != null && !only(pc)) continue;
            (targets ??= new()).Add(id);
        }
        if (targets == null) return;
        // 消す電文を送った同じ版の客は、手元で死体を消している (一覧は送り終えた時に埋まるので、出し終えた時にも見る)
        erasedOnClients.TryGetValue(state.PlayerId, out var erasedOn);
        if (erasedOn != null) targets.ExceptWith(erasedOn);
        if (targets.Count == 0) return;
        var cover = Core.CustomNetObject.Create(CoverSpriteAt(state.Pos), CoverPark);
        if (cover == null) return;
        cover.HideFrom(p => p.AmOwner || !targets.Contains(p.OwnerId) || (erasedOn != null && erasedOn.Contains(p.OwnerId)));
        // 覆いは会議まで残る。絵が入らずに人の姿で残ると目に付くので、見た目を送り直す
        cover.ResendSprite();
        state.Objects.Add(cover);
        state.Done.UnionWith(targets);
        MoveWhenLive(cover, state.Pos, CoverParkTimeout);
    }

    private static void MoveWhenLive(Core.CustomNetObject cover, Vector2 pos, float left)
        => _ = new LateTask(() =>
        {
            if (cover.IsGone) return;
            if (cover.IsLive) { cover.MoveTo(pos); return; }
            if (left > CoverParkPoll) MoveWhenLive(cover, pos, left - CoverParkPoll);
        }, CoverParkPoll, "");

    private static void DespawnAll(CoverState state)
    {
        foreach (var cover in state.Objects) cover?.Despawn();
    }

    /// <summary>
    /// 本編の客の画面で、<paramref name="pos"/> (死体の真ん中) の死体を床の色で覆う CNO を出す。
    /// ホストと <paramref name="skipClients"/> の客 (死体を手元で消した・動かした客) には出さない。外すのは呼んだ側
    /// </summary>
    public static Core.CustomNetObject CoverAt(Vector2 pos, HashSet<int> skipClients)
    {
        var cover = Core.CustomNetObject.Create(CoverSpriteAt(pos), pos);
        cover?.HideFrom(p => p.AmOwner || (skipClients != null && skipClients.Contains(p.OwnerId)));
        // 覆いは会議まで残る。絵が入らずに人の姿で残ると目に付くので、見た目を送り直す
        cover?.ResendSprite();
        return cover;
    }

    /// <summary><paramref name="pos"/> の床の色で描いた、死体を覆う絵</summary>
    public static string CoverSpriteAt(Vector2 pos)
        => CoverArt(ColorUtility.ToHtmlStringRGB(FloorColorSampler.Sample(pos)));

    /// <summary>死体の絵の真ん中 (覆いを置く所)</summary>
    public static Vector2 CenterOf(DeadBody body) => BodyCenter(body);

    // 絵の大きさは、体を覆う実績のあるポータルの絵 (VanillaHnsPortalMaker) の字の大きさに合わせ、横たわった死体の幅まで広げる
    private static string CoverArt(string hex)
    {
        var row = string.Concat(Enumerable.Repeat("W", CoverColumns));
        var sb = new System.Text.StringBuilder(CoverPrefix);
        for (var r = 0; r < CoverRows; r++)
        {
            sb.Append($"<alpha=#FF><mark=#{hex}FF><alpha=#00>").Append(row).Append("</mark>");
            if (r < CoverRows - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    public static void OnReportDeadBody()
    {
        foreach (var state in covers.Values) DespawnAll(state);
        covers.Clear();
    }

    /// <summary>画面の死体そのものを消せた客から、覆いを外す</summary>
    public static void HideCoversFrom(int clientId)
    {
        foreach (var state in covers.Values)
        {
            state.Done.Add(clientId);
            foreach (var cover in state.Objects) cover?.HideFromClient(clientId);
        }
    }

    private static void RemovePet(PlayerControl pc)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost) return;
        if (!(Options.ErasedBodyRemovePet?.GetBool() ?? true)) return;
        PetAbility.RemovePet(pc);
    }
}
