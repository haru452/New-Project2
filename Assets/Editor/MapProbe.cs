using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: メインルームの地図はどれか（地図らしい物と、その場所・画像）
[InitializeOnLoad]
public static class MapProbe
{
    const string Flag = "Backup_flight/map_probe.flag";
    static MapProbe() { EditorApplication.delayCall += Run; }
    static string P(Transform t) { string s = t.name; t = t.parent; while (t != null) { s = t.name + "/" + s; t = t.parent; } return s; }
    static void Run()
    {
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Flag);
        var sb = new StringBuilder();
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>(true);
        if (desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null) sb.AppendLine("出現地点 " + desc.spawns[0].position.ToString("F1"));
        foreach (var r in Object.FindObjectsOfType<Renderer>(true))
        {
            string n = P(r.transform);
            string tex = "";
            foreach (var m in r.sharedMaterials) if (m != null && m.HasProperty("_MainTex") && m.mainTexture != null) tex += m.mainTexture.name + "(" + AssetDatabase.GetAssetPath(m.mainTexture) + ") ";
            string low = (n + " " + tex).ToLower();
            if (low.Contains("map") || low.Contains("chart") || low.Contains("地図") || low.Contains("island") || low.Contains("world"))
            {
                if (low.Contains("lightmap") || low.Contains("normalmap") || low.Contains("radar")) continue;
                sb.AppendLine(n + "  pos " + r.transform.position.ToString("F1") + "  size " + r.bounds.size.ToString("F1") + "  tex " + tex);
            }
        }
        File.WriteAllText("Backup_flight/map_probe.txt", sb.ToString());
        Debug.Log("[MapProbe] 書きました");
    }
}
