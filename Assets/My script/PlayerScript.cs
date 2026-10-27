
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 高さで空（スカイボックス）と重力を切り替える
//  ・1200m 以上: 宇宙の空・重力 1/4
//  ・それ以外  : 普通の空・普通の重力
//  （海中の空に切り替える処理は外した。海面近くで誤って切り替わるため）
public class PlayerScript : UdonSharpBehaviour
{
    public Material sky1, sky2_space, sky3_sea;   // sky3_sea は使っていない
    private int state = -1;   // 0 = 普通, 1 = 宇宙

    void Start()
    {
        RenderSettings.skybox = sky1;
    }

    void Update()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null) return;
        int s = player.GetPosition().y >= 1200.0f ? 1 : 0;
        if (s == state) return;   // 変わった時だけ設定する
        state = s;
        if (s == 1)
        {
            RenderSettings.skybox = sky2_space;
            player.SetGravityStrength(0.25f);
        }
        else
        {
            RenderSettings.skybox = sky1;
            player.SetGravityStrength(1.0f);
        }
    }
}
