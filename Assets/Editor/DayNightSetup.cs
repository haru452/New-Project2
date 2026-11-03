using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using TMPro;

// =============================================================
//  昼と夜の切り替えを作る（Backup_flight/run_daynight.flag があると、Unity に戻った時に 1 回だけ動く）
//  ・メインルームのスイッチ（DayNight）
//  ・夜空のスカイボックス（星と月）
//  ・夜だけ点く物: 基地と町の窓・人が住む島の古い船の明かり・滑走路の灯り・飛行機の赤と緑のランプ
//  ・戦艦のサーチライト（夜に撃つ時だけ点く）
//  作り直すと、前に作った夜の物は消してから作る
// =============================================================
[InitializeOnLoad]
public static class DayNightSetup
{
    const string Flag = "Backup_flight/run_daynight.flag";
    const string Report = "Backup_flight/daynight_report.txt";
    const string Dir = "Assets/My script/drone";
    const string FxDir = Dir + "/FX";
    const string Cs = Dir + "/DayNight.cs";
    const string Asset = Dir + "/DayNight.asset";

    static DayNightSetup() { EditorApplication.delayCall += () => { if (File.Exists(Flag) && !EditorApplication.isPlayingOrWillChangePlaymode) { File.Delete(Flag); tries = 0; TryRun(); } }; }
    static int tries; static double retryAt;
    static void TryRun()
    {
        try { Run(); }
        catch (System.Exception e)
        {
            if (e.Message.Contains("outdated script version") && tries < 6)
            {
                tries++; retryAt = EditorApplication.timeSinceStartup + 5.0; EditorApplication.update += Wait;
                File.WriteAllText(Report, "コンパイル待ち… " + tries + " 回目のやり直しを 5 秒後に"); return;
            }
            File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e);
        }
    }
    static void Wait()
    {
        if (EditorApplication.timeSinceStartup < retryAt || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= Wait; TryRun();
    }

    static Material UnlitColor(string name, Color c)
    {
        string p = FxDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Unlit/Color")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("Unlit/Color"); m.color = c; EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Ball(string n, Transform parent, Vector3 worldPos, float worldSize, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.name = n;
        g.transform.SetParent(parent, true);
        g.transform.position = worldPos;
        Vector3 ls = parent.lossyScale;
        g.transform.localScale = new Vector3(worldSize / Mathf.Max(Mathf.Abs(ls.x), 1e-4f), worldSize / Mathf.Max(Mathf.Abs(ls.y), 1e-4f), worldSize / Mathf.Max(Mathf.Abs(ls.z), 1e-4f));
        var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return g;
    }

    static GameObject Panel(string n, Transform parent, Vector3 worldPos, Vector3 outward, Vector2 size, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.name = n;
        g.transform.SetParent(parent, true);
        g.transform.position = worldPos;
        g.transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);
        Vector3 ls = parent.lossyScale;
        float s = Mathf.Max((Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f, 1e-4f);
        g.transform.localScale = new Vector3(size.x / s, size.y / s, 1f);
        var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return g;
    }

    static float GroundY(Vector3 p, float fallback)
    {
        RaycastHit h;
        if (Physics.Raycast(new Vector3(p.x, p.y + 400f, p.z), Vector3.down, out h, 900f, ~((1 << 4) | (1 << 9) | (1 << 10) | (1 << 18)), QueryTriggerInteraction.Ignore)) return h.point.y;
        return fallback;
    }

    public static void Run()
    {
        var log = new StringBuilder();
        // ---- プログラム ----
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Asset) == null)
        {
            var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(Cs);
            if (ms == null) { File.WriteAllText(Report, "ERROR: " + Cs + " が無い"); return; }
            var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            prog.sourceCsScript = ms;
            AssetDatabase.CreateAsset(prog, Asset);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        }
        Physics.SyncTransforms();
        var nightObjs = new List<GameObject>();
        Material warm = UnlitColor("NightWindow", new Color(1f, 0.78f, 0.42f));
        Material warm2 = UnlitColor("NightWindowDim", new Color(0.85f, 0.6f, 0.32f));
        Material red = UnlitColor("NavRed", new Color(1f, 0.08f, 0.05f));
        Material green = UnlitColor("NavGreen", new Color(0.1f, 1f, 0.25f));
        Material rwWhite = UnlitColor("RunwayWhite", new Color(1f, 0.95f, 0.8f));
        Material rwGreen = UnlitColor("RunwayGreen", new Color(0.2f, 1f, 0.35f));
        Material rwRed = UnlitColor("RunwayRed", new Color(1f, 0.15f, 0.1f));

        // ---- 前に作った物を消す ----
        int removed = 0;
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t == null) continue;
            if (t.name == "NightGlow" || t.name == "NavLights" || t.name == "Searchlight" || t.name == "NightLights" || t.name == "DayNightSwitch") { Undo.DestroyObjectImmediate(t.gameObject); removed++; }
        }

        // ---- 基地と町の窓 ----
        var buildings = new List<Transform>();
        foreach (var isl in new[] { "人がいる島", "Astral island(アストラル）" })
        {
            GameObject root = GameObject.Find(isl);
            if (root == null) { log.AppendLine("夜: " + isl + " が見つからない"); continue; }
            foreach (Transform ch in root.transform)
            {
                string nm = ch.name;
                bool house = nm.StartsWith("BigHousePrefab") || nm.StartsWith("OldHousePrefab") || (nm.StartsWith("House") && nm.Length > 5 && char.IsDigit(nm[5]));
                if (house) buildings.Add(ch);
            }
        }
        GameObject baseGo = GameObject.Find("military base");
        if (baseGo != null)
        {
            Vector3 bp = baseGo.transform.position;
            foreach (var t in baseGo.GetComponentsInChildren<Transform>(true))
                if (t.name == "House_2") buildings.Add(t);
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (!t.name.StartsWith("Old Hangar") || (t.parent != null && t.parent.name.StartsWith("Old Hangar"))) continue;
                Vector3 d = t.position - bp; d.y = 0f;
                if (d.magnitude < 1500f) buildings.Add(t);
            }
        }
        var rnd = new System.Random(1234);
        int win = 0;
        foreach (var b in buildings)
        {
            // 一番大きな形（MeshFilter）の、その形の中での箱
            MeshFilter best = null; float bv = 0f;
            foreach (var mf in b.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Vector3 s = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale);
                float v = Mathf.Abs(s.x * s.y * s.z);
                if (v > bv) { bv = v; best = mf; }
            }
            if (best == null) continue;
            var glow = new GameObject("NightGlow");
            Undo.RegisterCreatedObjectUndo(glow, "Day night");
            glow.transform.SetParent(b, false);
            Transform T = best.transform;
            Bounds lb = best.sharedMesh.bounds;
            Vector3[] faces = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            foreach (var f in faces)
            {
                Vector3 fw = T.TransformDirection(f);
                if (Mathf.Abs(fw.y) > 0.5f) continue;   // 横向きの面だけ
                fw.y = 0f; fw.Normalize();
                Vector3 axisU = f.x != 0f ? Vector3.forward : Vector3.right;   // 面の横方向（形の中）
                float halfU = Mathf.Abs(Vector3.Dot(lb.extents, axisU));
                float halfN = Mathf.Abs(Vector3.Dot(lb.extents, new Vector3(Mathf.Abs(f.x), 0f, Mathf.Abs(f.z))));
                Vector3 wU = T.TransformVector(axisU * halfU * 2f);
                float widthW = wU.magnitude;
                float heightW = Mathf.Abs(T.TransformVector(Vector3.up * lb.size.y).y);
                if (widthW < 3f || heightW < 2.5f) continue;
                int cols = Mathf.Clamp(Mathf.RoundToInt(widthW / 6f), 1, 6);
                int rows = Mathf.Clamp(Mathf.RoundToInt(heightW / 5f), 1, 4);
                for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (rnd.NextDouble() < 0.35) continue;   // 消えている窓もある
                    Vector3 lp = lb.center + f * halfN + axisU * (-halfU + halfU * 2f * (c + 0.5f) / cols)
                               + Vector3.up * (-lb.extents.y + lb.size.y * (r + 0.55f) / (rows + 0.3f));
                    Vector3 wp = T.TransformPoint(lp) + fw * 0.15f;
                    Panel("Win", glow.transform, wp, fw, new Vector2(Mathf.Min(1.6f, widthW / cols * 0.5f), Mathf.Min(1.2f, heightW / rows * 0.4f)), rnd.NextDouble() < 0.3 ? warm2 : warm);
                    win++;
                }
            }
            glow.SetActive(false);
            nightObjs.Add(glow);
        }
        log.AppendLine("夜: 建物 " + buildings.Count + " 棟に窓の明かり " + win + " 個");

        // ---- 人が住む島の古い船 ----
        int ships = 0;
        GameObject town = GameObject.Find("人がいる島");
        if (town != null)
        {
            foreach (Transform ch in town.transform)
            {
                string n = ch.name.ToLower();
                if (!(n.Contains("ship") || n.Contains("boat") || ch.name.Contains("船"))) continue;
                Bounds wb; bool has = false; wb = new Bounds();
                foreach (var r in ch.GetComponentsInChildren<Renderer>(true)) { if (!has) { wb = r.bounds; has = true; } else wb.Encapsulate(r.bounds); }
                if (!has) continue;
                var glow = new GameObject("NightGlow");
                Undo.RegisterCreatedObjectUndo(glow, "Day night");
                glow.transform.SetParent(ch, false);
                bool alongX = wb.size.x > wb.size.z;
                float L = alongX ? wb.size.x : wb.size.z;
                for (int k = 0; k < 6; k++)
                {
                    float t = -0.4f + 0.8f * k / 5f;
                    Vector3 p = wb.center + (alongX ? Vector3.right : Vector3.forward) * (L * t);
                    p.y = wb.max.y - wb.size.y * 0.15f;
                    Ball("Lamp", glow.transform, p, 0.8f, warm);
                }
                glow.SetActive(false);
                nightObjs.Add(glow);
                ships++;
                log.AppendLine("夜: 古い船 " + ch.name + " に明かり");
            }
        }
        if (ships == 0) log.AppendLine("夜: 人が住む島の古い船が見つからない（名前に ship / boat / 船 が入っている物を探しました）");

        // ---- 滑走路の灯り（基地とアストラル空港）----
        var nl = new GameObject("NightLights");
        Undo.RegisterCreatedObjectUndo(nl, "Day night");
        var runways = new List<KeyValuePair<Vector3, Vector3>>();
        if (baseGo != null)
        {
            Transform af = baseGo.transform.Find("BaseAirfield");
            if (af != null) { Vector3 d = af.forward; d.y = 0f; runways.Add(new KeyValuePair<Vector3, Vector3>(af.position - d.normalized * 40f, d.normalized)); }
        }
        var gm = Object.FindObjectOfType<GameMode>(true);
        if (gm != null && gm.astralAirport != null) runways.Add(new KeyValuePair<Vector3, Vector3>(gm.astralAirport.position, Vector3.left));   // アストラル: マイナス X の向きへ
        int rl = 0;
        foreach (var rw in runways)
        {
            Vector3 s0 = rw.Key, d = rw.Value, side = Vector3.Cross(Vector3.up, d);
            for (int k = 0; k <= 13; k++)
            {
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 p = s0 + d * (25f * k) + side * (20f * sd);
                    p.y = GroundY(p, s0.y) + 0.4f;
                    Ball("RunwayLight", nl.transform, p, 0.7f, k == 0 ? rwGreen : (k == 13 ? rwRed : rwWhite));
                    rl++;
                }
            }
            for (int c = -2; c <= 2; c++)   // 入口の緑の列
            {
                Vector3 p = s0 + side * (8f * c); p.y = GroundY(p, s0.y) + 0.4f;
                Ball("RunwayThreshold", nl.transform, p, 0.7f, rwGreen); rl++;
            }
        }
        nl.SetActive(false);
        nightObjs.Add(nl);
        log.AppendLine("夜: 滑走路 " + runways.Count + " 本に灯り " + rl + " 個");

        // ---- 飛行機の赤と緑のランプ（左の翼 = 赤・右の翼 = 緑）----
        int nav = 0;
        var planes = new List<KeyValuePair<Transform, Vector3>>();   // 機体と、右の向き（ワールド）
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true))
        {
            if (ed.rb == null) continue;
            Transform t = ed.rb.transform;
            Vector3 right = ed.noseIsZ ? t.right : t.forward;   // 機首が -X の機体は、右が +Z
            planes.Add(new KeyValuePair<Transform, Vector3>(t, right));
        }
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || c.radarTracks == null || c.radarTracks.Length == 0) continue;
            Transform t = c.car.transform;
            Transform axis = t.Find("FlightAxis");
            planes.Add(new KeyValuePair<Transform, Vector3>(t, axis != null ? axis.right : t.right));
        }
        foreach (var kv in planes)
        {
            Transform t = kv.Key;
            Bounds wb = new Bounds(); bool has = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (r.name.Contains("Tracer") || r.name.Contains("Missile") || r.name.Contains("Flare")) continue;
                if (!has) { wb = r.bounds; has = true; } else wb.Encapsulate(r.bounds);
            }
            if (!has) continue;
            Vector3 right = kv.Value.normalized;
            float half = Mathf.Abs(Vector3.Dot(wb.extents, new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z))));
            if (half < 1f) continue;
            var g = new GameObject("NavLights");
            Undo.RegisterCreatedObjectUndo(g, "Day night");
            g.transform.SetParent(t, false);
            float sz = Mathf.Clamp(half * 0.06f, 0.5f, 2.5f);
            Ball("NavLeftRed", g.transform, wb.center - right * (half * 0.95f), sz, red);
            Ball("NavRightGreen", g.transform, wb.center + right * (half * 0.95f), sz, green);
            g.SetActive(false);
            nightObjs.Add(g);
            nav++;
        }
        log.AppendLine("夜: 飛行機 " + nav + " 機に赤と緑のランプ");

        // ---- 夜空 ----
        Material nightSky = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/NightSky.mat");
        Shader nsh = Shader.Find("FlightUI/NightSky");
        if (nightSky == null && nsh != null) { nightSky = new Material(nsh); AssetDatabase.CreateAsset(nightSky, FxDir + "/NightSky.mat"); }
        Material daySky = RenderSettings.skybox;
        if (daySky == nightSky) daySky = null;
        Light sun = RenderSettings.sun;
        if (sun == null) foreach (var l in Object.FindObjectsOfType<Light>(true)) if (l.type == LightType.Directional) { sun = l; break; }

        // ---- スイッチ（モード切り替えボタンの左）----
        GameObject rankings = GameObject.Find("Rankings");
        Transform modeBtn = rankings != null ? rankings.transform.Find("ModeButton") : null;
        var sw = new GameObject("DayNightSwitch");
        Undo.RegisterCreatedObjectUndo(sw, "Day night");
        // 掲示板（Rankings）は FlightSetup が作り直すので、その外（同じ部屋）に置く
        if (rankings != null && rankings.transform.parent != null) sw.transform.SetParent(rankings.transform.parent, false);
        if (modeBtn != null) { sw.transform.position = modeBtn.position - rankings.transform.right * 1.4f; sw.transform.rotation = rankings.transform.rotation; }
        else if (rankings != null) { sw.transform.position = rankings.transform.position - rankings.transform.right * 6f; sw.transform.rotation = rankings.transform.rotation; }
        var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(post.GetComponent<Collider>());
        post.name = "Post"; post.transform.SetParent(sw.transform, false);
        post.transform.localPosition = new Vector3(0f, 0.5f, 0.05f); post.transform.localScale = new Vector3(0.12f, 1f, 0.12f);
        post.GetComponent<MeshRenderer>().sharedMaterial = UnlitColor("DayNightPost", new Color(0.18f, 0.2f, 0.24f));
        var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
        btn.name = "Button"; btn.transform.SetParent(sw.transform, false);
        btn.transform.localPosition = new Vector3(0f, 1.15f, 0f); btn.transform.localScale = new Vector3(0.55f, 0.3f, 0.15f);
        Material dayM = UnlitColor("DayNightDay", new Color(1f, 0.78f, 0.25f));
        Material nightM = UnlitColor("DayNightNight", new Color(0.18f, 0.25f, 0.6f));
        btn.GetComponent<MeshRenderer>().sharedMaterial = dayM;
        var lab = new GameObject("Label"); lab.transform.SetParent(sw.transform, false);
        lab.transform.localPosition = new Vector3(0f, 1.48f, -0.02f);
        var lt = lab.AddComponent<TextMeshPro>();
        lt.fontSize = 0.9f; lt.alignment = TextAlignmentOptions.Center; lt.enableWordWrapping = false; lt.richText = true;
        lt.rectTransform.sizeDelta = new Vector2(1.2f, 0.3f);
        lt.text = "TIME: DAY";

        var dn = UdonSharpEditor.UdonSharpUndo.AddComponent<DayNight>(btn);
        dn.daySkybox = daySky; dn.nightSkybox = nightSky; dn.sun = sun;
        if (sun != null) { dn.daySunIntensity = sun.intensity; dn.daySunColor = sun.color; }
        dn.dayAmbient = RenderSettings.ambientIntensity;
        dn.dayFog = RenderSettings.fogColor;
        dn.nightObjects = nightObjs.ToArray();
        dn.label = lt; dn.switchRenderer = btn.GetComponent<MeshRenderer>(); dn.dayMat = dayM; dn.nightMat = nightM;
        UdonSharpEditorUtility.CopyProxyToUdon(dn);
        log.AppendLine("スイッチ: " + (rankings != null ? "掲示板の並び（モード切り替えボタンの左）" : "Rankings が見つからないので原点の近く") + "に置きました");
        log.AppendLine("空: 昼 = " + (daySky != null ? daySky.name : "（なし: インスペクターで入れてください）") + "・夜 = " + (nightSky != null ? nightSky.name : "なし"));
        log.AppendLine("太陽: " + (sun != null ? sun.name : "見つからない"));

        // ---- 戦艦のサーチライト ----
        Mesh beam = BeamMesh();
        Material beamM = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/SearchBeam.mat");
        if (beamM == null) { beamM = new Material(Shader.Find("Legacy Shaders/Particles/Additive")); AssetDatabase.CreateAsset(beamM, FxDir + "/SearchBeam.mat"); }
        beamM.SetColor("_TintColor", new Color(0.55f, 0.55f, 0.5f, 0.5f)); EditorUtility.SetDirty(beamM);
        Material lampM = UnlitColor("SearchLamp", new Color(1f, 0.97f, 0.85f));
        int sl = 0;
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
        {
            GameObject mdl = ws.model != null ? ws.model : ws.gameObject;
            Bounds wb = new Bounds(); bool has = false;
            foreach (var r in mdl.GetComponentsInChildren<Renderer>(true)) { if (r is ParticleSystemRenderer) continue; if (!has) { wb = r.bounds; has = true; } else wb.Encapsulate(r.bounds); }
            if (!has) continue;
            var g = new GameObject("Searchlight");
            Undo.RegisterCreatedObjectUndo(g, "Day night");
            g.transform.SetParent(mdl.transform, true);
            g.transform.position = new Vector3(wb.center.x, wb.max.y - wb.size.y * 0.1f, wb.center.z);
            g.transform.rotation = Quaternion.identity;
            Vector3 ls = g.transform.lossyScale;
            var bg = new GameObject("Beam"); bg.transform.SetParent(g.transform, false);
            bg.transform.localScale = new Vector3(45f / Mathf.Max(ls.x, 1e-4f), 45f / Mathf.Max(ls.y, 1e-4f), 900f / Mathf.Max(ls.z, 1e-4f));
            bg.AddComponent<MeshFilter>().sharedMesh = beam;
            var br = bg.AddComponent<MeshRenderer>(); br.sharedMaterial = beamM; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; br.receiveShadows = false;
            Ball("Lamp", g.transform, g.transform.position, 2.5f, lampM);
            g.SetActive(false);
            Undo.RecordObject(ws, "Day night");
            ws.searchlight = g; ws.dayNight = dn;
            UdonSharpEditorUtility.CopyProxyToUdon(ws);
            sl++;
        }
        log.AppendLine("戦艦: " + sl + " 隻にサーチライト（夜に撃つ時だけ、相手を照らす）");

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        log.Insert(0, "前に作った夜の物を " + removed + " 個消してから作り直しました\n");
        File.WriteAllText(Report, log.ToString());
        Debug.Log("[DayNight]\n" + log);
    }

    // サーチライトの光の筋: 先が広がる円すい（+Z 向き・長さ 1・先の半径 1）。手前が明るく、先へ行くほど消える
    static Mesh BeamMesh()
    {
        string p = FxDir + "/SearchBeam.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
        if (m != null) return m;
        m = new Mesh();
        int seg = 20;
        var v = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v.Add(dir * 0.04f); col.Add(new Color(1f, 1f, 1f, 0.9f));
            v.Add(dir * 1f + Vector3.forward); col.Add(new Color(1f, 1f, 1f, 0f));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2;
            tri.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
            tri.AddRange(new[] { a, a + 3, a + 1, a, a + 2, a + 3 });   // 裏からも見える
        }
        m.SetVertices(v); m.SetColors(col); m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, p);
        return m;
    }
}
