using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

// 調べるだけ: 再生中に EnemyFighter_13 が何度も「落ちて元の場所へ戻される」理由を調べる（10〜20 秒の間、0.25 秒ごとに書く）
[InitializeOnLoad]
public static class FighterProbe
{
    const string Flag = "Backup_flight/fighter_probe.flag";
    static double t0 = -1, next;
    static StringBuilder sb;
    static FighterProbe()
    {
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredPlayMode && File.Exists(Flag)) { t0 = EditorApplication.timeSinceStartup + 10.0; next = 0; sb = new StringBuilder(); } };
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (t0 < 0 || !EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        if (now < t0 || now < next) return;
        next = now + 0.25;
        var go = GameObject.Find("EnemyFighter_13");
        if (go == null) { sb.AppendLine("EnemyFighter_13 が無い"); }
        else
        {
            var rb = go.GetComponent<Rigidbody>();
            sb.Append((now - t0).ToString("F2") + "s pos " + go.transform.position.ToString("F1"));
            if (rb != null) sb.Append(" vel " + rb.velocity.ToString("F1") + " kin " + rb.isKinematic + " grav " + rb.useGravity + " sleep " + rb.IsSleeping());
            foreach (var u in go.GetComponentsInChildren<UdonBehaviour>(true))
            {
                if (u.programSource == null || u.programSource.name != "EnemyDrone") continue;
                sb.Append(" brain " + (u.enabled && u.gameObject.activeInHierarchy));
                foreach (var v in new[] { "cState", "wasDestroyed", "formationLeader", "carrierBased", "speed" })
                { object o; if (u.TryGetProgramVariable(v, out o)) sb.Append(" " + v + "=" + (o is Object ? ((Object)o != null ? ((Object)o).name : "null") : o)); }
            }
            var cols = go.GetComponentsInChildren<Collider>();
            sb.Append(" cols " + cols.Length);
            sb.AppendLine();
        }
        if (now > t0 + 10.0)
        {
            File.WriteAllText("Backup_flight/fighter_probe.txt", sb.ToString());
            if (File.Exists(Flag)) File.Delete(Flag);
            t0 = -1;
        }
    }
}
