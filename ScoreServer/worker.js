// 世界ランキング（全インスタンス共通・名前なし）— Cloudflare Workers + Durable Object
//   /s/<点数>  : 点数を送る（0〜3000。ワールドが、その人の最高記録が上がった時だけ呼ぶ）
//   /n/<点数>  : 同じ。ただし国を保存しない（赤いスイッチ「国名なし」）
//   /top       : 上位 5 つの点数を返す {"top":[{"s":点数,"d":"日付","c":"国コード"}, ...]}
// 5 分ごとに、上位を GitHub Gist（scores.json）へ書き出す（ワールドはそれを読む。Gist は全員が読める）
// 同じ人の記録が何個も並ばないよう、接続元（IP）を「ランダムな塩」と混ぜた短いハッシュにして、その人の最高だけ残す
//   （IP そのものは保存しない。塩はこのサーバーの中だけにあり、外からはハッシュから IP を戻せない）
// 荒らし対策: 同じ接続元からは 10 秒に 1 回まで・1 日 100 回まで

const MAX_SCORE = 3000;
const KEEP = 50;     // 覚えておく記録の数（上位だけ）
const SHOW = 5;      // 返す数

export class Board {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.hits = new Map();
    this.day = "";
  }

  async fetch(request) {
    const url = new URL(request.url);
    const s = this.state.storage;
    let entries = (await s.get("entries")) || [];   // [{h, s, d}]
    let salt = await s.get("salt");
    if (!salt) { salt = crypto.randomUUID(); await s.put("salt", salt); }

    const m = url.pathname.match(/^\/([sn])\/(\d{1,4})$/);
    if (m) {
      const noCountry = m[1] === "n";
      const score = parseInt(m[2], 10);
      if (score < 0 || score > MAX_SCORE) return json({ ok: false });
      const ip = request.headers.get("cf-connecting-ip") || "unknown";
      const h = await hash(salt + ip);
      const now = Date.now();
      const today = new Date().toISOString().slice(0, 10);
      if (today !== this.day) { this.day = today; this.hits.clear(); }
      const hit = this.hits.get(h) || { last: 0, n: 0 };
      if (now - hit.last < 10000 || hit.n >= 100) return json({ ok: false, limited: true });
      hit.last = now; hit.n++;
      this.hits.set(h, hit);
      // 国: Cloudflare が接続元から判断する国コード（JP など。VPN だとその国になる）
      //   「国名なし」の時は国を保存しない
      const c = (!noCountry && request.cf && request.cf.country) ? String(request.cf.country).slice(0, 2) : "";
      const mine = entries.find(e => e.h === h);
      if (mine) {
        if (score > mine.s) { mine.s = score; mine.d = today; }
        if (score >= mine.s) mine.c = c;   // スイッチを切り替えた時は、同じ点数で送り直して国の表示を変える
      }
      else entries.push({ h, s: score, d: today, c });
      entries.sort((a, b) => b.s - a.s);
      entries = entries.slice(0, KEEP);
      await s.put("entries", entries);
      return json({ ok: true });
    }
    return json(top(entries));
  }
}

function top(entries) {
  return { top: entries.slice(0, SHOW).map(e => ({ s: e.s, d: e.d, c: e.c || "" })) };
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (!(url.pathname === "/top" || /^\/[sn]\/\d{1,4}$/.test(url.pathname))) return new Response("not found", { status: 404 });
    const stub = env.BOARD.get(env.BOARD.idFromName("global"));
    return stub.fetch(request);
  },

  // 5 分ごと: Gist へ書き出す
  async scheduled(event, env, ctx) {
    const stub = env.BOARD.get(env.BOARD.idFromName("global"));
    const res = await stub.fetch(new Request("https://board/top"));
    const data = await res.text();
    const r = await fetch(`https://api.github.com/gists/${env.GIST_ID}`, {
      method: "PATCH",
      headers: {
        "Authorization": `Bearer ${env.GITHUB_TOKEN}`,
        "Accept": "application/vnd.github+json",
        "User-Agent": "score-board",
      },
      body: JSON.stringify({ files: { "scores.json": { content: data } } }),
    });
    if (!r.ok) console.log("gist update failed", r.status, await r.text());
  },
};

function json(o) {
  return new Response(JSON.stringify(o), {
    headers: { "content-type": "application/json", "cache-control": "no-store", "access-control-allow-origin": "*" },
  });
}

async function hash(text) {
  const d = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(d)].slice(0, 8).map(b => b.toString(16).padStart(2, "0")).join("");
}
