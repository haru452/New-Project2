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
public class controller : UdonSharpBehaviour
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
    private float nextTakingFireVoice;
    private float nextLaunchVoice;
    private float nextMissileWarn;
    private int burnLevel;              // 0 = 無事, 1 = 煙, 2 = 炎, 3 = 激しい炎（全員の画面）
    private int sentBurnLevel;          // 操縦者が最後に知らせた段階
    private bool repairing;
    private float fuelKg = -1f, fuelCapKg, fuelFlow;
    private bool lowFuelSaid, emptySaid;
    private float rearmTimer;
    private Material damageTintMat;
    private Vector3 cockpitBasePos;
    private int flickerPanel = -1;
    private float flickerUntil;
    private Transform[] staticQuads;
    private int staticPanel = -1;
    private float staticUntil;
    private bool hasTakenOff;
    private bool gunnerMode;            // P キー: 操縦席に座ったまま砲塔を操作（自動操縦）
    // AMA（敵）が攻撃してくるか（全員の画面で同じ。撃墜されたら解除）
    private bool hostile;
    private int warnStage = -1;
    // ペラゴス基地（アイン・エメラルド中将）の警告: 0 = まだ, 1 = 警告中（10 秒以内に離脱）, 2 = 離脱した（2 度目の警告はない）
    public Transform baseCenter;
    public float baseWarnRadius = 3000f;
    public AudioClip[] ainWarn;            // 「こちら海軍、アイン・エメラルド中将である…直ちに離脱願う。2 度目の警告はない」
    public AudioClip[] ainThanks;          // 「感謝する」
    private int ainStage;
    private float ainDeadline;         // -1 = まだ見つかっていない, 0 = 放送, 1〜3 = 警告, 4 = 最後の通告
    private float nextWarnTime, lastDetectedTime, nextDetectScan, nextCrewFiredSend;
    private bool detected;
    private float armedAt = -1f;        // この時刻から警告・攻撃の仕組みが動く（射出の 3 秒後）
    private bool usingGunnerRadar;
    private bool gunSide;               // 左右の翼から交互に撃つ
    private Transform[] pDots, pMslDots;
    private Renderer[] pDotR, pMslR;
    private Transform pSweep, pRose;
    private int stallState, sentStallState;   // 0 = 普通, 1 = 失速しそう, 2 = 失速（後ろの席にも鳴らす）
    private float nextAimRay;
    private Vector3 aimPoint;
    // 船に見つかっている（ハードモードでは戦闘機が遠くからでも向かってくる。全員の画面で同じ）
    private bool spotted, sentSpotted;
    private float lastShipSeen, nextSpotScan;
    private float apTargetRoll;         // 自動操縦: 今の目標の傾き（0 = まっすぐ、+ = 右旋回）
    private float apNextChange;           // 離陸するまでは敵ドローンの方向を知らせない
    private FlightTarget lastHitFt;     // 最後に自分が当てた標的（壊れたら知らせる）
    private float lastHitTime;
    private Vector3[] radarPrev;
    private bool[] radarSeen;

    // ---- 機銃 ----
    private bool fireInput, localFiring, remoteFiring;
    private float fireTimer;
    private int ammo;

    void Start()
    {
        station = GetComponent<VRCStation>();
        localPlayer = Networking.LocalPlayer;
        isVR = localPlayer != null && localPlayer.IsUserInVR();
        noiseSeed = Random.Range(0f, 100f);
        // Player / PlayerLocal / MirrorReflection を地面判定から除外
        groundMask = ~((1 << 9) | (1 << 10) | (1 << 18));
        ammo = maxAmmo;
        hp = maxHp;
        missileLeft = maxMissiles; guidedLeft = maxGuided;
        int mn = missiles != null ? missiles.Length : 0;
        mPos = new Vector3[mn];
        mVel = new Vector3[mn];
        mAge = new float[mn];
        mActive = new bool[mn];
        mReal = new bool[mn];
        mBurning = new bool[mn];
        mTgt = new int[mn];
        mTgtPrev = new Vector3[mn];
        if (lockBox != null) lockBox.gameObject.SetActive(false);
        if (oceanMaterial != null)
        {
            waveA = oceanMaterial.GetVector("_WaveA");
            waveB = oceanMaterial.GetVector("_WaveB");
            waveC = oceanMaterial.GetVector("_WaveC");
            waveScale = oceanMaterial.GetFloat("_WaveScale");
            haveWaves = true;
        }
        if (gunSight != null) gunSight.SetActive(false);
        // 照準の「＋」は目の正面（コックピットの基準）60m 先に置く。弾はそこを通る線上に集まる
        if (gunSight != null && cockpitRoot != null)
        {
            gunSight.transform.SetParent(cockpitRoot.transform, false);
            gunSight.transform.localPosition = new Vector3(0f, 0f, 60f);
            gunSight.transform.localRotation = Quaternion.identity;
        }
        if (cockpitRoot != null)
        {
            cockpitRoot.SetActive(false);
            // 計器だけのモード（H キー）で隠す、緑の文字の表示（Omega のみ）
            if (cockpitRoot.transform.Find("InstrumentPanel") != null)
            {
                string[] gn = { "MainPanel", "LeftPanel", "RightPanel", "WeaponPanel", "LogPanel", "RadarPanel" };
                greenPanels = new GameObject[gn.Length];
                for (int gi = 0; gi < gn.Length; gi++)
                {
                    Transform gt = cockpitRoot.transform.Find(gn[gi]);
                    greenPanels[gi] = gt != null ? gt.gameObject : null;
                }
            }
            bootPanels = new Transform[7];
            bootPanels[0] = cockpitRoot.transform.Find("MainPanel");
            bootPanels[1] = cockpitRoot.transform.Find("LeftPanel");
            bootPanels[2] = cockpitRoot.transform.Find("RightPanel");
            bootPanels[3] = cockpitRoot.transform.Find("CompassPanel");
            bootPanels[4] = cockpitRoot.transform.Find("RadarPanel");
            bootPanels[5] = cockpitRoot.transform.Find("WeaponPanel");
            bootPanels[6] = cockpitRoot.transform.Find("LogPanel");
            bootDelays = new float[7];
            // 真っ暗な状態から、中央 → 左 → 右 → 方位 → レーダーの順に点灯
            bootDelays[0] = 0.8f; bootDelays[1] = 1.5f; bootDelays[2] = 1.7f; bootDelays[3] = 2.0f; bootDelays[4] = 2.2f; bootDelays[5] = 1.3f; bootDelays[6] = 1.9f;
            if (blackout != null) { blackoutMat = blackout.material; blackout.enabled = false; }
            if (damageTint != null) { damageTintMat = damageTint.material; damageTint.enabled = false; }
            cockpitBasePos = cockpitRoot.transform.localPosition;
            staticQuads = new Transform[bootPanels.Length];
            for (int i = 0; i < bootPanels.Length; i++)
            {
                if (bootPanels[i] == null) continue;
                staticQuads[i] = bootPanels[i].Find("Static");
                if (staticQuads[i] != null) staticQuads[i].gameObject.SetActive(false);
            }
        }

        if (car == null)
        {
            Debug.LogError("[controller] car が未設定です");
            return;
        }
        rb = car.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("[controller] car に Rigidbody がありません");
            return;
        }
        rb.mass = mass;
        rb.drag = 0f;
        rb.angularDrag = 0.05f;
        rb.useGravity = false;   // 重力はこのスクリプトで計算する
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // ドローン用の重力スクリプトが付いていると二重に重力がかかるので止める
        ajsutGravity g = car.GetComponent<ajsutGravity>();
        if (g != null) g.enabled = false;

        // 機体コライダーの摩擦をゼロにする（地面との摩擦はスクリプトで計算）
        // ※摩擦があると球コライダーがボールのように転がって機体が回ってしまう
        Collider[] cols = car.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null || cols[i].isTrigger) continue;
            PhysicMaterial pm = cols[i].material;   // 個別インスタンスが作られる
            if (pm == null) continue;
            pm.dynamicFriction = 0f;
            pm.staticFriction = 0f;
            pm.frictionCombine = PhysicMaterialCombine.Minimum;
            pm.bounciness = 0f;
        }
    }

    // ---------------- 入力・表示 ----------------
    void Update()
    {
        float dt = Time.deltaTime;
        // HP に合わせて燃え方を全員に知らせる（操縦者の画面で HP を持っているので）
        if (isPiloting)
        {
            // 出発地点の近く（repairRadius 以内）にいると少しずつ修理される
            repairing = false;
            if (car != null && !crashed && hp < maxHp
                && ((respawnPoint != null && Vector3.Distance(car.transform.position, respawnPoint.position) < repairRadius) || AtAstralPad()))
            {
                hp = Mathf.Min(maxHp, hp + repairRate * dt);
                repairing = true;
            }
            // 燃料も補給
            if (car != null && !crashed && _NeedsFuel()
                && ((respawnPoint != null && Vector3.Distance(car.transform.position, respawnPoint.position) < repairRadius) || AtAstralPad()))
            {
                Refuel(dt);
                repairing = true;
            }
            // 弾とミサイルも少しずつ補充（1 秒ごとに 機銃 60 発・無誘導 2 発・ロックオン 1 発）
            if (car != null && !crashed
                && ((respawnPoint != null && Vector3.Distance(car.transform.position, respawnPoint.position) < repairRadius) || AtAstralPad())
                && (ammo < maxAmmo || missileLeft < maxMissiles || guidedLeft < maxGuided))
            {
                repairing = true;
                rearmTimer += dt;
                if (rearmTimer >= 1f)
                {
                    rearmTimer = 0f;
                    ammo = Mathf.Min(maxAmmo, ammo + 60);
                    missileLeft = Mathf.Min(maxMissiles, missileLeft + 2);
                    guidedLeft = Mathf.Min(maxGuided, guidedLeft + 1);
                }
            }
            int lv = hp < 25f ? 3 : (hp < 50f ? 2 : (hp < 75f ? 1 : 0));
            if (lv != sentBurnLevel)
            {
                sentBurnLevel = lv;
                if (lv == 0) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBurn0));
                else if (lv == 1) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBurn1));
                else if (lv == 2) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBurn2));
                else SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBurn3));
            }
        }

        UpdateKills();

        // 後ろの席に座っている自分: 操縦席の背中のレーダーを動かす
        if (!isPiloting && usingGunnerRadar) UpdateRadar();

        // 照準: 「＋」の先（目の位置から機首方向）に弾とミサイルが集まるよう、銃口をそこへ向ける
        if (isPiloting && !gunnerMode && gunMuzzle != null && cockpitRoot != null) AimGun();

        if (isPiloting)
        {
            // P: ガンナーモード（2 人目の席が空いている時だけ）
            if (!isVR && Input.GetKeyDown(KeyCode.P) && gunner != null && bootDone)
            {
                if (gunnerMode) SetGunnerMode(false);
                else if (gunner._GetGunner() == null) SetGunnerMode(true);
            }
            // 誰かが 2 人目の席に座ったら、ガンナーはその人に任せる
            if (gunnerMode && gunner != null && gunner._GetGunner() != null) SetGunnerMode(false);
            if (!gunnerMode) UpdateWeapons(dt);
            UpdateVoice();
            UpdateHostility();
            UpdateAstral();
            UpdateSpotted();
            UpdateCarrierWarning();
        }
        UpdateGun(dt);
        UpdateEngineSound();
        UpdateMissiles(dt);
        UpdateGear(dt);
        if (!isPiloting) return;

        if (isVR)
        {
            // ---- SaccFlight 風の VR 操作 ----
            VRCPlayerApi lp = Networking.LocalPlayer;
            Transform refT = cockpitRoot != null ? cockpitRoot.transform : car.transform;
            Quaternion refInv = Quaternion.Inverse(refT.rotation);
            float stickYaw = Mathf.Abs(vrLookX) > 0.15f ? vrLookX : 0f;
            // 右グリップ = 操縦桿: 握った時の手の向きから、前後に傾ける = 機首の上げ下げ、左右に傾ける = 横転、ひねる = ヨー
            if (vrGrip && lp != null)
            {
                Quaternion rel = refInv * lp.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation;
                if (!vrStickHeld) { vrStickHeld = true; vrStickRef = rel; }
                Quaternion dq = rel * Quaternion.Inverse(vrStickRef);
                Vector3 hf = dq * Vector3.forward, hu = dq * Vector3.up;
                inPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(hf.y, -1f, 1f)) * Mathf.Rad2Deg / vrStickMaxDeg, -1f, 1f);
                inRoll = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(hu.x, -1f, 1f)) * Mathf.Rad2Deg / vrStickMaxDeg, -1f, 1f);
                float tw = Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg / vrTwistMaxDeg;
                if (Mathf.Abs(tw) < 0.15f) tw = 0f;
                inYaw = Mathf.Clamp(tw + stickYaw, -1f, 1f);
            }
            else
            {
                vrStickHeld = false;
                inPitch = 0f; inRoll = 0f;
                inYaw = stickYaw;
            }
            // 左グリップ = スロットルレバー: 握ったまま手を前に出す = 上げる、引く = 下げる
            inThrottleCmd = 0f;
            if (vrLeftGrip && lp != null)
            {
                Vector3 hp2 = refInv * (lp.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position - refT.position);
                if (!vrLeverHeld) { vrLeverHeld = true; vrLeverRefZ = hp2.z; vrLeverRefThr = throttle; }
                throttle = Mathf.Clamp01(vrLeverRefThr + (hp2.z - vrLeverRefZ) / vrLeverTravel);
            }
            else
            {
                vrLeverHeld = false;
                inThrottleCmd = Mathf.Abs(vrLookY) > 0.25f ? vrLookY : 0f;   // 右スティックの上下でも動かせる
            }
            // 左スティック: 下 = ブレーキ / 上 = 短く倒すとフラップ、1 秒倒し続けるとブースト / 左 = 計器だけのモード / 右 = フレア（後で）
            braking = vrLsY < -0.5f;
            bool up = vrLsY > 0.6f;
            if (up && !vrLsUpPrev) { vrLsUpStart = Time.time; vrBoostDone = false; }
            if (up && !vrBoostDone && Time.time - vrLsUpStart >= 1f)
            {
                vrBoostDone = true;
                if (boostTimer < 0f) boostTimer = 0f;
            }
            if (!up && vrLsUpPrev && !vrBoostDone && Time.time - vrLsUpStart < 0.6f) flapsDown = !flapsDown;
            vrLsUpPrev = up;
            // 左: 短く倒す = 脚の出し入れ、1 秒倒し続ける = 計器だけのモード
            bool lft = vrLsX < -0.6f;
            if (lft && !vrLsLeftPrev) { vrLsLeftStart = Time.time; vrLsLeftDone = false; }
            if (lft && !vrLsLeftDone && Time.time - vrLsLeftStart >= 1f) { vrLsLeftDone = true; ToggleGaugeOnly(); }
            if (!lft && vrLsLeftPrev && !vrLsLeftDone && Time.time - vrLsLeftStart < 0.6f) ToggleGear();
            vrLsLeftPrev = lft;
            boosting = false;
        }
        else
        {
            inThrottleCmd = 0f;
            if (Input.GetKey(KeyCode.LeftShift)) inThrottleCmd += 1f;
            if (Input.GetKey(KeyCode.LeftControl)) inThrottleCmd -= 1f;
            inYaw = 0f;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.R)) inYaw += 1f;
            if (Input.GetKey(KeyCode.Q)) inYaw -= 1f;
            braking = Input.GetKey(KeyCode.B);
            // T を 1 回押すと自動で「上昇 → 前へ加速」（使っている間は押し直しても無視）
            if (Input.GetKeyDown(KeyCode.T) && boostTimer < 0f) boostTimer = 0f;
            if (Input.GetKeyDown(KeyCode.F)) flapsDown = !flapsDown;
        }
        throttle = Mathf.Clamp01(throttle + inThrottleCmd * throttleRate * dt);
        if (fuelKg == 0f) throttle = 0f;   // 燃料切れ: エンジンが止まる

        UpdateHud();
        UpdateAudio();
    }

    // ---------------- 物理 ----------------
    void FixedUpdate()
    {
        if (rb == null) return;
        // 物理計算は機体のオーナーだけが行う（他の人は ObjectSync で同期）
        if (!Networking.IsOwner(car)) { velValid = false; return; }
        if (rb.isKinematic) { velValid = false; return; }
        if (crashed)
        {
            if (sinking)
            {
                // ゆっくり機首から沈んでいく
                rb.velocity = Vector3.down * sinkSpeed;
                Vector3 rt = axisReference != null ? axisReference.right : car.transform.forward;
                rb.angularVelocity = rt * 0.08f;
            }
            else
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            velValid = false;
            return;
        }

        // 衝突の検出: 前の物理ステップの速度から急に変わっていたら、何かにぶつかった
        // （自分でかけている力では 1 ステップに 1m/s も変わらない）
        if (velValid && isPiloting)
        {
            float dv = (rb.velocity - lastVel).magnitude;
            if (dv > impactThreshold) Damage((dv - impactThreshold) * impactDamagePerMS);
        }
        if (crashed) return;

        Simulate(Time.fixedDeltaTime);
        lastVel = rb.velocity;
        velValid = !crashed;
    }

    // ---------------- HP ----------------
    // 船の大砲など、外からのダメージ（操縦している本人の画面から呼ぶ）
    // 敵の戦闘機のミサイルが当たった（狙われた本人の画面で判定）
    public void _EnemyMissileHit(float amount)
    {
        if (!isPiloting) return;
        Damage(amount);
        TakingFireVoice(0);
    }

    public void _ExternalDamage(float amount)
    {
        if (!isPiloting) return;
        Damage(amount);
        TakingFireVoice(1);   // 船の大砲
    }

    void Damage(float amount)
    {
        if (crashed || amount <= 0f) return;
        hp -= amount;
        lastDamage = amount;
        lastDamageTime = Time.time;
        if (hp <= 0f)
        {
            hp = 0f;
            Crash();
        }
    }

    void Simulate(float dt)
    {
        Transform ct = car.transform;
        Vector3 fwd, up, right;
        if (axisReference != null)
        {
            fwd = axisReference.forward; up = axisReference.up; right = axisReference.right;
        }
        else
        {
            fwd = -ct.right; up = ct.up; right = ct.forward;
        }

        // 操縦していない時は舵中立・アイドル
        float tElev = 0f, tAil = 0f, tRud = 0f;
        if (isPiloting && gunnerMode && !grounded && !floating)
        {
            // ガンナーモード中の自動操縦: ときどき向きを変えながら飛ぶ（傾きは 25° まで＝ゆるい旋回）
            float apPitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            float apRoll = Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;   // 右翼下げ +
            if (Time.time >= apNextChange)
            {
                apNextChange = Time.time + Random.Range(7f, 15f);
                float r = Random.value;
                apTargetRoll = r < 0.4f ? 0f : (r < 0.7f ? Random.Range(12f, 25f) : -Random.Range(12f, 25f));
            }
            float tgtRoll = apTargetRoll;
            // 遠くへ行きすぎたら（中心から 4km）中心の方へ曲がる
            Vector3 toC = new Vector3(150f - ct.position.x, 0f, 75f - ct.position.z);
            if (toC.magnitude > 4000f)
            {
                Vector3 rf = new Vector3(right.x, 0f, right.z);
                tgtRoll = Vector3.Dot(toC, rf) > 0f ? 25f : -25f;
            }
            // 高さ: 120m より低ければ上昇、450m より高ければ降下、それ以外は高さを保つ
            float bias = altitude < 120f ? 6f : (altitude > 450f ? -3f : 0f);
            // 前に島や船があれば、翼を水平にして上昇
            if (Physics.Raycast(ct.position, fwd, 350f, groundMask, QueryTriggerInteraction.Ignore)) { bias = 12f; tgtRoll = 0f; }
            float wantPitch = Mathf.Clamp(-rb.velocity.y * 1.2f, -6f, 8f) + 2f + bias;
            if (stallAmount > 0.05f || aoaDeg > stallAngle - 3f) wantPitch = Mathf.Min(wantPitch, apPitch - 3f);   // 失速しそうなら機首を下げる
            tElev = Mathf.Clamp((wantPitch - apPitch) / 12f, -0.5f, 0.5f);
            tAil = Mathf.Clamp((tgtRoll - apRoll) / 25f, -0.6f, 0.6f);
            if (throttle < 0.6f) throttle = 0.6f;   // 自動操縦中は速度を落としすぎない
        }
        else if (isPiloting && !gunnerMode)
        {
            tElev = invertPitch ? inPitch : -inPitch;   // 引く(S/スティック手前)で機首上げ
            tAil = inRoll;
            tRud = inYaw;
        }
        // 操縦桿をなめらかに動かす（キーを押した瞬間にいっぱいまで引かない）
        // デスクトップは controlRampTime 秒でいっぱいまで。VR はスティックなのでほぼそのまま
        float rate = isVR ? 6f : 1f / Mathf.Max(controlRampTime, 0.01f);
        sElev = Ramp(sElev, tElev, rate, dt);
        sAil = Ramp(sAil, tAil, rate * 1.5f, dt);
        sRud = Ramp(sRud, tRud, rate, dt);
        float elev = sElev, ail = sAil, rud = sRud;
        flapPos = Mathf.MoveTowards(flapPos, flapsDown ? 1f : 0f, 0.4f * dt);

        // ---- カタパルト（接続中・射出中はここで処理して終わり）----
        if (catapult != null && CatapultStep(dt, fwd, up)) return;

        // ---- 大気 ----
        altitude = ct.position.y - seaLevelY;

        // ---- 着水: 強く当たったら跳ね返る、そっと降りたら波に乗って浮く ----
        bool onWater = false;
        float waveH = 0f;
        waveNormal = Vector3.up;
        if (altitude < 8f) waveH = WaveHeight(ct.position.x, ct.position.z);
        float aboveWave = altitude - waveH;
        bool touching = aboveWave < waterContactHeight;
        if (floating && aboveWave < waterContactHeight + 0.6f) onWater = true;   // 水面すれすれは水上扱い（タクシー・抵抗）
        // 着水はできない: 甲板や地面の上でなく海面に当たったら、行動不能になって沈む
        if (touching && catapult == null && !crashed && !Physics.Raycast(ct.position, -up, gearHeightDown + 0.5f, groundMask, QueryTriggerInteraction.Ignore))
        {
            CrashIntoSea();
            return;
        }
        if (false)
        {
            if (!floating)
            {
                // 着水できる条件: 降下がゆっくり・速すぎない・翼が水平・機首が下がっていない
                float sink = -rb.velocity.y;
                float pitchW = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
                float rollW = Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;
                bool tooHard = sink > maxSinkRate
                            || rb.velocity.magnitude > maxWaterSpeed
                            || Mathf.Abs(rollW) > maxWaterRoll
                            || pitchW < -maxWaterNoseDown;
                if (tooHard)
                {
                    // 強く当たった分だけ HP が減る
                    Damage(Mathf.Clamp(10f + Mathf.Max(sink - maxSinkRate, 0f) * 8f
                                       + Mathf.Max(rb.velocity.magnitude - maxWaterSpeed, 0f) * 1.5f, 10f, 80f));
                    if (crashed) return;
                    // 爆発はせず、水面で跳ね返る（勢いは弱まる）
                    Vector3 bv = rb.velocity;
                    bv.y = Mathf.Max(Mathf.Abs(bv.y) * waterBounce, 1.5f);
                    bv.x *= 0.75f;
                    bv.z *= 0.75f;
                    rb.velocity = bv;
                    rb.angularVelocity *= 0.3f;
                    Vector3 bp = rb.position;
                    bp.y = seaLevelY + waveH + waterContactHeight + 0.05f;
                    rb.position = bp;
                    if (splashOnBounce && waterSplashFx != null)
                    {
                        waterSplashFx.transform.position = new Vector3(bp.x, seaLevelY, bp.z);
                        waterSplashFx.Emit(30);
                    }
                    return;
                }
            }
            floating = true;
            onWater = true;
            Vector3 fv = rb.velocity;
            if (fv.y < 0f) fv.y = 0f;
            rb.velocity = fv;
            Vector3 fp = rb.position;
            fp.y = seaLevelY + waveH + waterContactHeight;   // 波の上に乗る
            rb.position = fp;
        }
        else if (aboveWave > waterContactHeight + 0.6f) floating = false;
        float rho = 1.225f * Mathf.Exp(-Mathf.Max(altitude, 0f) / scaleHeight);
        rho *= Mathf.Clamp01((spaceAltitude - altitude) / 200f);
        float g = altitude >= spaceAltitude ? gravity * spaceGravityScale : gravity;
        rb.AddForce(Vector3.down * g, ForceMode.Acceleration);

        Vector3 vel = rb.velocity;
        float V = vel.magnitude;
        airspeed = V;
        float vf = Vector3.Dot(vel, fwd);
        float vu = Vector3.Dot(vel, up);
        float vr = Vector3.Dot(vel, right);

        float alpha = Mathf.Atan2(-vu, vf);                       // 迎角 (rad)
        float beta = V > 0.5f ? Mathf.Asin(Mathf.Clamp(vr / V, -1f, 1f)) : 0f; // 横滑り角
        aoaDeg = V > 2f ? alpha * Mathf.Rad2Deg : 0f;

        float S = wingArea;
        float b = wingSpan;
        float c = wingArea / wingSpan;
        float AR = b * b / S;
        float clAlpha = 2f * Mathf.PI * AR / (AR + 2f);
        float a0 = zeroLiftAoA * Mathf.Deg2Rad;
        float stallPos = (stallAngle - flapStallReduction * flapPos) * Mathf.Deg2Rad;
        float stallNeg = negativeStallAngle * Mathf.Deg2Rad;
        float trans = Mathf.Max(stallTransition, 0.5f) * Mathf.Deg2Rad;

        // ---- 失速の度合い 0..1 ----
        float s = 0f;
        if (alpha > stallPos) s = Mathf.Clamp01((alpha - stallPos) / trans);
        else if (alpha < stallNeg) s = Mathf.Clamp01((stallNeg - alpha) / trans);
        s = s * s * (3f - 2f * s);
        stallAmount = V > 3f ? s : 0f;

        float qbar = 0.5f * rho * V * V;

        if (V > 0.5f)
        {
            // ---- 揚力係数 ----
            float clFlap = flapLift * flapPos;
            float clMax = clAlpha * (stallPos - a0) + clFlap;
            float clMin = clAlpha * (stallNeg - a0) + clFlap;
            float clPre = Mathf.Clamp(clAlpha * (alpha - a0) + clFlap, clMin, clMax);
            float clPost = postStallLift * Mathf.Sin(2f * alpha);
            float CL = Mathf.Lerp(clPre, clPost, s);

            // ---- 抗力係数（有害抗力 + 誘導抗力 / 失速後は平板）----
            float sinA = Mathf.Sin(alpha);
            float sinB = Mathf.Sin(beta);
            float cdPre = parasiteDrag + flapDrag * flapPos + clPre * clPre / (Mathf.PI * oswaldEfficiency * AR);
            float cdPost = parasiteDrag + flapDrag * flapPos + 1.9f * sinA * sinA;
            float CD = Mathf.Lerp(cdPre, cdPost, s) + 0.5f * sinB * sinB;
            if (braking && isPiloting) CD += airBrakeDrag;   // B: 空中ではエアブレーキ
            CD += propWindmillDrag * (1f - throttle);         // 絞るとプロペラがブレーキになる

            Vector3 velDir = vel / V;
            Vector3 liftDir = Vector3.Cross(velDir, right);
            if (liftDir.sqrMagnitude > 0.0001f) liftDir.Normalize();

            Vector3 aero = liftDir * (CL * qbar * S)
                         - velDir * (CD * qbar * S)
                         + right * (-cyBeta * beta * qbar * S);
            rb.AddForce(aero, ForceMode.Force);

            // ---- モーメント ----
            Vector3 w = rb.angularVelocity;
            float p = -Vector3.Dot(w, fwd);     // ロール（右翼下げ +）※Unityでは+fwd回りが左ロール
            float q = -Vector3.Dot(w, right);   // ピッチ（機首上げ +）
            float r = Vector3.Dot(w, up);       // ヨー（機首右 +）
            float Vs = Mathf.Max(V, 5f);
            float ph = p * b / (2f * Vs);
            float qh = q * c / (2f * Vs);
            float rh = r * b / (2f * Vs);

            float ailEff = 1f - 0.6f * s;
            float clpEff = clRollDamping * (1f - 1.6f * s);   // 失速中はロール減衰が反転 → オートローテーション
            float wingDrop = stallWingDrop * s * (Mathf.PerlinNoise(Time.time * 0.6f, noiseSeed) - 0.5f) * 2f;

            // 速い時は舵の効きを弱める（G リミッター）。手を離した時は水平飛行（1G）になるように釣り合わせる
            float qRef = 0.5f * rho * pitchRefSpeed * pitchRefSpeed;
            float elevK = qbar > qRef ? qRef / qbar : 1f;
            float ailK = V > rollRefSpeed ? rollRefSpeed / V : 1f;
            float ailC = ail * ailK;
            float a1g = mass * 9.81f / (Mathf.Max(qbar, 1f) * S * clAlpha) + a0;
            float cmT = Mathf.Min(cm0, -cmAlpha * Mathf.Sin(a1g));
            float L = clBeta * beta + clAileron * ailC * ailEff + clpEff * ph + clYawRate * rh + wingDrop;
            float M = cmT + cmAlpha * Mathf.Sin(alpha) + cmElevator * elev * elevK + cmPitchDamping * qh;
            float N = cnBeta * sinB + cnRudder * rud * elevK + cnYawDamping * rh + cnAileron * ailC;

            float rollAcc = L * qbar * S * b / inertiaRoll;
            float pitchAcc = M * qbar * S * c / inertiaPitch;
            float yawAcc = N * qbar * S * b / inertiaYaw;
            rb.AddTorque(-fwd * rollAcc - right * pitchAcc + up * yawAcc, ForceMode.Acceleration);
        }

        // ---- 推力 ----
        // スロットル（Shift/Ctrl で決めた値）どおりに推力を出す。
        // 速度が上がると少し弱まり、空気が薄くても完全には止まらない（勝手にエンジンが切れたように感じないように）
        float propFactor = propMaxSpeed > 0f ? Mathf.Clamp(1f - Mathf.Max(vf, 0f) / propMaxSpeed, minEngineFactor, 1f) : 1f;
        float airFactor = Mathf.Max(rho / 1.225f, minEngineFactor);
        float thrust = maxThrust * throttle * airFactor * propFactor;

        rb.AddForce(fwd * thrust, ForceMode.Force);
        if (isPiloting) BurnFuel(dt, airFactor);

        // T: 真下に噴射して真上に打ち上がる（機体の向きに関係なく真上へ）
        boosting = false;
        if (boostTimer >= 0f && isPiloting)
        {
            boosting = true;
            if (boostTimer < boostLiftTime)
            {
                rb.AddForce(Vector3.up * boostLift, ForceMode.Force);            // 1) 真上へ
            }
            else
            {
                Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z);                 // 2) 前へ（水平方向）
                if (flatFwd.sqrMagnitude > 0.01f) rb.AddForce(flatFwd.normalized * boostForward, ForceMode.Force);
            }
            boostTimer += dt;
            if (boostTimer > boostLiftTime + boostForwardTime) boostTimer = -1f;
        }
        else boostTimer = -1f;

        // ---- 水上（タクシー）----
        if (onWater)
        {
            float wvf = Vector3.Dot(rb.velocity, fwd);
            float wvr = Vector3.Dot(rb.velocity, right);
            Vector3 ww = rb.angularVelocity;
            // 水の抵抗（横すべりは強く止め、前後は少しだけ）
            rb.AddForce(right * Mathf.Clamp(-wvr * 3f, -6f, 6f), ForceMode.Acceleration);
            rb.AddForce(-fwd * wvf * (braking && isPiloting ? 0.6f : 0.08f), ForceMode.Acceleration);   // B: 水上ブレーキ
            // ラダー（Q/E）でその場で向きを変える。速いほど効きを弱める
            float turnFactor = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(wvf) / 20f));
            float targetYawW = rud * waterTurnRate * Mathf.Deg2Rad * turnFactor;
            float yawErrW = targetYawW - Vector3.Dot(ww, up);
            rb.AddTorque(up * yawErrW * 6f, ForceMode.Acceleration);
            // 波の面に沿って傾く
            Vector3 alignW = Vector3.Cross(up, waveNormal);
            float lowSpeedW = 1f - Mathf.Clamp01(airspeed / 10f);
            rb.AddTorque(fwd * (Vector3.Dot(alignW, fwd) * 25f - Vector3.Dot(ww, fwd) * 8f), ForceMode.Acceleration);
            rb.AddTorque(right * ((Vector3.Dot(alignW, right) * 25f - Vector3.Dot(ww, right) * 8f) * lowSpeedW - Vector3.Dot(ww, right) * 2f), ForceMode.Acceleration);
        }

        // ---- 地上 ----
        RaycastHit hit;
        float gh = _HasGear() && gearAnim > 0.97f ? gearHeightDown : gearHeight;   // 脚を出していればタイヤの高さ、しまっていれば胴体
        grounded = Physics.Raycast(ct.position, -up, out hit, gh, groundMask, QueryTriggerInteraction.Ignore);
        // 脚を出して飛んでいる時は空気抵抗が少し増える
        if (!grounded && _HasGear() && gearAnim > 0.05f && airspeed > 1f)
            rb.AddForce(-rb.velocity.normalized * airspeed * airspeed * gearDrag * gearAnim, ForceMode.Acceleration);
        if (grounded)
        {
            vf = Vector3.Dot(rb.velocity, fwd);
            vr = Vector3.Dot(rb.velocity, right);
            Vector3 w = rb.angularVelocity;

            // タイヤの横グリップ
            float lat = Mathf.Clamp(-vr * lateralGrip, -8f, 8f);
            rb.AddForce(right * lat, ForceMode.Acceleration);

            // 転がり抵抗 + ブレーキ
            float decel = rollingResistance * g + (braking && isPiloting ? brakeDecel : 0f);
            float absVf = Mathf.Abs(vf);
            if (absVf > 0.05f)
            {
                float d = Mathf.Min(decel, absVf / dt);
                rb.AddForce(-fwd * Mathf.Sign(vf) * d, ForceMode.Acceleration);
            }

            // 前輪ステアリング（低速ほど効く）
            float steerFactor = Mathf.Clamp01(absVf / 2f) * Mathf.Clamp01(1f - absVf / 40f);
            float targetYaw = rud * groundSteerRate * Mathf.Deg2Rad * steerFactor * Mathf.Sign(vf);
            float yawErr = targetYaw - Vector3.Dot(w, up);
            rb.AddTorque(up * yawErr * 6f, ForceMode.Acceleration);

            // 脚で姿勢を保つ（転がり・ひっくり返り防止）
            // 機体の上方向を地面の法線にそろえる。ピッチは速度が出ると弱め、引き起こし（離陸）できるようにする
            float lowSpeed = 1f - Mathf.Clamp01(airspeed / 30f);
            Vector3 align = Vector3.Cross(up, hit.normal);        // 上方向を法線へ回す回転軸
            float rollErr = Vector3.Dot(align, fwd);
            float pitchErr = Vector3.Dot(align, right);
            float rollRate = Vector3.Dot(w, fwd);
            float pitchRate = Vector3.Dot(w, right);
            rb.AddTorque(fwd * (rollErr * 25f - rollRate * 8f), ForceMode.Acceleration);
            rb.AddTorque(right * ((pitchErr * 25f - pitchRate * 8f) * lowSpeed - pitchRate * 2f), ForceMode.Acceleration);
        }
    }

    // 海のシェーダーと同じ式で、その場所の波の高さと向きを求める
    float WaveHeight(float x, float z)
    {
        if (!haveWaves) return 0f;
        float t = (float)(Networking.GetServerTimeInSeconds() % 3600.0) + 1f;
        Vector3 tng = new Vector3(1f, 0f, 0f);
        Vector3 bin = new Vector3(0f, 0f, 1f);
        float h = 0f;
        for (int i = 0; i < 3; i++)
        {
            Vector4 w = i == 0 ? waveA : (i == 1 ? waveB : waveC);
            float steep = w.z * waveScale;
            float k = 6.28318f / w.w;
            float c = Mathf.Sqrt(9.8f / k);
            Vector2 d = new Vector2(w.x, w.y).normalized;
            float f = k * (d.x * x + d.y * z - c * t);
            float a = steep / k;
            float sf = Mathf.Sin(f);
            float cf = Mathf.Cos(f);
            h += a * sf;
            tng += new Vector3(-d.x * d.x * steep * sf, d.x * steep * cf, -d.x * d.y * steep * sf);
            bin += new Vector3(-d.x * d.y * steep * sf, d.y * steep * cf, -d.y * d.y * steep * sf);
        }
        waveNormal = Vector3.Cross(bin, tng).normalized;
        return h;
    }

    // 目標へ一定の速さで近づける。戻すときは 2 倍の速さ（手を離すと操縦桿はすぐ戻る）
    float Ramp(float cur, float tgt, float rate, float dt)
    {
        float r = (Mathf.Abs(tgt) < Mathf.Abs(cur) || tgt * cur < 0f) ? rate * 2f : rate;
        return Mathf.MoveTowards(cur, tgt, r * dt);
    }

    // ---------------- カタパルト ----------------
    public void _SetCatapult(Catapult c)
    {
        if (catapult == c || catLaunching || Time.time < catCooldownUntil) return;
        catapult = c;
        catHold = 0f;
    }

    public void _ClearCatapult(Catapult c)
    {
        if (catLaunching) return;
        if (catapult == c) catapult = null;
    }

    // true を返したフレームは通常の飛行計算をしない
    bool CatapultStep(float dt, Vector3 fwd, Vector3 up)
    {
        Transform cp = catapult.transform;
        Vector3 cf = cp.forward;
        Vector3 cu = cp.up;
        altitude = car.transform.position.y - seaLevelY;
        stallAmount = 0f;
        aoaDeg = 0f;

        if (!catLaunching)
        {
            // 別の場所へリセットされた等で離れていたら接続解除
            if (Vector3.Distance(rb.position, cp.position) > 8f)
            {
                catapult = null;
                return false;
            }
            ammo = maxAmmo;   // カタパルト上で弾を補充
            missileLeft = maxMissiles; guidedLeft = maxGuided;
            // 接続中: シャトルに固定されているので前後左右には動かない
            Vector3 v = rb.velocity;
            rb.velocity = new Vector3(0f, Mathf.Min(v.y, 0f), 0f);
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
            airspeed = 0f;
            grounded = true;

            if (isPiloting && throttle >= 0.99f)
            {
                if (catHold <= 0f && Time.time >= nextLaunchVoice)
                {
                    nextLaunchVoice = Time.time + 6f;
                    QueueVoice(voiceLaunching);
                }
                catHold += dt;
                if (catHold >= catapult.holdTime)
                {
                    // 射出開始: 機首を射出方向に合わせて開始点へ
                    Quaternion inv = Quaternion.Inverse(rb.rotation);
                    Vector3 lf = inv * fwd;
                    Vector3 lu = inv * up;
                    // 機首を launchPitch 度上げた姿勢でレールに乗せる（+right 回りの回転は機首下げなので負の角度）
                    Quaternion noseUp = Quaternion.AngleAxis(-catapult.launchPitch, cp.right);
                    catRot = Quaternion.LookRotation(noseUp * cf, noseUp * cu) * Quaternion.Inverse(Quaternion.LookRotation(lf, lu));
                    rb.position = cp.position + cu * _GearLift();   // 脚を出していればタイヤの分だけ上
                    rb.rotation = catRot;
                    rb.velocity = Vector3.zero;
                    catLaunching = true;
                    PlayVoiceNow(voiceGo);
                    catDist = 0f;
                    catSpeed = 0f;
                }
            }
            else catHold = 0f;
            return true;
        }

        // 射出中: レールに沿って一定加速
        float stroke = Mathf.Max(catapult.strokeLength, 1f);
        float vEnd = catapult.launchSpeed;
        float acc = vEnd * vEnd / (2f * stroke);
        catSpeed = Mathf.Min(catSpeed + acc * dt, vEnd);
        catDist += catSpeed * dt;

        Vector3 rail = cp.position + cf * catDist + cu * _GearLift();
        Vector3 err = rail - rb.position;
        rb.velocity = cf * catSpeed + err * 10f;
        rb.rotation = catRot;
        rb.angularVelocity = Vector3.zero;
        airspeed = catSpeed;
        grounded = true;

        if (catDist >= stroke)
        {
            // 射出完了: 少し上向きに放り出す。ここからは通常の飛行
            Vector3 releaseDir = Quaternion.AngleAxis(-catapult.climbAngle, cp.right) * cf;
            rb.velocity = releaseDir * vEnd;
            catLaunching = false;
            armedAt = Time.time + 3f;   // 潜水艦から射出して 3 秒後から、敵の警告が始まる
            catapult = null;
            catHold = 0f;
            catCooldownUntil = Time.time + 3f;
        }
        return true;
    }

    // ---------------- 海に落ちた: 行動不能になって沈む ----------------
    public float sinkSpeed = 1.3f;          // 沈む速さ（m/s）
    public float sinkSeconds = 9f;          // 沈んでから格納庫に戻るまで（秒）
    bool sinking;
    void CrashIntoSea()
    {
        sinking = true;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSink));
        Crash();
    }
    float lastSinkTime = -10f;
    public void NetSink()
    {
        lastSinkTime = Time.time;
        Vector3 p = car.transform.position;
        if (waterSplashFx != null)
        {
            waterSplashFx.transform.position = new Vector3(p.x, seaLevelY, p.z);
            waterSplashFx.Emit(80);
        }
    }

    // ---------------- 墜落 ----------------
    void Crash()
    {
        ReconAbort();
        if (isPiloting && hostile && gameMode != null) gameMode._ReportOwnLoss();   // 戦闘中に撃墜された（ワールド全体に）
        if (isPiloting && joinLog != null) joinLog._ResetRun();   // 撃墜された → 今回の記録は 0 から
        ClearHostile();   // 次の出撃は、また警告から
        armedAt = -1f;
        crashed = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        throttle = 0f;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetCrash));
        if (isPiloting && station != null)
        {
            station.ExitStation(Networking.LocalPlayer);
            SendCustomEventDelayedSeconds(nameof(_TeleportPilotHome), 0.2f);
        }
        SendCustomEventDelayedSeconds(nameof(_AfterCrash), sinking ? sinkSeconds : 3f);
    }

    // ---------------- 燃える演出 ----------------
    public void NetBurn0() { SetBurn(0); }
    public void NetBurn1() { SetBurn(1); }
    public void NetBurn2() { SetBurn(2); }
    public void NetBurn3() { SetBurn(3); }

    void SetBurn(int lv)
    {
        // 操縦している本人: 悪くなった時に音声で知らせる
        if (isPiloting && lv > burnLevel)
        {
            if (lv == 2) QueueVoice(voiceEngineFire);
            else if (lv >= 3) PlayVoiceNow(voiceCritical);
        }
        burnLevel = lv;
        // 炎: 段階 2 から。3 で大きく激しく
        float fireRate = lv >= 3 ? 70f : (lv == 2 ? 28f : 0f);
        float fireSize = lv >= 3 ? 1.8f : 1f;
        if (damageFire != null)
        {
            for (int i = 0; i < damageFire.Length; i++)
            {
                ParticleSystem ps = damageFire[i];
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTimeMultiplier = fireRate;
                var mn = ps.main;
                mn.startSizeMultiplier = fireSize * 2f;
                if (fireRate > 0f && !ps.isPlaying) ps.Play();
            }
        }
        // 煙: 段階 1 から。だんだん濃く
        float smokeRate = lv >= 3 ? 40f : (lv == 2 ? 22f : (lv == 1 ? 8f : 0f));
        if (damageSmoke != null)
        {
            for (int i = 0; i < damageSmoke.Length; i++)
            {
                ParticleSystem ps = damageSmoke[i];
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTimeMultiplier = smokeRate;
                var mn = ps.main;
                mn.startSizeMultiplier = lv >= 3 ? 7f : 4.5f;
                if (smokeRate > 0f && !ps.isPlaying) ps.Play();
            }
        }
    }

    public void NetCrash()
    {
        SetBurn(0);   // 格納庫に戻ると修理済み
        sentBurnLevel = 0;
        if (gunner != null) gunner._OnCrash();   // ガンナーも一緒に格納庫へ
        Vector3 p = car.transform.position;
        if (Time.time - lastSinkTime < 1f) return;   // 海に落ちた時は爆発せず、水しぶきだけ
        if (crashFx != null)
        {
            crashFx.transform.position = p;
            crashFx.Play(true);
        }
        if (crashAudio != null)
        {
            crashAudio.transform.position = p;
            crashAudio.Play();
        }
    }

    public void _TeleportPilotHome()
    {
        if (respawnPoint == null) return;
        Networking.LocalPlayer.TeleportTo(respawnPoint.position + Vector3.up * 1f, respawnPoint.rotation);
    }

    public void _AfterCrash()
    {
        crashed = false;
        sinking = false;
        floating = false;
        hp = maxHp;   // 格納庫に戻ったら修理済み
        fuelKg = -1f;  // 燃料も満タン（次に使う時に満たす）
        missileLeft = maxMissiles; guidedLeft = maxGuided;
        ammo = maxAmmo;
        velValid = false;
        if (!Networking.IsOwner(car)) return;
        throttle = 0f;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        if (homePool != null)
        {
            // 格納庫のプールに戻す（また「出す」ボタンで出せる）
            Networking.SetOwner(Networking.LocalPlayer, homePool.gameObject);
            homePool.Return(car);
        }
        else if (respawnPoint != null)
        {
            gearDown = true; gearAnim = 1f; ApplyGear();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetGearDown));
            rb.position = respawnPoint.position + Vector3.up * (1.5f + _GearLift());
            car.transform.position = rb.position;
        }
    }

    // ---------------- ミサイル ----------------
    // ---------------- 武器の選択・発射 ----------------
    void UpdateWeapons(float dt)
    {
        if (!isVR)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) weapon = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) weapon = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) weapon = 2;
        }
        else
        {
            // 左トリガー: 短く押す（離した時）と武器の切り替え、1 秒長押しで音声案内のオン/オフ
            if (cycleInput && !cyclePrev) { cycleStart = Time.time; voiceToggleDone = false; }
            if (cycleInput && !voiceToggleDone && Time.time - cycleStart >= 1f) { voiceToggleDone = true; ToggleVoice(); }
            if (!cycleInput && cyclePrev && !voiceToggleDone && Time.time - cycleStart < 0.6f) weapon = (weapon + 1) % 3;
            cyclePrev = cycleInput;
        }
        bool trig = useHeld || (!isVR && (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)));
        bool down = trig && !trigPrev;
        trigPrev = trig;
        fireInput = weapon == 0 && trig;
        if (down && weapon == 1) TryFireMissile(false);
        if (down && weapon == 2) TryFireMissile(true);
        UpdateLock(dt);
    }

    // ---------------- 燃料 ----------------
    float FuelFlow(float thr, float v, float sigma)
    {
        float ratio = Mathf.Clamp(v / Mathf.Max(fuelRefSpeed, 1f), 0f, 1.5f);
        float p = enginePowerMax * (idlePowerFrac + (1f - idlePowerFrac) * thr) * sigma * (0.55f + 0.45f * ratio);
        return bsfc * p / 3600f;   // kg/s
    }

    void InitFuel()
    {
        fuelCapKg = FuelFlow(1f, fuelRefSpeed, 1f) * fullThrottleMinutes * 60f;
        fuelKg = fuelCapKg;
        lowFuelSaid = false;
        emptySaid = false;
    }

    void BurnFuel(float dt, float sigma)
    {
        if (fuelKg < 0f) InitFuel();
        if (fuelKg <= 0f) { fuelFlow = 0f; return; }
        fuelFlow = FuelFlow(throttle, airspeed, sigma);
        if (boosting) fuelFlow += FuelFlow(1f, fuelRefSpeed, 1f) * 4f;
        fuelKg = Mathf.Max(fuelKg - fuelFlow * dt, 0f);
        float frac = fuelKg / Mathf.Max(fuelCapKg, 0.0001f);
        if (frac < 0.2f && !lowFuelSaid) { lowFuelSaid = true; QueueVoice(voiceLowFuel); }
        if (fuelKg <= 0f && !emptySaid) { emptySaid = true; PlayVoiceNow(voiceFuelEmpty); }
    }

    void Refuel(float dt)
    {
        if (fuelKg < 0f) InitFuel();
        if (fuelKg >= fuelCapKg) return;
        fuelKg = Mathf.Min(fuelCapKg, fuelKg + fuelCapKg / Mathf.Max(refuelSeconds, 1f) * dt);
        if (fuelKg / fuelCapKg > 0.25f) lowFuelSaid = false;
        emptySaid = false;
    }

    public bool _NeedsFuel() { return fuelKg >= 0f && fuelKg < fuelCapKg; }

    // ---------------- 照準 ----------------
    void AimGun()
    {
        Transform eye = cockpitRoot.transform;
        Vector3 o = eye.position;
        Vector3 f = eye.forward;
        if (Time.time >= nextAimRay)
        {
            nextAimRay = Time.time + 0.1f;
            aimPoint = o + f * gunRange;
            RaycastHit[] hits = Physics.RaycastAll(o, f, gunRange, groundMask, QueryTriggerInteraction.Ignore);
            float best = gunRange;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(car.transform)) continue;
                if (hits[i].distance < best && hits[i].distance > 20f) { best = hits[i].distance; aimPoint = hits[i].point; }
            }
            // 海面
            if (f.y < -0.0001f)
            {
                float t = (seaLevelY - o.y) / f.y;
                if (t > 20f && t < best) aimPoint = o + f * t;
            }
        }
        Vector3 d = aimPoint - gunMuzzle.position;
        if (d.sqrMagnitude > 1f) gunMuzzle.rotation = Quaternion.LookRotation(d.normalized, eye.up);
        if (gunMuzzle2 != null)
        {
            Vector3 d2 = aimPoint - gunMuzzle2.position;
            if (d2.sqrMagnitude > 1f) gunMuzzle2.rotation = Quaternion.LookRotation(d2.normalized, eye.up);
        }
    }

    // ---------------- 後ろの席のレーダー・警告音 ----------------
    public void _UseGunnerRadar(bool on)
    {
        if (on == usingGunnerRadar || radarDotsG == null) return;
        if (on)
        {
            pDots = radarDots; pDotR = radarDotRenderers; pSweep = radarSweep; pRose = radarRose; pMslDots = radarMslDots; pMslR = radarMslRenderers;
            radarDots = radarDotsG; radarDotRenderers = radarDotRenderersG; radarSweep = radarSweepG; radarRose = radarRoseG; radarMslDots = radarMslDotsG; radarMslRenderers = radarMslRenderersG;
        }
        else
        {
            radarDots = pDots; radarDotRenderers = pDotR; radarSweep = pSweep; radarRose = pRose; radarMslDots = pMslDots; radarMslRenderers = pMslR;
        }
        radarPrev = null;
        usingGunnerRadar = on;
    }

    public int _CrewStallState() { return stallState; }
    public int _BurnLevel() { return burnLevel; }
    public void NetStall0() { stallState = 0; }
    public void NetStall1() { stallState = 1; }
    public void NetStall2() { stallState = 2; }

    // ---------------- AMA の警告と攻撃 ----------------
    public bool _IsHostile() { return hostile; }
    public void NetHostileOn() { hostile = true; }
    // 最初の出撃の「これは偵察任務である…」が流れている間（全員の画面）: 敵はこの機体を探さない・追わない・撃たない
    private bool briefing;
    private float briefingUntil = -1f;
    private bool reconPlayed;
    private bool iDidRecon;   // 自分が「偵察任務」を聞いた 1 人目
    public bool _InBriefing() { return briefing; }
    public void NetBriefingOn() { briefing = true; }
    public void NetBriefingOff() { briefing = false; }
    public void NetHostileOff() { hostile = false; }

    // 近くに敵（戦闘機 3km・船 2.5km 以内）がいる = 見つかった
    bool EnemyNearby()
    {
        if (radarTracks == null || car == null) return false;
        Vector3 me = car.transform.position;
        for (int i = 0; i < radarTracks.Length; i++)
        {
            int ty = TrackType(i);
            if ((ty != 0 && ty != 1) || !TrackAlive(i)) continue;
            float d = Vector3.Distance(radarTracks[i].position, me);
            if ((ty == 0 && d < 3000f) || (ty == 1 && d < 2500f)) return true;
        }
        return false;
    }

    public bool _IsSpotted() { return spotted; }
    public void NetSpottedOn() { spotted = true; }
    public void NetSpottedOff() { spotted = false; }
    // 警戒機に見つかった（ハードモード）: 船に見つかった時と同じように、全部の戦闘機が向かって来る
    private float awacsSeenTime = -100f;
    public void NetAwacsSpot() { awacsSeenTime = Time.time; }

    // 敵の船の 2.5km 以内に入ったら「見つかった」を全員に知らせる。離れて 60 秒で解除
    bool Armed() { return armedAt >= 0f && Time.time >= armedAt; }

    void UpdateSpotted()
    {
        if (!Armed()) return;
        if (Time.time < briefingUntil || Quiet()) return;   // 偵察任務の無線の間は、船にも見つからない
        if (Time.time < nextSpotScan || radarTracks == null || car == null) return;
        nextSpotScan = Time.time + 1f;
        bool near = false;
        if (hasTakenOff)
        {
            Vector3 me = car.transform.position;
            for (int i = 0; i < radarTracks.Length; i++)
            {
                if (TrackType(i) != 1 || !TrackAlive(i)) continue;
                if (Vector3.Distance(radarTracks[i].position, me) < 2500f) { near = true; break; }
            }
        }
        if (Time.time - awacsSeenTime < 3f) near = true;
        if (near)
        {
            lastShipSeen = Time.time;
            if (!sentSpotted) { sentSpotted = true; SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSpottedOn)); }
        }
        else if (sentSpotted && Time.time - lastShipSeen > 60f)
        {
            sentSpotted = false;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSpottedOff));
        }
    }

    // 2 人乗りなら、後ろの席の人にも同じ音声を送る
    void RelayVoice(AudioClip c, int ch)
    {
        if (voiceRelay != null && isPiloting && HasCrewGunner()) voiceRelay._Send(c, ch);
    }

    void PlayRadio(AudioClip c)
    {
        if (enemyRadio == null || c == null) return;
        RelayVoice(c, 1);
        enemyRadio.Stop();
        enemyRadio.clip = c;
        enemyRadio.Play();
    }

    void SetHostile()
    {
        if (hostile) return;
        hostile = true;
        if (gameMode != null) gameMode._ReportWar();   // ワールドの全員、次の出撃からは警告なし
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetHostileOn));
    }

    void ClearHostile()
    {
        warnStage = -1;
        ainStage = 0;
        carrierWarned = false;
        reconPlayed = false;
        briefingUntil = -1f;
        if (briefing) { briefing = false; SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBriefingOff)); }
        if (sentSpotted) { sentSpotted = false; SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSpottedOff)); }
        if (!hostile) return;
        hostile = false;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetHostileOff));
    }

    // 見つかったら放送 → 警告 3 回 → 最後の通告 → 攻撃開始（その前に撃ったら即攻撃）
    bool Quiet() { return gameMode != null && gameMode._Quiet(); }

    void UpdateHostility()
    {
        if (hostile || !hasTakenOff || !Armed()) return;
        if (Quiet() && reconPlayed) return;   // 「偵察任務」の無線の間は、警告も攻撃開始もしない
        // 2 回目からの出撃（一度戦闘になった後）: 潜水艦から飛び立って 3 秒後に、警告なしで攻撃開始
        // 誰かがもう戦闘になっていたら（ワールド全体）: 潜水艦から飛び立って 3 秒後に、警告なしで攻撃開始
        //   ガウリア帝国の司令部から: 味方が撃墜されていたら「我々の戦闘機が撃破された…」、まだなら「攻撃を受けた…」
        if (gameMode != null && gameMode._WarStarted() && !gameMode._IsSightseeing())
        {
            AudioClip[] lines = gameMode._OwnLost() ? amaHeavy : amaAttacked;
            if (lines != null && lines.Length > 0) PlayRadio(lines[Random.Range(0, lines.Length)]);
            SetHostile();
            return;
        }
        // まだ戦闘になっていない時の出撃: 飛び立ったら司令部から「これは偵察任務である…」。流れている間は敵に見つからない
        //   ワールドで最初に飛び立った 1 人だけ（その人が戦闘の前に墜落・降りたら、次に飛び立つ人にまた流す）
        if (!reconPlayed && (gameMode == null || !gameMode._IsSightseeing()))
        {
            reconPlayed = true;
            if (gameMode != null && gameMode._ReconDone()) return;   // もう誰かが聞いた
            int ri = (gaurRecon != null && gaurRecon.Length > 0) ? Random.Range(0, gaurRecon.Length) : -1;
            AudioClip rc = ri >= 0 ? gaurRecon[ri] : null;
            if (gameMode != null) gameMode._ReportRecon();
            iDidRecon = true;
            if (rc != null)
            {
                // ワールドの全員に流す（自分もこれで聞く）。ゲームモードが無い時だけ自分の無線で
                if (gameMode != null && gameMode.radio != null) gameMode._BroadcastRecon(ri);
                else PlayRadio(rc);
                briefingUntil = Time.time + rc.length + 1f;
                briefing = true;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBriefingOn));
            }
            return;
        }
        if (Time.time < briefingUntil) return;
        if (briefing) { briefing = false; SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetBriefingOff)); }
        // ペラゴス基地に近づいた: アイン中将の警告（船の警告とはどちらか一方だけ）
        //   10 秒以内に基地から 3km より外へ離脱しなければ攻撃開始。離脱したら「感謝する」。また入ったら警告なしで攻撃
        if (baseCenter != null && !(gameMode != null && gameMode._IsSightseeing()))
        {
            Vector3 bd = car.transform.position - baseCenter.position;
            bd.y = 0f;
            bool inBase = bd.magnitude < baseWarnRadius;
            if (ainStage == 0 && inBase && warnStage < 0)
            {
                AudioClip w = (ainWarn != null && ainWarn.Length > 0) ? ainWarn[Random.Range(0, ainWarn.Length)] : null;
                PlayRadio(w);
                ainStage = 1;
                ainDeadline = Time.time + (w != null ? w.length : 12f) + 10f;
                return;
            }
            if (ainStage == 1)
            {
                if (!inBase)
                {
                    ainStage = 2;
                    if (ainThanks != null && ainThanks.Length > 0) PlayRadio(ainThanks[Random.Range(0, ainThanks.Length)]);
                    return;
                }
                if (Time.time >= ainDeadline) SetHostile();
                return;
            }
            if (ainStage == 2)
            {
                if (inBase) SetHostile();   // 2 度目の警告はない
                return;                     // アインの警告を受けたら、船の警告はしない
            }
        }
        if (Time.time >= nextDetectScan)
        {
            nextDetectScan = Time.time + 1f;
            detected = EnemyNearby();
            if (detected) lastDetectedTime = Time.time;
        }
        if (warnStage < 0)
        {
            if (detected) { warnStage = 0; PlayRadio(amaBroadcast); nextWarnTime = Time.time + 18f; }
            return;
        }
        // 離れて 60 秒たったら、警告は最初から
        if (!detected && Time.time - lastDetectedTime > 60f) { warnStage = -1; return; }
        if (!detected || Time.time < nextWarnTime) return;   // 範囲の外にいる間は進めない
        warnStage++;
        if (warnStage == 1) { PlayRadio(amaWarn1); nextWarnTime = Time.time + 15f; }
        else if (warnStage == 2) { PlayRadio(amaWarn2); nextWarnTime = Time.time + 14f; }
        else if (warnStage == 3) { PlayRadio(amaWarn3); nextWarnTime = Time.time + 13f; }
        else if (warnStage == 4) { PlayRadio(amaFinal); nextWarnTime = Time.time + 10f; }
        else { PlayRadio(amaEngage); SetHostile(); }
    }

    // 自分（またはガンナー）が撃った: 見つかっている間なら問答無用で攻撃開始
    void OnPlayerFired()
    {
        if (!isPiloting || hostile || !Armed()) return;   // 射出前（潜水艦の上・乗る時のクリック）は数えない
        if (Quiet()) return;   // 「偵察任務」の無線の間は、撃っても攻撃してこない
        if (warnStage < 0 && !EnemyNearby()) return;   // 敵のいない所での射撃（標的の練習など）
        PlayRadio(amaFired);
        SetHostile();
    }

    public void _GunnerFired()
    {
        if (isPiloting) { OnPlayerFired(); return; }
        if (hostile || Time.time < nextCrewFiredSend) return;
        nextCrewFiredSend = Time.time + 2f;
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetCrewFired));
    }

    public void NetCrewFired() { OnPlayerFired(); }

    // ---------------- ガンナーモード ----------------
    void SetGunnerMode(bool on)
    {
        gunnerMode = on;
        fireInput = false;
        useHeld = false;
        trigPrev = false;
        lockCand = -1;
        locked = false;
        if (lockBox != null) lockBox.gameObject.SetActive(false);
        if (gunSight != null) gunSight.SetActive(!on);
        if (gunner != null) gunner._SetSolo(on);
        apNextChange = Time.time + 4f;   // 最初の数秒はまっすぐ
        apTargetRoll = 0f;
        if (isPiloting) PlayVoiceNow(on ? voiceApOn : voiceApOff);
    }

    // ---------------- 音声案内 ----------------
    void UpdateVoice()
    {
        if (!isVR && Input.GetKeyDown(KeyCode.M)) ToggleVoice();
        if (!isVR && Input.GetKeyDown(KeyCode.H)) ToggleGaugeOnly();
        if (!isVR && Input.GetKeyDown(KeyCode.G)) ToggleGear();

        if (!hasTakenOff && catapult == null && !grounded && !floating && altitude > 30f) hasTakenOff = true;
        // カタパルト以外（水面から）で飛び立った時も、3 秒後から
        if (armedAt < 0f && hasTakenOff && catapult == null) armedAt = Time.time + 3f;

        if (Time.time >= nextVoiceScan && radarTracks != null && car != null && bootDone)
        {
            nextVoiceScan = Time.time + 0.5f;
            int n = radarTracks.Length;
            if (vAnnounced == null || vAnnounced.Length != n) { vAnnounced = new bool[n]; vDead = new bool[n]; vInit = false; }
            Vector3 me = car.transform.position;
            Vector3 f = axisReference != null ? axisReference.forward : -car.transform.right;
            f.y = 0f;
            if (f.sqrMagnitude < 0.0001f) f = Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                if (TrackType(i) != 0 || radarTracks[i] == null) continue;
                Vector3 d = radarTracks[i].position - me;
                float dist = d.magnitude;
                if (!TrackAlive(i))
                {
                    // 落ちた瞬間（近くにいた時だけ）
                    if (!vDead[i] && vInit && dist < voiceKillRange) QueueVoice(voiceKill);
                    vDead[i] = true;
                    vAnnounced[i] = false;
                    continue;
                }
                vDead[i] = false;
                if (!hasTakenOff) continue;   // 離陸するまでは敵ドローンの方向を知らせない
                if (!vAnnounced[i] && dist < voiceDetectRange)
                {
                    vAnnounced[i] = true;
                    d.y = 0f;
                    float rel = Vector3.SignedAngle(f, d, Vector3.up);   // 時計回りが +
                    int clock = ((Mathf.RoundToInt(rel / 30f) % 12) + 12) % 12;
                    if (voiceClock != null && clock < voiceClock.Length) QueueVoice(voiceClock[clock]);
                }
                else if (vAnnounced[i] && dist > voiceDetectRange * 1.3f) vAnnounced[i] = false;
            }
            vInit = true;
        }

        // 自分が当てた物が壊れたら（敵ドローンは「Splash one」が流れるので除く）
        if (lastHitFt != null)
        {
            if (Time.time - lastHitTime > 4f) lastHitFt = null;
            else if (lastHitFt._IsDestroyed())
            {
                if (!IsEnemyTarget(lastHitFt)) QueueVoice(voiceDestroyed);
                lastHitFt = null;
            }
        }

        // 順番に流す（重ならないように）
        if (voiceAudio != null && vQCount > 0 && !voiceAudio.isPlaying)
        {
            voiceAudio.clip = vQueue[0];
            for (int k = 1; k < vQCount; k++) vQueue[k - 1] = vQueue[k];
            vQCount--;
            if (voiceAudio.clip != null) voiceAudio.Play();
        }
    }

    // 今流れている音声を止めてすぐに流す（射出の瞬間など）
    void PlayVoiceNow(AudioClip c)
    {
        if (!voiceEnabled || c == null || voiceAudio == null) return;
        RelayVoice(c, 2);
        vQCount = 0;
        voiceAudio.Stop();
        voiceAudio.clip = c;
        voiceAudio.Play();
    }

    // ---------------- プレイヤー同士 ----------------
    // 自分の弾・ミサイルが、ほかのプレイヤーが乗っている機体に当たった → その機体の操縦者の画面でダメージ
    public bool _PvpHit(Collider col, bool missile)
    {
        if (col == null || car == null) return false;
        Rigidbody arb = col.attachedRigidbody;
        if (arb == null || arb.transform == car.transform) return false;
        controller other = arb.GetComponentInChildren<controller>();
        if (other == null || other == this || other._GetPilot() == null) return false;
        other.SendCustomNetworkEvent(NetworkEventTarget.Owner, missile ? nameof(NetPlayerMissileHit) : nameof(NetPlayerGunHit));
        return true;
    }

    public void NetPlayerGunHit()
    {
        if (!isPiloting) return;
        Damage(pvpGunDamage);
        TakingFireVoice(3);
    }

    public void NetPlayerMissileHit()
    {
        if (!isPiloting) return;
        Damage(pvpMissileDamage);
        TakingFireVoice(3);
    }

    // 船がこちらへミサイルを撃った（その船の方向を言う。急ぐので今の音声を止めてすぐ流す）
    // 空母の戦闘機の追尾ミサイル: 「誘導ミサイル接近」
    public AudioClip[] voiceGuided;
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
        joinLog._RunKill(ty, (g != null && Utilities.IsValid(g)) ? g.displayName : "", hard);
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

    // 赤い四角を標的に重ねる（カメラが動いた後に置くとずれない）
    public override void PostLateUpdate()
    {
        if (lockBox == null) return;
        bool show = isPiloting && lockCand >= 0 && TrackAlive(lockCand) && (locked || (Time.time * 8f) % 2f < 1.3f);
        if (lockBox.gameObject.activeSelf != show) lockBox.gameObject.SetActive(show);
        if (!show || localPlayer == null) return;
        VRCPlayerApi.TrackingData head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 d = radarTracks[lockCand].position - head.position;
        float dist = d.magnitude;
        if (dist < 1f) return;
        Vector3 dir = d / dist;
        // 5m 先に、標的と同じ見た目の大きさで置く
        float size = Mathf.Clamp(TrackRadius(lockCand) * 2.4f * 5f / dist, 0.18f, 2.5f);
        if (!locked) size *= 1f + 1.2f * (1f - Mathf.Clamp01(lockTimer / lockTime));   // ロック中は縮みながら近づく
        lockBox.position = head.position + dir * 5f;
        lockBox.rotation = Quaternion.LookRotation(dir, head.rotation * Vector3.up);
        lockBox.localScale = new Vector3(size, size, size);
    }

    void TryFireMissile(bool guided)
    {
        if (missiles == null || missiles.Length == 0 || gunMuzzle == null) return;
        if (Time.time - lastMissileTime < missileCooldown) return;
        if (guided)
        {
            if (guidedLeft <= 0) return;
            if (!locked) { noLockMsgUntil = Time.time + 1.2f; return; }
            guidedLeft--;
        }
        else
        {
            if (missileLeft <= 0) return;
            missileLeft--;
        }
        lastMissileTime = Time.time;
        OnPlayerFired();
        LaunchMissile(true, guided ? lockCand : -1);
        QueueVoice(voiceMissile);
        // 他の人の画面にも飛ばす（見た目だけ）
        SendCustomNetworkEvent(NetworkEventTarget.All, guided ? nameof(NetMissileGuided) : nameof(NetMissile));
    }

    public void NetMissile()
    {
        if (isPiloting) return;   // 自分の分は発射済み
        LaunchMissile(false, -1);
    }

    public void NetMissileGuided()
    {
        if (isPiloting) return;
        LaunchMissile(false, FindLockCandidate());   // 見ている人の画面でも、機首の前の標的を追う
    }

    void LaunchMissile(bool real, int tgt)
    {
        if (gunMuzzle == null) return;
        _LaunchMissileFrom(real, tgt, gunMuzzle.position - gunMuzzle.up * 0.6f, gunMuzzle.forward);
    }

    // 発射位置と向きを指定して撃つ（ガンナー席の砲塔からも使う）
    public void _LaunchMissileFrom(bool real, int tgt, Vector3 p, Vector3 fwd)
    {
        if (missiles == null || mActive == null) return;
        // 空いているミサイルを探す（無ければ一番古いものを使い回す）
        int slot = -1;
        float oldest = -1f;
        for (int i = 0; i < missiles.Length; i++)
        {
            if (missiles[i] == null) continue;
            if (!mActive[i]) { slot = i; break; }
            if (mAge[i] > oldest) { oldest = mAge[i]; slot = i; }
        }
        if (slot < 0) return;

        // 撃った向き（fwd）へ飛ばす。速さは機体の速さ＋発射の速さ
        // （機体の速度ベクトルをそのまま足すと、横や後ろへ撃っても機体の進む方へ流れてしまう）
        float baseSpeed = rb != null ? rb.velocity.magnitude : 0f;
        Vector3 v = fwd.normalized * (baseSpeed + missileLaunchSpeed);
        mPos[slot] = p;
        mVel[slot] = v;
        mAge[slot] = 0f;
        mActive[slot] = true;
        mReal[slot] = real;
        mBurning[slot] = true;
        mTgt[slot] = tgt;
        mTgtPrev[slot] = (tgt >= 0 && radarTracks[tgt] != null) ? radarTracks[tgt].position : Vector3.zero;

        Transform m = missiles[slot];
        m.position = p;
        m.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        m.gameObject.SetActive(true);
        TrailRenderer tr = m.GetComponent<TrailRenderer>();
        if (tr != null) { tr.Clear(); tr.emitting = true; }
        Transform flame = m.Find("Flame");
        if (flame != null) flame.gameObject.SetActive(true);
        AudioSource au = m.GetComponent<AudioSource>();
        if (au != null && au.clip != null) au.PlayOneShot(au.clip, 1f);
    }

    void UpdateMissiles(float dt)
    {
        if (missiles == null || mActive == null) return;
        for (int i = 0; i < missiles.Length; i++)
        {
            if (!mActive[i]) continue;
            mAge[i] += dt;
            Vector3 v = mVel[i];
            float sp = v.magnitude;
            Vector3 dir = sp > 0.1f ? v / sp : missiles[i].forward;
            bool guided = mTgt[i] >= 0;
            if (guided && !TrackAlive(mTgt[i])) { mTgt[i] = -1; guided = false; }
            bool burning = mAge[i] < (guided || mTgt[i] >= 0 ? guidedBurnTime : missileBurnTime);
            Vector3 tp = Vector3.zero;
            float tRad = 0f;
            if (guided)
            {
                // 誘導: 標的の動きを先読みして、曲がれる速さの範囲で向きを変える
                tp = radarTracks[mTgt[i]].position;
                tRad = TrackRadius(mTgt[i]);
                Vector3 tv = dt > 0.0001f ? (tp - mTgtPrev[i]) / dt : Vector3.zero;
                mTgtPrev[i] = tp;
                if (mAge[i] > 0.25f && sp > 1f)
                {
                    float tgo = Mathf.Min(Vector3.Distance(tp, mPos[i]) / Mathf.Max(sp, 40f), 4f);
                    Vector3 aim = tp + tv * tgo - mPos[i];
                    if (aim.sqrMagnitude > 0.01f)
                    {
                        v = Vector3.RotateTowards(v, aim.normalized * sp, guidedTurnRate * Mathf.Deg2Rad * dt, 0f);
                        dir = v / sp;
                    }
                }
            }

            // 推進中はほぼまっすぐ（重力は少しだけ）、推進が終わると重力で落ちていく
            Vector3 acc = Vector3.down * 9.81f * (burning ? (guided ? 0f : 0.15f) : 1f) - v * sp * 0.0012f;
            if (burning) acc += dir * missileAccel;
            v += acc * dt;
            Vector3 p0 = mPos[i];
            Vector3 p1 = p0 + v * dt;

            // 当たり判定（建物・標的・地面）
            Vector3 seg = p1 - p0;
            float len = seg.magnitude;
            float hitDist = float.MaxValue;
            Vector3 hitPoint = p1;
            Collider hitCol = null;
            if (len > 0.0001f)
            {
                RaycastHit[] hits = Physics.RaycastAll(p0, seg / len, len, groundMask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < hits.Length; k++)
                {
                    Collider c = hits[k].collider;
                    if (c == null || c.transform.IsChildOf(car.transform)) continue;
                    if (hits[k].distance < hitDist) { hitDist = hits[k].distance; hitPoint = hits[k].point; hitCol = c; }
                }
            }
            // 海面
            bool water = false;
            if (p1.y < seaLevelY && p0.y >= seaLevelY)
            {
                float t = (seaLevelY - p0.y) / (p1.y - p0.y);
                float wd = len * Mathf.Clamp01(t);
                if (wd <= hitDist) { water = true; hitPoint = p0 + seg * Mathf.Clamp01(t); hitCol = null; }
            }
            // 近接信管: 標的の近くを通ったら爆発
            if (guided && len > 0.0001f)
            {
                float tt = Mathf.Clamp01(Vector3.Dot(tp - p0, seg) / (len * len));
                Vector3 cpt = p0 + seg * tt;
                if ((tp - cpt).magnitude < tRad + 2f && len * tt <= hitDist)
                {
                    FlightTarget dft = (radarTrackTargets != null && mTgt[i] < radarTrackTargets.Length) ? radarTrackTargets[mTgt[i]] : null;
                    MissileImpact(i, cpt, false, null, dft);
                    continue;
                }
            }
            if (water || hitCol != null)
            {
                MissileImpact(i, hitPoint, water, hitCol, null);
                continue;
            }
            if (mAge[i] > 15f || p1.y < seaLevelY - 5f) { EndMissile(i); continue; }

            mPos[i] = p1;
            mVel[i] = v;
            missiles[i].position = p1;
            if (v.sqrMagnitude > 0.01f) missiles[i].rotation = Quaternion.LookRotation(v);
            if (mBurning[i] && !burning)
            {
                // 推進が終わったら炎と煙を止める
                mBurning[i] = false;
                Transform flame = missiles[i].Find("Flame");
                if (flame != null) flame.gameObject.SetActive(false);
                TrailRenderer tr = missiles[i].GetComponent<TrailRenderer>();
                if (tr != null) tr.emitting = false;
            }
        }
    }

    void MissileImpact(int i, Vector3 p, bool water, Collider hitCol, FlightTarget direct)
    {
        if (water)
        {
            // 海に落ちたら大きな水柱
            if (missileSplashFx != null)
            {
                missileSplashFx.transform.position = new Vector3(p.x, seaLevelY, p.z);
                missileSplashFx.Emit(70);
            }
        }
        else if (crashFx != null)
        {
            crashFx.transform.position = p;
            crashFx.Play(true);
        }
        if (crashAudio != null)
        {
            crashAudio.transform.position = p;
            crashAudio.Play();
        }

        // 撃った本人の画面でだけダメージを与える
        if (mReal[i] && hitCol != null)
        {
            FlightTarget tg = FindTarget(hitCol);
            if (tg != null) { tg._HitMissile(); NoteHit(tg); if (!IsEnemyTarget(tg)) QueueVoice(voiceTargetHit); }
            else if (_PvpHit(hitCol, true)) QueueVoice(voiceTargetHit);   // ほかのプレイヤーの機体
            else QueueVoice(voiceImpact);   // 島・建物など
        }
        else if (mReal[i] && direct != null) { direct._HitMissile(); NoteHit(direct); if (!IsEnemyTarget(direct)) QueueVoice(voiceTargetHit); }
        EndMissile(i);
    }

    void EndMissile(int i)
    {
        mActive[i] = false;
        if (missiles[i] != null) missiles[i].gameObject.SetActive(false);
    }

    // ---------------- 機銃 ----------------
    void UpdateGun(float dt)
    {
        bool want = isPiloting && fireInput && ammo > 0 && gunMuzzle != null;
        if (want != localFiring)
        {
            localFiring = want;
            // 他の人の画面でも曳光弾と音を出す（当たり判定は撃った本人だけ）
            SendCustomNetworkEvent(NetworkEventTarget.All, want ? nameof(NetFireOn) : nameof(NetFireOff));
        }

        bool firing = localFiring || remoteFiring;
        if (!firing || gunMuzzle == null)
        {
            fireTimer = 0f;
            return;
        }
        fireTimer -= dt;
        int guard = 0;
        while (fireTimer <= 0f && guard < 4)
        {
            fireTimer += 1f / Mathf.Max(fireRate, 1f);
            guard++;
            FireOne(localFiring);
            if (localFiring && ammo <= 0) break;
        }
    }

    public void NetFireOn()
    {
        if (isPiloting) return;   // 自分の分は UpdateGun で処理済み
        remoteFiring = true;
    }

    public void NetFireOff()
    {
        remoteFiring = false;
    }

    void FireOne(bool real)
    {
        // 翼に 2 門あれば、左右交互に撃つ
        Transform mz = gunMuzzle;
        ParticleSystem tfx = tracerFx;
        if (gunMuzzle2 != null)
        {
            gunSide = !gunSide;
            if (gunSide) { mz = gunMuzzle2; tfx = tracerFx2; }
        }
        if (tfx != null) tfx.Emit(1);
        if (gunAudio != null && gunAudio.clip != null)
        {
            gunAudio.pitch = Random.Range(0.92f, 1.08f);
            gunAudio.PlayOneShot(gunAudio.clip, Random.Range(0.85f, 1f));
        }

        Vector3 origin = mz.position;
        Vector3 dir = mz.forward;
        if (gunSpread > 0f)
        {
            dir = Quaternion.AngleAxis(Random.Range(-gunSpread, gunSpread), mz.up)
                * Quaternion.AngleAxis(Random.Range(-gunSpread, gunSpread), mz.right) * dir;
        }

        // 海面との交点（下向きに撃った時だけ）
        float waterDist = -1f;
        if (dir.y < -0.0001f)
        {
            float t = (seaLevelY - origin.y) / dir.y;
            if (t > 0f && t < gunRange) waterDist = t;
        }

        if (!real)
        {
            // 他の人が撃った弾: 水柱だけ出す（当たり判定は撃った本人だけ）
            if (waterDist > 0f) WaterSplash(origin + dir * waterDist);
            return;
        }

        ammo--;
        OnPlayerFired();
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, gunRange, groundMask, QueryTriggerInteraction.Ignore);
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null) continue;
            if (col.transform.IsChildOf(car.transform)) continue;   // 自分の機体は無視
            if (hits[i].distance < bestDist) { bestDist = hits[i].distance; best = i; }
        }

        // 海面の方が手前（または何にも当たらない）なら水柱
        if (waterDist > 0f && (best < 0 || waterDist <= bestDist + 0.5f))
        {
            WaterSplash(origin + dir * waterDist);
            return;
        }
        if (best < 0) return;

        RaycastHit h = hits[best];
        if (impactFx != null)
        {
            impactFx.transform.position = h.point;
            impactFx.transform.rotation = Quaternion.LookRotation(h.normal);
            impactFx.Emit(8);
        }
        FlightTarget tg = FindTarget(h.collider);
        if (tg != null) { tg._Hit(); NoteHit(tg); }
        else _PvpHit(h.collider, false);   // ほかのプレイヤーの機体
    }

    // 当たった物の標的を探す: 物理の付いた物（敵機など）はその中から、それ以外は親をたどる
    FlightTarget FindTarget(Collider col)
    {
        if (col == null) return null;
        Rigidbody arb = col.attachedRigidbody;
        if (arb != null && arb.transform != car.transform)
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

    void WaterSplash(Vector3 p)
    {
        if (waterSplashFx == null) return;
        waterSplashFx.transform.position = p;
        waterSplashFx.Emit(12);
    }

    // ---------------- HUD / 音 ----------------
    // ---------------- コックピット表示 ----------------
    void UpdateCockpit()
    {
        if (cockpitRoot == null || rb == null) return;
        Transform ct = car.transform;
        Vector3 fwd, up, right;
        if (axisReference != null) { fwd = axisReference.forward; up = axisReference.up; right = axisReference.right; }
        else { fwd = -ct.right; up = ct.up; right = ct.forward; }

        float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
        float roll = Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;     // 右翼下げ +
        float hdg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        if (hdg < 0f) hdg += 360f;
        float vs = rb.velocity.y;
        float stallLimit = stallAngle - flapStallReduction * flapPos;

        if (cpSpeed != null) cpSpeed.text = (airspeed * 3.6f).ToString("F0");
        if (cpAlt != null) cpAlt.text = altitude.ToString("F0");
        if (cpVs != null) cpVs.text = "VS " + (vs >= 0f ? "+" : "") + vs.ToString("F1") + " m/s";
        if (cpAtt != null) cpAtt.text = "P " + (pitch >= 0f ? "+" : "") + pitch.ToString("F0") + "°  R " + (roll >= 0f ? "+" : "") + roll.ToString("F0") + "°";
        if (cpHdg != null)
        {
            string hc = hp > 60f ? "#7fff9f" : (hp > 30f ? "#ffc040" : "#ff3030");
            cpHdg.text = "HDG " + Mathf.RoundToInt(hdg % 360f).ToString("000") + " " + CompassName(hdg);
        }
        // 時計のようなメーター（針は時計回り）
        if (cpSpdNeedle != null)
        {
            float kmh = Mathf.Clamp(airspeed * 3.6f, 0f, 300f);
            cpSpdNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-135f + kmh / 300f * 270f));
        }
        float altC = Mathf.Max(altitude, 0f);
        if (cpAltNeedle != null) cpAltNeedle.localRotation = Quaternion.Euler(0f, 0f, -(altC % 1000f) / 1000f * 360f);
        if (cpAltNeedleK != null) cpAltNeedleK.localRotation = Quaternion.Euler(0f, 0f, -(altC % 10000f) / 10000f * 360f);
        if (cpVsiNeedle != null) cpVsiNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-90f + Mathf.Clamp(vs, -10f, 10f) * 17f));
        if (cpThrNeedle != null) cpThrNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-120f + Mathf.Clamp01(throttle) * 240f));
        if (cpFuelNeedle != null) cpFuelNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-60f + (fuelKg < 0f || fuelCapKg <= 0f ? 1f : Mathf.Clamp01(fuelKg / fuelCapKg)) * 120f));
        if (cpAoaNeedle != null) cpAoaNeedle.localRotation = Quaternion.Euler(0f, 0f, -(-120f + Mathf.Clamp(aoaDeg, 0f, 25f) / 25f * 240f));
        if (cpHdgCard != null) cpHdgCard.localRotation = Quaternion.Euler(0f, 0f, hdg);
        if (cpHdgNum != null) cpHdgNum.text = Mathf.RoundToInt(hdg % 360f).ToString("000") + "°";
        if (cpVsNum != null) cpVsNum.text = (vs >= 0f ? "+" : "") + vs.ToString("F1");
        if (cpFuelPct != null) cpFuelPct.text = Mathf.RoundToInt((fuelKg < 0f || fuelCapKg <= 0f ? 1f : Mathf.Clamp01(fuelKg / fuelCapKg)) * 100f).ToString() + "%";
        if (cpTurnPlane != null)
        {
            // 旋回の速さ（度/秒）。1 秒に 3° で目盛り（20°）まで傾く
            float now = Time.time, rate = 0f;
            if (lastHdgT > 0f && now > lastHdgT) rate = Mathf.DeltaAngle(lastHdg, hdg) / (now - lastHdgT);
            lastHdgT = now; lastHdg = hdg;
            float cur = -cpTurnPlane.localEulerAngles.z;
            if (cur < -180f) cur += 360f;
            if (cur > 180f) cur -= 360f;
            float want = Mathf.Clamp(rate / 3f * 20f, -35f, 35f);
            cpTurnPlane.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Lerp(cur, want, 0.15f));
        }
        UpdateHud(pitch, roll, hdg, vs);
        if (cpAttDisc != null)
        {
            if (attMat == null) attMat = cpAttDisc.material;
            attMat.SetFloat("_Pitch", pitch);
            attMat.SetFloat("_Roll", roll);
        }

        // HP・弾数・武器
        if (cpHp != null)
        {
            string hc2 = hp > 60f ? "#7fff9f" : (hp > 30f ? "#ffc040" : "#ff3030");
            cpHp.text = "<color=" + hc2 + ">" + Mathf.CeilToInt(hp).ToString() + "</color><size=50%> / " + Mathf.CeilToInt(maxHp).ToString() + "</size>";
        }
        SetBar(cpHpBar, maxHp > 0f ? hp / maxHp : 0f, false);
        if (cpWeapons != null)
        {
            cpWeapons.text = WeaponLine(0, "1  GUN", ammo.ToString())
                + "\n" + WeaponLine(1, "2  RKT", missileLeft.ToString() + " / " + maxMissiles.ToString())
                + "\n" + WeaponLine(2, "3  MSL", guidedLeft.ToString() + " / " + maxGuided.ToString());
        }
        if (cpLock != null)
        {
            string lk = "";
            if (Time.time < noLockMsgUntil) lk = "<color=#ff3030>NO LOCK</color>";
            else if (weapon == 2)
            {
                if (guidedLeft <= 0) lk = "<color=#ff3030>MSL EMPTY</color>";
                else if (locked) lk = "<color=#ff3030>LOCK ON  " + (Vector3.Distance(radarTracks[lockCand].position, car.transform.position) / 1000f).ToString("F1") + "km</color>";
                else if (lockCand >= 0) lk = "<color=#ffc040>LOCKING...</color>";
                else lk = "<color=#3a8a98>SEARCH</color>";
            }
            cpLock.text = lk;
        }

        // 入室・退室の記録（0.5 秒ごと）
        if (joinLog != null && Time.time >= nextLogUpdate)
        {
            nextLogUpdate = Time.time + 0.5f;
            if (cpLog != null) cpLog.text = joinLog._GetText();
            if (cpPlayers != null) cpPlayers.text = "ONLINE  " + joinLog._GetPlayerCount().ToString();
            if (cpKills != null)
            {
                // 今回の出撃（撃墜されたら 0 から）の記録。2 人乗りなら 2 人の記録
                bool duoNow = HasCrewGunner();
                bool hardNow = gameMode != null && gameMode._IsHard();
                cpKills.text = (duoNow ? "<size=70%>DUO </size>" : "") + (hardNow ? "<size=70%><color=#ff4040>HARD </color></size>" : "")
                    + "<color=#ffd040>" + joinLog._RunScore(duoNow).ToString() + "pt</color>"
                    + "  <size=75%><color=#ff6a5a>AIR " + joinLog._RunAir(duoNow).ToString() + "</color>"
                    + " <color=#ffa030>SHIP " + joinLog._RunShips(duoNow).ToString() + "</color>"
                    + " <color=#ffe070>CV " + joinLog._RunCarriers(duoNow).ToString() + "</color></size>";
            }
        }

        if (cpAoa != null) cpAoa.text = aoaDeg.ToString("F1") + "°";
        if (cpFuel != null)
        {
            if (fuelKg < 0f) InitFuel();
            float fr = fuelKg / Mathf.Max(fuelCapKg, 0.0001f);
            float endur = fuelFlow > 0.00001f ? fuelKg / fuelFlow : 0f;
            int mm = Mathf.FloorToInt(endur / 60f), ss2 = Mathf.FloorToInt(endur % 60f);
            string fc = fr > 0.4f ? "#7fff9f" : (fr > 0.2f ? "#ffc040" : "#ff3030");
            cpFuel.text = "FUEL <color=" + fc + ">" + Mathf.CeilToInt(fr * 100f).ToString() + "%</color>"
                + "<size=75%>  " + (fuelKg / 0.72f).ToString("F1") + "L  " + (fuelFlow > 0.00001f ? mm.ToString() + ":" + ss2.ToString("00") : "--:--") + "</size>";
        }
        if (cpThr != null) cpThr.text = (throttle * 100f).ToString("F0") + "%";
        if (cpGun != null) cpGun.text = ammo.ToString() + "  <size=70%><color=#ffc040>MSL " + missileLeft.ToString() + "</color></size>";
        if (cpFlags != null)
        {
            string f = flapsDown ? "<color=#ffc040>FLAP</color>" : "<color=#9ab8a2>FLAP</color>";
            string brakeName = grounded ? "BRAKE" : (floating ? "W-BRAKE" : "AIR BRAKE");
            f += "\n" + (braking ? "<color=#ffc040>" + brakeName + "</color>" : "<color=#9ab8a2>BRAKE</color>");
            f += "\n" + (boosting ? "<color=#ff7040>BOOST</color>" : "<color=#9ab8a2>BOOST</color>");
            f += "\n" + (voiceEnabled ? "<color=#7fff9f>VOICE [M]</color>" : "<color=#9ab8a2>VOICE OFF [M]</color>");
            if (_HasGear())
            {
                if (gearAnim >= 1f) f += "\n<color=#7fff9f>GEAR DOWN [G]</color>";
                else if (gearAnim <= 0f) f += "\n<color=#9ab8a2>GEAR UP [G]</color>";
                else f += "\n<color=#ffc040>GEAR " + (gearDown ? "DOWN..." : "UP...") + "</color>";
            }
            cpFlags.text = f;
        }

        SetBar(cpThrBar, throttle, true);
        SetBar(cpAoaBar, Mathf.Clamp01(aoaDeg / (stallLimit + 5f)), true);
        SetBar(cpGunBar, maxAmmo > 0 ? (float)ammo / maxAmmo : 0f, false);

        if (cpHorizon != null)
        {
            cpHorizon.localRotation = Quaternion.Euler(0f, 0f, roll);
            Vector3 hp = cpHorizon.localPosition;
            hp.y = Mathf.Clamp(-pitch * 0.003f, -0.055f, 0.055f);
            cpHorizon.localPosition = hp;
        }

        if (cpWarn != null)
        {
            bool blink = (Time.time * 3f) % 2f < 1.2f;
            string w = "";
            if (Time.time - lastDamageTime < 1.5f) w = "<color=#ff3030>DAMAGE -" + Mathf.CeilToInt(lastDamage).ToString() + "</color>";
            else if (burnLevel >= 3) w = blink ? "<color=#ff3030>CRITICAL DAMAGE</color>" : "";
            else if (stallAmount > 0.05f) w = blink ? "<color=#ff3030>STALL</color>" : "";
            else if (burnLevel == 2) w = blink ? "<color=#ff7020>ENGINE FIRE</color>" : "";
            else if (fuelKg == 0f) w = blink ? "<color=#ff3030>NO FUEL</color>" : "";
            else if (fuelKg > 0f && fuelKg < fuelCapKg * 0.2f) w = "<color=#ffc040>LOW FUEL</color>";
            else if (!grounded && airspeed > 3f && aoaDeg > stallLimit - 3f) w = "<color=#ffc040>STALL WARNING</color>";
            else if (ammo <= 0 && weapon == 0) w = "<color=#ff3030>GUN EMPTY</color>";
            else if (!grounded && altitude < 15f && vs < -3f) w = blink ? "<color=#ff3030>PULL UP</color>" : "";
            cpWarn.text = w;
        }

        UpdateRadar();
        UpdateCockpitDamage();

        // 起動演出
        float bt = Time.time - bootStart;
        bool booting = bt < bootDuration;
        if (booting)
        {
            ApplyBootPanels(bt);
            // 2.4 秒まで真っ暗、そこから 3.9 秒にかけて外とコックピットが明るくなる
            SetBlackout(0f);   // 乗った時に真っ暗にしない
        }
        else if (!bootDone) { ApplyBootPanels(100f); SetBlackout(0f); bootDone = true; QueueVoice(voiceWelcome); }

        if (cpCenter != null && booting)
        {
            cpCenter.text = BootText(bt);
        }
        else if (cpCenter != null)
        {
            string cm = "";
            if (catapult != null)
            {
                if (catLaunching) cm = "<color=#ffc040><size=150%>LAUNCH!</size></color>";
                else if (catHold > 0f)
                    cm = "<color=#ffc040>LAUNCH IN\n<size=180%>" + Mathf.Max(catapult.holdTime - catHold, 0f).ToString("F1") + "</size></color>";
                else
                    cm = "<color=#66ccff>CATAPULT READY</color>\n"
                       + (isVR ? "<size=70%>Hold the LEFT GRIP and push your hand forward\nto raise the throttle to 100%. The catapult launches at 100%.</size>\n"
                               : "<size=70%>Hold SHIFT to raise the throttle to 100%.\nThe catapult launches at 100%.</size>\n")
                       + "<size=80%>THROTTLE  " + (throttle * 100f).ToString("F0") + "% / 100%</size>";
            }
            else if (boosting) cm = "<color=#ff7040>" + (boostTimer < boostLiftTime ? "BOOST  UP" : "BOOST  FORWARD") + "</color>";
            else if (gunnerMode) cm = "<size=60%><color=#7fff9f>GUNNER MODE  -  AUTOPILOT\nPress P to fly</color></size>";
            cpCenter.text = cm;
        }

        if (cpStatus != null)
        {
            string st = "";
            if (repairing) st = "<color=#7fff9f>REPAIR / REARM</color>";
            else if (radioCount > 0) st = "<color=#7fff9f>RADIO  " + radioCount.ToString() + " PILOT" + (radioCount > 1 ? "S" : "") + "</color>";
            cpStatus.text = st;
        }
    }

    // ---------------- 被弾時のコックピット ----------------
    // 段階 1: 小さく揺れる / 2: 赤い点滅・警報音・画面がちらつく / 3: さらに強く・中に煙
    // HUD の更新（線の位置は毎フレーム、文字は 1 秒に 10 回）
    void UpdateHud(float pitch, float roll, float hdg, float vs)
    {
        if (hudFpm == null && hudSpd == null) return;
        Transform cr = cockpitRoot.transform;
        float dt = Time.deltaTime;
        // G（機体の上方向にかかる力）
        Vector3 v = rb.velocity;
        if (!hudGInit || dt <= 0f) { hudLastVel = v; hudGInit = true; }
        else
        {
            Vector3 acc = (v - hudLastVel) / dt + Vector3.up * 9.81f;
            hudLastVel = v;
            float g = Vector3.Dot(acc, cr.up) / 9.81f;
            hudGNow = Mathf.Lerp(hudGNow, g, Mathf.Clamp01(dt * 8f));
            if (!grounded && hudGNow > hudGMax) hudGMax = hudGNow;
        }
        // 飛んでいる方向
        if (hudFpm != null)
        {
            Vector3 lv = cr.InverseTransformDirection(v);
            if (v.magnitude > 3f && lv.z > 0.1f)
            {
                float x = Mathf.Clamp(lv.x / lv.z, -0.36f, 0.36f), y = Mathf.Clamp(lv.y / lv.z, -0.2f, 0.36f);
                hudFpm.localPosition = new Vector3(x, y, 1f);
            }
            else hudFpm.localPosition = new Vector3(0f, 0f, 1f);
        }
        // 角度のはしご: 世界の水平・機首の方位に合わせる。前から外れた線と計器盤に重なる線は隠す
        if (hudLadder != null && hudLadderLines != null)
        {
            Vector3 f = cr.forward;
            hudLadder.rotation = Quaternion.Euler(0f, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 0f);
            for (int i = 0; i < hudLadderLines.Length; i++)
            {
                Transform ln = hudLadderLines[i];
                if (ln == null) continue;
                bool show = Mathf.Abs(hudLadderDeg[i] - pitch) < 25f;
                if (show)
                {
                    Vector3 ld = cr.InverseTransformDirection(ln.position - cr.position);
                    show = ld.z > 0.1f && ld.y / ld.z > -0.03f && ld.y / ld.z < 0.3f;
                }
                if (ln.gameObject.activeSelf != show) ln.gameObject.SetActive(show);
            }
        }
        if (hudRollPtr != null) hudRollPtr.localRotation = Quaternion.Euler(0f, 0f, roll);
        // 方位のテープ
        if (hudTapeTicks != null)
        {
            int n = hudTapeTicks.Length;
            float baseDeg = Mathf.Floor(hdg / 5f) * 5f;
            for (int i = 0; i < n; i++)
            {
                float td = baseDeg + (i - n / 2) * 5f;
                if (hudTapeTicks[i] == null) continue;
                hudTapeTicks[i].localPosition = new Vector3((td - hdg) * hudTapeScale, 0f, 0f);
                if (hudTapeLabels != null && hudTapeLabels[i] != null)
                {
                    int dd = Mathf.RoundToInt(Mathf.Repeat(td, 360f));
                    string want = dd % 10 == 0 ? (dd == 0 ? "N" : dd == 90 ? "E" : dd == 180 ? "S" : dd == 270 ? "W" : (dd / 10).ToString("00")) : "";
                    if (hudTapeLabels[i].text != want) hudTapeLabels[i].text = want;
                }
            }
        }
        // 棒と操縦桿
        if (hudThrFill != null) hudThrFill.localScale = new Vector3(Mathf.Clamp01(throttle), 1f, 1f);
        if (hudHpFill != null) hudHpFill.localScale = new Vector3(maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f, 1f, 1f);
        if (hudStickDot != null) hudStickDot.localPosition = new Vector3(Mathf.Clamp(inRoll, -1f, 1f) * hudStickSize, Mathf.Clamp(inPitch, -1f, 1f) * hudStickSize, 0f);
        if (hudYawDot != null) hudYawDot.localPosition = new Vector3(Mathf.Clamp(inYaw, -1f, 1f) * hudStickSize, -hudStickSize - 0.015f, 0f);

        // 文字
        if (Time.time < hudTextT) return;
        hudTextT = Time.time + 0.1f;
        if (hudHdg != null) hudHdg.text = Mathf.RoundToInt(hdg % 360f).ToString("000");
        if (hudSpd != null) hudSpd.text = (airspeed * 3.6f).ToString("F0");
        if (hudAoa != null)
        {
            float lim = stallAngle - flapStallReduction * flapPos;
            hudAoa.text = aoaDeg > lim - 3f ? "<color=#ff4030>" + aoaDeg.ToString("F1") + "</color>" : aoaDeg.ToString("F1");
        }
        if (hudMach != null) hudMach.text = (airspeed / 340f).ToString("F2");
        if (hudG != null) hudG.text = hudGNow.ToString("F1");
        if (hudMaxG != null) hudMaxG.text = hudGMax.ToString("F1");
        if (hudGun != null) hudGun.text = ammo.ToString();
        if (hudRkt != null) hudRkt.text = missileLeft.ToString();
        if (hudMsl != null) hudMsl.text = guidedLeft.ToString();
        if (hudAlt != null) hudAlt.text = altitude.ToString("F0");
        if (hudVs != null) hudVs.text = (vs >= 0f ? "+" : "") + vs.ToString("F1");
        if (hudFlags != null && hudFlags.Length >= 5)
        {
            HudFlag(hudFlags[0], braking);
            HudFlag(hudFlags[1], flapsDown);
            HudFlag(hudFlags[2], boosting);
            HudFlag(hudFlags[3], locked);
            HudFlag(hudFlags[4], voiceEnabled);
        }
    }
    void HudFlag(TextMeshPro t, bool on)
    {
        if (t == null) return;
        string nm = t.gameObject.name.Substring(1);
        string s = on ? "[ " + nm + " ]" : nm;
        if (t.text == s) return;
        t.text = s;
        t.color = on ? new Color(0.4f, 1f, 0.5f, 1f) : new Color(0.4f, 1f, 0.5f, 0.3f);
    }

    void UpdateCockpitDamage()
    {
        int lv = burnLevel;
        float t = Time.time;
        // 揺れ（コックピット全体を細かく震わせる）
        float amp = lv >= 3 ? 0.008f : (lv == 2 ? 0.004f : (lv == 1 ? 0.0015f : 0f));
        if (cockpitRoot != null)
            cockpitRoot.transform.localPosition = cockpitBasePos + (amp > 0f ? new Vector3(Random.Range(-amp, amp), Random.Range(-amp, amp), Random.Range(-amp, amp) * 0.5f) : Vector3.zero);
        // 赤い点滅
        if (damageTint != null && damageTintMat != null)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * (lv >= 3 ? 9f : 5f));
            float a = lv >= 3 ? 0.12f + 0.16f * pulse : (lv == 2 ? 0.04f + 0.09f * pulse : 0f);
            bool on = a > 0.001f;
            if (damageTint.enabled != on) damageTint.enabled = on;
            if (on) damageTintMat.SetColor("_Color", new Color(1f, 0.05f, 0.02f, a));
        }
        // 警報音
        if (alarmAudio != null)
        {
            if (lv >= 2 && !alarmAudio.isPlaying) alarmAudio.Play();
            else if (lv < 2 && alarmAudio.isPlaying) alarmAudio.Stop();
            alarmAudio.pitch = lv >= 3 ? 1.3f : 1f;
        }
        // 中の煙
        if (cabinSmoke != null)
        {
            var em = cabinSmoke.emission;
            em.rateOverTimeMultiplier = lv >= 3 ? 5f : 0f;
            if (lv >= 3 && !cabinSmoke.isPlaying) cabinSmoke.Play();
        }
        UpdatePanelStatic(lv, t);

        // 画面のちらつき（起動演出が終わっている時だけ）
        if (bootPanels != null && bootDone)
        {
            if (flickerPanel >= 0 && t >= flickerUntil)
            {
                if (bootPanels[flickerPanel] != null) bootPanels[flickerPanel].gameObject.SetActive(true);
                flickerPanel = -1;
            }
            if (flickerPanel < 0 && lv >= 2 && Random.value < Time.deltaTime * (lv >= 3 ? 4f : 1.2f))
            {
                flickerPanel = Random.Range(0, bootPanels.Length);
                flickerUntil = t + Random.Range(0.05f, 0.18f);
                if (bootPanels[flickerPanel] != null) bootPanels[flickerPanel].gameObject.SetActive(false);
            }
        }
    }

    // HP 25 未満: ときどきパネルが赤い砂嵐になる
    void UpdatePanelStatic(int lv, float t)
    {
        if (staticQuads == null) return;
        if (staticPanel >= 0 && (t >= staticUntil || lv < 3))
        {
            if (staticQuads[staticPanel] != null) staticQuads[staticPanel].gameObject.SetActive(false);
            staticPanel = -1;
        }
        if (staticPanel < 0 && lv >= 3 && bootDone && Random.value < Time.deltaTime * 0.8f)
        {
            int k = Random.Range(0, staticQuads.Length);
            if (staticQuads[k] != null)
            {
                staticPanel = k;
                staticUntil = t + Random.Range(0.25f, 0.9f);
                staticQuads[k].gameObject.SetActive(true);
            }
        }
        // 砂嵐を動かす（模様の位置を毎フレームでたらめにずらす）
        if (staticPanel >= 0 && staticMat != null)
            staticMat.SetTextureOffset("_MainTex", new Vector2(Random.value, Random.value));
    }

    void ClearCockpitDamage()
    {
        if (staticPanel >= 0 && staticQuads != null && staticQuads[staticPanel] != null) staticQuads[staticPanel].gameObject.SetActive(false);
        staticPanel = -1;
        if (cockpitRoot != null) cockpitRoot.transform.localPosition = cockpitBasePos;
        if (damageTint != null) damageTint.enabled = false;
        if (alarmAudio != null) alarmAudio.Stop();
        if (flickerPanel >= 0 && bootPanels != null && bootPanels[flickerPanel] != null) bootPanels[flickerPanel].gameObject.SetActive(true);
        flickerPanel = -1;
    }

    // 画面が「横線 → 上下に開く」ように点灯する（明滅あり）
    void ApplyBootPanels(float bt)
    {
        if (bootPanels == null || bootDelays == null) return;
        for (int i = 0; i < bootPanels.Length; i++)
        {
            if (bootPanels[i] == null) continue;
            float k = Mathf.Clamp01((bt - bootDelays[i]) / 0.35f);
            float sx = Mathf.Clamp01(k * 2f);
            float sy = k <= 0f ? 0f : Mathf.Clamp01(k * 2f - 1f) * 0.96f + 0.04f;
            bool flicker = k > 0f && k < 1f && Random.value < 0.25f;
            bootPanels[i].localScale = flicker ? Vector3.zero : new Vector3(sx, sy, 1f);
        }
    }

    // 周りの暗さ（1 = 真っ暗、0 = 普通に見える）
    void SetBlackout(float a)
    {
        if (blackout == null || blackoutMat == null) return;
        bool on = a > 0.001f;
        if (blackout.enabled != on) blackout.enabled = on;
        if (on) blackoutMat.SetColor("_Color", new Color(0f, 0f, 0f, a));
    }

    // 目の前に流れるシステムチェック（最初の 0.6 秒は真っ暗で何も出ない）
    string BootText(float bt)
    {
        bt -= 0.6f;
        if (bt < 0f) return "";
        if (bt > 2.2f)
        {
            bool blink = (bt * 6f) % 2f < 1.3f;
            return blink ? "<color=#7fff9f><size=120%>ALL SYSTEMS GO</size></color>" : "";
        }
        string t = "<color=#66ccff><size=80%>FLIGHT CONTROL SYSTEM  v2.6</size></color>\n";
        for (int i = 0; i < 5; i++)
        {
            float ti = 0.35f + i * 0.28f;
            if (bt < ti) break;
            string name = i == 0 ? "POWER" : (i == 1 ? "AVIONICS" : (i == 2 ? "ENGINE" : (i == 3 ? "WEAPONS" : "RADIO LINK")));
            bool ok = bt > ti + 0.15f;
            t += "<size=60%>" + name + "  ........  " + (ok ? "<color=#7fff9f>OK</color>" : "<color=#ffc040>CHECK</color>") + "</size>\n";
        }
        return t;
    }

    // 飛んでいるミサイルをレーダーに出す（速いので光を待たずに毎フレーム。点滅）
    void UpdateMissileBlips(Vector3 me, Vector3 f, Vector3 r)
    {
        if (radarMslDots == null) return;
        int used = 0;
        bool blinkOn = (Time.time * 6f) % 2f < 1.4f;
        if (radarPlanes != null)
        {
            for (int p = 0; p < radarPlanes.Length; p++)
            {
                controller pc = radarPlanes[p];
                if (pc == null) continue;
                int n = pc._MslCount();
                for (int i = 0; i < n && used < radarMslDots.Length; i++)
                {
                    if (!pc._MslActive(i)) continue;
                    if (PlaceMslBlip(used, pc._MslPos(i), pc == this ? 0 : 1, me, f, r, true)) used++;
                }
            }
        }
        if (radarShips != null)
        {
            for (int s = 0; s < radarShips.Length; s++)
            {
                WarShip ws = radarShips[s];
                if (ws == null) continue;
                int n = ws._RocketCount();
                for (int i = 0; i < n && used < radarMslDots.Length; i++)
                {
                    if (!ws._RocketActive(i)) continue;
                    if (PlaceMslBlip(used, ws._RocketPos(i), 2, me, f, r, blinkOn)) used++;
                }
            }
        }
        if (radarFighters != null)
        {
            for (int f2 = 0; f2 < radarFighters.Length && used < radarMslDots.Length; f2++)
            {
                EnemyDrone ef = radarFighters[f2];
                if (ef == null || !ef._MslActive()) continue;
                if (PlaceMslBlip(used, ef._MslPos(), 2, me, f, r, blinkOn)) used++;
            }
        }
        for (int i = used; i < radarMslDots.Length; i++)
            if (radarMslDots[i] != null && radarMslDots[i].gameObject.activeSelf) radarMslDots[i].gameObject.SetActive(false);
    }

    bool PlaceMslBlip(int k, Vector3 pos, int kind, Vector3 me, Vector3 f, Vector3 r, bool show)
    {
        Transform dot = radarMslDots[k];
        if (dot == null) return false;
        Vector3 d = pos - me;
        d.y = 0f;
        if (d.magnitude > radarRange) return false;
        dot.localPosition = new Vector3(Vector3.Dot(d, r) / radarRange * radarRadius, Vector3.Dot(d, f) / radarRange * radarRadius, dot.localPosition.z);
        if (radarMslMats != null && kind < radarMslMats.Length && radarMslRenderers != null && k < radarMslRenderers.Length && radarMslRenderers[k] != null)
            radarMslRenderers[k].sharedMaterial = radarMslMats[kind];
        if (dot.gameObject.activeSelf != show) dot.gameObject.SetActive(show);
        return true;
    }

    // レーダー: 光が回り、光が通った所の点だけ新しい位置に更新される（本物のレーダーと同じ）
    void UpdateRadar()
    {
        if (radarDots == null || radarTracks == null || car == null) return;
        float dt = Time.deltaTime;
        float s0 = sweepAng;
        float step = radarSweepSpeed * dt;
        sweepAng = (sweepAng + step) % 360f;
        if (radarSweep != null) radarSweep.localRotation = Quaternion.Euler(0f, 0f, -sweepAng);

        Vector3 me = car.transform.position;
        Vector3 f = axisReference != null ? axisReference.forward : -car.transform.right;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) f = Vector3.forward;
        f.Normalize();
        Vector3 r = new Vector3(f.z, 0f, -f.x);
        float hdg = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        if (radarRose != null) radarRose.localRotation = Quaternion.Euler(0f, 0f, hdg);   // 機首が常に上

        UpdateMissileBlips(me, f, r);

        // 島の地図: 自分の位置と機首の向きに合わせる
        Renderer mr = usingGunnerRadar ? radarMapG : radarMap;
        if (mr != null && radarMapRect.z > 1f)
        {
            Material mm;
            if (usingGunnerRadar) { if (mapMatG == null) mapMatG = mr.material; mm = mapMatG; }
            else { if (mapMatP == null) mapMatP = mr.material; mm = mapMatP; }
            float sz = radarMapRect.z;
            mm.SetVector("_Center", new Vector4((me.x - (radarMapRect.x - sz * 0.5f)) / sz, (me.z - (radarMapRect.y - sz * 0.5f)) / sz, 0f, 0f));
            mm.SetVector("_Dir", new Vector4(f.x, f.z, 0f, 0f));
            mm.SetFloat("_Scale", radarRange / sz);
        }

        int n = Mathf.Min(radarTracks.Length, radarDots.Length);
        if (radarPrev == null || radarPrev.Length != n) { radarPrev = new Vector3[n]; radarSeen = new bool[n]; }
        for (int i = 0; i < n; i++)
        {
            Transform dot = radarDots[i];
            if (dot == null) continue;
            Transform t = radarTracks[i];
            if (t == car.transform || !TrackAlive(i) || TrackType(i) == 5)   // 5 = レーダーに出さない物（基地の艦隊・止まっている警戒機）
            {
                if (dot.gameObject.activeSelf) dot.gameObject.SetActive(false);
                radarSeen[i] = false;
                continue;
            }
            Vector3 d = t.position - me;
            d.y = 0f;
            float x = Vector3.Dot(d, r) / radarRange * radarRadius;
            float y = Vector3.Dot(d, f) / radarRange * radarRadius;
            float bAng = Mathf.Atan2(x, y) * Mathf.Rad2Deg;
            float rel = (bAng - s0 + 720f) % 360f;
            if (rel > step + 0.001f) continue;   // まだ光が通っていない
            if (d.magnitude > radarRange)
            {
                if (dot.gameObject.activeSelf) dot.gameObject.SetActive(false);
                continue;
            }
            dot.localPosition = new Vector3(x, y, dot.localPosition.z);
            // 進んでいる向きにアイコンを回す
            Vector3 mv = t.position - radarPrev[i];
            mv.y = 0f;
            if (radarSeen[i] && mv.sqrMagnitude > 0.5f)
                dot.localRotation = Quaternion.Euler(0f, 0f, -Vector3.SignedAngle(f, mv, Vector3.up));
            radarPrev[i] = t.position;
            radarSeen[i] = true;
            int ty = TrackType(i);
            if (radarMats != null && ty < radarMats.Length && radarDotRenderers != null && i < radarDotRenderers.Length && radarDotRenderers[i] != null)
                radarDotRenderers[i].sharedMaterial = radarMats[ty];
            if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        }
    }

    string WeaponLine(int w, string name, string val)
    {
        if (weapon == w) return "<color=#ffc040>> " + name + "   " + val + "</color>";
        return "<color=#3a8a98>  " + name + "   " + val + "</color>";
    }

    void SetBar(Transform bar, float f, bool vertical)
    {
        if (bar == null) return;
        f = Mathf.Clamp(f, 0.001f, 1f);
        bar.localScale = vertical ? new Vector3(1f, f, 1f) : new Vector3(f, 1f, 1f);
    }

    string CompassName(float h)
    {
        int i = Mathf.RoundToInt(h / 45f) % 8;
        if (i == 0) return "N";
        if (i == 1) return "NE";
        if (i == 2) return "E";
        if (i == 3) return "SE";
        if (i == 4) return "S";
        if (i == 5) return "SW";
        if (i == 6) return "W";
        return "NW";
    }

    void UpdateHud()
    {
        UpdateCockpit();
        if (hudText == null) return;
        string t = "SPD " + (airspeed * 3.6f).ToString("F0") + " km/h\n"
                 + "ALT " + altitude.ToString("F0") + " m\n"
                 + "VS  " + (rb != null ? rb.velocity.y : 0f).ToString("F1") + " m/s\n"
                 + "AoA " + aoaDeg.ToString("F1") + "°\n"
                 + "THR " + (throttle * 100f).ToString("F0") + "%"
                 + (flapsDown ? "  FLAP" : "")
                 + (braking && grounded ? "  BRK" : "")
                 + "\nGUN " + ammo.ToString() + (ammo <= 0 ? " <color=red>EMPTY</color>" : "");
        if (stallAmount > 0.05f) t += "\n<color=red>STALL</color>";
        else if (aoaDeg > stallAngle - flapStallReduction * flapPos - 3f && !grounded) t += "\n<color=yellow>STALL WARNING</color>";
        if (catapult != null)
        {
            if (catLaunching) t += "\n<color=orange>LAUNCH!</color>";
            else if (catHold > 0f) t += "\n<color=orange>LAUNCH IN " + Mathf.Max(catapult.holdTime - catHold, 0f).ToString("F1") + "</color>";
            else t += "\n<color=#66ccff>CATAPULT READY\nFULL THROTTLE TO LAUNCH</color>";
        }
        hudText.text = t;
    }

    // エンジン音: 誰かが乗っている間は全員の画面で鳴る
    // 操縦している本人はスロットルで、ほかの人は機体の速さで音の高さ・大きさを決める
    void UpdateEngineSound()
    {
        if (engineAudio == null) return;
        bool running = pilotPlayer != null && Utilities.IsValid(pilotPlayer) && !crashed;
        if (running && !engineAudio.isPlaying) engineAudio.Play();
        else if (!running && engineAudio.isPlaying) engineAudio.Stop();
        if (!running) return;
        float spd = rb != null ? rb.velocity.magnitude : 0f;
        float thr = isPiloting ? throttle : Mathf.Clamp01(spd / 55f);
        float target = 0.72f + thr * 0.55f + Mathf.Clamp01(spd / 60f) * 0.12f;
        engineAudio.pitch = Mathf.Lerp(engineAudio.pitch, target, Time.deltaTime * 3f);
        float vm = volume != null ? volume._Engine() : 1f;
        engineAudio.volume = Mathf.Lerp(engineAudio.volume, Mathf.Clamp01(engineVolume * vm * (0.35f + thr * 0.65f)), Time.deltaTime * 3f);
    }

    void UpdateAudio()
    {
        bool inAir = !grounded && !floating && airspeed > 3f;
        bool stalled = inAir && stallAmount > 0.05f;
        bool warn = inAir && !stalled && aoaDeg > stallAngle - flapStallReduction * flapPos - 3f;
        // 後ろの席の人にも鳴らすため、状態が変わった時だけ全員に知らせる
        int ss = stalled ? 2 : (warn ? 1 : 0);
        if (ss != sentStallState && isPiloting)
        {
            sentStallState = ss;
            if (ss == 0) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStall0));
            else if (ss == 1) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStall1));
            else SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStall2));
        }
        if (stallWarningAudio != null)
        {
            if (warn && !stallWarningAudio.isPlaying) stallWarningAudio.Play();
            else if (!warn && stallWarningAudio.isPlaying) stallWarningAudio.Stop();
        }
        if (stallAudio != null)
        {
            if (stalled && !stallAudio.isPlaying) stallAudio.Play();
            else if (!stalled && stallAudio.isPlaying) stallAudio.Stop();
        }
    }

    // ---------------- 乗り降り ----------------
    public VRCPlayerApi _GetPilot() { return pilotPlayer; }
    public bool _IsFiring() { return localFiring || remoteFiring; }
    public bool _IsBoosting() { return boosting; }

    void SetExteriorVisible(bool v)
    {
        if (exteriorRenderers == null) return;
        for (int i = 0; i < exteriorRenderers.Length; i++) if (exteriorRenderers[i] != null) exteriorRenderers[i].enabled = v;
    }

    // 敵機の機銃が当たった（敵機を動かしている人の画面から、この機体の持ち主＝操縦者に届く）
    public void NetEnemyHit()
    {
        if (!isPiloting) return;
        Damage(5f);
        TakingFireVoice(0);   // 敵ドローンの機銃
    }

    // 座席に付いている全部の Udon（座るためのものを含む）のクリックを止める / 戻す（自分の画面だけ）
    void SetSeatInteractive(bool on)
    {
        Component[] ubs = GetComponents(typeof(UdonBehaviour));
        for (int i = 0; i < ubs.Length; i++)
        {
            UdonBehaviour ub = (UdonBehaviour)ubs[i];
            if (ub != null) ub.DisableInteractive = !on;
        }
    }
    void SetBoardClickable(bool on)
    {
        if (boarder == null) return;
        Collider[] cs = boarder.GetComponents<Collider>();
        for (int i = 0; i < cs.Length; i++)
            if (cs[i] != null && cs[i].isTrigger) cs[i].enabled = on;   // 機体の物理の当たり判定（トリガーでない物）は触らない
    }

    public void _SetRadioCount(int n) { radioCount = n; }

    public override void OnStationEntered(VRCPlayerApi player)
    {
        DisableInteractive = true;
        pilotPlayer = player;
        if (boarder != null) boarder._Refresh();   // 2 席とも埋まったら誰も乗れない
        if (player == null || !player.isLocal) return;
        // 座っている間は、座席の「クリックで座る」を止める（射撃の左クリックで座り直さないように）
        SetSeatInteractive(false);
        // 「クリックで乗る」の当たり判定も自分の画面では消す（視線が当たって機体全体が水色に光るのを防ぐ）
        SetBoardClickable(false);
        if (isPiloting) return;
        // 降りた直後（0.3 秒以内）にまた座った = クリックによる座り直し。状態を戻して起動演出はしない
        if (Time.time - lastExitTime < 0.3f)
        {
            isPiloting = true;
            throttle = savedThrottle;
            flapsDown = savedFlaps;
            if (gunSight != null) gunSight.SetActive(true);
            if (cockpitRoot != null) cockpitRoot.SetActive(true);
            SetExteriorVisible(false);
            if (gunAudio != null) gunAudio.spatialBlend = 0f;
            return;
        }

        Networking.SetOwner(player, car);
        Networking.SetOwner(player, gameObject);
        if (gunAudio != null) gunAudio.spatialBlend = 0f;   // 操縦している本人には銃の音を耳元で（機首から遠くても必ず聞こえる）
        if (voiceRelay != null) Networking.SetOwner(player, voiceRelay.gameObject);
        isVR = player.IsUserInVR();
        isPiloting = true;
        remoteFiring = false;
        fireInput = false;
        if (joinLog != null) joinLog._UseBlessing();   // 折り鶴の加護: 乗った時に 1 回だけ +10 点
        if (gunSight != null) gunSight.SetActive(true);
        if (cockpitRoot != null) cockpitRoot.SetActive(true);
        SetExteriorVisible(false);
        // 起動演出を始める
        bootStart = Time.time;
        bootDone = false;
        ApplyBootPanels(0f);
        SetBlackout(0f);   // 乗った時に真っ暗にしない
        if (cockpitAudio != null && cockpitAudio.clip != null) cockpitAudio.PlayDelayed(0.6f);
        inPitch = 0f; inRoll = 0f; inYaw = 0f; inThrottleCmd = 0f;
        vrStickHeld = false; vrLeverHeld = false; vrLsX = 0f; vrLsY = 0f; vrLsUpPrev = false; vrLsLeftPrev = false;
        throttle = 0f;
        flapsDown = false;
        hasTakenOff = false;
        armedAt = -1f;
        ClearHostile();
        hudGMax = 1f; hudGNow = 1f; hudGInit = false;
    }

    // 「偵察任務」を聞いた 1 人目が、戦闘の前に墜落・降りた（前は次の人にまた流していた）
    void ReconAbort()
    {
        if (!iDidRecon) return;
        iDidRecon = false;
        // 何度も流れないよう、降りても「もう聞いた」のまま（モードを切り替えた時だけ、また流れる）
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        DisableInteractive = false;
        remoteFiring = false;
        if (pilotPlayer == player) pilotPlayer = null;
        if (boarder != null) boarder._Refresh();
        radioCount = 0;
        if (player == null || !player.isLocal) return;
        ReconAbort();
        lastExitTime = Time.time;
        savedThrottle = throttle;
        savedFlaps = flapsDown;
        SetSeatInteractive(true);
        SetBoardClickable(true);

        if (gunnerMode) SetGunnerMode(false);
        ClearCockpitDamage();
        if (gunAudio != null) gunAudio.spatialBlend = 1f;
        isPiloting = false;
        fireInput = false;
        useHeld = false;
        trigPrev = false;
        lockCand = -1;
        locked = false;
        vQCount = 0;
        vInit = false;
        vAnnounced = null;
        if (voiceAudio != null) voiceAudio.Stop();
        if (lockBox != null) lockBox.gameObject.SetActive(false);
        if (gunSight != null) gunSight.SetActive(false);
        if (cockpitRoot != null) cockpitRoot.SetActive(false);
        SetExteriorVisible(true);
        SetBlackout(0f);
        throttle = 0f;   // 降りたらアイドル（機体はそのまま物理で滑空・落下する）
        inPitch = 0f; inRoll = 0f; inYaw = 0f;
        if (hudText != null) hudText.text = "";
        if (engineAudio != null) engineAudio.volume = 0.3f;
        if (stallWarningAudio != null && stallWarningAudio.isPlaying) stallWarningAudio.Stop();
        if (stallAudio != null && stallAudio.isPlaying) stallAudio.Stop();
    }

    // ---------------- 入力イベント ----------------
    // VR では左スティックは機能のボタン（操縦は右グリップの操縦桿）。デスクトップは WASD で操縦
    public override void InputMoveHorizontal(float value, UdonInputEventArgs args) { if (isVR) vrLsX = value; else inRoll = value; }
    public override void InputMoveVertical(float value, UdonInputEventArgs args) { if (isVR) vrLsY = value; else inPitch = value; }
    public override void InputLookHorizontal(float value, UdonInputEventArgs args) { vrLookX = value; }
    public override void InputLookVertical(float value, UdonInputEventArgs args) { vrLookY = value; }
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        // VR: 左手のトリガー = ミサイル、右手のトリガー = 機銃。デスクトップの左クリック = 機銃
        // VR: 左手のトリガー = 武器の切り替え、右手のトリガー = 撃つ。デスクトップの左クリック = 撃つ（Enter と同じ）
        if (isVR && args.handType == HandType.LEFT) cycleInput = value;
        else useHeld = value;
    }
    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        // VR: 右グリップ = 操縦桿、左グリップ = スロットルレバー（SaccFlight 風）
        if (isVR && args.handType == HandType.LEFT) vrLeftGrip = value;
        else vrGrip = value;
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        // 入ってきた人の準備ができた頃に、機体のオーナーが脚の状態を送る
        if (player != null && !player.isLocal) SendCustomEventDelayedSeconds(nameof(_SendGearState), 3f);
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        remoteFiring = false;
        if (pilotPlayer == player) pilotPlayer = null;
    }

    // 座席は Disable Station Exit をオンにしている（オフだとWASDで降りてしまう）ので、ジャンプで自分で降りる
    public override void InputJump(bool value, UdonInputEventArgs args)
    {
        if (!value || !isPiloting || station == null) return;
        station.ExitStation(Networking.LocalPlayer);
    }
}
