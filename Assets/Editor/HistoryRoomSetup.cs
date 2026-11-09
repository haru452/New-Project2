using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using TMPro;

// 新しい歴史の間（赤く浸水した艦橋）を作る。メニュー「Tools/歴史の間を作る」。ログは Backup_flight/history_room_log.txt
//  ・main room の艦橋（mainRoom/Bridge）と博物館のドアをコピーして、海のずっと下（勝利の部屋の隣）に置く
//  ・コピーからは、動く仕組み（Udon など）を全部外す → 博物館のドアは見た目だけ（使えない）
//  ・カタパルトへのハッチ（HatchToSubmarine）だけ、main room（スポーン地点）へ戻る扉にする
//  ・画面に赤い砂嵐、床は少し浸水、明かりは赤く明滅
//  ・main room の艦橋を作り直した時は、もう一度このメニューを実行する
public static partial class FlightSetup
{
    const string HistScript = "Assets/My script/drone/HistoryRoom.cs";
    const string HistAsset = "Assets/My script/drone/HistoryRoom.asset";
    const string StaticPng = "Assets/My script/drone/FX/red_static.png";

    static readonly string[] HistoryMsg =
    {
        "<size=140%><color=#ff5040>AFTER THIS PATROL MISSION</color></size>\nThe fighting in this patrol mission would spark the naval war to come,\nand the Pelagos Islands would become a sea of fire.",
        "<size=140%><color=#ff5040>この哨戒任務の後</color></size>\nこの哨戒任務における戦闘が、のちの海戦を引き起こし、\nペラゴス諸島は戦火の海となる。",
        "<size=140%><color=#ff5040>这次巡逻任务之后</color></size>\n这次巡逻任务中的战斗引发了之后的海战，\n佩拉戈斯群岛化为战火之海。",
    };

    // 文字と曲だけ入れ直す（部屋は作り直さない）。Backup_flight/run_history_text.flag があれば、コンパイル後に 1 回だけ自動で
    [InitializeOnLoadMethod]
    static void HistoryTextAuto()
    {
        EditorApplication.delayCall += () =>
        {
            const string flag = "Backup_flight/run_history_text.flag";
            if (!File.Exists(flag)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { HistoryTextAuto(); return; }
            File.Delete(flag);
            RunHistoryText();
        };
    }

    // 曲: 「凪の郷愁」エルム凪（BOOTH https://booth.pm/ja/items/7691728・クレジット必須）。部屋の中にいる間だけ。自分にだけ聞こえる
    static void SetupHistoryBgm(HistoryRoom hr, StringBuilder log)
    {
        var au = hr.GetComponent<AudioSource>();
        if (au == null) au = Undo.AddComponent<AudioSource>(hr.gameObject);
        au.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/My script/drone/Audio/history_nagi_no_kyoushu.mp3");
        au.playOnAwake = false; au.loop = true; au.spatialBlend = 0f; au.volume = 0f; au.priority = 64;
        Undo.RecordObject(hr, "History bgm");
        hr.bgm = au;
        UdonSharpEditorUtility.CopyProxyToUdon(hr);
        log.AppendLine(au.clip != null ? "曲: 凪の郷愁（エルム凪）" : "注意: Assets/My script/drone/Audio/history_nagi_no_kyoushu.mp3 が無い（曲が流れない）");
    }

    [MenuItem("Tools/歴史の間の文字と曲を直す")]
    public static void RunHistoryText()
    {
        var log = new StringBuilder();
        log.AppendLine("HistoryText " + System.DateTime.Now);
        var hr = Object.FindObjectOfType<HistoryRoom>(true);
        if (hr == null) log.AppendLine("ERROR: 歴史の間（HistoryRoom）が無い");
        else
        {
            Undo.RecordObject(hr, "History text");
            hr.msg = HistoryMsg;
            UdonSharpEditorUtility.CopyProxyToUdon(hr);
            if (hr.text != null) { Undo.RecordObject(hr.text, "History text"); hr.text.text = HistoryMsg[1]; EditorUtility.SetDirty(hr.text); }
            SetupHistoryBgm(hr, log);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            log.AppendLine("歴史の間の文字と曲を入れ直しました。シーンを保存してください");
        }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/history_text_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    // 博物館の第11パネル（Panel_p11「あなたが選んだ道」）を、歴史の間の西の壁の前に置く
    //  ・部屋を作り直しても消えないように、部屋とは別の入れ物（HistoryRoomPanels）に入れる
    //  ・Backup_flight/run_place_p11.flag があれば、コンパイル後に 1 回だけ自動で
    [InitializeOnLoadMethod]
    static void PlaceP11Auto()
    {
        EditorApplication.delayCall += () =>
        {
            const string flag = "Backup_flight/run_place_p11.flag";
            if (!File.Exists(flag)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { PlaceP11Auto(); return; }
            File.Delete(flag);
            RunPlaceP11();
        };
    }

    [MenuItem("Tools/第11パネルを歴史の間に置く")]
    public static void RunPlaceP11()
    {
        var log = new StringBuilder();
        log.AppendLine("PlaceP11 " + System.DateTime.Now);
        try { PlaceP11(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/place_p11_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void PlaceP11(StringBuilder log)
    {
        var hr = Object.FindObjectOfType<HistoryRoom>(true);
        if (hr == null) { log.AppendLine("ERROR: 歴史の間（HistoryRoom）が無い"); return; }
        Transform p11 = null;
        foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "Panel_p11") { p11 = t; break; }
        if (p11 == null) { log.AppendLine("ERROR: Panel_p11 が見つからない"); return; }
        var holder = GameObject.Find("HistoryRoomPanels");
        if (holder == null)
        {
            holder = new GameObject("HistoryRoomPanels");
            Undo.RegisterCreatedObjectUndo(holder, "Place p11");
        }
        log.AppendLine("前の場所: " + p11.position.ToString("F1"));
        Undo.SetTransformParent(p11, holder.transform, "Place p11");
        Undo.RecordObject(p11, "Place p11");
        // 部屋の中心（root）から西へ 11.6m（西の壁は 12m）。パネルの表（ローカルの +Z）を東（部屋の中）へ向ける
        p11.position = hr.transform.position + new Vector3(-11.6f, 0f, 0f);
        p11.rotation = Quaternion.Euler(0f, 90f, 0f);
        p11.localScale = Vector3.one;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        log.AppendLine("Panel_p11 を歴史の間の西の壁の前に置きました: " + p11.position.ToString("F1") + "（モードで絵が替わる仕組みはそのまま）。シーンを保存してください");
    }

    [MenuItem("Tools/歴史の間を作る")]
    public static void RunHistoryRoomSetup()
    {
        var log = new StringBuilder();
        log.AppendLine("HistoryRoomSetup " + System.DateTime.Now);
        try
        {
            EnsureProgram(HistScript, HistAsset, log);
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(HistAsset) == null) { log.AppendLine("ERROR: HistoryRoom のプログラムを作れない"); return; }
            BuildHistoryRoom(log);
            var sc = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(sc);
            AssetDatabase.SaveAssets();
            log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(sc) ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION: " + e); }
        finally
        {
            Directory.CreateDirectory("Backup_flight");
            File.WriteAllText("Backup_flight/history_room_log.txt", log.ToString());
        }
    }

    static void BuildHistoryRoom(StringBuilder log)
    {
        GameObject mainRoom = null;
        foreach (var g in SceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == "mainRoom") { mainRoom = g; break; }
        Transform bridge = mainRoom != null ? mainRoom.transform.Find("Bridge") : null;
        if (bridge == null) { log.AppendLine("ERROR: mainRoom/Bridge が無い（先に「Tools/main room を艦橋にする」）"); return; }

        var old = GameObject.Find("HistoryRoom");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject("HistoryRoom");
        var parts = new System.Collections.Generic.List<GameObject>();   // 近くにいない時は消す見た目
        Undo.RegisterCreatedObjectUndo(root, "History room");
        var vr = GameObject.Find("VictoryRoom");
        Vector3 basePos = vr != null ? vr.transform.position + new Vector3(80f, 0f, 0f) : new Vector3(150f + FlightSetup.RoomFarX + 80f, FlightSetup.RoomY, 75f);
        root.transform.position = basePos;

        // 艦橋のコピー（艦橋から見た位置・向きのまま）
        var copy = Object.Instantiate(bridge.gameObject);
        copy.name = "Bridge";
        copy.transform.SetParent(root.transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = bridge.rotation;
        copy.transform.localScale = bridge.lossyScale;
        parts.Add(copy);
        // 博物館のドア（西の隔壁。見た目だけ）
        Transform md = mainRoom.transform.Find("MuseumDoor");
        if (md != null)
        {
            var mdc = Object.Instantiate(md.gameObject);
            mdc.name = "MuseumDoor（使えない）";
            mdc.transform.SetParent(root.transform, false);
            mdc.transform.localPosition = bridge.InverseTransformPoint(md.position);
            mdc.transform.localRotation = Quaternion.Inverse(bridge.rotation) * md.rotation;
            mdc.transform.localScale = md.lossyScale;
            mdc.SetActive(true);
            parts.Add(mdc);
            int s = StripLogic(mdc);
            log.AppendLine("博物館のドアをコピー（動く仕組みを " + s + " 個外した → 使えない）");
        }
        else log.AppendLine("注意: mainRoom/MuseumDoor が無い");
        int n = StripLogic(copy);
        log.AppendLine("艦橋をコピー（動く仕組みを " + n + " 個外した）: " + root.transform.position.ToString("F0"));
        Transform C = copy.transform;

        // 赤い明かり: コピーの明かりを全部赤く、少し暗く
        var lights = copy.GetComponentsInChildren<Light>(true);
        foreach (var l in lights) { l.color = new Color(1f, 0.12f, 0.08f); l.intensity *= 0.9f; }
        Material redLamp = SolidMat("HistoryRedLamp", new Color(0.9f, 0.08f, 0.05f));
        redLamp.EnableKeyword("_EMISSION"); redLamp.SetColor("_EmissionColor", new Color(1f, 0.08f, 0.04f) * 3f);
        int bulbs = 0;
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name == "Bulb" || r.gameObject.name == "DoorLamp") { r.sharedMaterial = redLamp; bulbs++; }
        // 赤い回転灯の代わりに、天井に赤いランプを足す
        for (int i = -1; i <= 1; i++)
        {
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.name = "AlarmLamp";
            lamp.transform.SetParent(C, false);
            lamp.transform.localPosition = new Vector3(i * 7.5f, 4.6f, 0f);
            lamp.transform.localScale = Vector3.one * 0.35f;
            lamp.GetComponent<MeshRenderer>().sharedMaterial = redLamp;
            var lg = new GameObject("AlarmLight");
            lg.transform.SetParent(lamp.transform.parent, false);
            lg.transform.localPosition = new Vector3(i * 7.5f, 4.2f, 0f);
            var li = lg.AddComponent<Light>();
            li.type = LightType.Point; li.range = 12f; li.intensity = 2.2f; li.color = new Color(1f, 0.1f, 0.06f); li.shadows = LightShadows.None;
            li.renderMode = LightRenderMode.ForcePixel;
        }
        lights = copy.GetComponentsInChildren<Light>(true);
        log.AppendLine("明かりを赤く: " + lights.Length + " 個（電球 " + bulbs + " 個）");

        // 床の浸水（床の 15cm 上に、暗い水の板）
        Material water = UnlitMat("HistoryWater", new Color(0.05f, 0.07f, 0.09f, 0.7f), true, 3000);
        var wq = Quad(C, "FloodWater", new Vector3(0f, 0.15f, 0f), new Vector2(23.8f, 9.8f), water);
        wq.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // 画面の赤い砂嵐（画面の上に重ねる。HistoryRoom がたまに表示して流す）
        Material stat = StaticMaterial();
        var arrive = new GameObject("ArrivePoint").transform;
        arrive.SetParent(C, false);
        arrive.localPosition = new Vector3(0f, 0.05f, -2f);   // 部屋の真ん中・北の画面の方を向く
        var screens = new System.Collections.Generic.List<Renderer>();
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject.name != "Screen" && r.gameObject.name != "Sweep") continue;
            var sq = Object.Instantiate(r.gameObject, r.transform.parent);
            sq.name = "RedStatic";
            Vector3 toRoom = (arrive.position - r.transform.position).normalized;
            sq.transform.position = r.transform.position + toRoom * 0.006f;
            var sr = sq.GetComponent<Renderer>();
            sr.sharedMaterial = stat;
            sr.enabled = false;
            foreach (var c in sq.GetComponents<Collider>()) Object.DestroyImmediate(c);
            screens.Add(sr);
        }
        log.AppendLine("赤い砂嵐の画面: " + screens.Count + " 枚");

        // 文字（部屋の真ん中の上。入ってきた人の正面）
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
        if (font != null)
        {
            var chars = new StringBuilder();
            foreach (var m in HistoryMsg) foreach (char ch in m) if (ch > 127 && chars.ToString().IndexOf(ch) < 0) chars.Append(ch);
            font.TryAddCharacters(chars.ToString());
            foreach (var at in font.atlasTextures) if (at != null) EditorUtility.SetDirty(at);
            EditorUtility.SetDirty(font);
        }
        Quad(C, "MessageBack", new Vector3(0f, 2.9f, 2.62f), new Vector2(7.6f, 1.9f), UnlitMat("HistoryMsgBack", new Color(0.02f, 0f, 0f, 0.8f), true, 3000));
        var msg = WallText(C, "Message", new Vector3(0f, 2.9f, 2.6f), new Vector2(7.2f, 1.7f), 0.9f, font);
        msg.text = HistoryMsg[1];

        // カタパルトへのハッチ → main room へ戻る扉
        Transform hatch = FindDeep(C, "HatchToSubmarine");
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
        Transform spawn = (desc != null && desc.spawns != null && desc.spawns.Length > 0) ? desc.spawns[0] : null;
        if (hatch != null && spawn != null)
        {
            hatch.name = "HatchToMainRoom";
            var lab = hatch.Find("Label");
            if (lab != null) { var t = lab.GetComponent<TextMeshPro>(); if (t != null) t.text = "MAIN ROOM"; }
            var dt = UdonSharpUndo.AddComponent<DoorTeleport>(hatch.gameObject);
            dt.target = spawn;
            UdonSharpEditorUtility.CopyProxyToUdon(dt);
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(dt);
            if (ub != null) { ub.interactText = "main room へ戻る / Return to main room"; ub.proximity = 3f; }
            log.AppendLine("ハッチ → main room へ戻る扉にしました");
        }
        else log.AppendLine("ERROR: ハッチ（HatchToSubmarine）かスポーン地点が見つからない → 部屋から出られない");

        // 仕組み
        var hr = UdonSharpUndo.AddComponent<HistoryRoom>(root);
        hr.museum = Object.FindObjectOfType<MuseumAudio>(true);
        hr.arrivePoint = arrive;
        hr.text = msg; hr.msg = HistoryMsg;
        hr.subtitles = Object.FindObjectOfType<Subtitles>(true);
        hr.staticScreens = screens.ToArray();
        hr.alarmLights = lights;
        hr.roomParts = parts.ToArray();
        UdonSharpEditorUtility.CopyProxyToUdon(hr);
        SetupHistoryBgm(hr, log);
        var jl = Object.FindObjectOfType<JoinLog>(true);
        if (jl != null) { Undo.RecordObject(jl, "History room"); jl.historyRoom = hr; UdonSharpEditorUtility.CopyProxyToUdon(jl); }
        else log.AppendLine("ERROR: JoinLog が無い（撃墜されても連れてこられない）");
        if (hr.museum == null) log.AppendLine("注意: MuseumAudio が無い");
        log.AppendLine("歴史の間: できました（" + root.transform.position.ToString("F0") + "）");
    }

    // コピーから動く仕組みを外す（Udon・VRChat の部品）。見た目と当たり判定は残す
    static int StripLogic(GameObject go)
    {
        int n = 0;
        foreach (var b in go.GetComponentsInChildren<UdonSharpBehaviour>(true)) { Object.DestroyImmediate(b); n++; }
        foreach (var c in go.GetComponentsInChildren<Component>(true))
        {
            if (c == null || c is Transform) continue;
            string ns = c.GetType().Namespace ?? "";
            if (c is VRC.Udon.UdonBehaviour || ns.StartsWith("VRC")) { Object.DestroyImmediate(c); n++; }
        }
        return n;
    }

    static Transform FindDeep(Transform t, string name)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
        return null;
    }

    // 赤い砂嵐の絵（128×128、赤と黒の点）と材質
    static Material StaticMaterial()
    {
        if (!File.Exists(StaticPng))
        {
            var tex = new Texture2D(128, 128, TextureFormat.RGB24, false);
            var rnd = new System.Random(7);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float v = (float)rnd.NextDouble();
                    v = v * v;
                    tex.SetPixel(x, y, new Color(0.25f + 0.75f * v, 0.05f * v, 0.04f * v));
                }
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(StaticPng));
            File.WriteAllBytes(StaticPng, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(StaticPng);
            var ti = AssetImporter.GetAtPath(StaticPng) as TextureImporter;
            if (ti != null) { ti.filterMode = FilterMode.Point; ti.wrapMode = TextureWrapMode.Repeat; ti.mipmapEnabled = false; ti.SaveAndReimport(); }
        }
        string mp = FxDir + "/HistoryStatic.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (m == null) { m = new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(m, mp); }
        m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(StaticPng);
        m.mainTextureScale = new Vector2(2f, 2f);
        EditorUtility.SetDirty(m);
        return m;
    }
}
