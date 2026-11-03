using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

// =============================================================
//  言語選択と字幕（自分の画面だけ）
//  ・lang: 0 = English（字幕なし）, 1 = 日本語, 2 = 中文
//  ・watch に入れた AudioSource を毎フレーム見て、音声が鳴り始めたら、その音声（clips の何番か）の字幕を出す
//    → 音声を鳴らす側のスクリプトは変えなくてよい
//  ・ギャルゲー風: 下にメッセージウィンドウ・話している人の名前の札・1 文字ずつ出る文字・終わると点滅する ▼
//    話している人（speaker）ごとに、名前と文字の色が変わる
//  ・飛行機に乗っている間: 乗っている機体のコックピットに固定した字幕（機体が動いてもぶれない）
//  ・乗っていない時: 目の前に出す
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Subtitles : UdonSharpBehaviour
{
    public AudioSource[] watch;
    public AudioClip[] clips;
    public string[] ja;
    public string[] zh;
    public string[] spkJa;            // 話している人の名前（clips と同じ並び）
    public string[] spkZh;
    public Color[] spkColor;          // 話している人の色
    public TextMeshPro text;          // 乗っていない時に目の前に出す字幕
    public TextMeshPro nameText;
    public GameObject textGo;         // 同じ物の GameObject（TMP の gameObject / transform は Udon で使えないため別に持つ）
    public Transform textTf;
    public controller[] planes;       // 機体ごとのコックピットの字幕（planes[i] の字幕 = hudTexts[i]）
    public TextMeshPro[] hudTexts;
    public TextMeshPro[] hudNames;
    public GameObject[] hudGos;
    public GameObject chooser;        // 言語選択の看板（選んだら消す）
    public float distance = 1.4f;
    public float down = 0.42f;

    private int lang;
    private AudioClip[] last;
    private bool[] was;
    private float hideAt = -1f;
    private bool shownInHud;
    // 1 文字ずつ出す
    private TextMeshPro curMsg;
    private string fullText = "";
    private float revealStart;
    private float revealDur = 0.3f;
    private int shownKey = -1;
    private Color curColor = Color.white;
    private string sizeTag = "";

    void Start()
    {
        if (watch != null) { last = new AudioClip[watch.Length]; was = new bool[watch.Length]; }
        HideAll();
    }

    public void _SetLang(int l)
    {
        lang = l;
        Debug.Log("[Subtitles] 言語 = " + l + "（0 英語 / 1 日本語 / 2 中文）。見張る音声 " + (watch == null ? 0 : watch.Length) + " 個・字幕 " + (clips == null ? 0 : clips.Length) + " 本・機体 " + (planes == null ? 0 : planes.Length) + " 機");
        hideAt = -1f;
        HideAll();
        if (chooser != null) chooser.SetActive(false);
    }

    public int _Lang() { return lang; }

    public void _ShowChooser() { if (chooser != null) chooser.SetActive(true); }

    void HideAll()
    {
        curMsg = null;
        if (textGo != null) textGo.SetActive(false);
        if (hudGos != null) for (int i = 0; i < hudGos.Length; i++) if (hudGos[i] != null) hudGos[i].SetActive(false);
    }

    // 自分が乗っている機体の番号（乗っていなければ -1）
    int MyPlane()
    {
        if (planes == null) return -1;
        for (int i = 0; i < planes.Length; i++)
        {
            if (planes[i] == null) continue;
            VRCPlayerApi p = planes[i]._GetPilot();
            if (p != null && Utilities.IsValid(p) && p.isLocal) return i;
        }
        return -1;
    }

    void Update()
    {
        if (watch == null || last == null) return;
        for (int i = 0; i < watch.Length; i++)
        {
            AudioSource s = watch[i];
            if (s == null) continue;
            bool playing = s.isPlaying;
            AudioClip c = s.clip;
            if (playing && c != null && (!was[i] || c != last[i]) && lang != 0) Show(c);
            was[i] = playing;
            last[i] = c;
        }
        if (hideAt < 0f) return;
        if (Time.time > hideAt) { hideAt = -1f; HideAll(); return; }
        // 1 文字ずつ出す。全部出たら ▼ を点滅
        if (curMsg != null)
        {
            int len = fullText.Length;
            int n = (int)Mathf.Ceil(len * Mathf.Clamp01((Time.time - revealStart) / revealDur));
            bool done = n >= len;
            int blink = done && ((int)(Time.time * 2f) % 2 == 0) ? 1 : 0;
            int key = n * 2 + blink;
            if (key != shownKey)
            {
                shownKey = key;
                string shown = fullText.Substring(0, n);
                if (blink == 1) shown = shown + " ▼";
                curMsg.text = sizeTag + shown;
            }
        }
        if (shownInHud || textTf == null) return;
        // 乗っていない時だけ、目の前に追いかけさせる
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || !Utilities.IsValid(lp)) return;
        VRCPlayerApi.TrackingData h = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 pos = h.position + h.rotation * new Vector3(0f, -down, distance);
        textTf.SetPositionAndRotation(pos, h.rotation);
    }

    void Show(AudioClip c)
    {
        if (clips == null) return;
        int idx = -1;
        for (int k = 0; k < clips.Length; k++) if (clips[k] == c) { idx = k; break; }
        if (idx < 0) { Debug.Log("[Subtitles] 字幕の一覧に無い音声: " + c.name); return; }
        string[] src = lang == 1 ? ja : zh;
        if (src == null || idx >= src.Length) return;
        string t = src[idx];
        if (t == null || t.Length == 0) return;
        string[] spk = lang == 1 ? spkJa : spkZh;
        string who = (spk != null && idx < spk.Length) ? spk[idx] : "";
        Color col = (spkColor != null && idx < spkColor.Length) ? spkColor[idx] : Color.white;

        Debug.Log("[Subtitles] 表示: " + c.name + " / 乗っている機体の番号 " + MyPlane());
        HideAll();
        TextMeshPro msg = null;
        TextMeshPro nm = null;
        int mp = MyPlane();
        if (mp >= 0 && hudTexts != null && mp < hudTexts.Length && hudTexts[mp] != null && hudGos[mp] != null)
        {
            shownInHud = true;
            msg = hudTexts[mp];
            if (hudNames != null && mp < hudNames.Length) nm = hudNames[mp];
            hudGos[mp].SetActive(true);
        }
        else if (text != null && textGo != null)
        {
            shownInHud = false;
            msg = text;
            nm = nameText;
            textGo.SetActive(true);
        }
        else return;

        if (nm != null) { nm.text = who; nm.color = col; }
        // 文字も話している人の色を少し混ぜる（白寄り）
        curColor = Color.Lerp(Color.white, col, 0.35f);
        msg.color = curColor;
        // 長い文章は文字を小さくして、ウィンドウに収める（1 文字ずつ出す途中で大きさが変わらないよう、最初に決める）
        // （fontSize は Udon で使えないので、リッチテキストの <size> を頭に付ける）
        sizeTag = t.Length <= 40 ? "" : (t.Length <= 62 ? "<size=80%>" : (t.Length <= 100 ? "<size=63%>" : "<size=50%>"));
        curMsg = msg;
        fullText = t;
        revealStart = Time.time;
        revealDur = Mathf.Clamp(t.Length * 0.05f, 0.25f, Mathf.Max(0.25f, c.length * 0.8f));
        shownKey = -1;
        msg.text = "";
        hideAt = Time.time + c.length + 0.5f;
    }
}
