// TOHL のバグ・要望の報告を受け取り、開発者の Discord へ送る中継 (Cloudflare Workers)。
//
// MOD には Discord の URL や bot のトークンを書かず、この中継の URL だけを書く。
// 秘密の値 (bot のトークン・Webhook の URL) は Cloudflare の秘密の値にだけ置くので、ソースや DLL を読んでも分からない。
// 荒らされないよう、ここで次を確かめる:
//   ・フレンドコードの形 (例: word#1234)・止めたフレンドコード (BLOCKED)
//   ・客から送る時は、部屋のホストのフレンドコードがあること
//   ・同じフレンドコードは 1 日 5 回まで (運営が状態のボタンを押すと、その人はまた 5 回送れる)・同じ回線の間隔と回数
//   ・文の長さ・ファイルの数と大きさ・ファイルの種類 (画像と動画とログだけ)
//   ・@everyone などで人を呼ばない
//
// bot (DISCORD_BOT_TOKEN) を入れると:
//   ・報告ごとに、フォーラム (DISCORD_FORUM_ID) に投稿を作る (フォーラムが無ければ DISCORD_CHANNEL_ID のチャンネルに送ってスレッドを作る)
//   ・投稿に「既読・修正中・修正済み」のボタンを付ける。押せるのは運営 (ADMIN_IDS の人か、サーバーの管理者の権限を持つ人) だけ
//   ・状態はフォーラムのタグ (未対応・既読・修正中・修正済み) で表し、勝手に付け替える
//       - 作った時: 未対応 / 運営が投稿の中で返事をした時: 未対応なら既読 / ボタンを押した時: その状態
//   ・報告した人は、ゲームの中から投稿の中で運営とやりとりできる (/chat)。報告した人だけが持つ合言葉 (token) で確かめる
// ボタンを押した知らせは Discord の「Interactions Endpoint URL」に入れた この中継の /interactions に届く (DISCORD_PUBLIC_KEY で本物か確かめる)。
// bot を入れていない時は、前と同じく Webhook (DISCORD_WEBHOOK) へボタン無し・やりとり無しで送る。

const MAX_TEXT = 2000;          // 報告の文 (1 行目の情報も入れた全体)
const MAX_CHAT = 500;           // やりとりの 1 通の字数
const MAX_FILES = 6;               // 画像・動画 5 個 + バグの報告のログ (log.zip)
const MAX_TOTAL_BYTES = 9_500_000; // Discord の 1 通の上限 (10MB) より少し下
const ALLOWED_EXT = /\.(png|jpe?g|gif|bmp|webp|mp4|mov|webm|mkv|avi)$/i;
const LOG_NAME = /^log\.zip$/i;     // バグの報告に自動で付くゲームのログ (1 個だけ)
const FRIEND_CODE = /^[a-z0-9]{2,16}#\d{4}$/i;
const SNOWFLAKE = /^\d{5,25}$/;
const PER_DAY = 5;                  // 同じフレンドコードが 1 日に送れる報告の数
const MIN_INTERVAL = 60;            // 同じフレンドコードが続けて送れる間隔 (秒)
const GAME_PREFIX = "🎮 ";          // ゲームから送ったやりとりの印
const API = "https://discord.com/api/v10";

// 状態。ボタンの custom_id は "st:<状態>"
const STATUSES = {
  open: { label: "未対応", emoji: "🆕", button: false },
  read: { label: "既読", emoji: "👀", button: true },
  doing: { label: "修正中", emoji: "🔧", button: true },
  fixed: { label: "修正済み", emoji: "✅", button: true },
};
const KIND_TAGS = { bug: { label: "バグ", emoji: "🐞" }, request: { label: "要望", emoji: "💡" } };
const STATUS_LINE = "📌 状態: ";

const json = (status, body) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json; charset=utf-8" } });

const today = () => new Date().toISOString().slice(0, 10);

// 間隔と 1 日の回数を数える。KV (REPORT_KV) が無ければ数えない (手元で試す時など)
async function checkRate(env, key, minIntervalSec, perDay) {
  if (!env.REPORT_KV) return null;
  const now = Math.floor(Date.now() / 1000);
  const last = Number((await env.REPORT_KV.get(`last:${key}`)) || 0);
  if (now - last < minIntervalSec) return "too_fast";
  const count = Number((await env.REPORT_KV.get(`day:${today()}:${key}`)) || 0);
  if (count >= perDay) return "too_many";
  await env.REPORT_KV.put(`last:${key}`, String(now), { expirationTtl: Math.max(60, minIntervalSec * 2) });
  await env.REPORT_KV.put(`day:${today()}:${key}`, String(count + 1), { expirationTtl: 60 * 60 * 26 });
  return null;
}

// 運営がボタンを押した時: その人の今日の回数と間隔を戻す
async function resetRate(env, fc) {
  if (!env.REPORT_KV || !FRIEND_CODE.test(fc || "")) return;
  const key = `fc:${fc.toLowerCase()}`;
  await env.REPORT_KV.delete(`day:${today()}:${key}`);
  await env.REPORT_KV.delete(`last:${key}`);
}

// 報告 1 つ分の記録 (送った人・合言葉・状態)。キーは投稿 (スレッド) の ID
async function getRep(env, id) {
  if (!env.REPORT_KV || !SNOWFLAKE.test(id || "")) return null;
  const s = await env.REPORT_KV.get(`rep:${id}`);
  return s ? JSON.parse(s) : null;
}
async function putRep(env, id, rep) {
  if (env.REPORT_KV) await env.REPORT_KV.put(`rep:${id}`, JSON.stringify(rep), { expirationTtl: 60 * 60 * 24 * 120 });
}

// みんなが見られる報告の一覧 (題・種類・状態・日時・中身の始めだけ。名前やフレンドコードは載せない)
const INDEX_MAX = 300;
async function getIndex(env) {
  if (!env.REPORT_KV) return [];
  const s = await env.REPORT_KV.get("index");
  return s ? JSON.parse(s) : [];
}
async function updateIndex(env, id, change) {
  if (!env.REPORT_KV) return;
  const list = await getIndex(env);
  const i = list.findIndex((x) => x.id === id);
  if (i >= 0) Object.assign(list[i], change);
  else if (change.title !== undefined) list.unshift({ id, ...change });
  else return;
  await env.REPORT_KV.put("index", JSON.stringify(list.slice(0, INDEX_MAX)));
}

function listOf(s) {
  return String(s || "").split(",").map((x) => x.trim().toLowerCase()).filter(Boolean);
}

const blocked = (env, fc) => listOf(env.BLOCKED).includes(fc.toLowerCase());

// Discord の書式で人を呼んだり、リンクを埋め込んだりしないよう、記号を無害にする
function clean(s, max) {
  return String(s ?? "")
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, "")
    .replace(/@/g, "@​")
    .slice(0, max);
}

function randomToken() {
  const b = new Uint8Array(24);
  crypto.getRandomValues(b);
  return [...b].map((x) => x.toString(16).padStart(2, "0")).join("");
}

// 合言葉を、かかる時間で中身が分からないように比べる
function sameToken(a, b) {
  a = String(a || ""); b = String(b || "");
  if (a.length !== b.length || a.length === 0) return false;
  let d = 0;
  for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return d === 0;
}

function buttons(current) {
  return [{
    type: 1,
    components: Object.entries(STATUSES).filter(([, s]) => s.button).map(([id, s]) => ({
      type: 2,
      style: id === current ? 3 : 2, // 今の状態は緑・他は灰色
      label: s.label,
      emoji: { name: s.emoji },
      custom_id: `st:${id}`,
    })),
  }];
}

const useBot = (env) => Boolean(env.DISCORD_BOT_TOKEN && (env.DISCORD_FORUM_ID || env.DISCORD_CHANNEL_ID));

async function discord(env, method, path, body, isForm = false) {
  const headers = { authorization: `Bot ${env.DISCORD_BOT_TOKEN}` };
  if (body !== undefined && !isForm) headers["content-type"] = "application/json";
  const res = await fetch(API + path, { method, headers, body: body === undefined ? undefined : isForm ? body : JSON.stringify(body) });
  let data = null;
  try { data = await res.json(); } catch { /* 中身なし */ }
  return { ok: res.ok, status: res.status, data };
}

// ---- フォーラムのタグ ----

// フォーラムのタグの ID (名前 → ID)。無いタグは足す (bot に「チャンネルの管理」の権限が要る)。1 時間覚えておく
async function forumTags(env) {
  if (!env.DISCORD_FORUM_ID) return null;
  if (env.REPORT_KV) {
    const c = await env.REPORT_KV.get("tags");
    if (c) return JSON.parse(c);
  }
  const ch = await discord(env, "GET", `/channels/${env.DISCORD_FORUM_ID}`);
  if (!ch.ok) return null;
  let tags = ch.data.available_tags || [];
  const want = [...Object.values(STATUSES), ...Object.values(KIND_TAGS)];
  const missing = want.filter((w) => !tags.some((t) => t.name === w.label));
  if (missing.length > 0) {
    const all = [...tags.map((t) => ({ id: t.id, name: t.name, moderated: t.moderated, emoji_id: t.emoji_id, emoji_name: t.emoji_name })),
      ...missing.map((w) => ({ name: w.label, emoji_name: w.emoji, moderated: true }))].slice(0, 20);
    const p = await discord(env, "PATCH", `/channels/${env.DISCORD_FORUM_ID}`, { available_tags: all });
    if (p.ok) tags = p.data.available_tags || tags;
  }
  const map = {};
  for (const t of tags) map[t.name] = t.id;
  if (env.REPORT_KV) await env.REPORT_KV.put("tags", JSON.stringify(map), { expirationTtl: 3600 });
  return map;
}

function tagIds(map, rep) {
  if (!map) return null;
  const ids = [];
  const k = KIND_TAGS[rep.kind];
  if (k && map[k.label]) ids.push(map[k.label]);
  const s = STATUSES[rep.status];
  if (s && map[s.label]) ids.push(map[s.label]);
  return ids;
}

// 状態を変える: 記録・フォーラムのタグ・投稿の中のお知らせ
async function setStatus(env, id, rep, status, by, note) {
  rep.status = status;
  await putRep(env, id, rep);
  await updateIndex(env, id, { status });
  if (env.DISCORD_FORUM_ID) {
    const ids = tagIds(await forumTags(env), rep);
    if (ids) await discord(env, "PATCH", `/channels/${id}`, { applied_tags: ids });
  }
  if (note) {
    const s = STATUSES[status];
    await discord(env, "POST", `/channels/${id}/messages`, {
      content: `${STATUS_LINE}${s.emoji} **${s.label}**${by ? ` (${by})` : ""}`, allowed_mentions: { parse: [] },
    });
  }
}

// ---- 報告を受け取る ----

async function handleReport(request, env) {
  if (!useBot(env) && !env.DISCORD_WEBHOOK) return json(500, { ok: false, error: "not_configured" });

  const size = Number(request.headers.get("content-length") || 0);
  if (size > MAX_TOTAL_BYTES + 200_000) return json(413, { ok: false, error: "too_large" });

  let form;
  try { form = await request.formData(); } catch { return json(400, { ok: false, error: "bad_form" }); }

  let info;
  try { info = JSON.parse(form.get("payload") || "{}"); } catch { return json(400, { ok: false, error: "bad_payload" }); }
  const fc = String(info.friendCode || "").trim();
  if (!FRIEND_CODE.test(fc)) return json(400, { ok: false, error: "no_friend_code" });
  if (blocked(env, fc)) return json(403, { ok: false, error: "blocked" });
  // 客から送る時は、部屋のホストのフレンドコードが要る (ホストが持っていなければ送れない)
  const hostFc = String(info.hostFriendCode || "").trim();
  const fromHost = info.kind === "host";
  if (!fromHost && !FRIEND_CODE.test(hostFc)) return json(400, { ok: false, error: "no_host_friend_code" });
  if (!fromHost && blocked(env, hostFc)) return json(403, { ok: false, error: "blocked" });
  const text = String(info.text || "").trim();
  if (text.length === 0) return json(400, { ok: false, error: "empty" });
  const kind = info.type === "request" ? "request" : "bug";

  // ファイル: 数・大きさ・種類
  const files = form.getAll("files").filter((f) => typeof f === "object" && f && "arrayBuffer" in f);
  if (files.length > MAX_FILES) return json(400, { ok: false, error: "too_many_files" });
  let total = 0;
  let logs = 0;
  for (const f of files) {
    const isLog = LOG_NAME.test(f.name || "");
    if (isLog) logs++;
    if (!isLog && !ALLOWED_EXT.test(f.name || "")) return json(400, { ok: false, error: "bad_file_type" });
    total += f.size;
  }
  if (logs > 1) return json(400, { ok: false, error: "bad_file_type" });
  if (total > MAX_TOTAL_BYTES) return json(413, { ok: false, error: "too_large" });

  // 同じフレンドコードは 60 秒に 1 回・1 日 5 回、同じ回線は 20 秒に 1 回・1 日 30 回
  const ip = request.headers.get("cf-connecting-ip") || "local";
  const byFc = await checkRate(env, `fc:${fc.toLowerCase()}`, MIN_INTERVAL, PER_DAY);
  if (byFc) return json(429, { ok: false, error: byFc, perDay: PER_DAY });
  const byIp = await checkRate(env, `ip:${ip}`, 20, 30);
  if (byIp) return json(429, { ok: false, error: byIp });

  const hostLine = fromHost ? "" : `  \`${clean(hostFc, 24)}\``;
  const head = [
    `${kind === "request" ? "💡 **要望**" : "🐞 **バグ**"}  ${fromHost ? "ホストから" : "プレイヤーから"}`,
    `🧩 MOD: TOHL ${clean(info.version, 20)} (${clean(info.commit, 12)})  ・ 🎮 Among Us ${clean(info.gameVersion || "?", 30)}${info.platform ? ` (${clean(info.platform, 20)})` : ""}`,
    `👤 ${clean(info.name, 40)}  \`${clean(fc, 24)}\`  ・ ホスト: ${clean(info.host, 40)}${hostLine}`,
    `📍 ${clean(info.situation, 80)}`,
  ].join("\n");
  const content = `${head}\n\`\`\`\n${clean(text, MAX_TEXT).replace(/```/g, "ʼʼʼ")}\n\`\`\``.slice(0, 1900);
  // 投稿の題: 文の 1 行目
  const title = `${KIND_TAGS[kind].emoji} ${clean(text.split("\n")[0], 80) || KIND_TAGS[kind].label}`.slice(0, 95);

  const message = { content, allowed_mentions: { parse: [] } };
  const out = new FormData();
  const addFiles = () => files.forEach((f, i) => out.append(`files[${i}]`, f, clean(f.name, 80)));

  // bot が無い時: 前と同じく Webhook へ
  if (!useBot(env)) {
    out.append("payload_json", JSON.stringify({ ...message, username: "TOHL Report" }));
    addFiles();
    const res = await fetch(env.DISCORD_WEBHOOK, { method: "POST", body: out });
    if (!res.ok) return json(502, { ok: false, error: "discord_" + res.status });
    return json(200, { ok: true, perDay: PER_DAY });
  }

  message.components = buttons(null);
  const rep = { fc, name: clean(info.name, 40), kind, status: "open", token: randomToken(), at: Date.now() };
  let id;
  if (env.DISCORD_FORUM_ID) {
    // フォーラムに投稿を作る (投稿の ID = 最初のメッセージの ID)
    const tags = tagIds(await forumTags(env), rep);
    out.append("payload_json", JSON.stringify({ name: title, message, ...(tags ? { applied_tags: tags } : {}) }));
    addFiles();
    const r = await discord(env, "POST", `/channels/${env.DISCORD_FORUM_ID}/threads`, out, true);
    if (!r.ok) return json(502, { ok: false, error: "discord_" + r.status });
    id = r.data.id;
  } else {
    // チャンネルに送って、そのメッセージからスレッドを作る (スレッドの ID = メッセージの ID)
    out.append("payload_json", JSON.stringify(message));
    addFiles();
    const r = await discord(env, "POST", `/channels/${env.DISCORD_CHANNEL_ID}/messages`, out, true);
    if (!r.ok) return json(502, { ok: false, error: "discord_" + r.status });
    id = r.data.id;
    await discord(env, "POST", `/channels/${env.DISCORD_CHANNEL_ID}/messages/${id}/threads`, { name: title, auto_archive_duration: 10080 });
  }
  await putRep(env, id, rep);
  await updateIndex(env, id, {
    title: clean(text.split("\n")[0], 80), kind, status: "open", at: rep.at,
    summary: clean(text, 300).replace(/@\u200b/g, "@"),
  });
  return json(200, { ok: true, perDay: PER_DAY, id, token: rep.token });
}

// ---- ゲームの中のやりとり ----

async function handleChatGet(url, env) {
  if (!useBot(env)) return json(404, { ok: false, error: "no_chat" });
  const id = url.searchParams.get("id") || "";
  const rep = await getRep(env, id);
  if (!rep || !sameToken(rep.token, url.searchParams.get("token"))) return json(403, { ok: false, error: "bad_token" });
  const after = url.searchParams.get("after") || "";
  const q = SNOWFLAKE.test(after) ? `?limit=50&after=${after}` : "?limit=50";
  const r = await discord(env, "GET", `/channels/${id}/messages${q}`);
  if (!r.ok) return json(502, { ok: false, error: "discord_" + r.status });
  const list = (r.data || []).slice().sort((a, b) => (BigInt(a.id) < BigInt(b.id) ? -1 : 1));
  const messages = [];
  let staffReplied = false;
  for (const m of list) {
    if (m.id === id) continue; // 報告そのもの
    const bot = Boolean(m.author?.bot);
    let text = String(m.content || "");
    let from = "staff";
    if (bot && text.startsWith(GAME_PREFIX)) {
      from = "me";
      const i = text.indexOf("\n");
      text = i >= 0 ? text.slice(i + 1) : "";
    } else if (bot) {
      if (!text.startsWith(STATUS_LINE)) continue;
      from = "system";
    } else {
      staffReplied = true;
    }
    if ((m.attachments || []).length > 0) text += (text ? "\n" : "") + `📎 ×${m.attachments.length}`;
    messages.push({
      id: m.id, from, text: text.slice(0, 2000),
      name: from === "staff" ? (m.member?.nick || m.author?.global_name || m.author?.username || "?") : "",
      at: Date.parse(m.timestamp) || 0,
    });
  }
  // 運営が返事をしたら、未対応は既読にする
  if (staffReplied && rep.status === "open") await setStatus(env, id, rep, "read", "", true);
  return json(200, { ok: true, status: rep.status, messages });
}

async function handleChatPost(request, env) {
  if (!useBot(env)) return json(404, { ok: false, error: "no_chat" });
  let body;
  try { body = await request.json(); } catch { return json(400, { ok: false, error: "bad_payload" }); }
  const id = String(body.id || "");
  const rep = await getRep(env, id);
  if (!rep || !sameToken(rep.token, body.token)) return json(403, { ok: false, error: "bad_token" });
  if (blocked(env, rep.fc)) return json(403, { ok: false, error: "blocked" });
  const text = clean(String(body.text || "").trim(), MAX_CHAT);
  if (text.length === 0) return json(400, { ok: false, error: "empty" });
  // 同じ報告へは 3 秒に 1 回・1 日 100 回
  const rate = await checkRate(env, `chat:${id}`, 3, 100);
  if (rate) return json(429, { ok: false, error: rate });
  const r = await discord(env, "POST", `/channels/${id}/messages`, {
    content: `${GAME_PREFIX}**${rep.name}** (ゲームから)\n${text}`, allowed_mentions: { parse: [] },
  });
  if (!r.ok) return json(502, { ok: false, error: "discord_" + r.status });
  return json(200, { ok: true, id: r.data.id });
}

// ---- Discord のボタン ----

function hexToBytes(hex) {
  const out = new Uint8Array(hex.length / 2);
  for (let i = 0; i < out.length; i++) out[i] = parseInt(hex.substr(i * 2, 2), 16);
  return out;
}

// Discord から来た知らせか (Ed25519 の署名) を確かめる
async function verify(request, env, raw) {
  const sig = request.headers.get("x-signature-ed25519");
  const ts = request.headers.get("x-signature-timestamp");
  if (!sig || !ts || !env.DISCORD_PUBLIC_KEY) return false;
  const data = new TextEncoder().encode(ts + raw);
  for (const alg of [{ name: "Ed25519" }, { name: "NODE-ED25519", namedCurve: "NODE-ED25519" }]) {
    try {
      const key = await crypto.subtle.importKey("raw", hexToBytes(env.DISCORD_PUBLIC_KEY), alg, false, ["verify"]);
      return await crypto.subtle.verify(alg.name, key, hexToBytes(sig), data);
    } catch { /* 次の書き方で */ }
  }
  return false;
}

// 運営か: ADMIN_IDS に入っている人、または (ADMIN_IDS が空の時) サーバーの管理者の権限を持つ人
function isAdmin(env, it) {
  const id = it.member?.user?.id || it.user?.id || "";
  const ids = listOf(env.ADMIN_IDS);
  if (ids.length > 0) return ids.includes(id);
  try { return (BigInt(it.member?.permissions || "0") & 8n) === 8n; } catch { return false; }
}

async function handleInteraction(request, env, ctx) {
  const raw = await request.text();
  if (!(await verify(request, env, raw))) return new Response("bad signature", { status: 401 });
  const it = JSON.parse(raw);
  if (it.type === 1) return json(200, { type: 1 }); // PING

  if (it.type === 3) {
    const [tag, status, old] = String(it.data?.custom_id || "").split(":");
    if (tag !== "st" || !STATUSES[status]) return json(200, { type: 6 });
    if (!isAdmin(env, it)) {
      return json(200, { type: 4, data: { content: "このボタンは運営だけが押せます", flags: 64 } });
    }
    const who = clean(it.member?.nick || it.member?.user?.global_name || it.member?.user?.username || it.user?.username || "?", 40);
    const s = STATUSES[status];
    const now = Math.floor(Date.now() / 1000);
    const id = it.message?.id || "";
    const rep = await getRep(env, id);
    // 前の形のボタン (custom_id にフレンドコード) にも対応する
    await resetRate(env, rep?.fc || old);
    if (rep) {
      const work = setStatus(env, id, rep, status, who, true);
      if (ctx?.waitUntil) ctx.waitUntil(work); else await work;
    }
    const lines = String(it.message?.content || "").split("\n").filter((l) => !l.startsWith(STATUS_LINE));
    lines.push(`${STATUS_LINE}${s.emoji} **${s.label}** (${who} ・ <t:${now}:f>)`);
    return json(200, {
      type: 7, // 押したメッセージを書き換える
      data: { content: lines.join("\n").slice(0, 2000), components: buttons(status), allowed_mentions: { parse: [] } },
    });
  }
  return json(200, { type: 6 });
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/") return json(200, { ok: true, service: "tohl-report-relay" });
    if (request.method === "POST" && url.pathname === "/report") return handleReport(request, env);
    if (request.method === "GET" && url.pathname === "/chat") return handleChatGet(url, env);
    if (request.method === "GET" && url.pathname === "/list") return json(200, { ok: true, items: await getIndex(env) });
    if (request.method === "POST" && url.pathname === "/chat") return handleChatPost(request, env);
    if (request.method === "POST" && url.pathname === "/interactions") return handleInteraction(request, env, ctx);
    return json(404, { ok: false, error: "not_found" });
  },
};
