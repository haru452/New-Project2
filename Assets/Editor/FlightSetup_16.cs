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
    static ParticleSystem MakePS(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        return ps;
    }

    static void SetFade(ParticleSystem ps, Color from, Color to)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    static float BodyRadius(GameObject car)
    {
        Transform ct = car.transform;
        Vector3 ls = ct.lossyScale;
        float s = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        var sph = car.GetComponent<SphereCollider>();
        if (sph != null) return sph.radius * s + ct.TransformVector(sph.center).magnitude;
        var bx = car.GetComponent<BoxCollider>();
        if (bx != null) return Mathf.Max(bx.size.x, bx.size.y, bx.size.z) * 0.5f * s;
        return 1.5f;
    }

    static void SetWorldScaleOne(Transform t)
    {
        if (t.parent == null) { t.localScale = Vector3.one; return; }
        Vector3 ls = t.parent.lossyScale;
        t.localScale = new Vector3(1f / Mathf.Max(Mathf.Abs(ls.x), 1e-4f), 1f / Mathf.Max(Mathf.Abs(ls.y), 1e-4f), 1f / Mathf.Max(Mathf.Abs(ls.z), 1e-4f));
    }

    static Texture2D GetSoftTexture()
    {
        string path = FxDir + "/soft.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(FxDir);
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float dx = (x + 0.5f) / 32f - 1f;
            float dy = (y + 0.5f) / 32f - 1f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            a = a * a * (3f - 2f * a);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material GetFxMaterial(Texture2D tex)
    {
        string path = FxDir + "/FlightFx.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(FxDir);
        // VRChat は Built-in なので URP のシェーダーは使わない
        Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
        var m = new Material(sh);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        // 加算合成（光る弾・炎）
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        else m.mainTexture = tex;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material GetBalloonMaterial()
    {
        string path = FxDir + "/Balloon.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(FxDir);
        Shader sh = Shader.Find("Standard");
        var m = new Material(sh);
        Color red = new Color(0.85f, 0.1f, 0.08f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", red);
        if (m.HasProperty("_Color")) m.SetColor("_Color", red);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
    const string CatapultScriptPath = "Assets/My script/drone/Catapult.cs";
    const string CatapultAssetPath = "Assets/My script/drone/Catapult.asset";

    // true = すでにプログラムがあって使える / false = 今作った（次回設置）
    static bool EnsureCatapultProgram(StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(CatapultAssetPath) != null) return true;
        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(CatapultScriptPath);
        if (ms == null) { log.AppendLine("ERROR: Catapult.cs が見つからない"); return true; }
        var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        prog.sourceCsScript = ms;
        AssetDatabase.CreateAsset(prog, CatapultAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        log.AppendLine("Catapult.asset 作成・コンパイル");
        return false;
    }

    static void CreateCatapult(StringBuilder log)
    {
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        if (putOut == null || putOut.reportPoint == null) { log.AppendLine("ERROR: 機体の出現地点(PutOutDrone.reportPoint)が見つからない"); return; }
        GameObject sub = GameObject.Find("伊402_完全版");
        if (sub == null) { log.AppendLine("ERROR: 伊402_完全版 が見つからない"); return; }

        Vector3 start = putOut.reportPoint.transform.position;
        Bounds bd = new Bounds(sub.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in sub.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { bd = r.bounds; any = true; } else bd.Encapsulate(r.bounds);
        }

        // 艦の長い方向に沿って、出現地点に近い側の艦首（端）へ向けて射出
        Vector3 dir;
        float edgeDist;
        if (bd.size.x >= bd.size.z)
        {
            float sgn = start.x < bd.center.x ? -1f : 1f;
            dir = new Vector3(sgn, 0f, 0f);
            edgeDist = sgn < 0 ? start.x - bd.min.x : bd.max.x - start.x;
        }
        else
        {
            float sgn = start.z < bd.center.z ? -1f : 1f;
            dir = new Vector3(0f, 0f, sgn);
            edgeDist = sgn < 0 ? start.z - bd.min.z : bd.max.z - start.z;
        }
        float stroke = Mathf.Clamp(edgeDist - 1f, 8f, 40f);

        var go = new GameObject("Catapult");
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        go.transform.SetPositionAndRotation(start, Quaternion.LookRotation(dir, Vector3.up));
        go.layer = 17; // Walkthrough: プレイヤーには当たらず、機体には反応する
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(5f, 4f, 5f);

        var cat = UdonSharpEditor.UdonSharpUndo.AddComponent<Catapult>(go);
        cat.strokeLength = stroke;
        cat.launchSpeed = 40f;
        cat.holdTime = 2.5f;   // 「Catapult engaged. Launching.」を言い終わってから射出
        UdonSharpEditorUtility.CopyProxyToUdon(cat);
        PlaceCatapult(cat, log);
        return;

        // 見た目のレール（当たり判定なし）
        float deckY = start.y - 1f;
        foreach (var h in Physics.RaycastAll(start + Vector3.up * 3f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(sub.transform)) { deckY = Mathf.Max(h.point.y, -1000f); break; }
        }
        var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rail.name = "CatapultRail";
        Object.DestroyImmediate(rail.GetComponent<Collider>());
        rail.transform.SetParent(go.transform, true);
        rail.transform.position = new Vector3(start.x, deckY + 0.08f, start.z) + dir * (stroke * 0.5f);
        rail.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        rail.transform.localScale = new Vector3(0.5f, 0.15f, stroke);

        log.AppendLine("カタパルト設置: 位置 " + start + " 方向 " + dir + " 長さ " + stroke.ToString("F1") + "m 艦の大きさ " + bd.size + " 甲板高さ " + deckY.ToString("F2"));
    }

    // カタパルトを甲板の高さ（機体が甲板に乗った時の中心の高さ）に置き、
    // 検知範囲を出現地点の高さまで広げる。レールも甲板に合わせる
    static void PlaceCatapult(Catapult cat, StringBuilder log)
    {
        GameObject sub = GameObject.Find("伊402_完全版");
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        Transform t = cat.transform;
        Vector3 spawn = (putOut != null && putOut.reportPoint != null) ? putOut.reportPoint.transform.position : t.position;
        Vector3 dir = t.forward; dir.y = 0f; dir.Normalize();

        // 甲板の高さ
        float deckY = spawn.y - 1f;
        bool hitDeck = false;
        foreach (var h in Physics.RaycastAll(new Vector3(spawn.x, spawn.y + 3f, spawn.z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (sub != null && h.transform.IsChildOf(sub.transform) && h.point.y <= spawn.y + 0.1f && (!hitDeck || h.point.y > deckY)) { deckY = h.point.y; hitDeck = true; }
        }

        // 機体が甲板に乗った時の中心の高さ（機体コライダーの下端から中心まで）
        // 非表示の機体だと bounds が取れないので、コライダーの形から計算する
        // 格納庫から出てくる機体（プールの中）で一番背の高いものに合わせる
        float restH = 1.0f;
        var poolObjs = (putOut != null && putOut.dronePool != null) ? putOut.dronePool.Pool : null;
        foreach (var cc in Object.FindObjectsOfType<controller>(true))
        {
            if (cc.car == null) continue;
            if (poolObjs != null && System.Array.IndexOf(poolObjs, cc.car) < 0) continue;
            Transform ct = cc.car.transform;
            float h = -1f;
            var sph = cc.car.GetComponent<SphereCollider>();
            var bx = cc.car.GetComponent<BoxCollider>();
            if (sph != null)
            {
                Vector3 ls = ct.lossyScale;
                float r = sph.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                h = r - ct.TransformVector(sph.center).y;
            }
            else if (bx != null)
            {
                float hy = Mathf.Abs(ct.TransformVector(new Vector3(0f, bx.size.y * 0.5f, 0f)).y);
                h = hy - ct.TransformVector(bx.center).y;
            }
            if (h > restH) restH = h;
        }

        // 秒読みを 2.5 秒に（音声「Catapult engaged. Launching.」を言い終わってから射出）
        if (cat.holdTime < 2.5f)
        {
            Undo.RecordObject(cat, "Flight setup");
            cat.holdTime = 2.5f;
            UdonSharpEditorUtility.CopyProxyToUdon(cat);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cat);
        }

        // 射出速度を 40 → 45 m/s に（手で変えていたら触らない）
        if (cat.launchSpeed < 60f)   // 重くなって失速速度が約 50m/s になったので、射出は 62m/s（約 220km/h）
        {
            Undo.RecordObject(cat, "Flight setup");
            cat.launchSpeed = 62f;
            UdonSharpEditorUtility.CopyProxyToUdon(cat);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cat);
        }

        Undo.RecordObject(t, "Flight setup");
        t.position = new Vector3(spawn.x, deckY + restH + 0.05f, spawn.z);
        t.rotation = Quaternion.LookRotation(dir, Vector3.up);

        // 検知範囲: 甲板付近〜出現地点の少し上まで
        var box = cat.GetComponent<BoxCollider>();
        if (box != null)
        {
            Undo.RecordObject(box, "Flight setup");
            float bottom = -restH - 0.5f;
            float top = Mathf.Max(spawn.y - t.position.y, 0f) + 2f;
            box.isTrigger = true;
            box.size = new Vector3(6f, top - bottom, 6f);
            box.center = new Vector3(0f, (top + bottom) * 0.5f, 0f);
        }

        var rail = t.Find("CatapultRail");
        if (rail != null)
        {
            Undo.RecordObject(rail, "Flight setup");
            rail.position = new Vector3(t.position.x, deckY + 0.08f, t.position.z) + dir * (cat.strokeLength * 0.5f);
            rail.rotation = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 ps = t.lossyScale;
            rail.localScale = new Vector3(0.5f / ps.x, 0.15f / ps.y, cat.strokeLength / ps.z);
        }
        if (putOut != null)
        {
            Undo.RecordObject(putOut, "Flight setup");
            putOut.catapultPoint = t;
            putOut.blockRadius = 30f;
            UdonSharpEditorUtility.CopyProxyToUdon(putOut);
        }
        log.AppendLine("カタパルト配置: 開始点 " + t.position.ToString("F2") + " 甲板 " + deckY.ToString("F2") + (hitDeck ? "" : "(推定)") + " 機体の高さ " + restH.ToString("F2") + " 出現地点 " + spawn.ToString("F2") + " 検知範囲 " + (box != null ? box.size.ToString("F1") : "-") + " 長さ " + cat.strokeLength.ToString("F1") + "m");
    }

    static string GetPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
