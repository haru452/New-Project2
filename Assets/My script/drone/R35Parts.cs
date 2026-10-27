using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// =============================================================
//  R35 の部品を動かす（各自の画面で計算・通信なし）
//  ・エンジン: 遅い時・垂直噴射中は下向き(90°)、速いと前向き(0°)
//  ・着陸脚: 地面・水面に近い／遅い／誰も乗っていない時は出す。高く速く飛ぶと収納
//  ・ハッチ: 誰も乗っていない時は開く、乗ると閉まる
//  ・レーザー砲: 撃っている間、反動で前後に動く
//  ベクトル・点はすべて「機体本体のローカル座標」（セットアップが計算して入れる）
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class R35Parts : UdonSharpBehaviour
{
    public controller plane;
    public float seaLevel = -27.7f;
    public Vector3 lateralAxis = Vector3.right;     // 機体の横軸（エンジンの回転軸）

    public Transform[] engines;
    public Vector3[] enginePivots;
    public float engineTiltSpeed = 60f;             // 度/秒

    public Transform[] gears;
    public Vector3[] gearRetractOffsets;            // 収納した時に動く量
    public float gearDeployHeight = 20f;            // これより低いと脚を出す (m)

    public Transform hatch;
    public Vector3 hatchPivot;
    public Vector3 hatchAxis = Vector3.right;
    public float hatchOpenAngle = -70f;

    public Transform[] lasers;
    public Vector3 laserRecoil;                     // 反動で下がる量

    public float activeDistance = 1500f;            // これより遠いと動かさない（軽量化）

    private Quaternion[] engBaseRot, gearBaseRot;
    private Vector3[] engBasePos, gearBasePos, gearBaseScale, laserBasePos;
    private Quaternion hatchBaseRot;
    private Vector3 hatchBasePos;
    private float tilt = 90f, gearT = 1f, hatchT = 1f;
    private Vector3 lastPos;
    private float speed;

    void Start()
    {
        int ne = engines != null ? engines.Length : 0;
        engBaseRot = new Quaternion[ne];
        engBasePos = new Vector3[ne];
        for (int i = 0; i < ne; i++) if (engines[i] != null) { engBaseRot[i] = engines[i].localRotation; engBasePos[i] = engines[i].localPosition; }
        int ng = gears != null ? gears.Length : 0;
        gearBaseRot = new Quaternion[ng];
        gearBasePos = new Vector3[ng];
        gearBaseScale = new Vector3[ng];
        for (int i = 0; i < ng; i++) if (gears[i] != null) { gearBaseRot[i] = gears[i].localRotation; gearBasePos[i] = gears[i].localPosition; gearBaseScale[i] = gears[i].localScale; }
        int nl = lasers != null ? lasers.Length : 0;
        laserBasePos = new Vector3[nl];
        for (int i = 0; i < nl; i++) if (lasers[i] != null) laserBasePos[i] = lasers[i].localPosition;
        if (hatch != null) { hatchBaseRot = hatch.localRotation; hatchBasePos = hatch.localPosition; }
        lastPos = transform.position;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 pos = transform.position;
        // 速さは位置の変化から求める（他の人の機体でも使えるように）
        speed = Mathf.Lerp(speed, (pos - lastPos).magnitude / dt, dt * 4f);
        lastPos = pos;

        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp != null && (lp.GetPosition() - pos).sqrMagnitude > activeDistance * activeDistance) return;

        bool piloted = plane != null && plane._GetPilot() != null;
        bool boosting = plane != null && plane._IsBoosting();
        bool firing = plane != null && plane._IsFiring();

        // エンジン
        float targetTilt = (!piloted || boosting || speed < 20f) ? 90f : (speed > 45f ? 0f : 90f * (45f - speed) / 25f);
        tilt = Mathf.MoveTowards(tilt, targetTilt, engineTiltSpeed * dt);
        if (engines != null)
        {
            Quaternion r = Quaternion.AngleAxis(-tilt, lateralAxis);
            for (int i = 0; i < engines.Length; i++)
            {
                if (engines[i] == null) continue;
                Vector3 pv = enginePivots != null && i < enginePivots.Length ? enginePivots[i] : engBasePos[i];
                engines[i].localRotation = r * engBaseRot[i];
                engines[i].localPosition = pv + r * (engBasePos[i] - pv);
            }
        }

        // 着陸脚
        float alt = pos.y - seaLevel;
        bool nearSurface = alt < gearDeployHeight || Physics.Raycast(pos, Vector3.down, gearDeployHeight, ~((1 << 9) | (1 << 10) | (1 << 18)), QueryTriggerInteraction.Ignore);
        bool gearDown = !piloted || nearSurface || speed < 15f;
        gearT = Mathf.MoveTowards(gearT, gearDown ? 1f : 0f, dt * 0.8f);
        if (gears != null)
        {
            for (int i = 0; i < gears.Length; i++)
            {
                if (gears[i] == null) continue;
                bool show = gearT > 0.02f;
                if (gears[i].gameObject.activeSelf != show) gears[i].gameObject.SetActive(show);
                Vector3 off = gearRetractOffsets != null && i < gearRetractOffsets.Length ? gearRetractOffsets[i] : Vector3.zero;
                gears[i].localPosition = gearBasePos[i] + off * (1f - gearT);
                gears[i].localScale = gearBaseScale[i] * Mathf.Lerp(0.25f, 1f, gearT);
            }
        }

        // ハッチ
        hatchT = Mathf.MoveTowards(hatchT, piloted ? 0f : 1f, dt * 0.7f);
        if (hatch != null)
        {
            Quaternion hr = Quaternion.AngleAxis(hatchOpenAngle * hatchT, hatchAxis);
            hatch.localRotation = hr * hatchBaseRot;
            hatch.localPosition = hatchPivot + hr * (hatchBasePos - hatchPivot);
        }

        // レーザー砲の反動
        if (lasers != null)
        {
            float k = firing ? Mathf.PingPong(Time.time * 24f, 1f) : 0f;
            for (int i = 0; i < lasers.Length; i++)
            {
                if (lasers[i] == null) continue;
                lasers[i].localPosition = laserBasePos[i] + laserRecoil * k;
            }
        }
    }
}
