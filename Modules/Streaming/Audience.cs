using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AmongUs.GameOptions;
using TownOfHostL.Attributes;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules.Streaming;

/// <summary>
/// 配信の視聴者が YouTube のコメント (!停電・!呪い 名前 など) で試合に手を出せるようにする。
/// 視聴者はコメントでポイントを貯め、干渉ごとの値段を払う。干渉は 1 本の待ち行列に積み、全体の間隔を空けて 1 つずつ行う
/// (視聴者の書き込みの量はホストが決められないので、送信の量をここで抑える)。どの干渉も本編のサボタージュ・ドア・設定の送信・チャットだけを使う。
/// </summary>
/// <remarks>視聴者のポイントと干渉の仕組みは End K not (waffle-ful/Aeterna-End-K-not Modules/Audience) を元にした。</remarks>
public static class Audience
{
    public static OptionItem Enabled;
    static OptionItem PointsPerComment, PointsCooldown, Interval, TargetCooldown, CurseDuration, BlessDuration;

    enum Kind { Blackout, Reactor, Comms, Doors, Curse, Bless, Voice }
    static readonly Dictionary<Kind, (OptionItem On, OptionItem Price)> kindOptions = new();
    static readonly (Kind Kind, int Price)[] DefaultPrices =
    {
        (Kind.Blackout, 100), (Kind.Reactor, 150), (Kind.Comms, 150), (Kind.Doors, 80), (Kind.Curse, 120), (Kind.Bless, 120), (Kind.Voice, 150),
    };

    public static void SetupCustomOption()
    {
        Enabled = BooleanOptionItem.Create("Audience", false, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.All);
        PointsPerComment = IntegerOptionItem.Create("AudiencePointsPerComment", new(1, 100, 1), 10, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetGameMode(CustomGameMode.All);
        PointsCooldown = IntegerOptionItem.Create("AudiencePointsCooldown", new(0, 300, 5), 30, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
        Interval = IntegerOptionItem.Create("AudienceInterval", new(3, 120, 1), 10, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
        TargetCooldown = IntegerOptionItem.Create("AudienceTargetCooldown", new(0, 600, 5), 60, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
        CurseDuration = IntegerOptionItem.Create("AudienceCurseDuration", new(1, 60, 1), 10, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
        BlessDuration = IntegerOptionItem.Create("AudienceBlessDuration", new(1, 60, 1), 10, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
        foreach (var (kind, price) in DefaultPrices)
        {
            var on = BooleanOptionItem.Create($"Audience{kind}", true, TabGroup.ModMainSettings, false).SetParent(Enabled)
                .SetGameMode(CustomGameMode.All);
            var p = IntegerOptionItem.Create($"Audience{kind}Price", new(0, 10000, 10), price, TabGroup.ModMainSettings, false).SetParent(on)
                .SetGameMode(CustomGameMode.All);
            kindOptions[kind] = (on, p);
        }
    }

    // コマンドの名前 (日本語と英語)。視聴者は全角の ！ や空白で打ちがちなので、読む前に半角へ寄せる
    static readonly Dictionary<string, Kind> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["停電"] = Kind.Blackout, ["blackout"] = Kind.Blackout,
        ["リアクター"] = Kind.Reactor, ["reactor"] = Kind.Reactor,
        ["通信"] = Kind.Comms, ["comms"] = Kind.Comms,
        ["ドア"] = Kind.Doors, ["doors"] = Kind.Doors,
        ["呪い"] = Kind.Curse, ["curse"] = Kind.Curse,
        ["祝福"] = Kind.Bless, ["bless"] = Kind.Bless,
        ["天の声"] = Kind.Voice, ["voice"] = Kind.Voice,
    };

    /// <summary>Id はポイントの持ち主 (チャンネルの ID)、Author は表示に使う名前</summary>
    readonly record struct Request(Kind Kind, string Id, string Author, byte Target, string Text);

    static readonly List<Request> queue = new();
    static readonly Dictionary<byte, float> targetCooldownUntil = new();
    static readonly HashSet<byte> cursed = new();
    static readonly HashSet<byte> blessed = new();
    static float lastRunAt = -999f;

    const float CursedVision = 0.5f;
    const float BlessedVision = 2f;
    const float BlessedSpeed = 0.2f;
    const string SpeedKey = "Audience.Bless";
    const int VoiceMaxLength = 60;
    const int QueueLimit = 30;

    [GameModuleInitializer]
    public static void Init()
    {
        // ロビーで積まれた干渉は試合では行わない (前の試合の PlayerId が別の人に当たらないよう、対象も捨てる)
        if (queue.Count > 0) Logger.Info($"試合の始めに待ちの干渉 {queue.Count} 件を捨てた", "Audience");
        queue.Clear();
        targetCooldownUntil.Clear();
        cursed.Clear();
        blessed.Clear();
    }

    public static int QueueCount => queue.Count;
    /// <summary>干渉を使うホストか (配信のコメントを読むのもこの時だけ)</summary>
    public static bool Wanted => Enabled != null && Enabled.GetBool() && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

    /// <summary>毎フレーム (メインスレッド)。待ち行列から 1 つ行う。コメントは <see cref="StreamHub"/> が配る</summary>
    public static void Tick()
    {
        if (!Wanted) { queue.Clear(); Points.SaveIfDirty(); return; }
        RunNext();
        Points.SaveIfDirty();
    }

    /// <summary>届いたコメントを受けてよいか (締め出した視聴者でないか)。名前と ID を覚える</summary>
    public static bool Accept(string id, string author)
    {
        Points.Remember(id, author);
        return !Points.IsBanned(id);
    }

    /// <summary>干渉のコマンドか。色は観客席の吹き出しと板の印に使う</summary>
    public static bool TryGetCommand(string text, out Color accent)
    {
        accent = default;
        if (!TryParse(text, out var kind, out _)) return false;
        accent = KindColor(kind);
        return true;
    }

    static Color KindColor(Kind kind) => kind switch
    {
        Kind.Blackout => new Color(1f, 0.84f, 0.30f),
        Kind.Reactor => new Color(1f, 0.36f, 0.30f),
        Kind.Comms => new Color(0.40f, 0.90f, 0.50f),
        Kind.Doors => new Color(1f, 0.60f, 0.25f),
        Kind.Curse => new Color(0.72f, 0.42f, 1f),
        Kind.Bless => new Color(0.40f, 0.88f, 1f),
        _ => new Color(1f, 0.52f, 0.80f),
    };

    static bool TryParse(string text, out Kind kind, out string rest)
    {
        kind = default;
        rest = "";
        text = text.Trim().Replace('！', '!').Replace('　', ' ');
        if (!text.StartsWith('!')) return false;
        var parts = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Commands.TryGetValue(parts[0], out kind)) return false;
        rest = parts.Length > 1 ? string.Join(' ', parts, 1, parts.Length - 1) : "";
        return true;
    }

    /// <summary>届いたコメント (締め出しの判定は済んでいる)。ポイントを足し、コマンドなら待ちに積む</summary>
    public static void OnComment(string id, string author, string text)
    {
        if (!Wanted || string.IsNullOrWhiteSpace(id)) return;
        Points.GrantForComment(id, PointsPerComment.GetInt(), PointsCooldown.GetInt());
        if (!TryParse(text, out var kind, out var rest)) return;
        if (queue.Count >= QueueLimit) { Logger.Info($"待ちが {QueueLimit} 件あるので捨てた ({author} {kind})", "Audience"); return; }

        var target = byte.MaxValue;
        if (kind is Kind.Curse or Kind.Bless)
        {
            var pc = FindPlayer(rest);
            if (pc == null) { Logger.Info($"対象が見つからない ({author} {kind} \"{rest}\")", "Audience"); return; }
            target = pc.PlayerId;
        }
        else if (kind == Kind.Voice && rest.Length == 0) return;
        queue.Add(new(kind, id, author, target, rest));
        Logger.Info($"干渉を待ちに積んだ (まだ行っていない): {kind} ({author}・待ち {queue.Count}・{Points.Get(id)} pt)", "Audience");
    }

    static PlayerControl FindPlayer(string part)
    {
        part = part.Trim();
        if (part.Length == 0) return null;
        PlayerControl hit = null;
        foreach (var pc in Main.AllPlayerControls)
        {
            var name = Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(pc.PlayerId, out var n) ? n : pc.Data?.PlayerName;
            if (name == null) continue;
            if (name.Equals(part, StringComparison.OrdinalIgnoreCase)) return pc;
            if (hit == null && name.Contains(part, StringComparison.OrdinalIgnoreCase)) hit = pc;
        }
        return hit;
    }

    /// <summary>今その干渉を行えるか。行えない物は待たせる (払ったポイントを無駄にしない)</summary>
    static bool CanRunNow(Kind kind)
    {
        var inTask = GameStates.IsInTask && !GameStates.IsMeeting && ExileController.Instance == null;
        return kind == Kind.Voice ? !GameStates.IsNotJoined : inTask;
    }

    static void RunNext()
    {
        if (queue.Count == 0 || Time.time - lastRunAt < Interval.GetInt()) return;
        var idx = queue.FindIndex(r => CanRunNow(r.Kind));
        if (idx < 0) return;
        var req = queue[idx];
        queue.RemoveAt(idx);
        if (Run(req)) lastRunAt = Time.time;
    }

    static bool Run(Request req)
    {
        var (on, priceOpt) = kindOptions[req.Kind];
        if (!on.GetBool()) { Logger.Info($"干渉を行わなかった (切ってある): {req.Kind} ({req.Author})", "Audience"); return false; }
        if (req.Target != byte.MaxValue && targetCooldownUntil.TryGetValue(req.Target, out var until) && Time.time < until)
        {
            Logger.Info($"干渉を行わなかった (同じ人への間隔): {req.Kind} ({req.Author})", "Audience");
            return false;
        }
        var price = priceOpt.GetInt();
        if (!Points.TrySpend(req.Id, price))
        {
            Logger.Info($"干渉を行わなかった (ポイント不足 {Points.Get(req.Id)}/{price}): {req.Kind} ({req.Author})", "Audience");
            return false;
        }
        bool ok;
        try
        {
            ok = req.Kind switch
            {
                Kind.Blackout => Sabotage(SystemTypes.Electrical),
                Kind.Reactor => Sabotage((MapNames)Main.NormalOptions.MapId switch
                {
                    MapNames.Polus => SystemTypes.Laboratory,
                    MapNames.Airship => SystemTypes.HeliSabotage,
                    _ => SystemTypes.Reactor,
                }),
                Kind.Comms => Sabotage(SystemTypes.Comms),
                Kind.Doors => CloseDoors(),
                Kind.Curse => Curse(req.Target),
                Kind.Bless => Bless(req.Target),
                Kind.Voice => Voice(req.Author, req.Text),
                _ => false,
            };
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "Audience");
            ok = false;
        }
        if (!ok)
        {
            // 行えなかった時 (もうサボ中・ドアの無いマップ・対象が死んでいる など) は払った分を返す
            Points.Add(req.Id, price);
            Logger.Info($"干渉を行えなかったので返した: {req.Kind} ({req.Author})", "Audience");
            return false;
        }
        if (req.Target != byte.MaxValue) targetCooldownUntil[req.Target] = Time.time + TargetCooldown.GetInt();
        Logger.Info($"干渉を行った: {req.Kind} ({req.Author}・{price} pt)", "Audience");
        StreamHub.OnExecuted(req.Id, req.Author, GetString($"Audience.Name.{req.Kind}"), KindColor(req.Kind));
        // 知らせが失敗しても、行った干渉は行ったまま (ポイントも返さない)
        try { if (req.Kind != Kind.Voice) Announce(req); }
        catch (Exception ex) { Logger.Exception(ex, "Audience"); }
        return true;
    }

    static string Title => $"<color=#ffd700>{GetString("Audience.Title")}</color>";

    static void Announce(Request req)
    {
        var what = GetString($"Audience.Name.{req.Kind}");
        var text = req.Target == byte.MaxValue
            ? string.Format(GetString("Audience.Done"), Name(req.Author), what)
            : string.Format(GetString("Audience.DoneTarget"), Name(req.Author), what, Main.AllPlayerNames.TryGetValue(req.Target, out var n) ? n : "?");
        Utils.SendMessageAutoSplit(text, true, Title);
    }

    /// <summary>視聴者の名前や文がチャットの書式を壊さないようにする</summary>
    static string Clean(string s) => s.Replace("<", "＜").Replace(">", "＞").Replace("\r", " ").Replace("\n", " ").Trim();
    /// <summary>視聴者の名前 (チャットの見出しに入るので長さも切る。1 通は 1200 バイトまで)</summary>
    static string Name(string author)
    {
        var n = Clean(author);
        return n.Length > 30 ? n[..30] : n;
    }

    // ===== 干渉 =====

    static bool Sabotage(SystemTypes type)
    {
        var ship = ShipStatus.Instance;
        if (ship == null || !ship.Systems.ContainsKey(type)) return false;
        // サボの無いモード (かくれんぼなど) と、設定で止めた種類は起こさない
        if (!Core.Modes.ModeCatalog.Current.AllowsSabotage) return false;
        if (type switch
        {
            SystemTypes.Electrical => Options.DisableSabotageLights.GetBool(),
            SystemTypes.Comms => Options.DisableSabotageComms.GetBool(),
            _ => Options.DisableSabotageReactor.GetBool(),
        }) return false;
        // 他のサボの最中は重ねない (本編も 1 つずつ)
        if (Utils.IsActive(SystemTypes.Electrical) || Utils.IsActive(SystemTypes.Comms) || Utils.IsActive(SystemTypes.Reactor)
            || Utils.IsActive(SystemTypes.Laboratory) || Utils.IsActive(SystemTypes.HeliSabotage) || Utils.IsActive(SystemTypes.LifeSupp)
            || Utils.IsActive(SystemTypes.MushroomMixupSabotage)) return false;
        // 停電は 128 と一緒に「倒すスイッチ」を下位 5 ビットで渡す (128 だけでは 1 つも倒れない)。他は 128 で始まる
        byte amount = type == SystemTypes.Electrical ? (byte)(128 | IRandom.Instance.Next(1, 32)) : (byte)128;
        ship.RpcUpdateSystem(type, amount);
        return true;
    }

    static bool CloseDoors()
    {
        var ship = ShipStatus.Instance;
        if (ship == null || !ship.Systems.ContainsKey(SystemTypes.Doors) || ship.AllDoors.Count == 0) return false;
        // ドアを閉められないモード・設定では、本編の処理が黙って止めるので、ここで断ってポイントを返す
        if (!Core.Modes.ModeCatalog.Current.AllowsDoorSabotage || Options.DisableSabotageDoors.GetBool()) return false;
        var rooms = ship.AllDoors.Select(d => d.Room).Distinct().ToList();
        ship.RpcCloseDoorsOfType(rooms[IRandom.Instance.Next(rooms.Count)]);
        return true;
    }

    static bool Curse(byte id)
    {
        var pc = Utils.GetPlayerById(id);
        if (pc == null || !pc.IsAlive()) return false;
        cursed.Add(id);
        pc.MarkDirtySettings();
        Expire(id, CurseDuration.GetInt(), () => cursed.Remove(id));
        return true;
    }

    static bool Bless(byte id)
    {
        var pc = Utils.GetPlayerById(id);
        if (pc == null || !pc.IsAlive()) return false;
        blessed.Add(id);
        PlayerSpeed.Add(id, SpeedKey, BlessedSpeed);
        pc.MarkDirtySettings();
        Expire(id, BlessDuration.GetInt(), () => { blessed.Remove(id); PlayerSpeed.Remove(id, SpeedKey); });
        return true;
    }

    /// <summary>効き目を切って設定を送り直す。LateTask は試合に属すので、試合が閉じたら走らない</summary>
    static void Expire(byte id, int seconds, Action undo)
    {
        _ = new LateTask(() =>
        {
            undo();
            var pc = Utils.GetPlayerById(id);
            if (pc != null && GameStates.IsInGame) pc.MarkDirtySettings();
        }, seconds, "Audience.Expire");
    }

    /// <summary>PlayerGameOptionsSender から、その人の設定を作る時に呼ぶ</summary>
    public static void ApplyGameOptions(byte id, IGameOptions opt)
    {
        float vision;
        if (cursed.Contains(id)) vision = CursedVision;
        else if (blessed.Contains(id)) vision = BlessedVision;
        else return;
        opt.SetVision(false);
        opt.SetFloat(FloatOptionNames.CrewLightMod, vision);
        opt.SetFloat(FloatOptionNames.ImpostorLightMod, vision);
    }

    static bool Voice(string author, string message)
    {
        message = Clean(message);
        if (message.Length > VoiceMaxLength) message = message[..VoiceMaxLength];
        if (message.Length == 0) return false;
        if (NgWords.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            Logger.Info($"天の声を NG ワードで止めた ({author})", "Audience");
            return false;
        }
        Utils.SendMessageAutoSplit(message, true, $"{Title} <size=70%>{Name(author)}</size>");
        return true;
    }

    const string NgWordsPath = "./TOHL_DATA/AudienceNgWords.txt";
    static readonly string[] DefaultNgWords = { "死ね", "殺すぞ", "きもい", "キモい", "うざい", "カス", "クズ", "kys", "kill yourself", "fuck", "shit" };
    static string[] ngWords;
    /// <summary>天の声で止める言葉。ホストが TOHL_DATA/AudienceNgWords.txt で直せる (1 行 1 語・# で始まる行は注釈)</summary>
    static string[] NgWords
    {
        get
        {
            if (ngWords != null) return ngWords;
            try
            {
                Directory.CreateDirectory("TOHL_DATA");
                if (!File.Exists(NgWordsPath))
                    File.WriteAllLines(NgWordsPath, new[] { "# 天の声で止める言葉 (1 行 1 語・部分一致・大文字小文字は区別しない)" }.Concat(DefaultNgWords));
                ngWords = File.ReadAllLines(NgWordsPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();
            }
            catch (Exception ex)
            {
                Logger.Warn($"NG ワードを読めなかったので既定の物を使う: {ex.Message}", "Audience");
                ngWords = DefaultNgWords;
            }
            return ngWords;
        }
    }

    // ===== ホストの命令 =====

    /// <summary>/yt URL|stop (配信のコメントを読む)</summary>
    public static void YouTubeCommand(ChatCommandContext c)
    {
        var arg = c.Arg(1).Trim();
        if (arg.Equals("stop", StringComparison.OrdinalIgnoreCase)) YouTubeChat.Stop();
        else if (arg.Length > 0 && !YouTubeChat.Start(arg)) { Reply(GetString("YouTube.BadUrl")); return; }
        Reply(YouTubeStatus());
    }

    public static string YouTubeStatus() => YouTubeChat.Active
        ? string.Format(GetString("YouTube.Status"), YouTubeChat.VideoId, YouTubeChat.LastStatus)
        : GetString("YouTube.Off");

    /// <summary>/audience points 名前 数 | ban 名前 | unban 名前 | top</summary>
    public static void AudienceCommand(ChatCommandContext c)
    {
        var sub = c.Arg(1).ToLowerInvariant();
        // 視聴者の名前は空白を含むことがあるので、数は最後の引数から取る
        string NameFrom(int start, int endExclusive) => string.Join(' ', c.Args.Skip(start).Take(endExclusive - start)).Trim();
        switch (sub)
        {
            case "points" when c.Args.Length >= 4 && int.TryParse(c.Args[^1], out var n):
                {
                    var name = NameFrom(2, c.Args.Length - 1);
                    var id = Points.IdOf(name);
                    Points.Add(id, n);
                    Reply($"{name}: {Points.Get(id)} pt");
                    return;
                }
            case "ban" when c.Args.Length >= 3:
                Points.SetBanned(Points.IdOf(NameFrom(2, c.Args.Length)), true);
                Reply(GetString("Audience.Banned"));
                return;
            case "unban" when c.Args.Length >= 3:
                Points.SetBanned(Points.IdOf(NameFrom(2, c.Args.Length)), false);
                Reply(GetString("Audience.Unbanned"));
                return;
            case "top":
                Reply(string.Join("\n", Points.Top(10).Select((p, i) => $"{i + 1}. {Points.NameOf(p.Key)}: {p.Value} pt")));
                return;
        }
        Reply(string.Format(GetString("Audience.Status"), GetString(Enabled.GetBool() ? "ColoredOn" : "ColoredOff"), queue.Count) + "\n" + YouTubeStatus());
    }

    static void Reply(string text) => HudManager.Instance?.Chat?.AddChat(PlayerControl.LocalPlayer, text);

    // ===== 視聴者のポイント =====

    /// <summary>
    /// 視聴者ごとのポイントと、締め出した視聴者。ホストの手元の TOHL_DATA/AudiencePoints.json にだけ持つ。
    /// 持ち主はチャンネルの ID で見分ける (表示名は他の人が同じ名前を名乗れる)。ホストの命令は名前で打つので、最後に見た名前から ID を引く
    /// </summary>
    static class Points
    {
        sealed class Data
        {
            public Dictionary<string, int> Points { get; set; } = new();
            public List<string> Banned { get; set; } = new();
        }

        const string FilePath = "./TOHL_DATA/AudiencePoints.json";
        const float SaveDelay = 30f;
        static Dictionary<string, int> points;
        static HashSet<string> banned;
        static readonly Dictionary<string, float> lastGrant = new();
        static readonly Dictionary<string, string> idByName = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> nameById = new(StringComparer.Ordinal);

        public static void Remember(string id, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            idByName[name] = id;
            nameById[id] = name;
        }
        /// <summary>名前から ID (この起動で見ていない名前はそのまま)</summary>
        public static string IdOf(string name) => idByName.TryGetValue(name, out var id) ? id : name;
        public static string NameOf(string id) => nameById.TryGetValue(id, out var n) ? n : id;
        static bool dirty;
        static float dirtySince;

        static void Load()
        {
            if (points != null) return;
            points = new(StringComparer.Ordinal);
            banned = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return;
                var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
                if (data == null) return;
                foreach (var (k, v) in data.Points) points[k] = v;
                foreach (var b in data.Banned) banned.Add(b);
            }
            catch (Exception ex) { Logger.Warn($"視聴者のポイントを読めなかった: {ex.Message}", "Audience"); }
        }

        static void MarkDirty()
        {
            if (!dirty) dirtySince = Time.realtimeSinceStartup;
            dirty = true;
        }

        public static void SaveIfDirty()
        {
            // 試合の終わりは待たずに書く
            if (!dirty || (Time.realtimeSinceStartup - dirtySince < SaveDelay && !GameStates.IsLobby)) return;
            dirty = false;
            try
            {
                Directory.CreateDirectory("TOHL_DATA");
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data { Points = new(points), Banned = banned.ToList() }));
            }
            catch (Exception ex) { Logger.Warn($"視聴者のポイントを書けなかった: {ex.Message}", "Audience"); }
        }

        public static bool IsBanned(string author) { Load(); return banned.Contains(author); }
        public static void SetBanned(string author, bool on)
        {
            Load();
            if (on ? banned.Add(author) : banned.Remove(author)) MarkDirty();
        }
        public static int Get(string author) { Load(); return points.TryGetValue(author, out var p) ? p : 0; }
        public static void Add(string author, int amount)
        {
            Load();
            if (string.IsNullOrEmpty(author) || amount == 0) return;
            points[author] = Get(author) + amount;
            MarkDirty();
        }
        public static void GrantForComment(string author, int amount, int cooldown)
        {
            var now = Time.realtimeSinceStartup;
            if (lastGrant.TryGetValue(author, out var last) && now - last < cooldown) return;
            lastGrant[author] = now;
            Add(author, amount);
        }
        public static bool TrySpend(string author, int price)
        {
            if (price <= 0) return true;
            var cur = Get(author);
            if (cur < price) return false;
            points[author] = cur - price;
            MarkDirty();
            return true;
        }
        public static IEnumerable<KeyValuePair<string, int>> Top(int n) { Load(); return points.OrderByDescending(p => p.Value).Take(n); }
    }
}
