using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// =============================================================
//  2 人乗りの時、操縦士に流れた音声（案内・敵の無線）を後ろの席の人にも流す
//  ・操縦士がこのオブジェクトの持ち主になって「何番の音声を流したか」を同期する
//  ・ガンナー席に座っている人の画面でだけ、同じ音声を順番に流す
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class VoiceRelay : UdonSharpBehaviour
{
    public AudioClip[] clips;           // 流せる音声の一覧（番号で送る）
    public GunnerStation gunner;
    public AudioSource voiceOut;        // 案内の音声
    public AudioSource radioOut;        // 敵（AMA）の無線

    [UdonSynced] private int seq;
    [UdonSynced] private int[] ids = new int[8];   // 最近 8 個（番号 * 4 + 種類）

    private int lastSeen = -1;
    private int[] q = new int[8];
    private int qn;

    // 操縦士の画面から: ch 0 = 順番に, 1 = 敵の無線, 2 = 割り込み
    public void _Send(AudioClip c, int ch)
    {
        if (c == null || clips == null) return;
        int idx = -1;
        for (int i = 0; i < clips.Length; i++) if (clips[i] == c) { idx = i; break; }
        if (idx < 0) return;
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        seq++;
        ids[seq % 8] = idx * 4 + ch;
        RequestSerialization();
    }

    // ガンナー席に座った時: それまでの分は流さない
    public void _MarkSeen() { lastSeen = seq; qn = 0; }

    public override void OnDeserialization()
    {
        if (lastSeen < 0 || gunner == null || !gunner._IsLocalGunner()) { lastSeen = seq; return; }
        int from = Mathf.Max(lastSeen + 1, seq - 7);
        for (int s = from; s <= seq; s++) Enqueue(ids[s % 8]);
        lastSeen = seq;
    }

    void Enqueue(int id)
    {
        int ch = id % 4;
        int idx = id / 4;
        if (idx < 0 || idx >= clips.Length || clips[idx] == null) return;
        AudioClip c = clips[idx];
        if (ch == 1)
        {
            if (radioOut == null) return;
            radioOut.Stop();
            radioOut.clip = c;
            radioOut.Play();
            return;
        }
        if (voiceOut == null) return;
        if (ch == 2)
        {
            qn = 0;
            voiceOut.Stop();
            voiceOut.clip = c;
            voiceOut.Play();
            return;
        }
        if (qn < q.Length) { q[qn] = idx; qn++; }
    }

    void Update()
    {
        if (qn <= 0 || voiceOut == null || voiceOut.isPlaying) return;
        voiceOut.clip = clips[q[0]];
        for (int k = 1; k < qn; k++) q[k - 1] = q[k];
        qn--;
        voiceOut.Play();
    }
}
