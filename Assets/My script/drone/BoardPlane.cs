using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 機体本体をクリックすると、その機体の椅子に座る
// ・操縦席が空いていれば操縦席、埋まっていれば 2 人目の席（ガンナー）へ
// ・2 席とも埋まっている間はクリックできない
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class BoardPlane : UdonSharpBehaviour
{
    public VRCStation station;      // 操縦席
    public controller plane;
    public VRCStation station2;     // ガンナー席（任意）
    public GunnerStation gunner;

    public override void Interact()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        bool pilotFree = plane == null || plane._GetPilot() == null;
        bool gunFree = gunner != null && station2 != null && gunner._GetGunner() == null;
        if (pilotFree && station != null) station.UseStation(me);
        else if (gunFree) station2.UseStation(me);
    }

    public void _Refresh()
    {
        bool pilotFree = plane == null || plane._GetPilot() == null;
        bool gunFree = gunner != null && station2 != null && gunner._GetGunner() == null;
        DisableInteractive = !(pilotFree || gunFree);
        InteractionText = pilotFree ? "乗る（操縦席）" : "乗る（ガンナー席）";
    }
}
