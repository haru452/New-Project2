using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// ドアを使うと target の場所へ移動する（main room ⇔ 博物館）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DoorTeleport : UdonSharpBehaviour
{
    public Transform target;
    public AudioSource sound;   // ドアの音（無くてもよい）

    public override void Interact()
    {
        if (target == null) return;
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null) return;
        if (sound != null) sound.Play();
        lp.TeleportTo(target.position, target.rotation);
    }
}
