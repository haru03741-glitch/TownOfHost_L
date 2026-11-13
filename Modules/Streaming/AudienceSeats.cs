using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHostL.Modules.Streaming;

/// <summary>
/// 画面の左下の「観客席」。2 段のひな壇に、コメントした視聴者がクルー (色と帽子はチャンネルごとに決まる) として座る。
/// コメントするとその子が跳ねて頭の上に吹き出しが出る。吹き出しは新しい物ほど頭のすぐ上に積み、古い物は上へ押し上げて消す。
/// 干渉のコマンドの吹き出しは干渉の色で縁取り、実際に発動するとその子が大きく跳ねて一回転し、光る。
/// 席を譲った子は右へ歩いて去る。絵はすべて HUD のカメラに置く (IMGUI は使わない)。
/// </summary>
/// <remarks>観客席の作り (席の並び・入場と退場・仕草・跳ね・光り方の数値) は End K not (waffle-ful/Aeterna-End-K-not Modules/YouTubeChat/AudienceSeats.cs) を元にした。</remarks>
sealed class AudienceSeats
{
    // 前列 5 席・後列 4 席 (後列は前列の間に半席ずらして座る)
    const int FrontSeats = 5;
    const int MaxSeats = 9;
    const float Step = 74f;
    const float RowLift = 46f;

    const float BubbleSeconds = 5f;
    const float BubbleFade = 0.6f;
    const int MaxBubbles = 4;
    const float HopSeconds = 0.32f;
    const float GlowSeconds = 1.6f;
    const int MaxTextLength = 40;
    const float WalkSpeed = 170f;
    const float StepsPerSecond = 4.5f;
    const float SpinSeconds = 0.55f;
    const float PicoGap = 0.12f;
    /// <summary>PoolablePlayer を大きさ 1 で置いた時の、帽子を除いた体の高さ (本編の単位)</summary>
    const float CrewUnitHeight = 1.6f;

    enum Fidget : byte { None, LookAround, Hop, Lean, Wiggle }

    sealed class Seat
    {
        public string Id, Author;
        public ViewerLook Look;
        public float LastActive, HopStart = -10f, HopHeight;
        public string Text;
        public bool IsCommand, IsPaid;
        public string Paid;
        public Color Accent;
        public float BubbleStart = -10f;
        public float GlowStart = -10f;
        public Color Glow;
        // 絵 (毎フレーム触るので Transform と出し入れの状態は手元に持つ。壊れたかは台 1 つで見る)
        public PoolablePlayer Crew;
        public Transform Tf;
        public SpriteRenderer GlowSr;
        public Transform GlowTf;
        public bool GlowOn;
        public float WalkFrom, WalkStart = -10f, WalkSeconds;
        public Fidget Fidget;
        public float FidgetStart = -10f, FidgetSeconds, NextFidget, LeanDir;
        public float SpinStart = -10f;
        public float RetrySpawnAt;
        public int BubbleSerial;
        /// <summary>出している吹き出し (出していなければ null) と、その高さ (押し上げを滑らかにするため人ごとに持つ)</summary>
        public Bubble Bubble;
        public float BubbleY;
        public bool HasBubbleY;
    }

    sealed class Leaver
    {
        public PoolablePlayer Crew;
        public Transform Tf;
        public float X, Y, Z, K, Start;
    }

    /// <summary>吹き出し 1 つぶんの絵 (使い回す)</summary>
    sealed class Bubble
    {
        public GameObject Root;
        public Transform RootTf, EdgeTf, FillTf, NameTf, BodyTf, TailTf;
        public bool Active, TailOn;
        public SpriteRenderer Edge, Fill, Tail;
        public TextMeshPro Name, Body;
        public int Serial = -1;
        public float W, H, BodyH;
        public int Rank = -1;
        public float Alpha = -1f;
        public Color EdgeColor, FillColor;
    }

    readonly Seat[] seats = new Seat[MaxSeats];
    readonly List<Leaver> leavers = new();
    readonly int[] order = new int[MaxSeats];
    readonly Bubble[] bubbles = new Bubble[MaxBubbles];
    float nextWalkSlot;
    float lastPico = -10f;
    GameObject stand;
    float builtScale = -1f;
    TextMeshPro signText;
    SpriteRenderer signEdge, signFill;
    /// <summary>今の大きさ (EK と同じく 1080 幅の画面で 0.5) と、台の左端・前列の足元 (画面の画素・下から)</summary>
    float s = 1f, left, floor;
    Transform root;

    public float BenchWidth => (FrontSeats * Step + 30f) * s;
    /// <summary>台の下端 (前の札の下端)</summary>
    public float Bottom => floor - 53f * s;
    public float Top => floor + RowLift * s + CrewHeight + 8f * s;
    float CrewHeight => 64f * s;
    float NameH => 20f * s;

    /// <summary>前の札の範囲 (画面の画素・下から)。押すと板を畳む</summary>
    public Area SignRect => new(left + BenchWidth * 0.5f - 62f * s, floor - 51f * s, 124f * s, 30f * s);

    public int Occupied
    {
        get
        {
            var n = 0;
            foreach (var seat in seats) if (seat != null) n++;
            return n;
        }
    }

    // ===== コメントを受ける =====

    public void OnArrived(StreamHub.Entry e, float now)
    {
        var seat = Find(e.Id);
        if (seat == null)
        {
            // 空席 (前列から)、無ければ一番長くコメントしていない人の席を譲ってもらう
            var pick = -1;
            for (var i = 0; i < MaxSeats; i++)
            {
                if (seats[i] == null) { pick = i; break; }
                if (pick < 0 || seats[i].LastActive < seats[pick].LastActive) pick = i;
            }
            if (seats[pick] != null) StartLeaving(seats[pick], pick, now);
            seat = seats[pick] = new Seat { Id = e.Id, Look = e.Look };
            // 台の左端の外から自分の席まで歩いてくる (同時に何人も来たら 0.35 秒ずつ間を空ける)
            seat.WalkFrom = -40f * s - (SeatX(pick) - left);
            seat.WalkStart = Fx.Max(now, nextWalkSlot);
            nextWalkSlot = seat.WalkStart + 0.35f;
            seat.WalkSeconds = -seat.WalkFrom / (WalkSpeed * s);
            seat.NextFidget = seat.WalkStart + seat.WalkSeconds + Fx.Range(2f, 5f);
        }
        seat.Author = e.Author;
        seat.Text = Trim(e.Text, MaxTextLength);
        seat.Paid = e.Paid;
        seat.IsPaid = e.Kind == StreamHub.EntryKind.Paid;
        seat.IsCommand = e.Accent.a > 0f;
        seat.Accent = seat.IsCommand ? e.Accent : Fx.Rgba(0.10f, 0.10f, 0.12f);
        seat.BubbleSerial = e.Serial;
        // 吹き出しは席に着いてから出す (歩いている子について横に動かさない)
        seat.BubbleStart = Fx.Max(now, seat.WalkStart + seat.WalkSeconds);
        seat.LastActive = now;
        seat.HopStart = now;
        seat.HopHeight = seat.IsPaid ? 24f : 14f;
        if (seat.IsPaid) { seat.GlowStart = now; seat.Glow = Fx.Rgba(1f, 0.83f, 0.25f); }

        // 来た合図の「ピコッ」。立て続けに来ても間を空ける。コマンドは少し低く
        if (StreamHub.Sound.Value && now - lastPico >= PicoGap)
        {
            lastPico = now;
            StreamSound.Pico(seat.IsCommand ? 0.8f : Fx.Range(0.95f, 1.12f));
        }
    }

    public void OnExecuted(StreamHub.Entry e, float now)
    {
        var seat = Find(e.Id);
        if (seat == null) return;
        seat.GlowStart = now;
        seat.Glow = e.Accent;
        seat.HopStart = now;
        seat.HopHeight = 34f;
        seat.SpinStart = now;
        seat.LastActive = now;
    }

    Seat Find(string id)
    {
        foreach (var seat in seats)
            if (seat != null && string.Equals(seat.Id, id, StringComparison.Ordinal)) return seat;
        return null;
    }

    static string Trim(string str, int max) => str.Length <= max ? str : str[..(max - 1)] + "…";

    // ===== 席の並び (画面の画素・下から) =====

    float SeatX(int i) => i < FrontSeats
        ? left + (15f + Step * 0.5f + i * Step) * s
        : left + (15f + Step + (i - FrontSeats) * Step) * s;

    float SeatFloor(int i) => i < FrontSeats ? floor : floor + RowLift * s;

    static bool Walking(Seat seat, float now) => now - seat.WalkStart < seat.WalkSeconds;

    /// <summary>頭の上の高さ</summary>
    float CrewTop(Seat seat, int i, float now)
    {
        var y = SeatFloor(i) + CrewHeight;
        // 歩いている間は一歩ごとに上下する
        var w = now - seat.WalkStart;
        if (w < seat.WalkSeconds) y += Fx.Abs(Fx.Sin(w * Fx.PI * StepsPerSecond)) * 5f * s;
        var h = (now - seat.HopStart) / HopSeconds;
        if (h < 1f) y += seat.HopHeight * s * Fx.Sin(h * Fx.PI);
        if (seat.Fidget == Fidget.Hop)
        {
            var f = (now - seat.FidgetStart) / seat.FidgetSeconds;
            if (f < 1f) y += 9f * s * Fx.Sin(f * Fx.PI);
        }
        return y;
    }

    float CrewX(Seat seat, int i, float now)
    {
        var x = SeatX(i);
        var w = now - seat.WalkStart;
        if (w < seat.WalkSeconds) x += seat.WalkFrom * (1f - w / seat.WalkSeconds);
        return x;
    }

    // 座っている間の仕草を時々選ぶ
    static void TickFidget(Seat seat, float now)
    {
        if (Walking(seat, now)) return;
        if (seat.Fidget != Fidget.None && now - seat.FidgetStart < seat.FidgetSeconds) return;
        seat.Fidget = Fidget.None;
        if (now < seat.NextFidget) return;
        var r = Fx.Value;
        seat.Fidget = r < 0.35f ? Fidget.LookAround : r < 0.6f ? Fidget.Hop : r < 0.85f ? Fidget.Lean : Fidget.Wiggle;
        seat.FidgetStart = now;
        seat.FidgetSeconds = seat.Fidget switch { Fidget.LookAround => Fx.Range(0.8f, 1.6f), Fidget.Hop => 0.3f, Fidget.Lean => 1.1f, _ => 0.9f };
        seat.LeanDir = Fx.Value < 0.5f ? -1f : 1f;
        seat.NextFidget = now + seat.FidgetSeconds + Fx.Range(2.5f, 6f);
    }

    // 傾き (度)。歩いている間は左右に揺れ、仕草で傾き、発動した時は一回転する
    static float CrewTilt(Seat seat, float now)
    {
        var w = now - seat.WalkStart;
        if (w < seat.WalkSeconds) return Fx.Sin(w * Fx.PI * StepsPerSecond) * 7f;
        var spin = (now - seat.SpinStart) / SpinSeconds;
        if (spin < 1f) return spin * spin * (3f - 2f * spin) * 360f;
        var f = (now - seat.FidgetStart) / Fx.Max(seat.FidgetSeconds, 0.01f);
        if (f >= 1f) return 0f;
        return seat.Fidget switch
        {
            // 傾けて少し止まり、戻す
            Fidget.Lean => seat.LeanDir * 9f * Fx.Sin(Fx.Min(f * 3f, 1f) * Fx.PI * 0.5f) * (f > 0.75f ? (1f - f) * 4f : 1f),
            Fidget.Wiggle => Fx.Sin(f * Fx.PI * 6f) * 8f * (1f - f),
            _ => 0f,
        };
    }

    // 向き: 歩いている間は進む方向、きょろきょろの間は反対を向く
    static bool FacesLeft(Seat seat, float now)
    {
        if (Walking(seat, now)) return seat.WalkFrom > 0f;
        var look = seat.Fidget == Fidget.LookAround && now - seat.FidgetStart < seat.FidgetSeconds;
        return look ? !seat.Look.FaceLeft : seat.Look.FaceLeft;
    }

    void StartLeaving(Seat old, int index, float now)
    {
        if (old.Crew != null)
        {
            leavers.Add(new Leaver
            {
                Crew = old.Crew,
                Tf = old.Tf,
                K = CrewHeight / CrewUnitHeight,
                Start = now,
                Z = index < FrontSeats ? -0.2f : -0.1f,
                X = SeatX(index),
                Y = SeatFloor(index) + CrewHeight * 0.5f,
            });
            old.Crew = null;
            old.Tf = null;
        }
        DestroySeatObjects(old);
    }

    // ===== 絵 =====

    /// <summary>毎フレーム。大きさと置き場所を受け取り、台・クルー・吹き出しを動かす</summary>
    public void Update(HudSpace space, float scale, float benchLeft, float benchFloor, float now)
    {
        root = space.Root.transform;
        s = scale;
        left = benchLeft;
        floor = benchFloor;

        // HUD ごと作り直されると絵も消えるので、参照を捨てる
        if (stand == null && builtScale > 0f)
        {
            builtScale = -1f;
            foreach (var seat in seats) if (seat != null) { seat.Crew = null; seat.Tf = null; seat.GlowSr = null; seat.GlowTf = null; seat.GlowOn = false; seat.Bubble = null; seat.HasBubbleY = false; }
            leavers.Clear();
            for (var i = 0; i < MaxBubbles; i++) bubbles[i] = null;
        }
        if (stand == null || !Fx.Same(builtScale, s)) BuildStand();
        if (stand == null) return;
        stand.transform.localPosition = space.Px(left, floor);

        TickLeavers(space, now);
        for (var i = 0; i < MaxSeats; i++)
        {
            var seat = seats[i];
            if (seat == null) continue;
            if (seat.Crew is null && now >= seat.RetrySpawnAt) SpawnCrew(seat, now);
            if (seat.Tf is null) continue;

            TickFidget(seat, now);
            var top = CrewTop(seat, i, now);
            var cx = CrewX(seat, i, now);
            var cy = top - CrewHeight * 0.5f;
            // 後列は前列より奥
            var z = i < FrontSeats ? -0.2f : -0.1f;
            var tf = seat.Tf;
            tf.localPosition = space.Px(cx, cy, z);
            tf.localRotation = Fx.RotZ(CrewTilt(seat, now));
            // ゆっくりした呼吸で縦にわずかに伸び縮みする。入場の順番待ちの間は見せない
            var breath = 1f + 0.025f * Fx.Sin(now * 2.2f + seat.Look.Phase);
            var k = now < seat.WalkStart ? 0f : CrewHeight / CrewUnitHeight;
            tf.localScale = Fx.V3(FacesLeft(seat, now) ? -k : k, k * breath, 1f);

            if (seat.GlowSr is not null)
            {
                var g = now - seat.GlowStart;
                if (g < GlowSeconds)
                {
                    var a = (1f - g / GlowSeconds) * (0.6f + 0.4f * Fx.Sin(g * 18f));
                    seat.GlowSr.color = Fx.Rgba(seat.Glow.r, seat.Glow.g, seat.Glow.b, a);
                    seat.GlowTf.localPosition = space.Px(cx, cy, z + 0.05f);
                    if (!seat.GlowOn) { seat.GlowOn = true; seat.GlowSr.enabled = true; }
                }
                else if (seat.GlowOn) { seat.GlowOn = false; seat.GlowSr.enabled = false; }
            }
        }
        UpdateBubbles(space, now);
    }

    public void SetVisible(bool on)
    {
        if (stand != null && stand.activeSelf != on) stand.SetActive(on);
        foreach (var seat in seats)
        {
            if (seat == null) continue;
            if (seat.Crew != null) seat.Crew.gameObject.SetActive(on);
            if (seat.GlowSr != null && !on) { seat.GlowSr.enabled = false; seat.GlowOn = false; }
        }
        foreach (var l in leavers) if (l.Crew != null) l.Crew.gameObject.SetActive(on);
        foreach (var b in bubbles) if (b?.Root != null && !on) { b.Root.SetActive(false); b.Active = false; }
    }

    /// <summary>札を作り直したので文字を入れ直す必要がある</summary>
    public bool SignStale { get; private set; }

    public void SetSign(string text)
    {
        if (signText == null) return;
        signText.text = text;
        SignStale = false;
        // 札の幅を文字に合わせる (縁と板の 2 枚)
        var sw = Fx.Max(120f * s, signText.GetPreferredValues(text).x + 24f * s);
        signEdge.size = Fx.V2(sw + 4f * s, signEdge.size.y);
        signFill.size = Fx.V2(sw, signFill.size.y);
        signText.rectTransform.sizeDelta = Fx.V2(sw, signFill.size.y);
    }

    /// <summary>吹き出しを出すか (板を観客席の上に積んだ時は、板の文を隠さないよう出さない)</summary>
    public bool Bubbles = true;

    void TickLeavers(HudSpace space, float now)
    {
        var exit = left + BenchWidth + 50f * s;
        for (var i = leavers.Count - 1; i >= 0; i--)
        {
            var l = leavers[i];
            if (l.Tf is null) { leavers.RemoveAt(i); continue; }
            var t = now - l.Start;
            var x = l.X + t * WalkSpeed * s;
            if (x > exit)
            {
                Object.Destroy(l.Crew.gameObject);
                leavers.RemoveAt(i);
                continue;
            }
            var bob = Fx.Abs(Fx.Sin(t * Fx.PI * StepsPerSecond)) * 5f * s;
            var tf = l.Tf;
            tf.localPosition = space.Px(x, l.Y + bob, l.Z);
            tf.localRotation = Fx.RotZ(Fx.Sin(t * Fx.PI * StepsPerSecond) * 7f);
            tf.localScale = Fx.V3(l.K, l.K, 1f);
        }
    }

    void BuildStand()
    {
        // 吹き出しの字の大きさも作る時に決まるので、一緒に作り直す
        foreach (var seat in seats) if (seat != null) Release(seat);
        for (var i = 0; i < MaxBubbles; i++)
        {
            if (bubbles[i]?.Root != null) Object.Destroy(bubbles[i].Root);
            bubbles[i] = null;
        }
        if (stand != null) Object.Destroy(stand);
        stand = null;
        signText = null;
        try
        {
            stand = new GameObject("TOHL_AudienceStand") { layer = 5 };
            var t = stand.transform;
            t.SetParent(root, false);
            builtScale = s;
            var w = BenchWidth;
            var stepH = 14f * s;
            var lift = RowLift * s;

            // ひな壇: 後ろの段 → 前の段 (段の上面は明るく、側面は暗く)
            var side = Fx.Rgba(0.16f, 0.17f, 0.22f, 0.95f);
            var topFace = Fx.Rgba(0.36f, 0.38f, 0.46f, 1f);
            var backTop = Fx.Rgba(0.30f, 0.32f, 0.40f, 1f);
            Art.Quad(t, side, w * 0.5f, lift * 0.5f, w * 0.92f, lift + stepH, -40);
            Art.Quad(t, backTop, w * 0.5f, lift, w * 0.92f, stepH * 0.45f, -39);
            Art.Quad(t, side, w * 0.5f, -stepH * 0.5f, w, stepH * 2f, -38);
            Art.Quad(t, topFace, w * 0.5f, 0f, w, stepH * 0.45f, -37);

            // 座席の背もたれ (前列は赤、後列は青のスタジアムの椅子)
            float seatW = 40f * s, seatH = 34f * s;
            for (var i = 0; i < MaxSeats; i++)
            {
                var x = SeatX(i) - left;
                var y = SeatFloor(i) - floor + seatH * 0.55f;
                var col = i < FrontSeats ? Fx.Rgba(0.78f, 0.18f, 0.20f, 1f) : Fx.Rgba(0.20f, 0.36f, 0.78f, 1f);
                var back = i < FrontSeats ? 0 : -6;
                Art.Panel(t, 7f * s, Fx.Rgba(0f, 0f, 0f, 0.6f), x, y - 2f * s, seatW * 1.1f, seatH * 1.08f, -34 + back);
                Art.Panel(t, 6f * s, col, x, y, seatW, seatH, -33 + back);
            }

            // 前の手すり (細い棒と支柱)。クルーの足元より手前
            var rail = Fx.Rgba(0.82f, 0.84f, 0.90f, 1f);
            var railY = -stepH * 1.5f;
            Art.Quad(t, rail, w * 0.5f, railY + 18f * s, w, 4f * s, 60);
            for (var k = 0; k <= 5; k++) Art.Quad(t, rail, w * k / 5f, railY + 9f * s, 3f * s, 18f * s, 60);

            // 前の札 (金の縁取りの紺の板)
            float sw = 120f * s, sh = 26f * s;
            var sy = railY - sh * 0.5f - 2f * s;
            signEdge = Art.Panel(t, 7f * s, Fx.Rgba(1f, 0.80f, 0.18f, 1f), w * 0.5f, sy, sw + 4f * s, sh + 4f * s, 61);
            signFill = Art.Panel(t, 6f * s, Fx.Rgba(0.07f, 0.09f, 0.20f, 1f), w * 0.5f, sy, sw, sh, 62);
            SignStale = true;
            signText = Art.Text(t, "Sign", 18f * s, Fx.Rgba(1f, 0.85f, 0.30f), TextAlignmentOptions.Center, 63);
            if (signText != null)
            {
                signText.rectTransform.sizeDelta = Fx.V2(sw, sh);
                signText.transform.localPosition = Fx.V3(w * 0.5f, sy, 0f);
                signText.fontStyle = FontStyles.Bold;
            }
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, nameof(AudienceSeats));
            if (stand != null) Object.Destroy(stand);
            stand = null;
        }
    }

    void SpawnCrew(Seat seat, float now)
    {
        try
        {
            var prefab = HudManager.InstanceExists && HudManager.Instance.IntroPrefab != null ? HudManager.Instance.IntroPrefab.PlayerPrefab : null;
            if (prefab == null || root == null) { seat.RetrySpawnAt = now + 5f; return; }
            var crew = Object.Instantiate(prefab, root);
            seat.Crew = crew;
            seat.Tf = crew.transform;
            var outfit = new NetworkedPlayerInfo.PlayerOutfit
            {
                PlayerName = "",
                ColorId = (byte)seat.Look.ColorId,
                HatId = seat.Look.HatId,
                SkinId = "skin_None",
                VisorId = "visor_EmptyVisor",
                PetId = "pet_EmptyPet",
                NamePlateId = "nameplate_NoPlate",
            };
            crew.UpdateFromPlayerOutfit(outfit, PlayerMaterial.MaskType.None, false, false);
            crew.ToggleName(false);
            crew.TogglePet(false);
            Art.SetLayer(crew.transform);

            var glow = new GameObject("Glow") { layer = 5 };
            glow.transform.SetParent(root, false);
            var gs = CrewHeight * 2.2f;
            glow.transform.localScale = Fx.V3(gs, gs, 1f);
            var sr = glow.AddComponent<SpriteRenderer>();
            sr.sprite = Art.GlowSprite;
            sr.sortingOrder = -5;
            sr.enabled = false;
            seat.GlowSr = sr;
            seat.GlowTf = glow.transform;
            seat.GlowOn = false;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, nameof(AudienceSeats));
            DestroySeatObjects(seat);
            seat.RetrySpawnAt = now + 5f;
        }
    }

    static void DestroySeatObjects(Seat seat)
    {
        Release(seat);
        if (seat.Crew != null) Object.Destroy(seat.Crew.gameObject);
        if (seat.GlowSr != null) Object.Destroy(seat.GlowSr.gameObject);
        seat.Crew = null;
        seat.Tf = null;
        seat.GlowSr = null;
        seat.GlowTf = null;
        seat.GlowOn = false;
    }

    // ===== 吹き出し =====

    void UpdateBubbles(HudSpace space, float now)
    {
        // 出ている吹き出しを新しい順に並べ、新しい物ほど下 (頭のすぐ上) に積む。上限を超えた古い物は出さない
        var n = 0;
        for (var i = 0; i < MaxSeats; i++)
        {
            var seat = seats[i];
            if (!Bubbles || seat == null || seat.Tf is null || now < seat.BubbleStart || now - seat.BubbleStart >= BubbleSeconds) continue;
            var k = n++;
            while (k > 0 && seats[order[k - 1]].BubbleStart < seat.BubbleStart) { order[k] = order[k - 1]; k--; }
            order[k] = i;
        }
        if (n > MaxBubbles) n = MaxBubbles;

        // 出さなくなった人の吹き出しを返す
        for (var i = 0; i < MaxSeats; i++)
        {
            var seat = seats[i];
            if (seat?.Bubble == null) continue;
            var keep = false;
            for (var j = 0; j < n; j++) if (order[j] == i) { keep = true; break; }
            if (keep) continue;
            Release(seat);
        }

        var dt = Fx.Min(Time.unscaledDeltaTime, 0.1f);
        var gap = 8f * s;
        // 後列の頭の上から積む
        var cursor = floor + RowLift * s + CrewHeight + 24f * s;
        for (var j = 0; j < n; j++)
        {
            var i = order[j];
            var seat = seats[i];
            var b = seat.Bubble ??= Take();
            if (b == null) continue;
            if (b.Serial != seat.BubbleSerial) Fill(b, seat);
            if (b.Rank != j) SetRank(b, j);

            var target = cursor;
            cursor += b.H + gap;
            // 積み直しで位置が変わる時は滑らかに寄せる (押し上げられる見え方)
            if (!seat.HasBubbleY) { seat.BubbleY = target - 10f * s; seat.HasBubbleY = true; }
            seat.BubbleY += (target - seat.BubbleY) * Fx.Min(1f, dt * 12f);

            var t = now - seat.BubbleStart;
            var alpha = t > BubbleSeconds - BubbleFade ? (BubbleSeconds - t) / BubbleFade : Fx.Min(1f, t / 0.12f);
            // 上限で押し出されかけている一番古い物は薄くする
            if (j == n - 1 && n == MaxBubbles) alpha *= 0.55f;
            Place(space, b, seat.BubbleY, CrewX(seat, i, now), CrewTop(seat, i, now), alpha, j);
        }
    }

    Bubble Take()
    {
        for (var j = 0; j < MaxBubbles; j++)
        {
            bubbles[j] ??= MakeBubble();
            var b = bubbles[j];
            if (b == null) return null;
            var used = false;
            foreach (var seat in seats) if (seat?.Bubble == b) { used = true; break; }
            if (!used) return b;
        }
        return null;
    }

    static void Release(Seat seat)
    {
        var b = seat.Bubble;
        seat.Bubble = null;
        seat.HasBubbleY = false;
        if (b?.Root == null) return;
        b.Root.SetActive(false);
        b.Active = false;
        b.Serial = -1;
    }

    /// <summary>新しい物ほど手前に描く</summary>
    static void SetRank(Bubble b, int rank)
    {
        b.Rank = rank;
        var o = 200 + (MaxBubbles - rank) * 10;
        b.Tail.sortingOrder = o;
        b.Edge.sortingOrder = o + 1;
        b.Fill.sortingOrder = o + 2;
        b.Name.sortingOrder = o + 3;
        b.Body.sortingOrder = o + 3;
    }

    Bubble MakeBubble()
    {
        try
        {
            var b = new Bubble { Root = new GameObject("Bubble") { layer = 5 } };
            b.Root.transform.SetParent(root, false);
            var t = b.Root.transform;
            b.Tail = Art.Quad(t, Color.white, 0f, 0f, 1f, 1f, 200);
            b.Edge = Art.Panel(t, 10f * s, Color.white, 0f, 0f, 10f, 10f, 201);
            b.Fill = Art.Panel(t, 9f * s, Color.white, 0f, 0f, 10f, 10f, 202);
            b.Name = Art.Text(t, "Name", 14f * s, Fx.Rgba(0.42f, 0.42f, 0.48f), TextAlignmentOptions.Left, 203);
            b.Body = Art.Text(t, "Body", 21f * s, Fx.Rgba(0.08f, 0.08f, 0.10f), TextAlignmentOptions.TopLeft, 203);
            if (b.Name == null || b.Body == null) { Object.Destroy(b.Root); return null; }
            b.RootTf = t;
            b.EdgeTf = b.Edge.transform;
            b.FillTf = b.Fill.transform;
            b.NameTf = b.Name.transform;
            b.BodyTf = b.Body.transform;
            b.TailTf = b.Tail.transform;
            b.TailOn = true;
            b.Name.fontStyle = FontStyles.Bold;
            b.Name.enableWordWrapping = false;
            b.Name.overflowMode = TextOverflowModes.Ellipsis;
            b.Body.enableWordWrapping = true;
            b.Root.SetActive(false);
            b.Active = false;
            return b;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, nameof(AudienceSeats));
            return null;
        }
    }

    /// <summary>吹き出しの中身を入れ、大きさを測る (中身が変わった時だけ)</summary>
    void Fill(Bubble b, Seat seat)
    {
        b.Serial = seat.BubbleSerial;
        var pad = 9f * s;
        var dot = ViewerLook.Hex(seat.Look.Color);
        var head = $"<color=#{dot}>●</color> {seat.Author}";
        if (seat.IsPaid) head = $"<color=#B07800>{seat.Paid}</color>  " + head;
        b.Name.text = head;
        b.Body.text = seat.Text;
        b.Body.gameObject.SetActive(seat.Text.Length > 0);
        var maxW = 280f * s;
        var nameW = b.Name.GetPreferredValues(head).x;
        var bodyW = seat.Text.Length > 0 ? b.Body.GetPreferredValues(seat.Text).x : 0f;
        b.W = Fx.Min(maxW, Fx.Max(nameW, bodyW) + pad * 2f);
        b.BodyH = seat.Text.Length > 0 ? b.Body.GetPreferredValues(seat.Text, b.W - pad * 2f, 0f).y : 0f;
        b.H = pad * 1.4f + NameH + b.BodyH;
        b.EdgeColor = seat.IsPaid ? Fx.Rgba(0.95f, 0.66f, 0.10f) : seat.Accent;
        b.FillColor = seat.IsPaid ? Fx.Rgba(1f, 0.95f, 0.72f) : Color.white;
        var bd = seat.IsCommand || seat.IsPaid ? 3.6f * s : 2f * s;
        b.Edge.size = Fx.V2(b.W + bd * 2f, b.H + bd * 2f);
        b.Fill.size = Fx.V2(b.W, b.H);
        // 行の高さより低い枠にすると、省く設定の文字は行ごと消えるので、字の大きさより十分高くする
        b.Name.rectTransform.sizeDelta = Fx.V2(b.W - pad * 2f, NameH * 1.4f);
        b.Body.rectTransform.sizeDelta = Fx.V2(b.W - pad * 2f, b.BodyH + 2f);
        // 中の物の位置は大きさが決まった時だけ置く
        b.EdgeTf.localPosition = Fx.V3(b.W * 0.5f, b.H * 0.5f, 0f);
        b.FillTf.localPosition = Fx.V3(b.W * 0.5f, b.H * 0.5f, 0f);
        b.NameTf.localPosition = Fx.V3(b.W * 0.5f, b.H - pad * 0.6f - NameH * 0.5f, 0f);
        b.BodyTf.localPosition = Fx.V3(b.W * 0.5f, b.H - pad * 0.6f - NameH - b.BodyH * 0.5f, 0f);
        b.Alpha = -1f;
    }

    void Place(HudSpace space, Bubble b, float by, float headX, float headTop, float alpha, int index)
    {
        if (!b.Active) { b.Active = true; b.Root.SetActive(true); }
        var pad = 9f * s;
        // 観客席の幅の内側に収める (右隣のコメントの板やロビーの開始のボタンに掛けない)
        var bx = Fx.Clamp(headX - b.W * 0.5f, Fx.Max(4f * s, left), Fx.Max(left, left + BenchWidth - b.W));
        // 吹き出しの中心を根の座標で置き、中の物は吹き出しの左下からの位置で置く
        b.RootTf.localPosition = space.Px(bx, by, -0.3f - index * 0.01f);
        // 吹き出しから自分の頭まで細い線を下ろす (誰の発言かが分かる)
        var lineBottom = headTop + 2f * s - by;
        var showTail = lineBottom < 0f;
        if (showTail != b.TailOn) { b.TailOn = showTail; b.Tail.enabled = showTail; }
        if (showTail)
        {
            b.TailTf.localPosition = Fx.V3(headX - bx, lineBottom * 0.5f, 0f);
            b.TailTf.localScale = Fx.V3(3f * s, -lineBottom, 1f);
        }
        if (Fx.Abs(alpha - b.Alpha) < 0.02f) return;
        b.Alpha = alpha;
        b.Edge.color = Fx.Rgba(b.EdgeColor.r, b.EdgeColor.g, b.EdgeColor.b, alpha);
        b.Fill.color = Fx.Rgba(b.FillColor.r, b.FillColor.g, b.FillColor.b, alpha);
        b.Tail.color = Fx.Rgba(b.EdgeColor.r, b.EdgeColor.g, b.EdgeColor.b, alpha * 0.8f);
        b.Name.alpha = alpha;
        b.Body.alpha = alpha;
    }

    // ===== 片付け =====

    public void Destroy()
    {
        foreach (var seat in seats) if (seat != null) DestroySeatObjects(seat);
        Array.Clear(seats);
        foreach (var l in leavers) if (l.Crew != null) Object.Destroy(l.Crew.gameObject);
        leavers.Clear();
        foreach (var b in bubbles) if (b?.Root != null) Object.Destroy(b.Root);
        Array.Clear(bubbles);
        if (stand != null) Object.Destroy(stand);
        stand = null;
        builtScale = -1f;
    }
}
