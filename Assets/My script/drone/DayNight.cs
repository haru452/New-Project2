using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// =============================================================
//  昼と夜の切り替えスイッチ（メインルーム・全員共通）
//  ・空（スカイボックス）・太陽（月明かり）・環境光・霞の色を切り替える
//  ・夜だけ点く物（基地と町の窓・古い船の明かり・滑走路の灯り・飛行機の赤と緑のランプ）を出し入れする
//  ・海と雲のシェーダーには _Udon_Night（0 = 昼, 1 = 夜）を渡す（空の映り込みを暗くする）
//  ・戦艦は _IsNight() を見て、撃つ時にサーチライトを点ける
//  ・夜は 2027 年公開予定（ハードモードと同じ時期）。nightEnabled = false の間はスイッチを使えず、いつも昼
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DayNight : UdonSharpBehaviour
{
    [Header("空（インスペクターで入れ替えられます）")]
    public Material daySkybox;
    public Material nightSkybox;

    [Header("太陽・月")]
    public Light sun;
    public float daySunIntensity = 1f;
    public Color daySunColor = Color.white;
    public float nightSunIntensity = 0.12f;
    public Color nightSunColor = new Color(0.55f, 0.65f, 1f);

    [Header("環境光・霞")]
    public float dayAmbient = 1f;
    public float nightAmbient = 0.18f;
    public Color dayFog = new Color(0.70f, 0.79f, 0.88f);
    public Color nightFog = new Color(0.02f, 0.03f, 0.06f);

    [Header("夜だけ出す物")]
    public GameObject[] nightObjects;

    [Header("夜を使えるか（2027 年公開予定。公開する時は GameMode.hardEnabled と一緒にオンにする）")]
    public bool nightEnabled = false;

    [Header("スイッチの見た目")]
    public TextMeshPro label;
    public Renderer switchRenderer;
    public Material dayMat, nightMat;

    [UdonSynced] private bool night;
    private int nightId;

    void Start()
    {
        nightId = VRCShader.PropertyToID("_Udon_Night");
        if (!nightEnabled) DisableInteractive = true;   // まだ使えない: 「押す」の表示も出さない
        Apply();
    }

    public bool _IsNight() { return night && nightEnabled; }

    public override void Interact()
    {
        if (!nightEnabled) return;   // 夜はまだ使えない
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        night = !night;
        RequestSerialization();
        Apply();
    }

    public override void OnDeserialization() { Apply(); }

    void Apply()
    {
        if (!nightEnabled) night = false;   // まだ使えない間は、いつも昼
        Material sky = night ? nightSkybox : daySkybox;
        if (sky != null) RenderSettings.skybox = sky;
        if (sun != null)
        {
            sun.intensity = night ? nightSunIntensity : daySunIntensity;
            sun.color = night ? nightSunColor : daySunColor;
        }
        RenderSettings.ambientIntensity = night ? nightAmbient : dayAmbient;
        RenderSettings.fogColor = night ? nightFog : dayFog;
        VRCShader.SetGlobalFloat(nightId, night ? 1f : 0f);
        if (nightObjects != null)
            for (int i = 0; i < nightObjects.Length; i++)
                if (nightObjects[i] != null && nightObjects[i].activeSelf != night) nightObjects[i].SetActive(night);
        if (label != null && !nightEnabled)
            label.text = "<b>TIME: <color=#ffd860>DAY</color></b>\n<size=65%><color=#ff8080>NIGHT: coming in 2027</color></size>";
        else if (label != null)
            label.text = night
                ? "<b>TIME: <color=#9fb8ff>NIGHT</color></b>\n<size=65%>Press to switch to DAY</size>"
                : "<b>TIME: <color=#ffd860>DAY</color></b>\n<size=65%>Press to switch to NIGHT</size>";
        if (switchRenderer != null && dayMat != null && nightMat != null) switchRenderer.sharedMaterial = night ? nightMat : dayMat;
    }
}
