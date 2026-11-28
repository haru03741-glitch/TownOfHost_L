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

## bot で送る (フォーラム・状態のボタン・ゲームの中のやりとり・一覧)

Webhook ではボタンもやりとりもできないので、bot で送ります。bot を入れると:

- **報告ごとにフォーラムに投稿を勝手に作る** (題は報告の 1 行目)。報告には MOD のバージョンと Among Us のバージョンも書く
- 投稿に **既読 / 修正中 / 修正済み** のボタンが付く。押せるのは運営だけ (`ADMIN_IDS` の人。空ならサーバーの管理者の権限を持つ人)。他の人が押すと「運営だけが押せます」と本人にだけ出る
- **状態はフォーラムのタグで表し、勝手に付け替える** (タグ「未対応・既読・修正中・修正済み・バグ・要望」は無ければ中継が作る)
  - 作った時は「未対応」
  - 運営が投稿の中で返事を書くと、「未対応」なら「既読」に
  - ボタンを押すとその状態に (投稿の中にも `📌 状態: …` と出る)
- ボタンを押すと、その報告を送った人の今日の回数が 0 に戻り、また 5 回送れる
- **ゲームの中でやりとりできる**: 報告の窓の「💬 やりとり」で、自分の報告の投稿の中の運営の返事を読み、返事を書ける (投稿に `🎮 名前 (ゲームから)` と出る)。運営は投稿の中に普通に書けばよい
- **ゲームの中で一覧を見られる**: 報告の窓の「📋 一覧」で、今あるバグと要望 (題・状態・中身の始め) を見られる。名前とフレンドコードは出さない

フォーラムを使わない時 (`DISCORD_CHANNEL_ID` だけ) は、チャンネルに送ったメッセージからスレッドを作ります (タグは付かない)。

### 準備 (1 回だけ)

1. https://discord.com/developers/applications で「New Application」→ 名前を付けて作る (他の bot とは別のアプリにする)
2. 「Bot」→「Reset Token」でトークンを出してコピー (**誰にも見せない**)
3. 「Installation」(または OAuth2 → URL Generator) で `bot` を選び、権限は「メッセージを送る・メッセージ履歴を読む・ファイルを添付」。出た URL を開いて、自分のサーバーに入れる
4. 「General Information」の **Public Key** をコピーして、`wrangler.toml` の `DISCORD_PUBLIC_KEY` に書く
5. Discord でフォーラムのチャンネルを作る (チャンネルを作成 →「フォーラム」)。設定 →「詳細設定」→「開発者モード」を ON にし、フォーラムを右クリック →「チャンネル ID をコピー」→ `wrangler.toml` の `DISCORD_FORUM_ID` に書く
   - bot には、そのフォーラムで「メッセージを送る・スレッドでメッセージを送る・投稿を作る・メッセージ履歴を読む・ファイルを添付・チャンネルの管理 (タグを作る・付け替える)」の権限を付ける
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
