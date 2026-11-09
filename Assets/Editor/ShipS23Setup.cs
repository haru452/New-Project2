using System.IO;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// S2・S3 の船（対空砲を持った護衛艦）
//  ・空母の艦隊ではない S2・S3: 戦闘が始まって EscortAppearDelay 秒後に出てくる（それまでは見えない・当たらない・レーダーにも出ない）
//  ・S2・S3 はみんな対空砲を持つ（空母の艦隊の中の S2・S3 も。どのモードでも）
//  ・出てきた時に、司令部の無線「対空砲を持った護衛艦が向かっている」を全員に流す（gaur_escort1.wav）
//  ・Tools/フライトを設定する などで船を作り直したら、もう一度実行する
// メニュー「Tools/S2・S3 の護衛艦を設定する」
//  Backup_flight/run_s23.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class ShipS23Setup
{
    // ★デバッグ用に 20 秒。本番は 300f（5 分）
    public const float EscortAppearDelay = 300f;   // 5 分後に出てくる
    public const float EscortAnnounceAt = 240f;    // 無線は 4 分後（出てくる 1 分前）
    const string FlagPath = "Backup_flight/run_s23.flag";
    const string ClipPath = "Assets/My script/drone/Audio/gaur_escort1.wav";

    static ShipS23Setup() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    // この船の見た目が S2 か S3 か
    public static bool IsS23(WarShip w)
    {
        foreach (Transform t in w.transform.GetComponentsInChildren<Transform>(true))
        {
            string p = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
            if (!string.IsNullOrEmpty(p) && (p.EndsWith("/S2.prefab") || p.EndsWith("/S3.prefab"))) return true;
        }
        return false;
    }

    [MenuItem("Tools/S2・S3 の護衛艦を設定する")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("ShipS23Setup " + System.DateTime.Now);
        try { Setup(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/s23_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void Setup(StringBuilder log)
    {
        int late = 0, fleetAA = 0, reset = 0;
        foreach (var w in Object.FindObjectsOfType<WarShip>(true))
        {
            bool isCarrier = w.target != null && w.target.kind == 2;
            if (isCarrier) continue;
            if (!IsS23(w))
            {
                // S2・S3 から別の船に変わった船: 普通の船に戻す（空母の艦隊の船は AchievementSetup.cs が決めるので触らない）
                if (!w.useFormation && (w.appearDelay > 0f || w.fleetAA))
                {
                    Undo.RecordObject(w, "S2 S3 escorts");
                    w.appearDelay = 0f; w.fleetAA = false;
                    UdonSharpEditorUtility.CopyProxyToUdon(w);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(w);
                    reset++;
                }
                continue;
            }
            bool cvFleet = w.useFormation;   // 空母の艦隊: 出てくる時間は空母と同じ（変えない）
            Undo.RecordObject(w, "S2 S3 escorts");
            w.fleetAA = true;
            if (!cvFleet) { w.appearDelay = EscortAppearDelay; late++; }
            else fleetAA++;
            UdonSharpEditorUtility.CopyProxyToUdon(w);
            PrefabUtility.RecordPrefabInstancePropertyModifications(w);
            log.AppendLine(w.name + (w.gameObject.activeInHierarchy ? "" : " [しまってある]") + ": " + (cvFleet ? "空母の艦隊 → 対空砲" : EscortAppearDelay + " 秒後に出てくる・対空砲"));
        }

        var gm = Object.FindObjectOfType<GameMode>(true);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        if (gm != null)
        {
            Undo.RecordObject(gm, "S2 S3 escorts");
            gm.escortAnnounceAt = EscortAnnounceAt;
            gm.escortClips = clip != null ? new[] { clip } : new AudioClip[0];
            UdonSharpEditorUtility.CopyProxyToUdon(gm);
            PrefabUtility.RecordPrefabInstancePropertyModifications(gm);
        }
        else log.AppendLine("注意: GameMode が無い（無線が流れない）");
        if (clip == null) log.AppendLine("注意: " + ClipPath + " が無い（無線が流れない）");

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("後から出てくる S2・S3: " + late + " 隻（戦闘が始まって " + EscortAppearDelay + " 秒後。無線は " + EscortAnnounceAt + " 秒後）");
        log.AppendLine("空母の艦隊の S2・S3（対空砲）: " + fleetAA + " 隻");
        if (reset > 0) log.AppendLine("S2・S3 ではなくなった船を普通の船に戻した: " + reset + " 隻");
        log.AppendLine("シーンを保存してください");
    }
}
