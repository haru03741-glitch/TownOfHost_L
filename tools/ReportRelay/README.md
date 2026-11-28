# TOHL 報告の中継 (Cloudflare Workers)

ゲームの中の「バグ・要望の報告」(ロビー・試合中の報告のボタン・`/bug 内容`) を、開発者の Discord のチャンネルへ届ける中継です。

## なぜ中継を挟むか

ソースを公開しているので、MOD に Discord の Webhook の URL を書くと、誰でも読んで直接書き込めてしまいます (荒らされる)。
そこで MOD にはこの中継の URL だけを書き、Webhook の URL は Cloudflare の **秘密の値** にだけ置きます。

中継が確かめること:

| 確かめること | 決まり |
|---|---|
| フレンドコード | `word#1234` の形でなければ受け付けない |
| 止めたフレンドコード | `wrangler.toml` の `BLOCKED` にあれば受け付けない |
| 同じフレンドコード | 60 秒に 1 回・1 日 5 回まで (運営が Discord でボタンを押すと、その人はまた 5 回送れる) |
| 客からの報告 | 部屋のホストのフレンドコードが無ければ受け付けない |
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

## 「既読・修正中・修正済み」のボタンを付ける (bot)

Webhook ではボタンを付けられないので、bot で送ります。bot を入れると、届いた報告の下に **既読 / 修正中 / 修正済み** のボタンが付きます。

- 押せるのは運営だけ (`ADMIN_IDS` の人。空ならサーバーの管理者の権限を持つ人)。他の人が押すと「運営だけが押せます」と本人にだけ出る
- 押すと報告の最後に `📌 状態: ✅ 修正済み (押した人・時刻)` が付き、押したボタンが緑になる
- 押すと、その報告を送った人の今日の回数が 0 に戻り、また 5 回送れる

### 準備 (1 回だけ)

1. https://discord.com/developers/applications で「New Application」→ 名前を付けて作る (他の bot とは別のアプリにする)
2. 「Bot」→「Reset Token」でトークンを出してコピー (**誰にも見せない**)
3. 「Installation」(または OAuth2 → URL Generator) で `bot` を選び、権限は「メッセージを送る・メッセージ履歴を読む・ファイルを添付」。出た URL を開いて、自分のサーバーに入れる
4. 「General Information」の **Public Key** をコピーして、`wrangler.toml` の `DISCORD_PUBLIC_KEY` に書く
5. Discord の設定 →「詳細設定」→「開発者モード」を ON。報告を受け取るチャンネルを右クリック →「チャンネル ID をコピー」→ `wrangler.toml` の `DISCORD_CHANNEL_ID` に書く
6. ボタンを押せる人を決めるなら、その人を右クリック →「ユーザー ID をコピー」→ `ADMIN_IDS` に書く (何人でもカンマ区切り)
7. トークンを秘密の値に入れて、デプロイし直す

```powershell
npx wrangler secret put DISCORD_BOT_TOKEN
npx wrangler deploy
```

8. Developer Portal の「General Information」の **Interactions Endpoint URL** に、中継の URL の後ろに `/interactions` を付けて入れて保存する (例: `https://relay.nkzk.win/interactions`)。Discord が試しに送って、中継が正しく答えれば保存できる

bot を入れると Webhook (`DISCORD_WEBHOOK`) は使わなくなります。

## 荒らされた時

- 特定の人: `wrangler.toml` の `BLOCKED = "aaa#1111,bbb#2222"` に足して `npx wrangler deploy`
- Webhook ごと: Discord で Webhook を消して作り直し、`npx wrangler secret put DISCORD_WEBHOOK` で入れ替える
- 一時的に止める: `npx wrangler secret delete DISCORD_WEBHOOK` (中継は `not_configured` を返し、MOD の手元の記録は残る)

## 手元で試す

`.dev.vars` に `DISCORD_WEBHOOK=...` を書いて `npx wrangler dev`。`.dev.vars` は git に入らない。
