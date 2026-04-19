
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
     private float maxSpeed = 5f;
     public Material sky1,sky2;
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
                speed+=0.01f;
            }
            if(Input.GetButtonUp("Shift")){
                 speed=0f;
                 station.ExitStation(Networking.LocalPlayer);
                 car.transform.rotation = Quaternion.identity;
                 //ReturnToPool();
            }
            if(Input.GetButtonUp("Fire1")){
                 speed-=0.01f;
            }
             DiveintoSea();

            car.transform.Rotate(horizontalInput, 0, verticalInput);
            car.transform.Translate(-speed, 0, 0);
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
            RenderSettings.skybox = sky2;
        }
        else
        {
            RenderSettings.skybox = sky1;
        }
    }
}

