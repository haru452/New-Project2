using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using TMPro;

// controller.cs の続き（長いので分けたファイル。中身は同じクラス controller）
public partial class controller
{
    public void _GuidedMissileWarning()
    {
        if (!isPiloting || Time.time < nextMissileWarn) return;
        nextMissileWarn = Time.time + 3f;
        if (voiceGuided != null && voiceGuided.Length > 0) PlayVoiceNow(voiceGuided[Random.Range(0, voiceGuided.Length)]);
    }

    // ---- 空母（AMA 連合艦隊旗艦ユリシーズ）に近づいた時の警告（ほかの警告とは別に、出撃ごとに 1 回） ----
    public Transform carrier;
    public WarShip carrierShip;            // 空母（ワールドが始まって 10 分後に出てくる。それまで警告しない）
    public float carrierWarnRange = 4000f;
    public AudioClip[] guanWarn;           // 「こちらはAMA連合艦隊旗艦ユリシーズ艦長グアン・ローエングラムである…おとなしく投降しろ」
    private bool carrierWarned;
    void UpdateCarrierWarning()
    {
        if (carrierWarned || carrier == null || car == null || !hasTakenOff || !Armed()) return;
        if (carrierShip != null && !carrierShip._Present()) return;
        if (gameMode != null && (gameMode._IsSightseeing() || gameMode._Quiet())) return;
        Vector3 d = car.transform.position - carrier.position;
        d.y = 0f;
        if (d.magnitude > carrierWarnRange) return;
        if (enemyRadio != null && enemyRadio.isPlaying) return;   // ほかの無線が終わってから
        carrierWarned = true;
        if (guanWarn != null && guanWarn.Length > 0) PlayRadio(guanWarn[Random.Range(0, guanWarn.Length)]);
    }

    public void _MissileWarning(Vector3 from)
    {
        if (!isPiloting || car == null || Time.time < nextMissileWarn) return;
        nextMissileWarn = Time.time + 3f;
        Vector3 f = axisReference != null ? axisReference.forward : -car.transform.right;
        f.y = 0f;
        Vector3 d = from - car.transform.position;
        d.y = 0f;
        if (f.sqrMagnitude < 0.0001f || d.sqrMagnitude < 0.01f) return;
        int clock = ((Mathf.RoundToInt(Vector3.SignedAngle(f, d, Vector3.up) / 30f) % 12) + 12) % 12;
        if (voiceMissileClock != null && clock < voiceMissileClock.Length) PlayVoiceNow(voiceMissileClock[clock]);
    }

    // 撃たれた: 撃ってきた相手（敵ドローンは撃っている最中の物を優先、無ければ一番近い物）の方向を時計で言う
    void TakingFireVoice(int type)
    {
        if (Time.time < nextTakingFireVoice) return;
        nextTakingFireVoice = Time.time + 6f;   // 撃たれ続けても 6 秒に 1 回
        int clock = -1;
        if (radarTracks != null && car != null)
        {
            Vector3 me = car.transform.position;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < radarTracks.Length; i++)
            {
                if (TrackType(i) != type || !TrackAlive(i)) continue;
                if (radarTracks[i] == car.transform) continue;   // 自分の機体は除く
                if (type == 3)
                {
                    // ほかのプレイヤー: 誰かが乗っている機体だけ
                    controller oc = radarTracks[i].GetComponentInChildren<controller>();
                    if (oc == null || oc._GetPilot() == null) continue;
                }
                float score = Vector3.Distance(radarTracks[i].position, me);
                if (type == 0 && radarTrackTargets != null && i < radarTrackTargets.Length && radarTrackTargets[i] != null)
                {
                    EnemyDrone ed = radarTrackTargets[i].GetComponent<EnemyDrone>();
                    if (ed != null && ed._IsFiring()) score -= 100000f;
                }
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0)
            {
                Vector3 f = axisReference != null ? axisReference.forward : -car.transform.right;
                f.y = 0f;
                Vector3 d = radarTracks[best].position - me;
                d.y = 0f;
                if (f.sqrMagnitude > 0.0001f && d.sqrMagnitude > 0.01f)
                    clock = ((Mathf.RoundToInt(Vector3.SignedAngle(f, d, Vector3.up) / 30f) % 12) + 12) % 12;
            }
        }
        if (clock >= 0 && voiceFireClock != null && clock < voiceFireClock.Length && voiceFireClock[clock] != null) QueueVoice(voiceFireClock[clock]);
        else QueueVoice(voiceTakingFire);
    }

    void NoteHit(FlightTarget ft)
    {
        if (ft == null) return;
        recentHit[recentIdx] = ft; recentHitT[recentIdx] = Time.time; recentIdx = (recentIdx + 1) % 8;
        // 遊覧モード: 敵（飛行機・船・基地）に当てて HP を減らしたら、裏切りで戦闘開始
        if (gameMode != null && gameMode._IsSightseeing() && ft.kind >= 0 && !ft._IsDestroyed()) gameMode._Betray();
        lastHitFt = ft;
        lastHitTime = Time.time;
        // 撃墜数を数えるため、どの標的にいつ当てたかを覚える
        if (radarTrackTargets == null) return;
        if (killHitTime == null || killHitTime.Length != radarTrackTargets.Length)
        {
            killHitTime = new float[radarTrackTargets.Length];
            killWasDead = new bool[radarTrackTargets.Length];
            for (int k = 0; k < killHitTime.Length; k++) killHitTime[k] = -100f;
        }
        for (int i = 0; i < radarTrackTargets.Length; i++)
            if (radarTrackTargets[i] == ft) killHitTime[i] = Time.time;
    }

    public void _NoteHit(FlightTarget ft) { NoteHit(ft); }   // ガンナー席の砲塔から

    // 今回の出撃の記録（ランキング用）。2 人乗りの時は操縦士の画面でまとめて数える
    void CountRunKill(int ty)
    {
        Debug.Log("[Score] 撃破を数える ty=" + ty + " joinLog=" + (joinLog != null) + " 操縦中=" + isPiloting);
        if (joinLog == null) return;
        if (isPiloting) CrewKill(ty);
        else if (gunner != null && gunner._IsLocalGunner())
            SendCustomNetworkEvent(NetworkEventTarget.Owner, ty == 0 ? nameof(NetCrewKillAir) : (ty == 1 ? nameof(NetCrewKillShip) : (ty == 3 ? nameof(NetCrewKillCargo) : (ty == 4 ? nameof(NetCrewKillAwacs) : (ty == 5 ? nameof(NetCrewKillCvFighter) : (ty == 7 ? nameof(NetCrewKillBase) : nameof(NetCrewKillCarrier)))))));
    }

    public void NetCrewKillAir() { if (isPiloting) CrewKill(0); }
    public void NetCrewKillShip() { if (isPiloting) CrewKill(1); }
    public void NetCrewKillCarrier() { if (isPiloting) CrewKill(2); }
    public void NetCrewKillCargo() { if (isPiloting) CrewKill(3); }
    public void NetCrewKillAwacs() { if (isPiloting) CrewKill(4); }
    public void NetCrewKillCvFighter() { if (isPiloting) CrewKill(5); }
    public void NetCrewKillBase() { if (isPiloting) CrewKill(7); }
    // 貨物船を撃破した: 機銃とミサイルを補充
    public void NetRefillWeapons() { if (isPiloting) RefillWeapons(); }
    void RefillWeapons()
    {
        ammo = maxAmmo;
        missileLeft = maxMissiles;
        guidedLeft = maxGuided;
    }

    void CrewKill(int ty)
    {
        if (joinLog == null) return;
        VRCPlayerApi g = gunner != null ? gunner._GetGunner() : null;
        bool hard = gameMode != null && gameMode._IsHard();
        int mul = (gameMode != null && gameMode._Betrayed()) ? 3 : 1;   // 遊覧モードの裏切りの後は 3 倍
        joinLog._RunKillMul(ty, "", hard, mul);   // 2 人用のランキングはなくしたので、後ろの席に人がいても 1 人用の記録に
    }

    bool HasCrewGunner()
    {
        VRCPlayerApi g = gunner != null ? gunner._GetGunner() : null;
        return g != null && Utilities.IsValid(g);
    }

    // 自分が当ててから 5 秒以内に壊れた敵ドローン・船を撃墜として数える
    void UpdateKills()
    {
        if (killHitTime == null || radarTrackTargets == null || joinLog == null) return;
        if (Time.time < nextKillScan) return;
        nextKillScan = Time.time + 0.25f;
        int n = Mathf.Min(killHitTime.Length, radarTrackTargets.Length);
        for (int i = 0; i < n; i++)
        {
            FlightTarget ft = radarTrackTargets[i];
            if (ft == null) continue;
            bool dead = ft._IsDestroyed();
            if (dead && !killWasDead[i] && Time.time - killHitTime[i] < 5f)
            {
                int kd = ft.kind;   // 0 = 飛行機, 1 = 船, 2 = 空母
                if (kd == 0 || kd == 4 || kd == 5) joinLog._AddDroneKill();
                else if (kd >= 1 && kd != 6) joinLog._AddShipKill();
                if (kd >= 0 && kd != 6) CountRunKill(kd);
                if (kd == 6 && AllBaseBuildingsDown()) CountRunKill(7);   // 基地の建物を全部壊した: 100 点
                if (kd == 3)
                {
                    // 貨物船: 撃破した機体の弾とミサイルを補充（ガンナーが撃破したら操縦士の機体へ）
                    if (isPiloting) RefillWeapons();
                    else if (gunner != null && gunner._IsLocalGunner()) SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetRefillWeapons));
                }
                killHitTime[i] = -100f;
            }
            killWasDead[i] = dead;
        }
    }

    // ---------------- アストラル空港 ----------------
    // 近づくと管制室の無線。着陸しようとすると（ゆっくり・フラップを下げて空港のそば）:
    //   戦闘中で敵の戦闘機が近くにいる → エルシディアのパイロットと管制官の言い合い（7 種）→ 戦闘機は攻撃をやめて離れる
    //   それ以外 → 「着陸を許可する」。遊覧モードは「ようこそ」
    //   空港に止まると、弾・ミサイル・HP・燃料が回復する
    public Transform astralAirport;
    public float astralCallRadius = 5000f;
    public float astralLandRadius = 2500f;
    public float astralRepairRadius = 900f;
    public AudioClip astralIntro, astralClear, astralWelcome;
    public AudioClip[] astralArgs;
    private bool astralIntroDone, astralApproachDone;
    float AstralDist()
    {
        if (astralAirport == null || car == null) return 1e9f;
        Vector3 d = car.transform.position - astralAirport.position; d.y = 0f;
        return d.magnitude;
    }
    void UpdateAstral()
    {
        if (astralAirport == null || !hasTakenOff) return;
        float d = AstralDist();
        if (d > astralCallRadius * 1.5f) { astralIntroDone = false; astralApproachDone = false; return; }
        bool sight = gameMode != null && gameMode._IsSightseeing();
        if (sight)
        {
            if (!astralIntroDone && d < astralCallRadius) { astralIntroDone = true; PlayRadio(astralWelcome); }
            return;
        }
        if (!astralIntroDone && d < astralCallRadius) { astralIntroDone = true; PlayRadio(astralIntro); return; }
        if (!astralApproachDone && d < astralLandRadius && flapPos > 0.5f && airspeed < 75f && altitude < 300f)
        {
            astralApproachDone = true;
            if (EnemyFighterNear(3000f))
            {
                if (astralArgs != null && astralArgs.Length > 0) PlayRadio(astralArgs[Random.Range(0, astralArgs.Length)]);
                if (gameMode != null) gameMode._AstralCeasefire();
            }
            else PlayRadio(astralClear);
        }
    }
    bool AtAstralPad()
    {
        return astralAirport != null && grounded && airspeed < 25f && AstralDist() < astralRepairRadius;
    }
    bool EnemyFighterNear(float r)
    {
        if (radarFighters == null || car == null) return false;
        Vector3 p = car.transform.position;
        for (int i = 0; i < radarFighters.Length; i++)
        {
            EnemyDrone f = radarFighters[i];
            if (f == null || f.isAwacs || !f._Alive()) continue;
            if (Vector3.Distance(f._Pos(), p) < r) return true;
        }
        return false;
    }

    // ---------------- 遊覧モード ----------------
    // 基地に近づくとアイン中将の歓迎、空母に近づくとグアン艦長の歓迎。基地・空母に止まると補給
    // 飛び立つと、エルシディアの戦闘機が歓迎の編隊を組んで少し一緒に飛ぶ
    // 隠し要素: 何かを壊すと裏切り（得点 3 倍・全員が攻撃される）。アインの歓迎を受けた後に基地の上を通ると「非常に残念である」
    public AudioClip ainWelcome, ainRegret, guanWelcome, escortBye;
    public AudioClip[] gaurExercise;
    public AudioClip[] elcGreet;   // 「ガウリア帝国海軍のパイロットですね。ようこそ、わが国へ」（敵の戦闘機に近づいた時・プレイヤーごとに 1 回）
    private bool elcGreeted;
    private bool exercisePlayed;
    private bool ainWarnHeard;   // アインの警告を一度聞いた（プレイヤーごとに最初の 1 回だけ流す）
    public AudioClip[] escortWelcome;
    private bool ainWelcomed, metAin, ainRegretDone, guanWelcomed;
    private bool escortRequested, escortGreeted, escortByeDone;
    private FlightTarget[] recentHit = new FlightTarget[8];
    private float[] recentHitT = new float[8];
    private int recentIdx;
    private float nextBetrayCheck;
    float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    int MyPlaneIndex()
    {
        if (gameMode == null || gameMode.planes == null) return -1;
        for (int i = 0; i < gameMode.planes.Length; i++) if (gameMode.planes[i] == this) return i;
        return -1;
    }
    void UpdateSight()
    {
        if (gameMode == null || !gameMode._SightMode() || car == null || !hasTakenOff) return;
        Vector3 p = car.transform.position;
        bool peaceful = gameMode._IsSightseeing();
        if (baseCenter != null)
        {
            float bd = FlatDist(p, baseCenter.position);
            if (peaceful && !ainWelcomed && bd < baseWarnRadius) { ainWelcomed = true; metAin = true; PlayRadio(ainWelcome); return; }
            if (!peaceful && metAin && !ainRegretDone && bd < 1500f) { ainRegretDone = true; PlayRadio(ainRegret); return; }
        }
        if (peaceful && carrier != null && (carrierShip == null || carrierShip._Present()))
        {
            float cd = FlatDist(p, carrier.position);
            if (!guanWelcomed && cd < 3000f) { guanWelcomed = true; PlayRadio(guanWelcome); return; }
            if (cd > 6000f) guanWelcomed = false;
        }
        // エルシディアの戦闘機に近づいた: 最初の 1 回だけあいさつ
        if (peaceful && !elcGreeted && (enemyRadio == null || !enemyRadio.isPlaying) && FighterNear(p, 1500f))
        {
            elcGreeted = true;
            if (elcGreet != null && elcGreet.Length > 0) PlayRadio(elcGreet[Random.Range(0, elcGreet.Length)]);
            return;
        }
        int me = MyPlaneIndex();
        if (peaceful && !escortRequested && altitude > 120f && me >= 0) { escortRequested = true; gameMode._RequestEscort(me); }
        int ep = gameMode._EscortPlane();
        if (ep >= 0 && ep == me)
        {
            if (!escortGreeted && (enemyRadio == null || !enemyRadio.isPlaying) && WelcomeNear(p, 1500f))
            {
                escortGreeted = true;
                if (escortWelcome != null && escortWelcome.Length > 0) PlayRadio(escortWelcome[Random.Range(0, escortWelcome.Length)]);
            }
        }
        else if (escortGreeted && !escortByeDone) { escortByeDone = true; if (peaceful) PlayRadio(escortBye); }
    }
    bool FighterNear(Vector3 p, float r)
    {
        if (radarFighters == null) return false;
        for (int i = 0; i < radarFighters.Length; i++)
        {
            EnemyDrone d = radarFighters[i];
            if (d != null && !d.isAwacs && d._Alive() && Vector3.Distance(d._Pos(), p) < r) return true;
        }
        return false;
    }
    bool WelcomeNear(Vector3 p, float r)
    {
        if (gameMode.welcomeFlight == null) return false;
        for (int i = 0; i < gameMode.welcomeFlight.Length; i++)
        {
            EnemyDrone d = gameMode.welcomeFlight[i];
            if (d != null && d._Alive() && Vector3.Distance(d._Pos(), p) < r) return true;
        }
        return false;
    }
    bool AtFriendlyPad()
    {
        if (gameMode == null || !gameMode._IsSightseeing() || car == null || !grounded) return false;
        Vector3 p = car.transform.position;
        if (baseCenter != null && airspeed < 25f && FlatDist(p, baseCenter.position) < 1500f) return true;
        if (carrier != null && FlatDist(p, carrier.position) < 250f) return true;   // 空母の甲板の上
        return false;
    }
    // 遊覧モード（平和な間）に、自分が当てた物が壊れたら裏切り
    void UpdateBetray()
    {
        if (gameMode == null || !gameMode._IsSightseeing() || Time.time < nextBetrayCheck) return;
        nextBetrayCheck = Time.time + 0.25f;
        for (int i = 0; i < 8; i++)
        {
            FlightTarget ft = recentHit[i];
            if (ft == null) continue;
            if (Time.time - recentHitT[i] > 5f) { recentHit[i] = null; continue; }
            if (ft._IsDestroyed()) { recentHit[i] = null; gameMode._Betray(); return; }
        }
    }

    // ---------------- フレア ----------------
    // X キー: フレアを 2 発まく。追ってくる敵のミサイルが 75% でそれる（1.2 秒に 1 回まで・補給で戻る）
    public int maxFlares = 24;
    private int flares = 24;
    public ParticleSystem flareFx;
    public AudioSource flareAudio;
    public AudioClip voiceFlare;
    private float flareTime = -100f;
    private Vector3 flarePos;
    private float nextFlare;
    private float lastFlareVoice = -100f;
    public float _FlareTime() { return flareTime; }
    public Vector3 _FlarePos() { return flarePos; }
    public void _DropFlares() { DropFlares(); }
    void DropFlares()
    {
        if (!isPiloting || flares <= 0 || Time.time < nextFlare || !hasTakenOff) return;
        nextFlare = Time.time + 1.2f;
        flares = Mathf.Max(0, flares - 2);
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetFlare));
        if (Time.time - lastFlareVoice > 10f) { lastFlareVoice = Time.time; QueueVoice(voiceFlare); }
    }
    public void NetFlare()
    {
        flareTime = Time.time;
        if (car == null) return;
        Rigidbody frb = car.GetComponent<Rigidbody>();
        Vector3 fv = frb != null ? frb.velocity : Vector3.zero;
        flarePos = car.transform.position - fv * 0.4f + Vector3.down * 6f;
        if (flareFx != null) { flareFx.transform.position = car.transform.position - fv * 0.05f; flareFx.Emit(16); }
        if (flareAudio != null) { flareAudio.transform.position = car.transform.position; flareAudio.Play(); }
    }

    // 基地の建物（House_2・格納庫）。全部壊すと 100 点
    public FlightTarget[] baseBuildings;
    bool AllBaseBuildingsDown()
    {
        if (baseBuildings == null || baseBuildings.Length == 0) return false;
        for (int i = 0; i < baseBuildings.Length; i++)
            if (baseBuildings[i] != null && !baseBuildings[i]._IsDestroyed()) return false;
        return true;
    }

    bool IsEnemyTarget(FlightTarget ft)
    {
        if (radarTrackTargets == null) return false;
        for (int i = 0; i < radarTrackTargets.Length; i++)
            if (radarTrackTargets[i] == ft && TrackType(i) == 0) return true;
        return false;
    }

    void ToggleVoice()
    {
        voiceEnabled = !voiceEnabled;
        vQCount = 0;
        if (voiceAudio != null)
        {
            voiceAudio.Stop();
            AudioClip c = voiceEnabled ? voiceOn : voiceOff;
            if (c != null) { voiceAudio.clip = c; voiceAudio.Play(); }
        }
    }

    void QueueVoice(AudioClip c)
    {
        if (!voiceEnabled || c == null) return;
        if (vQCount >= vQueue.Length) return;   // 多すぎる分は流さない
        for (int k = 0; k < vQCount; k++) if (vQueue[k] == c) return;
        vQueue[vQCount] = c;
        vQCount++;
        RelayVoice(c, 0);
    }

    Vector3 AimForward()
    {
        if (gunMuzzle != null) return gunMuzzle.forward;
        if (axisReference != null) return axisReference.forward;
        return -car.transform.right;
    }

    bool TrackAlive(int i)
    {
        if (radarTracks == null || i < 0 || i >= radarTracks.Length) return false;
        Transform t = radarTracks[i];
        if (t == null || !t.gameObject.activeInHierarchy) return false;
        if (radarTrackTargets != null && i < radarTrackTargets.Length && radarTrackTargets[i] != null && radarTrackTargets[i]._IsDestroyed()) return false;
        return true;
    }

    int TrackType(int i)
    {
        return radarTypes != null && i >= 0 && i < radarTypes.Length ? radarTypes[i] : 2;
    }

    // 標的のだいたいの大きさ（半径 m）
    float TrackRadius(int i)
    {
        int ty = TrackType(i);
        if (ty == 0) return 15f;   // 敵機（4.5 倍の大きさ）
        if (ty == 1) return 35f;
        if (ty == 2) return 3f;
        if (ty == 5) return 30f;   // レーダーに出さない物（基地の艦隊・止まっている警戒機）
        return 5f;
    }

    // 機首の前にいる、ロックできる物（味方の機体は除く）
    // ---- ガンナー席から使う ----
    public int _FindLockFrom(Vector3 origin, Vector3 dir, float cone)
    {
        if (radarTracks == null || car == null) return -1;
        int best = -1;
        float bestA = cone;
        for (int i = 0; i < radarTracks.Length; i++)
        {
            if (TrackType(i) == 3 || !TrackAlive(i)) continue;
            Vector3 d = radarTracks[i].position - origin;
            float dist = d.magnitude;
            if (dist > lockRange || dist < 30f) continue;
            float a = Vector3.Angle(dir, d);
            if (a < bestA) { bestA = a; best = i; }
        }
        return best;
    }
    public bool _TrackAlive(int i) { return TrackAlive(i); }
    public int _MslCount() { return missiles != null && mActive != null ? missiles.Length : 0; }
    public bool _MslActive(int i) { return mActive != null && i < mActive.Length && mActive[i]; }
    public bool _MslReal(int i) { return mReal != null && i < mReal.Length && mReal[i]; }
    public Vector3 _MslVel(int i) { return mVel[i]; }
    // 対空砲火でミサイルが撃ち落とされた（撃った本人の画面）
    public AudioClip[] voiceIntercepted;   // 「ミサイルが対空砲火で撃ち落とされた」
    public void _InterceptMissile(int i)
    {
        if (!_MslActive(i)) return;
        bool real = mReal[i];
        if (crashFx != null) { crashFx.transform.position = mPos[i]; crashFx.Play(true); }
        EndMissile(i);
        if (real && voiceIntercepted != null && voiceIntercepted.Length > 0) QueueVoice(voiceIntercepted[Random.Range(0, voiceIntercepted.Length)]);
    }
    public Vector3 _MslPos(int i) { return mPos[i]; }
    public Vector3 _TrackPos(int i) { return radarTracks[i].position; }
    public float _TrackRadius(int i) { return TrackRadius(i); }
    public FlightTarget _FindTarget(Collider c) { return FindTarget(c); }
    public void _Splash(Vector3 p) { WaterSplash(p); }
    public void _SetExteriorVisible(bool v) { SetExteriorVisible(v); }
    public void _SetBoardClickable(bool on) { SetBoardClickable(on); }
    public bool _IsPilotLocal() { return isPiloting; }

    int FindLockCandidate()
    {
        if (radarTracks == null || car == null) return -1;
        Vector3 me = car.transform.position;
        Vector3 f = AimForward();
        int best = -1;
        float bestA = lockCone;
        for (int i = 0; i < radarTracks.Length; i++)
        {
            if (TrackType(i) == 3) continue;
            if (!TrackAlive(i)) continue;
            Vector3 d = radarTracks[i].position - me;
            float dist = d.magnitude;
            if (dist > lockRange || dist < 30f) continue;
            float a = Vector3.Angle(f, d);
            if (a < bestA) { bestA = a; best = i; }
        }
        return best;
    }

    void UpdateLock(float dt)
    {
        int cand = (weapon == 2 && guidedLeft > 0) ? FindLockCandidate() : -1;
        if (cand != lockCand) { lockCand = cand; lockTimer = 0f; }
        else if (cand >= 0) lockTimer += dt;
        locked = cand >= 0 && lockTimer >= lockTime;
    }

    // 標的の見た目の中心（モデルの基準点が機体の真ん中に無い物があるので、見えている形の中心を使う）
    private int lockRendFor = -1;
    private MeshRenderer[] lockRends;
    Vector3 LockCenter(int i)
    {
        Transform t = radarTracks[i];
        if (lockRendFor != i)
        {
            lockRendFor = i;
            lockRends = t.GetComponentsInChildren<MeshRenderer>(false);
        }
        bool any = false;
        Bounds bb = new Bounds(t.position, Vector3.zero);
        if (lockRends != null)
            for (int k = 0; k < lockRends.Length; k++)
            {
                MeshRenderer r = lockRends[k];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
            }
        return any ? bb.center : t.position;
    }

    // 赤い四角を標的に重ねる（カメラが動いた後に置くとずれない）
}
