using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;

// 声が重ならないようにする（VoiceBus）。シーンに 1 個置いて、声を流す所（機体・GameMode・作戦範囲の警告）につなぐ
// メニュー「Tools/音声が重ならないようにする」。1 回目はプログラムを作るだけのことがある（もう一度実行）
//  Backup_flight/run_voicebus.flag があれば、Unity がコンパイルし終わった時に自動で実行する（プログラムを作った時は、もう一度）
[InitializeOnLoad]
public static class VoiceBusSetup
{
    const string FlagPath = "Backup_flight/run_voicebus.flag";
    static VoiceBusSetup() { EditorApplication.delayCall += TryFlag; }
    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        bool hadProgram = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Asset) != null;
        if (hadProgram) File.Delete(FlagPath);   // プログラムを作っただけの時は、フラグを残して次のコンパイル後にもう一度
        Run();
    }

    const string Script = "Assets/My script/drone/VoiceBus.cs";
    const string Asset = "Assets/My script/drone/VoiceBus.asset";

    [MenuItem("Tools/音声が重ならないようにする")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("VoiceBusSetup " + System.DateTime.Now);
        try { Build(log); }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/voice_bus_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void Build(StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Asset) == null)
        {
            var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(Script);
            if (ms == null) { log.AppendLine("ERROR: VoiceBus.cs が無い"); return; }
            var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            prog.sourceCsScript = ms;
            AssetDatabase.CreateAsset(prog, Asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            log.AppendLine("プログラムを作りました。コンパイルが終わったら、もう一度メニューを実行してください");
            return;
        }
        var bus = Find(true);
        int n = Link(bus);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        log.AppendLine("VoiceBus を、声を流す " + n + " 個の部品につなぎました。シーンを保存してください");
    }

    // シーンの VoiceBus（無ければ create = true の時に作る）
    public static VoiceBus Find(bool create)
    {
        var bus = Object.FindObjectOfType<VoiceBus>(true);
        if (bus != null || !create) return bus;
        var go = new GameObject("VoiceBus");
        Undo.RegisterCreatedObjectUndo(go, "VoiceBus");
        return UdonSharpUndo.AddComponent<VoiceBus>(go);
    }

    // voiceBus という欄を持つ部品すべてにつなぐ
    public static int Link(VoiceBus bus)
    {
        if (bus == null) return 0;
        int n = 0;
        foreach (var b in Object.FindObjectsOfType<UdonSharpBehaviour>(true))
        {
            if (b == null || b == bus) continue;
            var fi = b.GetType().GetField("voiceBus");
            if (fi == null || fi.FieldType != typeof(VoiceBus)) continue;
            if ((Object)(fi.GetValue(b) as VoiceBus) == bus) { n++; continue; }
            Undo.RecordObject(b, "VoiceBus");
            fi.SetValue(b, bus);
            UdonSharpEditorUtility.CopyProxyToUdon(b);
            PrefabUtility.RecordPrefabInstancePropertyModifications(b);
            n++;
        }
        return n;
    }
}
