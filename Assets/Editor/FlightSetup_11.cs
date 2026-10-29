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
    static Mesh GetOceanMesh()
    {
        string path = FxDir + "/OceanMesh.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        const int N = 250;
        const float R = 8000f;
        const float P = 2.6f;
        var verts = new Vector3[(N + 1) * (N + 1)];
        var normals = new Vector3[verts.Length];
        for (int j = 0; j <= N; j++)
        for (int i = 0; i <= N; i++)
        {
            float u = i / (float)N * 2f - 1f;
            float w = j / (float)N * 2f - 1f;
            float x = Mathf.Sign(u) * R * Mathf.Pow(Mathf.Abs(u), P);
            float z = Mathf.Sign(w) * R * Mathf.Pow(Mathf.Abs(w), P);
            verts[j * (N + 1) + i] = new Vector3(x, 0f, z);
            normals[j * (N + 1) + i] = Vector3.up;
        }
        var tris = new int[N * N * 6];
        int t = 0;
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            int a = j * (N + 1) + i;
            int b = a + 1;
            int c = a + (N + 1);
            int d = c + 1;
            tris[t++] = a; tris[t++] = c; tris[t++] = b;
            tris[t++] = b; tris[t++] = c; tris[t++] = d;
        }
        var mesh = new Mesh();
        mesh.name = "OceanMesh";
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(R * 2f, 40f, R * 2f));
        Directory.CreateDirectory(FxDir);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // ---------------- 描画距離・霞 ----------------
    const float ViewDistance = 12000f;     // カメラの描画距離（m）
    const float FogStart = 1500f;          // ここから霞み始める
    const float FogEnd = 11000f;           // ここで完全に霞む（描画距離より手前にして、切れ目を見せない）
    static readonly Color FogColor = new Color(0.70f, 0.79f, 0.88f, 1f);   // 地平線の空の色に近い薄い青

    static void SetupView(StringBuilder log)
    {
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>(true);
        Camera cam = null;
        if (desc != null && desc.ReferenceCamera != null) cam = desc.ReferenceCamera.GetComponent<Camera>();
        if (cam != null)
        {
            Undo.RecordObject(cam, "Flight setup");
            cam.farClipPlane = ViewDistance;
            log.AppendLine("描画距離: " + cam.name + " → " + ViewDistance + "m");
        }
        else log.AppendLine("WARNING: VRChat の基準カメラ(ReferenceCamera)が見つからない");

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;
        log.AppendLine("霞: " + FogStart + "〜" + FogEnd + "m");
    }

    // ---------------- 雲の層 ----------------
    const float CloudBottom = 430f;   // 下の雲の面の高さ（ここより上は下から見えない）
    const float CloudTop = 600f;      // 上の雲の面の高さ
    const float CloudSize = 12000f;   // 雲の層の広さ（m）
    static readonly Vector3 CloudCenter = new Vector3(150f, 0f, 75f);
    // もこもこの雲（大きな半透明の板）は重いので、軽さ優先で使わない。true にすると出る
    const bool CloudPuffs = false;
    const float ElevatorCoverSize = 2200f;   // エレベーターの上の丸い雲の直径（端は薄くなるので実際は約1.6km）

    static void CreateClouds(StringBuilder log)
    {
        GameObject old = GameObject.Find("CloudLayer");
        if (old != null) Undo.DestroyObjectImmediate(old);

        Shader layerSh = Shader.Find("FlightUI/CloudLayer");
        Shader puffSh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (layerSh == null || puffSh == null) { log.AppendLine("ERROR: 雲のシェーダーが見つからない"); return; }
        Texture2D noise = ImportTex(FxDir + "/cloud_noise.png", true);
        Texture2D puff = ImportTex(FxDir + "/cloud_puff.png", false);

        // 下の面: ところどころに雲（切れ間から空が見える）
        Material lower = LoadOrCreateMat("CloudLower", layerSh);
        lower.SetTexture("_MainTex", noise);
        lower.SetFloat("_Scale", 1800f);
        lower.SetFloat("_Scale2", 450f);
        lower.SetColor("_TopColor", new Color(0.97f, 0.98f, 1f, 1f));
        lower.SetColor("_BottomColor", new Color(0.70f, 0.73f, 0.78f, 1f));
        lower.SetFloat("_Density", 0.95f);
        lower.SetFloat("_MinAlpha", 0f);
        lower.SetFloat("_Coverage", 0.52f);
        lower.SetVector("_Speed", new Vector4(0.004f, 0.002f, 0f, 0f));
        lower.SetFloat("_EdgeFade", 0.3f);
        lower.SetFloat("_ZWriteOn", 0f);
        lower.SetFloat("_Round", 0f);
        lower.renderQueue = 2981;
        EditorUtility.SetDirty(lower);

        // 上の面: 切れ切れの雲。上から見たときの雲海
        Material upper = LoadOrCreateMat("CloudUpper", layerSh);
        upper.SetTexture("_MainTex", noise);
        upper.SetFloat("_Scale", 1300f);
        upper.SetFloat("_Scale2", 380f);
        upper.SetColor("_TopColor", Color.white);
        upper.SetColor("_BottomColor", new Color(0.72f, 0.75f, 0.8f, 1f));
        upper.SetFloat("_Density", 0.9f);
        upper.SetFloat("_MinAlpha", 0f);
        upper.SetFloat("_Coverage", 0.6f);
        upper.SetVector("_Speed", new Vector4(-0.003f, 0.0025f, 0f, 0f));
        upper.SetFloat("_EdgeFade", 0.3f);
        upper.SetFloat("_ZWriteOn", 0f);
        upper.SetFloat("_Round", 0f);
        upper.renderQueue = 2985;

        // 宇宙エレベーターの真上だけ、丸く厚い雲で覆って隠す
        Material cover = LoadOrCreateMat("CloudCover", layerSh);
        cover.SetTexture("_MainTex", noise);
        cover.SetFloat("_Scale", 900f);
        cover.SetFloat("_Scale2", 300f);
        cover.SetColor("_TopColor", new Color(0.97f, 0.98f, 1f, 1f));
        cover.SetColor("_BottomColor", new Color(0.66f, 0.69f, 0.75f, 1f));
        cover.SetFloat("_Density", 1f);
        cover.SetFloat("_MinAlpha", 0.97f);
        cover.SetFloat("_Coverage", 0.4f);
        cover.SetVector("_Speed", new Vector4(0.002f, 0.001f, 0f, 0f));
        cover.SetFloat("_EdgeFade", 0.45f);
        cover.SetFloat("_ZWriteOn", 1f);
        cover.SetFloat("_Round", 1f);
        cover.renderQueue = 2980;
        EditorUtility.SetDirty(cover);
        EditorUtility.SetDirty(upper);

        // 間のもこもこ（雲の中を飛んでいる感じを出す）
        Material puffMat = LoadOrCreateMat("CloudPuff", puffSh);
        puffMat.SetTexture("_MainTex", puff);
        puffMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        puffMat.renderQueue = 3000;
        EditorUtility.SetDirty(puffMat);

        var root = new GameObject("CloudLayer");
        Undo.RegisterCreatedObjectUndo(root, "Flight setup");
        root.transform.position = CloudCenter;

        CloudDeck(root.transform, "LowerDeck", CloudBottom, lower);
        CloudDeck(root.transform, "UpperDeck", CloudTop, upper);

        // エレベーターの位置（見つからなければ既定の位置）
        Vector3 elev = new Vector3(1801.6f, 0f, -148f);
        GameObject eg = GameObject.Find("spaceElavater");
        if (eg != null) elev = eg.transform.position;
        var coverQ = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(coverQ.GetComponent<Collider>());
        coverQ.SetActive(false);   // 宇宙エレベーターは消したので、上の丸い雲は使わない
        coverQ.name = "ElevatorCover";
        coverQ.transform.SetParent(root.transform, true);
        coverQ.transform.position = new Vector3(elev.x, CloudBottom - 10f, elev.z);
        coverQ.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        coverQ.transform.localScale = new Vector3(ElevatorCoverSize, ElevatorCoverSize, 1f);
        var cr = coverQ.GetComponent<MeshRenderer>();
        cr.sharedMaterial = cover;
        cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cr.receiveShadows = false;

        if (!CloudPuffs)
        {
            log.AppendLine("雲の層: ところどころ / 高さ " + CloudBottom + "〜" + CloudTop + "m / エレベーター上の丸い雲 " + elev.ToString("F0"));
            return;
        }

        var go = new GameObject("Puffs");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, (CloudBottom + CloudTop) * 0.5f + 20f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 12345;   // 全員同じ配置になるように固定
        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = 1000000f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(150f, 350f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.45f), new Color(0.9f, 0.92f, 0.95f, 0.75f));
        main.gravityModifier = 0f;
        main.maxParticles = 160;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 150) });
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(CloudSize * 0.66f, CloudTop - CloudBottom - 40f, CloudSize * 0.66f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = puffMat;
        r.sortMode = ParticleSystemSortMode.Distance;
        r.maxParticleSize = 10f;   // 近くで画面いっぱいになっても切れないように
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        log.AppendLine("雲の層: 高さ " + CloudBottom + "〜" + CloudTop + "m 広さ " + CloudSize + "m 中心 " + CloudCenter);
    }

    static void CloudDeck(Transform parent, string name, float y, Material m)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(parent, false);
        q.transform.localPosition = new Vector3(0f, y, 0f);
        q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 表が上を向く
        q.transform.localScale = new Vector3(CloudSize, CloudSize, 1f);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static Material LoadOrCreateMat(string name, Shader sh)
    {
        string path = FxDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != sh) m.shader = sh;
        return m;
    }

    static Texture2D ImportTex(string path, bool repeat)
    {
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            bool changed = false;
            var wm = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (imp.wrapMode != wm) { imp.wrapMode = wm; changed = true; }
            if (!repeat && !imp.alphaIsTransparency) { imp.alphaIsTransparency = true; changed = true; }
            if (changed) imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------- コックピット画面 ----------------
    static readonly Color CCyan = new Color(0.35f, 0.95f, 1f, 1f);
    static readonly Color CCyanDim = new Color(0.35f, 0.95f, 1f, 0.65f);
    static readonly Color CWhite = new Color(0.92f, 1f, 1f, 1f);
    static readonly Color CAmber = new Color(1f, 0.75f, 0.25f, 1f);

    // 画面全体の高さ調整（m）。上げたいときは大きく、下げたいときは小さく
    const float CockpitRaise = -0.12f;   // 身長 1.6m に固定したので、画面の基準（目の位置）を下げる
    const float CockpitScale = 0.85f;    // コックピットの画面・内装を全体に小さく（目の位置を中心に縮める）
    const float ChairScale = 0.8f;       // 椅子の見た目も小さく

    static Material s_consoleMat;
    static Material s_staticMat;   // パネルの赤い砂嵐（Panel() が各パネルに非表示で入れておく）
}
