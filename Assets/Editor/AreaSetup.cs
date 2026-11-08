using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;
using TMPro;

// 海を広げる（±21km）＋ 作戦範囲（±16km）から出た時の警告
//  ・作戦範囲 = Ocean の位置を中心に、たて・よこ ±16km の四角（元は ±8km → ±10km → アストラル島を動かしたので ±16km）
//  ・範囲外に出ると、ガウリア帝国司令部の声・文字と、戻る方向を指す矢印（AreaBoundary.cs）
// メニュー「Tools/作戦範囲と広い海を設定する」。1 回目はプログラムを作るだけのことがある（もう一度実行）
public static partial class FlightSetup
{
    const string AreaScriptPath = "Assets/My script/drone/AreaBoundary.cs";
    const string AreaAssetPath = "Assets/My script/drone/AreaBoundary.asset";
    const float OpAreaHalf = 16000f;  // 作戦範囲のたて・よこの半分（海は ±21km。勝利の部屋・歴史の間は中心から東へ 25km）
    const float AreaIgnoreBeyond = 22000f;   // これより遠い所（勝利の部屋・歴史の間）では範囲外の警告を出さない

    static readonly string[] AreaWarn =
    {
        "This is Gauria Imperial Command. You are outside the area of operations. Turn back at once.",
        "こちらガウリア帝国司令部。そっちは作戦範囲外の領域だぞ。直ちに引き返せ。",
        "这里是高里亚帝国司令部。那边是作战范围以外的区域。立即返回。",
    };
    static readonly string[] AreaRepeat =   // [言語 * 2 + 何番目]
    {
        "Pilot, you are still outside the area of operations. Get back here.",
        "Turn around. Follow the arrow back to the area of operations.",
        "まだ作戦範囲外だぞ。そっちじゃない、戻れ。",
        "引き返せ。矢印の方向へ戻れ。",
        "你仍在作战范围以外。不是那边，快回来。",
        "掉头。按箭头方向返回作战范围。",
    };
    static readonly string[] AreaBack =
    {
        "You are back in the area of operations. Continue your mission.",
        "作戦範囲内に戻ったな。任務を続行せよ。",
        "你已返回作战范围。继续执行任务。",
    };
    static readonly string[] AreaSpeaker = { "Gauria Imperial Command", "ガウリア帝国 司令部", "高里亚帝国 司令部" };

    // Backup_flight/run_area.flag があれば、コンパイル後に 1 回だけ自動で実行する（続けて夜の明かりも作り直す）
    [InitializeOnLoadMethod]
    static void AreaAuto()
    {
        EditorApplication.delayCall += () =>
        {
            const string flag = "Backup_flight/run_area.flag";
            if (!File.Exists(flag)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { AreaAuto(); return; }
            File.Delete(flag);
            RunArea();
            DayNightSetup.Run();   // 滑走路の明かり（アストラル空港の場所は GameMode から）
        };
    }

    [MenuItem("Tools/作戦範囲と広い海を設定する")]
    public static void RunArea()
    {
        var log = new StringBuilder();
        log.AppendLine("AreaSetup " + System.DateTime.Now);
        try { BuildArea(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/area_setup_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void BuildArea(StringBuilder log)
    {
        bool ready = EnsureProgram(AreaScriptPath, AreaAssetPath, log);
        ready &= EnsureProgram(VictoryScriptPath, VictoryAssetPath, log);
        ready &= EnsureProgram("Assets/My script/transport/DoorTeleport.cs", "Assets/My script/transport/DoorTeleport.asset", log);
        if (!ready)
        {
            log.AppendLine("プログラムを作りました。コンパイルが終わったら、もう一度メニューを実行してください");
            return;
        }
        var ocean = GameObject.Find("Ocean");
        if (ocean == null) { log.AppendLine("ERROR: Ocean が見つからない"); return; }

        // 1) 海を広げる
        var mf = ocean.GetComponent<MeshFilter>();
        Undo.RecordObject(mf, "Area setup");
        mf.sharedMesh = GetOceanMesh();
        log.AppendLine("海: " + mf.sharedMesh.name + "（半径 " + (mf.sharedMesh.bounds.extents.x / 1000f).ToString("F0") + "km）");

        // 2) 作戦範囲の警告
        var old = GameObject.Find("AreaBoundary");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject("AreaBoundary");
        Undo.RegisterCreatedObjectUndo(root, "Area setup");

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
        var ovMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/My script/drone/Subtitles/Font/SubtitleOverlay.mat");
        var winMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/My script/drone/Subtitles/Font/SubtitleWindow.mat");
        if (font == null) log.AppendLine("WARNING: 字幕のフォントが無い（先に「Tools/言語選択と字幕を設定する」を実行）");
        else
        {
            var chars = new StringBuilder();
            foreach (var a in new[] { AreaWarn, AreaRepeat, AreaBack, AreaSpeaker }) foreach (var t in a) chars.Append(t);
            font.TryAddCharacters(chars.ToString());
            foreach (var at in font.atlasTextures) if (at != null) EditorUtility.SetDirty(at);
            EditorUtility.SetDirty(font);
        }

        // 矢印（横向きと縦向きの 2 枚を十字に重ねる。どの角度から見ても見える）
        var arrowGo = new GameObject("Arrow");
        arrowGo.transform.SetParent(root.transform, false);
        arrowGo.AddComponent<MeshFilter>().sharedMesh = GetArrowMesh();
        var amr = arrowGo.AddComponent<MeshRenderer>();
        amr.sharedMaterial = ArrowMat();
        amr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        amr.receiveShadows = false;
        arrowGo.SetActive(false);

        // 文字（矢印の下）
        var msgGo = new GameObject("Message");
        msgGo.transform.SetParent(root.transform, false);
        if (winMat != null)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Window"; Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(msgGo.transform, false);
            q.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            q.transform.localScale = new Vector3(1.5f, 0.34f, 1f);
            q.GetComponent<MeshRenderer>().sharedMaterial = winMat;
        }
        var tg = new GameObject("Text");
        tg.transform.SetParent(msgGo.transform, false);
        var tmp = tg.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        if (ovMat != null) tmp.fontSharedMaterial = ovMat;
        tmp.fontSize = 0.4f; tmp.color = Color.white; tmp.richText = true;
        tmp.alignment = TextAlignmentOptions.Center; tmp.enableWordWrapping = true;
        tmp.rectTransform.sizeDelta = new Vector2(1.4f, 0.32f);
        tmp.text = "";
        msgGo.SetActive(false);

        // 無線の声（自分だけに聞こえる。立体音にしない）
        var au = root.AddComponent<AudioSource>();
        au.playOnAwake = false; au.spatialBlend = 0f; au.volume = 1f; au.loop = false;

        var ab = UdonSharpUndo.AddComponent<AreaBoundary>(root);
        Vector3 c = ocean.transform.position;
        ab.center = new Vector2(c.x, c.z);
        ab.halfSize = new Vector2(OpAreaHalf, OpAreaHalf);
        ab.ignoreBeyond = AreaIgnoreBeyond;
        ab.arrow = arrowGo.transform; ab.arrowGo = arrowGo;
        ab.msgText = tmp; ab.msgGo = msgGo; ab.msgTf = msgGo.transform;
        ab.radio = au;
        ab.warnClips = Clips(log, "gaur_area_warn1");
        ab.repeatClips = Clips(log, "gaur_area_return1", "gaur_area_return2");
        ab.backClips = Clips(log, "gaur_area_back1");
        ab.warnText = AreaWarn; ab.repeatText = AreaRepeat; ab.backText = AreaBack; ab.speaker = AreaSpeaker;
        ab.subtitles = Object.FindObjectOfType<Subtitles>(true);
        if (ab.subtitles == null) log.AppendLine("WARNING: Subtitles が無い（言語は English になる）");
        ab.voiceBus = Object.FindObjectOfType<VoiceBus>(true);   // 声が重ならないように（無ければ「Tools/音声が重ならないようにする」）
        if (ab.voiceBus == null) log.AppendLine("注意: VoiceBus が無い → 範囲外の声がほかの声と重なる（「Tools/音声が重ならないようにする」を実行）");
        UdonSharpEditorUtility.CopyProxyToUdon(ab);

        // アストラル空港の場所を GameMode に入れる（非戦闘地域・滑走路の明かりが使う）
        var gmA = Object.FindObjectOfType<GameMode>(true);
        var islA = GameObject.Find("Astral island(アストラル）");
        Transform apA = islA != null ? islA.transform.Find("AstralAirport") : null;
        if (gmA != null && apA != null)
        {
            Undo.RecordObject(gmA, "Area setup");
            gmA.astralAirport = apA;
            UdonSharpEditorUtility.CopyProxyToUdon(gmA);
            log.AppendLine("アストラル空港: " + apA.position.ToString("F0") + "（非戦闘地域 " + gmA.astralNoCombatRadius.ToString("F0") + "m）");
        }
        else log.AppendLine("ERROR: アストラル空港（Astral island の AstralAirport）か GameMode が見つからない → 非戦闘地域が働かない");

        // 空母の艦隊: 範囲の外に現れて、範囲内へ入ってくる（遊覧モードでは範囲内で止まる。WarShip_4.cs）
        int cv = 0;
        foreach (var w in Object.FindObjectsOfType<WarShip>(true))
        {
            if (!w.noRelocate || !w.roaming) continue;
            Undo.RecordObject(w, "Area setup");
            w.carrierApproach = true;
            w.opCenter = ab.center;
            w.opHalf = OpAreaHalf;
            w.approachMin = 2000f;   // 範囲の端から 2〜3.5km 外に現れる（海の端 21km より内側）
            w.approachMax = 3500f;
            UdonSharpEditorUtility.CopyProxyToUdon(w);
            if (w.target != null)
            {
                // 沈められたら復活しない（モードを切り替えると元に戻る）
                Undo.RecordObject(w.target, "Area setup");
                w.target.noRespawn = true;
                UdonSharpEditorUtility.CopyProxyToUdon(w.target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(w.target);
            }
            int ne = 0;
            if (w.escorts != null)
                foreach (var e in w.escorts)
                {
                    if (e == null || e.target == null) continue;
                    // 護衛の船も、沈められたら復活しない（モードを切り替えると元に戻る）
                    Undo.RecordObject(e.target, "Area setup");
                    e.target.noRespawn = true;
                    UdonSharpEditorUtility.CopyProxyToUdon(e.target);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(e.target);
                    ne++;
                }
            log.AppendLine("空母の護衛 " + ne + " 隻: 復活しない");
            PrefabUtility.RecordPrefabInstancePropertyModifications(w);
            log.AppendLine("空母: " + w.name + "（復活しない・艦隊の幅 " + w.pathMargin.ToString("F0") + "m。この幅の中に島が無いコースだけ使う）");
            cv++;
        }
        if (cv == 0) log.AppendLine("WARNING: 空母（noRelocate の船）が見つからない");
        ShipAreaSetup.Apply(ab, log);   // 空母の艦隊以外の船は、範囲の中だけを走る

        // 音量設定（無線）に入れる
        var vs = Object.FindObjectOfType<VolumeSettings>(true);
        if (vs != null)
        {
            var list = new List<AudioSource>();
            if (vs.radioSources != null) foreach (var s in vs.radioSources) if (s != null && s != au) list.Add(s);
            list.Add(au);
            Undo.RecordObject(vs, "Area setup");
            vs.radioSources = list.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(vs);
        }

        // 空母を倒した後の部屋（VictoryRoomSetup.cs）
        BuildVictoryRoom(c, log);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("作戦範囲: 中心 (" + c.x.ToString("F0") + ", " + c.z.ToString("F0") + ") たて・よこ ±" + OpAreaHalf + "m");
        log.AppendLine("x " + (c.x - OpAreaHalf).ToString("F0") + "〜" + (c.x + OpAreaHalf).ToString("F0") + " / z " + (c.z - OpAreaHalf).ToString("F0") + "〜" + (c.z + OpAreaHalf).ToString("F0"));
        log.AppendLine("シーンを保存してください");
    }

    static AudioClip[] Clips(StringBuilder log, params string[] names)
    {
        var l = new List<AudioClip>();
        foreach (var n in names)
        {
            var a = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/" + n + ".mp3");
            if (a == null) a = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/" + n + ".wav");
            if (a == null) log.AppendLine("音声が無い（文字だけで知らせる）: " + n); else l.Add(a);
        }
        return l.ToArray();
    }

    static Material ArrowMat()
    {
        string p = FxDir + "/AreaArrow.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        bool create = m == null;
        if (create) m = new Material(Shader.Find("FlightUI/UnlitTint"));
        m.SetColor("_Color", new Color(1f, 0.45f, 0.1f, 1f));
        m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);   // コックピットに隠れない
        m.renderQueue = 3095;
        if (create) AssetDatabase.CreateAsset(m, p); else EditorUtility.SetDirty(m);
        return m;
    }

    // 先が +Z の矢印。横向き（XZ）と縦向き（YZ）を十字に重ねる
    static Mesh GetArrowMesh()
    {
        string path = FxDir + "/AreaArrow.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        // 形（横幅 w, 長さ z）: 軸の四角 + 先の三角
        float L = 0.34f, headL = 0.14f, shaftW = 0.075f, headW = 0.2f;
        float z0 = -L * 0.5f, z1 = L * 0.5f - headL, z2 = L * 0.5f;
        Vector2[] shape =
        {
            new Vector2(-shaftW * 0.5f, z0), new Vector2(shaftW * 0.5f, z0), new Vector2(shaftW * 0.5f, z1), new Vector2(-shaftW * 0.5f, z1),
            new Vector2(-headW * 0.5f, z1), new Vector2(headW * 0.5f, z1), new Vector2(0f, z2),
        };
        int[] tri = { 0, 2, 1, 0, 3, 2, 4, 6, 5 };
        var v = new List<Vector3>(); var t = new List<int>();
        for (int k = 0; k < 2; k++)
        {
            int b = v.Count;
            foreach (var s in shape) v.Add(k == 0 ? new Vector3(s.x, 0f, s.y) : new Vector3(0f, s.x, s.y));
            foreach (var i in tri) t.Add(b + i);
        }
        var mesh = new Mesh { name = "AreaArrow" };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        Directory.CreateDirectory(FxDir);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
