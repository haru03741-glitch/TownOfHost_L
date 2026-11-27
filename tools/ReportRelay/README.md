# TOHL 報告の中継 (Cloudflare Workers)

ゲームの中の「バグ・要望の報告」(`/bug 内容`・ホストの操作盤の「報告を書く…」) を、開発者の Discord のチャンネルへ届ける中継です。

## なぜ中継を挟むか

ソースを公開しているので、MOD に Discord の Webhook の URL を書くと、誰でも読んで直接書き込めてしまいます (荒らされる)。
そこで MOD にはこの中継の URL だけを書き、Webhook の URL は Cloudflare の **秘密の値** にだけ置きます。

中継が確かめること:

| 確かめること | 決まり |
|---|---|
| フレンドコード | `word#1234` の形でなければ受け付けない |
| 止めたフレンドコード | `wrangler.toml` の `BLOCKED` にあれば受け付けない |
| 同じフレンドコード | 60 秒に 1 回・1 日 10 回まで |
| 同じ回線 (IP) | 20 秒に 1 回・1 日 30 回まで |
| 文 | 2000 字まで・空は受け付けない |
| ファイル | 画像と動画だけ・5 個まで・合計 9.5MB まで |
| @everyone など | 人を呼ばないように無害にする |

ソースが公開されている以上「本物の MOD から来たか」は見分けられません。上の制限で荒らしの量を抑え、荒らされたら `BLOCKED` に足すか、Webhook を作り直して秘密の値を入れ替えます (MOD の更新は要りません)。

## はじめの準備 (1 回だけ)

1. [Cloudflare](https://dash.cloudflare.com/sign-up) のアカウントを作る (無料)。
2. Discord で、報告を受け取るチャンネルの「連携サービス → ウェブフック → 新しいウェブフック」で Webhook を作り、URL をコピーしておく (**この URL は誰にも見せない・どこにも書かない**)。
3. このフォルダでターミナルを開いて、次を順に打つ。

```powershell
npx wrangler login
```
ブラウザが開くので、Cloudflare にログインして許可する。

```powershell
npx wrangler kv namespace create REPORT_KV
```
出てきた `id = "..."` を、`wrangler.toml` の `[[kv_namespaces]]` の 3 行の `#` を外して書き込む。

```powershell
npx wrangler secret put DISCORD_WEBHOOK
```
聞かれたら、2. でコピーした Webhook の URL を貼って Enter。

```powershell
npx wrangler deploy
```
最後に `https://tohl-report-relay.〇〇.workers.dev` のような URL が出る。

4. その URL を `Modules/BugReport.cs` の `DevRelayUrl` に書いて、MOD をビルドし直す。

```csharp
const string DevRelayUrl = "https://tohl-report-relay.〇〇.workers.dev";
```

## 荒らされた時

- 特定の人: `wrangler.toml` の `BLOCKED = "aaa#1111,bbb#2222"` に足して `npx wrangler deploy`
- Webhook ごと: Discord で Webhook を消して作り直し、`npx wrangler secret put DISCORD_WEBHOOK` で入れ替える
- 一時的に止める: `npx wrangler secret delete DISCORD_WEBHOOK` (中継は `not_configured` を返し、MOD の手元の記録は残る)

## 手元で試す

`.dev.vars` に `DISCORD_WEBHOOK=...` を書いて `npx wrangler dev`。`.dev.vars` は git に入らない。
