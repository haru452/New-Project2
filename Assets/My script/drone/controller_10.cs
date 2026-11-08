using UdonSharp;
using UnityEngine;
using TMPro;

// controller.cs の続き: レーダーの縁に、行き先の方向を矢印で出す（乗っている間ずっと）
//  ・イ402（黄）・アストラル空港（水色）・敵のペラゴス基地（赤）
//  ・遊覧モードでは空母（白）も。空母がまだ出てきていない時は出さない
//  ・矢印はレーダーの縁、文字（名前と距離）は矢印の内側。レーダーと同じく機首が上
//  ・部品は FuelGuideSetup（Tools/レーダーに行き先の矢印を付ける）が作る
public partial class controller
{
    [Header("レーダーの行き先の矢印（FuelGuideSetup が作る）")]
    public GameObject fuelGuide;               // 矢印と文字をまとめた物
    public Transform guideSubArrow, guideAstralArrow, guideBaseArrow, guideCvArrow;
    public TextMeshPro guideSubText, guideAstralText, guideBaseText, guideCvText;
    public Transform guideSubLabel, guideAstralLabel, guideBaseLabel, guideCvLabel;   // 文字の置き場所（TMP の transform / gameObject は Udon で使えないため別に持つ）
    private float nextGuideText;

    void UpdateFuelGuide(Vector3 me, Vector3 f, Vector3 r)
    {
        if (fuelGuide == null) return;
        bool show = isPiloting && !usingGunnerRadar;
        if (fuelGuide.activeSelf != show) fuelGuide.SetActive(show);
        if (!show) return;
        bool txt = Time.time >= nextGuideText;   // 文字は 0.5 秒ごと（毎フレーム作り直すと重い）
        if (txt) nextGuideText = Time.time + 0.5f;
        PlaceGuide(respawnPoint, guideSubArrow, guideSubLabel, guideSubText, "I-402", me, f, r, txt);
        PlaceGuide(astralAirport, guideAstralArrow, guideAstralLabel, guideAstralText, "ASTRAL", me, f, r, txt);
        PlaceGuide(baseCenter, guideBaseArrow, guideBaseLabel, guideBaseText, "PELAGOS", me, f, r, txt);
        bool cv = gameMode != null && gameMode._IsSightseeing() && carrier != null && (carrierShip == null || carrierShip._Present());
        PlaceGuide(cv ? carrier : null, guideCvArrow, guideCvLabel, guideCvText, "CARRIER", me, f, r, txt);
    }

    void PlaceGuide(Transform target, Transform arrow, Transform labelTf, TextMeshPro label, string name, Vector3 me, Vector3 f, Vector3 r, bool txt)
    {
        if (arrow == null) return;
        bool on = target != null;
        if (labelTf != null && labelTf.gameObject.activeSelf != on) labelTf.gameObject.SetActive(on);
        if (!on) { if (arrow.gameObject.activeSelf) arrow.gameObject.SetActive(false); return; }
        Vector3 d = target.position - me;
        d.y = 0f;
        float x = Vector3.Dot(d, r), y = Vector3.Dot(d, f);
        float len = Mathf.Sqrt(x * x + y * y);
        Vector2 u = len > 0.1f ? new Vector2(x / len, y / len) : new Vector2(0f, 1f);
        // レーダーの範囲の中（島や船がレーダーに映っている）: 矢印は消して、その場所に名前を出す
        bool inside = len < radarRange * 0.86f;
        if (arrow.gameObject.activeSelf == inside) arrow.gameObject.SetActive(!inside);
        if (inside)
        {
            if (labelTf != null) labelTf.localPosition = new Vector3(x / radarRange * radarRadius, y / radarRange * radarRadius, labelTf.localPosition.z);
        }
        else
        {
            float ang = Mathf.Atan2(u.x, u.y) * Mathf.Rad2Deg;
            arrow.localPosition = new Vector3(u.x * radarRadius * 0.86f, u.y * radarRadius * 0.86f, arrow.localPosition.z);
            arrow.localRotation = Quaternion.Euler(0f, 0f, -ang);   // 矢印の絵は上向き
            if (labelTf != null) labelTf.localPosition = new Vector3(u.x * radarRadius * 0.6f, u.y * radarRadius * 0.6f, labelTf.localPosition.z);
        }
        if (txt && label != null) label.text = name + "\n<size=80%>" + (len / 1000f).ToString("F1") + "km</size>";
    }
}
