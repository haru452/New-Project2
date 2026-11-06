using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// =============================================================
//  新しい歴史の間（赤く浸水した艦橋）。自分の画面だけ
//  ・ワールドに入ってから「博物館に行って、折り鶴を納めた」人が、その後に撃墜されると、ここへ連れてこられる
//  ・ワールドに来るたびに 1 回だけ（入り直すと、また条件を満たせば来る）
//  ・画面にたまに赤い砂嵐、赤いランプが明滅する（部屋の近くにいる時だけ動かす）
//  ・main room へは、カタパルトへのドアと同じハッチで戻る（DoorTeleport）
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class HistoryRoom : UdonSharpBehaviour
{
    public MuseumAudio museum;           // 博物館の中にいるか
    public Transform arrivePoint;        // 連れてこられる場所
    public float delay = 3f;             // 撃墜されてから連れてくるまで（格納庫へ戻されるより後）
    public TextMeshPro text;
    public string[] msg;                 // 0 English / 1 日本語 / 2 中文
    public Subtitles subtitles;
    public Renderer[] staticScreens;     // 赤い砂嵐（画面の上に重ねた板）
    public Light[] alarmLights;          // 赤く明滅する明かり
    public float activeRange = 40f;      // この距離以内にいる時だけ演出を動かす

    private bool visited, offered, shown;
    private float nextCheck;
    private bool staticOn;
    private float staticUntil, nextStatic;
    private float[] baseIntensity;
    private int shownLang = -1;

    void Start()
    {
        if (alarmLights != null)
        {
            baseIntensity = new float[alarmLights.Length];
            for (int i = 0; i < alarmLights.Length; i++) baseIntensity[i] = alarmLights[i] != null ? alarmLights[i].intensity : 1f;
        }
        SetStatic(false);
        nextStatic = Time.time + 2f;
        ShowText();
    }

    // JoinLog から: 折り鶴を納めた
    public void _Offered() { offered = true; visited = true; }   // 箱は博物館の中にある

    // JoinLog から: 自分が撃墜された
    public void _ShotDown()
    {
        if (shown || !offered || !visited || arrivePoint == null) return;
        shown = true;
        SendCustomEventDelayedSeconds(nameof(_Go), delay);
    }

    public void _Go()
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || arrivePoint == null) return;
        ShowText();
        lp.TeleportTo(arrivePoint.position, arrivePoint.rotation);
    }

    void Update()
    {
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 1f;
            if (!visited && museum != null && museum._IsInside()) visited = true;
            int l = Lang();
            if (l != shownLang) ShowText();
        }
        // 演出は部屋の近くにいる時だけ
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || arrivePoint == null) return;
        if ((lp.GetPosition() - arrivePoint.position).sqrMagnitude > activeRange * activeRange)
        {
            if (staticOn) SetStatic(false);
            return;
        }
        // 赤い砂嵐: 0.3〜1.2 秒流れて、1.5〜5 秒止まる
        if (staticOn)
        {
            if (Time.time >= staticUntil) { SetStatic(false); nextStatic = Time.time + Random.Range(1.5f, 5f); }
            else ScrollStatic();
        }
        else if (Time.time >= nextStatic)
        {
            SetStatic(true);
            staticUntil = Time.time + Random.Range(0.3f, 1.2f);
        }
        // 赤いランプの明滅
        if (alarmLights != null && baseIntensity != null)
        {
            float k = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f));
            for (int i = 0; i < alarmLights.Length; i++) if (alarmLights[i] != null) alarmLights[i].intensity = baseIntensity[i] * k;
        }
    }

    void SetStatic(bool on)
    {
        staticOn = on;
        if (staticScreens == null) return;
        for (int i = 0; i < staticScreens.Length; i++) if (staticScreens[i] != null) staticScreens[i].enabled = on;
    }

    void ScrollStatic()
    {
        if (staticScreens == null) return;
        for (int i = 0; i < staticScreens.Length; i++)
        {
            Renderer r = staticScreens[i];
            if (r == null) continue;
            r.material.mainTextureOffset = new Vector2(Random.value, Random.value);
        }
    }

    int Lang()
    {
        int l = subtitles != null ? subtitles._Lang() : 0;
        if (l < 0 || l > 2) l = 0;
        return l;
    }

    void ShowText()
    {
        int l = Lang();
        shownLang = l;
        if (text == null || msg == null || msg.Length == 0) return;
        if (l >= msg.Length) l = 0;
        text.text = msg[l];
    }
}
