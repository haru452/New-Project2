using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// main room の中にある物の一覧（位置・大きさ）を Backup_flight/mainroom_info.txt に書き出す（調べるだけ。何も変えない）
[InitializeOnLoad]
public static class MainRoomInfo
{
    const string FlagPath = "Backup_flight/run_roominfo.flag";
    static MainRoomInfo() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(FlagPath);
        Run();
    }
    [MenuItem("Tools/main room の中身を調べる")]
    public static void Run()
    {
        var sb = new StringBuilder();
        // 部屋の範囲: mainRoom の見た目全体
        GameObject mr = null;
        foreach (var g in SceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == "mainRoom") mr = g;
        Bounds rb = new Bounds();
        bool has = false;
        foreach (var r in mr.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.name.StartsWith("Cube")) continue;
            if (!has) { rb = r.bounds; has = true; } else rb.Encapsulate(r.bounds);
            sb.AppendLine("WALL " + r.name + " center " + r.bounds.center.ToString("F1") + " size " + r.bounds.size.ToString("F1") + " rot " + r.transform.eulerAngles.ToString("F0"));
        }
        sb.AppendLine("ROOM bounds " + rb.center.ToString("F1") + " size " + rb.size.ToString("F1"));
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>(true);
        if (desc != null)
        {
            sb.AppendLine("Descriptor " + desc.transform.position.ToString("F1") + " spawns " + (desc.spawns != null ? desc.spawns.Length : 0) + " respawnY " + desc.RespawnHeightY);
            if (desc.spawns != null) foreach (var s in desc.spawns) if (s != null) sb.AppendLine("  spawn " + s.name + " " + s.position.ToString("F1") + " rotY " + s.eulerAngles.y.ToString("F0"));
        }
        // 範囲の中にある物（親が範囲の中にあるものは省略）
        Bounds big = rb; big.Expand(4f);
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (!big.Contains(t.position)) continue;
            if (t.parent != null && big.Contains(t.parent.position) && t.parent != mr.transform) continue;
            string path = t.name; for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            var comps = t.GetComponents<Component>();
            var cn = new StringBuilder();
            foreach (var c in comps) if (c != null && !(c is Transform)) cn.Append(c.GetType().Name + ",");
            Bounds ob = new Bounds(t.position, Vector3.zero); bool oh = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) { if (!oh) { ob = r.bounds; oh = true; } else ob.Encapsulate(r.bounds); }
            sb.AppendLine((t.gameObject.activeInHierarchy ? "" : "[off] ") + path + "  pos " + t.position.ToString("F1") + " rotY " + t.eulerAngles.y.ToString("F0") + (oh ? " bsize " + ob.size.ToString("F1") : "") + "  {" + cn + "}");
        }
        File.WriteAllText("Backup_flight/mainroom_info.txt", sb.ToString());
    }
}
