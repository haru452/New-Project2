using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// =============================================================
//  パイロット同士の無線（電話のように距離に関係なく話せる）
//  ・自分が操縦中のとき、同じく操縦中のほかのパイロットの声を
//    遠くまで・小さくならずに聞こえるようにする
//  ・どちらかが降りたら、普通の声の聞こえ方に戻す
//  ・声の聞こえ方は「聞く側」の設定なので、通信は不要
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PilotRadio : UdonSharpBehaviour
{
    public controller[] planes;
    [Tooltip("無線の声の大きさ（VRChat の標準は 15）")]
    public float radioGain = 7f;   // 無線の声の大きさ（拾いすぎないよう標準の 15 より小さく）
    [Tooltip("この距離まで声が届く (m)")]
    public float radioRange = 100000f;
    public float checkInterval = 0.5f;
    public VolumeSettings volume;   // main room の音の大きさの設定（仲間の無線の声）

    // VRChat の標準の声の設定
    private const float DefaultGain = 15f;
    private const float DefaultNear = 0f;
    private const float DefaultFar = 25f;

    private int[] radioIds = new int[100];   // いま無線にしている人の playerId
    private int radioIdCount;

    void Start()
    {
        SendCustomEventDelayedSeconds(nameof(_Tick), checkInterval);
    }

    public void _Tick()
    {
        SendCustomEventDelayedSeconds(nameof(_Tick), checkInterval);
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || planes == null) return;

        // 自分が操縦しているか
        controller myPlane = null;
        for (int i = 0; i < planes.Length; i++)
        {
            if (planes[i] == null) continue;
            VRCPlayerApi p = planes[i]._GetPilot();
            if (p != null && p.isLocal) { myPlane = planes[i]; break; }
        }

        int n = VRCPlayerApi.GetPlayerCount();

        int[] newIds = new int[Mathf.Max(n, planes.Length)];
        int newCount = 0;
        if (myPlane != null)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                if (planes[i] == null) continue;
                VRCPlayerApi p = planes[i]._GetPilot();
                if (p == null || !Utilities.IsValid(p) || p.isLocal) continue;
                newIds[newCount++] = p.playerId;
            }
        }

        // 無線にする
        for (int i = 0; i < newCount; i++)
        {
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(newIds[i]);
            if (!Utilities.IsValid(p)) continue;
            p.SetVoiceGain(radioGain * (volume != null ? volume._TeamRadio() : 1f));
            p.SetVoiceDistanceNear(radioRange * 0.999f);   // 遠くても小さくならない
            p.SetVoiceDistanceFar(radioRange);
            p.SetVoiceLowpass(false);                      // 遠くの声がこもらないように
        }

        // 無線から外れた人は標準に戻す
        for (int i = 0; i < radioIdCount; i++)
        {
            int id = radioIds[i];
            bool still = false;
            for (int j = 0; j < newCount; j++) if (newIds[j] == id) { still = true; break; }
            if (still) continue;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(id);
            if (!Utilities.IsValid(p)) continue;
            p.SetVoiceGain(DefaultGain);
            p.SetVoiceDistanceNear(DefaultNear);
            p.SetVoiceDistanceFar(DefaultFar);
            p.SetVoiceLowpass(true);
        }

        if (radioIds.Length < newCount) radioIds = new int[newCount + 20];
        for (int i = 0; i < newCount; i++) radioIds[i] = newIds[i];
        radioIdCount = newCount;

        if (myPlane != null) myPlane._SetRadioCount(newCount);
    }
}
