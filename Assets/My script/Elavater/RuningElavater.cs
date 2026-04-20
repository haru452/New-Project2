
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

public class RuningElavater : UdonSharpBehaviour
{
    public bool condention;
         public VRCStation station;
    public GameObject elavater;
        public Material sky1,sky2;
        public Transform target;
            void Start()
    {
        station = GetComponent<VRCStation>();
        
    }
    public override void OnStationEntered(VRCPlayerApi player)
    {
        // 誰かが入ったら、他の人はもう使えないようにする
        // DisableInteractiveはローカルでのみ設定すればOK
        this.DisableInteractive = true;
        condention = true;
    }
    void Update()
    {
        if (condention)
        {
            elavater.transform.Translate(0, 2.0f, 0);
        }
        if(elavater.transform.position.y >= 1200.0f)
        {
            //condention = false;
            RenderSettings.skybox = sky2;
        }
        if((elavater.transform.position.y >= 1900.0f)){
            
           // SendCustomEventDelayedSeconds(60f);
           if(condention){
             VRCPlayerApi player = Networking.LocalPlayer;
            station.ExitStation(player);
           // SendCustomEventDelayedSeconds( 3.0f);
           // player.TeleportTo(target.position,target.rotation);
           }
            condention = false;
           
      // player.TeleportTo(target.position,target.rotation);
       
        }
    }
    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        RenderSettings.skybox = sky1;
        condention = false;
         elavater.transform.position = new Vector3(0, 0, 0);
         this.DisableInteractive = false;
    }
    

}
