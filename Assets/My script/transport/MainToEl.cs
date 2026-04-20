
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class MainToEl : UdonSharpBehaviour
{
    public Transform target;
    void Start()
    {
        
    }
     public override void Interact()
    {
         VRCPlayerApi player = Networking.LocalPlayer;
       player.TeleportTo(target.position,target.rotation);
    }
}
