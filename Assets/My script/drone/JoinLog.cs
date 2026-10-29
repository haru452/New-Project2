using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Persistence;

// =============================================================
//  入室・退室の記録（自分がワールドに入ってから）
//  ・自分の画面だけで記録する（通信なし）
//  ・自分が入った時にもういた人は「HERE」、後から来た人は「JOIN」、出た人は「LEFT」
//  ・コックピットの右のパネルが _GetText() で読んで表示する
//  ※ 機体はプールで非表示になることがあり、非表示の間はイベントを受け取れないので、
//    いつも表示されている専用のオブジェクトに付ける
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class JoinLog : UdonSharpBehaviour
{
    public int maxLines = 9;

    private string[] lines = new string[40];
    private int count;
    private float startTime;
    private bool selfJoined;

    [Tooltip("全員の身長をこの高さにそろえる（目の高さ m。身長 1.6m ≒ 目の高さ 1.5m）。0 ならそろえない")]
    public float eyeHeight = 1.5f;

    void Start()
    {
        startTime = Time.realtimeSinceStartup;
        FixHeight();
    }

    // 小さいアバターだとコックピットや計器が見えないので、身長を固定する
    void FixHeight()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me == null || eyeHeight <= 0f) return;
        me.SetManualAvatarScalingAllowed(false);
        me.SetAvatarEyeHeightByMeters(eyeHeight);
    }

    public override void OnAvatarChanged(VRCPlayerApi player)
    {
        if (player != null && player.isLocal) SendCustomEventDelayedSeconds(nameof(_FixHeightLater), 0.5f);
    }

    public void _FixHeightLater() { FixHeight(); }

    string Now()
    {
        return System.DateTime.Now.ToString("HH:mm");
    }

    string NameOf(VRCPlayerApi p)
    {
        if (p == null || !Utilities.IsValid(p)) return "?";
        string n = p.displayName;
        if (n == null) n = "?";
        if (n.Length > 14) n = n.Substring(0, 13) + "..";
        return n;
    }

    void Add(string s)
    {
        if (count >= lines.Length)
        {
            for (int i = 1; i < lines.Length; i++) lines[i - 1] = lines[i];
            count = lines.Length - 1;
        }
        lines[count] = s;
        count++;
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (player == null) return;
        if (player.isLocal)
        {
            selfJoined = true;
            Add("<color=#ffc040>" + Now() + "  JOIN  " + NameOf(player) + " (YOU)</color>");
            return;
        }
        // 自分が入った直後に届く分は、もともといた人
        bool already = !selfJoined || Time.realtimeSinceStartup - startTime < 3f;
        if (already) Add("<color=#5aaab8>" + Now() + "  HERE  " + NameOf(player) + "</color>");
        else Add("<color=#7fff9f>" + Now() + "  JOIN  " + NameOf(player) + "</color>");
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null) return;
        Add("<color=#ff6050>" + Now() + "  LEFT  " + NameOf(player) + "</color>");
    }

    public string _GetText()
    {
        string s = "";
        int from = Mathf.Max(0, count - maxLines);
        for (int i = from; i < count; i++)
        {
            if (s.Length > 0) s += "\n";
            s += lines[i];
        }
        return s;
    }

    // ---- 今回の出撃の記録（撃墜されたら 0 から）→ 掲示板へ ----
    // 得点: 飛行機 2 点・船 3 点・空母 10 点。ハードモード中に倒すと 2 倍
    public Leaderboard soloBoard;   // 1 人で乗った時
    public Leaderboard duoBoard;    // 2 人（操縦士＋ガンナー）で乗った時
    private int soloScore, soloA, soloS, soloC, soloMode;   // mode: 1 = 普通だけ, 2 = ハードだけ, 3 = 両方
    private int duoScore, duoA, duoS, duoC, duoMode;
    private string duoPartner = "";

    string Clean(string n)
    {
        if (n == null) return "?";
        return n.Replace("<", "＜").Replace(">", "＞");
    }

    int Points(int kind) { return kind == 0 ? 2 : (kind == 1 ? 3 : (kind == 3 ? 1 : (kind == 4 ? 4 : (kind == 5 ? 3 : (kind == 7 ? 100 : 10))))); }   // 7 = 基地の建物を全部壊した（100 点）   // 5 = 空母の戦闘機（3 点・飛行機）   // 3 = 貨物船（1 点・船）、4 = 飛ぶ警戒機（4 点・飛行機）

    // 撃墜した（kind: 0 = 飛行機, 1 = 船, 2 = 空母）。partner が空なら 1 人、あれば 2 人の記録
    public void _RunKill(int kind, string partner, bool hard) { _RunKillMul(kind, partner, hard, 1); }
    // mul = 得点の倍率（遊覧モードの裏切りの後は 3 倍）
    public void _RunKillMul(int kind, string partner, bool hard, int mul)
    {
        string me = Clean(Networking.LocalPlayer.displayName);
        int pts = Points(kind) * (hard ? 2 : 1) * Mathf.Max(mul, 1);
        int m = hard ? 2 : 1;
        if (string.IsNullOrEmpty(partner))
        {
            Debug.Log("[Score] 撃破 kind=" + kind + " +" + pts + " → 今回 " + (soloScore + pts) + " 点（掲示板 " + (soloBoard != null ? "あり" : "なし") + "）");
            soloScore += pts; soloMode |= m;
            if (kind == 0 || kind == 4 || kind == 5) soloA++; else if (kind == 2) soloC++; else if (kind != 7) soloS++;
            if (soloBoard != null) soloBoard._Submit(me, soloScore, soloA, soloS, soloC, soloMode);
            SaveBest("solo", soloScore, soloA, soloS, soloC, soloMode, "");
        }
        else
        {
            if (partner != duoPartner) { duoScore = 0; duoA = 0; duoS = 0; duoC = 0; duoMode = 0; duoPartner = partner; }   // 相方が変わったら新しい記録
            duoScore += pts; duoMode |= m;
            if (kind == 0 || kind == 4 || kind == 5) duoA++; else if (kind == 2) duoC++; else if (kind != 7) duoS++;
            string crew = me + " & " + Clean(partner);
            if (duoBoard != null) duoBoard._Submit(crew, duoScore, duoA, duoS, duoC, duoMode);
            SaveBest("duo", duoScore, duoA, duoS, duoC, duoMode, Clean(partner));
        }
    }

    // 歴代の自己ベストを PlayerData に保存（ワールドを出ても残る）
    public WorldRanking worldRanking;   // 世界ランキング（全インスタンス共通・名前なし）
    void SaveBest(string kind, int score, int a, int s, int c, int mode, string partner)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        int best = PlayerData.HasKey(me, kind + "BestScore") ? PlayerData.GetInt(me, kind + "BestScore") : 0;
        if (score <= best) return;
        PlayerData.SetInt(kind + "BestScore", score);
        if (kind == "solo" && worldRanking != null) worldRanking._Offer(score);   // 世界ランキングへ（点数だけ）
        PlayerData.SetInt(kind + "BestA", a);
        PlayerData.SetInt(kind + "BestS", s);
        PlayerData.SetInt(kind + "BestC", c);
        PlayerData.SetInt(kind + "BestMode", mode);
        if (kind == "duo") PlayerData.SetString("duoBestPartner", partner);
    }

    // ---- 折り鶴の加護（その場だけ）: 折り鶴を納めると、次に飛行機に乗った時に 1 回だけ +10 点 ----
    private bool blessed;
    public bool _IsBlessed() { return blessed; }
    public void _Bless() { blessed = true; }
    public void _UseBlessing()
    {
        if (!blessed) return;
        blessed = false;   // 1 回だけ。撃墜されて記録が 0 になったら、もう一度納めないともらえない
        string me = Clean(Networking.LocalPlayer.displayName);
        soloScore += 10;
        if (soloBoard != null) soloBoard._Submit(me, soloScore, soloA, soloS, soloC, soloMode);
        SaveBest("solo", soloScore, soloA, soloS, soloC, soloMode, "");
    }

    // 撃墜された → 今回の出撃の記録は 0 から
    public void _ResetRun()
    {
        soloScore = 0; soloA = 0; soloS = 0; soloC = 0; soloMode = 0;
        duoScore = 0; duoA = 0; duoS = 0; duoC = 0; duoMode = 0;
    }

    public int _RunScore(bool duo) { return duo ? duoScore : soloScore; }
    public int _RunAir(bool duo) { return duo ? duoA : soloA; }
    public int _RunShips(bool duo) { return duo ? duoS : soloS; }
    public int _RunCarriers(bool duo) { return duo ? duoC : soloC; }

    // ---- 自分の撃墜数（このワールドに入ってからの合計。どの機体に乗っても続く）----
    private int droneKills, shipKills;
    public void _AddDroneKill() { droneKills++; }
    // 戦闘が始まったことがあるか（ワールドに入ってから。自分の画面だけ）
    // 2 回目からは警告なしで「重火器の使用を許可する」から始まる
    private bool warStarted;
    public void _MarkWar() { warStarted = true; }
    public bool _WarStarted() { return warStarted; }
    public void _AddShipKill() { shipKills++; }
    public int _GetDroneKills() { return droneKills; }
    public int _GetShipKills() { return shipKills; }

    public int _GetPlayerCount()
    {
        return VRCPlayerApi.GetPlayerCount();
    }
}
