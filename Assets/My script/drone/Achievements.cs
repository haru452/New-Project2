using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

// =============================================================
//  アチーブメント（称号）。自分の画面だけ・PlayerData に保存（ワールドを出ても残る）
//  ・条件を満たすともらえる。main room の板で、世界ランキングに出す称号を 1 つ選ぶ
//  ・選んだ称号は WorldRanking がサーバーへ送る（番号だけ）
//  番号: 0 初心者パイロット / 1 歴史家 / 2 祈る者 / 3 撃墜王 / 4 艦船キラー
//        5 ペラゴス諸島の英雄 / 6 熟練パイロット / 7 エース / 8 観光客 / 9 裏切者（隠し） / 10 生還者
//        11 負けず嫌い / 12 不死鳥（隠し） / 13 麒麟（隠し） / 14 歴史オタク / 15 鳳凰（隠し） / 16 白虎（隠し）
//        17 ユニコーン（隠し） / 18 クラーケン（隠し） / 19 パイロット / 20 開発者（開発者だけ。ほかの人の板には出さない）
//  ※ 番号はサーバーに保存されるので、あとから並びを変えない（足すときは最後に足す。32 個まで）
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Achievements : UdonSharpBehaviour
{
    public TextMeshPro header;        // 板の見出し
    public TextMeshPro text;          // 左の列（前半）
    public TextMeshPro text2;         // 右の列（後半）
    public Subtitles subtitles;       // 選んだ言語（0 = English, 1 = 日本語, 2 = 中文）
    public WorldRanking ranking;
    public MuseumAudio museum;        // 博物館の中にいるか（歴史家・歴史オタク）
    public GameMode gameMode;         // 遊覧モードか（鳳凰）
    [Tooltip("称号「開発者」をもらえる人の VRChat の表示名（この人だけ）")]
    public string developerName = "hichi2025";
    private const int DEV = 20;       // 開発者: 持っていない人の板には、行そのものを出さない

    private string[] nEn = { "Rookie Pilot", "Historian", "Mourner", "Sky Hunter", "Ship Killer", "Hero of the Pelagos Islands", "Veteran Pilot", "Ace", "Tourist", "Traitor", "Survivor",
                             "Sore Loser", "Phoenix", "Qilin", "History Buff", "Fenghuang", "White Tiger", "Unicorn", "Kraken", "Pilot", "Developer" };
    private string[] nJa = { "初心者パイロット", "歴史家", "祈る者", "撃墜王", "艦船キラー", "ペラゴス諸島の英雄", "熟練パイロット", "エース", "観光客", "裏切者", "生還者",
                             "負けず嫌い", "不死鳥", "麒麟", "歴史オタク", "鳳凰", "白虎", "ユニコーン", "クラーケン", "パイロット", "開発者" };
    private string[] nZh = { "新手飞行员", "历史学家", "祈祷者", "击坠王", "舰船杀手", "佩拉戈斯群岛的英雄", "老练飞行员", "王牌", "观光客", "叛徒", "幸存者",
                             "不服输", "不死鸟", "麒麟", "历史迷", "凤凰", "白虎", "独角兽", "克拉肯", "飞行员", "开发者" };
    private string[] hEn = { "Given to everyone", "Visit the museum", "Offer a paper crane", "Shoot down 5 aircraft in one sortie", "Sink 3 ships in one sortie", "Sink the carrier", "Score 1000 in one sortie", "Score 2000 in one sortie", "Land at Astral Airport in Sightseeing mode", "Land at Pelagos Base in Normal or Hard mode", "Be flying when the carrier is sunk",
                             "Get shot down 30 times in total", "Reach a repair area with 10 HP or less", "Fly 15 minutes without using any weapon (Normal / Hard)", "Visit the museum 30 times", "Play 30 minutes in total in Sightseeing mode", "Sink the carrier in Hard mode without being shot down since joining", "Offer paper cranes 30 times in total", "Sink 10 ships in one sortie without shooting down any aircraft", "Board an aircraft 10 times in total", "The creator of this world" };
    private string[] hJa = { "最初から持っている", "博物館に入る", "折り鶴を箱に納める", "1 回の出撃で敵機を 5 機落とす", "1 回の出撃で船を 3 隻沈める", "空母を沈める", "1 回の出撃で 1000 点", "1 回の出撃で 2000 点", "遊覧モードでアストラル空港に着陸する", "普通・ハードモードでペラゴス基地に着陸する", "空母が沈んだ時に飛んでいる",
                             "合計 30 回撃墜される", "HP 10 以下で修理できる所にたどり着く", "普通・ハードで武器を使わずに 15 分飛ぶ", "博物館に合計 30 回行く", "遊覧モードで合計 30 分遊ぶ", "ワールドに入ってから一度も撃墜されずに、ハードで空母を沈める", "折り鶴を合計 30 回納める", "1 回の出撃で、敵機を 1 機も落とさずに船を 10 隻沈める", "飛行機に合計 10 回乗る", "このワールドを作った人" };
    private string[] hZh = { "所有人都有", "参观博物馆", "供奉纸鹤", "一次出击击落 5 架飞机", "一次出击击沉 3 艘船", "击沉航母", "一次出击得 1000 分", "一次出击得 2000 分", "在观光模式下降落在阿斯特拉尔机场", "在普通或困难模式下降落在佩拉戈斯基地", "航母被击沉时正在飞行",
                             "累计被击落 30 次", "HP 10 以下到达可修理的地方", "普通或困难模式下不使用武器飞行 15 分钟", "累计参观博物馆 30 次", "在观光模式下累计游玩 30 分钟", "进入世界后一次也未被击落，在困难模式下击沉航母", "累计供奉纸鹤 30 次", "一次出击中不击落任何飞机，击沉 10 艘船", "累计登上飞机 10 次", "这个世界的制作者" };
    private bool[] hidden = { false, false, false, false, false, false, false, false, false, true, false,   // 9 裏切者は隠し
                              false, true, true, false, true, true, true, true, false, true };   // 隠し（12 不死鳥・13 麒麟・15 鳳凰・16 白虎・17 ユニコーン・18 クラーケン）: 持つまで条件を見せない

    private int bits = 1;             // 持っている称号（ビット）。0 番は最初から
    private int sel;                  // 選んでいる称号
    private bool loaded;
    private int shownLang = -1;
    private float nextCheck;
    private int flash = -1;           // 今もらった称号（板で光らせる）
    private float flashUntil;
    private int losses;               // 撃墜された回数（合計）
    private int visits;               // 博物館に行った回数（合計）
    private bool wasInMuseum;
    private float outSince;           // 博物館の外に出た時刻（出入りの境目でちらつかないように 10 秒あける）
    private float sightSec;           // 遊覧モードで遊んだ秒数（合計）
    private float sightSaved;

    public int _Count() { return nEn.Length; }

    public string _Name(int i, int l)
    {
        if (i < 0 || i >= nEn.Length) return "";
        if (l == 1) return nJa[i];
        if (l == 2) return nZh[i];
        return nEn[i];
    }

    public int _Selected() { return sel; }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;
        if (PlayerData.HasKey(player, "achvBits")) bits = PlayerData.GetInt(player, "achvBits") | 1;
        if (PlayerData.HasKey(player, "achvSel")) sel = PlayerData.GetInt(player, "achvSel");
        if (PlayerData.HasKey(player, "achvLoss")) losses = PlayerData.GetInt(player, "achvLoss");
        if (PlayerData.HasKey(player, "achvMuseum")) visits = PlayerData.GetInt(player, "achvMuseum");
        if (PlayerData.HasKey(player, "achvBoard")) boards = PlayerData.GetInt(player, "achvBoard");
        if (PlayerData.HasKey(player, "achvCrane")) cranes = PlayerData.GetInt(player, "achvCrane");
        if (PlayerData.HasKey(player, "achvSight")) sightSec = PlayerData.GetInt(player, "achvSight");
        sightSaved = sightSec;
        if (!string.IsNullOrEmpty(developerName) && player.displayName == developerName) bits |= 1 << DEV;   // 開発者
        else bits &= ~(1 << DEV);   // ほかの人は持てない
        if (sel < 0 || sel >= nEn.Length || (bits & (1 << sel)) == 0) sel = 0;
        loaded = true;
        if (ranking != null) ranking._SetBadge(sel);
        Draw();
    }

    // 条件を満たした（JoinLog・controller から）
    public void _Unlock(int i)
    {
        if (!loaded || i < 0 || i >= nEn.Length || i == DEV) return;
        int b = 1 << i;
        if ((bits & b) != 0) return;
        bits |= b;
        PlayerData.SetInt("achvBits", bits);
        flash = i; flashUntil = Time.time + 8f;
        Draw();
    }

    // 1 回の出撃の記録から（JoinLog が撃墜のたびに呼ぶ）
    public void _CheckRun(int score, int a, int s, int c)
    {
        if (a >= 5) _Unlock(3);
        if (s >= 3) _Unlock(4);
        if (c >= 1) _Unlock(5);
        if (score >= 1000) _Unlock(6);
        if (score >= 2000) _Unlock(7);
        if (a == 0 && s >= 10) _Unlock(18);   // クラーケン
    }

    // 折り鶴を納めた（JoinLog._Bless から）: 合計 30 回でユニコーン
    private int cranes;
    public void _AddCrane()
    {
        if (!loaded) return;
        cranes++;
        PlayerData.SetInt("achvCrane", cranes);
        _Unlock(2);
        if (cranes >= 30) _Unlock(17);
    }

    // 飛行機に乗った（操縦席・ガンナー席）: 合計 10 回でパイロット
    private int boards;
    public void _AddBoard()
    {
        if (!loaded) return;
        boards++;
        PlayerData.SetInt("achvBoard", boards);
        if (boards >= 10) _Unlock(19);
    }

    // 撃墜された（JoinLog._ResetRun から）
    public void _AddLoss()
    {
        if (!loaded) return;
        losses++;
        PlayerData.SetInt("achvLoss", losses);
        if (losses >= 30) _Unlock(11);
    }

    // 板の ◀ ▶ : 持っている称号の中で選び直す
    public void _Step(int dir)
    {
        if (!loaded) return;
        int n = nEn.Length;
        for (int k = 1; k <= n; k++)
        {
            int j = ((sel + dir * k) % n + n) % n;
            if ((bits & (1 << j)) == 0) continue;
            sel = j;
            break;
        }
        PlayerData.SetInt("achvSel", sel);
        if (ranking != null) ranking._SetBadge(sel);
        Draw();
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        float step = 1f;
        nextCheck = Time.time + step;
        if (!loaded) return;
        // 博物館: 入った回数を数える
        bool inMuseum = museum != null && museum._IsInside();
        if (inMuseum && !wasInMuseum && Time.time - outSince > 10f)
        {
            visits++;
            PlayerData.SetInt("achvMuseum", visits);
            _Unlock(1);
            if (visits >= 30) _Unlock(14);
        }
        if (!inMuseum && wasInMuseum) outSince = Time.time;
        wasInMuseum = inMuseum;
        // 遊覧モードで遊んだ時間（30 秒ごとに保存）
        if (gameMode != null && gameMode._SightMode())
        {
            sightSec += step;
            if (sightSec - sightSaved >= 30f) { sightSaved = sightSec; PlayerData.SetInt("achvSight", (int)sightSec); }
            if (sightSec >= 1800f) _Unlock(15);   // 30 分
        }
        if (Lang() != shownLang) Draw();
        if (flash >= 0 && Time.time > flashUntil) { flash = -1; Draw(); }
    }

    int Lang()
    {
        int l = subtitles != null ? subtitles._Lang() : 0;
        if (l < 0 || l > 2) l = 0;
        return l;
    }

    string Hint(int i, int l)
    {
        if (l == 1) return hJa[i];
        if (l == 2) return hZh[i];
        return hEn[i];
    }

    string Line(int i, int l)
    {
        string[] secret = { "Hidden achievement", "隠しアチーブメント", "隐藏成就" };
        string[] got = { "NEW!", "獲得!", "获得!" };
        bool have = (bits & (1 << i)) != 0;
        string line;
        if (i == sel) line = "<color=#ffd84a>▶ <b>" + _Name(i, l) + "</b></color>";
        else if (have) line = "<color=#ffffff>　 " + _Name(i, l) + "</color>";
        else line = "<color=#55606c>　 ？？？</color>";
        string h = (!have && hidden[i]) ? secret[l] : Hint(i, l);
        line += "\n<size=55%><color=" + (have ? "#8fa0b0" : "#55606c") + ">　　 " + h + "</color></size>";
        if (i == flash) line += "  <color=#7fff9a>" + got[l] + "</color>";
        return line;
    }

    void Draw()
    {
        int l = Lang();
        shownLang = l;
        if (header != null)
        {
            string[] title = { "ACHIEVEMENTS", "アチーブメント", "成就" };
            string[] sub = { "Press ◀ ▶ to choose the one shown on the World Ranking", "◀ ▶ で世界ランキングに出す称号を選べます", "按 ◀ ▶ 选择在世界排行榜上显示的称号" };
            header.text = "<size=120%><b>" + title[l] + "</b></size>\n<size=55%><color=#9fb4c8>" + sub[l] + "</color></size>";
        }
        int half = (nEn.Length + 1) / 2;   // 左 11 個・右 10 個（開発者を持っていない人は右 9 個）
        bool dev = (bits & (1 << DEV)) != 0;
        if (text != null)
        {
            string t = "";
            for (int i = 0; i < half; i++)
            {
                if (i == DEV && !dev) continue;
                t += (t != "" ? "\n" : "") + Line(i, l);
            }
            text.text = t;
        }
        if (text2 != null)
        {
            string t = "";
            for (int i = half; i < nEn.Length; i++)
            {
                if (i == DEV && !dev) continue;   // 開発者は、持っていない人には見せない
                t += (t != "" ? "\n" : "") + Line(i, l);
            }
            text2.text = t;
        }
    }
}
