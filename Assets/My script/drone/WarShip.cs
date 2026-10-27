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
public class WarShip : UdonSharpBehaviour
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
    public bool _Present() { return appearDelay <= 0f || (gameMode != null && gameMode._WarElapsed() >= appearDelay); }

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

    bool PathClear()
    {
        if (sampPos == null) return false;
        if (OverlapsCarrier()) return false;   // 空母の艦隊とコースが重なるなら使わない
        for (int k = 0; k < sampPos.Length; k++)   // 全部の点で確かめる（小さな島も避ける）
        {
            Vector3 q = sampPos[k];
            Vector3 fa = q - avoidCenter;
            fa.y = 0f;
            if (fa.magnitude < avoidRadius + 300f) return false;
            // 艦隊の幅: まわり 8 方向にも島が無いこと
            if (pathMargin > 0f)
                for (int a8 = 0; a8 < 8; a8++)
                {
                    float an = a8 * Mathf.PI / 4f;
                    Vector3 qm = q + new Vector3(Mathf.Cos(an), 0f, Mathf.Sin(an)) * pathMargin;
                    RaycastHit[] hm = Physics.RaycastAll(new Vector3(qm.x, seaLevel + 400f, qm.z), Vector3.down, 430f, groundMask, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < hm.Length; h++)
                    {
                        Collider col = hm[h].collider;
                        if (col == null || col.transform.IsChildOf(transform) || col.attachedRigidbody != null) continue;
                        if (hm[h].point.y > seaLevel + 1.5f) return false;
                    }
                }
            // 止まっている船（空母・艦隊）の近くは通らない（ぶつからないように）
            if (allShips != null)
                for (int w = 0; w < allShips.Length; w++)
                {
                    WarShip o = allShips[w];
                    if (o == null || o == this || o.roaming || o.leader != null) continue;
                    Vector3 op = o.model != null ? o.model.transform.position : o.transform.position;
                    Vector3 dq = q - op;
                    dq.y = 0f;
                    if (dq.sqrMagnitude < shipClearance * shipClearance) return false;
                }
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(q.x, seaLevel + 400f, q.z), Vector3.down, 430f, groundMask, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < hits.Length; h++)
            {
                Collider col = hits[h].collider;
                if (col == null || col.transform.IsChildOf(transform)) continue;
                if (col.attachedRigidbody != null) continue;   // 飛行機・動く船
                if (hits[h].point.y > seaLevel + 1.5f) return false;   // 島・陸
            }
        }
        return true;
    }

    // コースをなめらかな曲線（Catmull-Rom）にして、細かい点に分けておく
    void BuildPath()
    {
        if (path == null || path.Length < 3) return;
        int n = path.Length;
        int per = 12;
        sampPos = new Vector3[n * per + 1];
        sampDist = new float[n * per + 1];
        int k = 0;
        float d = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = path[(i - 1 + n) % n], p1 = path[i], p2 = path[(i + 1) % n], p3 = path[(i + 2) % n];
            for (int j = 0; j < per; j++)
            {
                float t = j / (float)per;
                Vector3 q = CatmullRom(p0, p1, p2, p3, t);
                if (k > 0) d += Vector3.Distance(sampPos[k - 1], q);
                sampPos[k] = q;
                sampDist[k] = d;
                k++;
            }
        }
        d += Vector3.Distance(sampPos[k - 1], sampPos[0]);
        sampPos[k] = sampPos[0];
        sampDist[k] = d;
        pathLen = d;
        // コースの輪のだいたいの中心と半径（空母の艦隊とぶつからないかを調べるため）
        Vector3 c = Vector3.zero;
        for (int i = 0; i < k; i++) c += sampPos[i];
        c /= Mathf.Max(k, 1);
        float r = 0f;
        for (int i = 0; i < k; i++) { Vector3 dq = sampPos[i] - c; dq.y = 0f; r = Mathf.Max(r, dq.magnitude); }
        pathCenter = c;
        pathRadius = r;
    }
    private Vector3 pathCenter;
    private float pathRadius;
    public Vector3 _PathCenter() { return pathCenter; }
    public float _PathRadius() { return pathRadius; }

    // 空母の艦隊のコースと重なっているか（輪どうしが離れていない）
    bool OverlapsCarrier()
    {
        if (noRelocate || allShips == null || sampPos == null) return false;
        for (int i = 0; i < allShips.Length; i++)
        {
            WarShip o = allShips[i];
            if (o == null || o == this || !o.noRelocate || !o._HasPath()) continue;
            Vector3 dc = pathCenter - o._PathCenter();
            dc.y = 0f;
            if (dc.magnitude < pathRadius + o._PathRadius() + o.pathMargin + 250f) return true;
        }
        return false;
    }

    Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // サーバー時刻からコース上の位置を出す（全員同じ）
    void UpdateMove()
    {
        if (sampPos == null || pathLen <= 1f) return;
        float s = PathS(0f);
        int last = sampPos.Length - 1;
        if (sampIdx >= last || sampDist[sampIdx] > s) sampIdx = 0;
        while (sampIdx < last - 1 && sampDist[sampIdx + 1] < s) sampIdx++;
        float seg = sampDist[sampIdx + 1] - sampDist[sampIdx];
        float f = seg > 0.001f ? (s - sampDist[sampIdx]) / seg : 0f;
        Vector3 a = sampPos[sampIdx], b = sampPos[sampIdx + 1];
        Vector3 p = Vector3.Lerp(a, b, f);
        Vector3 dir = b - a;
        dir.y = 0f;
        transform.position = new Vector3(p.x, transform.position.y, p.z);
        if (dir.sqrMagnitude > 0.0001f)
        {
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, yaw - yawOffset, 0f);
        }
    }

    void Update()
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null) return;
        float dt = Time.deltaTime;

        // 沈められたら、次は別の場所に出る（持ち主が新しいコースの種を決める）
        if (roaming && target != null && Networking.IsOwner(gameObject))
        {
            bool dead = target._IsDestroyed();
            if (dead && !wasDead) relocPending = true;
            if (!dead) relocPending = false;
            // 護衛: 貨物船が残っている間は移らない（貨物船が同じコースを走り続けるため）
            bool folAlive = follower != null && follower.target != null && !follower.target._IsDestroyed();
            // 空母: 生き残った艦隊の船か、沈んだ場所が誰かに見えている間は移らない（復活もしない）
            if (noRelocate && relocPending && dead && CarrierGroupSeen()) folAlive = true;
            if (noRelocate && target != null) target.holdRespawn = relocPending && dead;
            if (relocPending && dead && !folAlive)
            {
                relocPending = false;
                // 次に出る場所: 近くの船が上限に達していないプレイヤーの近く（無理なら、ランダムな海）。空母はランダムな海
                if (noRelocate || !TryRelocate())
                {
                    pathSeed = Random.Range(1, 2000000000);
                    anchor = Vector3.zero;
                    RequestSerialization();
                    GeneratePath(pathSeed);
                }
            }
            wasDead = dead;
            if (!dead) UpdateAvoid();   // ほかの戦艦とぶつからないように
            if (!noRelocate) UpdateRelocate();
            else if (gameMode != null && gameMode._WorldStart() > 0.0 && gameMode._WorldStart() != seenWorldStart)
            {
                // 空母: ワールドの始まり・モードの切り替えのたびに、出てくる場所（コース）を決め直す
                bool first = seenWorldStart == 0.0;
                seenWorldStart = gameMode._WorldStart();
                if (!first)
                {
                    pathSeed = Random.Range(1, 2000000000);
                    anchor = Vector3.zero;
                    RequestSerialization();
                    GeneratePath(pathSeed);
                }
            }
        }

        // 貨物船: 護衛の後ろを付いて行く
        if (leader != null)
        {
            if (visible || Time.time >= nextMoveUpdate)
            {
                nextMoveUpdate = Time.time + 1f;
                if (leader._HasPath() && useFormation)
                {
                    // 艦隊: 空母の今の位置と向きから、決まった位置へ
                    Vector3 leadPos = leader._PosBehind(0f);
                    Vector3 leadDir = leadPos - leader._PosBehind(20f);
                    leadDir.y = 0f;
                    if (leadDir.sqrMagnitude < 0.0001f) leadDir = Vector3.forward;
                    Quaternion leadRot = Quaternion.LookRotation(leadDir.normalized, Vector3.up);
                    Vector3 fpos = leadPos + leadRot * formationOffset;
                    transform.position = new Vector3(fpos.x, transform.position.y, fpos.z);
                    transform.rotation = Quaternion.Euler(0f, leadRot.eulerAngles.y - yawOffset, 0f);
                }
                else if (leader._HasPath())
                {
                    Vector3 fp = leader._PosBehind(followGap);
                    Vector3 fd = leader._PosBehind(followGap - 20f) - fp;
                    transform.position = new Vector3(fp.x, transform.position.y, fp.z);
                    fd.y = 0f;
                    if (fd.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(fd.x, fd.z) * Mathf.Rad2Deg - yawOffset, 0f);
                }
            }
        }
        // 航行（見えている時は毎フレーム、遠い時は 1 秒ごと = 軽量化）
        else if (sampPos != null && (visible || Time.time >= nextMoveUpdate))
        {
            nextMoveUpdate = Time.time + 1f;
            UpdateMove();
        }

        // 遠いときは見た目ごと止める
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            bool v = (head - transform.position).sqrMagnitude < showDistance * showDistance;
            // 「偵察任務」の無線の間は、プレイヤーの近く（5km 以内）には船を出さない（撃たない）
            if (v && gameMode != null && gameMode._Quiet()) v = false;
            // まだ出てくる時間になっていない（空母）
            bool present = _Present();
            if (target != null) target._SetHidden(!present);
            if (!present) v = false;
            if (v != visible && model != null) model.SetActive(v);
            visible = v;
        }

        UpdateShells(dt);
        UpdateRockets(dt);
        if (!visible) return;

        // 航跡とダメージの煙・炎（0.5 秒ごと）
        if (Time.time >= nextFxCheck)
        {
            nextFxCheck = Time.time + 0.5f;
            float hr = target != null ? target._HpRatio() : 1f;
            bool alive = hr > 0f;
            SetRates(wakeFx, alive && sampPos != null ? 1f : 0f, true);
            SetRates(dmgSmoke, !alive ? 0f : (hr < 0.4f ? 30f : (hr < 0.75f ? 12f : 0f)), false);
            SetRates(dmgFire, !alive ? 0f : (hr < 0.25f ? 45f : (hr < 0.5f ? 20f : 0f)), false);
        }

        // 波でゆっくりゆれる
        if (model != null && bobAmount > 0f)
        {
            float t = Time.time;
            model.transform.localPosition = modelBasePos + Vector3.up * Mathf.Sin(t * 0.7f) * bobAmount;
            model.transform.localRotation = modelBaseRot * Quaternion.Euler(Mathf.Sin(t * 0.5f) * 2f, 0f, Mathf.Sin(t * 0.63f) * 3f);
        }

        if (target != null && target._IsDestroyed()) return;
        if (unarmed) return;   // 貨物船は撃たない

        // 自分が操縦しているドローンを探す
        controller mine = null;
        if (planes != null)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                if (planes[i] == null) continue;
                VRCPlayerApi p = planes[i]._GetPilot();
                if (p != null && p.isLocal) { mine = planes[i]; break; }
            }
        }
        if (mine == null || mine.car == null) return;
        if (!mine._IsHostile()) return;   // 警告の間は撃たない（攻撃開始になってから）

        // 追尾しないミサイル: 艦隊に近づいたら（飛んでいる時だけ）
        if (Time.time >= nextRocket && rocketLauncher != null)
        {
            Vector3 mpos = mine.car.transform.position;
            if (Vector3.Distance(mpos, rocketLauncher.position) <= rocketRange && mpos.y - seaLevel > 25f)
            {
                FireRocket(mine);
                nextRocket = Time.time + rocketInterval * Random.Range(0.8f, 1.3f);
            }
        }
        // 対空機銃: ハードモードの時だけ。1km 以内なら連射（撃つ・休むをくり返す）
        if (gameMode != null && gameMode._IsHard()) UpdateAA(mine, dt);

        if (Time.time < nextFire) return;

        Vector3 tp = mine.car.transform.position;
        Vector3 d = tp - transform.position;
        d.y = 0f;
        bool inRange = d.magnitude <= range && tp.y - seaLevel <= maxAltitude;
        if (Time.time >= nextLog)
        {
            nextLog = Time.time + 3f;
            Debug.Log("[WarShip] " + gameObject.name + " 距離 " + d.magnitude.ToString("F0") + "m 高さ " + (tp.y - seaLevel).ToString("F0") + "m → " + (inRange ? "砲撃" : "範囲外"));
        }
        if (!inRange) return;

        Fire(mine);
        Debug.Log("[WarShip] " + gameObject.name + " 発射");
        nextFire = Time.time + fireInterval * Random.Range(0.8f, 1.2f);
    }

    void Fire(controller mine)
    {
        if (cannonMuzzles == null || cannonMuzzles.Length == 0 || shells == null) return;
        Vector3 tp = mine.car.transform.position;
        Rigidbody trb = mine.car.GetComponent<Rigidbody>();
        Vector3 tv = trb != null ? trb.velocity : Vector3.zero;

        // ドローンに一番近い砲門
        Transform muzzle = null;
        float best = float.MaxValue;
        for (int i = 0; i < cannonMuzzles.Length; i++)
        {
            if (cannonMuzzles[i] == null) continue;
            float dd = (cannonMuzzles[i].position - tp).sqrMagnitude;
            if (dd < best) { best = dd; muzzle = cannonMuzzles[i]; }
        }
        if (muzzle == null) return;

        // 空いている砲弾
        int slot = -1;
        for (int i = 0; i < shells.Length; i++) if (shells[i] != null && !sActive[i]) { slot = i; break; }
        if (slot < 0) return;

        // 先読み（届くまでの時間ぶん先 + 重力で落ちる分を上に）
        Vector3 mp = muzzle.position;
        float t = Vector3.Distance(mp, tp) / shellSpeed;
        Vector3 aim = tp + tv * t + Vector3.up * (0.5f * 9.81f * t * t);
        Vector3 dir = (aim - mp).normalized;
        dir = Quaternion.AngleAxis(Random.Range(-spreadDeg, spreadDeg), Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-spreadDeg, spreadDeg), Vector3.Cross(dir, Vector3.up).normalized) * dir;

        sPos[slot] = mp;
        sVel[slot] = dir * shellSpeed;
        sAge[slot] = 0f;
        sActive[slot] = true;
        shells[slot].position = mp;
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
            cannonAudio.pitch = Random.Range(1.2f, 1.45f);
            cannonAudio.PlayOneShot(cannonAudio.clip, 0.8f);
        }
    }

    void UpdateShells(float dt)
    {
        if (sActive == null) return;
        controller mine = null;
        Vector3 tp = Vector3.zero;
        if (planes != null)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                if (planes[i] == null) continue;
                VRCPlayerApi p = planes[i]._GetPilot();
                if (p != null && p.isLocal) { mine = planes[i]; break; }
            }
        }
        if (mine != null && mine.car != null) tp = mine.car.transform.position;

        for (int i = 0; i < shells.Length; i++)
        {
            if (!sActive[i]) continue;
            sAge[i] += dt;
            Vector3 v = sVel[i] + Vector3.down * 9.81f * dt;
            Vector3 p0 = sPos[i];
            Vector3 p1 = p0 + v * dt;

            // ドローンの近くを通ったら空中で爆発
            if (mine != null && DistToSegment(tp, p0, p1) < hitRadius)
            {
                Explode(i, p1, false);
                mine._ExternalDamage(damage);
                continue;
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
        }
    }

    // ---------------- 追尾しないミサイル ----------------
    void FireRocket(controller mine)
    {
        if (rockets == null || rActive == null) return;
        int slot = -1;
        for (int i = 0; i < rockets.Length; i++) if (rockets[i] != null && !rActive[i]) { slot = i; break; }
        if (slot < 0) return;
        Vector3 lp = rocketLauncher.position;
        Vector3 tp = mine.car.transform.position;
        Rigidbody trb = mine.car.GetComponent<Rigidbody>();
        Vector3 tv = trb != null ? trb.velocity : Vector3.zero;
        // 発射の瞬間に、届くころの位置を一度だけ予想して狙う（あとは曲がらない）
        float t = Vector3.Distance(lp, tp) / 220f;   // 平均の速さ（加速して 260 m/s）
        Vector3 aim = tp + tv * t;
        Vector3 dir = (aim - lp).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        dir = Quaternion.AngleAxis(Random.Range(-rocketSpread, rocketSpread), Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-rocketSpread, rocketSpread), side.normalized) * dir;
        rPos[slot] = lp;
        rVel[slot] = dir * rocketLaunchSpeed;
        rAge[slot] = 0f;
        rActive[slot] = true;
        Transform r = rockets[slot];
        r.position = lp;
        r.rotation = Quaternion.LookRotation(dir);
        r.gameObject.SetActive(true);
        TrailRenderer tr = r.GetComponent<TrailRenderer>();
        if (tr != null) { tr.Clear(); tr.emitting = true; }
        Transform flame = r.Find("Flame");
        if (flame != null) flame.gameObject.SetActive(true);
        AudioSource au = r.GetComponent<AudioSource>();
        if (au != null && au.clip != null) au.PlayOneShot(au.clip, 1f);
        mine._MissileWarning(lp);   // 「Missile launch! N o'clock!」
    }

    void UpdateRockets(float dt)
    {
        if (rActive == null) return;
        controller mine = null;
        if (planes != null)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                if (planes[i] == null) continue;
                VRCPlayerApi p = planes[i]._GetPilot();
                if (p != null && p.isLocal) { mine = planes[i]; break; }
            }
        }
        Vector3 tp = (mine != null && mine.car != null) ? mine.car.transform.position : Vector3.zero;
        for (int i = 0; i < rockets.Length; i++)
        {
            if (!rActive[i]) continue;
            rAge[i] += dt;
            Vector3 v = rVel[i];
            float sp = v.magnitude;
            Vector3 dir = sp > 0.1f ? v / sp : rockets[i].forward;
            bool burning = rAge[i] < rocketBurnTime;
            if (burning && sp < rocketMaxSpeed) v += dir * rocketAccel * dt;
            if (!burning) v += Vector3.down * 9.81f * dt;   // 燃え尽きてから落ち始める
            Vector3 p0 = rPos[i];
            Vector3 p1 = p0 + v * dt;
            if (mine != null && DistToSegment(tp, p0, p1) < rocketHitRadius)
            {
                EndRocket(i, p1, false);
                mine._ExternalDamage(rocketDamage);
                continue;
            }
            // 建物・島・ほかの物に当たったら爆発（壊せる物ならダメージ）
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
                    if (hc == null || hc.transform.IsChildOf(transform)) continue;   // 撃った船自身は除く
                    if (hits[k].distance < bestD) { bestD = hits[k].distance; best = k; }
                }
                if (best >= 0)
                {
                    Collider hc = hits[best].collider;
                    EndRocket(i, hits[best].point, false);
                    if (mine != null && mine.car != null && hc.transform.IsChildOf(mine.car.transform)) mine._ExternalDamage(rocketDamage);
                    else
                    {
                        FlightTarget ft = FindTarget(hc);
                        // 船どうしは壊さない（建物・標的・敵ドローンなどは壊れる）
                        if (ft != null && ft.GetComponent<WarShip>() == null) ft._HitMissile();
                    }
                    continue;
                }
            }
            if (p1.y <= seaLevel)
            {
                Vector3 hp = Vector3.Lerp(p0, p1, Mathf.Clamp01((p0.y - seaLevel) / Mathf.Max(p0.y - p1.y, 0.0001f)));
                EndRocket(i, new Vector3(hp.x, seaLevel, hp.z), true);
                continue;
            }
            if (rAge[i] > 14f) { EndRocket(i, p1, false); continue; }
            rPos[i] = p1;
            rVel[i] = v;
            rockets[i].position = p1;
            if (v.sqrMagnitude > 0.01f) rockets[i].rotation = Quaternion.LookRotation(v);
            if (!burning)
            {
                Transform flame = rockets[i].Find("Flame");
                if (flame != null && flame.gameObject.activeSelf) flame.gameObject.SetActive(false);
            }
        }
    }

    // ---------------- 対空機銃 ----------------
    void UpdateAA(controller mine, float dt)
    {
        if (aaMuzzle == null) return;
        // ハードモード: プレイヤーがミサイルを撃ったら、機体ではなくミサイルを狙う（撃ち落とせることがある）
        int msl = -1;
        float md = aaRange;
        int mn = mine._MslCount();
        for (int i = 0; i < mn; i++)
        {
            if (!mine._MslActive(i) || !mine._MslReal(i)) continue;
            float d = Vector3.Distance(mine._MslPos(i), aaMuzzle.position);
            if (d < md) { md = d; msl = i; }
        }
        if (msl >= 0) { AAMissile(mine, msl, dt); return; }
        Vector3 tp = mine.car.transform.position;
        float dist = Vector3.Distance(tp, aaMuzzle.position);
        if (dist > aaRange || tp.y - seaLevel < 2f) { aaCycle = 0f; return; }
        aaCycle += dt;
        if (aaCycle > aaBurst + aaPause) aaCycle = 0f;
        if (aaCycle > aaBurst) return;   // 休み中
        aaTimer -= dt;
        if (aaTimer > 0f) return;
        aaTimer = 1f / Mathf.Max(aaFireRate, 1f);

        // 先読みして狙う（弾の速さ 600 m/s）。機体そのものより、前や後ろをなめるように撃つ
        Rigidbody trb = mine.car.GetComponent<Rigidbody>();
        Vector3 tv = trb != null ? trb.velocity : Vector3.zero;
        Vector3 mp = aaMuzzle.position;
        Vector3 aimTrue = tp + tv * (dist / 600f);       // 当たり判定に使う、機体が来る位置
        Vector3 along = tv.sqrMagnitude > 1f ? tv.normalized : mine.car.transform.forward;
        float pick = Random.value;
        Vector3 aim = aimTrue;
        if (pick < 0.4f) aim += along * Random.Range(15f, 60f);        // 前
        else if (pick < 0.8f) aim -= along * Random.Range(15f, 60f);   // 後ろ
        // 海面すれすれ（30m 以下）の時は少し下を撃つ → 弾が機体の前後の海に落ちて水柱
        if (tp.y - seaLevel < 30f) aim.y -= Random.Range(2f, 10f);
        Vector3 dir = (aim - mp).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        dir = Quaternion.AngleAxis(Random.Range(-aaSpread, aaSpread) * 0.5f, Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-aaSpread, aaSpread) * 0.5f, side.normalized) * dir;
        aaMuzzle.rotation = Quaternion.LookRotation(dir);
        if (aaTracer != null) aaTracer.Emit(1);
        if (aaAudio != null && aaAudio.clip != null)
        {
            aaAudio.pitch = Random.Range(1.15f, 1.3f);
            aaAudio.PlayOneShot(aaAudio.clip, 0.55f);
        }
        // 弾の通り道が機体の近くを通ったら当たり
        float reach = aaRange * 1.6f;
        if (DistToSegment(aimTrue, mp, mp + dir * reach) < aaHitRadius)
        {
            mine._ExternalDamage(aaDamage);
            return;
        }
        // 外れて海に落ちたら水柱
        if (dir.y < -0.0001f)
        {
            float t = (seaLevel - mp.y) / dir.y;
            if (t > 0f && t < reach) mine._Splash(mp + dir * t);
        }
    }

    // 対空機銃でミサイルを狙う（1 発ごとに少しの確率で撃ち落とす）
    void AAMissile(controller mine, int i, float dt)
    {
        aaCycle += dt;
        if (aaCycle > aaBurst + aaPause) aaCycle = 0f;
        if (aaCycle > aaBurst) return;
        aaTimer -= dt;
        if (aaTimer > 0f) return;
        aaTimer = 1f / Mathf.Max(aaFireRate, 1f);
        Vector3 mp = aaMuzzle.position;
        Vector3 p = mine._MslPos(i);
        Vector3 aim = p + mine._MslVel(i) * (Vector3.Distance(p, mp) / 600f);
        Vector3 dir = (aim - mp).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        dir = Quaternion.AngleAxis(Random.Range(-aaSpread, aaSpread) * 0.5f, Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-aaSpread, aaSpread) * 0.5f, side.normalized) * dir;
        aaMuzzle.rotation = Quaternion.LookRotation(dir);
        if (aaTracer != null) aaTracer.Emit(1);
        if (aaAudio != null && aaAudio.clip != null)
        {
            aaAudio.pitch = Random.Range(1.15f, 1.3f);
            aaAudio.PlayOneShot(aaAudio.clip, 0.55f);
        }
        if (Random.value < 0.06f) mine._InterceptMissile(i);
    }

    // relative = true: 作った時の量に掛ける（0 か 1）/ false: その量にする
    void SetRates(ParticleSystem[] list, float r, bool relative)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++)
        {
            ParticleSystem ps = list[i];
            if (ps == null) continue;
            var em = ps.emission;
            if (relative) em.enabled = r > 0f;
            else em.rateOverTimeMultiplier = r;
            if ((relative ? r > 0f : r > 0f) && !ps.isPlaying) ps.Play();
        }
    }

    // レーダー表示用
    public int _RocketCount() { return rockets != null ? rockets.Length : 0; }
    public bool _RocketActive(int i) { return rActive != null && i < rActive.Length && rActive[i]; }
    public Vector3 _RocketPos(int i) { return rPos[i]; }

    // 当たった物の標的を探す: 物理の付いた物（敵機など）はその中から、それ以外は親をたどる
    FlightTarget FindTarget(Collider col)
    {
        if (col == null) return null;
        Rigidbody arb = col.attachedRigidbody;
        if (arb != null)
        {
            FlightTarget ft = arb.GetComponentInChildren<FlightTarget>();
            if (ft != null) return ft;
        }
        Transform ht = col.transform;
        for (int k = 0; k < 12 && ht != null; k++)
        {
            FlightTarget t2 = ht.GetComponent<FlightTarget>();
            if (t2 != null) return t2;
            ht = ht.parent;
        }
        return null;
    }

    void EndRocket(int i, Vector3 p, bool water)
    {
        rActive[i] = false;
        rockets[i].gameObject.SetActive(false);
        if (water && splashFx != null)
        {
            splashFx.transform.position = p;
            splashFx.Emit(60);
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
}
