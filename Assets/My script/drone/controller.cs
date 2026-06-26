
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

public class controller : UdonSharpBehaviour
{
     private VRCStation station;
     public GameObject car;
     public bool condention =false;
     float horizontalInput;
     float verticalInput;
     bool jumpInput =false;
     public float speed = 0f;
     private float maxSpeed = 100f;
     //private float gravity =-9.8f;
    // public Material sky1,sky2;
     //public VRCObjectPool dronePool;

    void Start()
    {
          station = GetComponent<VRCStation>();

    }
    void Update()
    {
       
        if (condention)
        {
            Debug.Log("Horizontal input: " + horizontalInput);
            Debug.Log("Vertical input: " + verticalInput);
            Debug.Log("Jump input: " + jumpInput);
  
            
            if (jumpInput&& speed<maxSpeed)
            {
                speed+=10.0f;
            }
            else if (!jumpInput&& speed>0.0f)
            {
                speed-=0.01f;
            }
            if(Input.GetButtonUp("Shift")){
                 speed=0f;
                 station.ExitStation(Networking.LocalPlayer);
                 car.transform.rotation = Quaternion.identity;
                 //ReturnToPool();
            }
            if(Input.GetButtonUp("Fire1")){
                 speed-=5.0f;
            }
           
             //DiveintoSea();

            car.transform.Rotate(horizontalInput, 0, verticalInput);
            car.transform.Translate(-speed * Time.deltaTime, 0, 0);
             if(car.transform.position.y >1000.0f)
            {
               Vector3 pos = car.transform.position;
                pos.y = 1000.0f; // 高さを1000に固定
                car.transform.position = pos;
            }
        }
    }
     public override void OnStationEntered(VRCPlayerApi player)
    {
        // 誰かが入ったら、他の人はもう使えないようにする
        // DisableInteractiveはローカルでのみ設定すればOK
        this.DisableInteractive = true;
        condention = true;
    }
     public override void OnStationExited(VRCPlayerApi player)
    {
        // 誰も使っていなければ、再度使えるようにする
        this.DisableInteractive = false;
        condention = false;
    }
    ///////////////////////////////////////////////////////////////////////////////////////////////////
     public override void InputMoveHorizontal(float value, UdonInputEventArgs args)
    {
        /*if(condention)
        {
            Debug.Log("Horizontal input: " + value);
            car.transform.Translate(0, 0, value);
        }*/
        horizontalInput = value;/*
    }*/}
    public override void InputMoveVertical(float value, UdonInputEventArgs args)
    {
        /*if(condention)
        {
            Debug.Log("Vertical input: " + value);
            car.transform.Translate(-value, 0, 0);
        }*/
        verticalInput = value;
    }
    public override void InputJump(bool value, UdonInputEventArgs args)
    {
        /*if (condention&& value)
        {
            Debug.Log("Jump button pressed");
            car.transform.Translate(0, 1, 0);
        }*/
        jumpInput = value;
    }
    /*void Input.Getbutton("Run")
    {
        speeddown = true;
    }*/
    /////////////////////////////////////////////////////////////
   /*public void ReturnToPool()
    {
        if (dronePool != null)
        {
            dronePool.Return(this.gameObject);
        }
        else
        {
            // プールが設定されていない場合は単純に非表示にする
            this.gameObject.SetActive(false);
        }
    }*/
    void DiveintoSea()
    {
        if(car.transform.position.y < -27.0f)
        {
           // RenderSettings.skybox = sky2;
        }
        else
        {
           // RenderSettings.skybox = sky1;
        }
    }
    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        //RenderSettings.skybox = sky1;
         car.transform.position = new Vector3(0, 0, 0);
         this.DisableInteractive = false;
         condention = false;
         speed = 0f;
         
    }
}

