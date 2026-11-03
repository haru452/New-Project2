using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using TMPro;
using UnityEngine.TextCore.LowLevel;

// 言語選択（日本語 / English / 中文）と字幕の設定
//  ・文章は Assets/My script/drone/Subtitles/subtitles.tsv（名前・English・日本語・中文）。直したらもう一度実行
//  ・看板はスポーン地点の少し先。字幕はプレイヤーの目の前に出る（日本語・中文を選んだ時だけ）
//  ・字幕の対象 = 全機体の音声案内・敵の無線、2 人乗りの中継、GameMode の無線（博物館の案内は対象外）
// メニュー「Tools/言語選択と字幕を設定する」。1 回目はプログラムを作るだけのことがある（もう一度実行）
public static class LangSetup
{
    const string Dir = "Assets/My script/drone/Subtitles";
    const string AudioDir = "Assets/My script/drone/Audio";
    const string Scr = "Assets/My script/drone/";

    static bool EnsureProg(string name)
    {
        string asset = Scr + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(asset) != null) return true;
        if (AssetDatabase.LoadAssetAtPath<Object>(asset) != null) AssetDatabase.DeleteAsset(asset);
        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(Scr + name + ".cs");
        if (ms == null) return true;
        var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        prog.sourceCsScript = ms;
        AssetDatabase.CreateAsset(prog, asset);
        AssetDatabase.SaveAssets();
        return false;
    }

    // 日本語・中国語が出せるフォント（Windows のフォントを取り込む。配布する時はライセンスに注意）
    static TMP_FontAsset MakeFont(string allChars, StringBuilder log)
    {
        string fa = Dir + "/Font/SubtitleFont SDF.asset";
        var old = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fa);
        if (old != null) { old.TryAddCharacters(allChars); EditorUtility.SetDirty(old); return old; }
        Directory.CreateDirectory(Dir + "/Font");
        string ttc = Dir + "/Font/subtitle_font.ttc";
        string[] cands = { "C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/meiryo.ttc" };
        if (!File.Exists(ttc))
        {
            foreach (var c in cands) if (File.Exists(c)) { File.Copy(c, ttc); break; }
            AssetDatabase.ImportAsset(ttc);
        }
        var font = AssetDatabase.LoadAssetAtPath<Font>(ttc);
        if (font == null) { log.AppendLine("フォントが取り込めませんでした（Windows の msyh.ttc / meiryo.ttc が無い）"); return null; }
        var f = TMP_FontAsset.CreateFontAsset(font, 56, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        AssetDatabase.CreateAsset(f, fa);
        f.material.name = "SubtitleFont Material";
        AssetDatabase.AddObjectToAsset(f.material, f);
        for (int i = 0; i < f.atlasTextures.Length; i++) { f.atlasTextures[i].name = "SubtitleFont Atlas " + i; AssetDatabase.AddObjectToAsset(f.atlasTextures[i], f); }
        f.TryAddCharacters(allChars);
        EditorUtility.SetDirty(f);
        return f;
    }

    static Material OverlayMat(TMP_FontAsset f)
    {
        string p = Dir + "/Font/SubtitleOverlay.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        var sh = Shader.Find("TextMeshPro/Distance Field Overlay");
        if (sh == null) sh = Shader.Find("TextMeshPro/Distance Field");
        if (m == null) { m = new Material(f.material); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh;
        m.mainTexture = f.atlasTexture;
        m.SetColor("_OutlineColor", Color.black);
        m.SetFloat("_OutlineWidth", 0.22f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static TextMeshPro Txt(Transform p, string name, Vector3 lp, string s, float size, Vector2 box, TMP_FontAsset font, Color col)
    {
        var g = new GameObject(name);
        g.transform.SetParent(p, false); g.transform.localPosition = lp;
        var t = g.AddComponent<TextMeshPro>();
        t.font = font; t.text = s; t.fontSize = size; t.color = col; t.richText = true;
        t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = true;
        t.rectTransform.sizeDelta = box;
        return t;
    }

    static Material Mat(string name, Color c)
    {
        string p = Dir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Unlit/Color")); AssetDatabase.CreateAsset(m, p); }
        m.color = c; EditorUtility.SetDirty(m);
        return m;
    }

    // 話している人（音声ファイル名の頭で決める）: 名前（日本語・中文）と色
    static void Speaker(string file, out string ja, out string zh, out Color col)
    {
        if (file.StartsWith("voice_")) { ja = "R35搭載 戦闘支援AI"; zh = "R35搭载 战斗支援AI"; col = new Color(0.35f, 1f, 0.85f); }
        else if (file.StartsWith("gaur_")) { ja = "ガウリア帝国 司令部"; zh = "高里亚帝国 司令部"; col = new Color(1f, 0.8f, 0.3f); }
        else if (file.StartsWith("guan_")) { ja = "グアン艦長"; zh = "关舰长"; col = new Color(0.82f, 0.5f, 1f); }
        else if (file.StartsWith("astral_arg")) { ja = "アストラル空港 管制 / 戦闘機"; zh = "阿斯特拉机场 管制 / 战斗机"; col = new Color(0.85f, 0.85f, 0.85f); }
        else if (file.StartsWith("astral_")) { ja = "アストラル空港 管制"; zh = "阿斯特拉机场 管制"; col = new Color(0.55f, 0.9f, 0.45f); }
        else if (file.StartsWith("ain_")) { ja = "アイン・エメラルド中将"; zh = "艾因·艾默拉德中将"; col = new Color(0.45f, 0.7f, 1f); }
        else if (file.StartsWith("elc_")) { ja = "エルシディア王国"; zh = "埃尔西迪亚王国"; col = new Color(1f, 0.6f, 0.75f); }
        else if (file.StartsWith("escort_")) { ja = "エルシディア空軍"; zh = "埃尔西迪亚空军"; col = new Color(1f, 0.6f, 0.75f); }
        else if (file.StartsWith("ama_")) { ja = "AMA"; zh = "AMA"; col = new Color(1f, 0.38f, 0.38f); }
        else { ja = ""; zh = ""; col = Color.white; }
    }

    static Material WindowMat()
    {
        string p = Dir + "/Font/SubtitleWindow.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        bool create = m == null;
        var sh = Shader.Find("FlightUI/UnlitTint");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (create) m = new Material(sh);
        m.shader = sh;
        m.SetColor("_Color", new Color(0.03f, 0.05f, 0.12f, 0.72f));
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3090;
        if (create) AssetDatabase.CreateAsset(m, p); else EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Plate(Transform parent, string name, Vector3 lp, Vector2 size, Material m)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name; Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false);
        q.transform.localPosition = lp; q.transform.localScale = new Vector3(size.x, size.y, 1f);
        q.GetComponent<MeshRenderer>().sharedMaterial = m;
        return q;
    }

    // ギャルゲー風のメッセージウィンドウ（暗い板・名前の札・文字）。幅 1.0 のコックピットの単位で作る
    static GameObject MakeBox(string name, TMP_FontAsset font, Material ovMat, Material winMat, out TextMeshPro nameT, out TextMeshPro msgT)
    {
        var root = new GameObject(name);
        Plate(root.transform, "Window", new Vector3(0f, 0f, 0.002f), new Vector2(1.0f, 0.24f), winMat);
        Plate(root.transform, "NamePlate", new Vector3(-0.33f, 0.135f, 0.002f), new Vector2(0.34f, 0.055f), winMat);
        var ng = new GameObject("Name"); ng.transform.SetParent(root.transform, false);
        ng.transform.localPosition = new Vector3(-0.49f, 0.135f, 0f);
        nameT = ng.AddComponent<TextMeshPro>();
        nameT.font = font; nameT.fontSharedMaterial = ovMat;
        nameT.fontSize = 0.24f; nameT.fontStyle = FontStyles.Bold; nameT.color = Color.white;
        nameT.alignment = TextAlignmentOptions.MidlineLeft; nameT.enableWordWrapping = false; nameT.overflowMode = TextOverflowModes.Overflow;
        nameT.rectTransform.sizeDelta = new Vector2(0.5f, 0.06f); nameT.rectTransform.pivot = new Vector2(0f, 0.5f);
        nameT.text = "";
        var mg = new GameObject("Message"); mg.transform.SetParent(root.transform, false);
        mg.transform.localPosition = new Vector3(0f, -0.012f, 0f);
        msgT = mg.AddComponent<TextMeshPro>();
        msgT.font = font; msgT.fontSharedMaterial = ovMat;
        msgT.fontSize = 0.3f; msgT.color = Color.white;
        msgT.alignment = TextAlignmentOptions.TopLeft; msgT.enableWordWrapping = true; msgT.overflowMode = TextOverflowModes.Overflow;
        msgT.rectTransform.sizeDelta = new Vector2(0.94f, 0.19f);
        msgT.text = "";
        return root;
    }

    [MenuItem("Tools/言語選択と字幕を設定する")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("LangSetup " + System.DateTime.Now);
        try { Build(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/lang_setup_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void Build(StringBuilder log)
    {
        bool ok = EnsureProg("Subtitles");
        ok &= EnsureProg("LangButton");
        if (!ok)
        {
            AssetDatabase.Refresh();
            log.AppendLine("プログラムを作りました。コンパイルが終わったら、もう一度メニューを実行してください");
            return;
        }
        // 文章を読む
        string tsv = Dir + "/subtitles.tsv";
        if (!File.Exists(tsv)) { log.AppendLine("subtitles.tsv が無い"); return; }
        var clips = new List<AudioClip>(); var ja = new List<string>(); var zh = new List<string>();
        var spkJa = new List<string>(); var spkZh = new List<string>(); var spkCol = new List<Color>();
        var all = new StringBuilder("日本語English中文　。、！？「」…0123456789.,!?'’- ");
        int empty = 0;
        foreach (var line in File.ReadAllLines(tsv, Encoding.UTF8))
        {
            if (line.StartsWith("#") || line.Trim().Length == 0) continue;
            var c = line.Split('\t');
            if (c.Length < 4) continue;
            string j = c[2].Trim(), z = c[3].Trim();
            if (j.Length == 0 && z.Length == 0) { empty++; continue; }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/" + c[0].Trim() + ".wav");
            if (clip == null) { log.AppendLine("音声が無い: " + c[0]); continue; }
            clips.Add(clip); ja.Add(j); zh.Add(z); all.Append(j).Append(z);
            string sj, sz; Color sc; Speaker(c[0].Trim(), out sj, out sz, out sc);
            spkJa.Add(sj); spkZh.Add(sz); spkCol.Add(sc); all.Append(sj).Append(sz);
        }
        log.AppendLine("字幕 " + clips.Count + " 本（文章がまだ空の音声 " + empty + " 本は字幕なし）");

        string notice = "中文：部分内容暂不支持中文　/　Chinese is not fully supported　/　中国語は一部のみ対応";
        string title = "言語 / Language / 语言";
        all.Append(notice).Append(title).Append("日本語中文English字幕あり▼ ").Append(LangSign.ExtraChars);
        var font = MakeFont(all.ToString(), log);
        if (font == null) return;
        var ovMat = OverlayMat(font);

        // 古いものを消す
        foreach (var n in new[] { "LanguageSelect", "SubtitlesRoot" })
        {
            var go = GameObject.Find(n);
            if (go != null) Undo.DestroyObjectImmediate(go);
        }

        // 字幕本体
        var root = new GameObject("SubtitlesRoot");
        Undo.RegisterCreatedObjectUndo(root, "Lang");
        var winMat = WindowMat();
        TextMeshPro nameTmp, tmp;
        var txtGo = MakeBox("SubtitleText", font, ovMat, winMat, out nameTmp, out tmp);
        txtGo.transform.SetParent(root.transform, false);
        txtGo.transform.localScale = Vector3.one * 1.5f;   // 目の前 1.4m に出す時の大きさ（コックピットの 0.9m より遠い分）
        txtGo.SetActive(false);

        // 言語選択の看板（スポーンの少し先）
        Vector3 sp = new Vector3(0f, 0f, -2.558f);
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
        if (desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null) sp = desc.spawns[0].position;
        var sign = new GameObject("LanguageSelect");
        Undo.RegisterCreatedObjectUndo(sign, "Lang");
        sign.transform.position = sp + new Vector3(0f, 1.0f, 3.0f);
        var sub = UdonSharpUndo.AddComponent<Subtitles>(root);
        LangSign.Build(sign, font, sub, title, notice);   // 看板の見た目とボタンは LangSign.cs

        // 音声を鳴らす AudioSource
        var watch = new List<AudioSource>();
        foreach (var c in Object.FindObjectsOfType<controller>(true)) { if (c.voiceAudio != null) watch.Add(c.voiceAudio); if (c.enemyRadio != null) watch.Add(c.enemyRadio); }
        foreach (var v in Object.FindObjectsOfType<VoiceRelay>(true)) { if (v.voiceOut != null) watch.Add(v.voiceOut); if (v.radioOut != null) watch.Add(v.radioOut); }
        foreach (var g in Object.FindObjectsOfType<GameMode>(true)) if (g.radio != null) watch.Add(g.radio);
        log.AppendLine("見張る AudioSource " + watch.Count + " 個");

        // 機体ごとの、コックピットに固定した字幕（機体が動いても目の前のメッセージと一緒でぶれない）
        var pl = new List<controller>(); var hts = new List<TextMeshPro>(); var hns = new List<TextMeshPro>(); var hgs = new List<GameObject>();
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            // 目の前のメッセージ（CenterMessage・コックピットに固定）のすぐ下に置く
            if (c.cpCenter == null || c.cpCenter.transform.parent == null) continue;
            try
            {
                Transform hud = c.cpCenter.transform.parent;
                var oldSub = hud.Find("Subtitle");
                if (oldSub != null) Object.DestroyImmediate(oldSub.gameObject);
                // 使っていない（非表示の）機体の中では TMP が初期化されず失敗するので、表示されている所で作ってから入れる
                TextMeshPro nT, t;
                var g = MakeBox("Subtitle", font, ovMat, winMat, out nT, out t);
                g.transform.SetParent(hud, false);
                g.transform.localPosition = new Vector3(0f, -0.43f, 0f);   // 親(CenterMessage)を 0.13 上げたので、字幕は元の高さのまま
                g.transform.localRotation = Quaternion.identity;
                g.transform.localScale = Vector3.one;
                g.SetActive(false);
                pl.Add(c); hts.Add(t); hns.Add(nT); hgs.Add(g);
            }
            catch (System.Exception e) { log.AppendLine("字幕を作れなかった機体: " + c.name + " / " + e.Message); }
        }
        ovMat.renderQueue = 3100;   // HUD の文字と同じ
        log.AppendLine("コックピットの字幕: " + pl.Count + " 機");
        sub.planes = pl.ToArray(); sub.hudTexts = hts.ToArray(); sub.hudNames = hns.ToArray(); sub.hudGos = hgs.ToArray();

        sub.watch = watch.ToArray(); sub.clips = clips.ToArray(); sub.ja = ja.ToArray(); sub.zh = zh.ToArray();
        sub.spkJa = spkJa.ToArray(); sub.spkZh = spkZh.ToArray(); sub.spkColor = spkCol.ToArray();
        sub.text = tmp; sub.nameText = nameTmp; sub.textGo = txtGo; sub.textTf = txtGo.transform; sub.chooser = sign;
        UdonSharpEditorUtility.CopyProxyToUdon(sub);

        // 動的フォントに足した文字の画像も保存する（保存しないと、再生した時に文字の形が空になる）
        foreach (var at in font.atlasTextures) if (at != null) EditorUtility.SetDirty(at);
        EditorUtility.SetDirty(font); EditorUtility.SetDirty(font.material);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("言語選択の看板を " + sign.transform.position.ToString("F1") + " に置きました（動かして使ってください）。シーンを保存してください");
    }
}
