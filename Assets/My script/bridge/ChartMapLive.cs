using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// =============================================================
//  メインルームの海図台: 味方の戦闘機（プレイヤーの機体）の位置と向きを、地図の上にリアルタイムで出す
//  ・矢印 = 機体の進む向き。人が乗っている機体は明るい水色、誰も乗っていない機体は灰色
//  ・島の名前の札は、見ている人の方を向く
//  ・自分の画面だけで動く（通信なし）。0.1 秒ごとに更新
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ChartMapLive : UdonSharpBehaviour
{
    public Transform mapT;               // 地図の板
    public Vector3 p00, du, dv, nrm;     // mapT の中: 地図の左下（南西）・右（東）へ 1 枚分・上（北）へ 1 枚分・表の向き
    public float xmin, zmin, sx, sz;     // 地図に写っているワールドの範囲（m）
    public controller[] planes;
    public Transform[] markers;
    public Renderer[] markerR;
    public Material flyMat, idleMat;
    public Transform[] labels;           // 島の名前
    [Header("島の説明（名前を押すと出る）")]
    public Transform infoPanel;          // 地図の上に浮かぶ説明の板
    public Renderer infoR;
    public Material[] infoMats;          // 島ごとの説明の画像（labels と同じ順。無い島は null）
    public Vector2[] infoSizes;          // 板の大きさ（横・縦 m）
    public float infoSeconds = 20f;
    private int infoShown = -1;
    private float infoUntil;

    // 島の名前を押した: 説明を出す（同じ島をもう一度押すと消す）
    public void _ShowInfo(int i)
    {
        if (infoPanel == null || infoR == null || infoMats == null || i < 0 || i >= infoMats.Length || infoMats[i] == null) return;
        if (infoShown == i && infoPanel.gameObject.activeSelf) { infoPanel.gameObject.SetActive(false); infoShown = -1; return; }
        infoShown = i;
        infoR.sharedMaterial = infoMats[i];
        if (infoSizes != null && i < infoSizes.Length) infoPanel.localScale = new Vector3(infoSizes[i].x, infoSizes[i].y, 1f);
        infoPanel.gameObject.SetActive(true);
        infoUntil = Time.time + infoSeconds;
        next = 0f;
    }

    private Rigidbody[] rbs;
    private float next;

    void Start()
    {
        int n = planes != null ? planes.Length : 0;
        rbs = new Rigidbody[n];
        for (int i = 0; i < n; i++) if (planes[i] != null && planes[i].car != null) rbs[i] = planes[i].car.GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (mapT == null || Time.time < next) return;
        next = Time.time + 0.1f;
        Vector3 n = mapT.TransformDirection(nrm).normalized;
        Vector3 ax = mapT.TransformVector(du), az = mapT.TransformVector(dv);
        int cnt = markers != null ? markers.Length : 0;
        for (int i = 0; i < cnt; i++)
        {
            Transform m = markers[i];
            if (m == null) continue;
            controller c = (planes != null && i < planes.Length) ? planes[i] : null;
            bool on = c != null && c.car != null && c.car.activeInHierarchy;
            if (m.gameObject.activeSelf != on) m.gameObject.SetActive(on);
            if (!on) continue;
            Vector3 wp = c.car.transform.position;
            float u = Mathf.Clamp01((wp.x - xmin) / sx), v = Mathf.Clamp01((wp.z - zmin) / sz);
            m.position = mapT.TransformPoint(p00 + du * u + dv * v) + n * 0.004f;
            Rigidbody rb = (rbs != null && i < rbs.Length) ? rbs[i] : null;
            Vector3 vel = rb != null ? rb.velocity : Vector3.zero;
            if (vel.x * vel.x + vel.z * vel.z > 9f)
            {
                Vector3 dm = ax * (vel.x / sx) + az * (vel.z / sz);
                if (dm.sqrMagnitude > 1e-10f) m.rotation = Quaternion.LookRotation(-n, dm.normalized);
            }
            VRCPlayerApi p = c._GetPilot();
            bool flying = p != null && Utilities.IsValid(p);
            if (markerR != null && i < markerR.Length && markerR[i] != null && flyMat != null && idleMat != null)
            {
                Material want = flying ? flyMat : idleMat;
                if (markerR[i].sharedMaterial != want) markerR[i].sharedMaterial = want;
            }
        }
        if (infoPanel != null && infoPanel.gameObject.activeSelf && Time.time > infoUntil) { infoPanel.gameObject.SetActive(false); infoShown = -1; }
        // 島の名前: 見ている人の方を向く
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || labels == null) return;
        Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        if ((head - mapT.position).sqrMagnitude > 400f) return;   // 20m より遠い時は回さない
        for (int i = 0; i < labels.Length; i++)
        {
            Transform l = labels[i];
            if (l == null) continue;
            Vector3 d = l.position - head;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) l.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
        if (infoPanel != null && infoPanel.gameObject.activeSelf)
        {
            Vector3 d2 = infoPanel.position - head;
            d2.y = 0f;
            if (d2.sqrMagnitude > 1e-4f) infoPanel.rotation = Quaternion.LookRotation(d2.normalized, Vector3.up);
        }
    }
}
