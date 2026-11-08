using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using TMPro;

// アチーブメント（称号）の板を main room の世界ランキングの横に置き、つなぐ
//  ・メニュー「Tools/アチーブメントを設定する」（ランキングを作り直した時も自動で呼ばれる）
//  ・称号を送る URL（/b/0〜/b/31）を世界ランキングに設定（Backup_flight/score_urls.txt の 1 行目のサーバー）
//  ・ログは Backup_flight/achievement_log.txt
public static partial class FlightSetup
{
    const string AchScript = "Assets/My script/drone/Achievements.cs";
    const string AchAsset = "Assets/My script/drone/Achievements.asset";
    const string AchBtnScript = "Assets/My script/drone/AchievementButton.cs";
    const string AchBtnAsset = "Assets/My script/drone/AchievementButton.asset";

    [MenuItem("Tools/アチーブメントを設定する")]
    public static void RunAchievementSetup()
    {
        var log = new StringBuilder();
        log.AppendLine("AchievementSetup " + System.DateTime.Now);
        try
        {
            var wr = Object.FindObjectOfType<WorldRanking>(true);
            if (wr == null) log.AppendLine("ERROR: WorldRanking が無い");
            else
            {
                SetupAchievements(wr, log);
                AssetDatabase.SaveAssets();
                var sc = SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(sc);
                log.AppendLine("シーン保存: " + (EditorSceneManager.SaveScene(sc) ? "OK" : "失敗"));
            }
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION: " + e); }
        finally
        {
            Directory.CreateDirectory("Backup_flight");
            File.WriteAllText("Backup_flight/achievement_log.txt", log.ToString());
        }
    }

    static void SetupAchievements(WorldRanking wr, StringBuilder log)
    {
        EnsureProgram(AchScript, AchAsset, log);
        EnsureProgram(AchBtnScript, AchBtnAsset, log);
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AchAsset) == null || AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AchBtnAsset) == null)
        { log.AppendLine("アチーブメント: プログラムを作れませんでした"); return; }

        // 置き場所: 送るスイッチ（RankOptIn）の右。前に作った板は消して作り直す（手で動かした位置はそのまま）
        Transform root = wr.transform.parent != null ? wr.transform.parent : wr.transform;
        Transform sw = root.Find("RankOptIn");
        Vector3 pos = (sw != null ? sw.localPosition : wr.transform.localPosition) + new Vector3(5.3f, 0f, 0f);
        Quaternion rot = Quaternion.identity;
        Transform old = root.Find("Achievements");
        if (old != null) { pos = old.localPosition; rot = old.localRotation; Undo.DestroyObjectImmediate(old.gameObject); }
        var go = new GameObject("Achievements");
        Undo.RegisterCreatedObjectUndo(go, "Achievements");
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;

        const float W = 4.6f, H = 2.15f, CY = 1.95f;   // 22 個を左右 2 列（11 個ずつ）
        Quad(go.transform, "Back", new Vector3(0f, CY, 0f), new Vector2(W, H), UnlitMat("AchBack", new Color(0.05f, 0.07f, 0.10f, 1f), false, 2000));
        Material edge = UnlitMat("AchEdge", new Color(0.85f, 0.70f, 0.35f, 1f), false, 2000);
        Quad(go.transform, "EdgeT", new Vector3(0f, CY + H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
        Quad(go.transform, "EdgeB", new Vector3(0f, CY - H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
        Quad(go.transform, "EdgeL", new Vector3(-W * 0.5f, CY, -0.001f), new Vector2(0.025f, H), edge);
        Quad(go.transform, "EdgeR", new Vector3(W * 0.5f, CY, -0.001f), new Vector2(0.025f, H), edge);

        // 「国名を隠す」赤いスイッチはやめた: シーンに残っていれば消す
        if (sw != null)
            foreach (var n in new[] { "CountryButton", "CountryLabel" })
            {
                Transform o = sw.Find(n);
                if (o != null) { Undo.DestroyObjectImmediate(o.gameObject); log.AppendLine("消しました: " + n); }
            }
        foreach (var lc in new[] { "en", "ja", "zh" })   // 注意事項の板の絵（Tools/make_rank_notice.py で作り直した）
            AssetDatabase.ImportAsset("Assets/My script/drone/Rank/rank_notice_" + lc + ".png");

        // 日本語・中文が出るフォント。称号・国名の字をフォントに足す（スクリプトの文字から全部拾う）
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
        if (font == null && wr.text != null) font = wr.text.font;
        if (font != null)
        {
            var chars = new StringBuilder("←→");   // ボタンの矢印（◀ ▶ はフォントに無いので使わない）
            foreach (var src in new[] { AchScript, RankScriptPath, "Assets/My script/drone/GameMode.cs" })
                if (File.Exists(src)) foreach (char ch in File.ReadAllText(src)) if (ch > 127 && chars.ToString().IndexOf(ch) < 0) chars.Append(ch);
            string missing;
            font.TryAddCharacters(chars.ToString(), out missing);
            foreach (var at in font.atlasTextures) if (at != null) EditorUtility.SetDirty(at);
            EditorUtility.SetDirty(font); EditorUtility.SetDirty(font.material);
            log.AppendLine("フォントに字を足しました（" + chars.Length + " 字" + (string.IsNullOrEmpty(missing) ? "" : "、出せない字: " + missing) + "）");
            if (wr.text != null && wr.text.font != font) { Undo.RecordObject(wr.text, "Achievements"); wr.text.font = font; }
        }
        else log.AppendLine("注意: 字幕のフォントが無い（称号・国名の日本語・中文が出ないかもしれない）");
        var head = AchText(go.transform, "Header", new Vector3(0f, CY + H * 0.5f - 0.2f, -0.01f), new Vector2(W - 0.15f, 0.35f), 0.8f, TextAlignmentOptions.Top, font);
        head.text = "ACHIEVEMENTS";
        float colH = H - 0.5f, colY = CY - 0.2f;
        var tmp = AchText(go.transform, "TextL", new Vector3(-W * 0.25f, colY, -0.01f), new Vector2(W * 0.5f - 0.1f, colH), 0.58f, TextAlignmentOptions.TopLeft, font);
        var tmp2 = AchText(go.transform, "TextR", new Vector3(W * 0.25f, colY, -0.01f), new Vector2(W * 0.5f - 0.1f, colH), 0.58f, TextAlignmentOptions.TopLeft, font);

        var ach = UdonSharpUndo.AddComponent<Achievements>(go);
        ach.header = head;
        ach.text = tmp;
        ach.text2 = tmp2;
        ach.gameMode = Object.FindObjectOfType<GameMode>(true);
        if (ach.gameMode == null) log.AppendLine("注意: GameMode が無い（鳳凰がもらえない）");
        ach.ranking = wr;
        ach.subtitles = wr.subtitles != null ? wr.subtitles : Object.FindObjectOfType<Subtitles>(true);
        ach.museum = Object.FindObjectOfType<MuseumAudio>(true);
        if (ach.museum == null) log.AppendLine("注意: MuseumAudio が無い（歴史家がもらえない）");
        UdonSharpEditorUtility.CopyProxyToUdon(ach);

        // ◀ ▶ ボタン（板の下・手の届く高さ）
        Material btnM = UnlitMat("AchButton", new Color(0.85f, 0.70f, 0.35f, 1f), false, 2000);
        for (int k = 0; k < 2; k++)
        {
            int dir = k == 0 ? -1 : 1;
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 当たり判定付き（押せる）
            b.name = k == 0 ? "Prev" : "Next";
            b.transform.SetParent(go.transform, false);
            b.transform.localPosition = new Vector3(k == 0 ? -0.45f : 0.45f, CY - H * 0.5f - 0.25f, 0f);
            b.transform.localScale = new Vector3(0.4f, 0.25f, 0.12f);
            b.GetComponent<MeshRenderer>().sharedMaterial = btnM;
            var lab = new GameObject("Label");
            lab.transform.SetParent(go.transform, false);
            lab.transform.localPosition = b.transform.localPosition + new Vector3(0f, 0f, -0.07f);
            var lt = lab.AddComponent<TextMeshPro>();
            if (tmp.font != null) lt.font = tmp.font;
            lt.text = k == 0 ? "←" : "→";
            lt.fontSize = 1.4f; lt.color = new Color(0.1f, 0.08f, 0.05f); lt.alignment = TextAlignmentOptions.Center;
            lt.rectTransform.sizeDelta = new Vector2(0.4f, 0.25f);
            var ab = UdonSharpUndo.AddComponent<AchievementButton>(b);
            ab.achievements = ach;
            ab.dir = dir;
            UdonSharpEditorUtility.CopyProxyToUdon(ab);
        }

        // 世界ランキング: 称号の名前と、称号を送る URL
        Undo.RecordObject(wr, "Achievements");
        wr.achievements = ach;
        string f = "Backup_flight/score_urls.txt";
        string[] lines = File.Exists(f) ? File.ReadAllLines(f) : new string[0];
        string baseUrl = lines.Length > 0 ? lines[0].Trim().TrimEnd('/') : "";
        if (baseUrl.StartsWith("https://"))
        {
            var urls = new VRC.SDKBase.VRCUrl[32];
            for (int i = 0; i < 32; i++) urls[i] = new VRC.SDKBase.VRCUrl(baseUrl + "/b/" + i);
            wr.badgeUrls = urls;
            log.AppendLine("世界ランキング: 称号の URL 32 個（/b/0〜/b/31）を設定");
        }
        else { wr.badgeUrls = new VRC.SDKBase.VRCUrl[0]; log.AppendLine("世界ランキング: サーバーの URL が無いので、称号は送らない"); }
        UdonSharpEditorUtility.CopyProxyToUdon(wr);

        // 称号をもらう所とつなぐ
        var jl = Object.FindObjectOfType<JoinLog>(true);
        if (jl != null) { Undo.RecordObject(jl, "Achievements"); jl.achievements = ach; UdonSharpEditorUtility.CopyProxyToUdon(jl); }
        else log.AppendLine("注意: JoinLog が無い（撃墜・折り鶴・着陸の称号がもらえない）");
        int cv = 0;
        // 空母の HP: ミサイル（15）10 発で沈む = 150（kind 2 の標的すべて）
        int cvHp = 0;
        foreach (var ft in Object.FindObjectsOfType<FlightTarget>(true))
        {
            if (ft.kind != 2 || ft.maxHp == 150) continue;
            Undo.RecordObject(ft, "Achievements"); ft.maxHp = 150; UdonSharpEditorUtility.CopyProxyToUdon(ft); cvHp++;
        }
        log.AppendLine("空母の HP を 150 にしました（" + cvHp + " 隻）");
        // 空母の艦隊で対空砲を持つ船: S2・S3 だけ（ShipS23Setup.cs と同じ）。ほかの艦隊の船は持たない
        int aaN = 0;
        foreach (var w in Object.FindObjectsOfType<WarShip>(true))
        {
            bool fleet = w.useFormation || (w.escorts != null && w.escorts.Length > 0);
            if (!fleet) continue;
            bool want = w.useFormation && ShipS23Setup.IsS23(w);
            if (want) aaN++;
            if (w.fleetAA == want) continue;
            Undo.RecordObject(w, "Achievements"); w.fleetAA = want; UdonSharpEditorUtility.CopyProxyToUdon(w);
        }
        log.AppendLine("空母の艦隊の対空砲: " + aaN + " 隻（S2・S3）");
        // 空母の艦隊が出てくるまでの時間（FlightSetup_9.cs の CarrierAppearDelay = 600 秒）
        int fleetN = 0;
        foreach (var w in Object.FindObjectsOfType<WarShip>(true))
        {
            if (w.appearDelay <= 0f || w.appearDelay == CarrierAppearDelay) continue;
            bool cvFleet = w.useFormation || (w.target != null && w.target.kind == 2);
            if (!cvFleet) continue;   // 後から出てくる S2・S3 の護衛艦は別（ShipS23Setup.cs）
            Undo.RecordObject(w, "Achievements"); w.appearDelay = CarrierAppearDelay; UdonSharpEditorUtility.CopyProxyToUdon(w); fleetN++;
        }
        var gmd = Object.FindObjectOfType<GameMode>(true);
        if (gmd != null)
        {
            Undo.RecordObject(gmd, "Achievements");
            gmd.fleetAnnounceAt = FleetAnnounceAt;   // 司令部の「艦隊（旗艦ユリシーズ）が向かってきている」の無線
            UdonSharpEditorUtility.CopyProxyToUdon(gmd);
        }
        log.AppendLine("空母の艦隊が出てくるまで: " + CarrierAppearDelay + " 秒（" + fleetN + " 隻を変更）" + (CarrierAppearDelay < 600f ? "  ★デバッグ用。公開前に 600 に戻す" : ""));
        foreach (var v in Object.FindObjectsOfType<CarrierVictory>(true))
        {
            Undo.RecordObject(v, "Achievements"); v.joinLog = jl; v.gameMode = Object.FindObjectOfType<GameMode>(true); UdonSharpEditorUtility.CopyProxyToUdon(v); cv++;
        }
        if (cv == 0) log.AppendLine("注意: CarrierVictory が無い（生還者がもらえない）");
        log.AppendLine("アチーブメント: 板を置いてつなぎました（位置 " + pos + "、CarrierVictory " + cv + " 個）");
    }

    // 司令部の艦隊の無線を流す時刻（戦闘が始まってから）。空母と同じ 10 分（空母は範囲の外に出てから入ってくるので、無線の後に近づいてくる）
    const float FleetAnnounceAt = 600f;

    // 無線の時刻だけ入れる（ほかは何も変えない）
    [MenuItem("Tools/艦隊の無線の時刻を入れる")]
    public static void SetFleetAnnounce()
    {
        var gmd = Object.FindObjectOfType<GameMode>(true);
        if (gmd == null) { Debug.Log("GameMode が無い"); return; }
        Undo.RecordObject(gmd, "Fleet announce");
        gmd.fleetAnnounceAt = FleetAnnounceAt;
        UdonSharpEditorUtility.CopyProxyToUdon(gmd);
        PrefabUtility.RecordPrefabInstancePropertyModifications(gmd);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gmd.gameObject.scene);
        System.IO.File.WriteAllText("Backup_flight/fleet_announce_log.txt", "艦隊の無線: 戦闘が始まって " + FleetAnnounceAt + " 秒後（空母は " + CarrierAppearDelay + " 秒後）");
    }
    // Backup_flight/run_achievements.flag がある時に「アチーブメントを設定する」を 1 回だけ
    [InitializeOnLoadMethod]
    static void AchievementFlag() { EditorApplication.delayCall += AchievementFlagTry; }
    static void AchievementFlagTry()
    {
        if (!System.IO.File.Exists("Backup_flight/run_achievements.flag")) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += AchievementFlagTry; return; }
        System.IO.File.Delete("Backup_flight/run_achievements.flag");
        RunAchievementSetup();
    }
    [InitializeOnLoadMethod]
    static void FleetAnnounceFlag()
    {
        EditorApplication.delayCall += () =>
        {
            if (!System.IO.File.Exists("Backup_flight/run_fleetann.flag")) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            System.IO.File.Delete("Backup_flight/run_fleetann.flag");
            SetFleetAnnounce();
        };
    }

    static TextMeshPro AchText(Transform p, string name, Vector3 lp, Vector2 size, float fontSize, TextAlignmentOptions align, TMP_FontAsset font)
    {
        var g = new GameObject(name);
        g.transform.SetParent(p, false);
        g.transform.localPosition = lp;
        var t = g.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.fontSize = fontSize;
        t.color = Color.white;
        t.alignment = align;
        t.enableWordWrapping = false;
        t.richText = true;
        t.overflowMode = TextOverflowModes.Truncate;
        t.rectTransform.sizeDelta = size;
        return t;
    }
}
