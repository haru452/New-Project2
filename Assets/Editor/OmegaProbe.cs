using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: Omega_1 の部品（材質・透明さ）と、操縦桿・座席・目の位置
[InitializeOnLoad]
public static class OmegaProbe
{
    const string Flag = "Backup_flight/omega_probe.flag";
    static OmegaProbe() { EditorApplication.delayCall += Run; }
    static string P(Transform t, Transform stop) { string s = t.name; t = t.parent; while (t != null && t != stop) { s = t.name + "/" + s; t = t.parent; } return s; }
    static void Run()
    {
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Flag);
        var sb = new StringBuilder();
        controller c = null;
        foreach (var x in Object.FindObjectsOfType<controller>(true)) if (x.car != null && x.car.name == "Omega_1" && x.radarTracks != null && x.radarTracks.Length > 0) { c = x; break; }
        if (c == null) { File.WriteAllText("Backup_flight/omega_probe.txt", "Omega_1 が無い"); return; }
        Transform car = c.car.transform;
        sb.AppendLine("car " + car.name + " scale " + car.lossyScale.ToString("F3"));
        foreach (var r in car.GetComponentsInChildren<Renderer>(true))
        {
            string path = P(r.transform, car);
            if (path.Contains("Levers") || path.Contains("Interior") || path.Contains("Panel") || path.Contains("Radar") || path.Contains("NavLights") || path.Contains("Txt")) continue;
            if (r is ParticleSystemRenderer) continue;
            sb.Append(path + " en=" + r.enabled + " act=" + r.gameObject.activeInHierarchy + " c=" + car.InverseTransformPoint(r.bounds.center).ToString("F2") + " sz=" + r.bounds.size.ToString("F2"));
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) { sb.Append(" [null]"); continue; }
                sb.Append(" [" + m.name + " / " + m.shader.name + " q" + m.renderQueue + (m.HasProperty("_Color") ? " col" + m.color.ToString("F2") : "") + (m.HasProperty("_Mode") ? " mode" + m.GetFloat("_Mode") : "") + "]");
            }
            sb.AppendLine();
        }
        foreach (var n in new[] { "Levers", "StickPivot", "ThrottlePivot" })
        {
            foreach (var t in car.GetComponentsInChildren<Transform>(true)) if (t.name == n) { sb.AppendLine(n + " carLocal " + car.InverseTransformPoint(t.position).ToString("F3") + " parent " + t.parent.name); break; }
        }
        sb.AppendLine("controller obj " + P(c.transform, null) + " carLocal " + car.InverseTransformPoint(c.transform.position).ToString("F3"));
        var st = c.GetComponent<VRC.SDK3.Components.VRCStation>();
        if (st != null && st.stationEnterPlayerLocation != null) sb.AppendLine("station enter carLocal " + car.InverseTransformPoint(st.stationEnterPlayerLocation.position).ToString("F3"));
        File.WriteAllText("Backup_flight/omega_probe.txt", sb.ToString());
        Debug.Log("[OmegaProbe] 書きました");
    }
}
