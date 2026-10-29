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
public partial class EnemyDrone : UdonSharpBehaviour
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
    [Tooltip("遊覧モードの歓迎の編隊での位置（-1 = 入らない）")]
    public int welcomeSlot = -1;
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
    [Tooltip("出撃する潜水艦。追いかけていない時は、この周り noFlyRadius 以内を飛ばない")]
    public Transform noFlyRef;
    [Tooltip("同じ滑走路・空母から飛び立つ仲間。ぶつかり合わない（重なって地面でのたうち回るのを防ぐ）")]
    public EnemyDrone[] noCollide;
    private float stuckT;
    private bool shadowing;
    private float extendUntil;
    private Vector3 extendGoal;
    public float noFlyRadius = 1000f;
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
    public float rollRate = 2f;          // （古い設定。今は使わない）

    [Header("飛行の物理（揚力・推力・抗力・重力）")]
    [Tooltip("失速速度 (m/s)。これより遅いと機体を支えきれずに落ちていく")]
    public float stallSpeed = 60f;
    [Tooltip("かけられる G の上限（戦闘機 7G、警戒機 2.5G）")]
    public float gLimit = 7f;
    [Tooltip("エンジン全開の加速 (m/s²) = 推力 ÷ 重さ")]
    public float thrustAcc = 12f;
    [Tooltip("水平飛行での最高速度 (m/s)。推力と空気抵抗が釣り合う速さ")]
    public float topSpeed = 175f;
    [Tooltip("ロールの速さ（度/秒）。曲がる前に、まず機体を傾ける")]
    public float rollRateDeg = 150f;
    [Tooltip("離陸の加速 (m/s²)。空母はカタパルトでこれより強く押す")]
    public float takeoffAcc = 12f;

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
        if (noCollide != null && rb != null)
        {
            Collider[] mine = rb.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < noCollide.Length; i++)
            {
                EnemyDrone o = noCollide[i];
                if (o == null || o == this || o.rb == null) continue;
                Collider[] th = o.rb.GetComponentsInChildren<Collider>(true);
                for (int a = 0; a < mine.Length; a++)
                    for (int b = 0; b < th.Length; b++)
                        if (mine[a] != null && th[b] != null) Physics.IgnoreCollision(mine[a], th[b], true);
            }
        }
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

}
