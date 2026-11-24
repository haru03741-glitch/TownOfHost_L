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
    private static readonly Dictionary<byte, Core.CustomNetObject> covers = new();
    // 死体そのものを消す電文を送った客 (死体の持ち主ごと)。この客には覆いを出さない
    private static readonly Dictionary<byte, HashSet<int>> erasedOnClients = new();

    private const int CoverColumns = 6;
    private const int CoverRows = 6;
    private const string CoverPrefix = "<size=70%><line-height=97%><cspace=0.16em>";

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
        if (bodyPos.HasValue) Cover(playerId, bodyPos.Value);
        RemovePet(Utils.GetPlayerById(playerId));
        Logger.Info($"{playerId} の死体を消しました", "DeadBodyEraser");
    }

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
            Logger.Info($"{playerId} の残った死体を隠しました", "DeadBodyEraser");
        }
        return pos;
    }

    /// <summary>本編の客の画面に残る死体を床の色で覆う (会議の通報で外れる)</summary>
    public static void CoverLeftover(byte playerId, Vector2 pos)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        // 同じ人の前の覆い (復活してまた死んで、また復活した時など) は外して、今の死体の所に作り直す
        if (covers.Remove(playerId, out var old)) old?.Despawn();
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
        var color = FloorColorSampler.Sample(pos);
        var cover = Core.CustomNetObject.Create(CoverArt(ColorUtility.ToHtmlStringRGB(color)), pos);
        if (cover == null) return;
        // 覆いが要るのは、死体が残る客だけ。死体そのものを消したホストと、消す電文を送った客には出さない
        erasedOnClients.TryGetValue(playerId, out var erasedOn);
        cover.HideFrom(p => p.AmOwner || (erasedOn != null && erasedOn.Contains(p.OwnerId)));
        covers[playerId] = cover;
    }

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
        foreach (var cover in covers.Values) cover?.Despawn();
        covers.Clear();
    }

    private static void RemovePet(PlayerControl pc)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost) return;
        if (!(Options.ErasedBodyRemovePet?.GetBool() ?? true)) return;
        PetAbility.RemovePet(pc);
    }
}
