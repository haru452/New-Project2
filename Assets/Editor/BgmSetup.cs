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

// BGM の設定
//  ・main room（艦橋）の中では「クラーゲ海」（びじえもん）を流す
//  ・艦橋と博物館に、BGM を消せるボタン（自分だけ）を付ける
// メニュー「Tools/BGM を設定する」。Backup_flight/run_bgm.flag があれば再読み込み時に一度だけ自動実行。
[InitializeOnLoad]
public static class BgmSetup
{
    const string Flag = "Backup_flight/run_bgm.flag";
    const string Clip = "Assets/My script/bridge/bridge_bgm_kurage.mp3";
    static bool retried;
    static BgmSetup() { EditorApplication.delayCall += Try; }
    static void Try()
    {
        if (!File.Exists(Flag)) return;
        if (EditorApplication.isPlaying) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Try; return; }
        File.Delete(Flag);
        Run();
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

    static TextMeshPro Label(Transform p, Vector3 lp, Quaternion lr, float size)
    {
        var g = new GameObject("BgmLabel");
        g.transform.SetParent(p, false); g.transform.localPosition = lp; g.transform.localRotation = lr;
        var t = g.AddComponent<TextMeshPro>();
        t.text = "BGM  ON"; t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.richText = true;
        t.color = new Color(1f, 0.88f, 0.6f); t.enableWordWrapping = false;
        t.rectTransform.sizeDelta = new Vector2(1.2f, 0.3f);
        return t;
    }

    static Material Mat(string name, Color c)
    {
        string p = "Assets/My script/bridge/Materials/" + name + ".mat";
        Directory.CreateDirectory("Assets/My script/bridge/Materials");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, p); }
        m.color = c; EditorUtility.SetDirty(m);
        return m;
    }

    // ボタン（小さな箱。触ると target._ToggleBgm）。forward の方から押す
    static GameObject Button(Transform parent, string name, Vector3 lp, Quaternion lr, UdonSharpBehaviour target)
    {
        var old = parent.Find(name); if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name; g.transform.SetParent(parent, false);
        g.transform.localPosition = lp; g.transform.localRotation = lr; g.transform.localScale = new Vector3(0.3f, 0.2f, 0.12f);
        g.GetComponent<MeshRenderer>().sharedMaterial = Mat("BgmButton", new Color(0.18f, 0.35f, 0.55f));
        Undo.RegisterCreatedObjectUndo(g, "Bgm");
        var bt = UdonSharpUndo.AddComponent<BgmToggle>(g);
        bt.target = target;
        UdonSharpEditorUtility.CopyProxyToUdon(bt);
        var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(bt);
        if (ub != null) { ub.interactText = "BGM を消す / 流す（自分だけ）"; ub.proximity = 2.5f; }
        return g;
    }

    [MenuItem("Tools/BGM を設定する")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("BgmSetup " + System.DateTime.Now);
        try
        {
            bool ok = EnsureProg("Assets/My script/bridge/AreaBgm.cs", "Assets/My script/bridge/AreaBgm.asset");
            ok &= EnsureProg("Assets/My script/bridge/BgmToggle.cs", "Assets/My script/bridge/BgmToggle.asset");
            if (!ok && !retried)
            {
                retried = true;
                AssetDatabase.Refresh();
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
                File.WriteAllText(Flag, "again"); EditorApplication.delayCall += Try;
                log.AppendLine("プログラムを作りました。もう一度実行します");
                return;
            }
            // 音の読み込み設定（長い曲なのでストリーミング）
            var ai = AssetImporter.GetAtPath(Clip) as AudioImporter;
            if (ai != null)
            {
                var st = ai.defaultSampleSettings;
                st.loadType = AudioClipLoadType.Streaming; st.compressionFormat = AudioCompressionFormat.Vorbis; st.quality = 0.5f;
                ai.defaultSampleSettings = st; ai.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Clip);
            if (clip == null) log.AppendLine("ERROR: クラーゲ海 の曲が無い");

            // ---- 艦橋 ----
            Transform bridge = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "Bridge" && t.parent != null && t.parent.name == "mainRoom") bridge = t;
            if (bridge == null) log.AppendLine("ERROR: mainRoom/Bridge が無い");
            else
            {
                var old = bridge.Find("BridgeBgm"); if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
                var g = new GameObject("BridgeBgm");
                Undo.RegisterCreatedObjectUndo(g, "Bgm");
                g.transform.SetParent(bridge, false);
                var au = g.AddComponent<AudioSource>();
                au.clip = clip; au.loop = true; au.playOnAwake = false; au.spatialBlend = 0f; au.volume = 0f; au.priority = 64;
                var zone = new GameObject("BgmZone").transform;
                zone.SetParent(g.transform, false);
                zone.localPosition = new Vector3(0f, 2.6f, 0f);
                zone.localScale = new Vector3(24.6f, 6f, 10.6f);   // 艦橋の部屋の大きさ
                var ab = UdonSharpUndo.AddComponent<AreaBgm>(g);
                ab.zone = zone; ab.bgm = au; ab.volume = 0.3f;
                // ボタン: 南の壁、操作方法の板とランキングの間
                var bt = Button(bridge, "BgmButton", new Vector3(-6.0f, 1.3f, -4.88f), Quaternion.identity, ab);
                var lab = Label(bridge, new Vector3(-6.0f, 1.62f, -4.86f), Quaternion.Euler(0f, 180f, 0f), 1.6f);   // 北から南の壁を見て読める向き
                lab.name = "BgmButtonLabel";
                ab.labels = new[] { lab };
                UdonSharpEditorUtility.CopyProxyToUdon(ab);
                log.AppendLine("艦橋: クラーゲ海 を流す（部屋の中だけ）。BGM のボタンを南の壁に置きました " + bt.transform.position.ToString("F1"));
            }

            // ---- 博物館: 音声ガイドの台の横にボタン ----
            var ma = Object.FindObjectOfType<MuseumAudio>(true);
            Transform kiosk = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "AudioGuide") kiosk = t;
            if (ma == null || kiosk == null) log.AppendLine("博物館: MuseumAudio か 音声ガイドの台が無い");
            else
            {
                Undo.RecordObject(ma, "Bgm");
                var oldL = kiosk.Find("BgmButtonLabel"); if (oldL != null) Undo.DestroyObjectImmediate(oldL.gameObject);
                var oldP = kiosk.Find("BgmPost"); if (oldP != null) Undo.DestroyObjectImmediate(oldP.gameObject);
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "BgmPost"; post.transform.SetParent(kiosk, false);
                post.transform.localPosition = new Vector3(0.95f, 0.5f, 0f); post.transform.localScale = new Vector3(0.4f, 1.0f, 0.4f);
                post.GetComponent<MeshRenderer>().sharedMaterial = Mat("BgmPost", new Color(0.13f, 0.12f, 0.11f));
                Undo.RegisterCreatedObjectUndo(post, "Bgm");
                Button(kiosk, "BgmButton", new Vector3(0.95f, 1.06f, 0f), Quaternion.identity, ma);
                // 台は入口の方（+z の反対 = 台の前）を向いているので、文字は台の前から読める向き
                var lab = Label(kiosk, new Vector3(0.95f, 1.3f, 0.21f), Quaternion.Euler(0f, 180f, 0f), 1.2f);
                ma.bgmLabels = new[] { lab };
                UdonSharpEditorUtility.CopyProxyToUdon(ma);
                log.AppendLine("博物館: 音声ガイドの台の横に BGM のボタン");
            }

            var sc = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(sc);
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(sc) ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION: " + e); }
        finally { File.WriteAllText("Backup_flight/bgm_report.txt", log.ToString()); }
    }
}
