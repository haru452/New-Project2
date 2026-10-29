using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// =============================================================
//  博物館の VTR（約 1 分 40 秒）。分岐の間の右の通路の奥のスクリーン
//  ・通路の奥に近づくと、その人の画面だけで最初から再生する（ほかの人には関係しない）
//  ・0〜20 秒: 文字カード / 20〜60 秒: 南アルカディア海の地図 / 60〜70 秒: 通達 /
//    70〜90 秒: 基地の場面（離れた撮影所をカメラで撮って映す。なつきが R35 へ歩く） / 90〜100 秒: 最後の一枚
//  ・画像はすべて重ねた板（layerA・layerB）の色の透明度で切り替える
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class VtrPlayer : UdonSharpBehaviour
{
    [Header("スクリーンの板（Sprites/Default のマテリアル）")]
    public Renderer layerA;          // 前の画像（重ねて切り替える時の下）
    public Renderer layerB;          // 今の画像
    public Renderer caption;         // 地図の字幕・「行ってきます。」の字幕
    public Renderer film;            // 撮影所のカメラの映像（RenderTexture）
    public Renderer plane;           // 地図の上を通る R35 の影

    [Header("画像")]
    public Texture idleTex;
    public Texture[] cards;          // 文字カード 5 枚
    public Texture[] maps;           // 地図 6 枚（0 = 何もなし, 1 = ガウリア, 2 = AMA, 3 = ペラゴス・海底, 4 = 条約の線, 5 = 線が消えた）
    public Texture[] caps;           // 地図の字幕 5 枚（113, 114, 116, 120, 136）
    public Texture noticeTex, finalTex, subtitleTex;

    [Header("撮影所")]
    public Camera cam;
    public Transform[] shots;        // カメラの位置 0 = R35 の遠景, 1 = 歩いてきて振り返る, 2 = 後ろ姿を遠くから
    public GameObject studio;        // 撮影所（再生中だけ表示）
    public Transform natsuki;
    public Animator anim;
    public SkinnedMeshRenderer face;
    public int smileIndex = -1;
    public Transform walkFrom, walkStop, walkTo;
    public float walkSpeed = 1.4f;

    [Header("音")]
    public AudioSource voice;        // 「行ってきます。」
    public AudioSource wind;         // 基地の場面の環境音

    [Header("どこからどこまで流すか（秒）。入口 = 0〜60（歴史と地図）、右の通路 = 60〜100（通達・基地・最後の一枚）")]
    public float from = 0f;
    public float to = 100f;

    private float t0 = -1f;
    // 画像の場面: 始まり, 終わり, 何の画像か（0〜4 = カード, 10〜15 = 地図, 20 = 通達, 30 = 最後）, 前の画像から重ねて切り替えるか
    private float[] segS = { 0f, 4f, 8f, 12f, 16f, 20f, 22f, 30f, 40f, 45f, 50f, 60.5f, 90.8f };
    private float[] segE = { 4f, 8f, 12f, 16f, 20f, 22f, 30f, 40f, 45f, 50f, 59.6f, 69.5f, 99.4f };
    private int[] segT = { 0, 1, 2, 3, 4, 10, 11, 12, 13, 14, 15, 20, 30 };
    private bool[] segX = { false, false, false, false, false, false, true, true, true, true, true, false, false };
    private float[] capS = { 22.5f, 31f, 41f, 45.5f, 51f };
    private float[] capE = { 29.5f, 39.5f, 45f, 49.5f, 59f };
    private bool voicePlayed;
    private Vector3 walkDir;

    void Start()
    {
        ShowIdle();
        if (studio != null) studio.SetActive(false);
        if (cam != null) cam.enabled = false;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal || t0 >= 0f) return;
        Play();
    }

    public void Play()
    {
        t0 = Time.time;
        voicePlayed = false;
        if (studio != null) studio.SetActive(true);
        if (anim != null) { anim.enabled = true; anim.SetBool("walk", false); }
        if (natsuki != null && walkFrom != null) natsuki.SetPositionAndRotation(walkFrom.position, walkFrom.rotation);
        SetSmile(0f);
    }

    void ShowIdle()
    {
        Set(layerA, null, 0f);
        Set(layerB, idleTex, 1f);
        Set(caption, null, 0f);
        Set(film, null, 0f);
        Set(plane, null, 0f);
    }

    void Stop()
    {
        t0 = -1f;
        if (cam != null) cam.enabled = false;
        if (studio != null) studio.SetActive(false);
        if (wind != null) wind.Stop();
        ShowIdle();
    }

    void Set(Renderer r, Texture tex, float a)
    {
        if (r == null) return;
        Material m = r.sharedMaterial;
        if (tex != null) m.SetTexture("_MainTex", tex);
        m.SetColor("_Color", new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
        r.enabled = a > 0.001f;
    }

    Texture TexOf(int id)
    {
        if (id < 10) return (cards != null && id < cards.Length) ? cards[id] : null;
        if (id < 20) return (maps != null && id - 10 < maps.Length) ? maps[id - 10] : null;
        if (id == 20) return noticeTex;
        return finalTex;
    }

    float Ramp(float t, float s, float e, float fin, float fout)
    {
        if (t < s || t > e) return 0f;
        float a = 1f;
        if (fin > 0f) a = Mathf.Min(a, (t - s) / fin);
        if (fout > 0f) a = Mathf.Min(a, (e - t) / fout);
        return Mathf.Clamp01(a);
    }

    void Update()
    {
        if (t0 < 0f) return;
        float t = Time.time - t0 + from;
        if (t > to) { Stop(); return; }

        // ---- 画像（カード・地図・通達・最後の一枚）----
        int cur = -1;
        for (int i = 0; i < segS.Length; i++) if (t >= segS[i] && t <= segE[i]) cur = i;
        if (cur >= 0)
        {
            bool nextX = cur + 1 < segS.Length && segX[cur + 1] && Mathf.Abs(segS[cur + 1] - segE[cur]) < 0.01f;
            float fin = segX[cur] ? 1.2f : (segT[cur] == 30 ? 1.2f : 0.6f);
            float fout = nextX ? 0f : 0.6f;
            float a = Ramp(t, segS[cur], segE[cur], fin, fout);
            if (segX[cur] && cur > 0)
            {
                // 前の画像の上に、今の画像をゆっくり重ねる
                Set(layerA, TexOf(segT[cur - 1]), Ramp(t, segS[cur], segE[cur], 0f, fout));
            }
            else Set(layerA, null, 0f);
            Set(layerB, TexOf(segT[cur]), a);
        }
        else { Set(layerA, null, 0f); Set(layerB, null, 0f); }

        // ---- 地図の字幕・「行ってきます。」の字幕 ----
        float ca = 0f; Texture ct = null;
        for (int i = 0; i < capS.Length; i++)
        {
            float x = Ramp(t, capS[i], capE[i], 0.5f, 0.5f);
            if (x > 0f && caps != null && i < caps.Length) { ca = x; ct = caps[i]; }
        }
        float sa = Ramp(t, 82.2f, 86f, 0.3f, 0.5f);
        if (sa > 0f) { ca = sa; ct = subtitleTex; }
        Set(caption, ct, ca);

        // ---- 地図の上を通る R35 の影（51〜56 秒）----
        float pa = Ramp(t, 51f, 56.5f, 0.6f, 0.6f);
        if (plane != null)
        {
            Set(plane, null, pa);
            if (pa > 0f)
            {
                float u = (t - 51f) / 5.5f;
                plane.transform.localPosition = new Vector3(Mathf.Lerp(-0.42f, 0.42f, u), Mathf.Lerp(-0.30f, 0.30f, u), plane.transform.localPosition.z);
            }
        }

        // ---- 基地の場面（70〜90 秒）----
        bool filming = cam != null && t >= 69.5f && t <= 90.6f;
        if (cam != null && cam.enabled != filming) cam.enabled = filming;
        Set(film, null, Ramp(t, 70f, 90f, 0.8f, 0.8f));
        if (filming)
        {
            UpdateNatsuki(t);
            if (t < 73f)
            {
                // R35 の遠景（少し横へ流す）
                if (shots != null && shots.Length > 0 && shots[0] != null)
                {
                    Transform s = shots[0];
                    float k = (t - 70f) / 3f;
                    cam.transform.SetPositionAndRotation(s.position + s.forward * 1.2f * k + s.right * 2.5f * k, s.rotation);
                }
                followInit = false;
            }
            else FollowCam(t);
            if (wind != null && !wind.isPlaying && t < 89f) wind.Play();
        }
        else if (wind != null && wind.isPlaying) wind.Stop();
        if (!voicePlayed && t >= 82f) { voicePlayed = true; if (voice != null) voice.Play(); }
    }

    void UpdateNatsuki(float t)
    {
        if (natsuki == null || walkFrom == null || walkStop == null || walkTo == null) return;
        Vector3 a = walkFrom.position, b = walkStop.position, c = walkTo.position;
        Quaternion fwd1 = Quaternion.LookRotation(Flat(b - a), Vector3.up);
        Quaternion fwd2 = Quaternion.LookRotation(Flat(c - b), Vector3.up);
        Quaternion back = Quaternion.LookRotation(Flat(a - b), Vector3.up);   // 振り返る = 後ろ（ついて来るカメラの方）
        bool walk = false;
        if (t < 73f) { natsuki.SetPositionAndRotation(a, fwd1); }
        else if (t < 76f)
        {
            float u = Mathf.Clamp01((t - 73f) / 3f);
            natsuki.SetPositionAndRotation(Vector3.Lerp(a, b, u), fwd1);
            walk = true;
        }
        else if (t < 80f)
        {
            // 一度、振り返り、少しだけ笑う
            Quaternion r = fwd1;
            if (t < 76.9f) r = Quaternion.Slerp(fwd1, back, (t - 76f) / 0.9f);
            else if (t < 78.7f) r = back;
            else r = Quaternion.Slerp(back, fwd2, Mathf.Clamp01((t - 78.7f) / 0.9f));
            natsuki.SetPositionAndRotation(b, r);
            float sm = t < 77.1f ? 0f : (t < 77.6f ? (t - 77.1f) / 0.5f : (t < 78.4f ? 1f : Mathf.Max(0f, 1f - (t - 78.4f) / 0.6f)));
            SetSmile(sm * 70f);
        }
        else
        {
            SetSmile(0f);
            float dist = Vector3.Distance(b, c);
            float u = Mathf.Clamp01((t - 80f) * walkSpeed / Mathf.Max(dist, 0.1f));
            natsuki.SetPositionAndRotation(Vector3.Lerp(b, c, u), fwd2);
            walk = u < 1f;
        }
        if (anim != null) anim.SetBool("walk", walk);
    }

    // なつきの後ろからついて行くカメラ（肩ごしに少し右・目より少し上）。R35 へ向かう後半は、だんだん離れて後ろ姿を遠くから
    private bool followInit;
    private Vector3 camPos, camLook;
    void FollowCam(float t)
    {
        if (natsuki == null || walkFrom == null || walkStop == null || walkTo == null) return;
        Vector3 dir = t < 80f ? Flat(walkStop.position - walkFrom.position) : Flat(walkTo.position - walkStop.position);
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        float dist = t < 80f ? 3.6f : Mathf.Lerp(3.6f, 8.5f, Mathf.Clamp01((t - 81f) / 8f));
        float hgt = t < 80f ? 1.55f : Mathf.Lerp(1.55f, 2.3f, Mathf.Clamp01((t - 81f) / 8f));
        Vector3 np = natsuki.position;
        Vector3 wantPos = np - dir * dist + Vector3.up * hgt + side * 0.45f;
        Vector3 wantLook = np + Vector3.up * 1.25f + dir * 2.5f;
        if (!followInit) { camPos = wantPos; camLook = wantLook; followInit = true; }
        float k = Mathf.Clamp01(Time.deltaTime * 2.5f);
        camPos = Vector3.Lerp(camPos, wantPos, k);
        camLook = Vector3.Lerp(camLook, wantLook, k);
        cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(camLook - camPos, Vector3.up));
    }

    Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized; }

    void SetSmile(float w)
    {
        if (face != null && smileIndex >= 0) face.SetBlendShapeWeight(smileIndex, w);
    }
}
