using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 機体本体をクリックすると、その機体の操縦席に座る
// ・操縦席が埋まっている間はクリックできない
// ・後ろの席（ガンナー席）には乗れない（廃止。station2 / gunner は古いシーンの参照を壊さないために残してあるだけ）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class BoardPlane : UdonSharpBehaviour
{
    public VRCStation station;      // 操縦席
    public controller plane;
    public VRCStation station2;     // （廃止・使わない）
    public GunnerStation gunner;    // （廃止・使わない）

    public override void Interact()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        bool pilotFree = plane == null || plane._GetPilot() == null;
        if (pilotFree && station != null) station.UseStation(me);
    }

    public void _Refresh()
    {
        bool pilotFree = plane == null || plane._GetPilot() == null;
        DisableInteractive = !pilotFree;
        InteractionText = "乗る";
    }
}
