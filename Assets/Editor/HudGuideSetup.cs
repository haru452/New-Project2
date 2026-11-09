using System.IO;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// main room（艦橋）の、格納庫へのハッチの横（南側）に「HUD の見かた」の板を置く
//  ・上に HUD の写真 2 枚（番号の丸付き。Tools/make_hud_guide.py で作る hud_guide_1.png / hud_guide_2.png）
//  ・下に番号ごとの説明。言語選択で English / 日本語 / 中文 を切り替える
//  Tools/HUD の見かたの板を置く、または Backup_flight/run_hudguide.flag。結果は Backup_flight/hudguide_report.txt
//  板の作り（背景・枠・フォントの字の確認）は RulesSetup と同じ
[InitializeOnLoad]
public static class HudGuideSetup
{
    const string FlagPath = "Backup_flight/run_hudguide.flag";
    const string BoardName = "HudGuideBoard";
    const string ImgDir = "Assets/My script/bridge/";
    // 艦橋のローカル座標。東の壁（ハッチは z = 0）の南側に貼って、西（部屋の中）を向く
    static readonly Vector3 Pos = new Vector3(11.9f, 1.75f, -3.0f);
    static readonly Vector3 LookDir = new Vector3(-1f, 0f, 0f);
    const float W = 2.6f, H = 2.5f;

    const string N = "<color=#ffd200><b>", M = "</b></color> ";   // 番号（写真の丸と同じ黄色）
    static readonly string[] Titles = { "HUD GUIDE", "HUD の見かた", "HUD 说明" };
    // [言語][左の列, 右の列]
    static readonly string[][] Cols =
    {
        new[] {
            N + "1" + M + "SPEED  speed (km/h)\n" +
            N + "2" + M + "ALTITUDE  height (m)\n" +
            N + "3" + M + "P / R  nose up-down / bank (°)\n" +
            N + "4" + M + "HDG  heading (270 W = west)\n" +
            N + "5" + M + "+  aim (where guns and missiles go)\n" +
            N + "6" + M + "RADAR  enemies and torpedoes. Arrows: <color=#ffd040>yellow</color> I-402 / <color=#40d0ff>cyan</color> Astral airport / <color=#ff5040>red</color> Pelagos base / white carrier (sightseeing)\n" +
            N + "7" + M + "KILLS  pt points / AIR fighters / SHIP ships / CV carriers",
            N + "8" + M + "THR  throttle (%)\n" +
            N + "9" + M + "VS  climb / descent (m/s, + is up)\n" +
            N + "10" + M + "FUEL  % / litres / time left at this throttle\n" +
            N + "11" + M + "HP  aircraft health (0 = shot down)\n" +
            N + "12" + M + "WEAPON  > is selected. 1 gun / 2 rockets / 3 lock-on missiles / 4 torpedoes / X flares\n" +
            N + "13" + M + "AoA  angle of attack (too high = stall)\n" +
            N + "14" + M + "Lit = on: FLAP / BRAKE / BOOST / VOICE [M] / GEAR [G]\n" +
            "<color=#ff6050>Warnings</color>  STALL WARNING, STALL, PULL UP (too low), LOW FUEL (under 20%), ENGINE FIRE, CRITICAL DAMAGE",
        },
        new[] {
            N + "1" + M + "SPEED  速さ（km/h）\n" +
            N + "2" + M + "ALTITUDE  高さ（m）\n" +
            N + "3" + M + "P / R  機首の上下・左右の傾き（°）\n" +
            N + "4" + M + "HDG  機首の向き（270 W = 西）\n" +
            N + "5" + M + "＋  照準（銃やミサイルが飛ぶ所）\n" +
            N + "6" + M + "RADAR  敵や魚雷が映る。矢印: <color=#ffd040>黄</color> I-402 / <color=#40d0ff>水色</color> アストラル空港 / <color=#ff5040>赤</color> ペラゴス基地 / 白 空母（遊覧モード）\n" +
            N + "7" + M + "KILLS  pt 点数 / AIR 落とした戦闘機 / SHIP 沈めた船 / CV 空母",
            N + "8" + M + "THR  スロットル（%）\n" +
            N + "9" + M + "VS  上昇・下降の速さ（m/s、＋で上昇）\n" +
            N + "10" + M + "FUEL  燃料（% / リットル / 今の出力であと何分）\n" +
            N + "11" + M + "HP  機体の耐久（0 で撃墜）\n" +
            N + "12" + M + "WEAPON  > が選んでいる武器。1 機銃 / 2 ロケット / 3 ロックオンミサイル / 4 魚雷 / X フレア\n" +
            N + "13" + M + "AoA  迎え角（大きすぎると失速）\n" +
            N + "14" + M + "明るいものが使用中: FLAP フラップ / BRAKE ブレーキ / BOOST ブースト / VOICE [M] 音声 / GEAR [G] 脚\n" +
            "<color=#ff6050>警告</color>  STALL WARNING 失速しそう・STALL 失速・PULL UP 低すぎる・LOW FUEL 燃料 20% 以下・ENGINE FIRE エンジン火災・CRITICAL DAMAGE 大破",
        },
        new[] {
            N + "1" + M + "SPEED  速度（km/h）\n" +
            N + "2" + M + "ALTITUDE  高度（m）\n" +
            N + "3" + M + "P / R  俯仰・横滚（°）\n" +
            N + "4" + M + "HDG  机头方向（270 W = 西）\n" +
            N + "5" + M + "＋  瞄准（子弹和导弹飞去的地方）\n" +
            N + "6" + M + "RADAR  显示敌人和鱼雷。箭头: <color=#ffd040>黄</color> I-402 / <color=#40d0ff>蓝</color> 阿斯特拉尔机场 / <color=#ff5040>红</color> 佩拉戈斯基地 / 白 航母（游览模式）\n" +
            N + "7" + M + "KILLS  pt 分数 / AIR 击落战斗机 / SHIP 击沉舰船 / CV 航母",
            N + "8" + M + "THR  油门（%）\n" +
            N + "9" + M + "VS  爬升・下降速度（m/s，＋为爬升）\n" +
            N + "10" + M + "FUEL  燃料（% / 升 / 以当前油门还能飞多久）\n" +
            N + "11" + M + "HP  机体耐久（0 即被击落）\n" +
            N + "12" + M + "WEAPON  > 为当前武器。1 机枪 / 2 火箭 / 3 锁定导弹 / 4 鱼雷 / X 干扰弹\n" +
            N + "13" + M + "AoA  迎角（过大会失速）\n" +
            N + "14" + M + "亮起表示开启: FLAP 襟翼 / BRAKE 刹车 / BOOST 加速 / VOICE [M] 语音 / GEAR [G] 起落架\n" +
            "<color=#ff6050>警告</color>  STALL WARNING 快要失速・STALL 失速・PULL UP 高度过低・LOW FUEL 燃料低于 20%・ENGINE FIRE 发动机起火・CRITICAL DAMAGE 严重损伤",
        },
    };

    static HudGuideSetup() { EditorApplication.delayCall += TryFlag; }
    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/HUD の見かたの板を置く")]
    public static void Run()
    {
        var log = new StringBuilder();
        if (EditorApplication.isPlaying) { File.WriteAllText(FlagPath, "run"); EditorApplication.delayCall += TryFlag; return; }
        try { Build(log); }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/hudguide_report.txt", log.ToString());
    }

    static void Build(StringBuilder log)
    {
        Transform bridge = null;
        foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "Bridge" && t.parent != null && t.parent.name == "mainRoom") bridge = t;
        if (bridge == null) { log.AppendLine("ERROR mainRoom/Bridge が無い"); return; }
        var sub = Object.FindObjectOfType<Subtitles>(true);
        if (sub == null) log.AppendLine("注意: Subtitles が無いので、言語の切り替えができません（English のまま）");
        var old = bridge.Find(BoardName);
        if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); log.AppendLine("前の板を消しました"); }

        var root = new GameObject(BoardName);
        Undo.RegisterCreatedObjectUndo(root, "HudGuide");
        root.transform.SetParent(bridge, false);
        root.transform.localPosition = Pos;
        root.transform.localRotation = Quaternion.LookRotation(-LookDir, Vector3.up);
        RulesSetup.Part(root.transform, "Back", new Vector3(0f, 0f, 0.02f), new Vector3(W, H, 0.03f), RulesSetup.Mat("rules_board", new Color(0.05f, 0.08f, 0.14f), 0f));
        RulesSetup.Part(root.transform, "Frame", new Vector3(0f, 0f, 0.035f), new Vector3(W + 0.06f, H + 0.06f, 0.02f), RulesSetup.Mat("rules_frame", new Color(0.55f, 0.47f, 0.28f), 0.6f));

        // 写真 2 枚（左右に並べる。上をそろえる）
        const float imgTop = H / 2f - 0.2f, imgW = 1.24f;
        float h1 = Photo(root.transform, "hud_guide_1", new Vector2(-W / 4f + 0.01f, 0f), imgTop, imgW, log);
        float h2 = Photo(root.transform, "hud_guide_2", new Vector2(W / 4f - 0.01f, 0f), imgTop, imgW, log);
        float textTop = imgTop - Mathf.Max(h1, h2) - 0.06f;
        float textH = textTop + H / 2f - 0.06f;

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
        string[] langs = { "en", "ja", "zh" };
        var objs = new GameObject[3];
        var all = new StringBuilder();
        for (int i = 0; i < 3; i++)
        {
            var lg = new GameObject("Text_" + langs[i]);
            lg.transform.SetParent(root.transform, false);
            Text(lg.transform, "Title", new Vector3(0f, H / 2f - 0.1f, 0f), new Vector2(W - 0.2f, 0.16f), "<b>" + Titles[i] + "</b>", TextAlignmentOptions.Center, 1.2f, font);
            float cw = W / 2f - 0.1f;
            Text(lg.transform, "Left", new Vector3(-W / 4f + 0.02f, textTop - textH / 2f, 0f), new Vector2(cw, textH), Cols[i][0], TextAlignmentOptions.TopLeft, 0.75f, font);
            Text(lg.transform, "Right", new Vector3(W / 4f - 0.02f, textTop - textH / 2f, 0f), new Vector2(cw, textH), Cols[i][1], TextAlignmentOptions.TopLeft, 0.75f, font);
            lg.SetActive(i == 0);
            objs[i] = lg;
            all.Append(Titles[i]).Append(Cols[i][0]).Append(Cols[i][1]);
        }
        if (sub != null)
        {
            var ls = UdonSharpUndo.AddComponent<LangSwap>(root);
            ls.subtitles = sub; ls.objs = objs;
            UdonSharpEditorUtility.CopyProxyToUdon(ls);
            EditorUtility.SetDirty(ls);
            log.AppendLine("言語の切り替え: LangSwap（English / 日本語 / 中文）");
        }
        RulesSetup.AddChars(font, all.ToString(), log);

        EditorSceneManager.MarkSceneDirty(bridge.gameObject.scene);
        EditorSceneManager.SaveScene(bridge.gameObject.scene);
        log.AppendLine("HUD の見かたの板を置きました: 艦橋から " + Pos.ToString("F2") + "（ハッチの南側。写真 2 枚と説明。シーンを保存しました）");
        log.AppendLine("DONE");
    }

    // 写真の板（上の辺を top にそろえる）。返り値は高さ
    static float Photo(Transform p, string name, Vector2 center, float top, float w, StringBuilder log)
    {
        string path = ImgDir + name + ".png";
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null && (imp.maxTextureSize != 2048 || imp.wrapMode != TextureWrapMode.Clamp || imp.npotScale != TextureImporterNPOTScale.None))
        {
            imp.maxTextureSize = 2048; imp.wrapMode = TextureWrapMode.Clamp; imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null) { log.AppendLine("ERROR 写真が無い: " + path + "（Tools/make_hud_guide.py で作る）"); return 0f; }
        float h = w * tex.height / tex.width;
        string mp = ImgDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (m == null) { m = new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(m, mp); }
        m.mainTexture = tex;
        EditorUtility.SetDirty(m);
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(p, false);
        q.transform.localPosition = new Vector3(center.x, top - h / 2f, -0.002f);
        q.transform.localScale = new Vector3(w, h, 1f);
        q.GetComponent<Renderer>().sharedMaterial = m;
        return h;
    }

    static void Text(Transform p, string name, Vector3 lp, Vector2 size, string s, TextAlignmentOptions align, float maxSize, TMP_FontAsset font)
    {
        var g = new GameObject(name);
        g.transform.SetParent(p, false);
        g.transform.localPosition = lp;
        var t = g.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.rectTransform.sizeDelta = size;
        t.enableAutoSizing = true; t.fontSizeMin = 0.2f; t.fontSizeMax = maxSize;
        t.alignment = align; t.richText = true; t.enableWordWrapping = true;
        t.color = new Color(0.92f, 0.95f, 1f);
        t.text = s;
    }
}
