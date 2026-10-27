using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

// =============================================================
//  博物館の音（自分の画面だけ）
//  ・倉庫の中に入ると BGM（shinkai）が流れる。出ると小さくなって止まる
//  ・音声ガイド（日本語・英語）: ボタンで再生／停止。流れている間は BGM を小さくする
//  ・倉庫から出たら音声ガイドも止める
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MuseumAudio : UdonSharpBehaviour
{
    public Transform zone;              // 倉庫の範囲（中心 = 位置、大きさ = localScale の箱）
    public AudioSource bgm;             // 館内の曲（入口〜分岐の間）
    public AudioSource bgm2;            // 慰霊の間（折り鶴の箱の近く）の曲
    public Transform memorialZone;      // 慰霊の間の範囲（中心 = 位置、大きさ = localScale の箱）
    public Transform horseZone;         // 馬の像のまわり（ここでも慰霊の間と同じ曲）
    public float bgmVolume = 0.35f;
    public float duckVolume = 0.08f;    // ガイドが流れている間の BGM
    public float fadeTime = 1.5f;
    public AudioSource[] guides;        // 0 = 日本語, 1 = 英語
    public float guideVolume = 1f;
    public VolumeSettings volume;       // 「案内の声」の大きさ（3）を使う
    public GameObject[] playingMarks;   // 再生中の印（ボタンごと）
    public TextMeshPro[] bgmLabels;     // BGM のボタンの「ON / OFF」
    private bool bgmMuted;

    private bool inside;
    private float nextCheck;
    private float cur, cur2;
    private bool nearMem;

    void Start()
    {
        if (bgm != null) { bgm.volume = 0f; bgm.loop = true; }
        if (bgm2 != null) { bgm2.volume = 0f; bgm2.loop = true; }
        ShowBgmLabels();
    }

    public bool _IsInside() { return inside; }

    bool Inside() { return InBox(zone); }

    bool InBox(Transform box)
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || box == null) return false;
        Vector3 p = box.InverseTransformPoint(lp.GetPosition());
        return Mathf.Abs(p.x) <= 0.5f && Mathf.Abs(p.y) <= 0.5f && Mathf.Abs(p.z) <= 0.5f;
    }

    bool GuidePlaying()
    {
        if (guides == null) return false;
        for (int i = 0; i < guides.Length; i++) if (guides[i] != null && guides[i].isPlaying) return true;
        return false;
    }

    void Update()
    {
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.25f;
            bool now = Inside();
            if (now != inside)
            {
                inside = now;
                if (!inside) StopGuides();
            }
            nearMem = inside && (InBox(memorialZone) || InBox(horseZone));
            UpdateMarks();
        }
        // 慰霊の間に入ると、館内の曲から慰霊の間の曲へゆっくり入れ替える
        float full = bgmMuted ? 0f : (GuidePlaying() ? duckVolume : bgmVolume);
        bool useSecond = nearMem && bgm2 != null;
        float want = inside && !useSecond ? full : 0f;
        float want2 = inside && useSecond ? full : 0f;
        float step = Time.deltaTime * bgmVolume / Mathf.Max(fadeTime, 0.1f);
        cur = Mathf.MoveTowards(cur, want, step);
        cur2 = Mathf.MoveTowards(cur2, want2, step);
        Fade(bgm, cur, want);
        Fade(bgm2, cur2, want2);
        float gv = guideVolume * (volume != null ? volume._Mult(3) : 1f);
        if (guides != null) for (int i = 0; i < guides.Length; i++) if (guides[i] != null) guides[i].volume = gv;
    }

    // BGM のボタン（自分だけ消す／また流す）
    public void _ToggleBgm()
    {
        bgmMuted = !bgmMuted;
        ShowBgmLabels();
    }

    void ShowBgmLabels()
    {
        if (bgmLabels == null) return;
        for (int i = 0; i < bgmLabels.Length; i++)
            if (bgmLabels[i] != null) bgmLabels[i].text = bgmMuted ? "BGM  <color=#ff7060>OFF</color>" : "BGM  <color=#80ff90>ON</color>";
    }

    void Fade(AudioSource a, float v, float want)
    {
        if (a == null) return;
        a.volume = v;
        if (want > 0f && !a.isPlaying) a.Play();
        if (want <= 0f && v <= 0f && a.isPlaying) a.Stop();
    }

    // ボタンから: lang のガイドを再生（流れていたら止める）
    public void _Toggle(int lang)
    {
        if (guides == null || lang < 0 || lang >= guides.Length || guides[lang] == null) return;
        bool was = guides[lang].isPlaying;
        StopGuides();
        if (!was) guides[lang].Play();
        UpdateMarks();
    }

    void StopGuides()
    {
        if (guides == null) return;
        for (int i = 0; i < guides.Length; i++) if (guides[i] != null && guides[i].isPlaying) guides[i].Stop();
    }

    void UpdateMarks()
    {
        if (playingMarks == null) return;
        for (int i = 0; i < playingMarks.Length; i++)
        {
            if (playingMarks[i] == null) continue;
            bool on = guides != null && i < guides.Length && guides[i] != null && guides[i].isPlaying;
            if (playingMarks[i].activeSelf != on) playingMarks[i].SetActive(on);
        }
    }
}
