using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;
using VRC.SDK3.Components;
using VRC.Udon;
using TMPro;
using UdonSharp;

// FlightSetup.cs の続き（長いので分けたファイル。中身は同じクラス FlightSetup）
public static partial class FlightSetup
{
    static void BuildRadar(controller c, Transform root, Material bg, Material frame, Material amber, bool gunnerSet = false)
    {
        const float S = 0.30f;           // 円の直径（m）
        float R = S / 2f;
        bool hudR = !gunnerSet && c.car != null && IsOmega(c.car);
        Transform rp = Panel(root, "RadarPanel", gunnerSet ? new Vector3(0f, -0.25f, 0.85f) : (hudR ? new Vector3(0.40f, 0.15f, 0.66f) : new Vector3(0f, -0.40f, 0.62f)), 0.33f, 0.33f, bg, frame);
        if (hudR) rp.localScale = Vector3.one * 0.75f;   // Omega: 右の上（下の丸い計器と重ならない）
        Material scope = TexMat("RadarScope", GenTex("radar_scope.png", 512, RadarScopePix), Color.white, 2992);
        Material sweep = TexMat("RadarSweep", GenTex("radar_sweep.png", 256, RadarSweepPix), new Color(0.35f, 0.95f, 1f, 1f), 2993);
        Material rose = TexMat("RadarRose", GenTex("radar_rose.png", 512, RadarRosePix), new Color(0.35f, 0.95f, 1f, 1f), 2994);
        Quad(rp, "Scope", new Vector3(0f, 0f, -0.001f), new Vector2(S, S), scope);
        // 島（上から見た陸の形）。範囲の円と同じ大きさ
        Material mapM = RadarMapMat(hudR);   // Omega の操縦席は緑の HUD（何にも隠れない）
        Renderer mapR = null;
        if (mapM != null)
        {
            var mq = Quad(rp, "IslandMap", new Vector3(0f, 0f, -0.0015f), new Vector2(R * RadarScope * 2f, R * RadarScope * 2f), mapM);
            mapR = mq.GetComponent<Renderer>();
        }
        var sw = Quad(rp, "Sweep", new Vector3(0f, 0f, -0.002f), new Vector2(S, S), sweep);
        var rose_t = new GameObject("Rose").transform;
        rose_t.SetParent(rp, false);
        rose_t.localPosition = new Vector3(0f, 0f, -0.003f);
        Quad(rose_t, "Ticks", Vector3.zero, new Vector2(S, S), rose);
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            string lab = i == 0 ? "N" : (i == 3 ? "E" : (i == 6 ? "S" : (i == 9 ? "W" : (i * 30).ToString("000"))));
            var t = Txt(rose_t, "Deg" + i, lab, new Vector2(Mathf.Sin(a) * R * 0.955f, Mathf.Cos(a) * R * 0.955f), 0.04f, i % 3 == 0 ? 0.085f : 0.06f, i % 3 == 0 ? CWhite : CCyan, TextAlignmentOptions.Center);
            t.transform.localRotation = Quaternion.Euler(0f, 0f, -i * 30f);   // 文字も円に沿って回す
        }
        // 自機（中央・黄色の飛行機）
        Texture2D planeTex = GenTex("icon_plane.png", 64, PlaneIconPix);
        Texture2D shipTex = GenTex("icon_ship.png", 64, ShipIconPix);
        Texture2D ringTex = GenTex("icon_ring.png", 64, RingIconPix);
        Material meMat = TexMat("RadarMe", planeTex, new Color(1f, 0.8f, 0.3f, 1f), 2998);
        Quad(rp, "Me", new Vector3(0f, 0f, -0.006f), new Vector2(0.018f, 0.018f), meMat);
        // 四隅の飾りの文字
        Txt(rp, "Title", "RADAR", new Vector2(-0.125f, 0.15f), 0.08f, 0.1f, CCyan, TextAlignmentOptions.Left);
        Txt(rp, "Range", "RNG 2.0km", new Vector2(0.125f, 0.15f), 0.1f, 0.08f, CCyanDim, TextAlignmentOptions.Right);
        Txt(rp, "Mode", "HDG UP", new Vector2(-0.125f, -0.15f), 0.08f, 0.08f, CCyanDim, TextAlignmentOptions.Left);
        Txt(rp, "Legend", "<color=#ff4a40>ENEMY</color> <color=#ffa030>SHIP</color> <color=#80ff90>ALLY</color>", new Vector2(0.125f, -0.15f), 0.14f, 0.06f, CCyanDim, TextAlignmentOptions.Right);

        var mats = new Material[4];
        mats[0] = TexMat("RadarIconEnemy", planeTex, new Color(1f, 0.3f, 0.25f, 1f), 2998);
        mats[1] = TexMat("RadarIconShip", shipTex, new Color(1f, 0.62f, 0.2f, 1f), 2998);
        mats[2] = TexMat("RadarIconTarget", ringTex, new Color(1f, 0.95f, 0.35f, 1f), 2998);
        mats[3] = TexMat("RadarIconFriend", planeTex, new Color(0.5f, 1f, 0.6f, 1f), 2998);
        var dots = new Transform[RadarDotCount];   // 敵機・船・味方など全部を出せる数（足りないと後ろの方の船や味方が出なかった）
        var drs = new Renderer[RadarDotCount];
        for (int i = 0; i < dots.Length; i++)
        {
            var d = Quad(rp, "Blip", new Vector3(0f, 0f, -0.007f), new Vector2(0.017f, 0.017f), mats[0]);
            d.SetActive(false);
            dots[i] = d.transform;
            drs[i] = d.GetComponent<Renderer>();
        }
        // ミサイルの点（白 = 自分、オレンジ = ほかのプレイヤー、赤 = 船のミサイル）
        Texture2D dotTex = GenTex("icon_dot.png", 32, (u, v) => new Color(1f, 1f, 1f, (u * u + v * v) < 0.8f ? 1f : 0f));
        var mslMats = new Material[4];
        mslMats[0] = TexMat("RadarMslOwn", dotTex, new Color(1f, 1f, 1f, 1f), 2999);
        mslMats[1] = TexMat("RadarMslOther", dotTex, new Color(1f, 0.7f, 0.2f, 1f), 2999);
        mslMats[2] = TexMat("RadarMslEnemy", dotTex, new Color(1f, 0.15f, 0.1f, 1f), 2999);
        mslMats[3] = TexMat("RadarMslGuided", dotTex, new Color(0.95f, 0.25f, 1f, 1f), 2999);   // 追ってくる追尾ミサイル（紫）
        var mdots = new Transform[24];
        var mdrs = new Renderer[24];
        for (int i = 0; i < mdots.Length; i++)
        {
            var d = Quad(rp, "MslBlip", new Vector3(0f, 0f, -0.008f), new Vector2(0.008f, 0.008f), mslMats[0]);
            d.SetActive(false);
            mdots[i] = d.transform;
            mdrs[i] = d.GetComponent<Renderer>();
        }

        Undo.RecordObject(c, "Flight setup");
        if (gunnerSet)
        {
            // 後ろの席用（色や範囲は操縦席と共通）
            c.radarMslDotsG = mdots;
            c.radarMslRenderersG = mdrs;
            c.radarDotsG = dots;
            c.radarDotRenderersG = drs;
            c.radarSweepG = sw.transform;
            c.radarRoseG = rose_t;
            c.radarMapG = mapR;
            return;
        }
        c.radarMap = mapR;
        c.radarMapRect = radarMapRect;
        c.radarMslDots = mdots;
        c.radarMslRenderers = mdrs;
        c.radarMslMats = mslMats;
        c.radarDots = dots;
        c.radarDotRenderers = drs;
        c.radarMats = mats;
        c.radarRange = 2000f;
        c.radarRadius = R * RadarScope;
        c.radarSweep = sw.transform;
        c.radarRose = rose_t;
    }

    // 時計のようなメーター（文字盤・数字・針）。針の回転の中心を返す
    static Transform Dial(Transform p, string name, Vector2 ctr, float r, Material face, string[] labels, float[] angles, Material needleMat, float len, float w, float labelSize = 0.085f)
    {
        var d = new GameObject(name).transform;
        d.SetParent(p, false);
        Quad(d, "Face", new Vector3(ctr.x, ctr.y, -0.001f), new Vector2(r * 2f, r * 2f), face);
        for (int i = 0; i < labels.Length; i++)
        {
            float a = angles[i] * Mathf.Deg2Rad;
            Txt(d, "N" + labels[i], labels[i], new Vector2(ctr.x + Mathf.Sin(a) * r * 0.62f, ctr.y + Mathf.Cos(a) * r * 0.62f), 0.04f, labelSize, CWhite, TextAlignmentOptions.Center);
        }
        var pv = new GameObject("Needle").transform;
        pv.SetParent(d, false);
        pv.localPosition = new Vector3(ctr.x, ctr.y, -0.005f);
        Quad(pv, "Bar", new Vector3(0f, len * 0.5f - len * 0.15f, 0f), new Vector2(w, len), needleMat);
        float hub = Mathf.Min(0.01f, r * 0.22f);
        Quad(d, "Hub", new Vector3(ctr.x, ctr.y, -0.006f), new Vector2(hub, hub), needleMat);
        return pv;
    }

    // ---- テクスチャを式で作る（1 回作ったら再利用） ----
    static Texture2D GenTex(string file, int N, System.Func<float, float, Color> fn)
    {
        string path = FxDir + "/" + file;
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
            tex.SetPixel(x, y, fn(u, v));
        }
        Directory.CreateDirectory(FxDir);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null) { imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static readonly Color HudGreen = new Color(0.4f, 1f, 0.5f, 1f);

    // ---------------- 丸い計器の計器盤（Omega） ----------------
    // 上の段: 速度計・姿勢指示器・高度計 / 下の段: 旋回計・方位計・昇降計（本物の飛行機の「6 つの計器」の並び）
    // 左の小さい計器: スロットル・燃料・迎角 / 右: レーダー / 両端: 武器と記録の小さな画面 / 下: 警告の表示
    static void BuildAnalogPanel(controller c, Transform root, bool gaugesOnly = false)
    {
        // gaugesOnly = 丸い計器だけ（文字の情報は緑の HUD に出す時）
        // 戦闘機の計器盤の並び:
        //   上の中央 = 警告灯 / 左 = 四角い画面（HP・武器・燃料・撃墜数）/ 右 = 四角い画面（レーダー・入室記録）
        //   中央 = 丸い計器 6 つ（上: 速度・姿勢・高度 / 下: 旋回・方位・昇降）/ その下 = 迎角・スロットル・燃料
        // 機体の中から見える範囲（目から左右 約 ±19°、下 約 27° まで）に収める
        if (!gaugesOnly)
        foreach (var nm in new[] { "MainPanel", "LeftPanel", "RightPanel", "WeaponPanel", "LogPanel" })
        {
            Transform o = root.Find(nm);
            if (o != null) Object.DestroyImmediate(o.gameObject);
        }
        Material plateMat = LoadOrCreateMat("AnalogPanel", Shader.Find("Standard"));
        plateMat.SetColor("_Color", new Color(0.16f, 0.17f, 0.18f));
        plateMat.SetFloat("_Metallic", 0.35f);
        plateMat.SetFloat("_Glossiness", 0.35f);
        EditorUtility.SetDirty(plateMat);
        Material bezelMat = LoadOrCreateMat("AnalogBezel", Shader.Find("Standard"));
        bezelMat.SetColor("_Color", new Color(0.04f, 0.04f, 0.045f));
        bezelMat.SetFloat("_Metallic", 0.5f);
        bezelMat.SetFloat("_Glossiness", 0.6f);
        EditorUtility.SetDirty(bezelMat);
        Material needle = UnlitMat("AnalogNeedle", new Color(0.95f, 0.95f, 0.92f, 1f), false, 2000);
        Material orange = UnlitMat("AnalogOrange", new Color(1f, 0.55f, 0.1f, 1f), false, 2000);
        Material lcdMat = UnlitMat("AnalogLcd", new Color(0.02f, 0.05f, 0.03f, 1f), false, 2000);
        Material osbMat = UnlitMat("AnalogOsb", new Color(0.30f, 0.31f, 0.32f, 1f), false, 2000);
        Color W = new Color(0.93f, 0.93f, 0.9f, 1f);
        Color LG = new Color(0.5f, 1f, 0.55f, 1f);

        // 計器盤（目の前・少し下。目から 0.52、下に 12°）
        var panel = new GameObject("InstrumentPanel").transform;   // 起動時の「画面が開く」演出を受けない名前
        panel.SetParent(root, false);
        Vector3 pp = gaugesOnly ? new Vector3(0.0296f, -0.0814f, 0.5127f)   // 丸い計器だけ: 目から 0.52・下に 9°・右に 3.3°
                                : new Vector3(0f, -0.126f, 0.505f);   // 目から 0.52・下に 14°
        panel.localPosition = pp;
        panel.localRotation = Quaternion.LookRotation(pp.normalized, Vector3.up);
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.name = "Plate";
        plate.transform.SetParent(panel, false);
        plate.transform.localPosition = gaugesOnly ? new Vector3(0f, 0.005f, 0.012f) : new Vector3(0f, 0f, 0.012f);
        plate.transform.localScale = gaugesOnly ? new Vector3(0.37f, 0.18f, 0.02f) : new Vector3(0.37f, 0.27f, 0.02f);
        plate.GetComponent<MeshRenderer>().sharedMaterial = plateMat;
        plate.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        const float D = 0.048f;     // 中央の丸い計器の直径
        const float d = 0.044f;     // 下の小さい計器
        float R = D / 2f, r = d / 2f;
        float k = R / 0.044f;       // 文字の大きさの倍率
        const float P = 0.054f;     // 計器の間隔
        const float Y1 = 0.062f, Y2 = 0.004f, Y3 = -0.054f;
        Undo.RecordObject(c, "Flight setup");

        // 1) 速度計（0〜300 km/h）
        Material asiM = TexMat("AnalogASI", GenTex("analog_asi.png", 256, (u, v) => AnalogPix(u, v, -135f, 270f, 6, 5,
            new float[] { 70f / 300f, 150f / 300f, 90f / 300f, 220f / 300f, 220f / 300f, 270f / 300f, 278f / 300f, 282f / 300f },
            new Color[] { new Color(0.9f, 0.9f, 0.9f), new Color(0.2f, 0.8f, 0.3f), new Color(0.95f, 0.85f, 0.2f), new Color(0.9f, 0.15f, 0.1f) },
            new float[] { 0.62f, 0.70f, 0.70f, 0.70f })), Color.white, 2992);
        var sl = new string[4]; var sa = new float[4];
        for (int i = 0; i < 4; i++) { sl[i] = (i * 100).ToString(); sa[i] = -135f + i * 90f; }
        Vector2 cA = new Vector2(-P, Y1);
        Bezel(panel, cA, D, bezelMat);
        c.cpSpdNeedle = Dial(panel, "ASI", cA, R, asiM, sl, sa, needle, R * 0.85f, 0.0022f, 0.085f * k);
        Txt(panel, "ASIlab", "KM/H", cA + new Vector2(0f, -R * 0.4f), 0.03f, 0.05f * k, W, TextAlignmentOptions.Center);

        // 2) 姿勢指示器
        Vector2 cB = new Vector2(0f, Y1);
        Bezel(panel, cB, D, bezelMat);
        var ball = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(ball.GetComponent<Collider>());
        ball.name = "AttitudeBall";
        ball.transform.SetParent(panel, false);
        ball.transform.localPosition = new Vector3(cB.x, cB.y, -0.001f);
        ball.transform.localScale = new Vector3(D * 0.92f, D * 0.92f, 1f);
        string ballMatPath = FxDir + "/AttitudeBall.mat";
        Material ballMat = AssetDatabase.LoadAssetAtPath<Material>(ballMatPath);
        if (ballMat == null) { ballMat = new Material(Shader.Find("FlightUI/AttitudeBall")); AssetDatabase.CreateAsset(ballMat, ballMatPath); }
        ballMat.SetTexture("_MainTex", GenTex("attitude_ball.png", 256, AttitudeBallPix));
        ballMat.SetFloat("_Span", 0.5f);
        EditorUtility.SetDirty(ballMat);
        ball.GetComponent<MeshRenderer>().sharedMaterial = ballMat;
        ball.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        c.cpAttDisc = ball.GetComponent<MeshRenderer>();
        Material maskM = TexMat("AnalogAttMask", GenTex("attitude_mask.png", 256, AttitudeMaskPix), Color.white, 2993);
        Quad(panel, "AttMask", new Vector3(cB.x, cB.y, -0.002f), new Vector2(D, D), maskM);
        Quad(panel, "AttWingL", new Vector3(cB.x - R * 0.39f, cB.y, -0.003f), new Vector2(R * 0.5f, R * 0.08f), orange);
        Quad(panel, "AttWingR", new Vector3(cB.x + R * 0.39f, cB.y, -0.003f), new Vector2(R * 0.5f, R * 0.08f), orange);
        Quad(panel, "AttDot", new Vector3(cB.x, cB.y, -0.003f), new Vector2(R * 0.1f, R * 0.1f), orange);

        // 3) 高度計（長い針 = 1 周 1000m、短い針 = 1 周 10000m）
        Material altM = TexMat("AnalogALT", GenTex("analog_alt.png", 256, (u, v) => AnalogPix(u, v, 0f, 360f, 10, 5, null, null, null)), Color.white, 2992);
        var al = new string[10]; var aa = new float[10];
        for (int i = 0; i < 10; i++) { al[i] = i.ToString(); aa[i] = i * 36f; }
        Vector2 cC = new Vector2(P, Y1);
        Bezel(panel, cC, D, bezelMat);
        c.cpAltNeedle = Dial(panel, "ALT", cC, R, altM, al, aa, needle, R * 0.85f, 0.0022f, 0.085f * k);
        var kp = new GameObject("AltNeedleK").transform;
        kp.SetParent(c.cpAltNeedle.parent, false);
        kp.localPosition = new Vector3(cC.x, cC.y, -0.0055f);
        Quad(kp, "Needle", new Vector3(0f, R * 0.25f, 0f), new Vector2(0.0035f, R * 0.55f), needle);
        c.cpAltNeedleK = kp;
        Txt(panel, "ALTlab", "x100m", cC + new Vector2(0f, -R * 0.4f), 0.03f, 0.05f * k, W, TextAlignmentOptions.Center);

        // 4) 旋回計（目盛り = 1 秒に 3° の旋回）
        Material tcM = TexMat("AnalogTC", GenTex("analog_tc.png", 256, TurnFacePix), Color.white, 2992);
        Vector2 cD = new Vector2(-P, Y2);
        Bezel(panel, cD, D, bezelMat);
        Quad(panel, "TCFace", new Vector3(cD.x, cD.y, -0.001f), new Vector2(D, D), tcM);
        var tcp = new GameObject("TurnPlane").transform;
        tcp.SetParent(panel, false);
        tcp.localPosition = new Vector3(cD.x, cD.y, -0.004f);
        Quad(tcp, "Wings", Vector3.zero, new Vector2(R * 1.2f, R * 0.09f), needle);
        Quad(tcp, "Body", new Vector3(0f, R * 0.09f, 0f), new Vector2(R * 0.14f, R * 0.18f), needle);
        Quad(tcp, "Tail", new Vector3(0f, R * 0.2f, 0f), new Vector2(R * 0.05f, R * 0.14f), needle);
        c.cpTurnPlane = tcp;
        Txt(panel, "TCL", "L", cD + new Vector2(-R * 0.62f, -R * 0.32f), 0.01f, 0.07f * k, W, TextAlignmentOptions.Center);
        Txt(panel, "TCR", "R", cD + new Vector2(R * 0.62f, -R * 0.32f), 0.01f, 0.07f * k, W, TextAlignmentOptions.Center);
        Txt(panel, "TClab", "TURN", cD + new Vector2(0f, R * 0.45f), 0.03f, 0.05f * k, W, TextAlignmentOptions.Center);

        // 5) 方位計（円盤が回る。上の三角が今の向き）
        Vector2 cE = new Vector2(0f, Y2);
        Bezel(panel, cE, D, bezelMat);
        Material hdgM = TexMat("AnalogHDG", GenTex("analog_hdg.png", 256, (u, v) => AnalogPix(u, v, 0f, 360f, 12, 6, null, null, null)), Color.white, 2992);
        var card = new GameObject("HdgCard").transform;
        card.SetParent(panel, false);
        card.localPosition = new Vector3(cE.x, cE.y, -0.001f);
        Quad(card, "Card", Vector3.zero, new Vector2(D, D), hdgM);
        string[] hl = { "N", "3", "6", "E", "12", "15", "S", "21", "24", "W", "30", "33" };
        for (int i = 0; i < 12; i++)
        {
            float a2 = i * 30f * Mathf.Deg2Rad;
            var t = Txt(card, "H" + i, hl[i], new Vector2(Mathf.Sin(a2) * R * 0.6f, Mathf.Cos(a2) * R * 0.6f), 0.012f, (i % 3 == 0 ? 0.08f : 0.06f) * k, W, TextAlignmentOptions.Center);
            t.transform.localRotation = Quaternion.Euler(0f, 0f, -i * 30f);
        }
        c.cpHdgCard = card;
        Quad(panel, "HdgLubber", new Vector3(cE.x, cE.y + R * 0.93f, -0.004f), new Vector2(R * 0.1f, R * 0.22f), orange);
        Quad(panel, "HdgPlane", new Vector3(cE.x, cE.y, -0.004f), new Vector2(R * 0.4f, R * 0.07f), orange);
        Quad(panel, "HdgPlaneB", new Vector3(cE.x, cE.y, -0.004f), new Vector2(R * 0.07f, R * 0.45f), orange);

        // 6) 昇降計（±10 m/s。0 が左）
        Material vsiM = TexMat("AnalogVSI", GenTex("analog_vsi.png", 256, (u, v) => AnalogPix(u, v, -260f, 340f, 4, 5, null, null, null)), Color.white, 2992);
        string[] vl = { "10", "5", "0", "5", "10" };
        float[] va = { -260f, -175f, -90f, -5f, 80f };
        Vector2 cF = new Vector2(P, Y2);
        Bezel(panel, cF, D, bezelMat);
        c.cpVsiNeedle = Dial(panel, "VSI", cF, R, vsiM, vl, va, needle, R * 0.85f, 0.0022f, 0.085f * k);
        Txt(panel, "VSIup", "UP", cF + new Vector2(R * 0.05f, R * 0.25f), 0.02f, 0.05f * k, W, TextAlignmentOptions.Center);
        Txt(panel, "VSIdn", "DN", cF + new Vector2(R * 0.05f, -R * 0.3f), 0.02f, 0.05f * k, W, TextAlignmentOptions.Center);

        // 下の段: 迎角・スロットル（エンジン）・燃料
        float kk = r / 0.044f;
        float stallN = Mathf.Clamp01(c.stallAngle / 25f);
        Material aoaM = TexMat("AnalogAOA", GenTex("analog_aoa.png", 256, (u, v) => AnalogPix(u, v, -120f, 240f, 5, 5,
            new float[] { stallN, 1f }, new Color[] { new Color(0.9f, 0.15f, 0.1f) }, new float[] { 0.70f })), Color.white, 2992);
        Vector2 cG = new Vector2(-P, Y3);
        Bezel(panel, cG, d, bezelMat);
        c.cpAoaNeedle = Dial(panel, "AOA", cG, r, aoaM, new[] { "0", "10", "20" }, new[] { -120f, -24f, 72f }, needle, r * 0.85f, 0.002f, 0.085f * kk);
        Txt(panel, "AOAlab", "AoA", cG + new Vector2(0f, -r * 0.45f), 0.03f, 0.05f * kk, W, TextAlignmentOptions.Center);
        Material thrM = TexMat("AnalogTHR", GenTex("analog_thr.png", 256, (u, v) => AnalogPix(u, v, -120f, 240f, 5, 4,
            new float[] { 0.6f, 0.9f, 0.98f, 1f }, new Color[] { new Color(0.2f, 0.8f, 0.3f), new Color(0.9f, 0.15f, 0.1f) }, new float[] { 0.70f, 0.70f })), Color.white, 2992);
        Vector2 cH = new Vector2(0f, Y3);
        Bezel(panel, cH, d, bezelMat);
        c.cpThrNeedle = Dial(panel, "THR", cH, r, thrM, new[] { "0", "50", "100" }, new[] { -120f, 0f, 120f }, needle, r * 0.85f, 0.002f, 0.085f * kk);
        Txt(panel, "THRlab", "THR %", cH + new Vector2(0f, -r * 0.45f), 0.03f, 0.05f * kk, W, TextAlignmentOptions.Center);
        Material fuelM = TexMat("AnalogFUEL", GenTex("analog_fuel.png", 256, (u, v) => AnalogPix(u, v, -60f, 120f, 4, 2,
            new float[] { 0f, 0.2f }, new Color[] { new Color(0.9f, 0.15f, 0.1f) }, new float[] { 0.70f })), Color.white, 2992);
        Vector2 cI = new Vector2(P, Y3);
        Bezel(panel, cI, d, bezelMat);
        c.cpFuelNeedle = Dial(panel, "FUEL", cI, r, fuelM, new[] { "E", "1/2", "F" }, new[] { -60f, 0f, 60f }, needle, r * 0.85f, 0.002f, 0.085f * kk);
        Txt(panel, "FUELlab", "FUEL", cI + new Vector2(0f, -r * 0.45f), 0.03f, 0.05f * kk, W, TextAlignmentOptions.Center);

        if (gaugesOnly) return;

        // 警告灯（上の中央）
        Transform ann = Lcd(panel, "Annunciator", new Vector2(0f, 0.112f), new Vector2(0.16f, 0.03f), lcdMat, bezelMat);
        c.cpWarn = Txt(ann, "Warn", "", new Vector2(0f, 0.005f), 0.155f, 0.075f, Color.white, TextAlignmentOptions.Center);
        c.cpStatus = Txt(ann, "Status", "", new Vector2(0f, -0.009f), 0.155f, 0.04f, LG, TextAlignmentOptions.Center);

        // 左の四角い画面: HP・武器・ロック・フラップなど・燃料・撃墜数・人数
        const float MX = 0.131f, MW = 0.084f, MH = 0.22f, MY = 0.01f;   // 丸い計器と重ならない位置
        Transform lcdL = Mfd(panel, "MfdLeft", new Vector2(-MX, MY), new Vector2(MW, MH), lcdMat, bezelMat, osbMat);
        Txt(lcdL, "HpLab", "HP", new Vector2(-0.03f, 0.093f), 0.02f, 0.05f, LG, TextAlignmentOptions.Left);
        c.cpHp = Txt(lcdL, "Hp", "100", new Vector2(0.008f, 0.09f), 0.065f, 0.09f, LG, TextAlignmentOptions.Right);
        c.cpWeapons = Txt(lcdL, "Weapons", "", new Vector2(0f, 0.052f), 0.082f, 0.045f, LG, TextAlignmentOptions.Left, 0.04f);
        c.cpLock = Txt(lcdL, "Lock", "", new Vector2(0f, 0.018f), 0.082f, 0.045f, LG, TextAlignmentOptions.Center);
        c.cpFlags = Txt(lcdL, "Flags", "", new Vector2(0f, -0.022f), 0.082f, 0.042f, LG, TextAlignmentOptions.Left, 0.045f);
        c.cpFuel = Txt(lcdL, "Fuel", "", new Vector2(0f, -0.058f), 0.082f, 0.04f, LG, TextAlignmentOptions.Center);
        c.cpKills = Txt(lcdL, "Kills", "", new Vector2(0f, -0.078f), 0.082f, 0.04f, LG, TextAlignmentOptions.Center);
        c.cpPlayers = Txt(lcdL, "Online", "ONLINE 1", new Vector2(0f, -0.096f), 0.082f, 0.04f, LG, TextAlignmentOptions.Center);

        // 右の四角い画面: 上 = レーダー、下 = 入室記録
        Transform lcdR = Mfd(panel, "MfdRight", new Vector2(MX, MY), new Vector2(MW, MH), lcdMat, bezelMat, osbMat);
        Transform rp = root.Find("RadarPanel");
        if (rp != null)
        {
            rp.SetParent(lcdR, false);
            rp.localPosition = new Vector3(0f, 0.058f, -0.001f);
            rp.localRotation = Quaternion.identity;
            rp.localScale = Vector3.one * 0.26f;
        }
        c.cpLog = Txt(lcdR, "Log", "", new Vector2(0f, -0.045f), 0.082f, 0.032f, LG, TextAlignmentOptions.TopLeft, 0.12f);

        // 数字だけの表示・パネル用の部品は使わない
        c.cpSpeed = null; c.cpAlt = null; c.cpVs = null; c.cpAtt = null; c.cpHdg = null; c.cpAoa = null; c.cpThr = null;
        c.cpHorizon = null; c.cpThrBar = null; c.cpAoaBar = null; c.cpHpBar = null; c.cpGun = null; c.cpGunBar = null;
        c.staticMat = null;
        c.cpHdgNum = null; c.cpVsNum = null; c.cpFuelPct = null;
    }


    // ---------------- HUD（前の空中に緑の線と文字で表示。計器が読めなくても全部分かる） ----------------
    // 真ん中: 機首の向き（W）・飛んでいる方向（○）・水平線と角度のはしご・傾き
    // 上: 方位のテープ / 左: 速度・迎角・マッハ・G・弾数・スロットル・操縦桿 / 右: 高度・上昇率・HP・各機能の状態
    const float HudL = 1f;   // 目からの距離（コックピットの単位）
    static Vector3 HudAt(float xDeg, float yDeg) { return new Vector3(Mathf.Tan(xDeg * Mathf.Deg2Rad) * HudL, Mathf.Tan(yDeg * Mathf.Deg2Rad) * HudL, HudL); }
    static Material hudLineMat, hudFontMat;
    static Material HudLine()
    {
        string path = FxDir + "/HUD_Line.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = m == null;
        if (create) m = new Material(Shader.Find("FlightUI/UnlitTint"));
        m.SetColor("_Color", new Color(HudGreen.r, HudGreen.g, HudGreen.b, 0.95f));
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3090;
        if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
        return m;
    }
    static TextMeshPro HudTxt(Transform p, string name, string text, Vector3 lp, float w, float size, TextAlignmentOptions al)
    {
        var t = Txt(p, name, text, Vector2.zero, w, size, HudGreen, al);
        t.transform.localPosition = lp;
        if (hudFontMat == null)
        {
            string fp = FxDir + "/HUD_Font.mat";
            hudFontMat = AssetDatabase.LoadAssetAtPath<Material>(fp);
            if (hudFontMat == null)
            {
                hudFontMat = new Material(t.fontSharedMaterial);
                Shader ov = Shader.Find("TextMeshPro/Distance Field Overlay");
                if (ov != null) hudFontMat.shader = ov;
                hudFontMat.renderQueue = 3100;
                AssetDatabase.CreateAsset(hudFontMat, fp);
            }
        }
        t.fontSharedMaterial = hudFontMat;
        return t;
    }
    // 線（太さ w・長さ len・角度 deg）
    static void HudSeg(Transform p, string name, Vector3 lp, float len, float w, float deg)
    {
        var q = Quad(p, name, lp, new Vector2(len, w), hudLineMat);
        q.transform.localRotation = Quaternion.Euler(0f, 0f, deg);
    }
}
