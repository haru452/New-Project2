using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

// 重さ調べ: 再生（Play）を始めて 5 秒後から 10 秒間、何に時間がかかっているかを測って
// Backup_flight/perf_report.txt に書く。Backup_flight/perf_probe.flag がある間だけ動く（測ったら消える）。
[InitializeOnLoad]
public static class PerfProbe
{
    const string Flag = "Backup_flight/perf_probe.flag";
    static double startAt = -1;
    static Recorder[] recs;
    static string[] names;
    static double[] sum;
    static int frames;
    static float dtSum, dtMax;

    static PerfProbe()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && File.Exists(Flag)) { startAt = EditorApplication.timeSinceStartup + 5.0; recs = null; }
            if (s == PlayModeStateChange.ExitingPlayMode) startAt = -1;
        };
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (startAt < 0 || !EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        if (now < startAt) return;
        if (recs == null)
        {
            var all = new System.Collections.Generic.List<string>();
            Sampler.GetNames(all);
            names = all.Distinct().ToArray();
            recs = new Recorder[names.Length];
            for (int i = 0; i < names.Length; i++) { recs[i] = Recorder.Get(names[i]); if (recs[i].isValid) recs[i].enabled = true; }
            sum = new double[names.Length];
            frames = 0; dtSum = 0; dtMax = 0;
            return;
        }
        frames++;
        dtSum += Time.unscaledDeltaTime; dtMax = Mathf.Max(dtMax, Time.unscaledDeltaTime);
        for (int i = 0; i < recs.Length; i++) if (recs[i].isValid) sum[i] += recs[i].elapsedNanoseconds;
        if (now < startAt + 10.0) return;
        // 書き出し
        var sb = new StringBuilder();
        sb.AppendLine("PerfProbe " + System.DateTime.Now + "  frames " + frames + "  平均 " + (dtSum / Mathf.Max(frames, 1) * 1000f).ToString("F1") + "ms（" + (frames / Mathf.Max(dtSum, 0.001f)).ToString("F0") + "fps）  最大 " + (dtMax * 1000f).ToString("F0") + "ms");
        var idx = Enumerable.Range(0, names.Length).OrderByDescending(i => sum[i]).Take(70);
        foreach (var i in idx) sb.AppendLine((sum[i] / Mathf.Max(frames, 1) / 1e6).ToString("F2").PadLeft(8) + " ms/frame  " + names[i]);
        // 数
        sb.AppendLine("--- 数 ---");
        sb.AppendLine("ParticleSystem 再生中: " + Object.FindObjectsOfType<ParticleSystem>().Count(p => p.isPlaying) + " / 有効 " + Object.FindObjectsOfType<ParticleSystem>().Length);
        sb.AppendLine("Light 有効: " + Object.FindObjectsOfType<Light>().Count(l => l.enabled) + "（影あり " + Object.FindObjectsOfType<Light>().Count(l => l.enabled && l.shadows != LightShadows.None) + "）");
        sb.AppendLine("AudioSource 再生中: " + Object.FindObjectsOfType<AudioSource>().Count(a => a.isPlaying));
        sb.AppendLine("Rigidbody 動く: " + Object.FindObjectsOfType<Rigidbody>().Count(r => !r.isKinematic && !r.IsSleeping()));
        sb.AppendLine("Camera 有効: " + string.Join(", ", Object.FindObjectsOfType<Camera>().Where(c => c.enabled).Select(c => c.name + (c.targetTexture != null ? "(RT)" : ""))));
        sb.AppendLine("UdonBehaviour 有効: " + Object.FindObjectsOfType<VRC.Udon.UdonBehaviour>().Count(u => u.enabled));
        sb.AppendLine("Renderer 表示中: " + Object.FindObjectsOfType<Renderer>().Count(r => r.enabled && r.isVisible));
        sb.AppendLine("Terrain: " + string.Join(", ", Object.FindObjectsOfType<Terrain>().Select(t => t.name + " layers " + (t.terrainData != null ? t.terrainData.terrainLayers.Length : 0) + " pixelError " + t.heightmapPixelError + " basemapDist " + t.basemapDistance)));
        File.WriteAllText("Backup_flight/perf_report.txt", sb.ToString());
        foreach (var r in recs) if (r.isValid) r.enabled = false;
        recs = null; startAt = -1;
        if (File.Exists(Flag)) File.Delete(Flag);
        Debug.Log("PerfProbe: Backup_flight/perf_report.txt に書きました");
    }
}
