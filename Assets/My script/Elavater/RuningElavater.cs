
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

public class RuningElavater : UdonSharpBehaviour
{
    public bool condention=false,condention2 = false;
         public VRCStation station;
         private int count;
    public GameObject elavater;
       // public Material sky1,sky2;
        public Transform target;
        public float speed = 100.0f;
        public GameObject switch1,switch2;
            void Start()
    {
        switch1.SetActive(true);
        switch2.SetActive(false);
        station = GetComponent<VRCStation>();
        count=0;
        
    }
    public override void OnStationEntered(VRCPlayerApi player)
    {
        // 誰かが入ったら、他の人はもう使えないようにする
        // DisableInteractiveはローカルでのみ設定すればOK
        if(elavater.transform.position.y <= 35.0f){
        this.DisableInteractive = true;
        condention = true;
        }
        else if(elavater.transform.position.y >= 1940.0f){
            this.DisableInteractive = true;
            condention2 = true;
        }
    }
    void Update()
    {
        if (condention)
        {
            if (switch1.activeSelf)
            {
                switch1.SetActive(false);
            }

            elavater.transform.Translate(0, speed * Time.deltaTime, 0);
        }

        if((elavater.transform.position.y >= 1940.0f)){
            
           // SendCustomEventDelayedSeconds(60f);
           if(condention){
             VRCPlayerApi player = Networking.LocalPlayer;
             count++;
             if(player!=null)station.ExitStation(player);
           SendCustomEventDelayedSeconds(nameof(transport), 1.0f);
           //player.TeleportTo(target.position,target.rotation);
           }
            condention = false;
           if(!switch2.activeSelf)
            {
                switch2.SetActive(true);
            }
      // player.TeleportTo(target.position,target.rotation);
       
        }
        else if(elavater.transform.position.y <= 35.0f){
            if(!switch1.activeSelf)
            {
                switch1.SetActive(true);
            }
            if(switch2.activeSelf)
            {
                switch2.SetActive(false);
            }
        }
        if(condention2){
            elavater.transform.Translate(0, -speed * Time.deltaTime, 0);
        }
         if(elavater.transform.position.y <= 35.0f&&count%2==1){
            condention2 = false;
             VRCPlayerApi player = Networking.LocalPlayer;
             count++;
             if(player!=null)station.ExitStation(player);
        }
    }
void transport(){}
    

}
