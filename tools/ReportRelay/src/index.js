// TOHL のバグ・要望の報告を受け取り、開発者の Discord のチャンネルへ送る中継 (Cloudflare Workers)。
//
// MOD には Discord の Webhook の URL を書かず、この中継の URL だけを書く。
// Webhook の URL は Cloudflare の秘密の値 (DISCORD_WEBHOOK) にだけ置くので、ソースや DLL を読んでも分からない。
// 荒らされないよう、ここで次を確かめる:
//   ・フレンドコードの形 (例: word#1234)・止めたフレンドコード (BLOCKED)
//   ・同じフレンドコード / 同じ回線から送れる間隔と 1 日の回数
//   ・文の長さ・ファイルの数と大きさ・ファイルの種類 (画像と動画だけ)
//   ・@everyone などで人を呼ばない
// ソースが公開されているので「本物の MOD から来たか」は見分けられない。上の制限で、荒らしの量を抑える。

const MAX_TEXT = 2000;          // 報告の文 (1 行目の情報も入れた全体)
const MAX_FILES = 6;               // 画像・動画 5 個 + バグの報告のログ (log.zip)
const MAX_TOTAL_BYTES = 9_500_000; // Discord の Webhook の 1 通の上限 (10MB) より少し下
const ALLOWED_EXT = /\.(png|jpe?g|gif|bmp|webp|mp4|mov|webm|mkv|avi)$/i;
const LOG_NAME = /^log\.zip$/i;     // バグの報告に自動で付くゲームのログ (1 個だけ)
const FRIEND_CODE = /^[a-z0-9]{2,16}#\d{4}$/i;

const json = (status, body) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json; charset=utf-8" } });

// 間隔と 1 日の回数を数える。KV (REPORT_KV) が無ければ数えない (手元で試す時など)
async function checkRate(env, key, minIntervalSec, perDay) {
  if (!env.REPORT_KV) return null;
  const now = Math.floor(Date.now() / 1000);
  const day = new Date().toISOString().slice(0, 10);
  const last = Number((await env.REPORT_KV.get(`last:${key}`)) || 0);
  if (now - last < minIntervalSec) return "too_fast";
  const count = Number((await env.REPORT_KV.get(`day:${day}:${key}`)) || 0);
  if (count >= perDay) return "too_many";
  await env.REPORT_KV.put(`last:${key}`, String(now), { expirationTtl: Math.max(60, minIntervalSec * 2) });
  await env.REPORT_KV.put(`day:${day}:${key}`, String(count + 1), { expirationTtl: 60 * 60 * 26 });
  return null;
}

function blocked(env, fc) {
  const list = (env.BLOCKED || "").split(",").map((s) => s.trim().toLowerCase()).filter(Boolean);
  return list.includes(fc.toLowerCase());
}

// Discord の書式で人を呼んだり、リンクを埋め込んだりしないよう、記号を無害にする
function clean(s, max) {
  return String(s ?? "")
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, "")
    .replace(/@/g, "@​")
    .slice(0, max);
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/") return json(200, { ok: true, service: "tohl-report-relay" });
    if (request.method !== "POST" || url.pathname !== "/report") return json(404, { ok: false, error: "not_found" });
    if (!env.DISCORD_WEBHOOK) return json(500, { ok: false, error: "not_configured" });

    const size = Number(request.headers.get("content-length") || 0);
    if (size > MAX_TOTAL_BYTES + 200_000) return json(413, { ok: false, error: "too_large" });

    let form;
    try { form = await request.formData(); } catch { return json(400, { ok: false, error: "bad_form" }); }

    let info;
    try { info = JSON.parse(form.get("payload") || "{}"); } catch { return json(400, { ok: false, error: "bad_payload" }); }
    const fc = String(info.friendCode || "").trim();
    if (!FRIEND_CODE.test(fc)) return json(400, { ok: false, error: "no_friend_code" });
    if (blocked(env, fc)) return json(403, { ok: false, error: "blocked" });
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

    // 間隔: 同じフレンドコードは 60 秒に 1 回・1 日 10 回、同じ回線は 20 秒に 1 回・1 日 30 回
    const ip = request.headers.get("cf-connecting-ip") || "local";
    const byFc = await checkRate(env, `fc:${fc.toLowerCase()}`, 60, 10);
    if (byFc) return json(429, { ok: false, error: byFc });
    const byIp = await checkRate(env, `ip:${ip}`, 20, 30);
    if (byIp) return json(429, { ok: false, error: byIp });

    const head = [
      `${info.type === "request" ? "💡 **要望**" : "🐞 **バグ**"}  ${info.kind === "host" ? "ホストから" : "プレイヤーから"}  TOHL ${clean(info.version, 20)} (${clean(info.commit, 12)})`,
      `👤 ${clean(info.name, 40)}  \`${clean(fc, 24)}\`  ・ ホスト: ${clean(info.host, 40)}`,
      `📍 ${clean(info.situation, 80)}`,
    ].join("\n");
    const content = `${head}\n\`\`\`\n${clean(text, MAX_TEXT).replace(/```/g, "ʼʼʼ")}\n\`\`\``.slice(0, 1990);

    const out = new FormData();
    out.append("payload_json", JSON.stringify({
      content,
      username: "TOHL Report",
      allowed_mentions: { parse: [] },
    }));
    files.forEach((f, i) => out.append(`files[${i}]`, f, clean(f.name, 80)));

    const res = await fetch(env.DISCORD_WEBHOOK, { method: "POST", body: out });
    if (!res.ok) return json(502, { ok: false, error: "discord_" + res.status });
    return json(200, { ok: true });
  },
};
