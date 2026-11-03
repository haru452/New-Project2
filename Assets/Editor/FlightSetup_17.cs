using System.Text;
using UnityEditor;
using UnityEngine;

// FlightSetup の続き: R35（Omega）の操縦席を、本物の戦闘機（F-22 のような）操縦席にする
//  ・灰色の計器盤と、その上のひさし（グレアシールド）
//  ・3 つの画面（MFD・まわりにボタン）と、真ん中上のキーパッド（UFCP）
//  ・正面の上のヘッドアップディスプレイ（HUD）のガラス
//  ・左右のコンソールとスイッチの列、両足の間の黄色い脱出ハンドル
//  位置はすべて目（Cockpit の原点）から見た位置。+Z が前・+Y が上
public static partial class FlightSetup
{
    static Material StdMat(string name, Color c, float metal, float gloss)
    {
        var m = LoadOrCreateMat(name, Shader.Find("Standard"));
        m.color = c; m.SetFloat("_Metallic", metal); m.SetFloat("_Glossiness", gloss);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Transform s_keptCockpit, s_keptLevers;
    static bool s_keptRootSet;
    static Vector3 s_keptRootPos, s_keptRootScale;
    static Quaternion s_keptRootRot;

    // 前に作った物（手で動かした位置）を、新しい Cockpit の中へ戻す
    static bool RestoreKept(Transform kept, Transform root)
    {
        if (kept == null) return false;
        Vector3 lp = root.InverseTransformPoint(kept.position);
        Quaternion lr = Quaternion.Inverse(root.rotation) * kept.rotation;
        kept.SetParent(root, false);
        kept.localPosition = lp; kept.localRotation = lr;
        return true;
    }

    static void BuildRealCockpit(Transform root)
    {
        if (RestoreKept(s_keptCockpit, root))
        {
            // 手で動かした計器盤はそのまま（機体を囲む殻が無ければ足す）
            Transform kc = s_keptCockpit; s_keptCockpit = null;
            if (kc.Find("Shell") == null) BuildShell(kc);
            DashV2(kc.Find("Dash"));
            foreach (var en in new[] { "EjectL", "EjectR", "EjectTop" }) { Transform e = kc.Find(en); if (e != null) Object.DestroyImmediate(e.gameObject); }   // 黄色い脱出ハンドルはやめた
            Transform kg = kc.Find("Shell/CanopyGlass");
            if (kg != null && !CanopyGlassOn) Object.DestroyImmediate(kg.gameObject);
            if (kg == null && CanopyGlassOn && kc.Find("Shell") == null) { Transform ks = kc.Find("Shell"); if (ks != null) Object.DestroyImmediate(ks.gameObject); BuildShell(kc); }   // ガラスを付け直す
            return;
        }
        Transform old = root.Find("RealCockpit");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var ck = new GameObject("RealCockpit").transform;
        ck.SetParent(root, false);

        Material gray = StdMat("CkPanelGray", new Color(0.46f, 0.50f, 0.54f), 0.15f, 0.35f);
        Material dark = StdMat("CkDark", new Color(0.13f, 0.14f, 0.15f), 0.2f, 0.3f);
        Material black = StdMat("CkBlack", new Color(0.04f, 0.045f, 0.05f), 0.1f, 0.5f);
        Material screen = UnlitMat("CkScreen", new Color(0.03f, 0.07f, 0.09f, 1f), false, 2000);
        Material screenBlue = UnlitMat("CkScreenBlue", new Color(0.06f, 0.12f, 0.45f, 1f), false, 2000);
        Material key = UnlitMat("CkKeyLight", new Color(0.78f, 0.80f, 0.72f, 1f), false, 2000);
        Material keyDim = UnlitMat("CkKeyDim", new Color(0.35f, 0.37f, 0.36f, 1f), false, 2000);
        Material green = UnlitMat("CkGreen", new Color(0.35f, 0.95f, 0.45f, 1f), false, 2000);
        Material yellow = StdMat("CkYellow", new Color(0.95f, 0.78f, 0.12f), 0.1f, 0.5f);
        Material glass = LoadOrCreateMat("CkHudGlass", Shader.Find("Standard"));
        glass.color = new Color(0.55f, 0.85f, 0.65f, 0.18f);
        glass.SetFloat("_Mode", 3f); glass.SetOverrideTag("RenderType", "Transparent");
        glass.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); glass.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glass.SetInt("_ZWrite", 0); glass.DisableKeyword("_ALPHATEST_ON"); glass.DisableKeyword("_ALPHABLEND_ON"); glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        glass.renderQueue = 3000; glass.SetFloat("_Glossiness", 0.95f);
        EditorUtility.SetDirty(glass);

        // ---- 計器盤（少し奥へ傾ける）----
        var dash = new GameObject("Dash").transform;
        dash.SetParent(ck, false);
        dash.localPosition = new Vector3(0f, -0.25f, 0.80f);
        dash.localRotation = Quaternion.Euler(12f, 0f, 0f);
        dash.localPosition = new Vector3(0f, -0.25f, 0.88f);   // 少し遠く・小さく（DashV2 と同じ）
        dash.localScale = Vector3.one * 0.85f;
        new GameObject("DashV2").transform.SetParent(dash, false);
        Box(dash, "Panel", new Vector3(0f, 0f, 0.03f), new Vector3(0.96f, 0.40f, 0.06f), Quaternion.identity, gray);
        Box(ck, "Glareshield", new Vector3(0f, -0.035f, 0.86f), new Vector3(0.98f, 0.03f, 0.10f), Quaternion.identity, dark);

        // 画面（MFD）: 黒い枠・まわりにボタン
        System.Action<string, Vector3, float, Material> mfd = (n, p, s, scr) =>
        {
            Box(dash, n + "Bezel", p + new Vector3(0f, 0f, 0.005f), new Vector3(s + 0.05f, s + 0.05f, 0.025f), Quaternion.identity, black);
            Quad(dash, n + "Screen", p + new Vector3(0f, 0f, -0.009f), new Vector2(s, s), scr);
            for (int i = 0; i < 4; i++)
            {
                float o = -s * 0.36f + i * s * 0.24f;
                Quad(dash, n + "BtnT", p + new Vector3(o, s * 0.5f + 0.014f, -0.009f), new Vector2(0.018f, 0.01f), keyDim);
                Quad(dash, n + "BtnB", p + new Vector3(o, -s * 0.5f - 0.014f, -0.009f), new Vector2(0.018f, 0.01f), keyDim);
                Quad(dash, n + "BtnL", p + new Vector3(-s * 0.5f - 0.014f, o, -0.009f), new Vector2(0.01f, 0.018f), keyDim);
                Quad(dash, n + "BtnR", p + new Vector3(s * 0.5f + 0.014f, o, -0.009f), new Vector2(0.01f, 0.018f), keyDim);
            }
        };
        // F-16 の並び: 真ん中の上 = キーパッド（ICP）、その左右 = 警報（RWR）と文字の画面（DED）、
        //   左右の下 = 画面（MFD）、真ん中の下 = 丸い計器の縦の列、右の端 = エンジンの計器（スロットル・燃料）
        mfd("MfdL", new Vector3(-0.25f, 0.0f, 0f), 0.15f, screen);
        mfd("MfdR", new Vector3(0.25f, 0.0f, 0f), 0.15f, screenBlue);
        // RWR（丸い警報の画面・ICP の左）と DED（緑の文字の画面・ICP の右）
        var rwr = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(rwr.GetComponent<Collider>());
        rwr.name = "RWR"; rwr.transform.SetParent(dash, false);
        rwr.transform.localPosition = new Vector3(-0.18f, 0.14f, -0.005f); rwr.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        rwr.transform.localScale = new Vector3(0.075f, 0.006f, 0.075f);
        rwr.GetComponent<MeshRenderer>().sharedMaterial = black;
        Quad(dash, "RwrScreen", new Vector3(-0.18f, 0.14f, -0.0125f), new Vector2(0.05f, 0.05f), screen);
        Quad(dash, "RwrCross", new Vector3(-0.18f, 0.14f, -0.013f), new Vector2(0.04f, 0.002f), green);
        Box(dash, "DedBox", new Vector3(0.18f, 0.14f, 0.005f), new Vector3(0.11f, 0.06f, 0.025f), Quaternion.identity, black);
        Quad(dash, "DedScreen", new Vector3(0.18f, 0.14f, -0.009f), new Vector2(0.09f, 0.04f), UnlitMat("CkDed", new Color(0.05f, 0.16f, 0.06f, 1f), false, 2000));
        for (int ln = 0; ln < 4; ln++) Quad(dash, "DedLine", new Vector3(0.17f - (ln % 2) * 0.008f, 0.153f - ln * 0.009f, -0.0095f), new Vector2(0.06f - (ln % 3) * 0.012f, 0.003f), green);
        // 眉の警告灯（ひさしの左右の端）・AOA の表示灯（HUD の左）
        Material amber = UnlitMat("CkAmber", new Color(1f, 0.6f, 0.1f, 1f), false, 2000);
        Material redL = UnlitMat("CkRedLight", new Color(0.9f, 0.12f, 0.08f, 1f), false, 2000);
        for (int sd = -1; sd <= 1; sd += 2)
            for (int i = 0; i < 3; i++)
                Box(ck, "EyebrowLight", new Vector3(sd * (0.30f + i * 0.035f), -0.012f, 0.83f), new Vector3(0.025f, 0.015f, 0.01f), Quaternion.identity, i == 0 ? redL : amber);
        for (int i = 0; i < 3; i++)
            Box(ck, "AoaIndexer", new Vector3(-0.15f, 0.0f + i * 0.02f, 0.82f), new Vector3(0.012f, 0.012f, 0.01f), Quaternion.identity, i == 1 ? green : amber);
        // 真ん中の下は、丸い計器（InstrumentPanel）をはめ込む場所（MountGauges）
        // 真ん中上のキーパッド（UFCP）
        Box(dash, "UfcpBox", new Vector3(0f, 0.135f, 0.005f), new Vector3(0.20f, 0.12f, 0.03f), Quaternion.identity, black);
        Quad(dash, "UfcpDisplay", new Vector3(0f, 0.175f, -0.011f), new Vector2(0.16f, 0.025f), green);
        for (int r = 0; r < 3; r++)
            for (int k = 0; k < 5; k++)
                Quad(dash, "UfcpKey", new Vector3(-0.07f + k * 0.035f, 0.14f - r * 0.025f, -0.011f), new Vector2(0.024f, 0.016f), (r + k) % 4 == 0 ? keyDim : key);
        // 丸い予備の計器（右下）
        for (int i = 0; i < 0; i++)   // 右の丸い計器は、本物の計器（スロットル・燃料）を置くのでやめた
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = "Gauge"; g.transform.SetParent(dash, false);
            g.transform.localPosition = new Vector3(0.36f, 0.03f - i * 0.055f, -0.005f);
            g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale = new Vector3(0.042f, 0.004f, 0.042f);
            g.GetComponent<MeshRenderer>().sharedMaterial = black;
            Quad(dash, "GaugeNeedle", new Vector3(0.36f, 0.03f - i * 0.055f, -0.0115f), new Vector2(0.003f, 0.016f), key);
        }

        // ---- ヘッドアップディスプレイ（HUD）: ひさしの上のガラス ----
        Rod(ck, "HudPostL", new Vector3(-0.11f, -0.02f, 0.84f), new Vector3(-0.11f, 0.16f, 0.80f), 0.012f, dark);
        Rod(ck, "HudPostR", new Vector3(0.11f, -0.02f, 0.84f), new Vector3(0.11f, 0.16f, 0.80f), 0.012f, dark);
        Rod(ck, "HudTop", new Vector3(-0.11f, 0.16f, 0.80f), new Vector3(0.11f, 0.16f, 0.80f), 0.012f, dark);
        var hg = Quad(ck, "HudGlass", new Vector3(0f, 0.07f, 0.82f), new Vector2(0.21f, 0.19f), glass);
        hg.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
        Box(ck, "HudBase", new Vector3(0f, -0.025f, 0.85f), new Vector3(0.26f, 0.02f, 0.06f), Quaternion.identity, black);

        // ---- 左右のコンソール（スイッチの列）----
        for (int side = -1; side <= 1; side += 2)
        {
            Box(ck, side < 0 ? "ConsoleL" : "ConsoleR", new Vector3(side * 0.40f, -0.34f, 0.50f), new Vector3(0.15f, 0.07f, 0.56f), Quaternion.identity, gray);
            for (int j = 0; j < 4; j++)
            {
                float z = 0.28f + j * 0.13f;
                if (side > 0 && j == 3) continue;   // 右の前は操縦桿
                if (side < 0 && j == 3) continue;   // 左の前はスロットル
                Box(ck, "SwitchPanel", new Vector3(side * 0.40f, -0.30f, z), new Vector3(0.13f, 0.012f, 0.11f), Quaternion.identity, black);
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 3; b++)
                    {
                        var sw = Box(ck, "Toggle", new Vector3(side * 0.40f + (a - 1) * 0.038f, -0.285f, z + (b - 1) * 0.032f), new Vector3(0.006f, 0.022f, 0.006f), Quaternion.Euler(20f, 0f, 0f), (a + b) % 3 == 0 ? yellow : key);
                    }
            }
        }
        // ---- 両足の間の黄色い脱出ハンドル ----

        foreach (var r in ck.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        // 機体の内側の壁より手前に収まるように、目を中心に小さく寄せる（見た目の大きさ・角度は同じ）
        ck.localScale = Vector3.one * CockpitShrink;
        BuildShell(ck);
    }

    const float CockpitShrink = 0.7f;
    const bool CanopyGlassOn = true;    // キャノピーのガラス
    const float CanopyH = 0.66f;        // 窓のふちからガラスのいちばん上までの高さ（頭がガラスから出ないように）

    // 丸い計器の板を、計器盤の真ん中の下にはめ込む
    static void MountGauges(Transform root)
    {
        Transform ip = root.Find("InstrumentPanel");
        Transform dash = root.Find("RealCockpit/Dash");
        if (ip == null || dash == null) return;
        // はめ込む場所（GaugeSlot）。手で動かせば、次からもその位置にはまる
        Transform slot = dash.Find("GaugeSlot");
        if (slot == null)
        {
            slot = new GameObject("GaugeSlot").transform;
            slot.SetParent(dash, false);
            slot.localPosition = new Vector3(0f, -0.065f, -0.02f);
            slot.localRotation = Quaternion.identity;
            slot.localScale = Vector3.one * 1.4f;   // F-16 のように大きな丸い計器の縦の列
        }
        Transform oldIp = slot.Find("InstrumentPanel");
        if (oldIp != null) Object.DestroyImmediate(oldIp.gameObject);
        ip.SetParent(slot, false);
        ip.localPosition = Vector3.zero; ip.localRotation = Quaternion.identity; ip.localScale = Vector3.one;
    }

    // ---- 乗っている人のまわりを囲む殻（機体は乗っている人には見えないので、その代わり）----
    //  左右の壁・床・前と後ろの壁・窓のふち・キャノピーの枠（アーチ）・ガラス。単位は m（目が原点）
    static void BuildShell(Transform ck)
    {
        var sh = new GameObject("Shell").transform;
        sh.SetParent(ck, false);
        new GameObject("ShellV3").transform.SetParent(sh, false);   // 高いキャノピー・内側だけのガラスで作った印
        sh.localScale = Vector3.one / CockpitShrink;   // 計器盤の縮小を打ち消して、本当の大きさで作る
        Material hull = StdMat("CkHull", new Color(0.30f, 0.33f, 0.36f), 0.3f, 0.35f);
        Material frameM = StdMat("CkFrame", new Color(0.22f, 0.24f, 0.26f), 0.5f, 0.45f);
        const float W = 0.44f, SILL = -0.16f, FLOOR = -0.80f, FRONT = 0.66f, BACK = -0.62f;
        float midZ = (FRONT + BACK) / 2f, len = FRONT - BACK;
        Box(sh, "WallL", new Vector3(-W, (SILL + FLOOR) / 2f, midZ), new Vector3(0.04f, SILL - FLOOR, len), Quaternion.identity, hull);
        Box(sh, "WallR", new Vector3(W, (SILL + FLOOR) / 2f, midZ), new Vector3(0.04f, SILL - FLOOR, len), Quaternion.identity, hull);
        Box(sh, "Floor", new Vector3(0f, FLOOR, midZ), new Vector3(W * 2f, 0.04f, len), Quaternion.identity, hull);
        Box(sh, "FrontWall", new Vector3(0f, (SILL + FLOOR) / 2f, FRONT), new Vector3(W * 2f, SILL - FLOOR, 0.04f), Quaternion.identity, hull);
        Box(sh, "RearWall", new Vector3(0f, -0.15f, BACK), new Vector3(W * 2f, 1.3f, 0.04f), Quaternion.identity, hull);
        Box(sh, "SillL", new Vector3(-W, SILL, midZ), new Vector3(0.09f, 0.035f, len), Quaternion.identity, frameM);
        Box(sh, "SillR", new Vector3(W, SILL, midZ), new Vector3(0.09f, 0.035f, len), Quaternion.identity, frameM);
        // 機首の上（計器盤の前の外側）
        Box(sh, "NoseTop", new Vector3(0f, SILL - 0.02f, FRONT + 0.6f), new Vector3(W * 2f, 0.04f, 1.2f), Quaternion.Euler(4f, 0f, 0f), hull);

        // キャノピー: 半だ円の筒（横 W・高さ 0.46）。前は少し低く細く。内側から見える向きに面を張る
        const int SEG = 16, RING = 10;
        float zf = FRONT + 0.05f, zb = BACK + 0.02f;
        var v = new System.Collections.Generic.List<Vector3>(); var tri = new System.Collections.Generic.List<int>();
        for (int r = 0; r <= RING; r++)
        {
            float t = r / (float)RING;
            float z = Mathf.Lerp(zb, zf, t);
            float sx = Mathf.Lerp(1f, 0.78f, Mathf.Clamp01((t - 0.55f) / 0.45f));   // 前ほど細く
            float sy = Mathf.Lerp(1f, 0.72f, Mathf.Clamp01((t - 0.55f) / 0.45f));   // 前ほど低く
            for (int i = 0; i <= SEG; i++)
            {
                float a = Mathf.PI * i / SEG;
                v.Add(new Vector3(Mathf.Cos(a) * W * sx, SILL + Mathf.Sin(a) * CanopyH * sy, z));
            }
        }
        for (int r = 0; r < RING; r++)
            for (int i = 0; i < SEG; i++)
            {
                int a0 = r * (SEG + 1) + i, a1 = a0 + 1, b0 = a0 + SEG + 1, b1 = b0 + 1;
                tri.AddRange(new[] { a0, a1, b0, a1, b1, b0 });   // 内側向き
            }
        // 前の窓（風防）: 前の端の半だ円をふさぐ
        int c0 = v.Count; v.Add(new Vector3(0f, SILL, zf));
        int last = RING * (SEG + 1);
        for (int i = 0; i < SEG; i++) { tri.AddRange(new[] { c0, last + i, last + i + 1 }); }
        var mesh = new Mesh(); mesh.name = "CanopyGlass";
        mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string mp = FxDir + "/CanopyGlass.asset";
        var oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
        if (oldMesh != null) { oldMesh.Clear(); EditorUtility.CopySerialized(mesh, oldMesh); mesh = oldMesh; } else AssetDatabase.CreateAsset(mesh, mp);
        Material glass = LoadOrCreateMat("CkCanopyGlass", Shader.Find("Standard"));
        glass.color = new Color(0.62f, 0.74f, 0.82f, 0.10f);
        glass.SetFloat("_Mode", 3f); glass.SetOverrideTag("RenderType", "Transparent");
        glass.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); glass.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glass.SetInt("_ZWrite", 0); glass.DisableKeyword("_ALPHATEST_ON"); glass.DisableKeyword("_ALPHABLEND_ON"); glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        glass.renderQueue = 3001; glass.SetFloat("_Glossiness", 0.95f); glass.SetFloat("_Metallic", 0.05f);
        EditorUtility.SetDirty(glass);
        if (!CanopyGlassOn) goto Frames;
        var gg = new GameObject("CanopyGlass");
        gg.transform.SetParent(sh, false);
        gg.AddComponent<MeshFilter>().sharedMesh = mesh;
        var gr = gg.AddComponent<MeshRenderer>(); gr.sharedMaterial = glass; gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; gr.receiveShadows = false;

        Frames:
        // キャノピーの枠（風防のアーチと、真ん中のアーチ）
        foreach (float zt in new[] { 1f, 0.42f })
        {
            float z = Mathf.Lerp(zb, zf, zt);
            float sx = Mathf.Lerp(1f, 0.78f, Mathf.Clamp01((zt - 0.55f) / 0.45f)), sy = Mathf.Lerp(1f, 0.72f, Mathf.Clamp01((zt - 0.55f) / 0.45f));
            for (int i = 0; i < SEG; i++)
            {
                float a0 = Mathf.PI * i / SEG, a1 = Mathf.PI * (i + 1) / SEG;
                Rod(sh, "CanopyBow", new Vector3(Mathf.Cos(a0) * W * sx, SILL + Mathf.Sin(a0) * CanopyH * sy, z), new Vector3(Mathf.Cos(a1) * W * sx, SILL + Mathf.Sin(a1) * CanopyH * sy, z), zt > 0.9f ? 0.035f : 0.025f, frameM);
            }
        }
        foreach (var r in sh.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    }

    // 座席の見た目だけを下げる（座る位置＝目の位置は変えない）。計器盤やレバーが座席に隠れないように
    const float SeatDrop = 0.35f;
    static void LowerSeatVisual(controller c, StringBuilder log)
    {
        Transform seat = c.transform;
        Transform vis = seat.Find("VRC_chair_udon");
        if (vis == null || vis.Find("SeatLowered") != null) return;
        var st = c.GetComponent<VRC.SDK3.Components.VRCStation>();
        // 座る場所の目印が見た目の中にあると一緒に下がるので、外へ出しておく
        if (st != null && st.stationEnterPlayerLocation != null && st.stationEnterPlayerLocation.IsChildOf(vis))
            Undo.SetTransformParent(st.stationEnterPlayerLocation, seat, "Flight setup");
        if (st != null && st.stationExitPlayerLocation != null && st.stationExitPlayerLocation.IsChildOf(vis))
            Undo.SetTransformParent(st.stationExitPlayerLocation, seat, "Flight setup");
        Undo.RecordObject(vis, "Flight setup");
        vis.position -= seat.up * SeatDrop;
        var mk = new GameObject("SeatLowered");
        Undo.RegisterCreatedObjectUndo(mk, "Flight setup");
        mk.transform.SetParent(vis, false);
        log.AppendLine("座席: " + c.car.name + " の座席の見た目を " + SeatDrop + "m 下げました（座る位置は同じ）");
    }

    // 計器盤を少し遠く・小さくする（前に作った物にも 1 回だけ）
    static void DashV2(Transform dash)
    {
        if (dash == null || dash.Find("DashV2") != null) return;
        dash.localPosition += new Vector3(0f, 0f, 0.08f);
        dash.localScale *= 0.85f;
        new GameObject("DashV2").transform.SetParent(dash, false);
    }

    // Omega_0 で手で合わせた座席（VRCChair3・その中の見た目・座る場所）を、ほかの Omega 全部に合わせる
    const string SeatSource = "Omega_111";   // 見本（この機体は絶対に動かさない）   // この機体の座席に、ほかの Omega を合わせる
    static void CopySeatFromOmega0(StringBuilder log)
    {
        controller src = null;
        foreach (var c in Object.FindObjectsOfType<controller>(true)) if (c.car != null && c.car.name == SeatSource && c.car.activeInHierarchy) { src = c; break; }
        if (src == null) { log.AppendLine("座席: " + SeatSource + "（表示されている物）が見つからないので合わせない"); return; }
        Transform sSeat = src.transform, sCar = src.car.transform;
        Transform sVis = sSeat.Find("VRC_chair_udon");
        var sSt = src.GetComponent<VRC.SDK3.Components.VRCStation>();
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c == src || c.car == null || !IsOmega(c.car)) continue;
            Transform seat = c.transform, car = c.car.transform;
            if (seat.parent == car && sSeat.parent == sCar)
            {
                Undo.RecordObject(seat, "Flight setup");
                seat.localPosition = sSeat.localPosition; seat.localRotation = sSeat.localRotation; seat.localScale = sSeat.localScale;
            }
            Transform vis = seat.Find("VRC_chair_udon");
            if (vis != null && sVis != null)
            {
                Undo.RecordObject(vis, "Flight setup");
                vis.localPosition = sVis.localPosition; vis.localRotation = sVis.localRotation; vis.localScale = sVis.localScale;
                if (vis.Find("SeatLowered") == null) new GameObject("SeatLowered").transform.SetParent(vis, false);
            }
            var st = c.GetComponent<VRC.SDK3.Components.VRCStation>();
            if (st != null && sSt != null && st.stationEnterPlayerLocation != null && sSt.stationEnterPlayerLocation != null && st.stationEnterPlayerLocation != seat)
            {
                Undo.RecordObject(st.stationEnterPlayerLocation, "Flight setup");
                st.stationEnterPlayerLocation.position = seat.TransformPoint(sSeat.InverseTransformPoint(sSt.stationEnterPlayerLocation.position));
                st.stationEnterPlayerLocation.rotation = seat.rotation * (Quaternion.Inverse(sSeat.rotation) * sSt.stationEnterPlayerLocation.rotation);
            }
            n++;
        }
        log.AppendLine("座席: " + SeatSource + " の座席の高さを、ほかの Omega " + n + " 機に合わせました");
    }

    // Omega_1 で手で直した操縦席（RealCockpit・Levers）を、ほかの Omega にそのまま写す（座席は変えない）
    static void CopyCockpitFromOmega1(StringBuilder log)
    {
        controller src = null;
        foreach (var c in Object.FindObjectsOfType<controller>(true)) if (c.car != null && c.car.name == SeatSource && c.car.activeInHierarchy && c.cockpitRoot != null) { src = c; break; }
        if (src == null) foreach (var c in Object.FindObjectsOfType<controller>(true)) if (c.car != null && c.car.name == SeatSource && c.cockpitRoot != null) { src = c; break; }
        if (src == null) { log.AppendLine("操縦席: " + SeatSource + " が見つからないので写さない"); return; }
        Transform sr = src.cockpitRoot.transform;
        Transform sCk = sr.Find("RealCockpit"), sLv = sr.Find("Levers");
        if (sCk == null) { log.AppendLine("操縦席: " + SeatSource + " に RealCockpit が無い"); return; }
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c == src || c.car == null || !IsOmega(c.car) || c.cockpitRoot == null) continue;
            Transform tr = c.cockpitRoot.transform;
            // Cockpit そのものの位置も、見本と同じ（機体の中での位置）に
            Undo.RecordObject(tr, "Flight setup");
            tr.localPosition = sr.localPosition; tr.localRotation = sr.localRotation; tr.localScale = sr.localScale;
            foreach (var nm in new[] { "RealCockpit", "Levers" }) { Transform o = tr.Find(nm); if (o != null) Undo.DestroyObjectImmediate(o.gameObject); }
            var ck = (GameObject)Object.Instantiate(sCk.gameObject, tr, false); ck.name = "RealCockpit";
            Undo.RegisterCreatedObjectUndo(ck, "Flight setup");
            if (sLv != null)
            {
                var lv = (GameObject)Object.Instantiate(sLv.gameObject, tr, false); lv.name = "Levers";
                Undo.RegisterCreatedObjectUndo(lv, "Flight setup");
                Undo.RecordObject(c, "Flight setup");
                c.stickPivot = FindDeepName(lv.transform, "StickPivot");
                c.throttlePivot = FindDeepName(lv.transform, "ThrottlePivot");
            }
            n++;
        }
        log.AppendLine("操縦席: " + SeatSource + " の計器盤・レバー・囲いを、ほかの Omega " + n + " 機に写しました（座席はそのまま）");
    }

    // 緑の表示（ヘッドアップ側のパネル）の配置を Omega_111 に合わせる。コックピットは毎回作り直すので、作り直す前に覚えた配置（s_panelSnap）を使う
    //   パネルそのものの大きさは起動演出で動くので写さない（位置・向きと、中身の部品の位置・向き・大きさを写す）
    //   CenterMessage（目の前のメッセージ）は対象外
    static readonly string[] PanelNames = { "MainPanel", "LeftPanel", "WeaponPanel", "RightPanel", "LogPanel", "CompassPanel", "RadarPanel" };
    struct PanelPose { public string path; public Vector3 pos; public Quaternion rot; public Vector3 scale; }
    static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<PanelPose>> s_panelSnap;
    static void SnapshotPanels(Transform cockpit)
    {
        if (cockpit == null) return;
        var snap = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<PanelPose>>();
        foreach (var nm in PanelNames)
        {
            Transform t = cockpit.Find(nm);
            if (t == null) continue;
            var list = new System.Collections.Generic.List<PanelPose>();
            CollectPose(t, "", list);
            snap[nm] = list;
        }
        if (snap.Count > 0) s_panelSnap = snap;
    }
    static void CollectPose(Transform t, string path, System.Collections.Generic.List<PanelPose> list)
    {
        list.Add(new PanelPose { path = path, pos = t.localPosition, rot = t.localRotation, scale = t.localScale });
        for (int i = 0; i < t.childCount; i++) CollectPose(t.GetChild(i), path + "/" + i, list);
    }
    static void CopyPanelsFromOmega1(StringBuilder log)
    {
        if (s_panelSnap == null)
        {
            foreach (var c in Object.FindObjectsOfType<controller>(true))
                if (c.car != null && c.car.name == SeatSource && c.cockpitRoot != null) { SnapshotPanels(c.cockpitRoot.transform); break; }
        }
        if (s_panelSnap == null) { log.AppendLine("緑の表示のパネル: " + SeatSource + " の配置が取れないので合わせない"); return; }
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsOmega(c.car) || c.cockpitRoot == null) continue;
            foreach (var kv in s_panelSnap)
            {
                Transform panel = c.cockpitRoot.transform.Find(kv.Key);
                if (panel == null) continue;
                foreach (var pp in kv.Value)
                {
                    Transform t = panel;
                    if (pp.path.Length > 0)
                        foreach (var part in pp.path.Substring(1).Split('/')) { int idx = int.Parse(part); t = idx < t.childCount ? t.GetChild(idx) : null; if (t == null) break; }
                    if (t == null) continue;
                    Undo.RecordObject(t, "Flight setup");
                    t.localPosition = pp.pos; t.localRotation = pp.rot;
                    if (pp.path.Length > 0) t.localScale = pp.scale;
                }
            }
            n++;
        }
        s_panelSnap = null;
        log.AppendLine("緑の表示のパネル: " + SeatSource + " の配置を Omega " + n + " 機に合わせました");
    }

    // 首を振ると操縦席が消える対策: 遮蔽カリング（オクルージョン）で、動く物（機体・操縦席）が隠れたと誤判定されないようにする
    static void NoOcclusionForPlanes(StringBuilder log)
    {
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null) continue;
            foreach (var r in c.car.GetComponentsInChildren<Renderer>(true))
                if (r.allowOcclusionWhenDynamic) { Undo.RecordObject(r, "Flight setup"); r.allowOcclusionWhenDynamic = false; n++; }
        }
        log.AppendLine("遮蔽カリング: 機体と操縦席の " + n + " 個の部品を、隠れたと判定されないように");
    }
}
