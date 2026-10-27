using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// =============================================================
//  敵機（古いドローン）
//  ・インスタンスのオーナー（基本はマスター）の画面で動きを計算し、VRC Object Sync で全員に同期
//  ・雲の下（海面から minAlt〜maxAlt）を、ランダムな目的地へ飛び回る
//  ・パイロットが乗った機体を detectRange 以内に見つけると追いかけ、
//    機首が向いて fireRange 以内なら機銃を撃つ（曳光弾と音は全員に見える）
//  ・動きは物理に沿う: 速さ一定、横向きの加速度（＝旋回の強さ）と上下の加速度に上限があり、
//    曲がる時は旋回の向きに機体を傾ける。急な方向転換や瞬間移動はしない
//  ・撃ち落とされると FlightTarget が消し、復活したらランダムな場所から再出発
//  ※ このスクリプトはドローン本体の子（EnemyBrain）に付ける。本体には VRC Object Sync があり、
//    手動同期のスクリプトを同じ物に付けられないため。動かすのは rb（本体）
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class EnemyDrone : UdonSharpBehaviour
{
    public Rigidbody rb;
    public FlightTarget target;
    public controller[] planes;
    public Transform muzzle;
    public ParticleSystem tracer;
    public AudioSource gunAudio;
    [Tooltip("機首の向き: オン = モデルの +Z（戦闘機）、オフ = -X（古いドローン）")]
    public bool noseIsZ = false;
    public GameMode gameMode;            // ハードモード: 船に見つかったプレイヤーへ遠くからでも向かう
    public ParticleSystem dmgSmoke;      // ダメージを受けた時の煙
    public ParticleSystem dmgFire;       // HP が少ない時の炎

    [Header("空母の戦闘機（甲板から発艦・範囲の外へは行かない・誰もいなければ着艦）")]
    public bool carrierBased;
    public Transform carrierRef;         // 空母（WarShip の親）。下の位置はこの中の位置
    public Vector3 parkLocal;            // 甲板の止まっている場所
    public Quaternion parkRotLocal = Quaternion.identity;
    public Vector3 deckStartLocal;       // 発艦の走り始め
    public Vector3 deckDirLocal = Vector3.forward;   // 発艦の向き
    public float deckRun = 150f;         // 甲板を走る距離 (m)
    public float carrierWarnRange = 4000f;   // 空母からこの距離にプレイヤーが来たら発艦
    public float carrierRange = 6000f;       // 空母からこの距離の中だけ飛ぶ
    public float launchDelay = 0f;           // 発艦の順番待ち（秒）
    public bool guidedMissile;               // 追尾ミサイル（よく曲がる・「誘導ミサイル接近」の警告）
    public bool baseAirfield;               // 基地の戦闘機（滑走路から発進。観光モードでは飛ばない）
    public WarShip carrierShip;              // 空母（出てくる前は、戦闘機も見えない・飛ばない）
    [UdonSynced] private int cState;         // 0 = 甲板で待機, 1 = 発艦中, 2 = 飛行中, 3 = 着艦に向かう
    private float cTimer, cNoPlayer, nextCarrierCheck, cNearest = 1e9f;

    [Header("警戒機と編隊")]
    public bool isAwacs;                 // 警戒機: 攻撃しない。見つけたら知らせる（ハード = 全戦闘機、ノーマル = 編隊の 2 機）
    public EnemyDrone[] wingmen;         // 警戒機: 編隊の戦闘機
    public EnemyDrone formationLeader;   // 戦闘機: 編隊を組む警戒機
    public Vector3 formationOffset;      // 編隊の位置（x = 右、y = 上、z = 前。警戒機の進む向きから見て）
    [UdonSynced] private int alertPlane = -1;   // 警戒機が見つけた相手（planes の番号。-1 = いない）
    private float alertLastSeen, nextSpotSend;
    private bool quiet;   // 「偵察任務」の無線が流れている間

    [Header("脚（タイヤ）: 飛んでいる間はしまう。地面の近くで出す")]
    public Transform[] gearPivots;       // 脚の付け根（ここを回して畳む）
    public Vector3[] gearFold;           // 畳んだ時の回転（度）
    public Quaternion[] gearBaseRot;     // 出し切った時の向き
    public GameObject[] gearVisuals;     // しまい切ったら隠す
    public GameObject gearRoot;          // 脚全体（LandingGear）。しまい切ったら SetActive(false)
    public float gearDownHeight = 60f;   // 真下の地面（島・滑走路・甲板）がこの高さ以内なら脚を出す (m)
    public float gearTime = 3f;          // 出し入れにかかる秒数
    public bool gearForceDown;           // 離陸・着陸の時に出す（後で基地の離着陸に使う）
    private float gearAnim = 0f, gearAnimShown = -1f, nextGearCheck;
    private bool gearWant;

    [Header("飛ぶ範囲")]
    public Vector3 areaCenter = new Vector3(150f, 0f, 75f);
    public float areaRadius = 9000f;     // 出現・巡回する広い範囲（半径 m）
    [Tooltip("出撃地点（潜水艦）。この周り avoidRadius 以内には出現しない")]
    public Vector3 avoidCenter = new Vector3(254f, 0f, 297f);
    public float avoidRadius = 2000f;   // 出撃地点からこの距離より外ならどこでも出現してよい
    [Tooltip("それぞれの機体は自分の持ち場（home）の周りこの半径を巡回する")]
    public float homeRadius = 1500f;
    [Tooltip("プレイヤー全員からこれより遠いドローンは、プレイヤーの近くへ出し直す (m)")]
    public float despawnDistance = 10000f;
    [Tooltip("出し直す時は、ランダムに選んだプレイヤーからこの距離以内 (m)")]
    public float spawnNearPlayer = 9000f;
    [Tooltip("出し直す時、どのプレイヤーからもこれ以上離す（目の前に急に現れないように）(m)")]
    public float minSpawnFromPlayers = 3500f;
    public float seaLevel = -27.7f;
    public float minAlt = 80f;
    public float maxAlt = 380f;

    [Header("飛び方（物理の上限）")]
    public float speed = 44f;            // m/s（約 160 km/h ＝ プレイヤーの機体の最高速度 約 200 km/h の 80%）
    public float maxLateralAcc = 5.5f;   // 旋回の強さの上限 (m/s²) ≒ 傾き 30°
    public float maxVerticalAcc = 3f;    // 上昇・降下の強さの上限 (m/s²)
    public float maxAlongAcc = 3f;       // 加速・減速の上限 (m/s²)
    public float rollRate = 2f;          // 機体の傾きが追いつく速さ

    [Header("攻撃")]
    [Tooltip("プレイヤーがこの距離まで近づくと追いかけて攻撃してくる (m)")]
    public float detectRange = 3000f;
    [Tooltip("追いかけている相手がこの距離より離れたらあきらめる (m)")]
    public float loseRange = 4000f;
    public float fireRange = 400f;
    [Tooltip("海面からこの高さより上を飛んでいる機体だけを狙う（甲板は海面から約 20m）")]
    public float takeoffAltitude = 40f;
    public float fireAngle = 8f;         // 度
    public float fireRate = 10f;         // 発/秒（見た目）
    public float hitCheckInterval = 0.25f;

    [UdonSynced] private bool firing;

    [Header("ミサイル（少しだけ追尾する。急に曲がればよけられる）")]
    public Transform missile;            // ミサイルの見た目（この機体専用・1 発）
    public ParticleSystem missileSplash; // 海に落ちた時の水柱
    public ParticleSystem missileBoom;   // 爆発
    public AudioSource missileBoomAudio;
    public float missileMinRange = 400f;
    public float missileMaxRange = 2000f;
    public float missileCone = 25f;       // 機首からこの角度以内なら撃つ（度）
    public float missileInterval = 12f;   // 秒
    public float missileTurnRate = 14f;   // 追尾で曲がれる速さ（度/秒）
    public float missileAccel = 60f;
    public float missileMaxSpeed = 200f;
    public float missileBurn = 5f;
    public float missileDamage = 30f;
    [UdonSynced] private int missileSeq;
    [UdonSynced] private int missileTarget = -1;   // planes の番号
    private int seenMissileSeq;
    private bool mActive;
    private Vector3 mPos, mVel;
    private float mAge;
    private int mTarget;
    private float nextMissile;

    private Vector3 waypoint;
    private float nextWaypointTime;
    private bool wasDestroyed;
    private float fireTimer;
    private float nextHitCheck;
    private int groundMask;
    private controller chasing;
    private Vector3 home;
    private bool hasHome;
    private float nextThink;
    private controller cachedPrey;
    private bool obstacleAhead;
    private VRCPlayerApi[] players = new VRCPlayerApi[90];
    private int playerCount;
    private float nextFarCheck;

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        groundMask = ~((1 << 9) | (1 << 10) | (1 << 18));
        if (rb != null)
        {
            rb.useGravity = false;
            rb.drag = 0f;
            rb.angularDrag = 2f;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
        PickWaypoint();
        // ワールドを開いた人（最初のオーナー）は、全機をランダムな場所に散らばらせてから飛ばす
        if (rb != null && Networking.IsOwner(rb.gameObject)) wasDestroyed = true;
    }

    void RefreshPlayers()
    {
        playerCount = VRCPlayerApi.GetPlayerCount();
        if (playerCount > players.Length) playerCount = players.Length;
        VRCPlayerApi.GetPlayers(players);
    }

    // 一番近いプレイヤーまでの水平距離
    float NearestPlayerDist(Vector3 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < playerCount; i++)
        {
            VRCPlayerApi pl = players[i];
            if (pl == null || !Utilities.IsValid(pl)) continue;
            Vector3 d = pl.GetPosition() - p;
            d.y = 0f;
            if (d.magnitude < best) best = d.magnitude;
        }
        return best;
    }

    // ランダムに選んだプレイヤーから spawnNearPlayer 以内、出撃地点からは離れた場所
    // （プレイヤーがいなければ広い範囲のどこか）
    Vector3 RandomFarPoint()
    {
        RefreshPlayers();
        Vector3 p = areaCenter;
        for (int tries = 0; tries < 30; tries++)
        {
            Vector3 c = areaCenter;
            float maxR = areaRadius;
            if (playerCount > 0)
            {
                VRCPlayerApi pl = players[Random.Range(0, playerCount)];
                if (pl != null && Utilities.IsValid(pl)) { c = pl.GetPosition(); maxR = spawnNearPlayer; }
            }
            // 面積が均等になるように半径は平方根で取る
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Random.value) * maxR;
            p = new Vector3(c.x + Mathf.Cos(ang) * r, 0f, c.z + Mathf.Sin(ang) * r);
            Vector3 fa = p - avoidCenter;
            fa.y = 0f;
            if (fa.magnitude < avoidRadius) continue;   // 出撃地点の 5km 以内には出ない
            if (playerCount > 0 && NearestPlayerDist(p) < minSpawnFromPlayers) continue;
            return p;
        }
        // 見つからなければ、出撃地点から 5km より外の輪のどこか
        float a2 = Random.Range(0f, Mathf.PI * 2f);
        float r2 = avoidRadius + Random.Range(200f, 3000f);
        return new Vector3(avoidCenter.x + Mathf.Cos(a2) * r2, 0f, avoidCenter.z + Mathf.Sin(a2) * r2);
    }

    void PickWaypoint()
    {
        if (!hasHome) { home = RandomFarPoint(); hasHome = true; }
        Vector2 c = Random.insideUnitCircle * homeRadius;
        waypoint = new Vector3(home.x + c.x, seaLevel + Random.Range(minAlt + 30f, maxAlt - 30f), home.z + c.y);
        // 巡回の目的地も出撃地点の 5km 以内には置かない（追いかける時は入ってよい）
        Vector3 fw = waypoint - avoidCenter;
        fw.y = 0f;
        if (fw.magnitude < avoidRadius + 200f)
        {
            Vector3 dirW = fw.sqrMagnitude > 1f ? fw.normalized : Vector3.forward;
            Vector3 pw = avoidCenter + dirW * (avoidRadius + 200f);
            waypoint = new Vector3(pw.x, waypoint.y, pw.z);
        }
        nextWaypointTime = Time.time + Random.Range(25f, 45f);
    }

    void FixedUpdate()
    {
        if (rb == null || !Networking.IsOwner(rb.gameObject)) return;
        // 撃っているかどうかの同期のため、この頭脳も本体と同じ人が持つ
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        float dt = Time.fixedDeltaTime;

        bool dead = target != null && target._IsDestroyed();
        if (dead)
        {
            wasDestroyed = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            SetFiring(false);
            return;
        }
        if (wasDestroyed && carrierBased && carrierRef != null)
        {
            // 空母の戦闘機: 復活したら甲板で待機（また近づいたら発艦）
            wasDestroyed = false;
            ParkNow();
            return;
        }
        if (carrierBased && carrierRef != null && CarrierStep(dt)) return;
        if (wasDestroyed)
        {
            // 復活: 出撃地点から離れた、広い範囲のランダムな場所に新しい持ち場を決めて再出発
            wasDestroyed = false;
            hasHome = false;
            PickWaypoint();
            Vector3 p = RandomSpawnPoint();
            if (InFormation())
            {
                // 編隊の戦闘機: 警戒機のそば（誰にも見えない時だけ）
                RefreshPlayers();
                Vector3 sl = FormationSlot();
                if (playerCount == 0 || NearestPlayerDist(sl) > minSpawnFromPlayers) p = sl;
            }
            Vector3 d0 = (waypoint - p);
            d0.y = 0f;
            Quaternion r0 = LookFor(d0.normalized, Vector3.up);
            rb.position = p;
            rb.rotation = r0;
            rb.transform.SetPositionAndRotation(p, r0);
            rb.velocity = d0.normalized * speed;
            return;
        }

        Vector3 pos = rb.position;

        // プレイヤー全員から遠すぎる（誰にも見えない）ドローンは、プレイヤーの近くへ出し直す
        if (!carrierBased && Time.time >= nextFarCheck)
        {
            nextFarCheck = Time.time + Random.Range(4f, 6f);
            RefreshPlayers();
            if (InFormation() && chasing == null)
            {
                // 編隊の戦闘機: 警戒機から離れすぎていて、誰にも見えていなければ、編隊の位置へ移る
                Vector3 slotF = FormationSlot();
                if ((slotF - pos).magnitude > 2000f && NearestPlayerDist(pos) > 4000f && NearestPlayerDist(slotF) > 4000f)
                {
                    Vector3 lvF = formationLeader._Vel();
                    if (lvF.sqrMagnitude < 1f) lvF = Vector3.forward * speed;
                    Quaternion rF = LookFor(lvF.normalized, Vector3.up);
                    rb.position = slotF;
                    rb.rotation = rF;
                    rb.transform.SetPositionAndRotation(slotF, rF);
                    rb.velocity = lvF;
                    return;
                }
            }
            else if (playerCount > 0 && NearestPlayerDist(pos) > despawnDistance)
            {
                chasing = null;
                cachedPrey = null;
                hasHome = false;
                PickWaypoint();
                Vector3 np = RandomSpawnPoint();
                Vector3 d1 = waypoint - np;
                d1.y = 0f;
                if (d1.sqrMagnitude < 1f) d1 = Vector3.forward;
                Quaternion r1 = LookFor(d1.normalized, Vector3.up);
                rb.position = np;
                rb.rotation = r1;
                rb.transform.SetPositionAndRotation(np, r1);
                rb.velocity = d1.normalized * speed;
                return;
            }
        }

        Vector3 v = rb.velocity;
        if (v.sqrMagnitude < 1f) v = Forward() * speed;

        // 目標: 近くのパイロット、いなければ目的地
        // 軽量化: 相手探しと前方の障害物チェックは 0.5 秒ごと
        if (Time.time >= nextThink)
        {
            nextThink = Time.time + 0.5f;
            quiet = gameMode != null && gameMode._Quiet();
            if (quiet) { cachedPrey = null; chasing = null; }   // 「偵察任務」の無線の間は誰も探さない・追わない
            else if (isAwacs) { AwacsScan(pos); cachedPrey = null; }   // 警戒機は追いかけない・撃たない
            else cachedPrey = FindPrey(pos);
            obstacleAhead = Physics.Raycast(pos, v.normalized, Mathf.Max(250f, v.magnitude * 4f), groundMask, QueryTriggerInteraction.Ignore);
        }
        controller prey = cachedPrey;
        Vector3 goal;
        if (prey != null)
        {
            Rigidbody prb = prey.car.GetComponent<Rigidbody>();
            Vector3 tp = prey.car.transform.position;
            Vector3 tv = prb != null ? prb.velocity : Vector3.zero;
            float lead = Vector3.Distance(pos, tp) / Mathf.Max(speed + 300f, 1f);
            goal = tp + tv * lead;
        }
        else
        {
            if (Time.time > nextWaypointTime || (waypoint - pos).sqrMagnitude < 200f * 200f) PickWaypoint();
            goal = waypoint;
        }

        // 「偵察任務」の無線の間: プレイヤーの近く（5km 以内）にいたら、一番近いプレイヤーから離れる方へ飛ぶ
        if (quiet)
        {
            RefreshPlayers();
            Vector3 away = Vector3.zero;
            float nd = float.MaxValue;
            for (int i = 0; i < playerCount; i++)
            {
                VRCPlayerApi pl = players[i];
                if (pl == null || !Utilities.IsValid(pl)) continue;
                Vector3 dp = pos - pl.GetPosition();
                dp.y = 0f;
                if (dp.magnitude < nd) { nd = dp.magnitude; away = dp; }
            }
            if (nd < 5000f && away.sqrMagnitude > 1f) goal = pos + away.normalized * 2000f + Vector3.up * (waypoint.y - pos.y);
        }
        // 空母の戦闘機: 着艦に向かう（甲板の後ろから入って、止まる場所の上で降りる）
        bool landing = false;
        if (carrierBased && carrierRef != null && cState == 3)
        {
            prey = null;
            Vector3 dd = carrierRef.TransformDirection(deckDirLocal);
            Vector3 td = carrierRef.TransformPoint(deckStartLocal);
            Vector3 approach = td - dd * 1200f + Vector3.up * 120f;   // 甲板の後ろ 1.2km・高さ 120m
            Vector3 toTd = td - pos;
            float alongD = Vector3.Dot(pos - td, dd);
            Vector3 flat = toTd; flat.y = 0f;
            if (alongD < -150f && Vector3.Angle(dd, flat) < 25f) goal = td + Vector3.up * 2f;   // 進入: 甲板へまっすぐ降りる
            else goal = approach;
            landing = flat.magnitude < 2500f;
            gearForceDown = flat.magnitude < 3000f;
            if (baseAirfield && flat.magnitude < 1500f) { ParkNow(); return; }   // 基地: 近くまで戻ったら駐機場へ（誰も見ていない時だけ着陸に向かう）
            if (toTd.magnitude < 60f || (alongD > -20f && alongD < deckRun && Mathf.Abs(pos.y - td.y) < 12f)) { ParkNow(); return; }
        }
        if (carrierBased && carrierRef != null && cState == 2 && prey == null)
        {
            // 空母のまわりを巡回（範囲の外へは行かない）
            Vector3 fc = pos - carrierRef.position; fc.y = 0f;
            if (fc.magnitude > carrierRange * 0.8f) goal = carrierRef.position + Vector3.up * (waypoint.y - carrierRef.position.y);
        }
        Vector3 dir = (goal - pos).normalized;
        float alt = pos.y - seaLevel;
        if (alt < minAlt && !landing) dir.y = Mathf.Max(dir.y, 0.3f);
        if (alt > maxAlt) dir.y = Mathf.Min(dir.y, -0.25f);
        dir.y = Mathf.Clamp(dir.y, -0.35f, 0.35f);   // 急上昇・急降下はしない
        // 前に島などがあれば上へよける（着艦の時は空母があるので、よけない）
        if (obstacleAhead && !landing) dir = (dir + Vector3.up * 1.5f).normalized;
        // 追いかけていない時に持ち場から離れすぎたら、持ち場へ戻る
        Vector3 fromC = pos - (hasHome ? home : areaCenter);
        fromC.y = 0f;
        if (prey == null && fromC.magnitude > homeRadius * 2.5f) { dir = (-fromC.normalized + Vector3.up * dir.y).normalized; }
        dir.Normalize();

        // 欲しい速度との差から加速度を出し、物理的な上限で切る
        Vector3 wantV = dir * speed;
        if (prey == null && InFormation())
        {
            // 編隊飛行: 警戒機の速さに合わせ、決まった位置へ寄っていく
            Vector3 slot = FormationSlot();
            wantV = formationLeader._Vel() + (slot - pos) * 0.3f;
            if (wantV.magnitude > speed * 1.4f) wantV = wantV.normalized * speed * 1.4f;
            if (obstacleAhead) wantV += Vector3.up * speed * 0.5f;
        }
        Vector3 acc = (wantV - v) * 0.8f;
        Vector3 vn = v.normalized;
        float along = Vector3.Dot(acc, vn);
        Vector3 side = acc - vn * along;
        float vert = side.y;
        Vector3 horiz = new Vector3(side.x, 0f, side.z);
        along = Mathf.Clamp(along, -maxAlongAcc, maxAlongAcc);
        vert = Mathf.Clamp(vert, -maxVerticalAcc, maxVerticalAcc);
        if (horiz.magnitude > maxLateralAcc) horiz = horiz.normalized * maxLateralAcc;
        Vector3 a = vn * along + horiz + Vector3.up * vert;
        rb.AddForce(a, ForceMode.Acceleration);

        // 機体の向き: 進む方向へ。旋回の加速度の分だけ傾ける（実際の飛行機と同じ）
        Vector3 liftDir = (Vector3.up * 9.81f + horiz).normalized;
        Quaternion want = LookFor(vn, liftDir);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, want, Mathf.Clamp01(dt * rollRate)));
        rb.angularVelocity = Vector3.zero;

        // 攻撃
        bool fire = false;
        if (prey != null)
        {
            Vector3 to = prey.car.transform.position - pos;
            // 警告の間は後ろについて来るだけ。攻撃開始（hostile）になってから撃つ
            fire = prey._IsHostile() && to.magnitude < fireRange && Vector3.Angle(Forward(), to) < fireAngle;
        }
        SetFiring(fire);

        // ミサイル（攻撃開始後・前方の少し離れた相手に、12 秒くらいに 1 発）
        if (prey != null && prey._IsHostile() && missile != null && !mActive && Time.time >= nextMissile)
        {
            Vector3 tm = prey.car.transform.position - pos;
            float dm = tm.magnitude;
            if (dm > missileMinRange && dm < missileMaxRange && Vector3.Angle(Forward(), tm) < missileCone)
            {
                nextMissile = Time.time + missileInterval * Random.Range(0.8f, 1.3f);
                int idx = -1;
                for (int i = 0; i < planes.Length; i++) if (planes[i] == prey) { idx = i; break; }
                if (idx >= 0)
                {
                    missileTarget = idx;
                    missileSeq++;
                    RequestSerialization();
                    LaunchMissile(idx);   // 自分（持ち主）の画面
                    seenMissileSeq = missileSeq;
                }
            }
        }
        if (fire && Time.time >= nextHitCheck)
        {
            nextHitCheck = Time.time + hitCheckInterval;
            Vector3 o = muzzle != null ? muzzle.position : pos;
            Vector3 aim = Quaternion.AngleAxis(Random.Range(-1.5f, 1.5f), Vector3.up) * Quaternion.AngleAxis(Random.Range(-1.5f, 1.5f), rb.transform.forward) * Forward();
            RaycastHit[] hits = Physics.RaycastAll(o, aim, fireRange, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider bestCol = null;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(rb.transform)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; bestCol = hits[i].collider; }
            }
            if (bestCol != null && bestCol.transform.IsChildOf(prey.car.transform))
                prey.SendCustomNetworkEvent(NetworkEventTarget.Owner, "NetEnemyHit");
        }
    }

    // 出現場所: 範囲の中のランダムな場所（パイロットのすぐ近くは避ける）
    Vector3 RandomSpawnPoint()
    {
        Vector3 p = home;
        for (int tries = 0; tries < 8; tries++)
        {
            Vector2 c = Random.insideUnitCircle * homeRadius;
            p = new Vector3(home.x + c.x, seaLevel + Random.Range(minAlt + 50f, maxAlt - 50f), home.z + c.y);
            bool near = false;
            if (planes != null)
            {
                for (int i = 0; i < planes.Length; i++)
                {
                    controller pc = planes[i];
                    if (pc == null || pc.car == null || pc._GetPilot() == null) continue;
                    if (Vector3.Distance(p, pc.car.transform.position) < detectRange * 1.5f) { near = true; break; }
                }
            }
            if (!near) break;
        }
        return p;
    }

    // ドローンの機首は -X（muzzle があればその向き）
    Vector3 Forward()
    {
        if (muzzle != null) return muzzle.forward;
        return noseIsZ ? rb.transform.forward : -rb.transform.right;
    }

    // 「機首を f、上を u」に向ける回転（ドローンは機首が -X なので補正）
    Quaternion LookFor(Vector3 f, Vector3 u)
    {
        if (noseIsZ) return Quaternion.LookRotation(f, u);
        return Quaternion.LookRotation(f, u) * Quaternion.Euler(0f, 90f, 0f);
    }

    controller FindPrey(Vector3 pos)
    {
        if (planes == null) return null;
        // 編隊の戦闘機: 警戒機が見つけた相手を攻撃しに行く
        if (InFormation())
        {
            int ap = formationLeader._AlertPlane();
            if (ap >= 0 && ap < planes.Length && planes[ap] != null && planes[ap].car != null)
            {
                VRCPlayerApi ppl = planes[ap]._GetPilot();
                if (ppl != null && Utilities.IsValid(ppl) && !planes[ap]._InBriefing() && !(gameMode != null && gameMode._AstralSafe(planes[ap].car.transform.position))) { chasing = planes[ap]; return chasing; }
            }
        }
        // 追いかけている相手は、loseRange まで離れるまで追い続ける
        if (chasing != null)
        {
            VRCPlayerApi cp = chasing._GetPilot();
            bool keep = !chasing._InBriefing() && chasing.car != null
                && (!carrierBased || carrierRef == null || Vector3.Distance(carrierRef.position, chasing.car.transform.position) < carrierRange) && (Vector3.Distance(pos, chasing.car.transform.position) < loseRange || SpottedHard(chasing));
            if (keep && gameMode != null && gameMode._AstralSafe(chasing.car.transform.position)) { keep = false; BreakAwayFromAstral(pos); }   // アストラル空港: 攻撃をやめて離れる
            if (cp != null && Utilities.IsValid(cp) && keep) return chasing;
            chasing = null;
        }
        controller best = null;
        float bd = float.MaxValue;
        for (int i = 0; i < planes.Length; i++)
        {
            controller c = planes[i];
            if (c == null || c.car == null) continue;
            VRCPlayerApi p = c._GetPilot();
            if (p == null || !Utilities.IsValid(p)) continue;
            // 離陸するまで（潜水艦の甲板・水面にいる間）は襲わない
            if (c.car.transform.position.y - seaLevel < takeoffAltitude) continue;
            if (c._InBriefing()) continue;   // 偵察任務の無線が流れている間は探さない
            if (gameMode != null && gameMode._AstralSafe(c.car.transform.position)) continue;   // アストラル空港のまわり（言い合いの後）
            if (carrierBased && carrierRef != null && Vector3.Distance(carrierRef.position, c.car.transform.position) > carrierRange) continue;   // 空母の戦闘機は範囲の中だけ
            float d = Vector3.Distance(pos, c.car.transform.position);
            bool sh = SpottedHard(c);
            if (!sh && d >= detectRange) continue;
            float key = sh ? d - 1000000f : d;   // 船に見つかった機体を優先
            if (key < bd) { bd = key; best = c; }
        }
        chasing = best;
        return best;
    }

    // ハードモードで、船に見つかっている機体
    bool SpottedHard(controller c)
    {
        return gameMode != null && gameMode._IsHard() && c != null && c._IsSpotted();
    }

    // アストラル空港から 4km 離れた所へ向かう
    void BreakAwayFromAstral(Vector3 pos)
    {
        if (gameMode == null || gameMode.astralAirport == null) return;
        Vector3 aw = pos - gameMode.astralAirport.position; aw.y = 0f;
        if (aw.sqrMagnitude < 1f) aw = Vector3.forward;
        Vector3 w = gameMode.astralAirport.position + aw.normalized * (gameMode.astralSafeRadius + 1500f);
        waypoint = new Vector3(w.x, waypoint.y, w.z);
        nextWaypointTime = Time.time + 40f;
        SetFiring(false);
    }

    public bool _IsFiring() { return firing; }
    public int _AlertPlane() { return alertPlane; }

    // ---- 空母の戦闘機 ----
    // 甲板の止まる場所に置く（物理は止める）
    void ParkNow()
    {
        cState = 0; cTimer = 0f; cNoPlayer = 0f;
        RequestSerialization();
        chasing = null; cachedPrey = null;
        SetFiring(false);
        gearForceDown = true;
        Vector3 p = carrierRef.TransformPoint(parkLocal);
        Quaternion r = carrierRef.rotation * parkRotLocal;
        rb.isKinematic = true;
        rb.position = p;
        rb.rotation = r;
        rb.transform.SetPositionAndRotation(p, r);
    }

    // 空母から一番近い、飛んでいるプレイヤーまでの距離（1 秒ごと）
    void CarrierScan()
    {
        if (Time.time < nextCarrierCheck) return;
        nextCarrierCheck = Time.time + 1f;
        cNearest = 1e9f;
        if (planes == null) return;
        Vector3 cp = carrierRef.position;
        bool q = gameMode != null && (gameMode._Quiet() || (baseAirfield && gameMode._IsSightseeing()));
        for (int i = 0; i < planes.Length; i++)
        {
            controller c = planes[i];
            if (c == null || c.car == null || q) continue;
            VRCPlayerApi p = c._GetPilot();
            if (p == null || !Utilities.IsValid(p) || c._InBriefing()) continue;
            if (c.car.transform.position.y - seaLevel < takeoffAltitude) continue;   // 離陸前は数えない
            Vector3 d = c.car.transform.position - cp;
            d.y = 0f;
            cNearest = Mathf.Min(cNearest, d.magnitude);
        }
    }

    // 待機・発艦（true = この中で動かしたので、普通の飛び方はしない）
    bool CarrierStep(float dt)
    {
        CarrierScan();
        if (cState == 0)
        {
            // 待機: 甲板に止まったまま。プレイヤーが近づいたら、順番に発艦
            Vector3 p = carrierRef.TransformPoint(parkLocal);
            if (!rb.isKinematic) ParkNow();
            else { rb.MovePosition(p); rb.MoveRotation(carrierRef.rotation * parkRotLocal); }   // 動いている空母の甲板に乗ったまま
            bool present = carrierShip == null || carrierShip._Present();
            if (cNearest < carrierWarnRange && present) cTimer += dt; else cTimer = 0f;
            if (cTimer >= launchDelay)
            {
                cState = 1; cTimer = 0f;
                RequestSerialization();
                Vector3 sp = carrierRef.TransformPoint(deckStartLocal);
                Vector3 dd = carrierRef.TransformDirection(deckDirLocal);
                Quaternion r = LookFor(dd, Vector3.up);
                rb.position = sp; rb.rotation = r;
                rb.transform.SetPositionAndRotation(sp, r);
                rb.isKinematic = false;
                rb.velocity = Vector3.zero;
            }
            return true;
        }
        if (cState == 1)
        {
            // 発艦: 甲板の上をまっすぐ加速して、端で飛び立つ
            cTimer += dt;
            Vector3 dd = carrierRef.TransformDirection(deckDirLocal);
            Vector3 sp = carrierRef.TransformPoint(deckStartLocal);
            float run = Vector3.Dot(rb.position - sp, dd);
            float spd = Mathf.Min(speed, 6f + cTimer * 40f);   // カタパルト・滑走路で強く加速
            if (run < deckRun)
            {
                rb.velocity = dd * spd;
                rb.MoveRotation(LookFor(dd, Vector3.up));
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                cState = 2; cTimer = 0f; cNoPlayer = 0f;
                RequestSerialization();
                gearForceDown = false;
                hasHome = true;
                home = carrierRef.position;
                waypoint = home + dd * 1500f + Vector3.up * 200f;
                nextWaypointTime = Time.time + 20f;
                rb.velocity = (dd + Vector3.up * 0.3f).normalized * Mathf.Max(spd, speed * 0.8f);
            }
            return true;
        }
        // 飛行中: 範囲の中に誰もいない状態が 15 秒続いたら着艦に向かう
        if (cState == 2)
        {
            if (cNearest > carrierRange) cNoPlayer += dt; else cNoPlayer = 0f;
            if (cNoPlayer > 15f) { cState = 3; RequestSerialization(); }
        }
        else if (cState == 3 && cNearest < carrierWarnRange) { cState = 2; cNoPlayer = 0f; gearForceDown = false; RequestSerialization(); }
        return false;
    }
    public bool _Alive() { return rb != null && rb.gameObject.activeInHierarchy && (target == null || !target._IsDestroyed()); }
    public Vector3 _Pos() { return rb != null ? rb.position : transform.position; }
    public Vector3 _Vel() { return rb != null ? rb.velocity : Vector3.zero; }

    // 編隊での自分の位置（警戒機の進む向きから見て）
    Vector3 FormationSlot()
    {
        Vector3 lv = formationLeader._Vel();
        lv.y = 0f;
        Vector3 lf = lv.sqrMagnitude > 1f ? lv.normalized : Vector3.forward;
        Vector3 lr = Vector3.Cross(Vector3.up, lf);
        return formationLeader._Pos() + lr * formationOffset.x + Vector3.up * formationOffset.y + lf * formationOffset.z;
    }
    bool InFormation() { return formationLeader != null && formationLeader._Alive(); }

    // 警戒機: 広い範囲でプレイヤーを探して、見つけたら知らせる（持ち主だけ）
    void AwacsScan(Vector3 pos)
    {
        if (planes == null) return;
        int best = -1;
        float bd = detectRange;
        for (int i = 0; i < planes.Length; i++)
        {
            controller c = planes[i];
            if (c == null || c.car == null) continue;
            VRCPlayerApi p = c._GetPilot();
            if (p == null || !Utilities.IsValid(p)) continue;
            if (c.car.transform.position.y - seaLevel < takeoffAltitude) continue;   // 離陸前は見ない
            if (c._InBriefing()) continue;   // 偵察任務の無線が流れている間は見ない
            float d = Vector3.Distance(pos, c.car.transform.position);
            if (d < bd) { bd = d; best = i; }
        }
        if (best >= 0)
        {
            alertLastSeen = Time.time;
            if (alertPlane != best) { alertPlane = best; RequestSerialization(); }
            // ハードモード: 全部の戦闘機に知らせる（船に見つかった時と同じ扱い）
            if (gameMode != null && gameMode._IsHard() && Time.time >= nextSpotSend)
            {
                nextSpotSend = Time.time + 2f;
                planes[best].SendCustomNetworkEvent(NetworkEventTarget.Owner, "NetAwacsSpot");
            }
        }
        else if (alertPlane >= 0 && Time.time - alertLastSeen > 10f)
        {
            alertPlane = -1;
            RequestSerialization();
        }
    }

    void SetRate(ParticleSystem ps, float r)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTimeMultiplier = r;
        if (r > 0f && !ps.isPlaying) ps.Play();
    }

    void SetFiring(bool f)
    {
        if (firing == f) return;
        firing = f;
        RequestSerialization();
    }

    private float nextDmgCheck;

    // 他の人の画面: 新しいミサイルが撃たれたら、同じように飛ばす
    public override void OnDeserialization()
    {
        if (missileSeq != seenMissileSeq)
        {
            bool first = seenMissileSeq == 0 && !mActive && Time.timeSinceLevelLoad < 5f;
            seenMissileSeq = missileSeq;
            if (!first && missileTarget >= 0) LaunchMissile(missileTarget);
        }
    }

    // レーダー表示用
    public bool _MslActive() { return mActive; }
    public Vector3 _MslPos() { return mPos; }

    void LaunchMissile(int idx)
    {
        if (missile == null || planes == null || idx < 0 || idx >= planes.Length) return;
        Transform from = muzzle != null ? muzzle : rb.transform;
        mPos = from.position + Forward() * 4f - rb.transform.up * 1.5f;
        mVel = Forward() * (speed + 25f);
        mAge = 0f;
        mTarget = idx;
        mActive = true;
        missile.position = mPos;
        missile.rotation = Quaternion.LookRotation(mVel);
        missile.gameObject.SetActive(true);
        TrailRenderer tr = missile.GetComponent<TrailRenderer>();
        if (tr != null) { tr.Clear(); tr.emitting = true; }
        Transform flame = missile.Find("Flame");
        if (flame != null) flame.gameObject.SetActive(true);
        AudioSource au = missile.GetComponent<AudioSource>();
        if (au != null && au.clip != null) au.PlayOneShot(au.clip, 1f);
        // 狙われた本人に「Missile launch! N o'clock!」（追尾ミサイルなら「誘導ミサイル接近」）
        controller tgt = planes[idx];
        if (tgt != null)
        {
            if (guidedMissile) tgt._GuidedMissileWarning();
            else tgt._MissileWarning(mPos);
        }
    }

    void UpdateMissile(float dt)
    {
        if (!mActive) return;
        mAge += dt;
        controller tgt = (planes != null && mTarget >= 0 && mTarget < planes.Length) ? planes[mTarget] : null;
        Vector3 v = mVel;
        float sp = v.magnitude;
        bool burning = mAge < missileBurn;
        if (tgt != null && tgt.car != null && burning && sp > 1f)
        {
            Vector3 want = tgt.car.transform.position - mPos;
            v = Vector3.RotateTowards(v, want.normalized * sp, missileTurnRate * Mathf.Deg2Rad * dt, 0f);
        }
        if (burning && sp < missileMaxSpeed) v += v.normalized * missileAccel * dt;
        v += Vector3.down * 9.81f * (burning ? 0.1f : 1f) * dt;
        Vector3 p0 = mPos;
        Vector3 p1 = p0 + v * dt;
        // 近くを通ったら爆発（ダメージは狙われた本人の画面で）
        if (tgt != null && tgt.car != null)
        {
            Vector3 tp = tgt.car.transform.position;
            Vector3 ab = p1 - p0;
            float l2 = ab.sqrMagnitude;
            float t = l2 > 0.0001f ? Mathf.Clamp01(Vector3.Dot(tp - p0, ab) / l2) : 0f;
            if (Vector3.Distance(tp, p0 + ab * t) < 10f)
            {
                EndMissile(p0 + ab * t, false);
                if (tgt._IsPilotLocal()) tgt._EnemyMissileHit(missileDamage);
                return;
            }
        }
        if (p1.y <= seaLevel) { EndMissile(new Vector3(p1.x, seaLevel, p1.z), true); return; }
        if (mAge > 14f) { EndMissile(p1, false); return; }
        mPos = p1;
        mVel = v;
        missile.position = p1;
        if (v.sqrMagnitude > 0.01f) missile.rotation = Quaternion.LookRotation(v);
        if (!burning)
        {
            Transform flame = missile.Find("Flame");
            if (flame != null && flame.gameObject.activeSelf) flame.gameObject.SetActive(false);
        }
    }

    void EndMissile(Vector3 p, bool water)
    {
        mActive = false;
        missile.gameObject.SetActive(false);
        if (water && missileSplash != null) { missileSplash.transform.position = p; missileSplash.Emit(50); }
        if (missileBoom != null) { missileBoom.transform.position = p; missileBoom.Play(true); }
        if (missileBoomAudio != null) { missileBoomAudio.transform.position = p; missileBoomAudio.Play(); }
    }

    // 脚: 0.5 秒ごとに真下を調べて、出す・しまうを決める（全員の画面で同じように動く）
    void UpdateGear(float dt)
    {
        if (gearPivots == null || gearPivots.Length == 0 || rb == null) return;
        if (Time.time >= nextGearCheck)
        {
            nextGearCheck = Time.time + 0.5f;
            bool down = gearForceDown;
            if (!down && rb.gameObject.activeInHierarchy && (target == null || !target._IsDestroyed()))
            {
                int mask = ~((1 << 9) | (1 << 10) | (1 << 18));   // プレイヤー・ミラーは除く
                RaycastHit hit;
                if (Physics.Raycast(rb.position, Vector3.down, out hit, gearDownHeight, mask, QueryTriggerInteraction.Ignore)
                    && hit.point.y > seaLevel + 0.5f)
                    down = true;   // 海の上ではなく、地面・甲板の近く
            }
            gearWant = down;
        }
        float want = gearWant ? 1f : 0f;
        if (gearAnim != want) gearAnim = Mathf.MoveTowards(gearAnim, want, dt / Mathf.Max(gearTime, 0.1f));
        bool vis = (gearWant || gearAnim > 0f) && (target == null || (!target._IsDestroyed() && !target._Culled()));   // しまい切ったら脚ごと非表示。撃墜された時・まだ出ていない時も非表示
        if (gearRoot != null && gearRoot.activeSelf != vis) gearRoot.SetActive(vis);
        if (gearAnim == gearAnimShown) return;
        gearAnimShown = gearAnim;
        float k = gearAnim * gearAnim * (3f - 2f * gearAnim);
        for (int i = 0; i < gearPivots.Length; i++)
        {
            if (gearPivots[i] == null) continue;
            Vector3 fe = (gearFold != null && i < gearFold.Length) ? gearFold[i] : new Vector3(-90f, 0f, 0f);
            Quaternion br = (gearBaseRot != null && i < gearBaseRot.Length) ? gearBaseRot[i] : Quaternion.identity;
            gearPivots[i].localRotation = br * Quaternion.Euler(fe * (1f - k));
        }
        if (gearVisuals != null)
            for (int i = 0; i < gearVisuals.Length; i++)
                if (gearVisuals[i] != null && gearVisuals[i].activeSelf != vis) gearVisuals[i].SetActive(vis);
    }

    // 曳光弾と音（全員の画面）
    void Update()
    {
        UpdateMissile(Time.deltaTime);
        UpdateGear(Time.deltaTime);
        // 空母がまだ出てきていない間は、甲板の戦闘機も見えない（全員の画面）
        // 空母が沈んだ・まだ出ていない → 甲板の上の戦闘機も消える（飛んでいる機体はそのまま飛び続ける）
        // 空母が遠くて見た目が消えている → 甲板の上の戦闘機も見た目だけ消す
        if (carrierBased && carrierShip != null && target != null)
        {
            bool onDeck = cState == 0 || cState == 1;
            bool cvPresent = carrierShip._Present();
            bool cvSunk = carrierShip.target != null && carrierShip.target._IsDestroyed();
            target._SetHidden(!cvPresent || (onDeck && cvSunk));
            target._SetCulled(onDeck && !carrierShip._Shown());
        }
        // ダメージの煙と炎（0.5 秒ごと）
        if (Time.time >= nextDmgCheck && target != null)
        {
            nextDmgCheck = Time.time + 0.5f;
            float hr = target._HpRatio();
            SetRate(dmgSmoke, hr <= 0f ? 0f : (hr < 0.4f ? 35f : (hr < 0.75f ? 14f : 0f)));
            SetRate(dmgFire, hr <= 0f ? 0f : (hr < 0.4f ? 40f : 0f));
        }
        if (!firing) { fireTimer = 0f; return; }
        if (target != null && target._IsDestroyed()) return;
        fireTimer -= Time.deltaTime;
        if (fireTimer > 0f) return;
        fireTimer = 1f / Mathf.Max(fireRate, 1f);
        if (tracer != null) tracer.Emit(1);
        if (gunAudio != null && gunAudio.clip != null)
        {
            gunAudio.pitch = Random.Range(1.0f, 1.15f);
            gunAudio.PlayOneShot(gunAudio.clip, 0.6f);
        }
    }
}
