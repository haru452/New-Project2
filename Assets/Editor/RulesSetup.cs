using System.IO;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// main room（艦橋）の、格納庫（I-402）へのハッチの横に「ゲームのルール」の板を置く。言語選択に合わせて English / 日本語 / 中文 を切り替える
//  Tools/ルールの板を置く、または Backup_flight/run_rules.flag がある時に 1 回だけ。結果は Backup_flight/rules_report.txt
//  何度実行しても、前の板を消して作り直す
[InitializeOnLoad]
public static class RulesSetup
{
    const string FlagPath = "Backup_flight/run_rules.flag";
    const string BoardName = "RulesBoard";
    // 艦橋のローカル座標（部屋は x ±12・z ±5）。東の壁（ハッチは z = 0）の北側に貼って、西（部屋の中）を向く
    static readonly Vector3 Pos = new Vector3(11.9f, 1.55f, 2.6f);
    static readonly Vector3 LookDir = new Vector3(-1f, 0f, 0f);
    const float W = 1.55f, H = 2.0f;

    static readonly string[] Texts =
    {
        // English
        "<size=140%><b>HOW TO PLAY</b></size>\n\n" +
        "<color=#9fd3ff><b>Goal</b></color>\n" +
        "Take off from the I-402 hangar and attack the Gauria Empire's fighters, warships and Pelagos base to earn points.\n\n" +
        "<color=#9fd3ff><b>Points</b></color>\n" +
        "Fighter 10 / Carrier fighter 14 / Warship 6 / Cargo ship 2 (refills ammo)\nBase building 4 (destroy them all: +100)\n" +
        "Aircraft carrier 200 (shared by the damage each player dealt)\n\n" +
        "<color=#9fd3ff><b>Aircraft carrier fleet</b></color>\n" +
        "After a while, an enemy carrier fleet with anti-air escorts arrives. Sink the carrier to win.\n\n" +
        "<color=#9fd3ff><b>Fuel and repair</b></color>\n" +
        "About 7 minutes at full throttle. Land at the I-402, Astral airport or a friendly runway to refuel, repair and reload.\n" +
        "Within 2.5 km of Astral airport is a no-combat zone.\n\n" +
        "<color=#9fd3ff><b>Watch out</b></color>\n" +
        "Stay inside the operation area. If you are shot down, you return to the hangar.\n" +
        "Sightseeing mode: enemies do not attack, unless you attack them first.",

        // 日本語
        "<size=140%><b>ゲームのルール</b></size>\n\n" +
        "<color=#9fd3ff><b>目的</b></color>\n" +
        "I-402 の格納庫から出撃し、ガウリア帝国の戦闘機・軍艦・ペラゴス基地を攻撃して点数をかせごう。\n\n" +
        "<color=#9fd3ff><b>点数</b></color>\n" +
        "戦闘機 10 点 / 空母の戦闘機 14 点 / 軍艦 6 点 / 貨物船 2 点（弾が満タンに）\n基地の建物 4 点（全部壊すと +100 点）\n" +
        "空母 200 点（削った割合でみんなで分ける）\n\n" +
        "<color=#9fd3ff><b>空母の艦隊</b></color>\n" +
        "しばらくすると、対空砲を持つ護衛を連れた敵の空母の艦隊がやってくる。空母を沈めれば勝利。\n\n" +
        "<color=#9fd3ff><b>燃料と修理</b></color>\n" +
        "全開で約 7 分。I-402・アストラル空港・味方の滑走路に降りると、燃料・修理・弾の補充ができる。\n" +
        "アストラル空港から 2.5km 以内は非戦闘地域。\n\n" +
        "<color=#9fd3ff><b>注意</b></color>\n" +
        "作戦範囲の外に出ないこと。撃墜されると格納庫に戻る。\n" +
        "遊覧モードでは、こちらから攻撃しなければ敵は攻撃してこない。",

        // 中文
        "<size=140%><b>游戏规则</b></size>\n\n" +
        "<color=#9fd3ff><b>目标</b></color>\n" +
        "从 I-402 的机库出击，攻击高利亚帝国的战斗机、军舰和佩拉戈斯基地来赚取分数。\n\n" +
        "<color=#9fd3ff><b>分数</b></color>\n" +
        "战斗机 10 分 / 航母战斗机 14 分 / 军舰 6 分 / 货船 2 分（弹药补满）\n基地建筑 4 分（全部摧毁 +100 分）\n" +
        "航母 200 分（按各自造成的伤害比例分配）\n\n" +
        "<color=#9fd3ff><b>航母舰队</b></color>\n" +
        "过一段时间，带着防空护卫舰的敌方航母舰队会到来。击沉航母即可获胜。\n\n" +
        "<color=#9fd3ff><b>燃料与修理</b></color>\n" +
        "全速约 7 分钟。降落在 I-402、阿斯特拉尔机场或友军跑道，可以加油、修理和补充弹药。\n" +
        "阿斯特拉尔机场 2.5 公里以内是非战斗区域。\n\n" +
        "<color=#9fd3ff><b>注意</b></color>\n" +
        "不要离开作战范围。被击落后会回到机库。\n" +
        "游览模式中，只要你不先攻击，敌人就不会攻击。",
    };

    static RulesSetup() { EditorApplication.delayCall += TryFlag; }
    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/ルールの板を置く")]
    public static void Run()
    {
        var log = new StringBuilder();
        if (EditorApplication.isPlaying) { File.WriteAllText(FlagPath, "run"); EditorApplication.delayCall += TryFlag; return; }   // プレイ中は待つ
        try
        {
            Transform bridge = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "Bridge" && t.parent != null && t.parent.name == "mainRoom") bridge = t;
            if (bridge == null) { log.AppendLine("ERROR mainRoom/Bridge が無い"); Save(log); return; }
            var sub = Object.FindObjectOfType<Subtitles>(true);
            if (sub == null) log.AppendLine("注意: Subtitles が無いので、言語の切り替えができません（English のまま）");
            var old = bridge.Find(BoardName);
            if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); log.AppendLine("前の板を消しました"); }

            var root = new GameObject(BoardName);
            Undo.RegisterCreatedObjectUndo(root, "Rules");
            root.transform.SetParent(bridge, false);
            root.transform.localPosition = Pos;
            root.transform.localRotation = Quaternion.LookRotation(-LookDir, Vector3.up);   // TMP は -z 側から読む

            Directory.CreateDirectory("Assets/My script/bridge");
            Part(root.transform, "Back", new Vector3(0f, 0f, 0.02f), new Vector3(W, H, 0.03f), Mat("rules_board", new Color(0.05f, 0.08f, 0.14f), 0f));
            Part(root.transform, "Frame", new Vector3(0f, 0f, 0.035f), new Vector3(W + 0.06f, H + 0.06f, 0.02f), Mat("rules_frame", new Color(0.55f, 0.47f, 0.28f), 0.6f));

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
            if (font == null) log.AppendLine("注意: 字幕のフォントが無いので標準のフォント（日本語・中文が出ないかも）");
            string[] names = { "Text_en", "Text_ja", "Text_zh" };
            var objs = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                var tg = new GameObject(names[i]);
                tg.transform.SetParent(root.transform, false);
                var tmp = tg.AddComponent<TextMeshPro>();
                if (font != null) tmp.font = font;
                tmp.rectTransform.sizeDelta = new Vector2(W - 0.14f, H - 0.14f);
                tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.3f; tmp.fontSizeMax = 1.0f;
                tmp.alignment = TextAlignmentOptions.TopLeft;
                tmp.richText = true;
                tmp.enableWordWrapping = true;
                tmp.color = new Color(0.92f, 0.95f, 1f);
                tmp.text = Texts[i];
                tg.SetActive(i == 0);
                objs[i] = tg;
            }
            if (sub != null)
            {
                var ls = UdonSharpUndo.AddComponent<LangSwap>(root);
                ls.subtitles = sub;
                ls.objs = objs;
                UdonSharpEditorUtility.CopyProxyToUdon(ls);
                EditorUtility.SetDirty(ls);
                log.AppendLine("言語の切り替え: LangSwap（English / 日本語 / 中文）");
            }
            AddMissingChars(font, log);

            // 近くの物の場所（板と重なっていないかの確認用）
            foreach (var ped in Object.FindObjectsOfType<VRC.SDK3.Components.VRCAvatarPedestal>(true))
                log.AppendLine("アバター台 " + ped.name + ": 艦橋から " + bridge.InverseTransformPoint(ped.transform.position).ToString("F1"));
            var cr = bridge.Find("CreditsBoard");
            if (cr != null) log.AppendLine("クレジットの板: 艦橋から " + cr.localPosition.ToString("F1"));

            EditorSceneManager.MarkSceneDirty(bridge.gameObject.scene);
            EditorSceneManager.SaveScene(bridge.gameObject.scene);
            log.AppendLine("ルールの板を置きました: 艦橋から " + Pos.ToString("F2") + "（ハッチの北側。シーンを保存しました）");
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); }
        Save(log);
    }

    internal static void Part(Transform p, string name, Vector3 lp, Vector3 ls, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(p, false);
        g.transform.localPosition = lp; g.transform.localScale = ls;
        g.GetComponent<Renderer>().sharedMaterial = m;
    }

    internal static Material Mat(string name, Color c, float metal)
    {
        string path = "Assets/My script/bridge/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.color = c; m.SetFloat("_Metallic", metal); m.SetFloat("_Glossiness", 0.25f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // 字幕のフォントが動的なら、板の字を足しておく（□ にならないように）
    static void AddMissingChars(TMP_FontAsset font, StringBuilder log) { AddChars(font, string.Concat(Texts), log); }
    internal static void AddChars(TMP_FontAsset font, string all, StringBuilder log)
    {
        if (font == null) return;
        if (font.atlasPopulationMode == AtlasPopulationMode.Dynamic) { font.TryAddCharacters(all, out string _); EditorUtility.SetDirty(font); }
        var sb = new StringBuilder();   // 足したあとで、本当に無い字だけ数える
        foreach (char ch in all) if (ch > 127 && !font.HasCharacter(ch, true, true) && sb.ToString().IndexOf(ch) < 0) sb.Append(ch);
        log.AppendLine(sb.Length == 0 ? "フォント: 足りない字なし" : "注意: フォントに無い字（□ になる）: " + sb);
    }

    static void Save(StringBuilder log) { Directory.CreateDirectory("Backup_flight"); File.WriteAllText("Backup_flight/rules_report.txt", log.ToString()); }
}
