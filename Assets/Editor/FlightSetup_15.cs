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
    static Color RadarScopePix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float R = RadarScope;
        if (r > R + 0.01f) return new Color(0f, 0f, 0f, 0f);
        Color baseC = Color.Lerp(new Color(0.02f, 0.14f, 0.22f), new Color(0.0f, 0.04f, 0.08f), r / R);   // 真ん中が少し明るい
        float a = 0.93f;
        float ink = 0f;
        // 細かい格子
        float g = 0.0925f;
        float gu = Mathf.Abs(Mathf.Repeat(u + g * 0.5f, g) - g * 0.5f), gv = Mathf.Abs(Mathf.Repeat(v + g * 0.5f, g) - g * 0.5f);
        ink = Mathf.Max(ink, Line(Mathf.Min(gu, gv), 0.0015f) * 0.18f);
        // 同心円 4 本と十字・斜めの線
        for (int i = 1; i <= 4; i++) ink = Mathf.Max(ink, Line(r - R * i / 4f, 0.003f) * (i == 4 ? 0.9f : 0.5f));
        ink = Mathf.Max(ink, Line(Mathf.Min(Mathf.Abs(u), Mathf.Abs(v)), 0.002f) * 0.45f);
        ink = Mathf.Max(ink, Line(Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) * 0.7071f, 0.0015f) * 0.22f);
        Color c = Color.Lerp(baseC, new Color(0.35f, 0.95f, 1f), ink);
        c.a = a * (1f - Mathf.Clamp01((r - R) / 0.01f));
        return c;
    }

    static Color RadarSweepPix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > RadarScope) return new Color(1f, 1f, 1f, 0f);
        float ang = ClockAngle(u, v);
        // 光の先端は真上（0°）。時計回りに回るので、尾は反時計回り側（360° から 300°）
        float behind = (360f - ang) % 360f;
        float a = 0f;
        if (behind < 60f) a = Mathf.Pow(1f - behind / 60f, 2f) * 0.45f;
        if (behind < 1.2f || ang < 0.4f) a = 0.9f;
        return new Color(1f, 1f, 1f, a);
    }

    static Color RadarRosePix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float ink = 0f;
        ink = Mathf.Max(ink, Line(r - 0.765f, 0.004f) * 0.9f);
        ink = Mathf.Max(ink, Line(r - 0.885f, 0.003f) * 0.6f);
        float ang = ClockAngle(u, v);
        int k = Mathf.RoundToInt(ang / 5f);
        float dAng = ang - k * 5f;
        float perp = Mathf.Abs(r * Mathf.Sin(dAng * Mathf.Deg2Rad));
        bool ten = k % 2 == 0, thirty = k % 6 == 0;
        float outer = thirty ? 0.885f : (ten ? 0.85f : 0.82f);
        if (r > 0.77f && r < outer) ink = Mathf.Max(ink, Line(perp, thirty ? 0.006f : 0.003f) * (ten ? 1f : 0.6f));
        return new Color(1f, 1f, 1f, ink);
    }

    // 飛行機の形（上 = 機首）
    static Color PlaneIconPix(float u, float v)
    {
        float au = Mathf.Abs(u);
        bool body = au < 0.1f && v > -0.8f && v < 0.85f - au;
        bool wing = au < 0.9f && v < 0.3f - 0.4f * au && v > 0.0f - 0.32f * au;
        bool tail = au < 0.38f && v < -0.55f - 0.3f * au && v > -0.82f;
        bool nose = v >= 0.75f && v < 0.92f && au < 0.92f - v;
        return new Color(1f, 1f, 1f, (body || wing || tail || nose) ? 1f : 0f);
    }

    // 船の形（上 = 船首）
    static Color ShipIconPix(float u, float v)
    {
        float au = Mathf.Abs(u);
        bool hull = (v > -0.8f && v <= 0.3f && au < 0.34f) || (v > 0.3f && v < 0.9f && au < 0.34f * (0.9f - v) / 0.6f);
        return new Color(1f, 1f, 1f, hull ? 1f : 0f);
    }

    static Color RingIconPix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        bool on = (r > 0.55f && r < 0.85f) || r < 0.22f;
        return new Color(1f, 1f, 1f, on ? 1f : 0f);
    }

    static Texture2D GetRadarTexture()
    {
        string path = FxDir + "/radar.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        const int N = 256;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            float a = 0f;
            float lw = 2.2f / N;
            foreach (float ring in new[] { 0.33f, 0.66f, 0.99f }) a = Mathf.Max(a, 1f - Mathf.Clamp01(Mathf.Abs(r - ring) / lw));
            if (r < 1f) a = Mathf.Max(a, 0.5f * (1f - Mathf.Clamp01(Mathf.Abs(u) / lw)), 0.5f * (1f - Mathf.Clamp01(Mathf.Abs(v) / lw)));
            if (r < 1f) a = Mathf.Max(a, 0.08f);   // うっすら円の中を塗る
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        Directory.CreateDirectory(FxDir);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null) { imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------- コックピットの内装 ----------------
    static void BuildInterior(Transform root, Material console)
    {
        var inside = new GameObject("Interior").transform;
        inside.SetParent(root, false);
        Material btnC = UnlitMat("CockpitBtnCyan", new Color(0.4f, 0.95f, 1f, 1f), false, 2000);
        Material btnW = UnlitMat("CockpitBtnWhite", new Color(0.85f, 0.95f, 1f, 1f), false, 2000);
        Material btnD = UnlitMat("CockpitBtnDim", new Color(0.12f, 0.35f, 0.4f, 1f), false, 2000);

        // 机（前のコンソール）と左右のコンソール、横の壁
        Box(inside, "Desk", new Vector3(0f, -0.62f, 0.52f), new Vector3(1.5f, 0.06f, 0.62f), Quaternion.Euler(14f, 0f, 0f), console);
        Box(inside, "DashFront", new Vector3(0f, -0.52f, 0.86f), new Vector3(1.1f, 0.34f, 0.08f), Quaternion.identity, console);
        Box(inside, "SideL", new Vector3(-0.64f, -0.44f, 0.38f), new Vector3(0.42f, 0.06f, 0.66f), Quaternion.Euler(0f, 0f, -12f), console);
        Box(inside, "SideR", new Vector3(0.64f, -0.44f, 0.38f), new Vector3(0.42f, 0.06f, 0.66f), Quaternion.Euler(0f, 0f, 12f), console);
        // 横の壁（窓の下・床まで）と床・前の下・後ろの壁で、すき間から外（海）が見えないように囲う
        Box(inside, "WallL", new Vector3(-0.88f, -0.67f, 0.17f), new Vector3(0.04f, 1.08f, 1.6f), Quaternion.identity, console);
        Box(inside, "WallR", new Vector3(0.88f, -0.67f, 0.17f), new Vector3(0.04f, 1.08f, 1.6f), Quaternion.identity, console);
        Box(inside, "Floor", new Vector3(0f, -1.22f, 0.17f), new Vector3(1.8f, 0.04f, 1.6f), Quaternion.identity, console);
        Box(inside, "FrontLower", new Vector3(0f, -0.88f, 0.95f), new Vector3(1.8f, 0.7f, 0.04f), Quaternion.identity, console);
        Box(inside, "RearWall", new Vector3(0f, -0.33f, -0.62f), new Vector3(1.8f, 1.8f, 0.04f), Quaternion.identity, console);
        // 前の角（横のコンソールと正面のパネルの間・窓枠の下）のすき間をふさぐ
        Box(inside, "CornerL", new Vector3(-0.70f, -0.42f, 0.80f), new Vector3(0.38f, 0.42f, 0.34f), Quaternion.Euler(0f, 35f, 0f), console);
        Box(inside, "CornerR", new Vector3(0.70f, -0.42f, 0.80f), new Vector3(0.38f, 0.42f, 0.34f), Quaternion.Euler(0f, -35f, 0f), console);

        // 窓枠（キャノピー）
        float t = 0.035f;
        Rod(inside, "PillarL", new Vector3(-0.52f, -0.27f, 0.88f), new Vector3(-0.34f, 0.5f, 0.62f), t, console);
        Rod(inside, "PillarR", new Vector3(0.52f, -0.27f, 0.88f), new Vector3(0.34f, 0.5f, 0.62f), t, console);
        Rod(inside, "TopBar", new Vector3(-0.34f, 0.5f, 0.62f), new Vector3(0.34f, 0.5f, 0.62f), t, console);
        Rod(inside, "RoofL", new Vector3(-0.34f, 0.5f, 0.62f), new Vector3(-0.42f, 0.55f, -0.2f), t, console);
        Rod(inside, "RoofR", new Vector3(0.34f, 0.5f, 0.62f), new Vector3(0.42f, 0.55f, -0.2f), t, console);
        Rod(inside, "SillL", new Vector3(-0.52f, -0.27f, 0.88f), new Vector3(-0.86f, -0.14f, -0.1f), t, console);
        Rod(inside, "SillR", new Vector3(0.52f, -0.27f, 0.88f), new Vector3(0.86f, -0.14f, -0.1f), t, console);
        Rod(inside, "SideFrameL", new Vector3(-0.86f, -0.14f, 0.2f), new Vector3(-0.42f, 0.55f, 0.15f), t, console);
        Rod(inside, "SideFrameR", new Vector3(0.86f, -0.14f, 0.2f), new Vector3(0.42f, 0.55f, 0.15f), t, console);

        // ボタン（左右のコンソールの上に光るボタンを並べる）
        var rnd = new System.Random(7);
        for (int side = -1; side <= 1; side += 2)
        for (int gx = 0; gx < 3; gx++)
        for (int gz = 0; gz < 5; gz++)
        {
            Material m = rnd.Next(5) == 0 ? btnW : (rnd.Next(3) == 0 ? btnD : btnC);
            float x = side * (0.52f + gx * 0.07f);
            float y = -0.405f - gx * 0.015f * 1f;
            float z = 0.18f + gz * 0.09f;
            var b = Quad(inside, "Button", new Vector3(x, y, z), new Vector2(0.04f, 0.04f), m);
            b.transform.localRotation = Quaternion.Euler(90f, 0f, side * 12f);
        }
        // 正面のボタン列（モニターの下）
        for (int i = 0; i < 10; i++)
        {
            Material m = i % 4 == 0 ? btnW : (i % 3 == 0 ? btnD : btnC);
            var b = Quad(inside, "FrontButton", new Vector3(-0.27f + i * 0.06f, -0.375f, 0.815f), new Vector2(0.035f, 0.022f), m);
            b.transform.localRotation = Quaternion.LookRotation(new Vector3(-0.27f + i * 0.06f, -0.375f, 0.815f).normalized, Vector3.up);
        }

        // 操縦桿
        Box(inside, "StickBase", new Vector3(0f, -0.6f, 0.36f), new Vector3(0.1f, 0.03f, 0.1f), Quaternion.identity, console);
        Rod(inside, "Stick", new Vector3(0f, -0.6f, 0.36f), new Vector3(0f, -0.42f, 0.38f), 0.03f, console);
        var knob = Box(inside, "StickTop", new Vector3(0f, -0.41f, 0.38f), new Vector3(0.05f, 0.07f, 0.05f), Quaternion.identity, console);
        var light = Quad(inside, "StickLight", new Vector3(0f, -0.385f, 0.38f), new Vector2(0.02f, 0.02f), btnC);
        light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    static GameObject Box(Transform parent, string name, Vector3 lp, Vector3 size, Quaternion rot, Material m)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(b.GetComponent<Collider>());
        b.name = name;
        b.transform.SetParent(parent, false);
        b.transform.localPosition = lp;
        b.transform.localRotation = rot;
        b.transform.localScale = size;
        var r = b.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return b;
    }

    static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float thick, Material m)
    {
        Vector3 d = b - a;
        Vector3 upHint = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        return Box(parent, name, (a + b) * 0.5f, new Vector3(thick, thick, d.magnitude), Quaternion.LookRotation(d.normalized, upHint), m);
    }

    static Transform Panel(Transform root, string name, Vector3 pos, float w, float h, Material bg, Material frame)
    {
        var p = new GameObject(name).transform;
        p.SetParent(root, false);
        p.localPosition = pos;
        p.localRotation = Quaternion.LookRotation(pos.normalized, Vector3.up);   // 目の方を向ける
        Quad(p, "BG", Vector3.zero, new Vector2(w, h), bg);
        if (s_consoleMat != null)
        {
            var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(housing.GetComponent<Collider>());
            housing.name = "Housing";
            housing.transform.SetParent(p, false);
            housing.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            housing.transform.localScale = new Vector3(w + 0.04f, h + 0.04f, 0.05f);
            var hr = housing.GetComponent<MeshRenderer>();
            hr.sharedMaterial = s_consoleMat;
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        float L = Mathf.Min(w, h) * 0.22f;
        float t = 0.005f;
        float z = -0.001f;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sy = -1; sy <= 1; sy += 2)
        {
            Quad(p, "Corner", new Vector3(sx * (w / 2f - L / 2f), sy * (h / 2f), z), new Vector2(L, t), frame);
            Quad(p, "Corner", new Vector3(sx * (w / 2f), sy * (h / 2f - L / 2f), z), new Vector2(t, L), frame);
        }
        Quad(p, "TopEdge", new Vector3(0f, h / 2f, z), new Vector2(w * 0.45f, 0.002f), frame);
        Quad(p, "BottomEdge", new Vector3(0f, -h / 2f, z), new Vector2(w * 0.3f, 0.002f), frame);
        if (s_staticMat != null)
        {
            var st = Quad(p, "Static", new Vector3(0f, 0f, -0.009f), new Vector2(w, h), s_staticMat);
            st.SetActive(false);
        }
        return p;
    }

    static GameObject Quad(Transform parent, string name, Vector3 lp, Vector2 size, Material m)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(parent, false);
        q.transform.localPosition = lp;
        q.transform.localRotation = Quaternion.identity;
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return q;
    }

    static TextMeshPro Txt(Transform p, string name, string text, Vector2 center, float width, float size, Color col, TextAlignmentOptions align, float height = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.localPosition = new Vector3(center.x, center.y, -0.004f);
        go.transform.localRotation = Quaternion.identity;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = col;
        tmp.alignment = align;
        tmp.enableWordWrapping = false;
        tmp.richText = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.rectTransform.sizeDelta = new Vector2(width, height > 0f ? height : size * 0.13f);
        return tmp;
    }

    static Transform Bar(Transform p, string name, Vector2 center, Vector2 size, bool vertical, Material bgM, Material fillM)
    {
        Quad(p, name + "BG", new Vector3(center.x, center.y, -0.001f), size, bgM);
        var pivot = new GameObject(name).transform;
        pivot.SetParent(p, false);
        pivot.localPosition = vertical
            ? new Vector3(center.x, center.y - size.y / 2f, -0.002f)
            : new Vector3(center.x - size.x / 2f, center.y, -0.002f);
        Quad(pivot, "Fill", vertical ? new Vector3(0f, size.y / 2f, 0f) : new Vector3(size.x / 2f, 0f, 0f), size, fillM);
        return pivot;
    }

    static Material UnlitMat(string name, Color col, bool transparent, int queue)
    {
        string path = FxDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(FxDir);
        Shader sh = Shader.Find("FlightUI/UnlitTint");
        m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        if (m.HasProperty("_Color")) m.SetColor("_Color", col);
        if (transparent)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        m.renderQueue = queue;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static bool SetupGun(controller c, Material fxMat, AudioClip clip, ParticleSystem impact, StringBuilder log)
    {
        if (c.car == null) return false;
        Transform car = c.car.transform;
        Transform gun = car.Find("Gun");
        if (gun == null)
        {
            Vector3 fwd = c.axisReference != null ? c.axisReference.forward : -car.right;
            Vector3 up = c.axisReference != null ? c.axisReference.up : car.up;
            float r = BodyRadius(c.car);

            var go = new GameObject("Gun");
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            go.transform.SetParent(car, false);
            go.transform.SetPositionAndRotation(car.position + fwd * (r + 0.8f), Quaternion.LookRotation(fwd, up));
            SetWorldScaleOne(go.transform);
            gun = go.transform;

            // 曳光弾
            var tr = MakePS("Tracer", gun, fxMat);
            var main = tr.main;
            main.startLifetime = 1.6f;
            main.startSpeed = 500f;
            main.startSize = 0.35f;
            main.startColor = new Color(1f, 0.75f, 0.3f, 1f);
            main.maxParticles = 300;
            var shape = tr.shape;
            shape.enabled = false;
            var iv = tr.inheritVelocity;
            iv.enabled = true;
            iv.mode = ParticleSystemInheritVelocityMode.Initial;
            iv.curve = new ParticleSystem.MinMaxCurve(1f);
            var rend = tr.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.025f;
            rend.lengthScale = 2f;

            // 発射音
            var au = go.AddComponent<AudioSource>();
            au.playOnAwake = false;
            au.clip = clip;
            au.spatialBlend = 1f;
            au.minDistance = 10f;
            au.maxDistance = 900f;
            au.volume = 0.7f;
            au.dopplerLevel = 0f;

            // 照準（60m 先の +）
            var sight = new GameObject("GunSight");
            Undo.RegisterCreatedObjectUndo(sight, "Flight setup");
            sight.transform.SetParent(gun, false);
            sight.transform.localPosition = new Vector3(0f, 0f, 60f);
            sight.transform.localRotation = Quaternion.identity;
            var tmp = sight.AddComponent<TextMeshPro>();
            tmp.text = "+";
            tmp.fontSize = 40f;
            tmp.color = new Color(0.3f, 1f, 0.45f, 0.9f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(10f, 10f);
            sight.SetActive(false);

            log.AppendLine("  機銃取付: " + GetPath(car));
        }

        Undo.RecordObject(c, "Flight setup");
        c.gunMuzzle = gun;
        Transform trT = gun.Find("Tracer");
        c.tracerFx = trT != null ? trT.GetComponent<ParticleSystem>() : null;
        c.gunAudio = gun.GetComponent<AudioSource>();
        Transform sT = gun.Find("GunSight");
        c.gunSight = sT != null ? sT.gameObject : null;
        c.impactFx = impact;
        UdonSharpEditorUtility.CopyProxyToUdon(c);
        PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
        if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        return true;
    }

    static ParticleSystem EnsureImpact(Material fxMat, StringBuilder log)
    {
        GameObject existing = GameObject.Find("GunImpact");
        if (existing != null && existing.GetComponent<ParticleSystem>() != null) return existing.GetComponent<ParticleSystem>();

        var ps = MakePS("GunImpact", null, fxMat);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
        main.startColor = new Color(1f, 0.6f, 0.2f, 1f);
        main.gravityModifier = 1f;
        main.maxParticles = 400;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.1f;
        SetFade(ps, new Color(1f, 0.8f, 0.4f), new Color(0.8f, 0.3f, 0.1f));
        log.AppendLine("着弾エフェクト作成");
        return ps;
    }

    // 海面に弾が当たった時の水柱（白い水しぶきが上に吹き上がって落ちる）
    static ParticleSystem EnsureWaterSplash(Texture2D tex, StringBuilder log)
    {
        GameObject old = GameObject.Find("WaterSplash");
        if (old != null) Undo.DestroyObjectImmediate(old);

        Shader sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        Material m = LoadOrCreateMat("WaterFx", sh);
        m.SetTexture("_MainTex", tex);
        m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);

        var ps = MakePS("WaterSplash", null, m);
        ps.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // 上向き
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 18f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.92f, 1f, 0.85f), new Color(1f, 1f, 1f, 0.95f));
        main.gravityModifier = 1.3f;
        main.maxParticles = 600;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.3f;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.5f));
        SetFade(ps, Color.white, new Color(0.8f, 0.88f, 0.95f));
        log.AppendLine("水柱エフェクト作成");
        return ps;
    }

    static void CreateTargets(Material fxMat, AudioClip boom, StringBuilder log)
    {
        var cat = Object.FindObjectOfType<Catapult>(true);
        Vector3 start = cat != null ? cat.transform.position : new Vector3(254f, -7f, 297f);
        Vector3 dir = cat != null ? cat.transform.forward : Vector3.left;
        dir.y = 0f;
        dir.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        Material balloonMat = GetBalloonMaterial();
        int mask = ~((1 << 9) | (1 << 10) | (1 << 17) | (1 << 18));

        var root = new GameObject("FlightTargets");
        Undo.RegisterCreatedObjectUndo(root, "Flight setup");
        Physics.SyncTransforms();

        var sb = new StringBuilder();
        for (int i = 0; i < 12; i++)
        {
            float dist = 300f + i * 130f;
            float lat = (i % 2 == 0 ? 1f : -1f) * (40f + (i % 3) * 45f);
            float alt = 40f + (i % 4) * 35f;
            Vector3 p = start + dir * dist + side * lat;
            p.y = alt;
            // 地形や建物があれば、その 40m 上に置く
            RaycastHit gh;
            if (Physics.Raycast(new Vector3(p.x, 400f, p.z), Vector3.down, out gh, 800f, mask, QueryTriggerInteraction.Ignore))
                p.y = Mathf.Max(p.y, gh.point.y + 40f);
            CreateBalloon(root.transform, "Target_" + (i + 1), p, balloonMat, fxMat, boom);
            sb.Append(" " + p.ToString("F0"));
        }
        log.AppendLine("標的を12個設置:" + sb);
    }

    static void CreateBalloon(Transform parent, string name, Vector3 pos, Material balloonMat, Material fxMat, AudioClip boom)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;

        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = Vector3.one * 8f;
        body.GetComponent<Renderer>().sharedMaterial = balloonMat;

        var basket = GameObject.CreatePrimitive(PrimitiveType.Cube);
        basket.name = "Basket";
        Object.DestroyImmediate(basket.GetComponent<Collider>());
        basket.transform.SetParent(body.transform, false);
        basket.transform.localPosition = new Vector3(0f, -0.65f, 0f);
        basket.transform.localScale = new Vector3(0.12f, 0.08f, 0.12f);

        var au = go.AddComponent<AudioSource>();
        au.playOnAwake = false;
        au.clip = boom;
        au.spatialBlend = 1f;
        au.minDistance = 20f;
        au.maxDistance = 1500f;
        au.volume = 1f;

        var ex = MakePS("Explosion", go.transform, fxMat);
        var main = ex.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 22f);
        main.startSize = new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.1f), new Color(1f, 0.85f, 0.3f));
        main.gravityModifier = -0.1f;
        main.maxParticles = 200;
        var em = ex.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });
        var shape = ex.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 2f;
        SetFade(ex, new Color(1f, 0.9f, 0.5f), new Color(0.6f, 0.15f, 0.05f));
        var sol = ex.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));

        var t = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(go);
        t.body = body;
        t.explosionFx = ex;
        t.explosionAudio = au;
        t.maxHp = 5;
        t.respawnTime = 20f;
        UdonSharpEditorUtility.CopyProxyToUdon(t);
    }
}
