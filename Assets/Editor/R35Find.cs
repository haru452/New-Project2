using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: 名前に R35 / Dropship を含む物を書き出す（親も同じ名前なら省く）
[InitializeOnLoad]
public static class R35Find
{
    const string FlagPath = "Backup_flight/run_r35find.flag";
    static R35Find() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(FlagPath);
        var sb = new StringBuilder();
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (!Match(t.name)) continue;
            if (t.parent != null && Match(t.parent.name)) continue;
            int rc = t.GetComponentsInChildren<Renderer>(true).Length;
            int act = 0; foreach (var r in t.GetComponentsInChildren<Renderer>(false)) if (r.enabled) act++;
            string path = t.name; var p = t.parent; while (p != null) { path = p.name + "/" + path; p = p.parent; }
            sb.AppendLine(path + (t.gameObject.activeInHierarchy ? "" : " [off]") + "  renderers " + rc + "（表示中 " + act + "）  children " + t.childCount + "  pos " + t.position.ToString("F0"));
        }
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            sb.AppendLine("controller: " + c.name + " car=" + (c.car != null ? c.car.name : "null"));
        File.WriteAllText("Backup_flight/r35find.txt", sb.ToString());
    }
    static bool Match(string n) { return n.Contains("R35") || n.Contains("Dropship"); }
}
