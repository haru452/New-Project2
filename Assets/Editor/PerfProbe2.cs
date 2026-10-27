using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using VRC.Udon;

// 重さ調べ 2: 再生中に、Udon の種類ごとに 3 秒ずつ止めてみて、どれだけ軽くなるかを測る（最後に全部元に戻す）。
// Backup_flight/perf_probe2.flag がある時だけ動く。結果は Backup_flight/perf_report2.txt
[InitializeOnLoad]
public static class PerfProbe2
{
    const string Flag = "Backup_flight/perf_probe2.flag";
    static double phaseStart = -1;
    static int phase;
    static System.Collections.Generic.List<string> groups;
    static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UdonBehaviour>> map;
    static System.Collections.Generic.List<UdonBehaviour> off = new System.Collections.Generic.List<UdonBehaviour>();
    static Recorder rf, ru;
    static double accF, accU, accDt; static int n;
    static StringBuilder sb;
    static double baseMs, baseUdon;

    static PerfProbe2()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && File.Exists(Flag)) { phase = -1; phaseStart = EditorApplication.timeSinceStartup + 30.0; groups = null; }   // 30 秒: 再生直後の重さが落ち着くまで待つ
            if (s == PlayModeStateChange.ExitingPlayMode) { Restore(); phaseStart = -1; }
        };
        EditorApplication.update += Tick;
    }
    static void Restore() { foreach (var u in off) if (u != null) u.enabled = true; off.Clear(); }

    static void Tick()
    {
        if (phaseStart < 0 || !EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        if (now < phaseStart) return;
        if (groups == null)
        {
            map = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UdonBehaviour>>();
            foreach (var u in Object.FindObjectsOfType<UdonBehaviour>())
            {
                if (!u.enabled) continue;
                string k = u.programSource != null ? u.programSource.name : "(none)";
                if (!map.ContainsKey(k)) map[k] = new System.Collections.Generic.List<UdonBehaviour>();
                map[k].Add(u);
            }
            groups = map.Keys.OrderByDescending(k => map[k].Count).ToList();
            rf = Recorder.Get("UdonBehaviour.ManagedFixedUpdate()"); rf.enabled = true;
            ru = Recorder.Get("UdonBehaviour.ManagedUpdate()"); ru.enabled = true;
            sb = new StringBuilder();
            sb.AppendLine("PerfProbe2 " + System.DateTime.Now + "  （止めた時の 1 フレームの時間 / Udon の時間。基準より大きく下がった物が重い）");
            phase = 0; Begin(now); return;
        }
        // 最初の 1 秒は落ち着くのを待つ
        if (now > phaseStart + 1.0)
        {
            accF += rf.elapsedNanoseconds; accU += ru.elapsedNanoseconds; accDt += Time.unscaledDeltaTime; n++;
        }
        if (now < phaseStart + 4.0) return;
        double ms = accDt / Mathf.Max(n, 1) * 1000.0;
        double udon = (accF + accU) / Mathf.Max(n, 1) / 1e6;
        if (phase % 2 == 0) { baseMs = ms; baseUdon = udon; sb.AppendLine("  （基準: " + ms.ToString("F1") + "ms  Udon " + udon.ToString("F1") + "ms）"); }
        else
        {
            string g = groups[phase / 2];
            sb.AppendLine((baseMs - ms).ToString("F1").PadLeft(7) + "ms 軽くなる  （Udon " + (baseUdon - udon).ToString("F1") + "ms）  " + g + " × " + map[g].Count + "   [止めた時 " + ms.ToString("F1") + "ms]");
        }
        Restore();
        phase++;
        if (phase >= groups.Count * 2)
        {
            File.WriteAllText("Backup_flight/perf_report2.txt", sb.ToString());
            rf.enabled = false; ru.enabled = false;
            phaseStart = -1;
            if (File.Exists(Flag)) File.Delete(Flag);
            Debug.Log("PerfProbe2: Backup_flight/perf_report2.txt に書きました");
            return;
        }
        Begin(now);
    }
    static void Begin(double now)
    {
        accF = accU = accDt = 0; n = 0; phaseStart = now;
        if (phase % 2 == 1) foreach (var u in map[groups[phase / 2]]) if (u != null && u.enabled) { u.enabled = false; off.Add(u); }
    }
}
