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
    static WorldRanking SetupWorldRanking(GameObject b, TextMeshPro tmp, string title, StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(RankAssetPath) == null)
        {
            EnsureProgram(RankScriptPath, RankAssetPath, log);
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(RankAssetPath) == null) { log.AppendLine("世界ランキング: プログラムを作れませんでした"); return null; }
        }
        var wr = UdonSharpEditor.UdonSharpUndo.AddComponent<WorldRanking>(b);
        wr.text = tmp;
        wr.title = title;
        string f = "Backup_flight/score_urls.txt";
        string[] lines = File.Exists(f) ? File.ReadAllLines(f) : new string[0];
        string baseUrl = lines.Length > 0 ? lines[0].Trim().TrimEnd('/') : "";
        string gist = lines.Length > 1 ? lines[1].Trim() : "";
        if (baseUrl.StartsWith("https://") && gist.StartsWith("https://"))
        {
            var urls = new VRC.SDKBase.VRCUrl[3001];
            for (int i = 0; i <= 3000; i++) urls[i] = new VRC.SDKBase.VRCUrl(baseUrl + "/s/" + i);
            wr.submitUrls = urls;
            var urlsNC = new VRC.SDKBase.VRCUrl[3001];
            for (int i = 0; i <= 3000; i++) urlsNC[i] = new VRC.SDKBase.VRCUrl(baseUrl + "/n/" + i);
            wr.submitUrlsNC = urlsNC;
            wr.readUrl = new VRC.SDKBase.VRCUrl(gist);
            log.AppendLine("世界ランキング: 点数の URL 3001 個（0〜3000 点）と、表示用の Gist を設定");
        }
        else
        {
            wr.submitUrls = new VRC.SDKBase.VRCUrl[0];
            wr.submitUrlsNC = new VRC.SDKBase.VRCUrl[0];
            wr.readUrl = null;
            log.AppendLine("世界ランキング: サーバーの URL がまだ無いので「準備中」と表示（Backup_flight/score_urls.txt に 2 行書くと設定されます）");
        }
        // 送るかどうかのスイッチと注意事項（掲示板の右・手の届く高さ）
        Transform root = b.transform.parent != null ? b.transform.parent : b.transform;
        Transform oldSw = root.Find("RankOptIn");
        if (oldSw != null) Undo.DestroyObjectImmediate(oldSw.gameObject);
        var sw = new GameObject("RankOptIn");
        Undo.RegisterCreatedObjectUndo(sw, "Flight setup");
        sw.transform.SetParent(root, false);
        sw.transform.localPosition = new Vector3(b.transform.localPosition.x + 1.3f + 0.75f, 0f, 0f);
        Material onM = UnlitMat("RankOptOn", new Color(0.2f, 0.85f, 0.45f, 1f), false, 2000);
        Material offM = UnlitMat("RankOptOff", new Color(0.35f, 0.38f, 0.42f, 1f), false, 2000);
        var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 当たり判定付き（押せる）
        btn.name = "Button";
        btn.transform.SetParent(sw.transform, false);
        btn.transform.localPosition = new Vector3(2.75f, 1.15f, 0f);
        btn.transform.localScale = new Vector3(0.55f, 0.3f, 0.15f);
        btn.GetComponent<MeshRenderer>().sharedMaterial = offM;
        var lab = new GameObject("Label");
        lab.transform.SetParent(sw.transform, false);
        lab.transform.localPosition = new Vector3(2.75f, 1.48f, -0.02f);
        var lt = lab.AddComponent<TextMeshPro>();
        lt.fontSize = 0.9f; lt.alignment = TextAlignmentOptions.Center; lt.enableWordWrapping = false; lt.richText = true;
        lt.rectTransform.sizeDelta = new Vector2(1.2f, 0.3f);
        lt.text = "SEND MY BEST: OFF";
        // 注意事項の板（日本語・英語・中国語を横に並べ、その右にスイッチ）
        string[] langs = { "ja", "en", "zh" };
        for (int li = 0; li < langs.Length; li++)
        {
            string np = "Assets/My script/drone/Rank/rank_notice_" + langs[li] + ".png";
            AssetDatabase.ImportAsset(np);
            var ntex = AssetDatabase.LoadAssetAtPath<Texture2D>(np);
            if (ntex == null) { log.AppendLine("世界ランキング: " + np + " が無い"); continue; }
            string mp = "Assets/My script/drone/Rank/RankNotice_" + langs[li] + ".mat";
            var nm = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (nm == null) { nm = new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(nm, mp); }
            nm.mainTexture = ntex; EditorUtility.SetDirty(nm);
            float nh = 0.9f * ntex.height / ntex.width;
            Quad(sw.transform, "Notice_" + langs[li], new Vector3(li * 0.95f, 1.0f + nh * 0.5f, -0.01f), new Vector2(0.9f, nh), nm);
        }
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/My script/drone/RankOptIn.asset") == null)
            EnsureProgram("Assets/My script/drone/RankOptIn.cs", "Assets/My script/drone/RankOptIn.asset", log);
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/My script/drone/RankOptIn.asset") != null)
        {
            var ro = UdonSharpEditor.UdonSharpUndo.AddComponent<RankOptIn>(btn);
            ro.ranking = wr;
            UdonSharpEditorUtility.CopyProxyToUdon(ro);
        }
        // 国名なしで参加する赤いスイッチ（送るスイッチの右）
        Material redOn = UnlitMat("RankCountryOn", new Color(0.95f, 0.18f, 0.16f, 1f), false, 2000);
        Material redOff = UnlitMat("RankCountryOff", new Color(0.45f, 0.14f, 0.13f, 1f), false, 2000);
        var btn2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        btn2.name = "CountryButton";
        btn2.transform.SetParent(sw.transform, false);
        btn2.transform.localPosition = new Vector3(4.05f, 1.15f, 0f);
        btn2.transform.localScale = new Vector3(0.55f, 0.3f, 0.15f);
        btn2.GetComponent<MeshRenderer>().sharedMaterial = redOff;
        var lab2 = new GameObject("CountryLabel");
        lab2.transform.SetParent(sw.transform, false);
        lab2.transform.localPosition = new Vector3(4.05f, 1.48f, -0.02f);
        var lt2 = lab2.AddComponent<TextMeshPro>();
        lt2.fontSize = 0.9f; lt2.alignment = TextAlignmentOptions.Center; lt2.enableWordWrapping = false; lt2.richText = true;
        lt2.rectTransform.sizeDelta = new Vector2(1.2f, 0.3f);
        lt2.text = "HIDE MY COUNTRY: OFF";
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/My script/drone/RankOptIn.asset") != null)
        {
            var ro2 = UdonSharpEditor.UdonSharpUndo.AddComponent<RankOptIn>(btn2);
            ro2.ranking = wr;
            ro2.countrySwitch = true;
            UdonSharpEditorUtility.CopyProxyToUdon(ro2);
        }
        wr.countryLabel = lt2;
        wr.countryRenderer = btn2.GetComponent<MeshRenderer>();
        wr.redOnMat = redOn; wr.redOffMat = redOff;
        wr.switchLabel = lt;
        wr.switchRenderer = btn.GetComponent<MeshRenderer>();
        wr.onMat = onM; wr.offMat = offM;
        log.AppendLine("世界ランキング: 送るかどうかのスイッチ（最初はオフ・プレイヤーごと）と注意事項の板を、掲示板の右に置きました");
        UdonSharpEditorUtility.CopyProxyToUdon(wr);
        return wr;
    }

    static void SetupBoards(StringBuilder log)
    {
        JoinLog jl = Object.FindObjectOfType<JoinLog>(true);
        if (jl == null) { log.AppendLine("ランキング: JoinLog が無い"); return; }
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>(true);
        Transform sp = (desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null) ? desc.spawns[0] : (desc != null ? desc.transform : null);
        if (sp == null) { log.AppendLine("ランキング: 出現地点が見つからない"); return; }

        GameObject old = GameObject.Find("Rankings");
        Vector3 keepPos = Vector3.zero; Quaternion keepRot = Quaternion.identity; bool keep = false;
        if (old != null) { keepPos = old.transform.position; keepRot = old.transform.rotation; keep = true; Undo.DestroyObjectImmediate(old); }   // 手で動かした位置はそのまま
        var root = new GameObject("Rankings");
        Undo.RegisterCreatedObjectUndo(root, "Flight setup");
        Vector3 f = sp.forward; f.y = 0f;
        if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
        f.Normalize();
        // mainRoom（最初にいる部屋）の中に置く: 出現地点から前を見て、壁があればその壁の手前に掛ける
        GameObject mainRoom = GameObject.Find("mainRoom");
        if (mainRoom != null) root.transform.SetParent(mainRoom.transform, true);
        if (keep) root.transform.SetPositionAndRotation(keepPos, keepRot);
        else
        {
            Vector3 eye = sp.position + Vector3.up * 1.5f;
            float dist = 6f;
            RaycastHit wh;
            if (Physics.Raycast(eye, f, out wh, 30f, ~((1 << 9) | (1 << 10) | (1 << 18)), QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(wh.distance - 0.15f, 2.5f);
            root.transform.SetPositionAndRotation(sp.position + f * dist, Quaternion.LookRotation(f, Vector3.up));   // 出現地点の方を向く
            log.AppendLine("ランキング: 出現地点 " + sp.position.ToString("F1") + " から前へ " + dist.ToString("F1") + "m" + (dist < 6f || wh.collider != null ? "（壁 " + (wh.collider != null ? wh.collider.name : "-") + " の手前）" : ""));
        }
        if (mainRoom != null)
        {
            Bounds rb = new Bounds(mainRoom.transform.position, Vector3.zero);
            bool anyR = false;
            foreach (var r in mainRoom.GetComponentsInChildren<Renderer>(true)) { if (!anyR) { rb = r.bounds; anyR = true; } else rb.Encapsulate(r.bounds); }
            log.AppendLine("ランキング: mainRoom の中に配置 / 部屋の範囲 " + rb.min.ToString("F0") + "〜" + rb.max.ToString("F0") + (anyR && rb.Contains(root.transform.position + Vector3.up) ? "（部屋の中）" : "（※部屋の外かも。手で動かしてください）"));
        }
        else log.AppendLine("ランキング: mainRoom が見つからないので出現地点の前に配置");

        Material back = LoadOrCreateMat("RankingBoard", Shader.Find("Standard"));
        back.SetColor("_Color", new Color(0.03f, 0.06f, 0.1f));
        back.SetFloat("_Glossiness", 0.3f);
        EditorUtility.SetDirty(back);
        Material edge = UnlitMat("RankingEdge", new Color(0.35f, 0.95f, 1f, 1f), false, 2000);

        // 2 × 2 に並べる: 上 = 今回（ワールドにいる間の最高）、下 = 歴代（自己ベスト）。左 = 1 人用、右 = 2 人用
        // 左 = 1 人用（上 = 今回、下 = 歴代）、右上 = 世界ランキング（全インスタンス共通・上位 5）。2 人用はなくした
        string[] titles = { "SOLO  -  THIS SESSION", "WORLD RANKING  -  TOP 5", "SOLO  -  ALL-TIME", "" };
        const float W = 2.6f, H = 1.7f, GAP = 0.25f;
        for (int k = 0; k < 3; k++)
        {
            int col = k % 2, row = k / 2;
            var b = new GameObject(k == 0 ? "SoloBoard" : (k == 1 ? "WorldBoard" : "SoloAllTime"));
            b.transform.SetParent(root.transform, false);
            b.transform.localPosition = new Vector3((col == 0 ? -1f : 1f) * (W + GAP) * 0.5f, 1.2f + (row == 0 ? H + GAP : 0f) + H * 0.5f, 0f);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            plate.transform.SetParent(b.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            plate.transform.localScale = new Vector3(W, H, 0.08f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = back;
            // 枠
            Quad(b.transform, "EdgeT", new Vector3(0f, H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
            Quad(b.transform, "EdgeB", new Vector3(0f, -H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
            Quad(b.transform, "EdgeL", new Vector3(-W * 0.5f, 0f, -0.001f), new Vector2(0.025f, H), edge);
            Quad(b.transform, "EdgeR", new Vector3(W * 0.5f, 0f, -0.001f), new Vector2(0.025f, H), edge);
            // 列の見出し
            var hd = new GameObject("Header");
            hd.transform.SetParent(b.transform, false);
            hd.transform.localPosition = new Vector3(0f, H * 0.5f - 0.42f, -0.01f);
            var ht = hd.AddComponent<TextMeshPro>();
            ht.text = k == 1 ? "" : "<pos=68%><color=#ff6a5a>DRONE</color><pos=82%><color=#ffa030>SHIP</color>";
            ht.fontSize = 0.9f;
            ht.alignment = TextAlignmentOptions.TopLeft;
            ht.enableWordWrapping = false;
            ht.richText = true;
            ht.rectTransform.sizeDelta = new Vector2(W - 0.2f, 0.2f);
            // 本文
            var tx = new GameObject("Text");
            tx.transform.SetParent(b.transform, false);
            tx.transform.localPosition = new Vector3(0f, -0.04f, -0.01f);
            var tmp = tx.AddComponent<TextMeshPro>();
            tmp.fontSize = 1.0f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = false;
            tmp.richText = true;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.rectTransform.sizeDelta = new Vector2(W - 0.2f, H - 0.12f);
            tmp.text = titles[k];

            if (k == 1)
            {
                Undo.RecordObject(jl, "Flight setup");
                jl.duoBoard = null;
                jl.worldRanking = SetupWorldRanking(b, tmp, titles[k], log);
            }
            else if (k == 0)
            {
                var lb = UdonSharpEditor.UdonSharpUndo.AddComponent<Leaderboard>(b);
                lb.text = tmp;
                lb.title = titles[k];
                UdonSharpEditorUtility.CopyProxyToUdon(lb);
                Undo.RecordObject(jl, "Flight setup");
                jl.soloBoard = lb;
            }
            else
            {
                var ab = UdonSharpEditor.UdonSharpUndo.AddComponent<AllTimeBoard>(b);
                ab.text = tmp;
                ab.title = titles[k];
                ab.duo = col == 1;
                UdonSharpEditorUtility.CopyProxyToUdon(ab);
            }
        }
        UdonSharpEditorUtility.CopyProxyToUdon(jl);

        // ハード / 普通モードの切り替えボタン（掲示板の左。全員共通）
        var btnRoot = new GameObject("ModeButton");
        btnRoot.transform.SetParent(root.transform, false);
        btnRoot.transform.localPosition = new Vector3(-(W + GAP) - 1.0f, 0f, 0f);
        var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(post.GetComponent<Collider>());
        post.name = "Post";
        post.transform.SetParent(btnRoot.transform, false);
        post.transform.localPosition = new Vector3(0f, 0.55f, 0.1f);
        post.transform.localScale = new Vector3(0.25f, 1.1f, 0.25f);
        post.GetComponent<MeshRenderer>().sharedMaterial = back;
        var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 当たり判定付き（押せる）
        btn.name = "Button";
        btn.transform.SetParent(btnRoot.transform, false);
        btn.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        btn.transform.localScale = new Vector3(0.9f, 0.45f, 0.2f);
        Material hardM = UnlitMat("ModeHard", new Color(0.9f, 0.15f, 0.12f, 1f), false, 2000);
        Material normM = UnlitMat("ModeNormal", new Color(0.2f, 0.85f, 0.4f, 1f), false, 2000);
        var btnR = btn.GetComponent<MeshRenderer>();
        btnR.sharedMaterial = normM;
        var lab = new GameObject("Label");
        lab.transform.SetParent(btnRoot.transform, false);
        lab.transform.localPosition = new Vector3(0f, 1.85f, -0.02f);
        var labT = lab.AddComponent<TextMeshPro>();
        labT.fontSize = 1.1f;
        labT.alignment = TextAlignmentOptions.Center;
        labT.enableWordWrapping = false;
        labT.richText = true;
        labT.rectTransform.sizeDelta = new Vector2(2.2f, 0.8f);
        labT.text = "NORMAL MODE";
        var gm = UdonSharpEditor.UdonSharpUndo.AddComponent<GameMode>(btn);
        gm.label = labT;
        gm.lamp = btnR;
        gm.hardMat = hardM;
        gm.normalMat = normM;
        gm.sightseeingMat = UnlitMat("ModeSightseeing", new Color(0.2f, 0.55f, 1f, 1f), false, 2000);   // 遊覧モードは青
        var gmPlanes = new System.Collections.Generic.List<controller>();
        foreach (var c in Object.FindObjectsOfType<controller>(true)) if (c.car != null && IsR35(c.car)) gmPlanes.Add(c);
        gm.planes = gmPlanes.ToArray();   // 誰かが飛んでいる間は切り替えできない
        // 「偵察任務」の無線をワールドの全員に流すスピーカー（どこにいても同じ大きさ）
        var gmAu = btn.GetComponent<AudioSource>();
        if (gmAu == null) gmAu = btn.AddComponent<AudioSource>();
        gmAu.playOnAwake = false;
        gmAu.spatialBlend = 0f;
        gmAu.volume = 1f;
        gm.radio = gmAu;
        gm.reconClips = new[] { LoadClip("gaur_recon1.wav"), LoadClip("gaur_recon2.wav") };
        gm.fleetClips = new[] { LoadClip("gaur_fleet1.wav"), LoadClip("gaur_fleet2.wav") };
        gm.fleetAnnounceAt = 60f;   // 戦闘が始まって 1 分後に「艦隊が向かってきている」と流す（2 分後に空母が現れる）
        UdonSharpEditorUtility.CopyProxyToUdon(gm);
        UdonBehaviour gmub = UdonSharpEditorUtility.GetBackingUdonBehaviour(gm);
        if (gmub != null) { gmub.interactText = "モード切り替え"; gmub.proximity = 3f; }
        // 機体と船に知らせる
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            Undo.RecordObject(c, "Flight setup");
            c.gameMode = gm;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
        {
            Undo.RecordObject(ws, "Flight setup");
            ws.gameMode = gm;
            UdonSharpEditorUtility.CopyProxyToUdon(ws);
        }
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true))
        {
            Undo.RecordObject(ed, "Flight setup");
            ed.gameMode = gm;
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
        }
        log.AppendLine("ランキング: 掲示板 4 枚とモード切り替えボタンを " + root.transform.position.ToString("F1") + " に設置");
    }

    // ---------------- プレイヤーは機体をすり抜ける ----------------
    // VRChat の Walkthrough レイヤー(17)は「プレイヤーとだけ当たらない」。海・島・弾・ミサイルとは今まで通り当たる
    static void SetupWalkthrough(StringBuilder log)
    {
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            foreach (var col in c.car.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;   // クリックで乗る当たり判定はそのまま
                if (col.gameObject.layer == 17) continue;
                Undo.RecordObject(col.gameObject, "Flight setup");
                col.gameObject.layer = 17;
                PrefabUtility.RecordPrefabInstancePropertyModifications(col.gameObject);
                n++;
            }
        }
        log.AppendLine("すり抜け: " + n + " 個の当たり判定を Walkthrough レイヤーに");
    }
    const string CloakScriptPath = "Assets/My script/drone/StationCloak.cs";
    const string CloakAssetPath = "Assets/My script/drone/StationCloak.asset";
    const float CloakRadius = 350f;
    const float CloakHideDistance = 2000f;   // これより遠いと高さに関係なく見えない

    static void RemoveElevator(StringBuilder log)
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (var nm in new[] { "elevater_ex", "MainToElavater", "StationCloak", "spaceElavater" })
        {
            GameObject g;
            int guard = 0;
            while ((g = FindAnyByName(nm)) != null && guard++ < 10) { Undo.DestroyObjectImmediate(g); names.Add(nm); }
        }
        // エレベーター用のスクリプトが付いた物（どこかに残っていれば）
        foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn == "RuningElavater" || tn == "DownElavater" || tn == "StationCloak") { names.Add(mb.gameObject.name); Undo.DestroyObjectImmediate(mb.gameObject); }
        }
        // スクリプトの本体も消す
        bool delScripts = false;
        if (AssetDatabase.IsValidFolder("Assets/My script/Elavater")) delScripts = AssetDatabase.DeleteAsset("Assets/My script/Elavater");
        log.AppendLine("宇宙エレベーター: " + (names.Count > 0 ? string.Join(", ", names.ToArray()) : "シーンに残り無し") + " を削除" + (delScripts ? " / スクリプトも削除" : ""));
    }

    static GameObject FindAnyByName(string nm)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == nm) return t.gameObject;
        return null;
    }

    static void CreateCloak(StringBuilder log)
    {
        GameObject old = GameObject.Find("StationCloak");
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject root = GameObject.Find("elevater_ex");
        if (root == null) { log.AppendLine("ERROR: elevater_ex が見つからない"); return; }
        Vector3 center = new Vector3(1801.6f, 0f, -148f);
        GameObject eg = GameObject.Find("spaceElavater");
        if (eg != null) center = eg.transform.position;

        // エレベーター周辺（水平 600m 以内）にある描画物だけを対象にする
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            Vector3 p = r.transform.position;
            float dx = p.x - center.x, dz = p.z - center.z;
            if (dx * dx + dz * dz > 600f * 600f) continue;
            if (r is ParticleSystemRenderer) continue;
            list.Add(r);
        }

        var go = new GameObject("StationCloak");
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        go.transform.position = new Vector3(center.x, 0f, center.z);

        // 隠している間に出す雲のかたまり（もこもこ 22 個・軽量）
        Shader puffSh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        Texture2D puff = ImportTex(FxDir + "/cloud_puff.png", false);
        Material puffMat = LoadOrCreateMat("CloudPuff", puffSh);
        puffMat.SetTexture("_MainTex", puff);
        puffMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        puffMat.renderQueue = 3000;
        EditorUtility.SetDirty(puffMat);

        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;
        var bank = new GameObject("CloudBank");
        bank.transform.SetParent(go.transform, false);
        bank.transform.position = new Vector3(center.x, (seaY + CloudBottom) * 0.5f + 20f, center.z);
        var ps = bank.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 4321;
        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = 1000000f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(220f, 380f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.85f), new Color(0.88f, 0.9f, 0.94f, 0.95f));
        main.maxParticles = 30;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(CloakRadius * 1.4f, CloudBottom - seaY - 60f, CloakRadius * 1.4f);
        var pr = bank.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = puffMat;
        pr.sortMode = ParticleSystemSortMode.Distance;
        pr.maxParticleSize = 10f;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        bank.SetActive(false);

        var cloak = UdonSharpEditor.UdonSharpUndo.AddComponent<StationCloak>(go);
        cloak.stationRenderers = list.ToArray();
        cloak.cloudBank = bank;
        cloak.center = center;
        cloak.showAltitude = CloudBottom;
        cloak.showRadius = CloakRadius;
        cloak.hideDistance = CloakHideDistance;
        UdonSharpEditorUtility.CopyProxyToUdon(cloak);
        log.AppendLine("ステーション隠し: 対象 " + list.Count + " 個 / 中心 " + center.ToString("F0") + " / 高さ " + CloudBottom + "m 未満・" + CloakRadius + "m 以上離れると隠す / " + CloakHideDistance + "m 以上は常に隠す");
    }

    // ---------------- 無線 ----------------
    static bool radioRetried;
    const string RadioScriptPath = "Assets/My script/drone/PilotRadio.cs";
    const string RadioAssetPath = "Assets/My script/drone/PilotRadio.asset";

    static void CreateRadio(StringBuilder log)
    {
        GameObject old = GameObject.Find("PilotRadio");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var go = new GameObject("PilotRadio");
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        var radio = UdonSharpEditor.UdonSharpUndo.AddComponent<PilotRadio>(go);
        radio.planes = Object.FindObjectsOfType<controller>(true);
        UdonSharpEditorUtility.CopyProxyToUdon(radio);
        log.AppendLine("無線: " + radio.planes.Length + " 機を登録");
    }

    // ---------------- 波のある海 ----------------
    static bool oceanRetried;
    const string OceanScriptPath = "Assets/My script/drone/OceanFollow.cs";
    const string OceanAssetPath = "Assets/My script/drone/OceanFollow.asset";
    const string OldSeaMatPath = "Assets/My material/sea_water.mat";

    static void CreateOcean(StringBuilder log)
    {
        // 元の海（画像を貼った板）は見た目だけ消す。当たり判定はそのまま残す
        float seaY = -27.7f;
        bool foundY = false;
        int hidden = 0;
        var oldMat = AssetDatabase.LoadAssetAtPath<Material>(OldSeaMatPath);
        if (oldMat != null)
        {
            foreach (var r in Object.FindObjectsOfType<MeshRenderer>(true))
            {
                if (r.sharedMaterial != oldMat) continue;
                if (!foundY) { seaY = r.transform.position.y; foundY = true; }
                if (r.enabled)
                {
                    Undo.RecordObject(r, "Flight setup");
                    r.enabled = false;
                    hidden++;
                }
            }
        }

        GameObject old = GameObject.Find("Ocean");
        if (old != null) Undo.DestroyObjectImmediate(old);

        Shader sh = Shader.Find("FlightUI/Ocean");
        if (sh == null) { log.AppendLine("ERROR: 海のシェーダーが見つからない"); return; }
        Texture2D noise = ImportTex(FxDir + "/cloud_noise.png", true);
        Material m = LoadOrCreateMat("Ocean", sh);
        m.SetTexture("_NoiseTex", noise);
        EditorUtility.SetDirty(m);

        var go = new GameObject("Ocean");
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        go.transform.position = new Vector3(CloudCenter.x, seaY, CloudCenter.z);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = GetOceanMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        var follow = UdonSharpEditor.UdonSharpUndo.AddComponent<OceanFollow>(go);
        follow.seaLevel = seaY;
        UdonSharpEditorUtility.CopyProxyToUdon(follow);

        // 高度表示を「海面からの高さ」にする
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (Mathf.Approximately(c.seaLevelY, seaY)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.seaLevelY = seaY;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        }

        log.AppendLine("海: 海面 " + seaY.ToString("F1") + "m 元の板 " + hidden + " 枚を非表示 / 波のある海を設置");
    }

    // 中心ほど細かく、外ほど粗い 1 枚の網目（半径 8km・約 6.3 万頂点）
}
