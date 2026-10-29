using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// =============================================================
//  大砲を撃ってくる船（自分の画面だけで動く・通信なし）
//  ・自分のドローンが近く（range 以内）で、海面から maxAltitude 以下なら砲撃
//  ・先読みして狙うが、ばらつきがあるので多くは周りの海面に落ちて水柱
//  ・ドローンの近く（hitRadius 以内）を通ると爆発して HP を減らす
//  ・自分が showDistance より遠いときは船の見た目ごと止める（軽量化）
//  ・船が壊れている間（FlightTarget）は撃たない
//  ・艦隊に近づく（rocketRange 以内）と、追尾しないミサイル（ロケット）も撃ってくる
//    発射の瞬間に狙った先へまっすぐ加速して飛ぶだけなので、向きを変えればよけられる
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public partial class WarShip : UdonSharpBehaviour
{
    [Header("参照")]
    public controller[] planes;
    public GameObject model;              // 船の見た目（遠いと非表示）
    public FlightTarget target;           // 船の耐久値
    public Transform[] cannonMuzzles;     // 砲門（+Z が外向き）
    public Transform[] shells;            // 砲弾の見た目（プール）
    public ParticleSystem muzzleFx;       // 発射の閃光と煙
    public AudioSource cannonAudio;       // 発射音（船に付いている）
    public ParticleSystem splashFx;       // 海に落ちた時の水柱
    public ParticleSystem impactFx;       // 爆発
    public AudioSource impactAudio;
    public float seaLevel = -27.7f;

    [Header("砲撃")]
    public float range = 1000f;           // この距離以内で撃つ (m)
    public float maxAltitude = 400f;      // 海面からこの高さ以下で撃つ (m)（雲より下）
    public float fireInterval = 2.5f;     // 秒
    public float shellSpeed = 140f;       // m/s
    public float spreadDeg = 2.0f;        // ばらつき（度）
    public float damage = 25f;
    public float hitRadius = 8f;

    [Header("ミサイル（追尾しない）")]
    public Transform[] rockets;           // ミサイルの見た目（プール）
    public Transform rocketLauncher;      // 発射位置
    public float rocketRange = 1800f;     // この距離以内に近づくと撃つ (m)
    public float rocketInterval = 7f;     // 秒
    public float rocketLaunchSpeed = 30f; // m/s
    public float rocketAccel = 80f;       // m/s²
    public float rocketMaxSpeed = 260f;   // m/s
    public float rocketBurnTime = 10f;    // 秒（燃えている間はまっすぐ飛ぶ → 高い所の機体にも届く）
    public float rocketSpread = 1.5f;     // 度
    public float rocketDamage = 30f;
    public float rocketHitRadius = 10f;

    public GameMode gameMode;             // ハードモードの時だけ対空機銃を撃つ

    [Header("演出")]
    public ParticleSystem[] wakeFx;       // 航跡（船の後ろの白い波）と船首の波
    public ParticleSystem[] dmgSmoke;     // ダメージの煙
    public ParticleSystem[] dmgFire;      // ダメージの炎

    [Header("航行（fleet 以外の船）")]
    [Tooltip("通る点（輪になったコース）。全員の画面でサーバー時刻から同じ位置を計算するので通信なし")]
    public Vector3[] path;
    public float pathSpeed = 7f;          // m/s（約 14 ノット）
    public float pathPhase = 0f;          // コース上の出発位置 (m)
    public float yawOffset = 0f;          // 船の前の向き（親から見た角度）
    [Tooltip("オン: 広い海のランダムな場所にコースを作って航行（沈められると別の場所に）")]
    public bool roaming = false;
    public Vector3 avoidCenter;           // 出撃地点
    public float avoidRadius = 2000f;     // 出撃地点からこの距離の中には入らない
    public float roamRadius = 12000f;     // 出撃地点からこの距離までの海に出る
    [UdonSynced] private int pathSeed;    // コースの種（持ち主が決めて全員に同期 → 全員同じコース）
    [UdonSynced] private Vector3 anchor;  // プレイヤーの近くへ移った時の出現地点（0 = 最初のランダムなコース）
    [UdonSynced] private float syncPhase; // その時のコース上の位置

    [Header("出てくるまでの時間（空母はワールドが始まって 10 分後）")]
    public float appearDelay = 0f;
    public bool _Shown() { return visible; }   // 自分の画面で見た目が出ているか
    // 空母の艦隊: 戦闘が始まって appearDelay 秒後に出てくる（戦闘になっていなければ出てこない）
    public bool _Present() { return appearDelay <= 0f || (gameMode != null && (gameMode._WarElapsed() >= appearDelay || (gameMode._SightMode() && gameMode._Elapsed() >= appearDelay))); }   // 遊覧モードは、ワールドが始まって少したったら出てくる

    [Header("空母の艦隊（ランダムな場所に艦隊を組んで現れ、ランダムな所へ向かう）")]
    public bool noRelocate;               // 空母: プレイヤーの近くへ移ったり、数を数えたりしない
    public float pathMargin = 0f;         // コースの左右この距離にも島が無いこと（艦隊の幅）
    public bool useFormation;             // 艦隊の船: leader（空母）の向きから見て formationOffset の位置を走る
    public WarShip[] escorts;             // 空母: 艦隊の船（沈められた時、生き残りが見えている間は別の場所へ移らない）
    public Vector3 formationOffset;
    private double seenWorldStart;

    [Header("貨物船と護衛")]
    public WarShip leader;                // 貨物船: この船（護衛）のコースの後ろを付いて行く
    public float followGap = 400f;        // 護衛の何 m 後ろを走るか（コースに沿って）
    public WarShip follower;              // 護衛: 付いて来る貨物船
    public bool unarmed;                  // 貨物船: 撃たない
    private bool relocPending;

    [Header("出現のルール（fleet 以外）")]
    public WarShip[] allShips;            // 全部の船（近くに何隻いるか数える）
    public int maxPerPlayer = 5;          // 1 人のプレイヤーの近くに出る船の数の上限
    public float nearRadius = 6000f;      // この距離以内を「そのプレイヤーの近く」とする (m)
    public float relocateDistance = 8000f;// 全員からこれより遠い状態が続いたら移動する (m)
    public float shipClearance = 700f;    // 止まっている船（空母・艦隊）からこの距離より近くは通らない (m)
    public float relocateDelay = 10f;     // その状態が何秒続いたら移動するか
    public float spawnDistance = 4300f;   // 移動先: 一番近いプレイヤーからこの距離（見えるぎりぎり外）
    private float farTime, nextRelocCheck;
    private VRCPlayerApi[] players = new VRCPlayerApi[90];

    [Header("対空機銃")]
    public Transform aaMuzzle;            // 機銃の向き（撃つたびに狙う方へ向ける）
    public ParticleSystem aaTracer;       // 曳光弾
    public AudioSource aaAudio;
    public float aaRange = 1000f;         // この距離以内なら撃つ (m)
    public float aaFireRate = 8f;         // 発/秒
    public float aaSpread = 2.5f;         // ばらつき（度）
    public float aaHitRadius = 5f;        // 弾がこの距離以内を通ったら当たり (m)
    public float aaDamage = 3f;
    public float aaBurst = 2.2f;          // 連射する時間（秒）
    public float aaPause = 1.6f;          // 休む時間（秒）

    [Header("表示")]
    public float showDistance = 4000f;
    public float bobAmount = 0.4f;        // 波でゆれる量

    private Vector3[] sPos, sVel;
    private float[] sAge;
    private bool[] sActive;
    private float nextFire;
    private bool visible = true;
    private float nextCheck;
    private float nextLog;
    private Vector3 modelBasePos;
    private Quaternion modelBaseRot;
    private Vector3[] rPos, rVel;
    private float[] rAge;
    private bool[] rActive;
    private float nextRocket;
    private int groundMask;
    private float aaTimer;
    private Vector3[] sampPos;
    private float[] sampDist;
    private float pathLen;
    private int sampIdx;
    private float nextMoveUpdate;
    private float nextFxCheck;
    private int builtSeed;
    private bool wasDead;
    private int rng;
    private float aaCycle;

    void Start()
    {
        int n = shells != null ? shells.Length : 0;
        sPos = new Vector3[n];
        sVel = new Vector3[n];
        sAge = new float[n];
        sActive = new bool[n];
        if (model != null)
        {
            modelBasePos = model.transform.localPosition;
            modelBaseRot = model.transform.localRotation;
        }
        nextFire = Time.time + 3f;
        int rn = rockets != null ? rockets.Length : 0;
        rPos = new Vector3[rn];
        rVel = new Vector3[rn];
        rAge = new float[rn];
        rActive = new bool[rn];
        nextRocket = Time.time + 5f;
        groundMask = ~((1 << 9) | (1 << 10) | (1 << 18));   // プレイヤー・ミラーは除く
        if (roaming)
        {
            if (Networking.IsOwner(gameObject) && pathSeed == 0)
            {
                pathSeed = Random.Range(1, 2000000000);
                RequestSerialization();
            }
            if (pathSeed != 0)
            {
                if (anchor != Vector3.zero && GeneratePathAt(pathSeed, anchor)) pathPhase = syncPhase;
                else GeneratePath(pathSeed);
            }
        }
        else BuildPath();
    }

    public override void OnDeserialization()
    {
        if (!roaming || pathSeed == 0 || pathSeed == builtSeed) return;
        if (anchor != Vector3.zero && GeneratePathAt(pathSeed, anchor)) pathPhase = syncPhase;
        else GeneratePath(pathSeed);
    }

    // 種から同じ乱数を出す（全員の画面で同じコースになる）
    float NextF()
    {
        // Park-Miller 乱数（Schrage 法: int の範囲だけで計算）
        int hi = rng / 44488;
        int lo = rng % 44488;
        int t = 48271 * lo - 3399 * hi;
        if (t <= 0) t += 2147483647;
        rng = t;
        return rng / 2147483647f;
    }

    // ランダムな海の上に輪のコースを作る（出撃地点の近く・島の上は避ける）
    void GeneratePath(int seed)
    {
        builtSeed = seed;
        phaseShift = 0f; waiting = false;   // 新しいコースは最初から
        rng = seed % 2147483647;
        if (rng <= 0) rng = 1;
        float y = transform.position.y;
        for (int tries = 0; tries < 25; tries++)
        {
            float ca = NextF() * Mathf.PI * 2f;
            float R = 900f + NextF() * 1400f;
            float cr = avoidRadius + R * 1.3f + 500f + NextF() * Mathf.Max(roamRadius - avoidRadius - R * 1.3f - 500f, 100f);
            Vector3 c = new Vector3(avoidCenter.x + Mathf.Cos(ca) * cr, y, avoidCenter.z + Mathf.Sin(ca) * cr);
            Vector3[] pts = new Vector3[6];
            float a0 = NextF() * Mathf.PI * 2f;
            for (int i = 0; i < 6; i++)
            {
                float a = a0 + i * Mathf.PI / 3f + (NextF() - 0.5f) * 0.5f;
                float r = R * (0.7f + NextF() * 0.5f);
                pts[i] = new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r);
            }
            path = pts;
            BuildPath();
            if (PathClear())
            {
                pathPhase = NextF() * pathLen;
                return;
            }
        }
        // 見つからなければ止まったまま
        path = new Vector3[0];
        sampPos = null;
    }

    // 決まった地点（anchor）を通る輪のコースを作る（種と anchor が同じなら全員同じコース）
    bool GeneratePathAt(int seed, Vector3 at)
    {
        builtSeed = seed;
        phaseShift = 0f; waiting = false;
        rng = seed % 2147483647;
        if (rng <= 0) rng = 1;
        float y = transform.position.y;
        float R = 700f + NextF() * 900f;
        float a = NextF() * Mathf.PI * 2f;
        Vector3 c = new Vector3(at.x + Mathf.Cos(a) * R, y, at.z + Mathf.Sin(a) * R);
        float a0 = a + Mathf.PI;   // 1 つ目の点が anchor
        Vector3[] pts = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float ang = a0 + i * Mathf.PI / 3f + (i == 0 ? 0f : (NextF() - 0.5f) * 0.5f);
            float r = i == 0 ? R : R * (0.7f + NextF() * 0.5f);
            pts[i] = new Vector3(c.x + Mathf.Cos(ang) * r, y, c.z + Mathf.Sin(ang) * r);
        }
        path = pts;
        BuildPath();
        if (PathClear()) return true;
        path = new Vector3[0];
        sampPos = null;
        return false;
    }

    public bool _HasPath() { return sampPos != null && pathLen > 1f; }

    // ---- ほかの戦艦とぶつからないように、片方が止まって待つ（持ち主が決めて全員に同期） ----
    [UdonSynced] private float phaseShift;   // 待った分だけコース上の位置を戻す (m)
    [UdonSynced] private bool waiting;       // 止まって待っている
    [UdonSynced] private double waitStart;   // 待ち始めたサーバー時刻
    private float nextAvoidCheck, clearSince;
    private int myIndex = -2;
    public float avoidDistance = 450f;       // この距離より近くで、向かって行く方が止まる (m)

    // コース上の今の位置（m）。待っている間は進まない
    float PathS(float gap)
    {
        double t = waiting ? waitStart : Networking.GetServerTimeInSeconds();
        float s = (float)((t * pathSpeed + pathPhase + phaseShift - gap) % pathLen);
        if (s < 0f) s += pathLen;
        return s;
    }

    Vector3 Heading()
    {
        return Quaternion.Euler(0f, transform.eulerAngles.y + yawOffset, 0f) * Vector3.forward;
    }
    public Vector3 _Heading() { return Heading(); }

    // 持ち主だけ・1 秒ごと: 近くの戦艦に向かって進んでいたら止まり、離れたら（または相手がどいたら）また進む
    void UpdateAvoid()
    {
        if (Time.time < nextAvoidCheck || allShips == null || sampPos == null) return;
        nextAvoidCheck = Time.time + 1f;
        if (myIndex == -2) { myIndex = -1; for (int i = 0; i < allShips.Length; i++) if (allShips[i] == this) { myIndex = i; break; } }
        bool block = false;
        Vector3 me = transform.position;
        Vector3 hf = Heading();
        for (int i = 0; i < allShips.Length; i++)
        {
            WarShip o = allShips[i];
            if (o == null || o == this || o.unarmed) continue;
            if (!o.roaming && o.leader == null) continue;   // 止まっている船はコースの時点で避けている
            if (o.target != null && o.target._IsDestroyed()) continue;
            Vector3 to = o.transform.position - me;
            to.y = 0f;
            float d = to.magnitude;
            if (d > avoidDistance || d < 0.1f) continue;
            // 番号の小さい船が優先（止まらない）。番号の大きい船は、小さい船の方へ進んでいる時だけ止まって待つ
            // （相手がこちらへ向かって来ていて、自分は向かっていない時は、止まらずに進んでどく）
            if (i > myIndex) continue;
            bool iGo = Vector3.Dot(hf, to / d) > 0.2f;   // 自分が相手の方へ進んでいる
            if (iGo) { block = true; break; }
        }
        double now = Networking.GetServerTimeInSeconds();
        if (block)
        {
            clearSince = -1f;
            if (!waiting) { waiting = true; waitStart = now; RequestSerialization(); }
        }
        else if (waiting)
        {
            if (clearSince < 0f) clearSince = Time.time;
            if (Time.time - clearSince >= 2f)
            {
                // 待った時間の分だけ位置を戻して、そこから進む（全員の画面で同じ）
                phaseShift -= (float)((now - waitStart) * pathSpeed);
                phaseShift = phaseShift % pathLen;
                waiting = false;
                RequestSerialization();
            }
        }
    }

    // 空母の艦隊（空母の今の位置・生き残った船）が、誰かから見える距離にあるか
    bool CarrierGroupSeen()
    {
        int n = VRCPlayerApi.GetPlayerCount();
        if (players == null || players.Length < n) players = new VRCPlayerApi[n + 10];
        VRCPlayerApi.GetPlayers(players);
        float lim = showDistance + 500f;
        for (int i = 0; i < n; i++)
        {
            if (players[i] == null || !players[i].IsValid()) continue;
            Vector3 pp = players[i].GetPosition();
            Vector3 d = pp - _PosBehind(0f); d.y = 0f;
            if (d.magnitude < lim) return true;
            if (escorts == null) continue;
            for (int k = 0; k < escorts.Length; k++)
            {
                WarShip e = escorts[k];
                if (e == null || (e.target != null && e.target._IsDestroyed())) continue;
                Vector3 de = pp - e.transform.position; de.y = 0f;
                if (de.magnitude < lim) return true;
            }
        }
        return false;
    }
    // コースに沿って、今いる所から gap m 後ろの点（貨物船が付いて行く位置）
    public Vector3 _PosBehind(float gap)
    {
        if (sampPos == null || pathLen <= 1f) return transform.position;
        float s = PathS(gap);
        int last = sampPos.Length - 1;
        int i = 0;
        while (i < last - 1 && sampDist[i + 1] < s) i++;
        float seg = sampDist[i + 1] - sampDist[i];
        float f = seg > 0.001f ? (s - sampDist[i]) / seg : 0f;
        return Vector3.Lerp(sampPos[i], sampPos[i + 1], f);
    }

    public bool _IsRoamingAlive() { return !noRelocate && roaming && sampPos != null && (target == null || !target._IsDestroyed()); }

    // 持ち主だけ: プレイヤーの近くへ移る。近くの船が上限に達していないプレイヤーの、見えるぎりぎり外に出る
    bool TryRelocate()
    {
        int n = VRCPlayerApi.GetPlayerCount();
        if (players == null || players.Length < n) players = new VRCPlayerApi[n + 10];
        VRCPlayerApi.GetPlayers(players);
        Vector3 me = transform.position;
        int best = -1;
        float bestScore = 1e18f;
        for (int i = 0; i < n; i++)
        {
            VRCPlayerApi p = players[i];
            if (p == null || !p.IsValid()) continue;
            Vector3 pp = p.GetPosition();
            int cnt = 0;
            if (allShips != null)
                for (int k = 0; k < allShips.Length; k++)
                {
                    WarShip w = allShips[k];
                    if (w == null || w == this || !w._IsRoamingAlive()) continue;
                    Vector3 d = w.transform.position - pp;
                    d.y = 0f;
                    if (d.sqrMagnitude < nearRadius * nearRadius) cnt++;
                }
            if (cnt >= maxPerPlayer) continue;
            Vector3 dm = pp - me;
            dm.y = 0f;
            float score = cnt * 1e9f + dm.sqrMagnitude;   // 船の少ないプレイヤー → 近いプレイヤーの順
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) return false;
        Vector3 pc = players[best].GetPosition();
        for (int tries = 0; tries < 16; tries++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector3 at = new Vector3(pc.x + Mathf.Cos(ang) * spawnDistance, transform.position.y, pc.z + Mathf.Sin(ang) * spawnDistance);
            Vector3 fa = at - avoidCenter;
            fa.y = 0f;
            if (fa.magnitude < avoidRadius + 300f) continue;
            int seed = Random.Range(1, 2000000000);
            if (!GeneratePathAt(seed, at)) continue;
            // 今この瞬間に anchor の位置にいるように、コース上の位置を合わせる
            float st = (float)(Networking.GetServerTimeInSeconds() * pathSpeed % pathLen);
            float ph = -st % pathLen;
            if (ph < 0f) ph += pathLen;
            pathPhase = ph;
            pathSeed = seed;
            anchor = at;
            syncPhase = ph;
            RequestSerialization();
            sampIdx = 0;
            UpdateMove();
            farTime = 0f;
            return true;
        }
        if (pathSeed != 0 && builtSeed != pathSeed)
        {
            // 失敗で消したコースを元に戻す
            if (anchor != Vector3.zero && GeneratePathAt(pathSeed, anchor)) pathPhase = syncPhase;
            else GeneratePath(pathSeed);
        }
        return false;
    }

    // 持ち主だけ: 全員から遠い状態が続いたら移る（1 秒ごとに確かめる）
    void UpdateRelocate()
    {
        if (Time.time < nextRelocCheck) return;
        nextRelocCheck = Time.time + 1f;
        if (target != null && target._IsDestroyed()) { farTime = 0f; return; }
        int n = VRCPlayerApi.GetPlayerCount();
        if (players == null || players.Length < n) players = new VRCPlayerApi[n + 10];
        VRCPlayerApi.GetPlayers(players);
        float dmin = 1e18f;
        int nearest = -1;
        Vector3 me = transform.position;
        for (int i = 0; i < n; i++)
        {
            if (players[i] == null || !players[i].IsValid()) continue;
            Vector3 d = players[i].GetPosition() - me;
            d.y = 0f;
            if (d.sqrMagnitude < dmin) { dmin = d.sqrMagnitude; nearest = i; }
        }
        // 空母の艦隊とコースが重なっていたら（空母が後から決めた時など）、誰にも見えていない時に別の海へ移る
        if (dmin > (showDistance + 300f) * (showDistance + 300f) && OverlapsCarrier() && MoveAway()) { farTime = 0f; return; }
        // 1 人の近くに上限より多い時: 番号の大きい船から、誰にも見えていない時に遠くの海へ移る
        if (nearest >= 0 && dmin < nearRadius * nearRadius && dmin > showDistance * showDistance && allShips != null)
        {
            Vector3 pp = players[nearest].GetPosition();
            int myIdx = -1, before = 0;
            for (int k = 0; k < allShips.Length; k++)
            {
                WarShip w = allShips[k];
                if (w == this) { myIdx = k; break; }
                if (w == null || !w._IsRoamingAlive()) continue;
                Vector3 d = w.transform.position - pp;
                d.y = 0f;
                if (d.sqrMagnitude < nearRadius * nearRadius) before++;
            }
            if (myIdx >= 0 && before >= maxPerPlayer && MoveAway()) { farTime = 0f; return; }
        }
        if (dmin > relocateDistance * relocateDistance) farTime += 1f;
        else farTime = 0f;
        if (farTime >= relocateDelay)
        {
            if (!TryRelocate()) farTime = 0f;   // 上限などで移れない時は、また 10 秒待つ
        }
    }

    // 全員から離れたランダムな海へ（上限を超えた船用）
    bool MoveAway()
    {
        int n = VRCPlayerApi.GetPlayerCount();
        for (int tries = 0; tries < 10; tries++)
        {
            int seed = Random.Range(1, 2000000000);
            GeneratePath(seed);
            if (sampPos == null) continue;
            float s0 = (float)((Networking.GetServerTimeInSeconds() * pathSpeed + pathPhase) % pathLen);
            int k0 = 0;
            while (k0 < sampPos.Length - 2 && sampDist[k0 + 1] < s0) k0++;
            Vector3 q = sampPos[k0];
            bool ok = true;
            for (int i = 0; i < n; i++)
            {
                if (players[i] == null || !players[i].IsValid()) continue;
                Vector3 d = players[i].GetPosition() - q;
                d.y = 0f;
                if (d.sqrMagnitude < relocateDistance * relocateDistance) { ok = false; break; }
            }
            if (!ok) continue;
            pathSeed = seed;
            anchor = Vector3.zero;
            RequestSerialization();
            sampIdx = 0;
            UpdateMove();
            return true;
        }
        // 見つからなければ元のコースに戻す
        if (pathSeed != 0)
        {
            if (anchor != Vector3.zero && GeneratePathAt(pathSeed, anchor)) pathPhase = syncPhase;
            else GeneratePath(pathSeed);
        }
        return false;
    }

}
