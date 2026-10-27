using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// =============================================================
//  固定砲台（自分の画面だけで動く・通信なし。WarShip と同じ仕組み）
//  ・自分のドローンが range 以内、海面から maxAltitude 以下に来たら砲撃
//  ・砲身の -Z 方向（barrelIsMinusZ がオンの時）に弾が出る
//  ・撃つ前に、砲身が狙う方向へ向くまで turret を回転させる（向いたら発射）
//  ・先読み + 重力で落ちる分を上に狙う。ばらつきあり
//  ・FlightTarget（同じオブジェクトに付ける）で壊れる:
//      HP が減ると煙 → 炎、0 で爆発 → 壊れた見た目（wreckModel）+ 炎上、
//      respawnTime 秒後に復活
//  ※ 壊れている間は撃たない。撃った弾は飛び続ける
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class FixedCannon : UdonSharpBehaviour
{
    [Header("参照")]
    public controller[] planes;
    public FlightTarget target;           // 耐久値（同じオブジェクトの FlightTarget）
    [Tooltip("回転する部分（砲塔・砲身）。空ならこのオブジェクト全体を回す")]
    public Transform turret;
    [Tooltip("砲口。turret の子にして、-Z が撃つ向きになるように置く。空なら turret の位置から撃つ")]
    public Transform muzzle;
    [Tooltip("オン: 砲口の -Z が撃つ向き（逆向き）/ オフ: +Z が撃つ向き")]
    public bool barrelIsMinusZ = true;
    public Transform[] shells;            // 砲弾の見た目（プール）。回転する物・壊れて消える物の子にしない
    public ParticleSystem muzzleFx;       // 発射の閃光と煙
    public AudioSource cannonAudio;       // 発射音
    public ParticleSystem splashFx;       // 海に落ちた時の水柱
    public ParticleSystem impactFx;       // 爆発（着弾）
    public AudioSource impactAudio;
    public float seaLevel = -27.7f;

    [Header("砲撃")]
    public float range = 1200f;           // この距離以内（水平）で撃つ (m)
    public float maxAltitude = 400f;      // 海面からこの高さ以下で撃つ (m)
    [Tooltip("オン: 警告の間は撃たず、攻撃開始（hostile）になってから撃つ（他の敵と同じ）")]
    public bool onlyWhenHostile = true;
    public float turnSpeed = 40f;         // 砲身が向きを変える速さ（度/秒）
    public float fireAngle = 3f;          // 狙いとの角度がこれ以内になったら撃つ（度）
    public float fireInterval = 3f;       // 秒
    public float shellSpeed = 140f;       // m/s
    public float spreadDeg = 1.5f;        // ばらつき（度）
    public float damage = 25f;
    public float hitRadius = 8f;
    [Tooltip("狙う相手がいない時は、最初の向きへゆっくり戻る")]
    public bool returnToRest = true;
    public bool debugLog = true;

    [Header("対空砲（オンにすると、大砲ではなく対空砲火になる）")]
    public bool antiAir = false;
    public GameMode gameMode;             // 遊覧モードでは撃たない
    public float aaRange = 2500f;         // この距離以内で撃つ (m)
    public float aaMaxAltitude = 2500f;
    public float aaShellSpeed = 550f;     // m/s
    public float aaInterval = 0.45f;      // 秒
    public float aaSpread = 2.5f;         // 度
    public float aaDamage = 6f;
    public float flakRadius = 16f;        // 空中で炸裂した時、この距離以内の機体にダメージ
    public float missileKillRadius = 12f; // この距離以内のミサイルを撃ち落とす
    [Tooltip("プレイヤーがミサイルを撃ったら、機体ではなくミサイルを狙う")]
    public bool targetMissiles = true;

    [Header("壊れた時の見た目と炎（FlightTarget の body の外に置く）")]
    [Tooltip("壊れている間だけ表示する見た目（残骸）。空なら何も出さない")]
    public GameObject wreckModel;
    public ParticleSystem dmgSmoke;       // ダメージを受けた時の煙
    public ParticleSystem dmgFire;        // HP が半分より減った時の炎
    public ParticleSystem wreckSmoke;     // 壊れた後の黒煙
    public ParticleSystem wreckFire;      // 壊れた後の炎

    private Vector3[] sPos, sVel;
    private float[] sAge;
    private float[] sFuse;                // 対空砲: この時間で空中炸裂
    private bool[] sActive;
    private float nextFire;
    private float nextFx;
    private float nextLog;
    private int groundMask;
    private Quaternion restLocalRot;
    private controller mine;
    private Rigidbody mineRb;

    void Start()
    {
        if (turret == null) turret = transform;
        restLocalRot = turret.localRotation;
        int n = shells != null ? shells.Length : 0;
        sPos = new Vector3[n];
        sVel = new Vector3[n];
        sAge = new float[n];
        sFuse = new float[n];
        sActive = new bool[n];
        groundMask = ~((1 << 9) | (1 << 10) | (1 << 18));   // プレイヤー・ミラーは除く
        nextFire = Time.time + 3f;
    }

    // 砲身が向いている方向（-Z が撃つ向き）
    Vector3 Barrel()
    {
        Transform b = muzzle != null ? muzzle : turret;
        return barrelIsMinusZ ? -b.forward : b.forward;
    }

    Vector3 MuzzlePos()
    {
        return muzzle != null ? muzzle.position : turret.position;
    }

    void RefreshMine()
    {
        mine = null;
        mineRb = null;
        if (planes == null) return;
        for (int i = 0; i < planes.Length; i++)
        {
            if (planes[i] == null) continue;
            VRCPlayerApi p = planes[i]._GetPilot();
            if (p != null && p.isLocal) { mine = planes[i]; break; }
        }
        if (mine != null && mine.car != null) mineRb = mine.car.GetComponent<Rigidbody>();
    }

    void Update()
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null) return;
        float dt = Time.deltaTime;

        // 飛んでいる弾は、砲台が壊れても動かし続ける
        UpdateShells(dt);

        // 0.5 秒ごと: 自分のドローン探し・壊れた見た目・煙と炎
        if (Time.time >= nextFx)
        {
            nextFx = Time.time + 0.5f;
            RefreshMine();
            UpdateFx();
        }

        if (target != null && target._IsDestroyed()) return;

        bool haveTarget = false;
        Vector3 dir = Vector3.forward;
        if (antiAir)
        {
            AimAA(dt);
            return;
        }
        if (mine != null && mine.car != null && (!onlyWhenHostile || mine._IsHostile()))
        {
            Vector3 tp = mine.car.transform.position;
            Vector3 tv = mineRb != null ? mineRb.velocity : Vector3.zero;
            Vector3 mp = MuzzlePos();
            Vector3 flat = tp - mp;
            flat.y = 0f;
            bool inRange = flat.magnitude <= range && tp.y - seaLevel <= maxAltitude;
            if (debugLog && Time.time >= nextLog)
            {
                nextLog = Time.time + 3f;
                Debug.Log("[FixedCannon] " + gameObject.name + " 距離 " + flat.magnitude.ToString("F0") + "m 高さ " + (tp.y - seaLevel).ToString("F0") + "m → " + (inRange ? "砲撃" : "範囲外"));
            }
            if (inRange)
            {
                // 先読み（届くまでの時間ぶん先 + 重力で落ちる分を上に）
                float t = Vector3.Distance(mp, tp) / Mathf.Max(shellSpeed, 1f);
                Vector3 aim = tp + tv * t + Vector3.up * (0.5f * 9.81f * t * t);
                dir = (aim - mp).normalized;
                haveTarget = true;
            }
        }

        if (haveTarget)
        {
            // 砲身（-Z）が狙う方向を向くまで回す。向いたら撃つ
            Quaternion goal = Quaternion.FromToRotation(Barrel(), dir) * turret.rotation;
            turret.rotation = Quaternion.RotateTowards(turret.rotation, goal, turnSpeed * dt);
            if (Time.time >= nextFire && Vector3.Angle(Barrel(), dir) <= fireAngle)
            {
                Fire();
                nextFire = Time.time + fireInterval * Random.Range(0.8f, 1.2f);
            }
        }
        else if (returnToRest)
        {
            turret.localRotation = Quaternion.RotateTowards(turret.localRotation, restLocalRot, turnSpeed * 0.5f * dt);
        }
    }

    // ---- 対空砲: 機体（ミサイルを撃ったらミサイル）を狙い、先読みした所で炸裂する弾を撃つ ----
    private float aimFuse;
    void AimAA(float dt)
    {
        if (gameMode != null && gameMode._IsSightseeing()) return;
        if (mine == null || mine.car == null) return;
        if (onlyWhenHostile && !mine._IsHostile()) return;
        Vector3 mp = MuzzlePos();
        Vector3 tp = mine.car.transform.position;
        Vector3 tv = mineRb != null ? mineRb.velocity : Vector3.zero;
        // ミサイルが飛んでいたら、近いミサイルを優先
        if (targetMissiles)
        {
            int n = mine._MslCount();
            float bd = aaRange;
            for (int i = 0; i < n; i++)
            {
                if (!mine._MslActive(i) || !mine._MslReal(i)) continue;
                Vector3 m = mine._MslPos(i);
                float d = Vector3.Distance(m, mp);
                if (d < bd) { bd = d; tp = m; tv = mine._MslVel(i); }
            }
        }
        float dist = Vector3.Distance(tp, mp);
        if (dist > aaRange || tp.y - seaLevel > aaMaxAltitude || tp.y - seaLevel < 3f)
        {
            if (returnToRest) turret.localRotation = Quaternion.RotateTowards(turret.localRotation, restLocalRot, turnSpeed * 0.5f * dt);
            return;
        }
        float t = dist / Mathf.Max(aaShellSpeed, 1f);
        Vector3 aim = tp + tv * t;
        Vector3 dir = (aim - mp).normalized;
        Quaternion goal = Quaternion.FromToRotation(Barrel(), dir) * turret.rotation;
        turret.rotation = Quaternion.RotateTowards(turret.rotation, goal, turnSpeed * 3f * dt);
        if (Time.time >= nextFire && Vector3.Angle(Barrel(), dir) <= fireAngle * 3f)
        {
            aimFuse = Vector3.Distance(aim, mp) / Mathf.Max(aaShellSpeed, 1f) * Random.Range(0.92f, 1.08f);
            Fire();
            nextFire = Time.time + aaInterval * Random.Range(0.7f, 1.3f);
        }
    }

    void Fire()
    {
        if (shells == null || sActive == null) return;
        int slot = -1;
        for (int i = 0; i < shells.Length; i++) if (shells[i] != null && !sActive[i]) { slot = i; break; }
        if (slot < 0) return;

        Vector3 mp = MuzzlePos();
        Vector3 dir = Barrel();     // 実際に砲身が向いている方向へ撃つ
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        float spr = antiAir ? aaSpread : spreadDeg;
        dir = Quaternion.AngleAxis(Random.Range(-spr, spr), Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-spr, spr), side.normalized) * dir;

        sPos[slot] = mp;
        sVel[slot] = dir * (antiAir ? aaShellSpeed : shellSpeed);
        sAge[slot] = 0f;
        sFuse[slot] = antiAir ? aimFuse : 999f;
        sActive[slot] = true;
        shells[slot].position = mp;
        shells[slot].rotation = Quaternion.LookRotation(dir);
        shells[slot].gameObject.SetActive(true);
        TrailRenderer tr = shells[slot].GetComponent<TrailRenderer>();
        if (tr != null) tr.Clear();

        if (muzzleFx != null)
        {
            muzzleFx.transform.position = mp;
            muzzleFx.transform.rotation = Quaternion.LookRotation(dir);
            muzzleFx.Play(true);
        }
        if (cannonAudio != null && cannonAudio.clip != null)
        {
            cannonAudio.pitch = Random.Range(1.0f, 1.2f);
            cannonAudio.PlayOneShot(cannonAudio.clip, 0.9f);
        }
        if (debugLog) Debug.Log("[FixedCannon] " + gameObject.name + " 発射");
    }

    void UpdateShells(float dt)
    {
        if (sActive == null) return;
        Vector3 tp = Vector3.zero;
        bool haveMine = mine != null && mine.car != null;
        if (haveMine) tp = mine.car.transform.position;

        for (int i = 0; i < shells.Length; i++)
        {
            if (!sActive[i]) continue;
            sAge[i] += dt;
            Vector3 v = sVel[i] + Vector3.down * 9.81f * (antiAir ? 0.2f : 1f) * dt;
            Vector3 p0 = sPos[i];
            Vector3 p1 = p0 + v * dt;

            // 対空砲: 決めた時間で空中炸裂（近くの機体にダメージ・近くのミサイルを撃ち落とす）
            if (antiAir && sAge[i] >= sFuse[i])
            {
                Explode(i, p1, false);
                if (haveMine)
                {
                    if (Vector3.Distance(tp, p1) < flakRadius) mine._ExternalDamage(aaDamage);
                    int n = mine._MslCount();
                    for (int k = 0; k < n; k++)
                        if (mine._MslActive(k) && mine._MslReal(k) && Vector3.Distance(mine._MslPos(k), p1) < missileKillRadius) mine._InterceptMissile(k);
                }
                continue;
            }
            // ドローンの近くを通ったら空中で爆発
            if (haveMine && DistToSegment(tp, p0, p1) < hitRadius)
            {
                Explode(i, p1, false);
                mine._ExternalDamage(antiAir ? aaDamage : damage);
                continue;
            }

            // 島・建物などに当たったら爆発（砲台自身は除く）
            Vector3 seg = p1 - p0;
            float len = seg.magnitude;
            if (len > 0.001f)
            {
                RaycastHit[] hits = Physics.RaycastAll(p0, seg / len, len, groundMask, QueryTriggerInteraction.Ignore);
                int best = -1;
                float bestD = float.MaxValue;
                for (int k = 0; k < hits.Length; k++)
                {
                    Collider hc = hits[k].collider;
                    if (hc == null || hc.transform.IsChildOf(transform)) continue;
                    if (hits[k].distance < bestD) { bestD = hits[k].distance; best = k; }
                }
                if (best >= 0)
                {
                    Collider hc = hits[best].collider;
                    Explode(i, hits[best].point, false);
                    if (haveMine && hc.transform.IsChildOf(mine.car.transform)) mine._ExternalDamage(damage);
                    continue;
                }
            }

            // 海面に落ちたら水柱と爆発
            if (p1.y <= seaLevel)
            {
                Vector3 hp = Vector3.Lerp(p0, p1, Mathf.Clamp01((p0.y - seaLevel) / Mathf.Max(p0.y - p1.y, 0.0001f)));
                Explode(i, new Vector3(hp.x, seaLevel, hp.z), true);
                continue;
            }
            if (sAge[i] > 12f) { sActive[i] = false; shells[i].gameObject.SetActive(false); continue; }
            sPos[i] = p1;
            sVel[i] = v;
            shells[i].position = p1;
            if (v.sqrMagnitude > 0.01f) shells[i].rotation = Quaternion.LookRotation(v);
        }
    }

    // 壊れた見た目と、ダメージの煙・炎
    void UpdateFx()
    {
        if (target == null) return;
        bool dead = target._IsDestroyed();
        float hr = target._HpRatio();
        if (wreckModel != null && wreckModel.activeSelf != dead) wreckModel.SetActive(dead);
        SetRate(dmgSmoke, dead ? 0f : (hr < 0.4f ? 35f : (hr < 0.75f ? 14f : 0f)));
        SetRate(dmgFire, dead ? 0f : (hr < 0.5f ? 40f : 0f));
        SetRate(wreckSmoke, dead ? 40f : 0f);
        SetRate(wreckFire, dead ? 55f : 0f);
    }

    void SetRate(ParticleSystem ps, float r)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTimeMultiplier = r;
        if (r > 0f && !ps.isPlaying) ps.Play();
    }

    void Explode(int i, Vector3 p, bool water)
    {
        sActive[i] = false;
        shells[i].gameObject.SetActive(false);
        if (water && splashFx != null)
        {
            splashFx.transform.position = p;
            splashFx.Emit(45);
        }
        if (impactFx != null)
        {
            impactFx.transform.position = p;
            impactFx.Play(true);
        }
        if (impactAudio != null)
        {
            impactAudio.transform.position = p;
            impactAudio.Play();
        }
    }

    float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float l2 = ab.sqrMagnitude;
        if (l2 < 0.0001f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
        return Vector3.Distance(p, a + ab * t);
    }
}
