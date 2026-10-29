using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
using TMPro;

// MuseumSetup.cs の続き（長いので分けたファイル。中身は同じクラス MuseumSetup）
public static partial class MuseumSetup
{
    static Material Mat(string name, Texture2D tex, Color col)
    {
        string p = MusDir + "/Materials/" + name + ".mat";
        Directory.CreateDirectory(MusDir + "/Materials");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null)
        {
            m = new Material(Shader.Find(tex != null ? "Unlit/Texture" : "Standard"));
            AssetDatabase.CreateAsset(m, p);
        }
        if (tex != null) { m.shader = Shader.Find("Unlit/Texture"); m.mainTexture = tex; }
        else { m.shader = Shader.Find("Standard"); m.color = col; m.SetFloat("_Glossiness", 0.25f); }
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Box(string name, Transform parent, Vector3 lp, Vector3 size, Material m, bool collider)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        g.transform.localScale = size;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    static GameObject Quad(string name, Transform parent, Vector3 lp, Vector2 size, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        g.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 表を stand の前（+z）に向ける
        g.transform.localScale = new Vector3(size.x, size.y, 1f);
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    // 展示台: 板（幅 w・高さ h、下端 bottom）＋ 黒い枠 ＋ 脚 2 本
    static Transform Stand(string name, Transform parent, Vector3 pos, Quaternion rot, Material face, float w, float h, float bottom, Material frame)
    {
        var st = new GameObject(name).transform;
        st.SetParent(parent, false);
        st.position = pos; st.rotation = rot;
        float cy = bottom + h * 0.5f;
        Box("Frame", st, new Vector3(0f, cy, -0.04f), new Vector3(w + 0.12f, h + 0.12f, 0.06f), frame, true);
        Quad("Face", st, new Vector3(0f, cy, -0.005f), new Vector2(w, h), face);
        for (int i = -1; i <= 1; i += 2)
            Box("Leg", st, new Vector3(i * (w * 0.5f - 0.1f), bottom * 0.5f, -0.04f), new Vector3(0.08f, bottom, 0.08f), frame, false);
        Box("Foot", st, new Vector3(0f, 0.03f, -0.04f), new Vector3(w * 0.8f, 0.06f, 0.5f), frame, false);
        return st;
    }

    static void SetupExhibits(Transform museum, Transform wh, StringBuilder log)
    {
        // 前に作った物（手で動かした位置・向きは使う）
        Transform old = museum.Find("Exhibits");
        bool keep = old != null;
        Vector3 keepPos = keep ? old.position : Vector3.zero;
        Quaternion keepRot = keep ? old.rotation : Quaternion.identity;
        if (keep) Undo.DestroyObjectImmediate(old.gameObject);

        Bounds lb = LocalBounds(wh);
        Transform door = museum.Find("ReturnDoor");
        Vector3 dl = door != null ? wh.InverseTransformPoint(door.position) : new Vector3(lb.center.x, lb.min.y, lb.min.z);
        float s = dl.z < lb.center.z ? 1f : -1f;            // 入口から奥へ向かう向き（倉庫の z）
        float floorY = dl.y;
        float cx = lb.center.x;

        var root = new GameObject("Exhibits");
        Undo.RegisterCreatedObjectUndo(root, "Museum setup");
        root.transform.SetParent(museum, false);
        // 根元 = 入口のドアの位置・倉庫の向き（奥 = +z）
        root.transform.position = wh.TransformPoint(new Vector3(cx, floorY, dl.z));
        root.transform.rotation = wh.rotation * Quaternion.Euler(0f, s > 0f ? 0f : 180f, 0f);
        Transform R = root.transform;

        Material frame = Mat("StandFrame", null, new Color(0.13f, 0.12f, 0.11f));
        float len = (s > 0f ? lb.max.z - dl.z : dl.z - lb.min.z);
        int n = PanelCount + 1;   // パネル 10 枚 + 地図
        float gap = Mathf.Min(5.6f, (len - 9f) / (n - 1));
        float side = Mathf.Min(3.6f, lb.size.x * 0.5f - 2.2f);
        int made = 0;
        for (int i = 0; i < n; i++)
        {
            float z = 6f + i * gap;
            float x = (i % 2 == 0 ? -1f : 1f) * side;
            // 通路の方を向き、少し入口側に傾ける
            Quaternion r = Quaternion.Euler(0f, (i % 2 == 0 ? 90f : -90f) + (i % 2 == 0 ? 25f : -25f), 0f);
            Vector3 p = new Vector3(x, 0f, z);
            if (i == 1)
            {
                var tex = Tex(MusDir + "/Panels/map.png", 2048);
                if (tex == null) { log.AppendLine("展示: map.png が無い"); continue; }
                var st = Stand("Map", R, R.TransformPoint(p), R.rotation * r, Mat("Map", tex, Color.white), 2.6f, 2.6f * tex.height / tex.width, 0.9f, frame);
                made++;
            }
            else
            {
                int no = i == 0 ? 1 : i;   // 0 → 1, 2 → 2 ... 10 → 10
                string tp = MusDir + "/Panels/panel_" + no.ToString("00") + ".png";
                var tex = Tex(tp, 2048);
                if (tex == null) { log.AppendLine("展示: " + tp + " が無い"); continue; }
                Stand("Panel_" + no.ToString("00"), R, R.TransformPoint(p), R.rotation * r, Mat("Panel_" + no.ToString("00"), tex, Color.white), 1.6f, 1.6f * 2880f / 2048f, 0.5f, frame);
                made++;
            }
        }
        log.AppendLine("展示: パネルと地図 " + made + " 枚（間隔 " + gap.ToString("F1") + "m）");

        // 「この建物について」（入口の近く。音声ガイドの台の反対側。入口の方を向く）
        var atex = Tex(MusDir + "/Panels/panel_about.png", 2048);
        if (atex != null)
        {
            Stand("About", R, R.TransformPoint(new Vector3(-side * 0.75f, 0f, 3.2f)), R.rotation * Quaternion.Euler(0f, 180f + 20f, 0f),
                Mat("Panel_About", atex, Color.white), 1.6f, 1.6f * 2880f / 2048f, 0.5f, frame);
            made++;
        }
        else log.AppendLine("展示: panel_about.png が無い");

        // 音（BGM・音声ガイド）
        var audioGo = new GameObject("MuseumAudio");
        audioGo.transform.SetParent(R, false);
        var bgm = audioGo.AddComponent<AudioSource>();
        bgm.clip = Clip(MusDir + "/shinkai.mp3", true);
        bgm.loop = true; bgm.playOnAwake = false; bgm.spatialBlend = 0f; bgm.volume = 0f; bgm.priority = 64;
        var gJa = new GameObject("GuideJa").AddComponent<AudioSource>();
        gJa.transform.SetParent(audioGo.transform, false);
        gJa.clip = Clip(MusDir + "/guide_ja.ogg", true);
        var gEn = new GameObject("GuideEn").AddComponent<AudioSource>();
        gEn.transform.SetParent(audioGo.transform, false);
        gEn.clip = Clip(MusDir + "/guide_en.ogg", true);
        foreach (var g in new[] { gJa, gEn }) { g.playOnAwake = false; g.loop = false; g.spatialBlend = 0f; g.priority = 32; }
        if (bgm.clip == null) log.AppendLine("展示: shinkai.mp3 が無い");
        if (gJa.clip == null || gEn.clip == null) log.AppendLine("展示: 音声ガイドの音が無い");

        // 倉庫の範囲（この中にいると BGM が流れる）
        var zone = new GameObject("MuseumZone").transform;
        zone.SetParent(R, true);
        zone.position = wh.TransformPoint(lb.center);
        zone.rotation = wh.rotation;
        zone.localScale = Vector3.one;
        Vector3 zs = lb.size + new Vector3(1f, 2f, 1f);
        Vector3 ps = zone.parent.lossyScale;
        zone.localScale = new Vector3(zs.x / ps.x, zs.y / ps.y, zs.z / ps.z);

        // 音声ガイドの台（入口の近く。入口の方を向く）
        var kiosk = new GameObject("AudioGuide").transform;
        kiosk.SetParent(R, false);
        kiosk.localPosition = new Vector3(side * 0.75f, 0f, 3.2f);
        kiosk.localRotation = Quaternion.Euler(0f, 180f - 20f, 0f);
        var signTex = Tex(MusDir + "/Panels/guide_sign.png", 1024);
        Box("Base", kiosk, new Vector3(0f, 0.5f, 0f), new Vector3(1.3f, 1.0f, 0.45f), frame, true);
        Box("SignFrame", kiosk, new Vector3(0f, 1.62f, -0.12f), new Vector3(1.32f, 0.9f, 0.05f), frame, false);
        if (signTex != null) Quad("Sign", kiosk, new Vector3(0f, 1.62f, -0.09f), new Vector2(1.2f, 0.8f), Mat("GuideSign", signTex, Color.white));
        var mats = new[] { Mat("GuideJa", null, new Color(0.6f, 0.16f, 0.14f)), Mat("GuideEn", null, new Color(0.14f, 0.28f, 0.56f)) };
        var onMat = Mat("GuideOn", null, new Color(0.2f, 1f, 0.3f));
        onMat.EnableKeyword("_EMISSION"); onMat.SetColor("_EmissionColor", new Color(0.2f, 1f, 0.3f) * 1.5f);
        var marks = new GameObject[2];
        var ma = UdonSharpUndo.AddComponent<MuseumAudio>(audioGo);
        for (int i = 0; i < 2; i++)
        {
            float bx = i == 0 ? -0.33f : 0.33f;
            var b = Box(i == 0 ? "ButtonJa" : "ButtonEn", kiosk, new Vector3(bx, 1.06f, 0.02f), new Vector3(0.42f, 0.12f, 0.3f), mats[i], true);
            marks[i] = Box("Playing", kiosk, new Vector3(bx, 1.15f, 0.12f), new Vector3(0.1f, 0.05f, 0.05f), onMat, false);
            marks[i].SetActive(false);
            var gb = UdonSharpUndo.AddComponent<MuseumGuideButton>(b);
            gb.museum = ma;
            gb.lang = i;
            UdonSharpEditorUtility.CopyProxyToUdon(gb);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(gb);
            if (ub != null) { ub.interactText = i == 0 ? "音声ガイド（日本語）再生／停止" : "Audio guide (English) play / stop"; ub.proximity = 2.5f; }
        }
        ma.zone = zone;
        ma.bgm = bgm;
        ma.guides = new[] { gJa, gEn };
        ma.playingMarks = marks;
        ma.volume = Object.FindObjectOfType<VolumeSettings>(true);
        UdonSharpEditorUtility.CopyProxyToUdon(ma);
        // 博物館の中では無線を聞こえなくする
        foreach (var gmode in Object.FindObjectsOfType<GameMode>(true))
        {
            Undo.RecordObject(gmode, "Museum setup");
            gmode.museum = ma;
            UdonSharpEditorUtility.CopyProxyToUdon(gmode);
        }

        SetupMemorial(R, len, frame, log);

        if (keep) { R.SetPositionAndRotation(keepPos, keepRot); log.AppendLine("展示: 前と同じ位置・向き"); }
        AssetDatabase.SaveAssets();
        log.AppendLine("展示: 音声ガイド台・BGM（倉庫の中で流れる）を置きました");
    }
    // ---------------- 弔いの箱（一番奥。入口のドアから一番遠い所） ----------------
    static void SetupMemorial(Transform R, float len, Material frame, StringBuilder log)
    {
        SetupMemorialAt(R, new Vector3(0f, 0f, len - 3.2f), 180f, MusDir + "/Panels/memorial.png", 1.6f, 1.2f, null, frame, log);
    }

    static void SetupMemorialAt(Transform R, Vector3 localPos, float yaw, string plaquePath, float pw, float ph, string labelPath, Material frame, StringBuilder log)
    {
        var mem = new GameObject("Memorial").transform;
        mem.SetParent(R, false);
        mem.localPosition = localPos;
        mem.localRotation = Quaternion.Euler(0f, yaw, 0f);   // 入口の方を向く

        // 碑（説明の板）
        float pb = labelPath != null ? 1.62f : 1.4f;
        var tex = Tex(plaquePath, 2048);
        if (tex != null) Stand("Plaque", mem, mem.TransformPoint(new Vector3(0f, 0f, -0.9f)), mem.rotation, Mat("Memorial_" + Path.GetFileNameWithoutExtension(plaquePath), tex, Color.white), pw, ph, pb, frame);
        if (labelPath != null)
        {
            var lt = Tex(labelPath, 1024);
            if (lt != null) Quad("CountLabel", mem, new Vector3(0f, 1.45f, -0.86f), new Vector2(1.4f, 0.19f), Mat("CountLabel", lt, Color.white));
        }
        // 自分が納めた数
        var ct = new GameObject("Count");
        ct.transform.SetParent(mem, false);
        ct.transform.localPosition = new Vector3(0f, 1.2f, -0.86f);
        ct.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var t = ct.AddComponent<TextMeshPro>();
        t.text = "0";
        t.fontSize = 2.2f;
        t.color = new Color(0.84f, 0.73f, 0.47f);
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.rectTransform.sizeDelta = new Vector2(1.4f, 0.4f);

        // 箱（上が開いている木の箱）
        Material wood = Mat("MemorialWood", null, new Color(0.30f, 0.19f, 0.11f));
        Material cloth = Mat("MemorialCloth", null, new Color(0.08f, 0.08f, 0.09f));
        var box = new GameObject("Box").transform;
        box.SetParent(mem, false);
        box.localPosition = new Vector3(0f, 0f, 0.3f);
        Box("Table", box, new Vector3(0f, 0.4f, 0f), new Vector3(1.1f, 0.8f, 0.7f), cloth, true);
        float bw = 0.7f, bd = 0.5f, bh = 0.4f, th = 0.03f, by = 0.8f;
        Box("Bottom", box, new Vector3(0f, by + th * 0.5f, 0f), new Vector3(bw, th, bd), wood, true);
        Box("WallF", box, new Vector3(0f, by + bh * 0.5f, bd * 0.5f), new Vector3(bw, bh, th), wood, true);
        Box("WallB", box, new Vector3(0f, by + bh * 0.5f, -bd * 0.5f), new Vector3(bw, bh, th), wood, true);
        Box("WallL", box, new Vector3(-bw * 0.5f, by + bh * 0.5f, 0f), new Vector3(th, bh, bd), wood, true);
        Box("WallR", box, new Vector3(bw * 0.5f, by + bh * 0.5f, 0f), new Vector3(th, bh, bd), wood, true);
        var inside = new GameObject("Inside").transform;
        inside.SetParent(box, false);
        inside.localPosition = new Vector3(0f, by + bh * 0.5f + 0.05f, 0f);
        inside.localScale = new Vector3(bw - 0.02f, bh + 0.1f, bd - 0.02f);

        var mbGo = box.gameObject;
        var au = mbGo.AddComponent<AudioSource>();
        au.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusDir + "/bell.wav");
        au.playOnAwake = false; au.spatialBlend = 1f; au.minDistance = 2f; au.maxDistance = 25f; au.volume = 0.7f;
        var mb = UdonSharpUndo.AddComponent<MemorialBox>(mbGo);
        mb.inside = inside;
        mb.joinLog = Object.FindObjectOfType<JoinLog>(true);
        t.gameObject.SetActive(false);   // 数は出さない（加護の仕組みに変えた）
        mb.bell = au;
        UdonSharpEditorUtility.CopyProxyToUdon(mb);

        // 折り鶴を置く台（箱の右）と折り鶴 5 羽
        var tray = Box("CraneTable", mem, new Vector3(1.0f, 0.45f, 0.3f), new Vector3(0.6f, 0.9f, 0.5f), cloth, true);
        Mesh cm = CraneMesh();
        var cols = new[] { new Color(0.85f, 0.15f, 0.15f), new Color(0.95f, 0.95f, 0.92f), new Color(0.9f, 0.75f, 0.3f), new Color(0.3f, 0.5f, 0.85f), new Color(0.95f, 0.6f, 0.7f) };
        for (int i = 0; i < 5; i++)
        {
            var c = new GameObject("PaperCrane_" + (i + 1));
            c.transform.SetParent(mem, false);
            c.transform.localPosition = new Vector3(0.82f + (i % 3) * 0.18f, 0.9f + 0.035f, 0.18f + (i / 3) * 0.22f);
            c.transform.localRotation = Quaternion.Euler(0f, -30f + i * 17f, 0f);
            c.AddComponent<MeshFilter>().sharedMesh = cm;
            c.AddComponent<MeshRenderer>().sharedMaterial = Mat("Crane" + i, null, cols[i]);
            var bc = c.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.03f, 0f);
            bc.size = new Vector3(0.16f, 0.07f, 0.14f);
            var rb = c.AddComponent<Rigidbody>();
            rb.mass = 0.05f; rb.drag = 2f; rb.angularDrag = 2f; rb.useGravity = true; rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var pk = c.AddComponent<VRC.SDK3.Components.VRCPickup>();
            pk.InteractionText = "折り鶴 / Paper crane";
            pk.UseText = "";
            pk.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.No;
            var pc = UdonSharpUndo.AddComponent<PaperCrane>(c);
            pc.box = mb;
            UdonSharpEditorUtility.CopyProxyToUdon(pc);
        }
        log.AppendLine("弔いの箱: 一番奥に置きました（折り鶴 5 羽、数は自分の合計）");
    }

    // 折り紙の鶴の形（両面）
    static Mesh CraneMesh()
    {
        string p = MusDir + "/PaperCraneMesh.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
        if (m == null) { m = new Mesh(); AssetDatabase.CreateAsset(m, p); }
        m.Clear();
        float s = 1.4f;
        var v = new System.Collections.Generic.List<Vector3>();
        var tri = new System.Collections.Generic.List<int>();
        System.Action<Vector3, Vector3, Vector3> T = (a, b, c) =>
        {
            int i = v.Count;
            v.Add(a * s); v.Add(b * s); v.Add(c * s);
            tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
            v.Add(a * s); v.Add(c * s); v.Add(b * s);   // 裏
            tri.Add(i + 3); tri.Add(i + 4); tri.Add(i + 5);
        };
        Vector3 F = new Vector3(0f, 0.02f, 0.035f), B = new Vector3(0f, 0.02f, -0.035f), D = new Vector3(0f, -0.005f, 0f);
        Vector3 L = new Vector3(-0.012f, 0.012f, 0f), Rt = new Vector3(0.012f, 0.012f, 0f);
        // 胴（ひし形の袋）
        T(F, L, D); T(L, B, D); T(F, D, Rt); T(D, B, Rt);
        // 翼
        T(F + Vector3.up * 0.002f, B + Vector3.up * 0.002f, new Vector3(-0.075f, 0.045f, -0.005f));
        T(B + Vector3.up * 0.002f, F + Vector3.up * 0.002f, new Vector3(0.075f, 0.045f, -0.005f));
        // 首と頭
        Vector3 nk = new Vector3(0f, 0.06f, 0.075f);
        T(new Vector3(-0.004f, 0.005f, 0.02f), new Vector3(0.004f, 0.005f, 0.02f), nk);
        T(nk, new Vector3(0f, 0.058f, 0.068f), new Vector3(0f, 0.052f, 0.09f));
        // 尾
        T(new Vector3(-0.004f, 0.005f, -0.02f), new Vector3(0.004f, 0.005f, -0.02f), new Vector3(0f, 0.065f, -0.08f));
        m.SetVertices(v);
        m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        EditorUtility.SetDirty(m);
        return m;
    }
}
