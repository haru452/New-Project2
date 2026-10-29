using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using TMPro;

// =============================================================
//  メインルーム（出発地点）の海図台の地図を作る
//  ・ワールドを真上から撮って、平らな地図の画像にする（雲・水面は外して、海は地図の青）
//  ・敵の船の位置に、デフォルメした船のアイコンを描く（空母は大きく、貨物船は小さく）
//  ・出撃地点（潜水艦）は青い星
//  Backup_flight/run_chartmap.flag があると、Unity に戻った時に 1 回だけ動く
// =============================================================
[InitializeOnLoad]
public static class ChartMapSetup
{
    const string Flag = "Backup_flight/run_chartmap.flag";
    public const string Png = "Assets/My script/bridge/chart_map.png";
    const int W = 2048, H = 1536;   // 海図台の地図と同じ 4:3

    static ChartMapSetup() { EditorApplication.delayCall += () => { if (File.Exists(Flag) && !EditorApplication.isPlayingOrWillChangePlaymode) { File.Delete(Flag); tries = 0; TryRun(); } }; }

    // 新しい U# スクリプトのコンパイルが終わっていない時は、少し待ってからやり直す（最大 6 回）
    static int tries;
    static double retryAt;
    static void TryRun()
    {
        try { Run(); }
        catch (System.Exception e)
        {
            if (e.Message.Contains("outdated script version") && tries < 6)
            {
                tries++;
                retryAt = EditorApplication.timeSinceStartup + 5.0;
                EditorApplication.update += WaitRetry;
                File.WriteAllText("Backup_flight/chartmap_report.txt", "コンパイル待ち… " + tries + " 回目のやり直しを 5 秒後に");
                return;
            }
            File.WriteAllText("Backup_flight/chartmap_report.txt", "ERROR: " + e);
            Debug.LogException(e);
        }
    }
    static void WaitRetry()
    {
        if (EditorApplication.timeSinceStartup < retryAt || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= WaitRetry;
        TryRun();
    }

    [MenuItem("Tools/Flight/Chart Map (main room)")]
    public static void Run()
    {
        var log = new StringBuilder();
        // ---- 範囲: 地形・島・船が全部入る長方形（4:3）----
        bool has = false; Bounds b = new Bounds();
        foreach (var t in Object.FindObjectsOfType<Terrain>())
        {
            if (!t.gameObject.activeInHierarchy || t.terrainData == null) continue;
            var tb = new Bounds(t.transform.position + t.terrainData.size * 0.5f, t.terrainData.size);
            if (!has) { b = tb; has = true; } else b.Encapsulate(tb);
        }
        var ships = new List<WarShip>();
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
        {
            if (!ws.gameObject.activeInHierarchy) continue;
            ships.Add(ws);
            Vector3 p = ShipPos(ws);
            if (!has) { b = new Bounds(p, Vector3.one); has = true; } else b.Encapsulate(p);
        }
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        Vector3 start = putOut != null && putOut.catapultPoint != null ? putOut.catapultPoint.position : Vector3.zero;
        if (!has) b = new Bounds(start, new Vector3(20000f, 1f, 20000f)); else b.Encapsulate(start);
        float sx = b.size.x * 1.08f + 1000f, sz = b.size.z * 1.08f + 1000f;
        if (sx / sz > (float)W / H) sz = sx * H / W; else sx = sz * W / H;
        Vector3 c = b.center;
        log.AppendLine("地図の範囲: 中心 " + c.ToString("F0") + "  横 " + sx.ToString("F0") + "m × 縦 " + sz.ToString("F0") + "m");

        // ---- 真上から撮る ----
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;
        var hidden = new List<Renderer>();
        foreach (var r in Object.FindObjectsOfType<Renderer>())
        {
            if (!r.enabled) continue;
            string n = r.name.ToLower();
            string mn = r.sharedMaterial != null ? r.sharedMaterial.name.ToLower() : "";
            bool water = r.gameObject.layer == 4 || n.Contains("ocean") || n.Contains("water") || n.Contains("suimono") || mn.Contains("water") || mn.Contains("ocean");
            bool cloud = n.Contains("cloud") || mn.Contains("cloud");
            if (water || cloud || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) { r.enabled = false; hidden.Add(r); }
        }
        Color sea = new Color(0.16f, 0.36f, 0.55f);
        var camGo = new GameObject("ChartMapCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = sz * 0.5f;
        cam.aspect = (float)W / H;
        cam.transform.position = new Vector3(c.x, seaY + 3000f, c.z);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 真下を向く・上が北（+Z）
        cam.nearClipPlane = 1f; cam.farClipPlane = 6000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = sea;
        cam.cullingMask = ~((1 << 4) | (1 << 9) | (1 << 10) | (1 << 18) | (1 << 5));
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        bool fog = RenderSettings.fog; RenderSettings.fog = false;
        cam.Render();
        RenderSettings.fog = fog;
        var img = new Texture2D(W, H, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        img.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        foreach (var r in hidden) if (r != null) r.enabled = true;

        Color32[] px = img.GetPixels32();
        float mPerPx = sx / W;
        // 陸か海かを、上から当たり判定で調べる（半分の細かさ）。海の中に見えている海底は、深さに合わせた海の色にする
        Physics.SyncTransforms();
        int GW = W / 2, GH = H / 2;
        float[] gh = new float[GW * GH];
        int mask = ~((1 << 4) | (1 << 9) | (1 << 10) | (1 << 18));
        for (int gy = 0; gy < GH; gy++)
        for (int gx = 0; gx < GW; gx++)
        {
            Vector3 o = new Vector3(c.x - sx / 2f + (gx * 2 + 1) * mPerPx, seaY + 1500f, c.z - sz / 2f + (gy * 2 + 1) * mPerPx);
            RaycastHit hit;
            gh[gy * GW + gx] = (Physics.Raycast(o, Vector3.down, out hit, 1700f, mask, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody == null) ? hit.point.y - seaY : -999f;
        }
        Color shallow = new Color(0.30f, 0.80f, 0.82f);   // 島のそばの浅瀬（明るい水色）
        Color mid = new Color(0.18f, 0.55f, 0.72f);
        // 陸からの距離（マス）。島のまわりほど明るい水色 → 沖へ行くほど深い青
        const int MaxD = 24;
        int[] dist = new int[GW * GH];
        var q = new System.Collections.Generic.Queue<int>();
        for (int k = 0; k < GW * GH; k++) { if (gh[k] > 0.3f) { dist[k] = 0; q.Enqueue(k); } else dist[k] = 9999; }
        // 距離は 2 回なぞって求める（縦横 3・斜め 4。丸い形になる。単位は 1/3 マス）
        for (int k = 0; k < GW * GH; k++) if (dist[k] == 0) dist[k] = 0; else dist[k] = 99999;
        for (int y0 = 0; y0 < GH; y0++) for (int x0 = 0; x0 < GW; x0++)
        {
            int k = y0 * GW + x0; int v0 = dist[k];
            if (x0 > 0) v0 = Mathf.Min(v0, dist[k - 1] + 3);
            if (y0 > 0) { v0 = Mathf.Min(v0, dist[k - GW] + 3); if (x0 > 0) v0 = Mathf.Min(v0, dist[k - GW - 1] + 4); if (x0 < GW - 1) v0 = Mathf.Min(v0, dist[k - GW + 1] + 4); }
            dist[k] = v0;
        }
        for (int y0 = GH - 1; y0 >= 0; y0--) for (int x0 = GW - 1; x0 >= 0; x0--)
        {
            int k = y0 * GW + x0; int v0 = dist[k];
            if (x0 < GW - 1) v0 = Mathf.Min(v0, dist[k + 1] + 3);
            if (y0 < GH - 1) { v0 = Mathf.Min(v0, dist[k + GW] + 3); if (x0 < GW - 1) v0 = Mathf.Min(v0, dist[k + GW + 1] + 4); if (x0 > 0) v0 = Mathf.Min(v0, dist[k + GW - 1] + 4); }
            dist[k] = v0;
        }
        BakeShore(dist, GW, GH, c.x - sx / 2f, c.z - sz / 2f, sx, sz, log);
        int land = 0;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int gx = Mathf.Min(x / 2, GW - 1), gy = Mathf.Min(y / 2, GH - 1);
            float h = gh[gy * GW + gx];
            int i = y * W + x;
            if (h > 0.3f)
            {
                land++;
                Color col = px[i];
                Color.RGBToHSV(col, out float hh, out float ss, out float vv);
                col = Color.HSVToRGB(hh, Mathf.Clamp01(ss * 1.15f), Mathf.Clamp01(vv * 1.1f));
                // 海岸線: となりが海なら濃い線
                bool edge = (gx > 0 && gh[gy * GW + gx - 1] <= 0.3f) || (gx < GW - 1 && gh[gy * GW + gx + 1] <= 0.3f)
                         || (gy > 0 && gh[(gy - 1) * GW + gx] <= 0.3f) || (gy < GH - 1 && gh[(gy + 1) * GW + gx] <= 0.3f);
                if (edge) col = Color.Lerp(col, new Color(0.18f, 0.14f, 0.08f), 0.6f);
                px[i] = col;
            }
            else
            {
                // 島のそばは浅瀬の水色、離れるほど深い青（少しだけ波のむら）
                float dd = Mathf.Min(dist[gy * GW + gx] / 3f, MaxD);
                float t = Mathf.Clamp01((dd + (Mathf.PerlinNoise(x * 0.02f, y * 0.02f) - 0.5f) * 4f) / MaxD);
                Color sc = t < 0.35f ? Color.Lerp(shallow, mid, t / 0.35f) : Color.Lerp(mid, sea, (t - 0.35f) / 0.65f);
                float wv = (Mathf.PerlinNoise(x * 0.11f + 3f, y * 0.11f) - 0.5f) * 0.035f;
                px[i] = new Color(sc.r + wv, sc.g + wv, sc.b + wv);
            }
        }
        log.AppendLine("陸: " + (land * 100f / (W * H)).ToString("F1") + "%");
        // 方眼（2km ごと）
        Color32 grid = new Color32(255, 255, 255, 40);
        for (float gx = Mathf.Ceil((c.x - sx / 2f) / 2000f) * 2000f; gx < c.x + sx / 2f; gx += 2000f)
        {
            int x = Mathf.RoundToInt((gx - (c.x - sx / 2f)) / mPerPx);
            for (int y = 0; y < H; y++) Blend(px, x, y, grid);
        }
        for (float gz = Mathf.Ceil((c.z - sz / 2f) / 2000f) * 2000f; gz < c.z + sz / 2f; gz += 2000f)
        {
            int y = Mathf.RoundToInt((gz - (c.z - sz / 2f)) / mPerPx);
            for (int x = 0; x < W; x++) Blend(px, x, y, grid);
        }

        // ---- 出撃地点（青い星）----
        Vector2 sp = new Vector2((start.x - (c.x - sx / 2f)) / mPerPx, (start.z - (c.z - sz / 2f)) / mPerPx);
        Star(px, sp, 26f, new Color32(20, 30, 60, 255));
        Star(px, sp, 20f, new Color32(80, 200, 255, 255));
        // ---- 方位（右上に N の矢印）と枠 ----
        Vector2 na = new Vector2(W - 90, H - 140);
        Poly(px, new[] { na + new Vector2(0, 70), na + new Vector2(-24, -10), na + new Vector2(0, 8), na + new Vector2(24, -10) }, new Color32(250, 250, 250, 255));
        for (int t = 0; t < 10; t++) for (int x = 0; x < W; x++) { Set(px, x, t, new Color32(60, 45, 30, 255)); Set(px, x, H - 1 - t, new Color32(60, 45, 30, 255)); }
        for (int t = 0; t < 10; t++) for (int y = 0; y < H; y++) { Set(px, t, y, new Color32(60, 45, 30, 255)); Set(px, W - 1 - t, y, new Color32(60, 45, 30, 255)); }

        img.SetPixels32(px);
        Directory.CreateDirectory(Path.GetDirectoryName(Png));
        File.WriteAllBytes(Png, img.EncodeToPNG());
        Object.DestroyImmediate(img);
        AssetDatabase.ImportAsset(Png);
        var imp = AssetImporter.GetAtPath(Png) as TextureImporter;
        if (imp != null) { imp.maxTextureSize = 2048; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Png);

        // ---- 海図台に貼る ----
        int set = 0;
        MeshRenderer mapR = null;
        foreach (var r in Object.FindObjectsOfType<MeshRenderer>(true))
        {
            if (r.name != "Map" || r.transform.parent == null || r.transform.parent.name != "ChartTable") continue;
            var m = r.sharedMaterial;
            if (m == null) continue;
            Undo.RecordObject(m, "Chart map");
            m.mainTexture = tex;
            EditorUtility.SetDirty(m);
            set++;
            mapR = r;
        }
        if (mapR != null) SetupLive(mapR, c.x - sx / 2f, c.z - sz / 2f, sx, sz, gh, GW, GH, mPerPx * 2f, seaY, log);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        log.AppendLine("海図台の地図に貼りました: " + set + " 枚（" + Png + "）");
        File.WriteAllText("Backup_flight/chartmap_report.txt", log.ToString());
        Debug.Log("[ChartMap]\n" + log);
    }


    // ---- 味方の戦闘機の印（リアルタイム）と島の名前 ----
    const string LiveCs = "Assets/My script/bridge/ChartMapLive.cs";
    const string LiveAsset = "Assets/My script/bridge/ChartMapLive.asset";
    const string InfoCs = "Assets/My script/bridge/IslandInfo.cs";
    const string InfoAsset = "Assets/My script/bridge/IslandInfo.asset";
    static void SetupLive(MeshRenderer mapR, float xmin, float zmin, float sx, float sz, float[] gh, int GW, int GH, float cell, float seaY, StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(LiveAsset) == null)
        {
            var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(LiveCs);
            if (ms == null) { log.AppendLine("ERROR: " + LiveCs + " が無い"); return; }
            var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            prog.sourceCsScript = ms;
            AssetDatabase.CreateAsset(prog, LiveAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        }
        Transform mapT = mapR.transform;
        Transform table = mapT.parent;
        Transform old = table.Find("ChartLive");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject("ChartLive");
        Undo.RegisterCreatedObjectUndo(root, "Chart map");
        root.transform.SetParent(table, false);

        // 地図の板の UV（0,0）（1,0）（0,1）が mapT の中のどこか
        Mesh mesh = mapR.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vs = mesh.vertices; Vector2[] uv = mesh.uv;
        Vector3 p00 = Vector3.zero, p10 = Vector3.zero, p01 = Vector3.zero;
        for (int i = 0; i < vs.Length; i++)
        {
            if (uv[i].x < 0.5f && uv[i].y < 0.5f) p00 = vs[i];
            if (uv[i].x > 0.5f && uv[i].y < 0.5f) p10 = vs[i];
            if (uv[i].x < 0.5f && uv[i].y > 0.5f) p01 = vs[i];
        }
        Vector3 du = p10 - p00, dv = p01 - p00;
        Vector3 nrm = Vector3.Cross(du, dv).normalized;
        if (mapT.TransformDirection(nrm).y < 0f) nrm = -nrm;   // 上（見る人の方）を表に

        var live = UdonSharpEditor.UdonSharpUndo.AddComponent<ChartMapLive>(root);
        live.mapT = mapT; live.p00 = p00; live.du = du; live.dv = dv; live.nrm = nrm;
        live.xmin = xmin; live.zmin = zmin; live.sx = sx; live.sz = sz;

        // 矢印の画像（水色 = 飛んでいる・灰色 = 誰も乗っていない）
        live.flyMat = ArrowMat("ChartArrowFly", new Color32(80, 235, 255, 255));
        live.idleMat = ArrowMat("ChartArrowIdle", new Color32(170, 180, 190, 255));
        var planes = new List<controller>();
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.car != null && c.radarTracks != null && c.radarTracks.Length > 0) planes.Add(c);   // 味方の戦闘機（レーダー付き = プレイヤーが乗る機体）
        planes.Sort((a, b) => string.CompareOrdinal(a.car.name, b.car.name));
        var mk = new Transform[planes.Count]; var mr = new Renderer[planes.Count];
        float worldW = mapT.TransformVector(du).magnitude;
        for (int i = 0; i < planes.Count; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "Plane_" + planes[i].car.name;
            q.transform.SetParent(root.transform, true);
            q.transform.localScale = Vector3.one * 0.06f;
            q.GetComponent<MeshRenderer>().sharedMaterial = live.idleMat;
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.SetActive(false);
            mk[i] = q.transform; mr[i] = q.GetComponent<MeshRenderer>();
        }
        live.planes = planes.ToArray(); live.markers = mk; live.markerR = mr;

        // 島の名前: 陸のかたまりごとに、その島（シーンの一番上の物）の名前を英語で
        var labels = new List<Transform>();
        var infoMats = new List<Material>(); var infoSizes = new List<Vector2>();
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(InfoAsset) == null)
        {
            var ms2 = AssetDatabase.LoadAssetAtPath<MonoScript>(InfoCs);
            if (ms2 != null)
            {
                var prog2 = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                prog2.sourceCsScript = ms2;
                AssetDatabase.CreateAsset(prog2, InfoAsset);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
            }
        }
        var seen = new Dictionary<string, Vector3>();
        var count = new Dictionary<string, int>();
        var comp = new int[GW * GH];
        int compN = 0;
        var stack = new Stack<int>();
        for (int s0 = 0; s0 < GW * GH; s0++)
        {
            if (gh[s0] <= 0.3f || comp[s0] != 0) continue;
            compN++;
            long sxi = 0, syi = 0; int n = 0;
            var cells = new List<int>();
            stack.Push(s0); comp[s0] = compN;
            while (stack.Count > 0)
            {
                int k = stack.Pop(); cells.Add(k);
                int x = k % GW, y = k / GW; sxi += x; syi += y; n++;
                int[] nb = { x > 0 ? k - 1 : -1, x < GW - 1 ? k + 1 : -1, y > 0 ? k - GW : -1, y < GH - 1 ? k + GW : -1 };
                foreach (int j in nb) if (j >= 0 && gh[j] > 0.3f && comp[j] == 0) { comp[j] = compN; stack.Push(j); }
            }
            if (n < 120) continue;   // 小さな岩は名前なし
            float cx = (float)sxi / n, cy = (float)syi / n;
            int best = cells[0]; float bd = float.MaxValue;
            foreach (int k in cells) { float dx = k % GW - cx, dy = k / GW - cy; float d = dx * dx + dy * dy; if (d < bd) { bd = d; best = k; } }
            Vector3 o = new Vector3(xmin + (best % GW + 0.5f) * cell, seaY + 1500f, zmin + (best / GW + 0.5f) * cell);
            RaycastHit hit;
            string name = "";
            if (Physics.Raycast(o, Vector3.down, out hit, 1700f, ~((1 << 4) | (1 << 9) | (1 << 10) | (1 << 18)), QueryTriggerInteraction.Ignore)) name = IslandName(hit.collider.transform);
            if (name == "") continue;
            Vector3 w = new Vector3(xmin + cx * cell, 0f, zmin + cy * cell);
            if (seen.ContainsKey(name)) { int c0 = count[name]; seen[name] = (seen[name] * c0 + w * n) / (c0 + n); count[name] = c0 + n; }
            else { seen[name] = w * n; count[name] = n; seen[name] = w; }
        }
        Vector3 nW = mapT.TransformDirection(nrm).normalized;
        foreach (var kv in seen)
        {
            Vector3 w = kv.Value;
            float u = Mathf.Clamp01((w.x - xmin) / sx), v = Mathf.Clamp01((w.z - zmin) / sz);
            Vector3 pos = mapT.TransformPoint(p00 + du * u + dv * v) + nW * 0.12f;
            var lg = new GameObject("Label_" + kv.Key);
            lg.transform.SetParent(root.transform, false);
            lg.transform.position = pos;
            var t = lg.AddComponent<TextMeshPro>();
            t.text = kv.Key;
            t.fontSize = 0.45f;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.color = new Color(1f, 0.95f, 0.75f);
            t.outlineWidth = 0.25f; t.outlineColor = new Color32(30, 20, 10, 255);
            t.rectTransform.sizeDelta = new Vector2(0.8f, 0.12f);
            labels.Add(lg.transform);
            // 説明の画像があれば、押せるようにする
            string key = kv.Key.Replace(" ", "_");
            string ipng = "Assets/My script/bridge/IslandInfo/info_" + key + ".png";
            Material im = null; Vector2 isz = Vector2.zero;
            if (File.Exists(ipng))
            {
                AssetDatabase.ImportAsset(ipng);
                var itex = AssetDatabase.LoadAssetAtPath<Texture2D>(ipng);
                string imp2 = "Assets/My script/bridge/Materials/IslandInfo_" + key + ".mat";
                im = AssetDatabase.LoadAssetAtPath<Material>(imp2);
                if (im == null) { im = new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(im, imp2); }
                im.mainTexture = itex; EditorUtility.SetDirty(im);
                isz = new Vector2(0.9f, 0.9f * itex.height / itex.width);
                var bc = lg.AddComponent<BoxCollider>();
                bc.size = new Vector3(0.8f, 0.14f, 0.05f);
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(InfoAsset) != null)
                {
                    var ii = UdonSharpEditor.UdonSharpUndo.AddComponent<IslandInfo>(lg);
                    ii.live = live; ii.index = labels.Count - 1;
                    UdonSharpEditorUtility.CopyProxyToUdon(ii);
                }
                t.text = kv.Key + " <size=60%><color=#9fe8ff>(?)</color></size>";
            }
            infoMats.Add(im); infoSizes.Add(isz);
            log.AppendLine("島の名前: " + kv.Key + "（" + w.ToString("F0") + "）");
        }
        live.labels = labels.ToArray();
        live.infoMats = infoMats.ToArray(); live.infoSizes = infoSizes.ToArray();
        // 説明の板（地図の真ん中の上 0.75m に浮かぶ。最初は隠す）
        var ip = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(ip.GetComponent<Collider>());
        ip.name = "IslandInfoPanel";
        ip.transform.SetParent(root.transform, false);
        ip.transform.position = mapT.position + nW * 0.75f;
        ip.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ip.SetActive(false);
        live.infoPanel = ip.transform; live.infoR = ip.GetComponent<MeshRenderer>();
        // 台の横の札「島の名前にさわると、くわしい説明が見られます」（手前と奥の 2 枚）
        string spng = "Assets/My script/bridge/IslandInfo/chart_sign.png";
        AssetDatabase.ImportAsset(spng);
        var stex = AssetDatabase.LoadAssetAtPath<Texture2D>(spng);
        Transform tbl = table.Find("Table");
        if (stex != null && tbl != null)
        {
            string smp = "Assets/My script/bridge/Materials/ChartSign.mat";
            var sm = AssetDatabase.LoadAssetAtPath<Material>(smp);
            if (sm == null) { sm = new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(sm, smp); }
            sm.mainTexture = stex; EditorUtility.SetDirty(sm);
            Bounds tb = tbl.GetComponent<Renderer>().bounds;
            float sw = 1.1f, shh = sw * stex.height / stex.width;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 outW = table.TransformDirection(new Vector3(0f, 0f, side)).normalized;
                float half = Mathf.Abs(Vector3.Dot(tb.extents, new Vector3(Mathf.Abs(outW.x), Mathf.Abs(outW.y), Mathf.Abs(outW.z))));
                var sq = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(sq.GetComponent<Collider>());
                sq.name = side < 0 ? "SignFront" : "SignBack";
                sq.transform.SetParent(root.transform, false);
                sq.transform.position = new Vector3(tb.center.x, tb.min.y + tb.size.y * 0.72f, tb.center.z) + outW * (half + 0.004f);
                sq.transform.rotation = Quaternion.LookRotation(-outW, Vector3.up);
                sq.transform.localScale = new Vector3(sw, shh, 1f);
                sq.GetComponent<MeshRenderer>().sharedMaterial = sm;
            }
            log.AppendLine("台の横に「島の名前にさわると、くわしい説明が見られます」の札を 2 枚");
        }
        UdonSharpEditorUtility.CopyProxyToUdon(live);
        log.AppendLine("味方の戦闘機の印: " + planes.Count + " 機（地図の上で 0.1 秒ごとに動く）");
    }

    // 島の名前（英語。日本語の部分は TMP の字が無いので外す。Backup_flight/island_names.txt で「元の名前=表示する名前」に変えられる）
    static Dictionary<string, string> nameMap;
    static string IslandName(Transform t)
    {
        if (nameMap == null)
        {
            nameMap = new Dictionary<string, string>();
            string f = "Backup_flight/island_names.txt";
            if (File.Exists(f)) foreach (var line in File.ReadAllLines(f)) { int k = line.IndexOf('='); if (k > 0) nameMap[line.Substring(0, k).Trim()] = line.Substring(k + 1).Trim(); }
        }
        // 一番上（シーン直下）から順に、名前になりそうな物を探す
        var chain = new List<Transform>();
        for (var x = t; x != null; x = x.parent) chain.Insert(0, x);
        foreach (var x in chain)
        {
            if (nameMap.ContainsKey(x.name)) return nameMap[x.name];
        }
        Transform top = chain[0];
        string raw = top.name;
        if (raw.Contains("博物館") || raw.Contains("アイギス")) return "Aigis island";
        var sb = new StringBuilder();
        foreach (char ch in raw) { if (ch == '(' || ch == '（') break; if (ch < 128) sb.Append(ch); }
        string s = sb.ToString().Trim();
        if (s.Length < 2) return "";
        if (s == "Terrain" || s.StartsWith("Terrain ") || s == "Island" || s == "GameObject") return "";   // 名前の無い島（island_names.txt で付けられる）
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(\d+\)$", "");
        return char.ToUpper(s[0]) + s.Substring(1);
    }

    static Material ArrowMat(string name, Color32 col)
    {
        string dir = "Assets/My script/bridge";
        string png = dir + "/" + name + ".png";
        int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        var tri = new[] { new Vector2(32, 62), new Vector2(6, 4), new Vector2(32, 16), new Vector2(58, 4) };   // 上向きの矢印
        var outl = new[] { new Vector2(32, 64), new Vector2(2, 0), new Vector2(32, 12), new Vector2(62, 0) };
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            var q = new Vector2(x + 0.5f, y + 0.5f);
            px[y * N + x] = Inside(tri, q) ? col : (Inside(outl, q) ? new Color32(10, 20, 35, 255) : new Color32(0, 0, 0, 0));
        }
        tex.SetPixels32(px);
        File.WriteAllBytes(png, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(png);
        var imp = AssetImporter.GetAtPath(png) as TextureImporter;
        if (imp != null) { imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
        string mp = dir + "/Materials/" + name + ".mat";
        Directory.CreateDirectory(dir + "/Materials");
        var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (m == null) { m = new Material(Shader.Find("Unlit/Transparent")); AssetDatabase.CreateAsset(m, mp); }
        m.shader = Shader.Find("Unlit/Transparent");
        m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(png);
        m.renderQueue = 3100;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---- 本物の海: 島のまわりの浅瀬の地図（海のシェーダーに渡す）----
    const string ShorePng = "Assets/My script/drone/FX/shore_map.png";
    static void BakeShore(int[] dist, int GW, int GH, float xmin, float zmin, float sx, float sz, StringBuilder log)
    {
        float maxCells = 18f * 3f;   // 約 300m（距離は 1/3 マス単位）
        var tex = new Texture2D(GW, GH, TextureFormat.RGBA32, false, true);
        var px = new Color32[GW * GH];
        for (int k = 0; k < GW * GH; k++)
        {
            float t = Mathf.Clamp01(1f - dist[k] / maxCells);
            t = t * t * (3f - 2f * t);
            byte b = (byte)Mathf.RoundToInt(t * 255f);
            px[k] = new Color32(b, b, b, 255);
        }
        tex.SetPixels32(px);
        File.WriteAllBytes(ShorePng, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(ShorePng);
        var imp = AssetImporter.GetAtPath(ShorePng) as TextureImporter;
        if (imp != null) { imp.sRGBTexture = false; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.maxTextureSize = 1024; imp.SaveAndReimport(); }
        var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/My script/drone/FX/Ocean.mat");
        if (m == null) { log.AppendLine("海: Ocean.mat が見つからない"); return; }
        m.SetTexture("_ShoreTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ShorePng));
        m.SetVector("_ShoreRect", new Vector4(xmin, zmin, 1f / sx, 1f / sz));
        EditorUtility.SetDirty(m);
        log.AppendLine("海: 島のまわり約 300m を浅瀬の水色に");
    }

    static Vector3 ShipPos(WarShip ws) { return ws.model != null ? ws.model.transform.position : ws.transform.position; }

    // ---- 描く道具 ----
    static void Set(Color32[] px, int x, int y, Color32 c) { if (x < 0 || y < 0 || x >= W || y >= H) return; px[y * W + x] = c; }
    static void Blend(Color32[] px, int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        int i = y * W + x; float a = c.a / 255f;
        px[i] = Color32.Lerp(px[i], new Color32(c.r, c.g, c.b, 255), a);
    }
    static void Poly(Color32[] px, Vector2[] pts, Color32 c)
    {
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
        for (int y = Mathf.FloorToInt(y0); y <= Mathf.CeilToInt(y1); y++)
        for (int x = Mathf.FloorToInt(x0); x <= Mathf.CeilToInt(x1); x++)
            if (Inside(pts, new Vector2(x + 0.5f, y + 0.5f))) Set(px, x, y, c);
    }
    static bool Inside(Vector2[] p, Vector2 q)
    {
        bool r = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            if ((p[i].y > q.y) != (p[j].y > q.y) && q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x) r = !r;
        return r;
    }
    static Vector2[] Shape(Vector2 c, Vector2 f, Vector2[] local)
    {
        Vector2 s = new Vector2(f.y, -f.x);   // 右
        var o = new Vector2[local.Length];
        for (int i = 0; i < local.Length; i++) o[i] = c + f * local[i].x + s * local[i].y;
        return o;
    }
    static Vector2[] Hull(float L, float w)
    {
        return new[] { new Vector2(L * 0.5f, 0f), new Vector2(L * 0.22f, w * 0.5f), new Vector2(-L * 0.42f, w * 0.48f), new Vector2(-L * 0.5f, w * 0.3f),
                       new Vector2(-L * 0.5f, -w * 0.3f), new Vector2(-L * 0.42f, -w * 0.48f), new Vector2(L * 0.22f, -w * 0.5f) };
    }
    // 軍艦・貨物船: ころんとした船体 + 白いふち + 艦橋
    static void Ship(Color32[] px, Vector2 c, Vector2 f, float L, float w, Color32 col)
    {
        Poly(px, Shape(c, f, Hull(L + 8f, w + 8f)), new Color32(255, 255, 255, 255));
        Poly(px, Shape(c, f, Hull(L, w)), col);
        Poly(px, Shape(c, f, new[] { new Vector2(-L * 0.05f, w * 0.22f), new Vector2(-L * 0.05f, -w * 0.22f), new Vector2(-L * 0.22f, -w * 0.22f), new Vector2(-L * 0.22f, w * 0.22f) }), new Color32(70, 20, 20, 255));
    }
    // 空母: 大きな平たい甲板 + 白い線
    static void Carrier(Color32[] px, Vector2 c, Vector2 f)
    {
        float L = 70f, w = 24f;
        var deck = new[] { new Vector2(L * 0.5f, w * 0.3f), new Vector2(L * 0.42f, w * 0.5f), new Vector2(-L * 0.5f, w * 0.5f), new Vector2(-L * 0.5f, -w * 0.5f), new Vector2(L * 0.42f, -w * 0.5f), new Vector2(L * 0.5f, -w * 0.3f) };
        var deckO = new Vector2[deck.Length];
        for (int i = 0; i < deck.Length; i++) deckO[i] = deck[i] * 1.0f + deck[i].normalized * 5f;
        Poly(px, Shape(c, f, deckO), new Color32(255, 255, 255, 255));
        Poly(px, Shape(c, f, deck), new Color32(185, 30, 40, 255));
        Poly(px, Shape(c, f, new[] { new Vector2(L * 0.4f, 1.5f), new Vector2(L * 0.4f, -1.5f), new Vector2(-L * 0.45f, -1.5f), new Vector2(-L * 0.45f, 1.5f) }), new Color32(255, 235, 200, 255));
        Poly(px, Shape(c, f, new[] { new Vector2(L * 0.1f, -w * 0.5f), new Vector2(L * 0.1f, -w * 0.28f), new Vector2(-L * 0.1f, -w * 0.28f), new Vector2(-L * 0.1f, -w * 0.5f) }), new Color32(60, 15, 20, 255));
    }
    static void Star(Color32[] px, Vector2 c, float r, Color32 col)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
        }
        Poly(px, pts, col);
    }
}
