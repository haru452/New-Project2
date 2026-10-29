using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using TMPro;

// =============================================================
//  飛行機コントローラー（物理ベース・失速あり）
//  ・迎角(AoA)から揚力係数を計算 → 失速角を超えると揚力が急減
//  ・失速中はロール減衰が失われ、片翼が落ちる（スピンに入る）
//  ・縦/横/方向の安定性（機首が気流に向く・上反角効果）
//  ・高度で空気密度が下がる（高空では舵も揚力も効きにくい）
//  ・地上ではタイヤのグリップ／ブレーキ／前輪ステアリング
//
//  操作（デスクトップ）
//    W/S : ピッチ（Sで機首上げ。invertPitchで反転）
//    A/D : ロール      Q/E（または Q/R）: ラダー（ヨー）。水上ではその場で向きを変える
//    左Shift : スロットル上げ   左Ctrl : スロットル下げ
//    F : フラップ切替   B : ブレーキ   T : 離水ブースト（上昇→前へ加速）   ジャンプ : 降りる
//    1 / 2 / 3 : 武器の切り替え（1 = 機銃、2 = 無誘導ミサイル、3 = ロックオンミサイル）
//    Enter（または左クリック） : 選んだ武器を撃つ
//    M : 音声案内（敵ドローンの方向・撃墜）のオン/オフ
//    P : ガンナーモードの切り替え（1 人でも砲塔を操作できる。その間は自動操縦で水平飛行）
//    ロックオン: 3 を選んで敵を機首の前（約12°以内）に1秒入れると赤い四角で囲まれる → 発射
//  操作（VR）
//    左スティック : ピッチ/ロール   右スティック左右 : ラダー
//    右スティック上下 : スロットル   右トリガー : 選んだ武器を撃つ
//    左トリガー（短く押す）: 武器の切り替え（1→2→3→1）  左トリガー（1 秒長押し）: 音声案内のオン/オフ
//    右グリップ : ブレーキ   左グリップ（短く押す）: フラップ   左グリップ（1 秒長押し）: ブースト（T と同じ）
//
//  カタパルト（伊402の甲板）
//    出現地点で座ったらスロットルを100%に → 1秒後に射出（約162km/h・機首上げ姿勢）
//
//  機体の向き: axisReference 未設定なら「car の -X が機首、+Y が上」
//  （今までの drone と同じ）。別の機体を使う時は機首方向(+Z)を
//  向いた空のオブジェクトを axisReference に入れる。
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public partial class controller : UdonSharpBehaviour
{
    [Header("参照")]
    public GameObject car;                  // Rigidbody が付いた機体
    public BoardPlane boarder;              // 機体本体をクリックして乗る仕組み（任意）
    public GunnerStation gunner;            // 2 人目の席（任意）
    public GameMode gameMode;               // ハード / 普通（全員共通）
    public VoiceRelay voiceRelay;           // 2 人乗りの時、音声を後ろの席にも流す

    [Header("AMA（敵）の無線: 警告 → 攻撃")]
    public AudioSource enemyRadio;          // 敵の無線（本人にだけ聞こえる）
    public AudioClip amaBroadcast, amaWarn1, amaWarn2, amaWarn3, amaFinal, amaEngage, amaFired;
    public AudioClip[] amaHeavy;           // 戦闘になった後の出撃（ガウリア帝国の司令部・別の男性の声）:「我々の戦闘機が撃破された。重火器の使用を許可する。敵を殲滅せよ」
    public AudioClip[] amaAttacked;        // 同上・味方がまだ撃墜されていない時:「攻撃を受けた。重火器の使用を許可する。敵を殲滅せよ」
    public AudioClip[] gaurRecon;          // まだ戦闘になっていない時の出撃（ガウリア帝国の司令部）:「これは偵察任務である。ただ非常の場合は重火器の使用を許可する」
    public Renderer[] exteriorRenderers;    // 操縦中、本人の画面では隠す機体の外側（コックピットの中から見るため）

    [Header("レーダー")]
    public Transform[] radarTracks;         // レーダーに出す物
    public int[] radarTypes;                // 0 = 敵機, 1 = 船, 2 = 標的, 3 = 味方
    public FlightTarget[] radarTrackTargets;// 壊れている間は出さない（無ければ null）
    public Transform[] radarDots;           // レーダー画面の点
    public Renderer[] radarDotRenderers;
    public Material[] radarMats;            // 種類ごとの色
    public float radarRange = 2000f;        // m
    public Renderer radarMap, radarMapG;    // レーダーの島の地図（操縦席・後ろの席）
    public Vector4 radarMapRect;            // 地図の真ん中の x, z と 1 辺の長さ (m)
    private Material mapMatP, mapMatG;
    public float radarRadius = 0.11f;       // 画面上の半径（m）
    public Transform axisReference;         // 任意: 機首=+Z, 上=+Y の基準
    public TextMeshPro hudText;             // 任意: 速度・高度などを表示
    public AudioSource engineAudio;         // 任意: エンジン音（ループ）
    public VolumeSettings volume;           // main room の音の大きさの設定（エンジン）
    public AudioSource stallWarningAudio;   // 失速しかけの警告音（ループ・本人だけ）
    public AudioSource stallAudio;          // 失速した時の警告音（ループ・本人だけ）

    [Header("コックピット表示（操縦中の本人にだけ表示）")]
    public GameObject cockpitRoot;
    public TextMeshPro cpSpeed;
    public TextMeshPro cpAlt;
    public TextMeshPro cpVs;
    public TextMeshPro cpAtt;
    public TextMeshPro cpHdg;
    public TextMeshPro cpAoa;
    public TextMeshPro cpFlags;
    public TextMeshPro cpThr;
    public TextMeshPro cpGun;
    public TextMeshPro cpWarn;
    public TextMeshPro cpStatus;
    public TextMeshPro cpCenter;          // 目の前に大きく出すメッセージ（カタパルトなど）
    public AudioSource cockpitAudio;      // 起動音（本人にだけ聞こえる）
    [Tooltip("座った時の起動演出の長さ（秒）")]
    public float bootDuration = 4.2f;
    public Renderer blackout;              // 起動時に周りを真っ暗にする球（本人の画面だけ）
    public Transform cpHorizon;
    public Transform cpThrBar;
    public Transform cpAoaBar;
    public Transform cpGunBar;

    [Header("計器（時計のようなメーター）と武器パネル")]
    public Transform cpSpdNeedle;          // 速度計の針
    public Transform cpAltNeedle;          // 高度計の長い針（1周 = 1000m）
    public Transform cpAltNeedleK;         // 高度計の短い針（1周 = 10000m）
    public Transform cpVsiNeedle;          // 昇降計の針（±10 m/s）
    public Transform cpTurnPlane;          // 旋回計の機体マーク
    public Transform cpHdgCard;            // 方位計の回る円盤
    public Transform cpThrNeedle;          // スロットル計の針
    public Transform cpFuelNeedle;         // 燃料計の針
    public Transform cpAoaNeedle;          // 迎角計の針
    public Renderer cpAttDisc;             // 姿勢指示器の球
    public TextMeshPro cpHdgNum;           // 方位計の中の数字
    public TextMeshPro cpVsNum;            // 昇降計の中の数字
    public TextMeshPro cpFuelPct;          // 燃料計の中の数字（%）

    // ---- HUD（前の空中の緑の表示） ----
    public Transform hudFpm;               // 飛んでいる方向の印
    public Transform hudLadder;            // 角度のはしご（世界の水平に合わせて回す）
    public Transform[] hudLadderLines;
    public float[] hudLadderDeg;
    GameObject[] greenPanels;              // 緑の文字の表示（計器だけのモードで隠す）

    // ---- 出し入れできる脚（タイヤ）。G キー / VR: 左スティックを左に短く ----
    public Transform[] gearPivots;         // 脚の付け根（ここを回して畳む）
    public Vector3[] gearFold;             // 畳んだ時の回転（度）
    public Quaternion[] gearBaseRot;       // 出し切った時の向き（手で回した分）
    public Collider[] gearColliders;       // タイヤの当たり判定（出し切った時だけ有効）
    public GameObject[] gearVisuals;       // 脚の見た目（しまい切ったら隠す）
    public GameObject gearRoot;            // 脚全体（LandingGear）。しまい切ったら SetActive(false)
    public float gearHeightDown = 2.5f;    // 脚を出している時の、機体の中心から地面までの判定距離
    public float gearLift = 1.4f;          // 脚を出すと機体が持ち上がる高さ（出現・カタパルト用）
    public float gearTime = 2.5f;          // 出し入れにかかる秒数
    public float gearDrag = 0.00026f;      // 脚を出して飛んでいる時の空気抵抗
    bool gearDown = true;
    float gearAnim = 1f, gearAnimShown = -1f;
    public bool _HasGear() { return gearPivots != null && gearPivots.Length > 0; }
    public float _GearLift() { return _HasGear() && gearDown ? gearLift : 0f; }
    public void NetGearDown() { gearDown = true; }
    public void NetGearUp() { gearDown = false; }
    // 後から入ってきた人にも、今の脚の状態をそのまま見せる（動かさずに一瞬で合わせる）
    public void NetGearUpNow() { gearDown = false; gearAnim = 0f; ApplyGear(); }
    public void NetGearDownNow() { gearDown = true; gearAnim = 1f; ApplyGear(); }
    public void _SendGearState()
    {
        if (!_HasGear() || car == null || !car.activeInHierarchy || !Networking.IsOwner(car)) return;
        // 出し入れの途中なら、終わってから送る
        if (gearAnim != (gearDown ? 1f : 0f)) { SendCustomEventDelayedSeconds(nameof(_SendGearState), 1f); return; }
        SendCustomNetworkEvent(NetworkEventTarget.All, gearDown ? nameof(NetGearDownNow) : nameof(NetGearUpNow));
    }
    void OnEnable()
    {
        // 出てきた時（最初の出現・格納庫から出した時）は脚を出した状態
        gearDown = true;
        gearAnim = 1f;
        gearAnimShown = -1f;
        ApplyGear();
    }
    public void ToggleGear()
    {
        if (!_HasGear()) return;
        if (gearDown && grounded) return;   // 地面にいる間はしまえない
        gearDown = !gearDown;
        SendCustomNetworkEvent(NetworkEventTarget.All, gearDown ? nameof(NetGearDown) : nameof(NetGearUp));
    }
    void UpdateGear(float dt)
    {
        if (!_HasGear()) return;
        float target = gearDown ? 1f : 0f;
        gearAnim = Mathf.MoveTowards(gearAnim, target, dt / Mathf.Max(gearTime, 0.1f));
        if (gearAnim != gearAnimShown || (gearRoot != null && gearRoot.activeSelf != (gearDown || gearAnim > 0f))) ApplyGear();
    }
    void ApplyGear()
    {
        if (!_HasGear()) return;
        gearAnimShown = gearAnim;
        float k = gearAnim * gearAnim * (3f - 2f * gearAnim);
        for (int i = 0; i < gearPivots.Length; i++)
        {
            if (gearPivots[i] == null) continue;
            Vector3 fe = (gearFold != null && i < gearFold.Length) ? gearFold[i] : new Vector3(-90f, 0f, 0f);
            Quaternion br = (gearBaseRot != null && i < gearBaseRot.Length) ? gearBaseRot[i] : Quaternion.identity;
            gearPivots[i].localRotation = br * Quaternion.Euler(fe * (1f - k));
        }
        // しまい切ったら脚ごと非表示（SetActive false）。出す時は先に表示してから出す
        bool vis = gearDown || gearAnim > 0f;
        if (gearRoot != null && gearRoot.activeSelf != vis) gearRoot.SetActive(vis);
        if (gearVisuals != null)
            for (int i = 0; i < gearVisuals.Length; i++)
                if (gearVisuals[i] != null && gearVisuals[i].activeSelf != vis) gearVisuals[i].SetActive(vis);
        bool col = gearAnim > 0.97f;
        if (gearColliders != null)
            for (int i = 0; i < gearColliders.Length; i++)
                if (gearColliders[i] != null && gearColliders[i].enabled != col) gearColliders[i].enabled = col;
    }
    bool gaugeOnly;                        // true = 緑の表示を消して丸い計器だけで飛ぶ

    // H キー: 緑の文字の表示を消して、丸い計器だけで見るモードの切り替え
    public void ToggleGaugeOnly()
    {
        gaugeOnly = !gaugeOnly;
        ApplyGaugeOnly();
    }
    void ApplyGaugeOnly()
    {
        if (greenPanels == null) return;
        for (int i = 0; i < greenPanels.Length; i++)
            if (greenPanels[i] != null && greenPanels[i].activeSelf == gaugeOnly) greenPanels[i].SetActive(!gaugeOnly);
    }

    public Transform hudRollPtr;           // 傾きの三角
    public Transform[] hudTapeTicks;       // 方位のテープの目盛り
    public TextMeshPro[] hudTapeLabels;
    public float hudTapeScale = 0.006f;    // 方位 1° あたりの幅
    public TextMeshPro hudHdg, hudSpd, hudAoa, hudMach, hudG, hudMaxG, hudGun, hudRkt, hudMsl, hudAlt, hudVs;
    public Transform hudThrFill, hudHpFill, hudStickDot, hudYawDot;
    public float hudStickSize = 0.045f;
    public TextMeshPro[] hudFlags;         // BRAKE, FLAPS, BOOST, LOCK, VOICE
    Vector3 hudLastVel;
    float hudGNow = 1f, hudGMax = 1f, hudTextT;
    bool hudGInit;
    Material attMat;
    float lastHdgT = -1f, lastHdg;
    public TextMeshPro cpHp;
    public Transform cpHpBar;
    public TextMeshPro cpWeapons;
    public TextMeshPro cpLock;
    public JoinLog joinLog;                // 入室・退室の記録（シーンに1つ）
    public TextMeshPro cpPlayers;
    public TextMeshPro cpLog;
    public TextMeshPro cpKills;            // 撃墜数（ドローン・船）
    public Transform lockBox;              // ロックオンした標的を囲む赤い四角（本人の画面だけ）
    public Transform radarSweep;           // レーダーの回る光
    public Transform radarRose;            // レーダーの方位目盛り（機首が上になるよう回る）
    public float radarSweepSpeed = 90f;    // 度/秒
    [Header("後ろの席のレーダー（操縦席の背中）")]
    public Transform[] radarDotsG;
    public Renderer[] radarDotRenderersG;
    public Transform radarSweepG, radarRoseG;
    public Transform[] radarMslDotsG;
    public Renderer[] radarMslRenderersG;
    public controller[] radarPlanes;       // ミサイルをレーダーに出す機体（R35 全部）
    public WarShip[] radarShips;           // ミサイルをレーダーに出す船
    public EnemyDrone[] radarFighters;     // ミサイルをレーダーに出す敵の戦闘機
    public Transform[] radarMslDots;       // レーダーのミサイルの点
    public Renderer[] radarMslRenderers;
    public Material[] radarMslMats;        // 0 = 自分, 1 = ほかのプレイヤー, 2 = 敵（船）

    [Header("機銃")]
    public Transform gunMuzzle;             // 銃口（+Z が射撃方向）。翼に 2 門ある機体は左翼
    public Transform gunMuzzle2;            // 右翼の銃口（無ければ 1 門だけ）
    public ParticleSystem tracerFx2;        // 右翼の曳光弾
    public ParticleSystem tracerFx;         // 曳光弾
    public ParticleSystem impactFx;         // 着弾の火花（シーンに1つを共有）
    public ParticleSystem waterSplashFx;    // 海面に当たった時の水柱（シーンに1つを共有）
    public AudioSource gunAudio;            // 発射音（clip を設定）
    public GameObject gunSight;             // 照準（操縦中の本人にだけ表示）
    public float fireRate = 12f;            // 発/秒
    public int maxAmmo = 600;
    public float gunRange = 800f;           // m
    public float gunSpread = 0.35f;         // 度

    [Header("ミサイル（無誘導）")]
    public Transform[] missiles;            // ミサイルの見た目（この機体専用のプール）
    public ParticleSystem missileSplashFx;  // 海に落ちた時の大きな水柱（シーンに1つを共有）
    public int maxMissiles = 16;
    [Tooltip("推進する時間（秒）。この間はほぼまっすぐ飛ぶ")]
    public float missileBurnTime = 2.5f;
    [Tooltip("推進中の加速 (m/s²)")]
    public float missileAccel = 60f;
    [Tooltip("発射した瞬間に機体の速度に足される速さ (m/s)")]
    public float missileLaunchSpeed = 8f;
    public float missileCooldown = 0.8f;

    [Header("音声案内（本人にだけ聞こえる）")]
    public AudioSource voiceAudio;
    public AudioClip[] voiceClock;         // 「Enemy drone, N o'clock」 0 = 12時, 1 = 1時 …
    public AudioClip voiceKill;            // 「Splash one. Enemy drone destroyed.」
    public AudioClip voiceOn;
    public AudioClip voiceOff;
    public AudioClip voiceTakingFire;      // 「Warning! Taking fire!」敵に撃たれた（方向が分からない時）
    public AudioClip[] voiceFireClock;     // 「Taking fire! N o'clock!」撃ってきた方向 0 = 12時
    public AudioClip[] voiceMissileClock;  // 「Missile launch! N o'clock!」船がミサイルを撃った方向
    public AudioClip voiceMissile;         // 「Missile away.」
    public AudioClip voiceTargetHit;       // 「Target hit.」ミサイルが船・建物などの標的に命中
    public AudioClip voiceImpact;          // 「Missile impact.」ミサイルが島・建物などに着弾
    public AudioClip voiceDestroyed;       // 「Target destroyed.」自分の攻撃で何かを壊した
    public AudioClip voiceWelcome;         // 乗って起動が終わった時のあいさつ
    public AudioClip voiceLaunching;       // 「Catapult engaged. Launching.」射出の秒読み開始
    public AudioClip voiceGo;              // 「Here we go!」射出の瞬間
    public AudioClip voiceApOn;            // 「Autopilot engaged.」
    public AudioClip voiceApOff;           // 「Autopilot disengaged. You have control.」
    public bool voiceEnabled = true;
    public float voiceDetectRange = 1500f; // この距離に入った敵ドローンを知らせる (m)
    public float voiceKillRange = 2500f;   // この距離の中で敵ドローンが落ちたら知らせる (m)

    [Header("ミサイル（ロックオン）")]
    public int maxGuided = 8;
    public float lockRange = 1800f;        // m
    public float lockCone = 12f;           // 機首からこの角度以内の物をロックする（度）
    public float lockTime = 1.0f;          // ロックが完了するまでの時間（秒）
    public float guidedTurnRate = 35f;     // 誘導中に曲がれる速さ（度/秒）
    public float guidedBurnTime = 4f;      // 推進する時間（秒）

    [Header("墜落")]
    public ParticleSystem crashFx;          // 墜落時の爆発（シーンに1つを共有）
    public AudioSource crashAudio;
    [Tooltip("海のマテリアル（波の形を読み取って、機体を波に乗せる）")]
    public Material oceanMaterial;
    [Header("HP")]
    public float maxHp = 100f;
    [Tooltip("ダメージを受けると燃える炎と煙（全員に見える。HP が減るほど強くなる）")]
    public ParticleSystem[] damageFire;
    public ParticleSystem[] damageSmoke;
    [Header("被弾時のコックピットの変化（本人の画面だけ）")]
    public Renderer damageTint;            // 視界を赤く点滅させる球
    public AudioSource alarmAudio;         // 警報音（ループ）
    public ParticleSystem cabinSmoke;      // コックピットの中の煙
    public AudioClip voiceEngineFire;      // 「Warning! Engine fire!」
    public AudioClip voiceCritical;        // 「Critical damage!」
    public Material staticMat;             // パネルの赤い砂嵐（全パネルで共有）
    [Tooltip("出発地点（respawnPoint）からこの距離以内にいると HP が少しずつ回復する (m)")]
    public float repairRadius = 100f;
    [Tooltip("回復の速さ（HP/秒）")]
    public float repairRate = 4f;
    [Tooltip("ほかのプレイヤーの機銃 1 発のダメージ")]
    public float pvpGunDamage = 4f;
    [Tooltip("ほかのプレイヤーのミサイル 1 発のダメージ")]
    public float pvpMissileDamage = 35f;
    [Tooltip("1 ステップでこれ以上速度が変わったら衝突とみなす (m/s)")]
    public float impactThreshold = 6f;
    [Tooltip("衝突のダメージ: 速度の変化 1m/s あたり")]
    public float impactDamagePerMS = 6f;

    [Tooltip("水面に強く当たった時の跳ね返りの強さ（0〜1）")]
    public float waterBounce = 0.45f;
    public bool splashOnBounce = true;
    [Tooltip("着水: これより速く降下していたら跳ね返る (m/s)")]
    public float maxSinkRate = 4f;
    [Tooltip("着水: これより速いと跳ね返る (m/s)。50 m/s = 180 km/h")]
    public float maxWaterSpeed = 50f;
    [Tooltip("着水: 翼の傾きがこれより大きいと跳ね返る（度）")]
    public float maxWaterRoll = 20f;
    [Tooltip("着水: 機首がこれより下がっていると跳ね返る（度）")]
    public float maxWaterNoseDown = 6f;
    [Tooltip("水上で Q/E を押した時の向きを変える速さ（度/秒）")]
    public float waterTurnRate = 35f;
    [Tooltip("機体の中心が 海面＋この高さ より下になったら着水")]
    public float waterContactHeight = 1.5f;
    public VRC.SDK3.Components.VRCObjectPool homePool;   // 格納庫のプール（あれば戻す）
    public Transform respawnPoint;          // 墜落後に機体・パイロットを戻す場所

    [Header("機体諸元（初期値は軽飛行機相当）")]
    public float mass = 800f;               // kg
    public float wingArea = 16f;            // m^2
    public float wingSpan = 10.5f;          // m
    public float oswaldEfficiency = 0.8f;
    public float inertiaRoll = 1300f;       // kg m^2
    public float inertiaPitch = 1800f;
    public float inertiaYaw = 2600f;

    [Header("燃料（ガソリン）")]
    // 燃料の減り方（レシプロエンジンの式）:
    //   エンジンの出力 P = Pmax × (アイドル + (1 − アイドル) × スロットル) × 空気の濃さ σ × (0.55 + 0.45 × v / v基準)
    //     ・スロットルを開けるほど出力が上がる
    //     ・速く飛ぶほどプロペラが風で回されて回転数が上がり、出力（＝燃料）が増える
    //     ・高く上がると空気が薄く（σ が小さく）、出力も燃料の減りも少なくなる
    //   燃料の流量 ṁ [kg/s] = BSFC [kg/kWh] × P [kW] ÷ 3600
    //   タンクの量 = 「全開・最高速度・海面」の流量 × 満タンで飛べる時間（5 分）
    //   ブースト（T）中は全開の 4 倍を使う
    public float enginePowerMax = 150f;      // kW（約 200 馬力）
    public float bsfc = 0.30f;               // kg/kWh（ガソリンエンジンの燃料消費率）
    public float idlePowerFrac = 0.08f;      // アイドルの出力の割合
    public float fuelRefSpeed = 55f;         // 基準の速さ（最高速度 約 200 km/h）m/s
    public float fullThrottleMinutes = 5f;   // 満タンから全開で飛べる時間（分）
    public float refuelSeconds = 20f;        // 出発地点の近くで満タンになるまでの時間（秒）
    public TextMeshPro cpFuel;               // 燃料の表示
    public AudioClip voiceLowFuel, voiceFuelEmpty;

    [Header("エンジン")]
    public float maxThrust = 3000f;         // N
    [Tooltip("プロペラ推力がゼロになる速度(m/s)。0にするとジェット扱い")]
    public float propMaxSpeed = 90f;
    public float throttleRate = 0.5f;       // 1秒あたりの変化量
    [Tooltip("T を押すと、まず真上に持ち上げる力 (N)。機体の重さ(約7850N)より大きいと上昇する")]
    public float boostLift = 14000f;
    [Tooltip("真上に上がる時間（秒）")]
    public float boostLiftTime = 1.5f;
    [Tooltip("その直後に前へ押す力 (N)")]
    public float boostForward = 9000f;
    [Tooltip("前へ押す時間（秒）")]
    public float boostForwardTime = 2.5f;
    [Tooltip("エンジン音の大きさ（0〜1）")]
    public float engineVolume = 0.35f;
    [Tooltip("高速・高空でもこの割合以上の推力は出る（0にすると完全にリアル）")]
    public float minEngineFactor = 0.2f;

    [Header("揚力・抗力")]
    public float zeroLiftAoA = -2f;         // 度
    public float stallAngle = 15f;          // 度
    public float negativeStallAngle = -12f; // 度
    public float stallTransition = 4f;      // 失速後に揚力が落ち切るまでの角度幅
    public float postStallLift = 1.0f;      // 失速後の揚力（平板 ≒ sin2α）
    public float parasiteDrag = 0.035f;     // CD0
    public float flapLift = 0.5f;
    public float flapDrag = 0.03f;
    public float flapStallReduction = 2f;   // フラップで失速角が下がる量(度)
    [Tooltip("スロットルを絞った時、プロペラが空気に回されて生まれる抗力（風車ブレーキ）")]
    public float propWindmillDrag = 0.02f;
    [Tooltip("B を押している間に増える抗力係数（エアブレーキ）")]
    public float airBrakeDrag = 0.12f;

    [Header("安定性・舵の効き")]
    public float cm0 = 0.04f;
    public float cmAlpha = -0.8f;           // 縦安定（負で安定）
    [Tooltip("昇降舵の効き。0.24 で操縦桿いっぱいの迎角が約20°（実機並み）")]
    public float cmElevator = 0.24f;
    public float cmPitchDamping = -12f;
    public float clBeta = -0.09f;           // 上反角効果
    public float clAileron = 0.08f;
    public float clRollDamping = -0.45f;
    public float clYawRate = 0.08f;
    [Tooltip("この速さより速いと昇降舵・方向舵の効きを弱めて、曲がる G を一定に保つ（速くなっても機体が暴れない）m/s")]
    public float pitchRefSpeed = 75f;
    [Tooltip("この速さより速いと補助翼の効きを弱めて、ロールの速さを一定に保つ m/s")]
    public float rollRefSpeed = 90f;
    public float cnBeta = 0.065f;           // 方向安定（風見効果）
    public float cnRudder = 0.035f;
    public float cnYawDamping = -0.1f;
    public float cnAileron = -0.008f;       // アドバースヨー
    public float cyBeta = 0.4f;             // 横滑り時の横力
    public float stallWingDrop = 0.05f;     // 失速時の片翼落ちの強さ
    public bool invertPitch = false;
    [Tooltip("キーを押してから舵がいっぱいになるまでの秒数（デスクトップ）")]
    public float controlRampTime = 0.5f;

    [Header("地上")]
    public float gearHeight = 1.3f;         // 機体中心から地面までの判定距離
    public float groundSteerRate = 30f;     // 度/秒
    public float brakeDecel = 5f;           // m/s^2
    public float rollingResistance = 0.02f;
    public float lateralGrip = 8f;

    [Header("大気・重力")]
    public float gravity = 9.81f;
    public float seaLevelY = 0f;
    public float scaleHeight = 8500f;       // 空気密度が 1/e になる高度
    public float spaceAltitude = 1200f;     // これ以上は宇宙（空気なし）
    public float spaceGravityScale = 0.25f;

    // ---- 内部状態 ----
    private VRCStation station;
    private Rigidbody rb;
    private VRCPlayerApi localPlayer;
    private bool isVR;
    private bool isPiloting;

    private float inPitch, inRoll, inYaw, inThrottleCmd;
    private float vrLookX, vrLookY;
    private bool vrGrip;
    private bool vrLeftGrip, vrLeftGripPrev, vrBoostDone;
    // SaccFlight 風の VR 操作
    public float vrStickMaxDeg = 30f;      // 手をこの角度傾けると操縦桿がいっぱい
    public float vrTwistMaxDeg = 35f;      // 手をこの角度ひねるとヨーがいっぱい
    public float vrLeverTravel = 0.25f;    // 手をこの距離(m)動かすとスロットル 0→100%
    private bool vrStickHeld, vrLeverHeld, vrLsUpPrev, vrLsLeftPrev;
    private Quaternion vrStickRef;
    private float vrLeverRefZ, vrLeverRefThr, vrLsUpStart, vrLsX, vrLsY, vrLsLeftStart;
    private bool vrLsLeftDone;
    private float vrLeftGripStart;
    private float cycleStart;
    private bool voiceToggleDone;
    private float throttle;
    private bool flapsDown;
    private float flapPos;
    private bool braking;
    private bool boosting;
    private float boostTimer = -1f;
    // ---- コックピットの起動演出 ----
    private float bootStart = -100f;
    private float lastExitTime = -100f;
    private float savedThrottle;
    private bool savedFlaps;
    private bool bootDone = true;
    private Transform[] bootPanels;
    private float[] bootDelays;
    private Material blackoutMat;   // T を押してからの時間（-1 = 使っていない）

    private float aoaDeg, stallAmount, airspeed, altitude;
    private bool grounded;
    private float noiseSeed;
    private int groundMask;

    // ---- カタパルト ----
    private Catapult catapult;
    private bool catLaunching;
    private float catDist, catSpeed, catHold, catCooldownUntil;
    private Quaternion catRot;

    // ---- 墜落 ----
    private bool crashed;
    private bool floating;   // 水面に浮いている
    private float hp = 100f;
    private float lastDamage, lastDamageTime = -10f;
    private Vector3 lastVel;
    private bool velValid;
    private Vector4 waveA, waveB, waveC;
    private float waveScale;
    private bool haveWaves;
    private Vector3 waveNormal = Vector3.up;

    // ---- 操縦桿（なめらかにした値） ----
    private float sElev, sAil, sRud;

    // ---- レーダー ----
    private float nextRadar;

    // ---- 無線 ----
    private VRCPlayerApi pilotPlayer;   // この機体に乗っている人（全員の画面で把握）
    private int radioCount;             // 無線がつながっている他のパイロットの数

    // ---- ミサイル ----
    private Vector3[] mPos, mVel;
    private float[] mAge;
    private bool[] mActive, mReal, mBurning;
    private int missileLeft;
    private float lastMissileTime = -10f;
    private int guidedLeft;
    private int[] mTgt;                 // 誘導の標的（radarTracks の番号、-1 = 無誘導）
    private Vector3[] mTgtPrev;

    // ---- 武器の選択・ロックオン ----
    private int weapon;                 // 0 = 機銃, 1 = 無誘導ミサイル, 2 = ロックオンミサイル
    private bool useHeld, trigPrev, cycleInput, cyclePrev;
    private int lockCand = -1;
    private float lockTimer;
    private bool locked;
    private float noLockMsgUntil;

    // ---- レーダー ----
    private float sweepAng;
    private float nextLogUpdate;
    private float[] killHitTime;        // その標的に自分が最後に当てた時刻
    private bool[] killWasDead;
    private float nextKillScan;

    // ---- 音声案内 ----
    private bool[] vAnnounced, vDead;
    private bool vInit;
    private AudioClip[] vQueue = new AudioClip[3];
    private int vQCount;
    private float nextVoiceScan;
}
