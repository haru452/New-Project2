using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 島（Terrain）のテクスチャを写真の素材（ProceduralTerrainPainter の CC0 素材・凹凸付き）に替え、
// すべての島を同じルールで自動で塗り直す:
//   海辺・海の底 = 砂、急な斜面 = 岩、岩と砂の境目 = 砂利、平ら = 草、ゆるい斜面と所々 = 土
// 元の TerrainData は Backup_flight/terrain/ にコピーしてから塗る。
// メニュー「Tools/島のテクスチャをリアルにする」。Backup_flight/run_terrain.flag があれば再読み込み時に一度だけ自動実行。
[InitializeOnLoad]
public static class TerrainRepaint
{
    const string FlagPath = "Backup_flight/run_terrain.flag";
    const string ReportPath = "Backup_flight/terrain_report.txt";
    const string Src = "Assets/ProceduralTerrainPainter/_Demo/Textures/";
    const string LayerDir = "Assets/My material/TerrainReal";
    const float SeaY = -27.7f;

    static TerrainRepaint()
    {
        EditorApplication.delayCall += TryAutoRun;
        EditorApplication.playModeStateChanged += st => { if (st == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryAutoRun; };
    }
    static void TryAutoRun()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlaying) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryAutoRun; return; }
        File.Delete(FlagPath);
        Run();
    }

    static TerrainLayer Layer(string name, string tex, float tile, float smooth, StringBuilder log)
    {
        Directory.CreateDirectory(LayerDir);
        string p = LayerDir + "/" + name + ".terrainlayer";
        var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
        if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, p); }
        l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Src + tex + "_Albedo.png");
        l.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Src + tex + "_Normals.png");
        l.normalScale = 1f;
        l.tileSize = new Vector2(tile, tile);
        l.metallic = 0f;
        l.smoothness = smooth;
        if (l.diffuseTexture == null) log.AppendLine("素材が無い: " + tex);
        EditorUtility.SetDirty(l);
        return l;
    }

    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
    static float Noise(float x, float z, float scale, float seed) { return Mathf.PerlinNoise(x / scale + seed, z / scale + seed * 1.7f); }

    [MenuItem("Tools/島のテクスチャをリアルにする")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("TerrainRepaint " + System.DateTime.Now);
        try
        {
            if (EditorApplication.isPlaying) { log.AppendLine("ERROR: 再生中"); return; }
            // 0 草, 1 土, 2 岩, 3 砂, 4 砂利
            var layers = new[] {
                Layer("Real_Grass", "Ground_037", 7f, 0.05f, log),
                Layer("Real_Dirt", "Ground_008", 6f, 0.05f, log),
                Layer("Real_Rock", "Rock_003", 14f, 0.15f, log),
                Layer("Real_Sand", "Sand_034", 5f, 0.1f, log),
                Layer("Real_Gravel", "Gravel_020", 5f, 0.1f, log) };
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Backup_flight/terrain");
            var done = new HashSet<TerrainData>();
            foreach (var t in Object.FindObjectsOfType<Terrain>(true))
            {
                var td = t.terrainData;
                if (td == null || done.Contains(td)) continue;
                done.Add(td);
                string ap = AssetDatabase.GetAssetPath(td);
                if (!string.IsNullOrEmpty(ap))
                {
                    string bk = "Backup_flight/terrain/" + Path.GetFileName(ap);
                    if (!File.Exists(bk)) File.Copy(ap, bk);
                }
                Undo.RegisterCompleteObjectUndo(td, "Terrain repaint");
                td.terrainLayers = layers;
                if (td.alphamapResolution < 512) td.alphamapResolution = 512;
                int res = td.alphamapResolution;
                var map = new float[res, res, layers.Length];
                Vector3 tp = t.transform.position, sz = td.size;
                int counts0 = 0, counts2 = 0, counts3 = 0, land = 0;
                var hs = new List<float>();
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float nx = (x + 0.5f) / res, ny = (y + 0.5f) / res;
                        float h = tp.y + td.GetInterpolatedHeight(nx, ny);
                        float slope = td.GetSteepness(nx, ny);
                        float wx = tp.x + nx * sz.x, wz = tp.z + ny * sz.z;
                        float above = h - SeaY;
                        float n1 = Noise(wx, wz, 60f, 3.1f), n2 = Noise(wx, wz, 14f, 7.7f), n3 = Noise(wx, wz, 200f, 1.3f);
                        // 岩: 急な斜面（ノイズで境目をぼかす）。高い所ほど岩が出やすい
                        float rockStart = 30f - (above > 80f ? 6f : 0f) + (n2 - 0.5f) * 8f;
                        float rock = Smooth(rockStart, rockStart + 9f, slope);
                        // 砂: 海辺（海面から 1.5〜3m）と海の底
                        float beachTop = 1.8f + n1 * 2.2f;
                        float sand = Smooth(beachTop + 1.2f, beachTop - 0.4f, above);
                        // 砂利: 海辺の岩と砂の境目、急な斜面の下
                        float gravel = Smooth(beachTop + 4f, beachTop, above) * Smooth(18f, 28f, slope) * 0.9f;
                        // 土: ゆるい斜面と、所々の地面（大きさの違うノイズ）
                        float dirt = Mathf.Max(Smooth(16f, 26f, slope) * 0.8f, Smooth(0.58f, 0.72f, n1 * 0.6f + n3 * 0.4f) * 0.85f);
                        float grass = 1f;
                        // 重ねる（後のほうが上）
                        float[] w = new float[5];
                        w[0] = grass;
                        w[1] = dirt;
                        for (int k = 0; k < 1; k++) { w[0] *= 1f - dirt; }
                        float rest = 1f;
                        // 砂利 → 岩 → 砂 の順に上書き
                        System.Action<int, float> over = (idx, a) =>
                        {
                            for (int k = 0; k < 5; k++) w[k] *= 1f - a;
                            w[idx] += a;
                        };
                        over(4, gravel);
                        over(2, rock);
                        over(3, Mathf.Clamp01(sand * (1f - rock * 0.7f)));
                        float sum = 0f; for (int k = 0; k < 5; k++) sum += w[k];
                        for (int k = 0; k < 5; k++) map[y, x, k] = w[k] / Mathf.Max(sum, 0.0001f);
                        if (above > 0f)
                        {
                            land++;
                            if ((x & 3) == 0 && (y & 3) == 0) hs.Add(above);
                            if (w[0] > 0.5f) counts0++;
                            if (w[2] > 0.5f) counts2++;
                            if (w[3] > 0.5f) counts3++;
                        }
                        rest = 0f;
                    }
                td.SetAlphamaps(0, 0, map);
                EditorUtility.SetDirty(td);
                float tot = Mathf.Max(land, 1);
                hs.Sort();
                string pct = hs.Count > 0 ? ("  陸の高さ(海面から) 10%:" + hs[hs.Count / 10].ToString("F1") + " 50%:" + hs[hs.Count / 2].ToString("F1") + " 90%:" + hs[hs.Count * 9 / 10].ToString("F1") + "m") : "";
                log.AppendLine(t.name + "（" + Path.GetFileName(ap) + "）陸 " + (land / (float)(res * res) * 100f).ToString("F0") + "%" + pct + " / 陸のうち: 草 " + (counts0 / tot * 100f).ToString("F0") + "% 岩 " + (counts2 / tot * 100f).ToString("F0") + "% 砂 " + (counts3 / tot * 100f).ToString("F0") + "%（塗りの細かさ " + res + "）");
                // 遠くでもぼやけすぎないように
                t.basemapDistance = Mathf.Max(t.basemapDistance, 1500f);
                EditorUtility.SetDirty(t);
            }
            AssetDatabase.SaveAssets();
            var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(sc);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(sc) ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION: " + e); }
        finally
        {
            Directory.CreateDirectory("Backup_flight");
            File.WriteAllText(ReportPath, log.ToString());
            Debug.Log(log.ToString());
        }
    }
}
