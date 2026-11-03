using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.Persistence;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

// =============================================================
//  世界ランキング（全インスタンス共通・名前なし・上位 5）
//  ・自分の最高記録（1 人用）が上がったら、その点数の URL をサーバーへ送る（名前は送らない）
//    URL はゲーム中に作れないので、0〜3000 点の URL をアップロード前に全部用意してある
//  ・2 分ごとに Gist（全員が読める場所）から上位 5 を読んで表示
//  ・今ワールドにいる人の最高記録と同じ点数の行には、その人の名前を出す
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WorldRanking : UdonSharpBehaviour
{
    public TextMeshPro text;
    public string title = "WORLD RANKING  -  TOP 5";   // English の見出し（日本語・中文は Title() で出す）
    public Subtitles subtitles;          // 選んだ言語（0 = English, 1 = 日本語, 2 = 中文）
    private int shownLang = -1;
    public VRCUrl[] submitUrls;          // 0〜3000 点
    public VRCUrl[] submitUrlsNC;        // 0〜3000 点・国名なし（赤いスイッチ）
    public VRCUrl readUrl;               // Gist の scores.json
    public float readInterval = 120f;

    [Header("送るかどうかのスイッチ（プレイヤーごと・PlayerData に保存。最初はオフ）")]
    public TextMeshPro switchLabel;
    public Renderer switchRenderer;
    public Material onMat, offMat;
    private bool optIn;

    [Header("国名なしで参加する赤いスイッチ（プレイヤーごと・最初はオフ = 国名を出す）")]
    public TextMeshPro countryLabel;
    public Renderer countryRenderer;
    public Material redOnMat, redOffMat;
    private bool hideCountry;
    private bool resend;                 // スイッチを切り替えた: 今の最高記録を送り直して国の表示を変える

    private int pending = -1;            // 送りたい点数
    private int sent = -1;               // 送った点数（このワールドにいる間）
    private float nextSend;
    private float nextRead = 3f;
    private float nextDraw;
    private bool busy;
    private float busyUntil;
    private int[] topS = new int[5];
    private string[] topD = new string[5];
    private string[] topC = new string[5];   // 国コード（JP など）
    private int topN = -1;               // -1 = まだ読めていない
    private bool readFailed;

    void Start() { Draw(); ShowSwitch(); }

    // スイッチを押した: 送る / 送らない を切り替える（自分だけ）
    public void _ToggleOptIn()
    {
        optIn = !optIn;
        PlayerData.SetBool("worldOptIn", optIn);
        nextSend = Time.time + 1f;
        ShowSwitch();
    }

    // 赤いスイッチを押した: 国名を出す / 出さない を切り替える（自分だけ）
    public void _ToggleCountry()
    {
        hideCountry = !hideCountry;
        PlayerData.SetBool("worldHideCountry", hideCountry);
        if (sent >= 0) resend = true;
        nextSend = Mathf.Max(nextSend, Time.time + 1f);
        ShowSwitch();
    }

    int Lang()
    {
        int l = subtitles != null ? subtitles._Lang() : 0;
        if (l < 0 || l > 2) l = 0;
        return l;
    }

    // 文字は English / 日本語 / 中文の順
    void ShowSwitch()
    {
        int l = Lang();
        shownLang = l;
        if (countryLabel != null)
        {
            string[] cOn = {
                "<b>HIDE MY COUNTRY: <color=#ff7a7a>ON</color></b>\n<size=65%>Join the ranking without your country</size>",
                "<b>国名を隠す: <color=#ff7a7a>オン</color></b>\n<size=65%>国名なしでランキングに参加します</size>",
                "<b>隐藏我的国家: <color=#ff7a7a>开</color></b>\n<size=65%>不显示国家参加排行榜</size>" };
            string[] cOff = {
                "<b>HIDE MY COUNTRY: <color=#c8d4e0>OFF</color></b>\n<size=65%>Your country is shown on the ranking</size>",
                "<b>国名を隠す: <color=#c8d4e0>オフ</color></b>\n<size=65%>ランキングに国名が表示されます</size>",
                "<b>隐藏我的国家: <color=#c8d4e0>关</color></b>\n<size=65%>排行榜上会显示你的国家</size>" };
            countryLabel.text = hideCountry ? cOn[l] : cOff[l];
        }
        if (countryRenderer != null && redOnMat != null && redOffMat != null) countryRenderer.sharedMaterial = hideCountry ? redOnMat : redOffMat;
        if (switchLabel != null)
        {
            string[] sOn = {
                "<b>SEND MY BEST: <color=#7fff9a>ON</color></b>\n<size=65%>Send my best score to the World Ranking</size>",
                "<b>最高記録を送る: <color=#7fff9a>オン</color></b>\n<size=65%>自分の最高得点を世界ランキングに送ります</size>",
                "<b>发送我的最高分: <color=#7fff9a>开</color></b>\n<size=65%>把我的最高分发送到世界排行榜</size>" };
            string[] sOff = {
                "<b>SEND MY BEST: <color=#ff8a7a>OFF</color></b>\n<size=65%>Press to send your best score</size>",
                "<b>最高記録を送る: <color=#ff8a7a>オフ</color></b>\n<size=65%>押すと最高得点を送ります</size>",
                "<b>发送我的最高分: <color=#ff8a7a>关</color></b>\n<size=65%>按下即可发送你的最高分</size>" };
            switchLabel.text = optIn ? sOn[l] : sOff[l];
        }
        if (switchRenderer != null && onMat != null && offMat != null) switchRenderer.sharedMaterial = optIn ? onMat : offMat;
    }

    string Title()
    {
        int l = Lang();
        if (l == 1) return "世界ランキング  -  TOP 5";
        if (l == 2) return "世界排行榜  -  TOP 5";
        return title;
    }

    // JoinLog から: 自分の最高記録が上がった
    public void _Offer(int score)
    {
        if (score > pending) pending = score;
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player != null && player.isLocal)
        {
            // 前に送った点数は、もう送らない
            if (PlayerData.HasKey(player, "worldSentBest")) sent = PlayerData.GetInt(player, "worldSentBest");
            optIn = PlayerData.HasKey(player, "worldOptIn") && PlayerData.GetBool(player, "worldOptIn");
            hideCountry = PlayerData.HasKey(player, "worldHideCountry") && PlayerData.GetBool(player, "worldHideCountry");
            ShowSwitch();
            if (PlayerData.HasKey(player, "soloBestScore")) _Offer(PlayerData.GetInt(player, "soloBestScore"));
        }
        nextDraw = 0f;
    }
    public override void OnPlayerJoined(VRCPlayerApi player) { nextDraw = Time.time + 2f; }
    public override void OnPlayerLeft(VRCPlayerApi player) { nextDraw = Time.time + 1f; }

    void Update()
    {
        if (busy && Time.time > busyUntil) busy = false;   // 返事が来なくても先へ
        if (subtitles != null && subtitles._Lang() != shownLang) { ShowSwitch(); Draw(); }   // 言語を選び直した
        VRCUrl[] urls = hideCountry ? submitUrlsNC : submitUrls;
        if (optIn && !busy && (pending > sent || resend) && Mathf.Max(pending, sent) >= 0 && Time.time >= nextSend && urls != null && urls.Length > 0)
        {
            int best = Mathf.Max(pending, sent);
            int s = Mathf.Clamp(best, 0, urls.Length - 1);
            if (urls[s] != null)
            {
                busy = true; busyUntil = Time.time + 15f;
                VRCStringDownloader.LoadUrl(urls[s], (IUdonEventReceiver)this);
            }
            resend = false;
            sent = best;
            PlayerData.SetInt("worldSentBest", sent);
            nextSend = Time.time + 15f;
            return;
        }
        if (!busy && Time.time >= nextRead && readUrl != null)
        {
            busy = true; busyUntil = Time.time + 15f;
            nextRead = Time.time + readInterval;
            VRCStringDownloader.LoadUrl(readUrl, (IUdonEventReceiver)this);
            return;
        }
        if (Time.time >= nextDraw) { nextDraw = Time.time + 10f; Draw(); }
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        busy = false;
        string s = result.Result;
        if (s == null || !s.Contains("\"top\"")) return;   // 送った時の返事（{"ok":true}）は読まない
        DataToken tok;
        if (!VRCJson.TryDeserializeFromJson(s, out tok) || tok.TokenType != TokenType.DataDictionary) return;
        DataToken arr;
        if (!tok.DataDictionary.TryGetValue("top", out arr) || arr.TokenType != TokenType.DataList) return;
        DataList list = arr.DataList;
        topN = 0;
        for (int i = 0; i < list.Count && topN < 5; i++)
        {
            DataToken e = list[i];
            if (e.TokenType != TokenType.DataDictionary) continue;
            DataToken sv, dv;
            if (!e.DataDictionary.TryGetValue("s", out sv)) continue;
            topS[topN] = (int)sv.Number;
            topD[topN] = e.DataDictionary.TryGetValue("d", out dv) && dv.TokenType == TokenType.String ? dv.String : "";
            DataToken cv;
            topC[topN] = e.DataDictionary.TryGetValue("c", out cv) && cv.TokenType == TokenType.String ? cv.String : "";
            topN++;
        }
        readFailed = false;
        Draw();
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        busy = false;
        if (result != null && result.Url != null && readUrl != null && result.Url.Get() == readUrl.Get()) { readFailed = true; Draw(); }
    }

    string Clean(string n)
    {
        if (n == null) return "?";
        return n.Replace("<", "＜").Replace(">", "＞");
    }

    // 今ワールドにいる人で、最高記録がこの点数の人の名前（いなければ空）
    string NameFor(int score, VRCPlayerApi[] ps, int count, bool[] used)
    {
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = ps[i];
            if (used[i] || p == null || !Utilities.IsValid(p)) continue;
            if (!PlayerData.HasKey(p, "soloBestScore")) continue;
            if (PlayerData.GetInt(p, "soloBestScore") == score) { used[i] = true; return Clean(p.displayName); }
        }
        return "";
    }

    // 国コード → 国名（よく来る国だけ。ほかはコードのまま。無い時は Unknown）
    string Country(string c)
    {
        if (c == null || c == "" || c == "XX" || c == "T1") return "Unknown";   // 国名なし（赤いスイッチ）・国がわからない
        if (c == "JP") return "Japan"; if (c == "US") return "USA"; if (c == "KR") return "Korea"; if (c == "TW") return "Taiwan";
        if (c == "CN") return "China"; if (c == "HK") return "Hong Kong"; if (c == "GB") return "UK"; if (c == "DE") return "Germany";
        if (c == "FR") return "France"; if (c == "CA") return "Canada"; if (c == "AU") return "Australia"; if (c == "BR") return "Brazil";
        if (c == "RU") return "Russia"; if (c == "TH") return "Thailand"; if (c == "PH") return "Philippines"; if (c == "ID") return "Indonesia";
        if (c == "SG") return "Singapore"; if (c == "NL") return "Netherlands"; if (c == "ES") return "Spain"; if (c == "IT") return "Italy";
        if (c == "MX") return "Mexico"; if (c == "PL") return "Poland"; if (c == "SE") return "Sweden"; if (c == "NZ") return "New Zealand";
        return c;
    }

    void Draw()
    {
        if (text == null) return;
        int l = Lang();
        string[] allInst = { "ALL INSTANCES", "すべてのインスタンス", "所有房间" };
        string[] soon = { "Coming soon", "準備中", "敬请期待" };
        string[] fail = { "Could not load", "読み込めませんでした", "加载失败" };
        string[] load = { "Loading...", "読み込み中...", "加载中..." };
        string[] pts = { " pts", " 点", " 分" };
        string[] none = { "No records yet", "まだ記録がありません", "暂无记录" };
        string t = "<size=110%><b>" + Title() + "</b></size>\n<size=60%><color=#9fb4c8>" + allInst[l] + "</color></size>\n";
        if (submitUrls == null || submitUrls.Length == 0 || readUrl == null) { text.text = t + "\n<color=#9fb4c8>" + soon[l] + "</color>"; return; }
        if (topN < 0) { text.text = t + "\n<color=#9fb4c8>" + (readFailed ? fail[l] : load[l]) + "</color>"; return; }
        VRCPlayerApi[] ps = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
        VRCPlayerApi.GetPlayers(ps);
        bool[] used = new bool[ps.Length];
        for (int i = 0; i < topN; i++)
        {
            string nm = NameFor(topS[i], ps, ps.Length, used);
            string col = i == 0 ? "#ffd84a" : (i == 1 ? "#d8e2ee" : (i == 2 ? "#e0a070" : "#ffffff"));
            string cn = Country(topC[i]);
            t += "\n<color=" + col + ">" + (i + 1) + ".</color>  <b>" + topS[i] + "</b>" + pts[l] + "   " +(nm != "" ? "<color=#7fe0ff>" + nm + "</color>" : "<color=#6f8090>—</color>") + (cn != "" ? "  <color=#c8d4e0>" + cn + "</color>" : "") + "  <size=60%><color=#6f8090>" + topD[i] + "</color></size>";
        }
        if (topN == 0) t += "\n<color=#9fb4c8>" + none[l] + "</color>";
        text.text = t;
    }
}
