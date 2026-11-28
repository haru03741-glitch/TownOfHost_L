// TOHL の報告の中継を、自分の PC / VM で動かす鯖 (Cloudflare Workers の代わり)。
// 中身 (index.js) は Workers と同じ物を使い、ここでは次だけをする:
//   ・http で待ち受ける (Cloudflare のトンネルで外へ出す。例: https://srv.nkzk.win → localhost:8082)
//   ・設定.json を読んで、Workers の環境変数と同じ形で渡す
//   ・Workers の KV の代わりに、data/kv.json に覚える (期限付き)
// 動かし方: 起動.bat (中で node server.mjs)

import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const log = (...a) => console.log(`[${new Date().toLocaleString("ja-JP")}]`, ...a);

// ---- 設定 ----
const settingsPath = path.join(here, "設定.json");
let settings;
try {
  settings = JSON.parse(fs.readFileSync(settingsPath, "utf8").replace(/^﻿/, ""));
} catch (e) {
  console.error(`設定.json を読めません: ${e.message}`);
  process.exit(2);
}
const env = {};
for (const [k, v] of Object.entries(settings)) {
  if (k.startsWith("_")) continue; // 説明の欄
  if (v !== null && v !== undefined && String(v).trim() !== "") env[k] = String(v).trim();
}
const port = Number(env.PORT || 8082);
delete env.PORT;

// ---- KV の代わり (data/kv.json) ----
const dataDir = path.join(here, "data");
const kvPath = path.join(dataDir, "kv.json");
fs.mkdirSync(dataDir, { recursive: true });
let kv = {};
try { kv = JSON.parse(fs.readFileSync(kvPath, "utf8")); } catch { kv = {}; }
let saveTimer = null;
function saveSoon() {
  if (saveTimer) return;
  saveTimer = setTimeout(() => {
    saveTimer = null;
    const now = Date.now();
    for (const [k, v] of Object.entries(kv)) if (v.exp && v.exp < now) delete kv[k];
    const tmp = kvPath + ".tmp";
    fs.writeFileSync(tmp, JSON.stringify(kv));
    fs.renameSync(tmp, kvPath);
  }, 300);
}
env.REPORT_KV = {
  async get(key) {
    const e = kv[key];
    if (!e) return null;
    if (e.exp && e.exp < Date.now()) { delete kv[key]; saveSoon(); return null; }
    return e.v;
  },
  async put(key, value, opts = {}) {
    kv[key] = { v: String(value), exp: opts.expirationTtl ? Date.now() + opts.expirationTtl * 1000 : 0 };
    saveSoon();
  },
  async delete(key) { delete kv[key]; saveSoon(); },
};

// ---- 中継の中身 ----
const relayFile = fs.existsSync(path.join(here, "relay", "index.js")) ? path.join(here, "relay", "index.js") : path.join(here, "..", "src", "index.js");
const worker = (await import(pathToFileURL(relayFile).href)).default;

// ---- 確かめ ----
const useBot = env.DISCORD_BOT_TOKEN && (env.DISCORD_FORUM_ID || env.DISCORD_CHANNEL_ID);
if (!useBot && !env.DISCORD_WEBHOOK) {
  console.error("設定.json に DISCORD_BOT_TOKEN と DISCORD_FORUM_ID (または DISCORD_WEBHOOK) を入れてください");
  process.exit(2);
}
if (useBot && !env.DISCORD_PUBLIC_KEY) log("[!] DISCORD_PUBLIC_KEY が空なので、Discord のボタンが使えません");

// ---- 待ち受け ----
const server = http.createServer(async (req, res) => {
  const started = Date.now();
  try {
    const host = req.headers.host || `localhost:${port}`;
    const url = `http://${host}${req.url}`;
    const headers = new Headers();
    for (const [k, v] of Object.entries(req.headers)) {
      if (Array.isArray(v)) v.forEach((x) => headers.append(k, x));
      else if (v !== undefined) headers.set(k, v);
    }
    const hasBody = !["GET", "HEAD"].includes(req.method);
    const request = new Request(url, { method: req.method, headers, body: hasBody ? req : undefined, duplex: hasBody ? "half" : undefined });
    const pending = [];
    const ctx = { waitUntil: (p) => pending.push(Promise.resolve(p).catch((e) => log("後の処理で失敗:", e.message))) };
    const response = await worker.fetch(request, env, ctx);
    const body = Buffer.from(await response.arrayBuffer());
    const out = {};
    response.headers.forEach((v, k) => { out[k] = v; });
    res.writeHead(response.status, out);
    res.end(body);
    if (req.url !== "/") log(`${req.method} ${req.url.split("?")[0]} → ${response.status} (${Date.now() - started}ms)`);
    await Promise.all(pending);
  } catch (e) {
    log("失敗:", e.stack || e.message);
    if (!res.headersSent) res.writeHead(500, { "content-type": "application/json" });
    res.end(JSON.stringify({ ok: false, error: "server_error" }));
  }
});

server.listen(port, () => {
  log(`報告の中継を http://localhost:${port} で動かしています (止めるならこの窓を閉じる)`);
  log(useBot ? `bot で送ります (${env.DISCORD_FORUM_ID ? "フォーラム" : "チャンネル"})` : "Webhook で送ります (ボタン・やりとりは無し)");
});
server.on("error", (e) => {
  console.error(e.code === "EADDRINUSE" ? `ポート ${port} はもう使われています (前の鯖が動いたまま?)` : e.message);
  process.exit(3);
});
