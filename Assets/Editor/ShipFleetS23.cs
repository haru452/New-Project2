using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 空母の艦隊に S2 と S3 を 1 隻ずつ入れる（艦隊の S5 の 2 隻の見た目を S2・S3 に入れ替える）
//  ・対空砲を持つのは S2・S3 だけ。艦隊のほかの船の対空砲は外す
//  ・入れ替え方は ShipSwapModels.SwapOne と同じ（耐久値・航路・艦隊の位置はそのまま）
// メニュー「Tools/空母の艦隊に S2・S3 を 1 隻ずつ入れる」。ログは Backup_flight/fleet_s23_log.txt
//  Backup_flight/run_fleet_s23.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class ShipFleetS23
{
    const string FlagPath = "Backup_flight/run_fleet_s23.flag";

    static ShipFleetS23() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/空母の艦隊に S2・S3 を 1 隻ずつ入れる")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("ShipFleetS23 " + System.DateTime.Now);
        Undo.SetCurrentGroupName("Fleet S2 S3");
        int group = Undo.GetCurrentGroup();
        try { Swap(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Undo.CollapseUndoOperations(group);
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/fleet_s23_log.txt", log.ToString());
        Debug.Log(log.ToString());
        ShipFxFix.Run();      // 煙・炎の位置を新しい見た目に合わせる
        ShipS23Setup.Run();   // S2・S3 に対空砲
    }

    static void Swap(StringBuilder log)
    {
        var fleet = Object.FindObjectsOfType<WarShip>(true).Where(w => w.useFormation).OrderBy(w => w.name).ToList();
        log.AppendLine("空母の艦隊の船: " + fleet.Count + " 隻");
        foreach (var w in fleet) log.AppendLine("  " + w.name + ": " + (ShipSwapModels.TypeOf(w.model) ?? "?"));

        bool hasS2 = fleet.Any(w => ShipSwapModels.TypeOf(w.model) == "S2");
        bool hasS3 = fleet.Any(w => ShipSwapModels.TypeOf(w.model) == "S3");
        var want = new List<string>();
        if (!hasS2) want.Add("S2");
        if (!hasS3) want.Add("S3");
        var s5 = fleet.Where(w => ShipSwapModels.TypeOf(w.model) == "S5").ToList();
        for (int i = 0; i < want.Count; i++)
        {
            if (i >= s5.Count) { log.AppendLine("ERROR: 入れ替える S5 が足りない（" + want[i] + " を入れられない）"); break; }
            if (ShipSwapModels.SwapOne(s5[i], want[i], log)) log.AppendLine(s5[i].name + ": S5 → " + want[i]);
        }
        if (want.Count == 0) log.AppendLine("艦隊にはもう S2・S3 がいる（入れ替えない）");

        // 艦隊の S2・S3 以外は対空砲を外す（S2・S3 は ShipS23Setup が付ける）
        foreach (var w in fleet)
        {
            if (ShipS23Setup.IsS23(w) || !w.fleetAA) continue;
            Undo.RecordObject(w, "Fleet S2 S3");
            w.fleetAA = false;
            UdonSharpEditorUtility.CopyProxyToUdon(w);
            PrefabUtility.RecordPrefabInstancePropertyModifications(w);
            log.AppendLine(w.name + ": 対空砲を外した");
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("シーンを保存してください");
    }
}
