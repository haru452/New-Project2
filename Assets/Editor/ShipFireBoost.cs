using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 船が沈む時の炎を、煙に隠れずに見えるようにする
//  ・炎を大きく（4〜8m → 10〜18m）、少し長く残す（0.6〜1.2 秒 → 1.2〜2.2 秒）
//  ・炎を煙より手前に描く（同じ場所から出ていると、大きくて長く残る黒い煙に隠れていた）
// メニュー「Tools/船の炎を大きく・煙より手前に」
//  Backup_flight/run_shipfire.flag があれば、Unity がコンパイルし終わった時に 1 回だけ自動で実行する
[InitializeOnLoad]
public static class ShipFireBoost
{
    const string FlagPath = "Backup_flight/run_shipfire.flag";
    public const float FireSizeMin = 10f, FireSizeMax = 18f, FireLifeMin = 1.2f, FireLifeMax = 2.2f;
    public const float FireFudge = -100f, SmokeFudge = 100f;   // 小さいほど手前に描かれる

    static ShipFireBoost() { EditorApplication.delayCall += TryFlag; }

    static void TryFlag()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryFlag; return; }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/船の炎を大きく・煙より手前に")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("ShipFireBoost " + System.DateTime.Now);
        int ships = 0, fires = 0, smokes = 0;
        try
        {
            foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
            {
                int f = 0, s = 0;
                if (ws.dmgFire != null) foreach (var p in ws.dmgFire) if (p != null) { Fire(p); f++; }
                if (ws.dmgSmoke != null) foreach (var p in ws.dmgSmoke) if (p != null) { Smoke(p); s++; }
                if (f + s == 0) { log.AppendLine(ws.name + ": 炎と煙が無い（そのまま）"); continue; }
                ships++; fires += f; smokes += s;
            }
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            log.AppendLine(ships + " 隻の炎 " + fires + " 個・煙 " + smokes + " 個を直しました。シーンを保存してください");
        }
        catch (System.Exception e) { log.AppendLine("失敗: " + e); }
        Directory.CreateDirectory("Backup_flight");
        File.WriteAllText("Backup_flight/ship_fire_log.txt", log.ToString());
        Debug.Log(log.ToString());
    }

    static void Fire(ParticleSystem p)
    {
        Undo.RecordObject(p, "Ship fire");
        var m = p.main;
        m.startSize = new ParticleSystem.MinMaxCurve(FireSizeMin, FireSizeMax);
        m.startLifetime = new ParticleSystem.MinMaxCurve(FireLifeMin, FireLifeMax);
        m.maxParticles = Mathf.Max(m.maxParticles, 500);
        var r = p.GetComponent<ParticleSystemRenderer>();
        if (r != null) { Undo.RecordObject(r, "Ship fire"); r.sortingFudge = FireFudge; PrefabUtility.RecordPrefabInstancePropertyModifications(r); }
        PrefabUtility.RecordPrefabInstancePropertyModifications(p);
    }

    static void Smoke(ParticleSystem p)
    {
        var r = p.GetComponent<ParticleSystemRenderer>();
        if (r == null) return;
        Undo.RecordObject(r, "Ship smoke");
        r.sortingFudge = SmokeFudge;
        PrefabUtility.RecordPrefabInstancePropertyModifications(r);
    }
}
