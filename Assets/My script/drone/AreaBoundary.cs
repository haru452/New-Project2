using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

// =============================================================
//  作戦範囲（自分の画面だけ）
//  ・作戦範囲 = 元の海の広さ（四角）。そこから出ると、ガウリア帝国の司令部が「作戦範囲外だ」と言う
//  ・目の前に矢印を出して、戻る方向（範囲のいちばん近い所）を指す
//  ・戻るまで、ときどき「戻れ」と言い続ける。戻ったら矢印を消す
//  ・音声（clip）が入っていなければ、文字だけで知らせる
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AreaBoundary : UdonSharpBehaviour
{
    [Header("作戦範囲（ワールド座標の四角）")]
    public Vector2 center;              // x, z
    public Vector2 halfSize = new Vector2(8000f, 8000f);
    public float margin = 60f;          // 出入りの境目でちらつかないための余裕（m）
    public float ignoreBeyond = 20000f; // 中心からこれより遠い所（海の外の勝利の部屋・歴史の間）では警告しない

    [Header("見た目")]
    public Transform arrow;             // 矢印（+Z が先）
    public GameObject arrowGo;
    public TextMeshPro msgText;         // 矢印の下の文字
    public GameObject msgGo;
    public Transform msgTf;
    public float distance = 1.3f;
    public float up = 0.16f;

    [Header("声と言葉")]
    public AudioSource radio;
    public VoiceBus voiceBus;           // 声が重ならないように順番に流す
    public AudioClip[] warnClips;       // 出た時（1 回目）
    public AudioClip[] repeatClips;     // 戻るまで、ときどき
    public AudioClip[] backClips;       // 戻った時
    public string[] warnText;           // [言語]  0 English / 1 日本語 / 2 中文
    public string[] repeatText;         // [言語 * 2 + 何番目]
    public string[] backText;           // [言語]
    public string[] speaker;            // [言語]
    public Subtitles subtitles;         // 言語を知るため
    public float repeatEvery = 15f;
    public float showFor = 7f;

    private VRCPlayerApi lp;
    private bool outside;
    private float nextSay;
    private float hideMsgAt = -1f;
    private int repeatIdx;

    void Start()
    {
        lp = Networking.LocalPlayer;
        if (arrowGo != null) arrowGo.SetActive(false);
        if (msgGo != null) msgGo.SetActive(false);
    }

    int Lang() { return subtitles != null ? subtitles._Lang() : 0; }

    void Say(AudioClip[] clips, int clipIdx, string t)
    {
        if (radio != null && clips != null && clips.Length > 0)
        {
            AudioClip c = clips[clipIdx % clips.Length];
            if (c != null)
            {
                if (voiceBus != null) voiceBus._Play(radio, c, 10f, true);   // 今の声が終わったらすぐ（重ならない）
                else { radio.clip = c; radio.Play(); }
            }
        }
        int l = Lang();
        string who = (speaker != null && l < speaker.Length) ? speaker[l] : "";
        if (subtitles != null)
        {
            // いつもの字幕と同じ所（乗っている間はコックピットに固定）。視線には付いてこない
            subtitles._ShowText(who, t, new Color(1f, 0.8f, 0.3f), showFor);
        }
        else if (msgText != null && msgGo != null)
        {
            msgText.text = "<color=#FFCC4D><b>" + who + "</b></color>\n" + t;
            msgGo.SetActive(true);
            hideMsgAt = Time.time + showFor;
        }
    }

    string Pick(string[] arr, int i)
    {
        if (arr == null || arr.Length == 0) return "";
        if (i < 0) i = 0;
        if (i >= arr.Length) i = arr.Length - 1;
        return arr[i];
    }

    public override void PostLateUpdate()
    {
        if (lp == null || !Utilities.IsValid(lp)) return;
        Vector3 p = lp.GetPosition();
        float dx = p.x - center.x, dz = p.z - center.y;
        float ax = Mathf.Abs(dx), az = Mathf.Abs(dz);
        if (ax > ignoreBeyond || az > ignoreBeyond)
        {
            // 部屋の中: 警告も矢印も出さない（「戻った」とも言わない）
            if (outside)
            {
                outside = false;
                if (arrowGo != null) arrowGo.SetActive(false);
                if (msgGo != null) msgGo.SetActive(false);
            }
            return;
        }
        bool isOut = outside
            ? (ax > halfSize.x - margin || az > halfSize.y - margin)     // 戻るときは、少し内側まで入ったら
            : (ax > halfSize.x || az > halfSize.y);

        int l = Lang();
        if (isOut && !outside)
        {
            outside = true;
            repeatIdx = 0;
            Say(warnClips, 0, Pick(warnText, l));
            nextSay = Time.time + repeatEvery;
        }
        else if (!isOut && outside)
        {
            outside = false;
            if (arrowGo != null) arrowGo.SetActive(false);
            Say(backClips, 0, Pick(backText, l));
        }
        else if (outside && Time.time > nextSay)
        {
            Say(repeatClips, repeatIdx, Pick(repeatText, l * 2 + (repeatIdx % 2)));
            repeatIdx++;
            nextSay = Time.time + repeatEvery;
        }

        if (hideMsgAt > 0f && Time.time > hideMsgAt) { hideMsgAt = -1f; if (msgGo != null) msgGo.SetActive(false); }

        VRCPlayerApi.TrackingData h = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        if (outside && arrow != null)
        {
            // 戻る先 = 作戦範囲のいちばん近い所（少し内側）
            float tx = Mathf.Clamp(p.x, center.x - halfSize.x + margin * 2f, center.x + halfSize.x - margin * 2f);
            float tz = Mathf.Clamp(p.z, center.y - halfSize.y + margin * 2f, center.y + halfSize.y - margin * 2f);
            Vector3 dir = new Vector3(tx - p.x, 0f, tz - p.z);
            if (dir.sqrMagnitude < 0.01f) dir = new Vector3(-dx, 0f, -dz);
            if (arrowGo != null && !arrowGo.activeSelf) arrowGo.SetActive(true);
            Vector3 pos = h.position + h.rotation * new Vector3(0f, up, distance);
            arrow.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.normalized, Vector3.up));
            float s = 1f + 0.12f * Mathf.Sin(Time.time * 6f);   // 目立つように、少し脈打つ
            arrow.localScale = new Vector3(s, s, s);
        }
        if (msgTf != null && msgGo != null && msgGo.activeSelf)
        {
            Vector3 mp = h.position + h.rotation * new Vector3(0f, up - 0.2f, distance);
            msgTf.SetPositionAndRotation(mp, h.rotation);
        }
    }
}
