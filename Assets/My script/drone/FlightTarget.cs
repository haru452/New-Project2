using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

// =============================================================
//  機銃の標的（気球など）
//  ・自分か子のコライダーに弾が当たると耐久値が減る
//  ・body を指定した場合は body ごと消す。指定しない場合は renderers / colliders を消す
//    （潜水艦・建物・戦闘機など、元からある物に付ける時）
//  ・0 になると爆発して消え、respawnTime 秒後に復活する
//  ・船・飛行機（kind 0〜5）は、すぐ消さずに沈む（当たり判定だけ先に消す。動きは WarShip / EnemyDrone）。沈み終わったら消す
//  ・耐久値と状態はワールドの全員で同期（管理はオブジェクトのオーナー）
// =============================================================
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class FlightTarget : UdonSharpBehaviour
{
    public int maxHp = 5;
    [Tooltip("得点の種類: 0 = 飛行機, 1 = 船, 2 = 空母, 3 = 貨物船, 4 = 飛ぶ警戒機, 5 = 空母の戦闘機, -1 = 得点なし")]
    public int kind = -1;
    public float respawnTime = 20f;
    [Tooltip("ミサイル 1 発で減る耐久値（機銃は 1 発 1）")]
    public int missileDamage = 15;
    [Tooltip("魚雷 1 発で減る耐久値（ふつうの船 40 を 2 発）")]
    public int torpedoDamage = 20;
    public GameObject body;                 // 見た目と当たり判定（壊れたら非表示）
    public Renderer[] renderers;            // body が無い時に消す見た目
    public Collider[] colliders;            // body が無い時に消す当たり判定
    [Tooltip("これが壊れるまでは復活しない（貨物船の護衛の戦艦: 貨物船が撃破されるまで復活しない）")]
    public FlightTarget respawnAfter;
    [HideInInspector] public bool holdRespawn;   // true の間は復活しない（空母: 誰にも見えない所へ移ってから復活する）
    [Tooltip("オン: 壊れたら復活しない（基地の建物）")]
    public bool noRespawn;
    [Tooltip("壊れた後に出す物（がれき・炎）")]
    public GameObject wreck;
    public ParticleSystem explosionFx;
    [Tooltip("船・飛行機（kind 0〜5）: 壊れてから沈み終わって消えるまでの秒数（復活より前に終わるよう短くする）")]
    public float sinkTime = 40f;
    public AudioSource explosionAudio;

    [UdonSynced] private int hp = -1;
    [UdonSynced] private bool destroyed;

    private bool shownDestroyed;
    private bool firstApply = true;
    private bool respawnPending;
    private float sinkStart = -1f, sinkUntil = -1f;   // 沈んでいる間（自分の画面の時刻）

    bool Sinks() { return kind >= 0 && kind <= 5; }
    // 壊れて沈んでいる途中（見た目はまだ出ている）
    public bool _Sinking() { return destroyed && sinkUntil > 0f && Time.time < sinkUntil; }
    // 沈み始めてから何秒たったか（沈んでいない時は -1）
    public float _SinkElapsed() { return _Sinking() ? Time.time - sinkStart : -1f; }
    public float _SinkDuration() { return sinkUntil - sinkStart; }
    public void _SinkDone()
    {
        if (sinkUntil < 0f || Time.time < sinkUntil - 0.05f) return;
        sinkUntil = -1f;
        Apply();
    }

    void Start()
    {
        // 最初は送らない（hp = -1 は満タン扱い）。数百個が一斉に同期して、入った直後が重くなるのを防ぐ
        Apply();
    }

    // 撃った人の画面から呼ばれる
    public void _Hit()
    {
        if (destroyed) return;
        AddMyDamage(1);
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetHit));
    }

    // 自分が削った HP（自分の画面だけ）。空母の 200 点を、削った割合で分けるのに使う
    private int myDamage;
    void AddMyDamage(int d)
    {
        int left = hp < 0 ? maxHp : hp;   // 残りより多くは数えない
        myDamage += Mathf.Max(0, Mathf.Min(d, left));
    }
    public int _MyDamage() { return myDamage; }
    public void _ClearMyDamage() { myDamage = 0; }

    public bool _IsDestroyed() { return destroyed || hiddenLocal; }
    public bool _IsSunk() { return destroyed; }   // 本当に壊された（まだ出てきていないだけの時は false）
    // まだ出てきていない（空母は 10 分後に出る）: 見えない・当たらない・レーダーにも出ない（全員の画面で同じ時刻に切り替わる）
    private bool hiddenLocal;
    public void _SetHidden(bool h)
    {
        if (h == hiddenLocal) return;
        hiddenLocal = h;
        Apply();
    }

    // 見た目だけ消す（当たり判定・中身はそのまま。空母が遠くて見えない時、甲板の戦闘機も一緒に消す）
    private bool culledLocal;
    public bool _Culled() { return culledLocal; }
    public void _SetCulled(bool c)
    {
        if (c == culledLocal) return;
        culledLocal = c;
        Apply();
    }

    // 残りの HP の割合（0〜1。燃える演出に使う）
    public float _HpRatio()
    {
        if (destroyed) return 0f;
        if (hp < 0 || maxHp <= 0) return 1f;
        return hp / (float)maxHp;
    }

    // ミサイルが当たった時（撃った人の画面から呼ばれる）
    public void _HitMissile()
    {
        if (destroyed) return;
        AddMyDamage(missileDamage);
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetHitBig));
    }

    // 魚雷が当たった時（撃った人の画面から呼ばれる）
    public void _HitTorpedo()
    {
        if (destroyed) return;
        AddMyDamage(torpedoDamage);
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetHitTorpedo));
    }

    public void NetHitTorpedo()
    {
        ApplyDamage(torpedoDamage);
    }

    public void NetHitBig()
    {
        ApplyDamage(missileDamage);
    }

    public void NetHit()
    {
        ApplyDamage(1);
    }

    void ApplyDamage(int d)
    {
        if (!Networking.IsOwner(gameObject) || destroyed) return;
        if (hp < 0) hp = maxHp;
        hp -= d;
        if (hp <= 0)
        {
            destroyed = true;
            ScheduleRespawn();
        }
        RequestSerialization();
        Apply();
    }

    void ScheduleRespawn()
    {
        if (respawnPending || noRespawn) return;
        respawnPending = true;
        SendCustomEventDelayedSeconds(nameof(_Respawn), respawnTime);
    }

    public void _Respawn()
    {
        respawnPending = false;
        if (!Networking.IsOwner(gameObject)) return;
        if (holdRespawn || (respawnAfter != null && !respawnAfter._IsDestroyed()))
        {
            // まだ待つ（5 秒ごとに確かめる）
            respawnPending = true;
            SendCustomEventDelayedSeconds(nameof(_Respawn), 5f);
            return;
        }
        hp = maxHp;
        destroyed = false;
        RequestSerialization();
        Apply();
    }

    // 復活しない物（空母）を、モードを切り替えた時に元に戻す（オーナーに送る）
    public void NetRevive()
    {
        if (!Networking.IsOwner(gameObject) || !destroyed) return;
        hp = maxHp;
        destroyed = false;
        RequestSerialization();
        Apply();
    }

    public override void OnDeserialization()
    {
        Apply();
    }

    // オーナーが抜けて引き継いだ時、壊れたままなら復活の予約をし直す
    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (Networking.IsOwner(gameObject) && destroyed) ScheduleRespawn();
    }

    void Apply()
    {
        if (!destroyed && (hp < 0 || hp >= maxHp)) myDamage = 0;   // 満タンに戻った（モードの切り替えなど）: 数え直し
        if (!destroyed) { sinkStart = -1f; sinkUntil = -1f; }
        // 今壊れた船・飛行機: 沈み始める（入ってきた時にもう壊れていた物は、沈めずに消しておく）
        if (destroyed && !shownDestroyed && !firstApply && Sinks() && !hiddenLocal)
        {
            float dur = sinkTime;
            if (!noRespawn) dur = Mathf.Min(dur, respawnTime - 2f);
            if (dur > 0.5f)
            {
                sinkStart = Time.time; sinkUntil = Time.time + dur;
                SendCustomEventDelayedSeconds(nameof(_SinkDone), dur);
            }
        }
        bool show = (!destroyed || _Sinking()) && !hiddenLocal;
        if (body != null) body.SetActive(show);
        if (wreck != null) wreck.SetActive(destroyed && !hiddenLocal);
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = show && !culledLocal;
        }
        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = !destroyed && !hiddenLocal;
        }
        if (destroyed && !shownDestroyed && !firstApply)
        {
            if (explosionFx != null) explosionFx.Play();
            if (explosionAudio != null) explosionAudio.Play();
        }
        shownDestroyed = destroyed;
        firstApply = false;
    }
}
