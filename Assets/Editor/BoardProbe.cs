using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: 掲示板（今回・歴代）に点数が出ない理由（JoinLog と掲示板のつながり）
[InitializeOnLoad]
public static class BoardProbe
{
    const string Flag = "Backup_flight/board_probe.flag";
    static BoardProbe() { EditorApplication.delayCall += Run; }
    static string P(Component c) { if (c == null) return "null"; string s = c.name; var t = c.transform.parent; while (t != null) { s = t.name + "/" + s; t = t.parent; } return s; }
    static void Run()
    {
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Flag);
        var sb = new StringBuilder();
        var jls = Object.FindObjectsOfType<JoinLog>(true);
        sb.AppendLine("JoinLog " + jls.Length);
        foreach (var j in jls) sb.AppendLine("  " + P(j) + " active=" + j.gameObject.activeInHierarchy + " soloBoard=" + P(j.soloBoard) + " duoBoard=" + P(j.duoBoard) + " worldRanking=" + P(j.worldRanking));
        foreach (var lb in Object.FindObjectsOfType<Leaderboard>(true)) sb.AppendLine("Leaderboard " + P(lb) + " active=" + lb.gameObject.activeInHierarchy + " text=" + P(lb.text) + " title=" + lb.title);
        foreach (var ab in Object.FindObjectsOfType<AllTimeBoard>(true)) sb.AppendLine("AllTimeBoard " + P(ab) + " active=" + ab.gameObject.activeInHierarchy + " text=" + P(ab.text) + " duo=" + ab.duo);
        foreach (var wr in Object.FindObjectsOfType<WorldRanking>(true)) sb.AppendLine("WorldRanking " + P(wr) + " text=" + P(wr.text));
        foreach (var c in Object.FindObjectsOfType<controller>(true)) sb.AppendLine("controller " + P(c) + " joinLog=" + P(c.joinLog));
        foreach (var t in Object.FindObjectsOfType<TMPro.TextMeshPro>(true))
            if (t.name.Contains("Board") || (t.transform.parent != null && t.transform.parent.name.Contains("Board")) || (t.transform.parent != null && t.transform.parent.name.Contains("AllTime")))
                sb.AppendLine("TMP " + P(t) + " : " + (t.text.Length > 60 ? t.text.Substring(0, 60) : t.text).Replace("\n", " | "));
        File.WriteAllText("Backup_flight/board_probe.txt", sb.ToString());
        Debug.Log("[BoardProbe] 書きました");
    }
}
