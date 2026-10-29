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

// main room を潜水艦の艦橋（発令所）に作り直す
//  ・古い大きな部屋（壁・床・天井・古い説明）は消さずに非表示
//  ・出現地点を中心に 24m × 10m の、丸い船体の部屋を作る（古い潜水艦の配管・バルブ・計器 ＋ 後から付けた画面）
//  ・ランキング・音量・モード切替・鏡・博物館のドア・潜水艦へのハッチ・アバター台・ポータルを中に並べ直す
//  ・R35 の操作方法（PC・VR）の板を壁に貼る
// メニュー「Tools/main room を艦橋にする」。Backup_flight/run_bridge.flag があれば再読み込み時に一度だけ自動実行。
// 2 回目以降は Bridge だけ作り直す（手で動かしたランキング・ドアなどの位置はそのまま）。
[InitializeOnLoad]
public static partial class BridgeSetup
{
    const string FlagPath = "Backup_flight/run_bridge.flag";
    const string ReportPath = "Backup_flight/bridge_report.txt";
    const string Dir = "Assets/My script/bridge";
    static bool retried;

    static BridgeSetup()
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

    // 部屋の形（中心 = 出現地点。x = 長さ方向, z = 幅方向）
    const float L = 24f, HW = 5f;
    static readonly Vector2[] Profile = {
        new Vector2(-5f, 0f), new Vector2(-5f, 3.6f), new Vector2(-4f, 4.6f), new Vector2(-2f, 5.2f),
        new Vector2(2f, 5.2f), new Vector2(4f, 4.6f), new Vector2(5f, 3.6f), new Vector2(5f, 0f) };

    static StringBuilder log;
    static Transform B;   // 艦橋の根元

    [MenuItem("Tools/main room を艦橋にする")]
    public static void Run()
    {
        log = new StringBuilder();
        log.AppendLine("BridgeSetup " + System.DateTime.Now);
        try
        {
            if (EditorApplication.isPlaying) { log.AppendLine("ERROR: 再生中"); return; }
            if (!EnsureProg(Dir + "/SonarSweep.cs", Dir + "/SonarSweep.asset") && !retried)
            {
                retried = true;
                AssetDatabase.Refresh();
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
                File.WriteAllText(FlagPath, "again");
                EditorApplication.delayCall += TryAutoRun;
                log.AppendLine("SonarSweep のプログラムを作りました。もう一度実行します");
                return;
            }
            Scene sc = SceneManager.GetActiveScene();
            GameObject mainRoom = Root("mainRoom");
            if (mainRoom == null) { log.AppendLine("ERROR: mainRoom が無い"); return; }
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>(true);
            Transform sp = (desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null) ? desc.spawns[0] : desc.transform;
            Vector3 C = new Vector3(sp.position.x, sp.position.y, sp.position.z);

            Transform old = mainRoom.transform.Find("Bridge");
            bool first = old == null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var bgo = new GameObject("Bridge");
            Undo.RegisterCreatedObjectUndo(bgo, "Bridge");
            bgo.transform.SetParent(mainRoom.transform, true);
            bgo.transform.SetPositionAndRotation(C, Quaternion.identity);
            Vector3 ps = mainRoom.transform.lossyScale;
            bgo.transform.localScale = new Vector3(1f / ps.x, 1f / ps.y, 1f / ps.z);
            B = bgo.transform;

            HideOld(mainRoom);
            BuildHull();
            BuildPipes();
            BuildLights();
            BuildConsoles();
            BuildPeriscope();
            BuildChartTable();
            BuildControlBoards();
            Rearrange(mainRoom, first);

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

    static GameObject Root(string n)
    {
        foreach (var g in SceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == n) return g;
        return null;
    }

    static bool EnsureProg(string script, string asset)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(asset) != null) return true;
        if (AssetDatabase.LoadAssetAtPath<Object>(asset) != null) AssetDatabase.DeleteAsset(asset);
        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(script);
        if (ms == null) return true;
        var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        prog.sourceCsScript = ms;
        AssetDatabase.CreateAsset(prog, asset);
        AssetDatabase.SaveAssets();
        return false;
    }

    // ---------------- 古い部屋を非表示 ----------------
    static void HideOld(GameObject mainRoom)
    {
        var names = new List<string>();
        foreach (Transform ch in mainRoom.transform)
        {
            string n = ch.name;
            bool hide = n == "Cube" || n.StartsWith("Cube (") || n == "Quad (2)" || n.StartsWith("Text (TMP)")
                || n == "Cylinder" || n == "reset" || n == "Yumiri";
            if (hide && ch.gameObject.activeSelf) { Undo.RecordObject(ch.gameObject, "Bridge"); ch.gameObject.SetActive(false); names.Add("mainRoom/" + n); }
        }
        foreach (var n in new[] { "Floor", "Quad (2)", "gawlia" })
        {
            var g = Root(n);
            if (g != null && g.activeSelf) { Undo.RecordObject(g, "Bridge"); g.SetActive(false); names.Add(n); }
        }
        // 古い「潜水艦へ」の箱（新しいハッチに変える）
        var tr = Root("transport");
        if (tr != null)
        {
            var t = tr.transform.Find("main to submarine");
            if (t != null && t.gameObject.activeSelf) { Undo.RecordObject(t.gameObject, "Bridge"); t.gameObject.SetActive(false); names.Add("transport/main to submarine"); }
        }
        // 古い画面に映していたカメラ（画面が無くなるので止める。重さの節約）
        var cam = Root("Camera");
        if (cam != null)
        {
            var c = cam.GetComponent<Camera>();
            if (c != null && c.targetTexture != null && cam.activeSelf) { Undo.RecordObject(cam, "Bridge"); cam.SetActive(false); names.Add("Camera（古い画面用）"); }
        }
        log.AppendLine("非表示にした物: " + (names.Count > 0 ? string.Join(", ", names) : "（もう非表示）"));
    }

    // ---------------- 材質 ----------------
    static Material M(string name, Color col, float metal = 0.3f, float smooth = 0.35f, Color? emis = null, string tex = null, Vector2? tile = null)
    {
        Directory.CreateDirectory(Dir + "/Materials");
        string p = Dir + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("Standard");
        m.color = col;
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Glossiness", smooth);
        if (tex != null) { m.mainTexture = Tex(tex, 512); if (tile != null) m.mainTextureScale = tile.Value; }
        if (emis != null)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emis.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else m.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(m);
        return m;
    }
    static Material U(string name, string tex, bool transparent = false)
    {
        Directory.CreateDirectory(Dir + "/Materials");
        string p = Dir + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        var sh = Shader.Find(transparent ? "Unlit/Transparent" : "Unlit/Texture");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh;
        m.mainTexture = Tex(tex, 2048);
        EditorUtility.SetDirty(m);
        return m;
    }
    static Texture2D Tex(string path, int max)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { log.AppendLine("画像が無い: " + path); return null; }
        bool ch = false;
        if (ti.maxTextureSize != max) { ti.maxTextureSize = max; ch = true; }
        if (ti.anisoLevel != 4) { ti.anisoLevel = 4; ch = true; }
        if (path.EndsWith("sweep.png") && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ch = true; }
        if (ch) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------- 形を作る道具 ----------------
    static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 lp, Quaternion lr, Vector3 size, Material m, bool col)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        g.transform.localRotation = lr;
        g.transform.localScale = size;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (!col) Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }
    static GameObject Box(string n, Transform p, Vector3 lp, Vector3 s, Material m, bool col = false) { return Prim(PrimitiveType.Cube, n, p, lp, Quaternion.identity, s, m, col); }
    static GameObject BoxR(string n, Transform p, Vector3 lp, Quaternion r, Vector3 s, Material m, bool col = false) { return Prim(PrimitiveType.Cube, n, p, lp, r, s, m, col); }
    static GameObject Pipe(string n, Transform p, Vector3 a, Vector3 b, float r, Material m)
    {
        Vector3 d = b - a;
        return Prim(PrimitiveType.Cylinder, n, p, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized), new Vector3(r * 2f, d.magnitude * 0.5f, r * 2f), m, false);
    }
    static Transform Group(string n, Transform p = null)
    {
        var g = new GameObject(n);
        g.transform.SetParent(p != null ? p : B, false);
        return g.transform;
    }
    // 板（Quad。表を +z に向ける = forward の方から見える）
    static GameObject Face(string n, Transform p, Vector3 lp, Quaternion lr, Vector2 size, Material m)
    {
        return Prim(PrimitiveType.Quad, n, p, lp, lr * Quaternion.Euler(0f, 180f, 0f), new Vector3(size.x, size.y, 1f), m, false);
    }
    // バルブのハンドル（輪 + 十字）。forward の方を向く
    static void Wheel(string n, Transform p, Vector3 lp, Quaternion facing, float r, Material m)
    {
        var w = Group(n, p);
        w.localPosition = lp; w.localRotation = facing;
        int seg = 14;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg, a2 = (i + 1) * Mathf.PI * 2f / seg;
            Vector3 p0 = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f), p1 = new Vector3(Mathf.Cos(a2) * r, Mathf.Sin(a2) * r, 0f);
            Pipe("Rim", w, p0, p1, r * 0.07f, m);
        }
        Pipe("Spoke", w, new Vector3(-r, 0f, 0f), new Vector3(r, 0f, 0f), r * 0.045f, m);
        Pipe("Spoke", w, new Vector3(0f, -r, 0f), new Vector3(0f, r, 0f), r * 0.045f, m);
        Pipe("Hub", w, new Vector3(0f, 0f, -0.06f), new Vector3(0f, 0f, 0.04f), r * 0.16f, m);
    }
    // 丸い計器（真鍮の縁 + 文字盤）。forward の方を向く
    static void Gauge(string n, Transform p, Vector3 lp, Quaternion facing, float r, Material face, Material rim)
    {
        var g = Group(n, p);
        g.localPosition = lp; g.localRotation = facing;
        Prim(PrimitiveType.Cylinder, "Rim", g, new Vector3(0f, 0f, -0.02f), Quaternion.Euler(90f, 0f, 0f), new Vector3(r * 2.2f, 0.025f, r * 2.2f), rim, false);
        Face("Dial", g, new Vector3(0f, 0f, 0.008f), Quaternion.identity, new Vector2(r * 2f, r * 2f), face);
    }

    // ---------------- 船体（床・丸い壁・天井・肋材・前後の隔壁） ----------------
    static void BuildHull()
    {
        var hull = Group("Hull");
        Material paint = M("HullPaint", new Color(0.46f, 0.52f, 0.48f), 0.35f, 0.4f);
        Material rib = M("HullRib", new Color(0.33f, 0.38f, 0.35f), 0.4f, 0.35f);
        Material floor = M("Grating", new Color(0.75f, 0.75f, 0.72f), 0.6f, 0.45f, null, Dir + "/Tex/grating.png", new Vector2(24f, 10f));
        Material deck = M("DeckDark", new Color(0.12f, 0.13f, 0.13f), 0.3f, 0.3f);
        Box("Floor", hull, new Vector3(0f, -0.05f, 0f), new Vector3(L, 0.1f, HW * 2f), floor, true);
        Box("FloorTrim", hull, new Vector3(0f, -0.04f, 0f), new Vector3(L, 0.1f, HW * 2f + 0.2f), deck, false);
        Vector2 ctr = new Vector2(0f, 2.4f);
        for (int i = 0; i < Profile.Length - 1; i++)
        {
            Vector2 a = Profile[i], b = Profile[i + 1];
            Vector2 d = b - a; float len = d.magnitude;
            Vector2 mid = (a + b) * 0.5f;
            Vector2 n = new Vector2(-d.y, d.x).normalized;
            if (Vector2.Dot(n, ctr - mid) < 0f) n = -n;
            Quaternion r = Quaternion.LookRotation(new Vector3(0f, d.y, d.x).normalized, new Vector3(0f, n.y, n.x));
            Vector3 c = new Vector3(0f, mid.y, mid.x) - new Vector3(0f, n.y, n.x) * 0.08f;
            BoxR("Plate" + i, hull, c, r, new Vector3(L + 0.4f, 0.16f, len + 0.1f), paint, true);
            // 肋材（1.5m おき）
            for (float x = -L / 2f + 0.75f; x < L / 2f; x += 1.5f)
                BoxR("Rib", hull, new Vector3(x, 0f, 0f) + new Vector3(0f, mid.y, mid.x) + new Vector3(0f, n.y, n.x) * 0.06f, r, new Vector3(0.18f, 0.12f, len + 0.02f), rib, false);
        }
        // 前後の隔壁
        for (int s = -1; s <= 1; s += 2)
        {
            Box(s < 0 ? "BulkheadW" : "BulkheadE", hull, new Vector3(s * (L / 2f + 0.1f), 2.6f, 0f), new Vector3(0.2f, 5.4f, HW * 2f + 0.4f), paint, true);
            for (float z = -4f; z <= 4.01f; z += 2f)
                Box("Stiffener", hull, new Vector3(s * (L / 2f - 0.04f), 2.4f, z), new Vector3(0.1f, 4.8f, 0.14f), rib, false);
            Box("Stiffener", hull, new Vector3(s * (L / 2f - 0.04f), 3.0f, 0f), new Vector3(0.1f, 0.14f, HW * 2f), rib, false);
        }
        // 足元の溝（床の端）
        for (int s = -1; s <= 1; s += 2) Box("Kick", hull, new Vector3(0f, 0.12f, s * (HW - 0.1f)), new Vector3(L, 0.24f, 0.2f), rib, false);
    }

    // ---------------- 配管 ----------------
    static void BuildPipes()
    {
        var g = Group("Pipes");
        Material red = M("PipeRed", new Color(0.55f, 0.12f, 0.09f), 0.3f, 0.45f);
        Material yel = M("PipeYellow", new Color(0.75f, 0.6f, 0.15f), 0.3f, 0.45f);
        Material gry = M("PipeGrey", new Color(0.55f, 0.57f, 0.56f), 0.6f, 0.5f);
        Material grn = M("PipeGreen", new Color(0.2f, 0.42f, 0.28f), 0.3f, 0.45f);
        Material wht = M("PipeWhite", new Color(0.82f, 0.83f, 0.8f), 0.2f, 0.4f);
        Material brk = M("HullRib", new Color(0.33f, 0.38f, 0.35f), 0.4f, 0.35f);
        float x0 = -L / 2f + 0.1f, x1 = L / 2f - 0.1f;
        for (int s = -1; s <= 1; s += 2)
        {
            Pipe("PipeA", g, new Vector3(x0, 3.95f, s * 4.55f), new Vector3(x1, 3.95f, s * 4.55f), 0.09f, gry);
            Pipe("PipeB", g, new Vector3(x0, 4.25f, s * 4.25f), new Vector3(x1, 4.25f, s * 4.25f), 0.06f, s < 0 ? red : yel);
            Pipe("PipeC", g, new Vector3(x0, 4.5f, s * 3.98f), new Vector3(x1, 4.5f, s * 3.98f), 0.05f, grn);
            Pipe("PipeD", g, new Vector3(x0, 5.02f, s * 1.1f), new Vector3(x1, 5.02f, s * 1.1f), 0.1f, wht);
            Pipe("PipeE", g, new Vector3(x0, 5.05f, s * 1.45f), new Vector3(x1, 5.05f, s * 1.45f), 0.06f, s < 0 ? yel : red);
            for (float x = -L / 2f + 0.75f; x < L / 2f; x += 3f)
            {
                Box("Clamp", g, new Vector3(x, 3.95f, s * 4.55f), new Vector3(0.08f, 0.24f, 0.24f), brk);
                Box("Clamp", g, new Vector3(x, 5.03f, s * 1.27f), new Vector3(0.08f, 0.18f, 0.6f), brk);
            }
        }
        // 縦の配管とバルブ
        Material valve = M("ValveRed", new Color(0.62f, 0.08f, 0.06f), 0.4f, 0.5f);
        float[] vx = { -10.6f, -7.2f, 6.2f, 10.6f };
        foreach (float x in vx)
            for (int s = -1; s <= 1; s += 2)
            {
                if (s < 0 && x < -6f) continue;   // 南西は操作方法の板
                Pipe("Riser", g, new Vector3(x, 0f, s * 4.82f), new Vector3(x, 3.6f, s * 4.82f), 0.07f, gry);
                Wheel("Valve", g, new Vector3(x, 1.35f, s * 4.6f), Quaternion.LookRotation(new Vector3(0f, 0f, -s)), 0.18f, valve);
            }
    }

    // ---------------- 照明 ----------------
    static void BuildLights()
    {
        var g = Group("Lights");
        Material bulb = M("LampWarm", new Color(1f, 0.9f, 0.7f), 0f, 0.6f, new Color(1f, 0.78f, 0.5f) * 2.2f);
        Material cage = M("LampCage", new Color(0.12f, 0.12f, 0.12f), 0.6f, 0.4f);
        Material redL = M("LampRed", new Color(0.8f, 0.1f, 0.08f), 0f, 0.6f, new Color(1f, 0.1f, 0.06f) * 2.5f);
        for (float x = -9f; x <= 9.01f; x += 4.5f)
        {
            Pipe("Cage", g, new Vector3(x, 5.2f, 0f), new Vector3(x, 4.85f, 0f), 0.14f, cage);
            Prim(PrimitiveType.Sphere, "Bulb", g, new Vector3(x, 4.82f, 0f), Quaternion.identity, Vector3.one * 0.2f, bulb, false);
        }
        foreach (float x in new[] { -6f, 0f, 6f })
        {
            var lg = new GameObject("Light");
            lg.transform.SetParent(g, false);
            lg.transform.localPosition = new Vector3(x, 4.2f, 0f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.range = 11f; l.intensity = 1.5f; l.color = new Color(1f, 0.84f, 0.62f); l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
        }
        // ドアの上の赤いランプ
        foreach (float x in new[] { -L / 2f + 0.15f, L / 2f - 0.15f })
            Prim(PrimitiveType.Sphere, "DoorLamp", g, new Vector3(x, 3.1f, 0f), Quaternion.identity, Vector3.one * 0.16f, redL, false);
        var rl = new GameObject("RedLight");
        rl.transform.SetParent(g, false);
        rl.transform.localPosition = new Vector3(0f, 1.2f, 3.6f);
        var r = rl.AddComponent<Light>();
        r.type = LightType.Point; r.range = 5f; r.intensity = 0.8f; r.color = new Color(1f, 0.25f, 0.18f); r.shadows = LightShadows.None;
    }

    // ---------------- 北の壁: 古い操舵席・注排水盤・後から付けた画面 ----------------
    static void BuildConsoles()
    {
        var g = Group("Consoles");
        Material body = M("ConsoleOld", new Color(0.32f, 0.38f, 0.34f), 0.4f, 0.35f);
        Material dark = M("ConsoleDark", new Color(0.09f, 0.1f, 0.11f), 0.5f, 0.45f);
        Material brass = M("Brass", new Color(0.72f, 0.56f, 0.28f), 0.85f, 0.6f);
        Material wheel = M("WheelBlack", new Color(0.08f, 0.08f, 0.08f), 0.3f, 0.5f);
        string[] dials = { "dial_depth", "dial_press", "dial_speed", "dial_volt" };
        var dm = new Material[4];
        for (int i = 0; i < 4; i++) dm[i] = U("Dial_" + i, Dir + "/Tex/" + dials[i] + ".png");
        Quaternion south = Quaternion.LookRotation(Vector3.back);   // 部屋の中（南）を向く

        // A: 潜舵・横舵の操作席（古い）
        var a = Group("DiveStation", g);
        Box("Base", a, new Vector3(-5f, 0.5f, 4.55f), new Vector3(3.2f, 1.0f, 0.8f), body, true);
        BoxR("Panel", a, new Vector3(-5f, 1.55f, 4.82f), Quaternion.Euler(-12f, 0f, 0f), new Vector3(3.2f, 1.1f, 0.12f), body, true);
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 2; j++)
                Gauge("Gauge", a, new Vector3(-6.2f + i * 0.8f, 1.8f - j * 0.45f, 4.74f - j * 0.1f), south * Quaternion.Euler(12f, 0f, 0f), 0.16f, dm[(i + j) % 4], brass);
        Wheel("BowPlanes", a, new Vector3(-5.9f, 1.15f, 4.0f), south, 0.38f, wheel);
        Wheel("SternPlanes", a, new Vector3(-4.1f, 1.15f, 4.0f), south, 0.38f, wheel);
        Pipe("Shaft", a, new Vector3(-5.9f, 1.15f, 4.05f), new Vector3(-5.9f, 1.15f, 4.4f), 0.04f, brass);
        Pipe("Shaft", a, new Vector3(-4.1f, 1.15f, 4.05f), new Vector3(-4.1f, 1.15f, 4.4f), 0.04f, brass);
        Gauge("DepthBig", a, new Vector3(-5f, 2.45f, 4.86f), south, 0.3f, dm[0], brass);

        // B: 注排水盤（赤と緑のランプ）
        var bp = Group("BallastPanel", g);
        Box("Panel", bp, new Vector3(-2.3f, 1.75f, 4.9f), new Vector3(1.6f, 1.6f, 0.1f), dark, true);
        Material lr = M("LampR", new Color(0.6f, 0.05f, 0.04f), 0f, 0.6f, new Color(1f, 0.08f, 0.05f) * 1.8f);
        Material lgm = M("LampG", new Color(0.05f, 0.5f, 0.1f), 0f, 0.6f, new Color(0.1f, 1f, 0.25f) * 1.6f);
        var rnd = new System.Random(7);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                Prim(PrimitiveType.Sphere, "Lamp", bp, new Vector3(-2.95f + i * 0.26f, 2.4f - j * 0.26f, 4.84f), Quaternion.identity, Vector3.one * 0.09f, rnd.NextDouble() < 0.7 ? lgm : lr, false);
        Box("Base", bp, new Vector3(-2.3f, 0.45f, 4.7f), new Vector3(1.6f, 0.9f, 0.5f), body, true);
        for (int i = 0; i < 4; i++) Wheel("Valve", bp, new Vector3(-2.85f + i * 0.37f, 0.75f, 4.43f), south, 0.13f, M("ValveRed", new Color(0.62f, 0.08f, 0.06f), 0.4f, 0.5f));

        // C: 後から付けた新しい画面（ソナー・戦術・状態）
        var c = Group("ModernStation", g);
        Box("Desk", c, new Vector3(2f, 0.4f, 4.5f), new Vector3(5.6f, 0.8f, 0.9f), dark, true);
        BoxR("DeskTop", c, new Vector3(2f, 0.82f, 4.42f), Quaternion.identity, new Vector3(5.7f, 0.05f, 1.0f), M("DeskGrey", new Color(0.22f, 0.24f, 0.25f), 0.5f, 0.5f), false);
        string[] scr = { "sonar", "tactical", "status" };
        for (int i = 0; i < 3; i++)
        {
            float x = 0.1f + i * 1.9f;
            Quaternion tilt = south * Quaternion.Euler(8f, 0f, 0f);
            BoxR("MonitorFrame", c, new Vector3(x, 1.55f, 4.72f), tilt, new Vector3(1.75f, 1.12f, 0.08f), dark, false);
            var sMat = U("Screen_" + scr[i], Dir + "/Tex/" + scr[i] + ".png");
            float w = i == 0 ? 1.0f : 1.6f, h = i == 0 ? 1.0f : 1.0f;
            var sc = Face("Screen", c, new Vector3(x, 1.55f, 4.67f), tilt, new Vector2(w, h), sMat);
            if (i == 0)
            {
                var sw = Face("Sweep", c, new Vector3(x, 1.55f, 4.66f), tilt, new Vector2(1.0f, 1.0f), U("SonarSweep", Dir + "/Tex/sonar_sweep.png", true));
                var ss = UdonSharpUndo.AddComponent<SonarSweep>(sw);
                UdonSharpEditorUtility.CopyProxyToUdon(ss);
            }
            Box("Keyboard", c, new Vector3(x, 0.87f, 4.15f), new Vector3(0.7f, 0.04f, 0.25f), M("KeyGrey", new Color(0.3f, 0.32f, 0.33f), 0.3f, 0.4f), false);
            // 丸い椅子
            Pipe("Stool", c, new Vector3(x, 0f, 3.5f), new Vector3(x, 0.55f, 3.5f), 0.04f, brass);
            Prim(PrimitiveType.Cylinder, "Seat", c, new Vector3(x, 0.58f, 3.5f), Quaternion.identity, new Vector3(0.42f, 0.04f, 0.42f), M("SeatRed", new Color(0.4f, 0.08f, 0.06f), 0.1f, 0.4f), true);
        }
        // 電話（伝声管）
        Pipe("VoiceTube", g, new Vector3(-0.9f, 1.3f, 4.85f), new Vector3(-0.9f, 5.0f, 4.0f), 0.05f, brass);
        Prim(PrimitiveType.Sphere, "Mouth", g, new Vector3(-0.9f, 1.3f, 4.8f), Quaternion.identity, new Vector3(0.18f, 0.12f, 0.18f), brass, false);
    }

    // ---------------- 潜望鏡 ----------------
    static void BuildPeriscope()
    {
        var g = Group("Periscope");
        g.localPosition = new Vector3(-3.2f, 0f, -0.4f);
        Material steel = M("ScopeSteel", new Color(0.62f, 0.64f, 0.63f), 0.85f, 0.65f);
        Material dark = M("ConsoleDark", new Color(0.09f, 0.1f, 0.11f), 0.5f, 0.45f);
        Prim(PrimitiveType.Cylinder, "Well", g, new Vector3(0f, 0.04f, 0f), Quaternion.identity, new Vector3(1.0f, 0.04f, 1.0f), dark, true);
        Pipe("Tube", g, new Vector3(0f, 1.25f, 0f), new Vector3(0f, 5.2f, 0f), 0.16f, steel);
        Box("Body", g, new Vector3(0f, 1.55f, 0f), new Vector3(0.42f, 0.55f, 0.42f), steel, true);
        Box("Eyepiece", g, new Vector3(0f, 1.62f, -0.28f), new Vector3(0.22f, 0.14f, 0.18f), dark);
        Pipe("HandleL", g, new Vector3(-0.21f, 1.45f, 0f), new Vector3(-0.55f, 1.45f, 0f), 0.04f, dark);
        Pipe("HandleR", g, new Vector3(0.21f, 1.45f, 0f), new Vector3(0.55f, 1.45f, 0f), 0.04f, dark);
        Pipe("Ring", g, new Vector3(0f, 4.6f, 0f), new Vector3(0f, 4.75f, 0f), 0.28f, dark);
    }

    // ---------------- 海図台（地図） ----------------
    static void BuildChartTable()
    {
        var g = Group("ChartTable");
        g.localPosition = new Vector3(3.2f, 0f, -1.2f);
        Material wood = M("ChartWood", new Color(0.36f, 0.23f, 0.13f), 0.05f, 0.35f);
        Box("Table", g, new Vector3(0f, 0.47f, 0f), new Vector3(1.9f, 0.94f, 1.4f), wood, true);
        string mp = File.Exists(ChartMapSetup.Png) ? ChartMapSetup.Png : "Assets/My script/museum/Panels/map.png";   // メインルームの地図（上から見たワールド＋敵の船）
        var map = U("ChartMap", mp);
        Face("Map", g, new Vector3(0f, 0.945f, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward), new Vector2(1.76f, 1.32f), map);
        Material brass = M("Brass", new Color(0.72f, 0.56f, 0.28f), 0.85f, 0.6f);
        Box("Ruler", g, new Vector3(0.2f, 0.955f, -0.3f), new Vector3(0.9f, 0.01f, 0.06f), brass);
        Material bulb = M("LampWarm", new Color(1f, 0.9f, 0.7f), 0f, 0.6f, new Color(1f, 0.78f, 0.5f) * 2.2f);
        Pipe("LampCord", g, new Vector3(0f, 5.1f, 0f), new Vector3(0f, 2.1f, 0f), 0.01f, brass);
        Prim(PrimitiveType.Cylinder, "Shade", g, new Vector3(0f, 2.05f, 0f), Quaternion.identity, new Vector3(0.5f, 0.08f, 0.5f), M("ShadeGreen", new Color(0.1f, 0.3f, 0.18f), 0.2f, 0.5f), false);
        Prim(PrimitiveType.Sphere, "Bulb", g, new Vector3(0f, 1.98f, 0f), Quaternion.identity, Vector3.one * 0.12f, bulb, false);
        var lg = new GameObject("TableLight");
        lg.transform.SetParent(g, false);
        lg.transform.localPosition = new Vector3(0f, 1.9f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 3f; l.intensity = 1.2f; l.color = new Color(1f, 0.85f, 0.65f); l.shadows = LightShadows.None;
    }

    // ---------------- R35 の操作方法（南の壁の西側） ----------------
    static void BuildControlBoards()
    {
        var g = Group("ControlBoards");
        Material frame = M("ConsoleDark", new Color(0.09f, 0.1f, 0.11f), 0.5f, 0.45f);
        string[] names = { "controls_pc", "controls_vr" };
        for (int i = 0; i < 2; i++)
        {
            float x = -10.25f + i * 2.2f;
            Quaternion north = Quaternion.LookRotation(Vector3.forward);
            Box("Frame", g, new Vector3(x, 1.65f, -4.88f), new Vector3(2.04f, 2.54f, 0.06f), frame);
            Face(names[i], g, new Vector3(x, 1.65f, -4.84f), north, new Vector2(1.92f, 2.4f), U("Board_" + names[i], Dir + "/Tex/" + names[i] + ".png"));
        }
    }

    // ---------------- 今ある物を艦橋の中に並べ直す（1 回目だけ。2 回目からは手で動かした位置のまま） ----------------
    static void Place(Transform t, Vector3 local, float yaw, string label)
    {
        if (t == null) { log.AppendLine("並べ直し: " + label + " が見つからない"); return; }
        Undo.RecordObject(t, "Bridge");
        t.position = B.position + local;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        log.AppendLine("並べ直し: " + label + " → " + t.position.ToString("F1"));
    }
    static Transform FindAny(string name)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == name) return t;
        return null;
    }

}
