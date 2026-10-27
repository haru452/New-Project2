using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Persistence;
using TMPro;

// =============================================================
//  歴代ランキングの掲示板（自分の画面で作る・通信なし）
//  ・各プレイヤーの「歴代の自己ベスト」は VRChat の PlayerData に保存されていて、
//    ワールドを出ても次に来た時に残っている
//  ・VRChat の仕組み上、他の人のデータはその人がワールドにいる間しか読めないので、
//    今このワールドにいる人の歴代ベストを並べる
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AllTimeBoard : UdonSharpBehaviour
{
    public TextMeshPro text;
    public string title = "ALL-TIME";
    public bool duo;                 // false = 1 人用, true = 2 人用
    public int showCount = 10;

    private VRCPlayerApi[] players = new VRCPlayerApi[90];
    private string[] names = new string[90];
    private int[] ds = new int[90];
    private int[] ss = new int[90];
    private float nextRefresh;

    void Start() { Refresh(); }

    void Update()
    {
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + 5f;
        Refresh();
    }

    public override void OnPlayerRestored(VRCPlayerApi player) { Refresh(); }
    public override void OnPlayerDataUpdated(VRCPlayerApi player, PlayerData.Info[] infos) { Refresh(); }

    string Clean(string n)
    {
        if (n == null) return "?";
        return n.Replace("<", "＜").Replace(">", "＞");
    }

    private int[] sc = new int[90];
    private int[] cs = new int[90];
    private int[] ms = new int[90];

    string ModeName(int m)
    {
        if (m == 2) return "<color=#ff4040>HARD</color>";
        if (m == 3) return "<color=#ff9040>MIX</color>";
        return "<color=#7fff9f>NORMAL</color>";
    }

    int GetI(VRCPlayerApi p, string k) { return PlayerData.HasKey(p, k) ? PlayerData.GetInt(p, k) : 0; }

    public void Refresh()
    {
        if (text == null) return;
        int pc = VRCPlayerApi.GetPlayerCount();
        if (pc > players.Length) pc = players.Length;
        VRCPlayerApi.GetPlayers(players);
        string pre = duo ? "duoBest" : "soloBest";
        int n = 0;
        for (int i = 0; i < pc; i++)
        {
            VRCPlayerApi p = players[i];
            if (p == null || !Utilities.IsValid(p)) continue;
            if (!PlayerData.HasKey(p, pre + "Score")) continue;
            int score = PlayerData.GetInt(p, pre + "Score");
            if (score <= 0) continue;
            string nm = Clean(p.displayName);
            if (duo && PlayerData.HasKey(p, "duoBestPartner")) nm += " & " + Clean(PlayerData.GetString(p, "duoBestPartner"));
            names[n] = nm; sc[n] = score; ds[n] = GetI(p, pre + "A"); ss[n] = GetI(p, pre + "S"); cs[n] = GetI(p, pre + "C"); ms[n] = GetI(p, pre + "Mode");
            int k = n;
            while (k > 0 && sc[k] > sc[k - 1])
            {
                string tn = names[k]; names[k] = names[k - 1]; names[k - 1] = tn;
                int t = sc[k]; sc[k] = sc[k - 1]; sc[k - 1] = t;
                t = ds[k]; ds[k] = ds[k - 1]; ds[k - 1] = t;
                t = ss[k]; ss[k] = ss[k - 1]; ss[k - 1] = t;
                t = cs[k]; cs[k] = cs[k - 1]; cs[k - 1] = t;
                t = ms[k]; ms[k] = ms[k - 1]; ms[k - 1] = t;
                k--;
            }
            n++;
        }
        string s2 = "<size=130%><color=#ffd040>" + title + "</color></size>\n";
        s2 += "<size=65%><color=#7fa8b0>ALL-TIME BEST OF PLAYERS IN THIS WORLD   AIR 2 / CV FTR 3 / AWACS 4 / SHIP 3 / CARGO 1 / CV 10   HARD x2</color></size>\n";
        s2 += "<size=75%><color=#7fa8b0><pos=50%>SCORE<pos=63%>AIR<pos=72%>SHIP<pos=81%>CV<pos=88%>MODE</color></size>\n";
        if (n == 0) s2 += "<color=#7fa8b0>NO RECORDS YET</color>";
        for (int i = 0; i < n && i < showCount; i++)
        {
            string col = i == 0 ? "#ffd040" : (i == 1 ? "#d8e4ea" : (i == 2 ? "#e09a5a" : "#ffffff"));
            s2 += "<color=" + col + ">" + (i + 1).ToString() + ". " + names[i] + "</color>"
               + "<pos=50%><color=#ffd040>" + sc[i].ToString() + "</color>"
               + "<pos=63%><color=#ff6a5a>" + ds[i].ToString() + "</color>"
               + "<pos=72%><color=#ffa030>" + ss[i].ToString() + "</color>"
               + "<pos=81%><color=#ffe070>" + cs[i].ToString() + "</color>"
               + "<pos=88%><size=80%>" + ModeName(ms[i]) + "</size>\n";
        }
        text.text = s2;
    }
}
