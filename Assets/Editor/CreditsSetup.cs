using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// main room（艦橋）の壁に、クレジットの板を置く
//  Tools/クレジットの板を置く、または Backup_flight/run_credits.flag がある時に 1 回だけ。結果は Backup_flight/credits_report.txt
//  何度実行しても、前の板を消して作り直す
[InitializeOnLoad]
public static class CreditsSetup
{
    const string FlagPath = "Backup_flight/run_credits.flag";
    const string BoardName = "CreditsBoard";
    // 艦橋のローカル座標（部屋は x ±12・z ±5）。板の表は lookDir の向き
    public static Vector3 Pos = new Vector3(8.0f, 1.6f, 4.9f);
    public static Vector3 LookDir = new Vector3(0f, 0f, -1f);   // 北の壁（操作卓の右）に貼って、南（部屋の中）を向く

    const string Text =
        "<size=150%><b>CREDITS</b></size>\n" +
        "<size=80%>クレジット / 制作</size>\n\n" +
        "<color=#9fd3ff><b>World</b></color>\n" +
        "hichi2025\n\n" +
        "<color=#9fd3ff><b>Music</b></color>\n" +
        "「0135_クラーゲ海」\n<size=80%>びじえもん (BGMemon)  bgmemon.com</size>\n\n" +
        "「茜色の街と君の歌声」\n<size=80%>エルム凪 (Msyrfa)  booth.pm/ja/items/1388200</size>\n\n" +
        "「凪の郷愁」\n<size=80%>エルム凪  booth.pm/ja/items/7691728</size>\n\n" +
        "<size=80%>エルム凪 YouTube: youtube.com/user/sizimasorane</size>";

    static CreditsSetup() { EditorApplication.delayCall += TryFlag; }
    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/クレジットの板を置く")]
    public static void Run()
    {
        var log = new StringBuilder();
        if (EditorApplication.isPlaying) { File.WriteAllText(FlagPath, "run"); EditorApplication.delayCall += TryFlag; return; }   // プレイ中は待つ
        try
        {
            Transform bridge = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "Bridge" && t.parent != null && t.parent.name == "mainRoom") bridge = t;
            if (bridge == null) { log.AppendLine("ERROR mainRoom/Bridge が無い"); Save(log); return; }
            var old = bridge.Find(BoardName);
            if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); log.AppendLine("前の板を消しました"); }

            var root = new GameObject(BoardName);
            Undo.RegisterCreatedObjectUndo(root, "Credits");
            root.transform.SetParent(bridge, false);
            root.transform.localPosition = Pos;
            root.transform.localRotation = Quaternion.LookRotation(-LookDir, Vector3.up);   // TMP は -z 側から読む

            const float W = 1.5f, H = 1.75f;
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Back";
            Object.DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(root.transform, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            back.transform.localScale = new Vector3(W, H, 0.03f);
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.05f, 0.08f, 0.14f);
            mat.SetFloat("_Glossiness", 0.2f);
            Directory.CreateDirectory("Assets/My script/bridge");
            AssetDatabase.CreateAsset(mat, "Assets/My script/bridge/credits_board.mat");
            back.GetComponent<Renderer>().sharedMaterial = mat;

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.transform.SetParent(root.transform, false);
            frame.transform.localPosition = new Vector3(0f, 0f, 0.035f);
            frame.transform.localScale = new Vector3(W + 0.06f, H + 0.06f, 0.02f);
            var fmat = new Material(Shader.Find("Standard"));
            fmat.color = new Color(0.55f, 0.47f, 0.28f);
            fmat.SetFloat("_Metallic", 0.6f);
            AssetDatabase.CreateAsset(fmat, "Assets/My script/bridge/credits_frame.mat");
            frame.GetComponent<Renderer>().sharedMaterial = fmat;

            var tg = new GameObject("Text");
            tg.transform.SetParent(root.transform, false);
            tg.transform.localPosition = Vector3.zero;
            var tmp = tg.AddComponent<TextMeshPro>();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/My script/drone/Subtitles/Font/SubtitleFont SDF.asset");
            if (font != null) tmp.font = font; else log.AppendLine("注意: 字幕のフォントが無いので標準のフォント（日本語が出ないかも）");
            tmp.rectTransform.sizeDelta = new Vector2(W - 0.12f, H - 0.12f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.3f; tmp.fontSizeMax = 1.1f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.richText = true;
            tmp.color = new Color(0.92f, 0.95f, 1f);
            tmp.text = Text;

            EditorSceneManager.MarkSceneDirty(bridge.gameObject.scene);
            EditorSceneManager.SaveScene(bridge.gameObject.scene);
            log.AppendLine("クレジットの板を置きました: 艦橋から " + Pos.ToString("F2") + "（シーンを保存しました）");
            log.AppendLine("DONE");
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); }
        Save(log);
    }
    static void Save(StringBuilder log) { Directory.CreateDirectory("Backup_flight"); File.WriteAllText("Backup_flight/credits_report.txt", log.ToString()); }
}
