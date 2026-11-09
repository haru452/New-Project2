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
    public float delay = 3f;             // （今は使わない。撃墜されたら 0.3 秒後に、I-402 に戻さず直接連れてくる）
    public TextMeshPro text;
    public string[] msg;                 // 0 English / 1 日本語 / 2 中文
    public Subtitles subtitles;
    public Renderer[] staticScreens;     // 赤い砂嵐（画面の上に重ねた板）
    public Light[] alarmLights;          // 赤く明滅する明かり
    public float activeRange = 40f;      // この距離以内にいる時だけ演出を動かす
    public GameObject[] roomParts;       // 部屋の見た目（艦橋のコピーなど）。軽くするため、近くにいない時は消しておく
    public float showRange = 80f;        // この距離以内にいる時だけ部屋を出す
    [Header("曲（「凪の郷愁」エルム凪。部屋の中にいる間だけ。自分にだけ聞こえる）")]
    public AudioSource bgm;
    public float bgmVolume = 0.35f;      // 博物館と同じ大きさ
    public float bgmRange = 16f;         // 着く場所からこの距離の中（部屋の中）で流す
    public float bgmFade = 1.5f;
    private float bgmCur;

    private bool visited, offered, shown;
    private float nextCheck;
    private bool staticOn;
    private float staticUntil, nextStatic;
    private float[] baseIntensity;
    private int shownLang = -1;
    private bool roomOn = true;

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
        SetRoom(false);
    }

    void SetRoom(bool on)
    {
        if (roomOn == on) return;
        roomOn = on;
        if (roomParts == null) return;
        for (int i = 0; i < roomParts.Length; i++) if (roomParts[i] != null) roomParts[i].SetActive(on);
    }

    // JoinLog から: 折り鶴を納めた
    public void _Offered() { offered = true; visited = true; }   // 箱は博物館の中にある

    // JoinLog から: 自分が撃墜された
    public void _ShotDown()
    {
        if (shown || !offered || !visited || arrivePoint == null) return;
        shown = true;
        pending = true;   // I-402 には戻さず、座席から降りたらすぐ連れていく
        SendCustomEventDelayedSeconds(nameof(_Go), 0.3f);
    }

    private bool pending;
    public bool _Pending() { return pending; }

    public void _Go()
    {
        pending = false;
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || arrivePoint == null) return;
        SetRoom(true);   // 着く前に部屋を出しておく
        ShowText();
        lp.TeleportTo(arrivePoint.position, arrivePoint.rotation);
    }

    void Update()
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || arrivePoint == null) return;
        float d2 = (lp.GetPosition() - arrivePoint.position).sqrMagnitude;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            if (!visited && museum != null && museum._IsInside()) visited = true;
            SetRoom(d2 <= showRange * showRange);
            int l = Lang();
            if (l != shownLang && roomOn) ShowText();
        }
        UpdateBgm(d2 <= bgmRange * bgmRange);
        // 演出は部屋の近くにいる時だけ
        if (d2 > activeRange * activeRange)
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

    // 部屋に入るとゆっくり大きく、出るとゆっくり消して止める（博物館の曲と同じ）
    void UpdateBgm(bool inside)
    {
        if (bgm == null) return;
        float want = inside ? bgmVolume : 0f;
        bgmCur = Mathf.MoveTowards(bgmCur, want, Time.deltaTime * bgmVolume / Mathf.Max(bgmFade, 0.1f));
        bgm.volume = bgmCur;
        if (want > 0f && !bgm.isPlaying) bgm.Play();
        if (want <= 0f && bgmCur <= 0f && bgm.isPlaying) bgm.Stop();
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
