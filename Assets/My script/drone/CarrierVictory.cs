using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

// =============================================================
//  空母を倒したら（自分の画面だけ・通信なし）
//  ・空母（FlightTarget）が沈んだのを見たら、delay 秒（爆発を見る時間）待つ
//  ・その時、機体に乗っている人（操縦席・ガンナー席）だけ、座席から降ろして「勝利の部屋」へ移動する
//  ・入ってきたばかり（joinGrace 秒）の時は、もう沈んでいた空母を「今沈んだ」と見間違えないよう何もしない
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CarrierVictory : UdonSharpBehaviour
{
    public FlightTarget carrier;
    public controller[] planes;
    public GunnerStation[] gunners;
    public Transform roomPoint;          // 移動先（部屋の中）
    public float delay = 5f;
    public float joinGrace = 20f;

    [Header("部屋の文字（言語ごと: 0 English / 1 日本語 / 2 中文）")]
    public TextMeshPro roomText;
    public string[] roomMsg;
    public Subtitles subtitles;
    public JoinLog joinLog;              // 称号「生還者」（空母が沈んだ時に飛んでいた）

    private bool wasSunk;
    private float startTime;
    private float nextCheck;
    private float goAt = -1f;

    void Start()
    {
        startTime = Time.time;
        ShowText();
    }

    void ShowText()
    {
        if (roomText == null || roomMsg == null || roomMsg.Length == 0) return;
        int l = subtitles != null ? subtitles._Lang() : 0;
        if (l < 0 || l >= roomMsg.Length) l = 0;
        roomText.text = roomMsg[l];
    }

    void Update()
    {
        if (goAt > 0f && Time.time >= goAt) { goAt = -1f; Go(); }
        if (Time.time < nextCheck || carrier == null) return;
        nextCheck = Time.time + 0.5f;
        bool s = carrier._IsSunk();
        if (s && !wasSunk && Time.time - startTime > joinGrace) goAt = Time.time + delay;
        wasSunk = s;
    }

    void Go()
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || roomPoint == null) return;
        bool seated = false;
        if (planes != null)
            for (int i = 0; i < planes.Length; i++)
            {
                if (planes[i] == null) continue;
                VRCPlayerApi p = planes[i]._GetPilot();
                if (p != null && Utilities.IsValid(p) && p.isLocal) { planes[i]._ExitForVictory(); seated = true; }
            }
        if (gunners != null)
            for (int i = 0; i < gunners.Length; i++)
            {
                if (gunners[i] == null) continue;
                VRCPlayerApi g = gunners[i]._GetGunner();
                if (g != null && Utilities.IsValid(g) && g.isLocal && gunners[i].station != null) { gunners[i].station.ExitStation(lp); seated = true; }
            }
        if (!seated) return;   // 飛んでいない人は移動しない
        if (joinLog != null) joinLog._Achieve(10);   // 10 = 生還者
        ShowText();
        SendCustomEventDelayedSeconds(nameof(_Teleport), 0.3f);   // 座席から降りきってから
    }

    public void _Teleport()
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || roomPoint == null) return;
        lp.TeleportTo(roomPoint.position, roomPoint.rotation);
    }
}
