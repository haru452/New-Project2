using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 島（Terrain）の四角い台・四角い縁を海に沈める
//  ・海面すぐ上（1.5m 以内）に広がる平らな台 → 台の高さ +0.6m より低い所を海面の 2.5m 下へ
//  ・Terrain の縁が海より上にある（四角い崖が見える）→ 縁から 80m で、なめらかに海面の 3m 下まで下げる
// 元の TerrainData は Backup_flight/terrain_sink/ に保存。終わったらテクスチャの塗り直しと博物館のミニチュアも作り直す。
[InitializeOnLoad]
public static class TerrainSink
{
    const string Flag = "Backup_flight/run_terrainsink.flag";
    static TerrainSink() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlaying) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        Run();
    }

    [MenuItem("Tools/島の四角い台を海に沈める")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("TerrainSink " + System.DateTime.Now);
        try
        {
            float sea = -27.7f;
            var ocean = GameObject.Find("Ocean"); if (ocean != null) sea = ocean.transform.position.y;
            Directory.CreateDirectory("Backup_flight/terrain_sink");
            var done = new HashSet<TerrainData>();
            foreach (var t in Object.FindObjectsOfType<Terrain>(true))
            {
                var td = t.terrainData; if (td == null || done.Contains(td)) continue; done.Add(td);
                string ap = AssetDatabase.GetAssetPath(td);
                if (!string.IsNullOrEmpty(ap)) { string bk = "Backup_flight/terrain_sink/" + Path.GetFileName(ap); if (!File.Exists(bk)) File.Copy(ap, bk); }
                Undo.RegisterCompleteObjectUndo(td, "Terrain sink");
                int res = td.heightmapResolution;
                float[,] hs = td.GetHeights(0, 0, res, res);
                float py = t.transform.position.y, sy = td.size.y;
                // Terrain は自分の位置より下には下げられないので、必要なら Terrain ごと下げて、高さを足して戻す（中の物は元の高さのまま）
                bool shifted = false;
                float need = (sea - 4f) - py;
                if (need < 0f)
                {
                    float dlt = -need;
                    float maxN = 0f;
                    for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) maxN = Mathf.Max(maxN, hs[y, x]);
                    if (maxN + dlt / sy > 1f) { log.AppendLine(t.name + ": 高さの余裕が無いので下げられない"); }
                    else
                    {
                        for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) hs[y, x] += dlt / sy;
                        Undo.RecordObject(t.transform, "Terrain sink");
                        var kids = new System.Collections.Generic.List<Transform>();
                        foreach (Transform ch in t.transform) kids.Add(ch);
                        var keep = new System.Collections.Generic.List<Vector3>();
                        foreach (var ch in kids) keep.Add(ch.position);
                        t.transform.position -= new Vector3(0f, dlt, 0f);
                        for (int i = 0; i < kids.Count; i++) { Undo.RecordObject(kids[i], "Terrain sink"); kids[i].position = keep[i]; }
                        py -= dlt; shifted = true;
                        log.AppendLine(t.name + ": Terrain を " + dlt.ToString("F1") + "m 下げた（地形と中の物の高さはそのまま）");
                    }
                }
                // 一番多い高さ
                var hist = new Dictionary<float, int>(); int n = 0;
                for (int y = 0; y < res; y += 4) for (int x = 0; x < res; x += 4)
                    { float h = py + hs[y, x] * sy; float b = Mathf.Floor(h * 2f) / 2f; int c; hist.TryGetValue(b, out c); hist[b] = c + 1; n++; }
                float baseH = 0; int bc = -1; foreach (var kv in hist) if (kv.Value > bc) { bc = kv.Value; baseH = kv.Key; }
                float share = bc / (float)n;
                bool plate = baseH - sea > 0f && baseH - sea <= 1.5f && share > 0.3f;
                float low = (sea - 2.5f - py) / sy;
                float edgeLow = (sea - 3f - py) / sy;
                float cell = td.size.x / (res - 1);
                int W = Mathf.Max(2, Mathf.RoundToInt(80f / cell));
                int platePts = 0, edgePts = 0;
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float h = py + hs[y, x] * sy;
                        if (plate && h < baseH + 0.6f) { hs[y, x] = low; platePts++; h = sea - 2.5f; }
                        int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(res - 1 - x, res - 1 - y));
                        if (d < W && h > sea - 3f)
                        {
                            float k = 1f - Mathf.SmoothStep(0f, 1f, d / (float)W);
                            float nh = Mathf.Lerp(hs[y, x], edgeLow, k);
                            if (nh < hs[y, x]) { hs[y, x] = nh; edgePts++; }
                        }
                    }
                if (platePts > 0 || edgePts > 0 || shifted)
                {
                    td.SetHeights(0, 0, hs);
                    EditorUtility.SetDirty(td);
                }
                string path = t.name; for (var p = t.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                log.AppendLine(path + ": 台 " + (baseH - sea).ToString("F1") + "m（" + (share * 100f).ToString("F0") + "%）" + (plate ? " → 台を沈めた " + platePts + " 点" : "") + " / 縁を下げた " + edgePts + " 点");
            }
            AssetDatabase.SaveAssets();
            var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(sc);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(sc) ? "OK" : "失敗"));
            // テクスチャの塗り直し → 博物館のミニチュアの作り直し
            TerrainRepaint.Run();
            File.WriteAllText("Backup_flight/run_museum2.flag", "after sink");
            log.AppendLine("テクスチャを塗り直しました。博物館のミニチュアも作り直します");
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION: " + e); }
        finally { File.WriteAllText("Backup_flight/terrain_sink_report.txt", log.ToString()); }
    }
}
