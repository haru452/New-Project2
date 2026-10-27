using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: 伊402 と、そのまわりに置いた物の位置
[InitializeOnLoad]
public static class SubInfo
{
    const string FlagPath = "Backup_flight/run_subinfo.flag";
    static SubInfo() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(FlagPath);
        var sb = new StringBuilder();
        var subs = new System.Collections.Generic.List<Transform>();
        foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name.Contains("402")) subs.Add(t);
        foreach (var t in subs)
        {
            Bounds b = new Bounds(t.position, Vector3.zero); bool h = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) { if (!h) { b = r.bounds; h = true; } else b.Encapsulate(r.bounds); }
            sb.AppendLine("SUB " + P(t) + (t.gameObject.activeInHierarchy ? "" : " [off]") + " pos " + V(t.position) + " rot " + V(t.eulerAngles) + " bounds " + V(b.center) + " size " + V(b.size));
        }
        foreach (var c in Object.FindObjectsOfType<Catapult>(true)) sb.AppendLine("Catapult " + P(c.transform) + " " + V(c.transform.position));
        foreach (var p in Object.FindObjectsOfType<PutOutDrone>(true))
        {
            sb.AppendLine("PutOutDrone " + P(p.transform) + " " + V(p.transform.position));
            if (p.reportPoint != null) sb.AppendLine("  reportPoint " + P(p.reportPoint.transform) + " " + V(p.reportPoint.transform.position));
            if (p.catapultPoint != null) sb.AppendLine("  catapultPoint " + P(p.catapultPoint) + " " + V(p.catapultPoint.position));
            if (p.dronePool != null) foreach (var g in p.dronePool.Pool) if (g != null) sb.AppendLine("  pool " + P(g.transform) + " " + V(g.transform.position));
        }
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.respawnPoint != null && c.car != null && c.car.name.StartsWith("Omega")) sb.AppendLine("respawn of " + c.car.name + ": " + P(c.respawnPoint) + " " + V(c.respawnPoint.position));
        // 伊402 の近く（元の場所 290,297 から 300m 以内）にある、ほかの一番上の物
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.parent != null) continue;
            Vector3 d = t.position - new Vector3(290f, 0f, 297f); d.y = 0f;
            if (d.magnitude < 300f) sb.AppendLine("NEAR " + P(t) + (t.gameObject.activeInHierarchy ? "" : " [off]") + " " + V(t.position));
        }
        File.WriteAllText("Backup_flight/subinfo.txt", sb.ToString());
    }
    static string V(Vector3 v) { return v.ToString("F1"); }
    static string P(Transform t) { string p = t.name; var q = t.parent; while (q != null) { p = q.name + "/" + p; q = q.parent; } return p; }
}
