using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: Backup_flight/run_objinfo2.flag の 1 行目の物の、子を 2 段まで全部書く
[InitializeOnLoad]
public static class ObjInfo2
{
    const string FlagPath = "Backup_flight/run_objinfo2.flag";
    static ObjInfo2() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        string[] names = File.ReadAllLines(FlagPath);
        File.Delete(FlagPath);
        var sb = new StringBuilder();
        foreach (var n0 in names)
        {
            string n = n0.Trim(); if (n == "") continue;
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name != n) continue;
                Dump(sb, t, 0);
            }
        }
        File.WriteAllText("Backup_flight/objinfo2.txt", sb.ToString());
    }
    static void Dump(StringBuilder sb, Transform t, int d)
    {
        Bounds b = new Bounds(t.position, Vector3.zero); bool h = false; int rc = 0;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true)) { if (r is ParticleSystemRenderer) continue; rc++; if (!h) { b = r.bounds; h = true; } else b.Encapsulate(r.bounds); }
        var comps = new StringBuilder();
        foreach (var c in t.GetComponents<Component>()) if (c != null && !(c is Transform)) comps.Append(c.GetType().Name + ",");
        sb.AppendLine(new string(' ', d * 2) + "- " + t.name + (t.gameObject.activeInHierarchy ? "" : " [off]") + " pos " + t.position.ToString("F0") + " rotY " + t.eulerAngles.y.ToString("F0") + " size " + b.size.ToString("F0") + " r" + rc + " {" + comps + "}");
        if (d >= 1) return;
        foreach (Transform c in t) Dump(sb, c, d + 1);
    }
}
