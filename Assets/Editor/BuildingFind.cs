using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: 建物らしいプレハブ（元の場所ごと）と、地形（Terrain）の位置を書き出す
[InitializeOnLoad]
public static class BuildingFind
{
    const string FlagPath = "Backup_flight/run_bfind.flag";
    static BuildingFind() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(FlagPath);
        var sb = new StringBuilder();
        foreach (var t in Object.FindObjectsOfType<Terrain>(true))
        {
            var td = t.terrainData;
            sb.AppendLine("TERRAIN " + Path(t.transform) + " pos " + t.transform.position.ToString("F0") + " size " + (td != null ? td.size.ToString("F0") : "?"));
        }
        var seen = new System.Collections.Generic.HashSet<GameObject>();
        foreach (var r in Object.FindObjectsOfType<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
            if (root == null || seen.Contains(root)) continue;
            seen.Add(root);
            var src = PrefabUtility.GetCorrespondingObjectFromSource(root);
            string ap = src != null ? AssetDatabase.GetAssetPath(src) : "";
            if (ap.Contains("My script") || ap.Contains("Omega") || ap.Contains("FA_N26") || ap.Contains("VRChat") || ap.Contains("Packages/")) continue;
            Bounds b = r.bounds; bool h = false;
            foreach (var rr in root.GetComponentsInChildren<Renderer>(true)) { if (rr is ParticleSystemRenderer) continue; if (!h) { b = rr.bounds; h = true; } else b.Encapsulate(rr.bounds); }
            sb.AppendLine(Path(root.transform) + (root.activeInHierarchy ? "" : " [off]") + " | " + ap + " | pos " + root.transform.position.ToString("F0") + " size " + b.size.ToString("F0") + (root.GetComponent("FlightTarget") != null ? " [FT]" : ""));
        }
        File.WriteAllText("Backup_flight/bfind.txt", sb.ToString());
    }
    static string Path(Transform t) { string p = t.name; var q = t.parent; while (q != null) { p = q.name + "/" + p; q = q.parent; } return p; }
}
