using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: 海（水面）の物と、その材質・シェーダーの設定
[InitializeOnLoad]
public static class WaterProbe
{
    const string Flag = "Backup_flight/water_probe.flag";
    static WaterProbe() { EditorApplication.delayCall += Run; }
    static string P(Transform t) { string s = t.name; t = t.parent; while (t != null) { s = t.name + "/" + s; t = t.parent; } return s; }
    static void Run()
    {
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Flag);
        var sb = new StringBuilder();
        foreach (var r in Object.FindObjectsOfType<Renderer>(true))
        {
            string n = r.name.ToLower();
            bool water = r.gameObject.layer == 4 || n.Contains("ocean") || n.Contains("water") || n.Contains("sea") || n.Contains("suimono");
            foreach (var m in r.sharedMaterials) if (m != null && (m.shader.name.ToLower().Contains("water") || m.shader.name.ToLower().Contains("suimono") || m.shader.name.ToLower().Contains("ocean"))) water = true;
            if (!water) continue;
            sb.AppendLine(P(r.transform) + " active=" + r.gameObject.activeInHierarchy + " layer=" + r.gameObject.layer + " pos " + r.transform.position.ToString("F1") + " size " + r.bounds.size.ToString("F0"));
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                sb.AppendLine("   mat " + m.name + " (" + AssetDatabase.GetAssetPath(m) + ") shader " + m.shader.name + " queue " + m.renderQueue);
                var sh = m.shader;
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    string pn = sh.GetPropertyName(i);
                    var ty = sh.GetPropertyType(i);
                    string v = ty == UnityEngine.Rendering.ShaderPropertyType.Color ? m.GetColor(pn).ToString() : (ty == UnityEngine.Rendering.ShaderPropertyType.Float || ty == UnityEngine.Rendering.ShaderPropertyType.Range ? m.GetFloat(pn).ToString("F3") : (ty == UnityEngine.Rendering.ShaderPropertyType.Texture ? (m.GetTexture(pn) != null ? m.GetTexture(pn).name : "null") : m.GetVector(pn).ToString()));
                    sb.AppendLine("      " + pn + " = " + v);
                }
            }
            foreach (var mb in r.GetComponents<MonoBehaviour>()) if (mb != null) sb.AppendLine("   script " + mb.GetType().Name);
        }
        File.WriteAllText("Backup_flight/water_probe.txt", sb.ToString());
        Debug.Log("[WaterProbe] 書きました");
    }
}
