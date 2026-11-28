// TOHL のバグ・要望の報告を受け取り、開発者の Discord のチャンネルへ送る中継 (Cloudflare Workers)。
//
// MOD には Discord の URL や bot のトークンを書かず、この中継の URL だけを書く。
// 秘密の値 (bot のトークン・Webhook の URL) は Cloudflare の秘密の値にだけ置くので、ソースや DLL を読んでも分からない。
// 荒らされないよう、ここで次を確かめる:
//   ・フレンドコードの形 (例: word#1234)・止めたフレンドコード (BLOCKED)
//   ・客から送る時は、部屋のホストのフレンドコードがあること
//   ・同じフレンドコードは 1 日 5 回まで (運営がボタンを押すと、その人はまた 5 回送れる)・同じ回線の間隔と回数
//   ・文の長さ・ファイルの数と大きさ・ファイルの種類 (画像と動画とログだけ)
//   ・@everyone などで人を呼ばない
//
// bot (DISCORD_BOT_TOKEN と DISCORD_CHANNEL_ID) を入れると、報告に「既読・修正中・修正済み」のボタンが付く。
// ボタンは運営 (ADMIN_IDS の人か、サーバーの管理者の権限を持つ人) だけが押せる。押されると:
//   ・報告に状態 (誰がいつ押したか) が付く
//   ・送った人の今日の回数が 0 に戻り、また送れるようになる
// ボタンを押した知らせは Discord の「Interactions Endpoint URL」に入れた この中継の /interactions に届く (DISCORD_PUBLIC_KEY で本物か確かめる)。
// bot を入れていない時は、前と同じく Webhook (DISCORD_WEBHOOK) へボタン無しで送る。

const MAX_TEXT = 2000;          // 報告の文 (1 行目の情報も入れた全体)
const MAX_FILES = 6;               // 画像・動画 5 個 + バグの報告のログ (log.zip)
const MAX_TOTAL_BYTES = 9_500_000; // Discord の 1 通の上限 (10MB) より少し下
const ALLOWED_EXT = /\.(png|jpe?g|gif|bmp|webp|mp4|mov|webm|mkv|avi)$/i;
const LOG_NAME = /^log\.zip$/i;     // バグの報告に自動で付くゲームのログ (1 個だけ)
const FRIEND_CODE = /^[a-z0-9]{2,16}#\d{4}$/i;
const PER_DAY = 5;                  // 同じフレンドコードが 1 日に送れる数
const MIN_INTERVAL = 60;            // 同じフレンドコードが続けて送れる間隔 (秒)

// ボタン: custom_id は "st:<状態>:<フレンドコード>"
const STATUSES = {
  read: { label: "既読", emoji: "👀" },
  doing: { label: "修正中", emoji: "🔧" },
  fixed: { label: "修正済み", emoji: "✅" },
};
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
  if (!env.REPORT_KV || !FRIEND_CODE.test(fc)) return;
  const key = `fc:${fc.toLowerCase()}`;
  await env.REPORT_KV.delete(`day:${today()}:${key}`);
  await env.REPORT_KV.delete(`last:${key}`);
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

function buttons(fc, current) {
  return [{
    type: 1,
    components: Object.entries(STATUSES).map(([id, s]) => ({
      type: 2,
      style: id === current ? 3 : 2, // 今の状態は緑・他は灰色
      label: s.label,
      emoji: { name: s.emoji },
      custom_id: `st:${id}:${fc}`.slice(0, 100),
    })),
  }];
}

const useBot = (env) => Boolean(env.DISCORD_BOT_TOKEN && env.DISCORD_CHANNEL_ID);

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
    `${info.type === "request" ? "💡 **要望**" : "🐞 **バグ**"}  ${fromHost ? "ホストから" : "プレイヤーから"}  TOHL ${clean(info.version, 20)} (${clean(info.commit, 12)})`,
    `👤 ${clean(info.name, 40)}  \`${clean(fc, 24)}\`  ・ ホスト: ${clean(info.host, 40)}${hostLine}`,
    `📍 ${clean(info.situation, 80)}`,
  ].join("\n");
  const content = `${head}\n\`\`\`\n${clean(text, MAX_TEXT).replace(/```/g, "ʼʼʼ")}\n\`\`\``.slice(0, 1900);

  const body = { content, allowed_mentions: { parse: [] } };
  let target, headers = {};
  if (useBot(env)) {
    body.components = buttons(fc, null);
    target = `https://discord.com/api/v10/channels/${env.DISCORD_CHANNEL_ID}/messages`;
    headers = { authorization: `Bot ${env.DISCORD_BOT_TOKEN}` };
  } else {
    body.username = "TOHL Report";
    target = env.DISCORD_WEBHOOK;
  }
  const out = new FormData();
  out.append("payload_json", JSON.stringify(body));
  files.forEach((f, i) => out.append(`files[${i}]`, f, clean(f.name, 80)));

  const res = await fetch(target, { method: "POST", body: out, headers });
  if (!res.ok) return json(502, { ok: false, error: "discord_" + res.status });
  return json(200, { ok: true, perDay: PER_DAY });
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

async function handleInteraction(request, env) {
  const raw = await request.text();
  if (!(await verify(request, env, raw))) return new Response("bad signature", { status: 401 });
  const it = JSON.parse(raw);
  if (it.type === 1) return json(200, { type: 1 }); // PING

  if (it.type === 3) {
    const [tag, status, ...rest] = String(it.data?.custom_id || "").split(":");
    const fc = rest.join(":");
    if (tag !== "st" || !STATUSES[status]) return json(200, { type: 6 });
    if (!isAdmin(env, it)) {
      return json(200, { type: 4, data: { content: "このボタンは運営だけが押せます", flags: 64 } });
    }
    await resetRate(env, fc);
    const who = clean(it.member?.nick || it.member?.user?.global_name || it.member?.user?.username || it.user?.username || "?", 40);
    const s = STATUSES[status];
    const now = Math.floor(Date.now() / 1000);
    const lines = String(it.message?.content || "").split("\n").filter((l) => !l.startsWith(STATUS_LINE));
    lines.push(`${STATUS_LINE}${s.emoji} **${s.label}** (${who} ・ <t:${now}:f>)`);
    return json(200, {
      type: 7, // 押したメッセージを書き換える
      data: { content: lines.join("\n").slice(0, 2000), components: buttons(fc, status), allowed_mentions: { parse: [] } },
    });
  }
  return json(200, { type: 6 });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/") return json(200, { ok: true, service: "tohl-report-relay" });
    if (request.method === "POST" && url.pathname === "/report") return handleReport(request, env);
    if (request.method === "POST" && url.pathname === "/interactions") return handleInteraction(request, env);
    return json(404, { ok: false, error: "not_found" });
  },
};
