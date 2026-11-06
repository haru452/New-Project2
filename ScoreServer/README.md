# 世界ランキング（全インスタンス共通・名前なし）— サーバーの準備

ワールドの「世界ランキング 上位 5」を動かすためのサーバーです。
Cloudflare Workers（無料）で点数を受け取り、5 分ごとに GitHub Gist へ上位 5 を書き出します。ワールドは Gist を読んで表示します。
（折り鶴のときと同じ仕組みです）

## しくみ
| 役割 | URL | 届く人 |
|---|---|---|
| 点数を送る | `https://score-board.<あなた>.workers.dev/s/<点数>`（0〜3000。ワールドが自動で作る） | 「Allow Untrusted URLs」がオンの人 |
| 表示用の上位 5 | `https://gist.githubusercontent.com/<GitHubの名前>/<GistのID>/raw/scores.json` | 全員（Gist は信頼済みの場所） |

- 名前は送りません。点数だけです。ワールドにいる人は、その人の最高記録と同じ点数の行に名前が出ます。
- 国: サーバーが、接続元から国（JP など）を判断して記録と一緒に残します。表示されるのは国だけで、それより細かい場所は分かりません（VPN を使っている人は、その VPN の国になります）。
- 同じ人の記録が何個も並ばないよう、接続元をハッシュにして、その人の最高だけ残します（IP そのものは保存しません）。
- 荒らし対策: 同じ接続元からは 10 秒に 1 回まで、1 日 100 回まで。
- Gist は最大 5 分遅れで更新されます。
- 称号（アチーブメント）: ワールドが `/b/<番号>`（0〜31）を送ると、その人の記録に称号の番号を付けます（接続元のハッシュで同じ人と判断）。名前はワールド側で、選んだ言語で出します。番号の意味は `Assets/My script/drone/Achievements.cs` の先頭。

## 手順
1. **Node.js** が入っていなければ入れる: https://nodejs.org
2. **Cloudflare** のアカウント（折り鶴のときのものでよい）: https://dash.cloudflare.com
3. **Gist を作る**: https://gist.github.com で
   - ファイル名 `scores.json`、中身 `{"top":[]}`
   - 「Create secret gist」
   - できたページの URL の最後の英数字（32 文字）が **Gist の ID**
4. **GitHub のトークンを作る**: GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic) → Generate new token
   - チェックは **gist だけ**
5. このフォルダ（ScoreServer）で、コマンドを 1 行ずつ実行:
   ```
   npx wrangler login
   npx wrangler secret put GITHUB_TOKEN     （4 のトークンを貼る）
   npx wrangler secret put GIST_ID          （3 の Gist の ID を貼る）
   npx wrangler deploy
   ```
   最後に表示される `https://score-board.xxxx.workers.dev` が サーバーの URL。
6. ブラウザで `https://score-board.xxxx.workers.dev/top` を開き、`{"top":[]}` が出れば OK。
7. Claude に、5 の URL と、Gist のページの URL を教える（Claude がワールドに設定します）。
8. VRChat で確認する時は、設定の「Allow Untrusted URLs」をオンにする。

## 代わりの案（IP を使わない方法）— 2026-11-06 メモ・今は使っていない
今は「接続元（IP）をハッシュにして、同じ人の記録を 1 つにまとめる」方式。IP を使わずにやるなら次の方法がある。

**しくみ: 新しい記録を送る前に、前の記録を消す**
- ワールドは前に送った点数を覚えている（PlayerData の `worldSentBest`）。
- 記録が上がったら、まず `/r/<前の点数>`（その点数の記録を 1 つ消す）を送り、次に `/s/<新しい点数>` を送る。
- ワールド側には消す用の URL を 0〜3000 点ぶん（3001 個）追加で用意する（VRCUrl はゲーム中に作れないため）。
- 名前の表示は今と同じ（ワールドにいる人の最高記録と同じ点数の行に名前を出す。`WorldRanking.cs` の `NameFor`）。

**欠点**
- 荒らしに弱い: ワールドのデータから消す用の URL を抜き出せば、他人の記録も消せる（偽の点数を送られる危険は今も同じ）。
- 回数制限（10 秒に 1 回・1 日 100 回）が人ごとにできない。全体で「1 分に◯回まで」のような制限だけになる。
- 国の表示は IP から調べている。残すかやめるかを選ぶ（残す場合も、IP の保存や比較はしない）。

**選び方の候補**
1. IP を一切使わない（消す方式、国の表示と人ごとの回数制限もやめる）
2. IP は保存も比較もしない、国だけ残す（消す方式、国の表示は残す）
3. 今のまま（IP をハッシュにして使う）← 2026-11-06 時点ではこれを選んだ。荒らしに強く、同じ点数の人の記録も混ざらないので、仕組みとしてはこれが一番よい
   （2 の利点は「IP のハッシュも残さない」ことだけ。今もハッシュは塩付きで、IP には戻せない）

変えるときは worker.js と WorldRanking.cs を直し、`npx wrangler deploy` でサーバーを更新し直す。
