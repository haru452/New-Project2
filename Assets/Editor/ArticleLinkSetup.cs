using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

// 世界ランキングの「最高記録を送る」スイッチ（RankOptIn）の近くに、仕組みを説明した記事（Qiita）の URL の欄を置く
//  VRChat ではワールドからブラウザを開けないので、欄をクリックして URL をコピーしてもらう（読み取り専用の InputField）
//  記事を公開したら ArticleUrl を書き換えて、Tools/世界ランキングの記事の欄を置く を実行（前の欄は消して作り直す）
//  Backup_flight/run_article.flag でも 1 回だけ実行。結果は Backup_flight/article_report.txt
[InitializeOnLoad]
public static class ArticleLinkSetup
{
    // ★記事を公開したら、ここを記事の URL にする
    const string ArticleUrl = "https://qiita.com/haru452/items/9bf94574c9e727009a69";
    const string HolderName = "ArticleLink";
    const string FlagPath = "Backup_flight/run_article.flag";

    static ArticleLinkSetup() { EditorApplication.delayCall += TryFlag; }
    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/世界ランキングの記事の欄を置く")]
    public static void Run()
    {
        var log = new StringBuilder();
        if (EditorApplication.isPlaying) { File.WriteAllText(FlagPath, "run"); EditorApplication.delayCall += TryFlag; return; }
        try { Build(log); }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/article_report.txt", log.ToString());
    }

    static void Build(StringBuilder log)
    {
        Transform sw = null;
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name != "RankOptIn") continue;
            bool hist = false;
            for (var p = t; p != null; p = p.parent) if (p.name == "HistoryRoom") hist = true;   // 歴史の間のコピーは使わない
            if (!hist) sw = t;
        }
        if (sw == null) { log.AppendLine("ERROR RankOptIn（最高記録を送るスイッチ）が無い"); return; }
        var old = sw.Find(HolderName);
        // 「最高記録を送る」ボタンの下（左は注意事項の板、右は称号の板。間の幅は約 1.3m）
        Vector3 lp = new Vector3(-0.38f, 1.45f, -0.02f);
        if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); log.AppendLine("前の欄を消しました"); }

        var holder = new GameObject(HolderName);
        Undo.RegisterCreatedObjectUndo(holder, "Article");
        holder.transform.SetParent(sw, false);
        holder.transform.localPosition = lp;

        // 見出し（3 言語を 1 行ずつ）
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
        var lab = new GameObject("Label");
        lab.transform.SetParent(holder.transform, false);
        lab.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        var lt = lab.AddComponent<TextMeshPro>();
        if (font != null) lt.font = font;
        lt.rectTransform.sizeDelta = new Vector2(1.22f, 0.45f);
        lt.enableAutoSizing = true; lt.fontSizeMin = 0.2f; lt.fontSizeMax = 0.6f;
        lt.alignment = TextAlignmentOptions.Center; lt.richText = true; lt.enableWordWrapping = true;
        lt.color = new Color(0.85f, 0.9f, 0.95f);
        lt.text = "詳しくはこの記事を読んでください\n<size=70%>（下の欄をクリックして URL をコピー）</size>\n"
                +"<size=80%>For details, please read this article (in Japanese)  /  详情请阅读这篇文章（日文）</size>";
        RulesSetup.AddChars(font, lt.text, log);

        // URL の欄（ワールドスペースの Canvas + 読み取り専用の InputField）
        var cv = new GameObject("Canvas", typeof(RectTransform));
        cv.transform.SetParent(holder.transform, false);
        var canvas = cv.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var crt = cv.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(1250f, 90f);
        crt.localScale = Vector3.one * 0.001f;   // 1.25m × 0.09m
        crt.localPosition = Vector3.zero;
        cv.AddComponent<CanvasScaler>();
        cv.AddComponent<GraphicRaycaster>();
        cv.AddComponent<VRCUiShape>();   // VRChat で手やマウスで触れるように

        var fieldGo = new GameObject("UrlField", typeof(RectTransform));
        fieldGo.transform.SetParent(cv.transform, false);
        var frt = fieldGo.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        var bg = fieldGo.AddComponent<Image>();
        bg.color = new Color(0.93f, 0.95f, 0.97f, 1f);

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(fieldGo.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(16f, 6f); trt.offsetMax = new Vector2(-16f, -6f);
        var txt = textGo.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 40; txt.resizeTextForBestFit = true; txt.resizeTextMinSize = 20; txt.resizeTextMaxSize = 40; txt.color = new Color(0.08f, 0.1f, 0.14f);
        txt.alignment = TextAnchor.MiddleCenter; txt.supportRichText = false;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;

        var field = fieldGo.AddComponent<InputField>();
        field.textComponent = txt;
        field.targetGraphic = bg;
        field.readOnly = true;                 // 書き換えられないが、選んでコピーはできる
        field.lineType = InputField.LineType.SingleLine;
        field.text = ArticleUrl;
        var nav = field.navigation; nav.mode = Navigation.Mode.None; field.navigation = nav;

        EditorSceneManager.MarkSceneDirty(sw.gameObject.scene);
        EditorSceneManager.SaveScene(sw.gameObject.scene);
        log.AppendLine("記事の URL の欄を置きました: " + sw.name + " から " + lp.ToString("F2") + "  URL: " + ArticleUrl);
        if (ArticleUrl.Contains("coming soon")) log.AppendLine("注意: URL はまだ仮です。記事を公開したら ArticleLinkSetup.cs の ArticleUrl を書き換えて、もう一度実行");
        log.AppendLine("シーンを保存しました");
        log.AppendLine("DONE");
    }
}
