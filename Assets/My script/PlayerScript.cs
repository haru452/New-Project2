
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class PlayerScript : UdonSharpBehaviour
{
    public Material sky1,sky2_space,sky3_sea;
    void Start()
    {
        RenderSettings.skybox = sky1;
    }
    void Update()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        float playerY = player.GetPosition().y;
        if(playerY >= 1200.0f)
        {
            RenderSettings.skybox = sky2_space;
            player.SetGravityStrength(0.25f);
        }
        else if(playerY <= -27.0f)
        {
            RenderSettings.skybox = sky3_sea;
            player.SetGravityStrength(1.0f);
        }
        else
        {
            RenderSettings.skybox = sky1;
            player.SetGravityStrength(1.0f);
        }
    }
}
