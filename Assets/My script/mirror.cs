
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class mirror : UdonSharpBehaviour
{
      public GameObject mirror2;
    void Start()
    {
        
    }
     public override void Interact()
    {
      mirror2.SetActive(mirror2.activeSelf ? false : true);
    }
}
