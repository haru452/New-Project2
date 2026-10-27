using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// =============================================================
//  弔いの箱: 折り鶴を納めると「加護」を受ける（その場だけ）
//  ・次に飛行機に乗った時に 1 回だけ +10 点（何羽納めても +10 点だけ）
//  ・撃墜されて記録が 0 になったら、もう一度納めないともらえない
//  ・加護を受けている間は「次に飛行機に乗ったときに 10 ポイントもらえます」を出す
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MemorialBox : UdonSharpBehaviour
{
    public Transform inside;        // 箱の中（中心 = 位置、大きさ = localScale の箱）
    public JoinLog joinLog;         // 点数（加護）
    public GameObject blessedMessage;   // 「次に飛行機に乗ったときに 10 ポイントもらえます」
    public AudioSource bell;
    public ParticleSystem glow;
    private float nextCheck;

    void Start() { Show(); }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        Show();
    }

    public bool _Inside(Vector3 p)
    {
        if (inside == null) return false;
        Vector3 l = inside.InverseTransformPoint(p);
        return Mathf.Abs(l.x) <= 0.5f && Mathf.Abs(l.y) <= 0.5f && Mathf.Abs(l.z) <= 0.5f;
    }

    public bool _Ready() { return true; }

    // 折り鶴から: 納めた
    public void _Offer()
    {
        if (bell != null) bell.Play();
        if (glow != null) glow.Play();
        if (joinLog != null) joinLog._Bless();
        Show();
    }

    void Show()
    {
        bool b = joinLog != null && joinLog._IsBlessed();
        if (blessedMessage != null && blessedMessage.activeSelf != b) blessedMessage.SetActive(b);
    }
}
