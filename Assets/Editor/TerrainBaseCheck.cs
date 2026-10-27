using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 調べるだけ: 各 Terrain の一番多い高さ（四角い台）と海面との差を Backup_flight/terrain_base.txt に書く
[InitializeOnLoad]
public static class TerrainBaseCheck
{
    const string Flag = "Backup_flight/run_terrainbase.flag";
    static TerrainBaseCheck() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        var sb = new StringBuilder();
        float sea = -27.7f;
        var ocean = GameObject.Find("Ocean"); if (ocean != null) sea = ocean.transform.position.y;
        sb.AppendLine("海面 " + sea);
        foreach (var t in Object.FindObjectsOfType<Terrain>(true))
        {
            var td = t.terrainData; float py = t.transform.position.y;
            var hist = new Dictionary<float, int>(); float mn = float.MaxValue, mx = float.MinValue;
            for (int y = 0; y <= 100; y++) for (int x = 0; x <= 100; x++)
                {
                    float h = py + td.GetInterpolatedHeight(x / 100f, y / 100f);
                    mn = Mathf.Min(mn, h); mx = Mathf.Max(mx, h);
                    float bin = Mathf.Floor(h * 2f) / 2f; int c; hist.TryGetValue(bin, out c); hist[bin] = c + 1;
                }
            float best = 0; int bc = -1; foreach (var kv in hist) if (kv.Value > bc) { bc = kv.Value; best = kv.Key; }
            // 四辺（縁）の高さ
            float edgeMax = float.MinValue;
            for (int i = 0; i <= 100; i++)
                foreach (var uv in new[] { new Vector2(i / 100f, 0f), new Vector2(i / 100f, 1f), new Vector2(0f, i / 100f), new Vector2(1f, i / 100f) })
                    edgeMax = Mathf.Max(edgeMax, py + td.GetInterpolatedHeight(uv.x, uv.y));
            string path = t.name; for (var p = t.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
            sb.AppendLine(path + (t.gameObject.activeInHierarchy ? "" : " [off]") + ": 一番多い高さ " + best.ToString("F1") + "（海面から " + (best - sea).ToString("F1") + "m, " + (bc / 101f / 101f * 100f).ToString("F0") + "%） 縁の最高 " + (edgeMax - sea).ToString("F1") + "m 最低 " + (mn - sea).ToString("F1") + " 最高 " + (mx - sea).ToString("F1"));
        }
        File.WriteAllText("Backup_flight/terrain_base.txt", sb.ToString());
    }
}
