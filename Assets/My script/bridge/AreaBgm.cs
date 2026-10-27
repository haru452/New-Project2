using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

// =============================================================
//  その場所の中にいる間だけ BGM を流す（自分の画面だけ）
//  ・zone（中心 = 位置、大きさ = localScale の箱）に入るとゆっくり大きく、出るとゆっくり小さくなって止まる
//  ・_ToggleBgm（BGM のボタン）で自分だけ消せる／また流せる
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AreaBgm : UdonSharpBehaviour
{
    public Transform zone;
    public AudioSource bgm;
    public float volume = 0.3f;
    public float fadeTime = 1.5f;
    public TextMeshPro[] labels;     // 「BGM ON / OFF」
    private bool inside, muted;
    private float cur, nextCheck;

    void Start()
    {
        if (bgm != null) { bgm.volume = 0f; bgm.loop = true; }
        ShowLabels();
    }

    void Update()
    {
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.25f;
            VRCPlayerApi lp = Networking.LocalPlayer;
            inside = false;
            if (lp != null && zone != null)
            {
                Vector3 p = zone.InverseTransformPoint(lp.GetPosition());
                inside = Mathf.Abs(p.x) <= 0.5f && Mathf.Abs(p.y) <= 0.5f && Mathf.Abs(p.z) <= 0.5f;
            }
        }
        if (bgm == null) return;
        float want = inside && !muted ? volume : 0f;
        cur = Mathf.MoveTowards(cur, want, Time.deltaTime * volume / Mathf.Max(fadeTime, 0.1f));
        bgm.volume = cur;
        if (want > 0f && !bgm.isPlaying) bgm.Play();
        if (want <= 0f && cur <= 0f && bgm.isPlaying) bgm.Stop();
    }

    public void _ToggleBgm()
    {
        muted = !muted;
        ShowLabels();
    }

    void ShowLabels()
    {
        if (labels == null) return;
        for (int i = 0; i < labels.Length; i++)
            if (labels[i] != null) labels[i].text = muted ? "BGM  <color=#ff7060>OFF</color>" : "BGM  <color=#80ff90>ON</color>";
    }
}
