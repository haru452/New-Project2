using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
using TMPro;

// MuseumV2.cs の続き（長いので分けたファイル。中身は同じクラス MuseumSetup）
public static partial class MuseumSetup
{
    static void BuildMiniature(Transform R, Vector3 lp, float size, StringBuilder log)
    {
        var g = new GameObject("Miniature").transform;
        g.SetParent(R, false); g.localPosition = lp; g.localRotation = Quaternion.identity;
        Material wood = Mat("MiniTable", null, new Color(0.25f, 0.17f, 0.1f));
        Box("Table", g, new Vector3(0f, 0.42f, 0f), new Vector3(size + 0.4f, 0.84f, size + 0.4f), wood, true);
        Material sea = Mat("MiniSea", null, new Color(0.1f, 0.27f, 0.42f));
        sea.SetFloat("_Glossiness", 0.85f);
        Box("Sea", g, new Vector3(0f, 0.85f, 0f), new Vector3(size, 0.02f, size), sea, false);
        float seaTop = 0.861f;
        // 島
        var src = new List<Terrain>(); var labels = new List<string>(); var roots = new List<Transform>();
        foreach (var t in Object.FindObjectsOfType<Terrain>(true))
        {
            string n = t.name; Transform root = t.transform;
            if (n.StartsWith("Astral")) { labels.Add("ASTRAL"); }
            else if (n == "人がいる島") { labels.Add("SMALL ISLANDS"); }
            else if (n == IslandName) { labels.Add("AEGIS"); }
            else if (t.transform.parent != null && t.transform.parent.name == "military base") { labels.Add("PELAGOS"); root = t.transform.parent; }
            else continue;
            src.Add(t); roots.Add(root);
        }
        if (src.Count == 0) { log.AppendLine("ミニチュア: 島が見つからない"); return; }
        // 高さは、海図（ChartMapSetup）と同じく「当たり判定の高さ」を使う（Terrain の絵の高さとずれている所があるため）。
        //   海面 + 0.3m より低い所は海として描かない → 海図と同じ島の形になり、四角い台が出ない
        var drop = new float[src.Count];
        // 陸のある範囲から縮尺を決める
        Bounds land = new Bounds(); bool has = false;
        for (int ti = 0; ti < src.Count; ti++)
        {
            var t = src[ti];
            var td = t.terrainData; Vector3 p = t.transform.position;
            for (int y = 0; y <= 32; y++) for (int x = 0; x <= 32; x++)
                {
                    float h = ColH(t, x / 32f, y / 32f);
                    if (h < SeaY + 0.3f) continue;
                    Vector3 w = new Vector3(p.x + x / 32f * td.size.x, 0f, p.z + y / 32f * td.size.z);
                    if (!has) { land = new Bounds(w, Vector3.zero); has = true; } else land.Encapsulate(w);
                }
        }
        float span = Mathf.Max(land.size.x, land.size.z) * 1.1f;
        float s = (size * 0.92f) / Mathf.Max(span, 1f);
        float v = s * 3f;   // 高さは 3 倍
        Vector3 c0 = land.center;
        Directory.CreateDirectory(MusDir + "/Mini");
        Material bld = Mat("MiniBuilding", null, new Color(0.62f, 0.6f, 0.56f));
        for (int i = 0; i < src.Count; i++)
        {
            var t = src[i]; var td = t.terrainData; Vector3 p = t.transform.position;
            int N = 110;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var hgt = new float[N + 1, N + 1];
            for (int y = 0; y <= N; y++) for (int x = 0; x <= N; x++)
                {
                    float u = x / (float)N, w = y / (float)N;
                    float h = ColH(t, u, w);
                    hgt[x, y] = h;
                    float wx = p.x + u * td.size.x, wz = p.z + w * td.size.z;
                    verts.Add(new Vector3((wx - c0.x) * s, seaTop + Mathf.Max(h - SeaY, -2f) * v, (wz - c0.z) * s));
                    uvs.Add(new Vector2(u, w));
                }
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    if (Mathf.Max(Mathf.Max(hgt[x, y], hgt[x + 1, y]), Mathf.Max(hgt[x, y + 1], hgt[x + 1, y + 1])) < SeaY + 0.3f) continue;
                    int a = y * (N + 1) + x, b = a + 1, cI = a + N + 1, d = cI + 1;
                    tris.Add(a); tris.Add(cI); tris.Add(b); tris.Add(b); tris.Add(cI); tris.Add(d);
                }
            string mp = MusDir + "/Mini/Mini_" + labels[i].Replace(" ", "_") + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, mp); }
            mesh.Clear(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            // 色（今の塗りから上から見た絵を作る）
            int res = 256; var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
            int ar = td.alphamapResolution; float[,,] am = td.GetAlphamaps(0, 0, ar, ar); int L = Mathf.Min(td.alphamapLayers, LayerCol.Length);
            var layers = td.terrainLayers;
            for (int y = 0; y < res; y++) for (int x = 0; x < res; x++)
                {
                    int ax = x * ar / res, ay = y * ar / res; Color col = Color.black;
                    for (int k = 0; k < td.alphamapLayers; k++)
                    {
                        Color lc = k < LayerCol.Length && layers[k] != null && layers[k].name.StartsWith("Real_") ? LayerCol[k] : new Color(0.4f, 0.42f, 0.3f);
                        col += lc * am[ay, ax, k];
                    }
                    tex.SetPixel(x, y, col);
                }
            tex.Apply();
            string tp = MusDir + "/Mini/Mini_" + labels[i].Replace(" ", "_") + ".png";
            File.WriteAllBytes(tp, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(tp);
            var it = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            Material im = Mat("Mini_" + labels[i].Replace(" ", "_"), null, Color.white);
            im.mainTexture = it; EditorUtility.SetDirty(im);
            var ig = new GameObject("Island_" + labels[i]); ig.transform.SetParent(g, false);
            ig.AddComponent<MeshFilter>().sharedMesh = mesh; ig.AddComponent<MeshRenderer>().sharedMaterial = im;
            // 建物（小さな箱）
            int nb = 0;
            foreach (Transform ch in roots[i])
            {
                string n = ch.name;
                if (ch.GetComponent<Terrain>() != null || n.StartsWith("Cube") || n.StartsWith("solider") || n == "大砲" || n == "Exhibits" || n == "ReturnDoor" || n.StartsWith("SM_") || n == "S11") continue;
                var rs = ch.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) continue;
                Bounds bb = new Bounds(); bool hb = false;
                foreach (var r in rs) { if (r is ParticleSystemRenderer) continue; if (!hb) { bb = r.bounds; hb = true; } else bb.Encapsulate(r.bounds); }
                if (!hb || bb.size.x > 250f || bb.size.z > 250f) continue;
                float by = seaTop + Mathf.Max(bb.min.y - drop[i] - SeaY, 0f) * v;
                Vector3 bs = new Vector3(Mathf.Max(bb.size.x * s, 0.008f), Mathf.Max(bb.size.y * v * 0.5f, 0.006f), Mathf.Max(bb.size.z * s, 0.008f));
                Box("Bldg", ig.transform, new Vector3((bb.center.x - c0.x) * s, by + bs.y * 0.5f, (bb.center.z - c0.z) * s), bs, bld, false);
                nb++;
            }
            // 名札
            Bounds mb = mesh.bounds;
            var lab = new GameObject("Label"); lab.transform.SetParent(ig.transform, false);
            lab.transform.localPosition = new Vector3(mb.center.x, Mathf.Max(mb.max.y, seaTop) + 0.12f, mb.center.z);
            // 表と裏に 1 枚ずつ（裏から見ると左右反転するので、裏は描かない材質にして、反対向きのもう 1 枚を置く）
            for (int side = 0; side < 2; side++)
            {
                var lg = side == 0 ? lab : new GameObject("LabelBack");
                if (side == 1) { lg.transform.SetParent(lab.transform, false); lg.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); }
                var tm = lg.AddComponent<TextMeshPro>();
                tm.text = labels[i]; tm.fontSize = 0.18f * size; tm.alignment = TextAlignmentOptions.Center; tm.color = new Color(1f, 0.95f, 0.8f);
                tm.rectTransform.sizeDelta = new Vector2(0.35f * size, 0.05f * size);
                tm.fontSharedMaterial = OneSidedLabelMat(tm);
            }
            log.AppendLine("ミニチュア: " + labels[i] + "（建物 " + nb + " 個）");
        }
        log.AppendLine("ミニチュア: 縮尺 1/" + (1f / s).ToString("F0") + "（高さは 3 倍）");
    }

    // Terrain の当たり判定の高さ（u, w は 0〜1）。当たらなければ海の底
    static float ColH(Terrain t, float u, float w)
    {
        var td = t.terrainData; Vector3 p = t.transform.position;
        var col = t.GetComponent<TerrainCollider>();
        Vector3 o = new Vector3(p.x + u * td.size.x, p.y + td.size.y + 50f, p.z + w * td.size.z);
        if (col != null && col.enabled && col.Raycast(new Ray(o, Vector3.down), out RaycastHit hit, td.size.y + 200f)) return hit.point.y;
        return SeaY - 5f;
    }

    // 名札の材質: 裏面を描かない（左右反転した字が見えないように）
    static Material OneSidedLabelMat(TextMeshPro tm)
    {
        string mp = MusDir + "/Mini/MiniLabel.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (m == null || m.mainTexture != tm.font.material.mainTexture)
        {
            if (m != null) AssetDatabase.DeleteAsset(mp);
            m = new Material(tm.font.material);
            AssetDatabase.CreateAsset(m, mp);
        }
        m.SetFloat("_CullMode", 2f);   // 2 = 裏を描かない
        EditorUtility.SetDirty(m);
        return m;
    }

    // Backup_flight/run_mini.flag がある時に「ミニチュアだけ作り直す」を 1 回だけ
    [InitializeOnLoadMethod]
    static void MiniFlag() { EditorApplication.delayCall += MiniFlagTry; }
    static void MiniFlagTry()
    {
        if (!File.Exists("Backup_flight/run_mini.flag")) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += MiniFlagTry; return; }
        File.Delete("Backup_flight/run_mini.flag");
        RebuildMiniature();
    }
}
