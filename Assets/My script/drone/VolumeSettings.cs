using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Persistence;
using TMPro;

// =============================================================
//  音の大きさの設定（main room のパネル。自分の画面だけ・ワールドを出ても覚えている）
//  0 = エンジン, 1 = 無線（AMA・司令部）, 2 = 仲間の無線の声（ボイスチャット）,
//  3 = 案内の声, 4 = 武器・爆発, 5 = 警報
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class VolumeSettings : UdonSharpBehaviour
{
    public const int CAT_COUNT = 6;
    public TextMeshPro[] valueTexts;        // 各行の「100%」
    public AudioSource[] radioSources;      // 1: 無線（AMA・ガウリア司令部）
    public AudioSource[] voiceSources;      // 3: 案内の声
    public AudioSource[] weaponSources;     // 4: 機銃・ミサイル・砲撃・爆発
    public AudioSource[] alarmSources;      // 5: 失速・被弾の警報、起動音
    public int maxPercent = 200;
    public int stepPercent = 10;

    private int[] percent;
    private float[] radioBase, voiceBase, weaponBase, alarmBase;

    void Start()
    {
        Init();
        Apply();
    }

    void Init()
    {
        if (percent != null) return;
        percent = new int[CAT_COUNT];
        for (int i = 0; i < CAT_COUNT; i++) percent[i] = 100;
        radioBase = Bases(radioSources);
        voiceBase = Bases(voiceSources);
        weaponBase = Bases(weaponSources);
        alarmBase = Bases(alarmSources);
    }

    float[] Bases(AudioSource[] a)
    {
        if (a == null) return new float[0];
        var b = new float[a.Length];
        for (int i = 0; i < a.Length; i++) b[i] = a[i] != null ? a[i].volume : 1f;
        return b;
    }

    // 保存してある設定を読み込む（ワールドに入った時）
    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;
        Init();
        for (int i = 0; i < CAT_COUNT; i++)
        {
            string k = "vol" + i;
            if (PlayerData.HasKey(player, k)) percent[i] = Mathf.Clamp(PlayerData.GetInt(player, k), 0, maxPercent);
        }
        Apply();
    }

    // ボタンから: cat の大きさを delta 段階変える
    public void _Step(int cat, int delta)
    {
        Init();
        if (cat < 0 || cat >= CAT_COUNT) return;
        percent[cat] = Mathf.Clamp(percent[cat] + delta * stepPercent, 0, maxPercent);
        PlayerData.SetInt("vol" + cat, percent[cat]);
        Apply();
    }

    public float _Mult(int cat)
    {
        if (percent == null || cat < 0 || cat >= CAT_COUNT) return 1f;
        return percent[cat] / 100f;
    }
    public float _Engine() { return _Mult(0); }
    public float _TeamRadio() { return _Mult(2); }

    void Apply()
    {
        SetAll(radioSources, radioBase, _Mult(1));
        SetAll(voiceSources, voiceBase, _Mult(3));
        SetAll(weaponSources, weaponBase, _Mult(4));
        SetAll(alarmSources, alarmBase, _Mult(5));
        if (valueTexts != null)
            for (int i = 0; i < valueTexts.Length && i < CAT_COUNT; i++)
                if (valueTexts[i] != null) valueTexts[i].text = percent[i] + "%";
    }

    void SetAll(AudioSource[] a, float[] b, float m)
    {
        if (a == null || b == null) return;
        for (int i = 0; i < a.Length && i < b.Length; i++)
            if (a[i] != null) a[i].volume = Mathf.Clamp01(b[i] * m);
    }
}
