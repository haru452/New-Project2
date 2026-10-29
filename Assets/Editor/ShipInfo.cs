using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 調べるだけ: 船（WarShip）の高さ。見た目の一番下と海面の差
[InitializeOnLoad]
public static class ShipInfo
{
    const string FlagPath = "Backup_flight/run_shipinfo.flag";
    static ShipInfo() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(FlagPath);
        var sb = new StringBuilder();
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
        {
            Transform m = ws.model != null ? ws.model.transform : ws.transform;
            Bounds b = new Bounds(m.position, Vector3.zero); bool h = false;
            foreach (var r in m.GetComponentsInChildren<Renderer>(true)) { if (r is ParticleSystemRenderer || r is TrailRenderer) continue; if (!h) { b = r.bounds; h = true; } else b.Encapsulate(r.bounds); }
            sb.AppendLine(ws.name + (ws.gameObject.activeInHierarchy ? "" : " [off]") + " model " + (ws.model != null ? ws.model.name : "-") + " kind " + (ws.target != null ? ws.target.kind : -9) + " root " + ws.transform.position.ToString("F1") + " modelPos " + m.position.ToString("F1") + " 下端 " + b.min.y.ToString("F1") + " 上端 " + b.max.y.ToString("F1") + " 海面との差 " + (b.min.y - ws.seaLevel).ToString("F1") + " roaming " + ws.roaming);
        }
        File.WriteAllText("Backup_flight/shipinfo.txt", sb.ToString());
    }
}
