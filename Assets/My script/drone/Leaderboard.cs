using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

// =============================================================
//  撃墜ランキングの掲示板（全員で共有）
//  ・1 回の出撃（撃墜されるまで）の得点の、ワールドにいる間の最高記録
//  ・得点: 飛行機 2 点・船 3 点・空母 10 点（ハードモード中は 2 倍）
//  ・横に倒した数と、どのモードだったか（NORMAL / HARD / MIX）も出す
//  ・記録を更新した人がこの掲示板の持ち主になって書き換え、全員に同期する
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class Leaderboard : UdonSharpBehaviour
{
    public TextMeshPro text;
    public string title = "RANKING";
    public int showCount = 10;

    [UdonSynced] private string[] names = new string[20];
    [UdonSynced] private int[] scores = new int[20];
    [UdonSynced] private int[] air = new int[20];
    [UdonSynced] private int[] ships = new int[20];
    [UdonSynced] private int[] carriers = new int[20];
    [UdonSynced] private int[] modes = new int[20];
    [UdonSynced] private int count;

    void Start() { Redraw(); }

    public override void OnDeserialization() { Redraw(); }

    public void _Submit(string who, int score, int a, int s, int c, int mode)
    {
        if (string.IsNullOrEmpty(who) || score <= 0) return;
        if (names == null || names.Length < 20 || scores == null || scores.Length < 20) return;
        int idx = -1;
        for (int i = 0; i < count; i++) if (names[i] == who) { idx = i; break; }
        if (idx >= 0 && score <= scores[idx]) return;   // 最高記録ではない

        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        if (idx < 0)
        {
            if (count < names.Length) { idx = count; count++; }
            else
            {
                idx = count - 1;
                if (score <= scores[idx]) return;
            }
        }
        names[idx] = who; scores[idx] = score; air[idx] = a; ships[idx] = s; carriers[idx] = c; modes[idx] = mode;
        while (idx > 0 && scores[idx] > scores[idx - 1])
        {
            Swap(idx, idx - 1);
            idx--;
        }
        RequestSerialization();
        Redraw();
    }

    void Swap(int i, int j)
    {
        string tn = names[i]; names[i] = names[j]; names[j] = tn;
        int t = scores[i]; scores[i] = scores[j]; scores[j] = t;
        t = air[i]; air[i] = air[j]; air[j] = t;
        t = ships[i]; ships[i] = ships[j]; ships[j] = t;
        t = carriers[i]; carriers[i] = carriers[j]; carriers[j] = t;
        t = modes[i]; modes[i] = modes[j]; modes[j] = t;
    }

    string ModeName(int m)
    {
        if (m == 2) return "<color=#ff4040>HARD</color>";
        if (m == 3) return "<color=#ff9040>MIX</color>";
        return "<color=#7fff9f>NORMAL</color>";
    }

    void Redraw()
    {
        if (text == null) return;
        string s = "<size=130%><color=#5ff2ff>" + title + "</color></size>\n";
        s += "<size=65%><color=#7fa8b0>ONE SORTIE BEST   AIR 2pt / CV FIGHTER 3pt / AWACS 4pt / SHIP 3pt / CARGO 1pt / CARRIER 10pt   HARD x2</color></size>\n";
        s += "<size=75%><color=#7fa8b0><pos=50%>SCORE<pos=63%>AIR<pos=72%>SHIP<pos=81%>CV<pos=88%>MODE</color></size>\n";
        if (count == 0) s += "<color=#7fa8b0>NO RECORDS YET</color>";
        for (int i = 0; i < count && i < showCount; i++)
        {
            string col = i == 0 ? "#ffd040" : (i == 1 ? "#d8e4ea" : (i == 2 ? "#e09a5a" : "#ffffff"));
            s += "<color=" + col + ">" + (i + 1).ToString() + ". " + names[i] + "</color>"
               + "<pos=50%><color=#ffd040>" + scores[i].ToString() + "</color>"
               + "<pos=63%><color=#ff6a5a>" + air[i].ToString() + "</color>"
               + "<pos=72%><color=#ffa030>" + ships[i].ToString() + "</color>"
               + "<pos=81%><color=#ffe070>" + carriers[i].ToString() + "</color>"
               + "<pos=88%><size=80%>" + ModeName(modes[i]) + "</size>\n";
        }
        text.text = s;
    }
}
