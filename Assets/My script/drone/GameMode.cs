using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

// =============================================================
//  ゲームモードの切り替えボタン（全員共通の設定）
//  ・普通  : 対空機銃なし
//  ・ハード: 船が対空機銃を撃ってくる。得点 2 倍
//  ・遊覧  : 【予定】敵は撃ってこない。歓迎の編隊飛行つき（下の TODO 参照）
//  ・押した人が持ち主になって次のモードへ切り替え、全員に同期する
//
//  ※ 他のスクリプトは今まで通り _IsHard() だけ見ればよい（ハードの時だけ true）。
//    遊覧モードを判定したいスクリプトは _IsSightseeing() を使う。
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class GameMode : UdonSharpBehaviour
{
    public const int MODE_NORMAL = 0;
    public const int MODE_HARD = 1;
    public const int MODE_SIGHTSEEING = 2;
    private const int MODE_COUNT = 3;

    public TextMeshPro label;
    public Renderer lamp;                // ボタンの色（ハード = 赤、普通 = 緑、遊覧 = 青など）
    public Material hardMat;
    public Material normalMat;
    public Material sightseeingMat;      // 遊覧モード用。未設定なら normalMat で代用
    public controller[] planes;          // プレイヤーの機体（誰かが乗っている間はモードを切り替えられない）
    private float msgUntil, nextFlyCheck;
    private bool someoneFlying;

    // 現在のモード（0 = 普通、1 = ハード、2 = 遊覧）
    [UdonSynced] private int mode;

    // ワールド全体の戦闘の状態（誰か 1 人でも警告を受けて戦闘になったら、全員の次の出撃から警告なし）
    [UdonSynced] private bool warStarted;
    [UdonSynced] private bool ownLost;     // 戦闘中に味方（ガウリア）の機体が 1 機でも撃墜された
    public bool _WarStarted() { return warStarted; }
    public bool _OwnLost() { return ownLost; }
    public void _ReportWar() { if (!warStarted) SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetWar)); }
    public void _ReportOwnLoss() { if (!ownLost) SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetOwnLoss)); }
    public void NetWar() { if (warStarted) return; warStarted = true; warStart = Networking.GetServerTimeInSeconds(); RequestSerialization(); }
    // 戦闘が始まった時刻（空母の艦隊: 1 分後にアナウンス、2 分後に出てくる）
    [UdonSynced] private double warStart;
    public float _WarElapsed()
    {
        if (!warStarted || warStart <= 0.0) return -1f;
        return (float)(Networking.GetServerTimeInSeconds() - warStart);
    }
    // 博物館の中にいる間は、無線（偵察任務・艦隊のアナウンスなど）を聞こえなくする
    public MuseumAudio museum;
    bool InMuseum() { return museum != null && museum._IsInside(); }
    public void NetOwnLoss() { if (ownLost) return; ownLost = true; RequestSerialization(); }
    // アストラル空港: 着陸しようとした機体を追っていた戦闘機は、しばらく空港のまわりで攻撃しない（全員で同じ）
    public Transform astralAirport;
    public float astralSafeRadius = 5000f;
    [UdonSynced] private double astralCeaseUntil;
    public void _AstralCeasefire() { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetAstralCease)); }
    public void NetAstralCease() { astralCeaseUntil = Networking.GetServerTimeInSeconds() + 120.0; RequestSerialization(); }
    public bool _AstralSafe(Vector3 p)
    {
        if (astralAirport == null || Networking.GetServerTimeInSeconds() > astralCeaseUntil) return false;
        Vector3 d = p - astralAirport.position; d.y = 0f;
        return d.magnitude < astralSafeRadius;
    }
    // 「これは偵察任務である…」をもう誰かが聞いたか（ワールドで最初に飛び立った 1 人だけに流す）
    [UdonSynced] private bool reconDone;
    public bool _ReconDone() { return reconDone; }
    public void _ReportRecon() { if (!reconDone) SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetReconDone)); }
    public void _ResetRecon() { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetReconReset)); }
    public void NetReconDone() { if (reconDone) return; reconDone = true; RequestSerialization(); }
    public void NetReconReset() { if (!reconDone || warStarted) return; reconDone = false; RequestSerialization(); }
    // 「偵察任務」の無線をワールドの全員に流す（1 人目が飛び立った時）
    public AudioSource radio;            // 全員の画面で鳴らす無線（2D）
    public AudioClip[] reconClips;
    public void _BroadcastRecon(int i)
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, i == 0 ? nameof(NetRecon0) : nameof(NetRecon1));
    }
    public void NetRecon0() { PlayRecon(0); }
    public void NetRecon1() { PlayRecon(1); }
    // 「偵察任務」が流れている間（全員の画面）: 敵はプレイヤーに近づかない・探さない・撃たない。撃たれても攻撃してこない
    private float quietUntil = -1f;
    public bool _Quiet() { return Time.time < quietUntil; }
    void PlayRecon(int i)
    {
        if (radio == null || reconClips == null || i >= reconClips.Length || reconClips[i] == null) return;
        quietUntil = Time.time + reconClips[i].length + 1f;
        radio.Stop();
        radio.clip = reconClips[i];
        radio.Play();
    }

    // ワールド（インスタンス）が始まった時刻（サーバー時刻）。空母が 10 分後に出てくる時などに使う
    [UdonSynced] private double worldStart;
    void Start()
    {
        if (Networking.IsOwner(gameObject) && worldStart <= 0.0)
        {
            worldStart = Networking.GetServerTimeInSeconds();
            RequestSerialization();
        }
        Show();
    }
    // 空母（AMA 連合艦隊）が出てきた時の、ガウリア帝国の司令部の無線（全員に。遊覧モードでは流さない）
    public float fleetAnnounceAt = 575f;   // 空母が出てくる 25 秒前
    public AudioClip[] fleetClips;         // 「AMA の連合艦隊がこちらに向かっている。すべて撃破せよ…」
    private bool fleetAnnounced;
    private double announcedFor;
    void UpdateFleetAnnounce()
    {
        if (warStart != announcedFor) { announcedFor = warStart; fleetAnnounced = false; }   // モードを切り替えて、また戦闘になったらまた流す
        if (fleetAnnounced || fleetClips == null || fleetClips.Length == 0 || radio == null) return;
        float e = _WarElapsed();
        if (e < 0f) return;
        if (e < fleetAnnounceAt) return;
        if (e > fleetAnnounceAt + 60f || mode == MODE_SIGHTSEEING) { fleetAnnounced = true; return; }   // 後から入った人には流さない
        if (radio.isPlaying) return;   // ほかの無線（偵察任務など）が終わってから
        fleetAnnounced = true;
        int i = (int)(warStart % fleetClips.Length);   // 全員同じ方
        if (i < 0) i = 0;
        if (fleetClips[i] == null) return;
        radio.clip = fleetClips[i];
        radio.Play();
    }

    public double _WorldStart() { return worldStart; }   // モードを切り替えると変わる（空母の艦隊の出てくる場所を決め直す）

    // ワールドが始まってからの秒数（まだ分からない時は -1）
    public float _Elapsed()
    {
        if (worldStart <= 0.0) return -1f;
        return (float)(Networking.GetServerTimeInSeconds() - worldStart);
    }

    // ハードモードの時だけ true（既存のスクリプトとの互換用。変更しない）
    public bool _IsHard() { return mode == MODE_HARD; }

    // 遊覧モードで、まだ平和な時だけ true（裏切りが起きたら false = 普通の戦闘と同じ）
    public bool _IsSightseeing() { return mode == MODE_SIGHTSEEING && !betrayed; }
    // 遊覧モードかどうか（裏切りの後も true）
    public bool _SightMode() { return mode == MODE_SIGHTSEEING; }

    // ---- 遊覧モードの隠し要素: 裏切り ----
    // 遊覧モードで誰かが何かを壊したら、得点 3 倍・追尾ミサイルの戦闘機と戦艦が全員を攻撃する（モードを切り替えるまで）
    [UdonSynced] private bool betrayed;
    public bool _Betrayed() { return betrayed; }
    public void _Betray() { if (!betrayed && mode == MODE_SIGHTSEEING) SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetBetray)); }
    public void NetBetray() { if (betrayed || mode != MODE_SIGHTSEEING) return; betrayed = true; RequestSerialization(); }

    // ---- 遊覧モード: 歓迎の編隊（エルシディアの戦闘機が、飛び立った人のまわりを飛ぶ）----
    public EnemyDrone[] welcomeFlight;     // 歓迎の編隊の戦闘機（最大 5 機）
    public float escortSeconds = 80f;
    [UdonSynced] private int escortPlane = -1;
    [UdonSynced] private double escortStart;
    public int _EscortPlane()
    {
        if (escortPlane < 0 || !_IsSightseeing()) return -1;
        if (Networking.GetServerTimeInSeconds() - escortStart > escortSeconds) return -1;
        return escortPlane;
    }
    public float _EscortTime() { return (float)(Networking.GetServerTimeInSeconds() - escortStart); }
    private int reqEscort = -1;
    public void _RequestEscort(int planeIdx)
    {
        if (_EscortPlane() >= 0 || planeIdx < 0 || planeIdx > 7) return;   // ほかの人を案内中
        reqEscort = planeIdx;
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, planeIdx == 0 ? nameof(NetEscort0) : planeIdx == 1 ? nameof(NetEscort1) : planeIdx == 2 ? nameof(NetEscort2) : planeIdx == 3 ? nameof(NetEscort3) : planeIdx == 4 ? nameof(NetEscort4) : planeIdx == 5 ? nameof(NetEscort5) : planeIdx == 6 ? nameof(NetEscort6) : nameof(NetEscort7));
    }
    public void NetEscort0() { StartEscort(0); }
    public void NetEscort1() { StartEscort(1); }
    public void NetEscort2() { StartEscort(2); }
    public void NetEscort3() { StartEscort(3); }
    public void NetEscort4() { StartEscort(4); }
    public void NetEscort5() { StartEscort(5); }
    public void NetEscort6() { StartEscort(6); }
    public void NetEscort7() { StartEscort(7); }
    void StartEscort(int i)
    {
        if (_EscortPlane() >= 0) return;
        escortPlane = i; escortStart = Networking.GetServerTimeInSeconds();
        RequestSerialization();
    }

    // 現在のモード番号
    public int _GetMode() { return mode; }

    // 誰かが機体に乗っている（飛んでいる）か
    bool SomeoneFlying()
    {
        if (planes == null) return false;
        for (int i = 0; i < planes.Length; i++)
        {
            if (planes[i] == null) continue;
            VRCPlayerApi p = planes[i]._GetPilot();
            if (p != null && Utilities.IsValid(p)) return true;
        }
        return false;
    }

    // 1 秒ごとに確かめて、ボタンに近づいた時の文字を変える
    void Update()
    {
        UpdateFleetAnnounce();
        if (radio != null) { bool m = InMuseum(); if (radio.mute != m) radio.mute = m; }
        if (Time.time >= nextFlyCheck)
        {
            nextFlyCheck = Time.time + 1f;
            bool f = SomeoneFlying();
            if (f != someoneFlying) { someoneFlying = f; Show(); }
        }
        if (msgUntil > 0f && Time.time >= msgUntil) { msgUntil = 0f; Show(); }
    }

    public override void Interact()
    {
        if (SomeoneFlying())
        {
            // 飛んでいる人がいる間は切り替えない（その旨を表示）
            if (label != null) label.text = "<color=#ffc040><size=120%>CANNOT CHANGE MODE</size></color>\n<size=70%>Someone is flying right now.\nWait until everyone has landed or left their aircraft.</size>";
            msgUntil = Time.time + 4f;
            return;
        }
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        mode = (mode + 1) % MODE_COUNT;   // 普通 → ハード → 遊覧 → 普通 …
        warStarted = false;               // モードを切り替えたら、戦闘の状態は最初から（また警告から）
        betrayed = false;                 // 遊覧モードの裏切りも元に戻す
        escortPlane = -1;
        warStart = 0.0;                   // 空母の艦隊も、また戦闘が始まってから
        ownLost = false;
        reconDone = false;
        worldStart = Networking.GetServerTimeInSeconds();   // 空母が出てくるまでの 10 分も最初から
        RequestSerialization();
        Show();
    }

    public override void OnDeserialization() { Show(); }

    void Show()
    {
        string text;
        string interact;
        Material mat;

        if (mode == MODE_HARD)
        {
            text = "<color=#ff4040><size=140%>HARD MODE</size></color>\n<size=70%>Ships use anti-aircraft guns.  Score x2\nPress to switch to SIGHTSEEING</size>";
            interact = "遊覧モードにする";
            mat = hardMat;
        }
        else if (mode == MODE_SIGHTSEEING)
        {
            text = "<color=#5fc8ff><size=140%>SIGHTSEEING MODE</size></color>\n<size=70%>Enemies do not shoot.  Enjoy the view.\nPress to switch to NORMAL</size>";
            interact = "普通モードにする";
            mat = sightseeingMat != null ? sightseeingMat : normalMat;
        }
        else
        {
            text = "<color=#7fff9f><size=140%>NORMAL MODE</size></color>\n<size=70%>No anti-aircraft guns.\nPress to switch to HARD (score x2)</size>";
            interact = "ハードモードにする（得点2倍）";
            mat = normalMat;
        }

        if (someoneFlying)
        {
            interact = "飛んでいる人がいるので切り替えできません";
            text += "\n<size=55%><color=#ffc040>Locked: someone is flying</color></size>";
        }
        if (label != null && msgUntil <= 0f) label.text = text;
        if (lamp != null) lamp.sharedMaterial = mat;
        InteractionText = interact;
    }
}

// =============================================================
//  TODO（遊覧モードで付けたい機能。今は選べるだけで、動きは普通モードと同じ）
//
//  1. 敵ドローンは撃ってこない
//     → EnemyDrone 側で gameMode._IsSightseeing() の時は fire / ミサイル発射を止める
//       （追いかけずに、普通に巡回するだけにしてもよい）
//
//  2. 敵機が「ようこそ我らの国へ」のような歓迎メッセージを出して編隊飛行する.
//銃を撃つ分には何も起きないが、もし何かしら破壊してしまったら、ポイントはｘ３で追撃付きのミサイルを持った飛行機及び戦艦に持たユーザー全員を攻撃してください。
//それに伴って、フレアを実装したいです。
//