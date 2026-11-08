using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

// レーダーに出す行き先の矢印（イ402・アストラル空港・ペラゴス基地・遊覧モードの空母）を、各機体のレーダーに作る（controller_10.cs）
//  ・レーダーの点（radarDots）と同じ板の上に FuelGuide を作る。もう一度実行すると作り直す
// メニュー「Tools/レーダーに行き先の矢印を付ける」。ログは Backup_flight/fuel_guide_log.txt
//  Backup_flight/run_fuel_guide.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class FuelGuideAuto
{
    const string FlagPath = "Backup_flight/run_fuel_guide.flag";

    static FuelGuideAuto() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        FlightSetup.RunFuelGuide();
    }
}

public static partial class FlightSetup
{
    static readonly Color GuideSubColor = new Color(1f, 0.85f, 0.3f, 1f);      // イ402: 黄色
    static readonly Color GuideAstralColor = new Color(0.4f, 0.9f, 1f, 1f);    // アストラル空港: 水色
    static readonly Color GuideBaseColor = new Color(1f, 0.35f, 0.3f, 1f);     // ペラゴス基地（敵）: 赤
    static readonly Color GuideCvColor = new Color(1f, 1f, 1f, 1f);            // 空母（遊覧モード）: 白

    [MenuItem("Tools/レーダーに行き先の矢印を付ける")]
    public static void RunFuelGuide()
    {
        var log = new StringBuilder();
        log.AppendLine("FuelGuideSetup " + System.DateTime.Now);
        try { BuildFuelGuides(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/fuel_guide_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    // 矢印 1 本と文字を作る
    static void GuidePart(Transform root, string name, string label, Material m, Color col, out Transform arrow, out TextMeshPro text)
    {
        arrow = Quad(root, name + "Arrow", new Vector3(0f, 0f, -0.009f), new Vector2(0.026f, 0.026f), m).transform;
        text = Txt(root, name + "Label", label, Vector2.zero, 0.08f, 0.07f, col, TextAlignmentOptions.Center, 0.03f);
        text.transform.localPosition = new Vector3(0f, 0f, -0.009f);
    }

    static void BuildFuelGuides(StringBuilder log)
    {
        // 上向きの矢印の絵（先が上）
        Texture2D arrowTex = GenTex("icon_guide_arrow.png", 64, (u, v) =>
        {
            bool head = v > 0.05f && v < 0.95f && Mathf.Abs(u) < (0.95f - v) * 0.9f;
            bool tail = v >= -0.85f && v <= 0.1f && Mathf.Abs(u) < 0.22f;
            return new Color(1f, 1f, 1f, head || tail ? 1f : 0f);
        });
        Material subM = TexMat("RadarGuideSub", arrowTex, GuideSubColor, 2999);
        Material astM = TexMat("RadarGuideAstral", arrowTex, GuideAstralColor, 2999);
        Material baseM = TexMat("RadarGuideBase", arrowTex, GuideBaseColor, 2999);
        Material cvM = TexMat("RadarGuideCarrier", arrowTex, GuideCvColor, 2999);

        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || c.radarDots == null || c.radarDots.Length == 0 || c.radarDots[0] == null) continue;
            Transform rp = c.radarDots[0].parent;
            Transform old = rp.Find("FuelGuide");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject("FuelGuide");
            Undo.RegisterCreatedObjectUndo(root, "Fuel guide");
            root.transform.SetParent(rp, false);
            Transform subA, astA, baseA, cvA; TextMeshPro subT, astT, baseT, cvT;
            GuidePart(root.transform, "Sub", "I-402", subM, GuideSubColor, out subA, out subT);
            GuidePart(root.transform, "Astral", "ASTRAL", astM, GuideAstralColor, out astA, out astT);
            GuidePart(root.transform, "Base", "PELAGOS", baseM, GuideBaseColor, out baseA, out baseT);
            GuidePart(root.transform, "Carrier", "CARRIER", cvM, GuideCvColor, out cvA, out cvT);
            root.SetActive(false);

            Undo.RecordObject(c, "Fuel guide");
            c.fuelGuide = root;
            c.guideSubArrow = subA; c.guideSubText = subT; c.guideSubLabel = subT.transform;
            c.guideAstralArrow = astA; c.guideAstralText = astT; c.guideAstralLabel = astT.transform;
            c.guideBaseArrow = baseA; c.guideBaseText = baseT; c.guideBaseLabel = baseT.transform;
            c.guideCvArrow = cvA; c.guideCvText = cvT; c.guideCvLabel = cvT.transform;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
            string warn = (c.respawnPoint == null ? "（注意: イ402 の場所が無い）" : "") + (c.astralAirport == null ? "（注意: アストラル空港の場所が無い）" : "")
                + (c.baseCenter == null ? "（注意: ペラゴス基地の場所が無い）" : "") + (c.carrier == null ? "（注意: 空母が無い）" : "");
            log.AppendLine(c.car.name + ": 矢印 4 本を付けた" + warn);
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("レーダーの行き先の矢印: " + n + " 機。シーンを保存してください");
    }
}
