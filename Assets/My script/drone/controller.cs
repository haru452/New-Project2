
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
     bool jumpInput;
     private float speed = 0f;
     private float maxSpeed = 5f;
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
            /*car.transform.Translate(horizontalInput, 0, verticalInput);
            if (jumpInput)
            {
                car.transform.Translate(0, 1, 0);
            }*/
            if (jumpInput&& speed<maxSpeed)
            {
                speed+=0.01f;
            }
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

}

