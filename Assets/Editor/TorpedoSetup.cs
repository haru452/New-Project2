using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

// 魚雷（controller_9.cs）の見た目を、人が乗る機体（R35 / Omega）ごとに 8 本作る
//  ・灰色の円柱（長さ 5m）と、海面に残す白い航跡（Wake の TrailRenderer）
//  ・TorpedoPool の下にまとめる。もう一度実行すると作り直す
// メニュー「Tools/魚雷を設定する」。ログは Backup_flight/torpedo_log.txt
//  Backup_flight/run_torpedo.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class TorpedoSetupAuto
{
    const string FlagPath = "Backup_flight/run_torpedo.flag";

    static TorpedoSetupAuto() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        FlightSetup.RunTorpedoSetup();
    }
}

public static partial class FlightSetup
{
    const int TorpedoCount = 8;

    [MenuItem("Tools/魚雷を設定する")]
    public static void RunTorpedoSetup()
    {
        var log = new StringBuilder();
        log.AppendLine("TorpedoSetup " + System.DateTime.Now);
        try { BuildTorpedoes(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/torpedo_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void BuildTorpedoes(StringBuilder log)
    {
        GameObject old = GameObject.Find("TorpedoPool");
        if (old != null) Undo.DestroyObjectImmediate(old);

        Material body = LoadOrCreateMat("Torpedo", Shader.Find("Standard"));
        body.SetColor("_Color", new Color(0.22f, 0.24f, 0.26f));
        body.SetFloat("_Glossiness", 0.35f);
        EditorUtility.SetDirty(body);
        Material wake = LoadOrCreateMat("TorpedoWake", Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        wake.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorUtility.SetDirty(wake);

        var pool = new GameObject("TorpedoPool");
        Undo.RegisterCreatedObjectUndo(pool, "Torpedo setup");
        int planes = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            Undo.RecordObject(c, "Torpedo setup");
            if (c.car == null || !IsR35(c.car))
            {
                c.torpedoes = new Transform[0];   // 人が乗らない機体（敵のドローン）には作らない
                UdonSharpEditorUtility.CopyProxyToUdon(c);
                PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                continue;
            }
            planes++;
            var arr = new Transform[TorpedoCount];
            for (int k = 0; k < arr.Length; k++)
            {
                var t = new GameObject("Torpedo_" + planes + "_" + (k + 1));
                t.transform.SetParent(pool.transform, false);
                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(cyl.GetComponent<Collider>());
                cyl.name = "Body";
                cyl.transform.SetParent(t.transform, false);
                cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 円柱の軸を前(+Z)に
                cyl.transform.localScale = new Vector3(0.53f, 2.5f, 0.53f);    // 長さ 5m・太さ 53cm
                var mr = cyl.GetComponent<MeshRenderer>();
                mr.sharedMaterial = body;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // 海面の白い航跡（魚雷は海面の 0.8m 下を走るので、その少し上に置く）
                var w = new GameObject("Wake");
                w.transform.SetParent(t.transform, false);
                w.transform.localPosition = new Vector3(0f, 0.9f, -2f);
                var tr = w.AddComponent<TrailRenderer>();
                tr.sharedMaterial = wake;
                tr.time = 10f;
                tr.minVertexDistance = 2f;
                tr.alignment = LineAlignment.TransformZ;
                w.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 帯を水平に寝かせる
                tr.widthCurve = AnimationCurve.Linear(0f, 1.2f, 1f, 5f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.92f, 1f), 1f) },
                          new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.emitting = false;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;

                t.SetActive(false);
                arr[k] = t.transform;
            }
            c.torpedoes = arr;
            c.maxTorpedoes = TorpedoCount;
            SetupMechAudio(c, log);
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            log.AppendLine(c.car.name + ": 魚雷 " + TorpedoCount + " 本");
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("魚雷を作った機体: " + planes + " 機。シーンを保存してください");
    }

    // 機械の音（魚雷を落とす・脚の出し入れ）: 機体に MechAudio を付けて、音を入れる
    static void SetupMechAudio(controller c, StringBuilder log)
    {
        Transform car = c.car.transform;
        Transform mt = car.Find("MechAudio");
        if (mt == null)
        {
            var g = new GameObject("MechAudio");
            Undo.RegisterCreatedObjectUndo(g, "Torpedo setup");
            g.transform.SetParent(car, false);
            g.transform.localPosition = new Vector3(0f, -1f, 0f);   // 胴体の下（魚雷と脚のあたり）
            mt = g.transform;
        }
        var au = mt.GetComponent<AudioSource>();
        if (au == null) au = Undo.AddComponent<AudioSource>(mt.gameObject);
        au.playOnAwake = false; au.loop = false;
        au.spatialBlend = 1f; au.minDistance = 6f; au.maxDistance = 250f;
        au.rolloffMode = AudioRolloffMode.Logarithmic; au.dopplerLevel = 0f; au.volume = 1f;
        c.mechAudio = au;
        c.torpedoDropClip = MechClip("torpedo_release", log);
        c.gearUpClip = MechClip("gear_up", log);
        c.gearDownClip = MechClip("gear_down", log);
    }

    static AudioClip MechClip(string name, StringBuilder log)
    {
        var a = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/" + name + ".wav");
        if (a == null) log.AppendLine("注意: " + AudioDir + "/" + name + ".wav が無い（音が鳴らない）");
        return a;
    }
}
