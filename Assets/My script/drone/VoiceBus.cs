using UdonSharp;
using UnityEngine;

// =============================================================
//  音声の順番待ち（自分の画面だけ）。声が重ならないように、1 つずつ流す
//  ・機体の音声案内・敵の無線・司令部の放送・作戦範囲外の警告は、みんなここを通して流す
//  ・流している声が終わってから、次を流す
//  ・AI（戦闘支援 AI）の声は優先度が低い: ほかの声が流れている・待っている時は捨てる（_PlayAi / _PlayAiNow）
//    AI の声の途中でほかの声が来たら、AI の声を止めてほかの声を流す
//  ・それ以外の声（無線・放送・アストラル空港・範囲外の警告）は、順番待ちにして捨てない（_Play）
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class VoiceBus : UdonSharpBehaviour
{
    public float gap = 0.25f;            // 声と声のあいだ（秒）

    private const int N = 32;
    private AudioSource[] qs = new AudioSource[N];
    private AudioClip[] qc = new AudioClip[N];
    private int qn;
    private AudioSource cur;             // 今流している所
    private bool curAi;                  // 今流しているのが AI の声
    private float freeAt;                // 次を流してよい時刻

    // AI 以外の声: 順番に流す（捨てない）。front = true なら列の先頭に入れる（範囲外の警告など）
    //   maxWait は前の作りの名残（今は使わない）。AI の声が流れていたら止めて、こちらを先に流す
    public void _Play(AudioSource src, AudioClip c, float maxWait, bool front)
    {
        if (src == null || c == null) return;
        if (curAi && cur != null && cur.isPlaying) { cur.Stop(); curAi = false; freeAt = 0f; }   // AI の声は譲る
        if (!Busy() && qn == 0) { Begin(src, c, false); return; }
        if (qn >= N) qn = N - 1;   // いっぱい（ふつうは起きない）: いちばん後ろを捨てる
        if (front)
        {
            for (int k = qn; k > 0; k--) { qs[k] = qs[k - 1]; qc[k] = qc[k - 1]; }
            qs[0] = src; qc[0] = c;
        }
        else { qs[qn] = src; qc[qn] = c; }
        qn++;
    }

    // AI の声: 何も流れていない・待っていない時だけ流す。かぶったら捨てる（流せたら true）
    public bool _PlayAi(AudioSource src, AudioClip c)
    {
        if (src == null || c == null) return false;
        if (Busy() || qn > 0) return false;
        Begin(src, c, true);
        return true;
    }

    // AI の急ぎの声（ミサイル警告・射出の瞬間など）: 流れているのが AI の声なら止めてすぐ流す。ほかの声なら捨てる
    public bool _PlayAiNow(AudioSource src, AudioClip c)
    {
        if (src == null || c == null) return false;
        bool other = (cur != null && cur.isPlaying && !curAi) || qn > 0;
        if (other) return false;
        if (cur != null && cur.isPlaying) cur.Stop();
        Begin(src, c, true);
        return true;
    }

    // 待っている声を捨てる（音声をオフにした時など）。src = null なら全部
    public void _Clear(AudioSource src)
    {
        int w = 0;
        for (int k = 0; k < qn; k++)
        {
            if (src != null && qs[k] != src) { qs[w] = qs[k]; qc[w] = qc[k]; w++; }
        }
        for (int k = w; k < qn; k++) { qs[k] = null; qc[k] = null; }
        qn = w;
    }

    // AI 以外の声が流れている・待っている（AI の声はかぶるので捨てる）
    public bool _OtherBusy() { return (cur != null && cur.isPlaying && !curAi) || qn > 0; }

    // 何か流れている（または、すぐ次を流す間をあけている）
    public bool _Busy() { return Busy() || qn > 0; }

    bool Busy()
    {
        if (cur != null && cur.isPlaying) return true;
        return Time.time < freeAt;
    }

    void Begin(AudioSource src, AudioClip c, bool ai)
    {
        curAi = ai;
        src.Stop();
        src.clip = c;
        src.Play();
        cur = src;
        freeAt = Time.time + 0.1f;   // Play の直後は isPlaying がまだ false のことがある
    }

    void Update()
    {
        if (cur != null && !cur.isPlaying && Time.time >= freeAt) { cur = null; freeAt = Time.time + gap; }
        if (qn == 0 || Busy()) return;
        while (qn > 0)
        {
            AudioSource s = qs[0]; AudioClip c = qc[0];
            for (int k = 1; k < qn; k++) { qs[k - 1] = qs[k]; qc[k - 1] = qc[k]; }
            qn--;
            qs[qn] = null; qc[qn] = null;
            if (s == null || c == null) continue;
            if (!s.gameObject.activeInHierarchy) continue;
            Begin(s, c, false);
            return;
        }
    }
}
