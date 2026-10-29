using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;
using VRC.SDK3.Components;
using VRC.Udon;
using TMPro;
using UdonSharp;

// 飛行機の一括セットアップ
//  ・controller（飛行スクリプト）を全部有効化
//  ・機体に VRC Object Sync を追加
//  ・機体コライダーに NoFriction を設定
//  ・伊402の甲板にカタパルトを設置
//  ・座席の前に HUD（速度・高度など）の文字を置く
//  ・機体に機銃（曳光弾・発射音・照準）を取り付け
//  ・飛行ルート上に標的の気球を12個配置
//  ・操縦席のまわりにコックピット画面（本人にだけ表示）
//  ・高度 430〜600m に雲の層（宇宙エレベーターの上の方を隠す）
//  ・海の画像の板を隠し、波のある海に置き換え（当たり判定は元の板のまま）
//  ・パイロット同士の無線（PilotRadio）
//  ・低い所・遠くからは宇宙ステーションを雲で隠す（StationCloak・ローカル）
//  ・海面に速く突っ込むと爆発（墜落）
//  ・動いている潜水艦・島の建物・戦闘機を撃つと爆発
//  ・描画距離を 12km に広げ、遠景の霞（フォグ）で遠くの物が自然に見えてくるように
//  ・無誘導ミサイル（機体ごとに 4 発ぶんの見た目・煙・炎・音、海の大きな水柱）
//  ・置いてある帆船（Colonial Ship）を標的にして、近づくと大砲を撃ってくるように（WarShip）
//  ・R35 を 4 機に増やして格納庫のプールに入れる（ドローンは非表示）・部品の動き（R35Parts）
//  ・座席を Immobilize For Vehicle / 移動キーで降りない設定に（降りるのはジャンプ）
// メニュー「Tools/飛行機セットアップを実行」から実行できる。
// プロジェクト直下の Backup_flight/run_setup.flag があれば、Unity起動・再コンパイル時に一度だけ自動実行する。
[InitializeOnLoad]
public static partial class FlightSetup
{
    const string FlagPath = "Backup_flight/run_setup.flag";
    const string ReportPath = "Backup_flight/setup_report.txt";
    const string MaterialPath = "Assets/My script/drone/NoFriction.physicMaterial";

    static FlightSetup()
    {
        EditorApplication.delayCall += TryAutoRun;
        // 再生中だった場合は、再生を止めたときに実行する
        EditorApplication.playModeStateChanged += st =>
        {
            if (st == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryAutoRun;
        };
    }

    static void TryAutoRun()
    {
        if (!File.Exists(FlagPath)) return;
        if (EditorApplication.isPlaying) return;   // 再生中は何もしない（止めたら上のイベントで実行）
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutoRun;   // 落ち着いてから再試行
            return;
        }
        File.Delete(FlagPath);
        Run();
    }

    [MenuItem("Tools/飛行機セットアップを実行")]
    public static void Run()
    {
        var log = new StringBuilder();
        log.AppendLine("FlightSetup " + System.DateTime.Now);
        try
        {
            if (EditorApplication.isPlaying)
            {
                log.AppendLine("ERROR: 再生中のため実行できません。再生を止めてから Tools メニューで実行してください。");
                return;
            }

            // 入室記録のプログラム（controller から使うので最初に用意する）
            bool newGunner = !EnsureProgram(GunnerScriptPath, GunnerAssetPath, log);
            newGunner |= !EnsureProgram("Assets/My script/drone/Leaderboard.cs", "Assets/My script/drone/Leaderboard.asset", log);
            newGunner |= !EnsureProgram("Assets/My script/drone/AllTimeBoard.cs", "Assets/My script/drone/AllTimeBoard.asset", log);
            newGunner |= !EnsureProgram("Assets/My script/drone/GameMode.cs", "Assets/My script/drone/GameMode.asset", log);
            newGunner |= !EnsureProgram("Assets/My script/drone/VoiceRelay.cs", "Assets/My script/drone/VoiceRelay.asset", log);
            newGunner |= !EnsureProgram("Assets/My script/drone/VolumeSettings.cs", "Assets/My script/drone/VolumeSettings.asset", log);
            newGunner |= !EnsureProgram("Assets/My script/drone/VolumeButton.cs", "Assets/My script/drone/VolumeButton.asset", log);
            if ((!EnsureProgram(JoinScriptPath, JoinAssetPath, log) || newGunner) && !joinRetried)
            {
                joinRetried = true;
                File.WriteAllText(FlagPath, "joinlog");
                log.AppendLine("入室記録: プログラム作成済み。もう一度自動実行します");
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            var mat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(MaterialPath);
            log.AppendLine("NoFriction: " + (mat != null ? "OK" : "見つからない"));

            // 0) 新しく置いた機体（R35 など。椅子付き）を飛べるようにする
            SetupNewAircraft(log);
            // 0.5) R35 を増やして格納庫のプールに入れる（ドローンの代わり）
            SetupR35Pool(log);
            // 0.55) URP のマテリアル（ピンク）を VRChat 用に変換（新しく入れた素材のため）
            URPToBuiltin.Run();
            // 0.56) R35 → Omega FighterG のお試し（プールの 1 機だけ）
            SetupOmegaTrial(log);
            // 0.6) R35 の当たり判定・高さを正しい大きさに（非表示の機体でも測れる方法で）
            FixR35(log);

            var ctrls = Object.FindObjectsOfType<controller>(true);
            log.AppendLine("controller の数: " + ctrls.Length);

            foreach (var c in ctrls)
            {
                string path = GetPath(c.transform);
                // 1) 有効化（C#側と Udon 側の両方）
                if (!c.enabled)
                {
                    Undo.RecordObject(c, "Flight setup");
                    c.enabled = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                    log.AppendLine("  有効化: " + path);
                }
                UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
                if (ub != null && !ub.enabled)
                {
                    Undo.RecordObject(ub, "Flight setup");
                    ub.enabled = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
                    log.AppendLine("  Udon有効化: " + path);
                }

                // 2) 座席
                var st = c.GetComponent<VRCStation>();
                if (st != null)
                {
                    Undo.RecordObject(st, "Flight setup");
                    st.PlayerMobility = VRC.SDKBase.VRCStation.Mobility.ImmobilizeForVehicle;
                    st.disableStationExit = true;   // オフだとWASDで降りてしまう。降りるのはジャンプ（controller側で処理）
                    PrefabUtility.RecordPrefabInstancePropertyModifications(st);
                }
                else log.AppendLine("  WARNING: 座席(VRCStation)なし: " + path);

                // 2.5) HUD（操縦席の目の前に置く文字。操縦中だけ表示される）
                if (c.hudText == null && c.cockpitRoot == null)   // コックピット画面があれば古いHUDは作らない
                {
                    Transform seat = c.transform;
                    Transform hip = (st != null && st.stationEnterPlayerLocation != null) ? st.stationEnterPlayerLocation : seat;
                    var go = new GameObject("HUD");
                    Undo.RegisterCreatedObjectUndo(go, "Flight setup");
                    go.transform.SetParent(seat, false);
                    // 座った時の目線の少し下・前方 約0.7m（ワールド単位で配置）
                    go.transform.position = hip.position + seat.up * 0.55f + seat.forward * 0.7f;
                    go.transform.rotation = seat.rotation;
                    Vector3 ls = seat.lossyScale;
                    go.transform.localScale = new Vector3(1f / Mathf.Max(ls.x, 1e-4f), 1f / Mathf.Max(ls.y, 1e-4f), 1f / Mathf.Max(ls.z, 1e-4f));

                    var tmp = go.AddComponent<TextMeshPro>();
                    tmp.text = "";
                    tmp.fontSize = 0.32f;
                    tmp.color = new Color(0.3f, 1f, 0.45f);
                    tmp.alignment = TextAlignmentOptions.TopLeft;
                    tmp.enableWordWrapping = false;
                    tmp.richText = true;
                    tmp.rectTransform.sizeDelta = new Vector2(0.5f, 0.35f);

                    Undo.RecordObject(c, "Flight setup");
                    c.hudText = tmp;
                    UdonSharpEditorUtility.CopyProxyToUdon(c);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                    if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
                    log.AppendLine("  HUD作成: " + path);
                }

                // 3) 機体
                GameObject car = c.car;
                if (car == null) { log.AppendLine("  WARNING: car 未設定: " + path); continue; }
                if (car.GetComponent<Rigidbody>() == null) { log.AppendLine("  WARNING: car に Rigidbody なし: " + GetPath(car.transform)); continue; }

                if (car.GetComponent<VRCObjectSync>() == null)
                {
                    Undo.AddComponent<VRCObjectSync>(car);
                    log.AppendLine("  ObjectSync追加: " + GetPath(car.transform));
                }

                if (mat != null)
                {
                    foreach (var col in car.GetComponentsInChildren<Collider>(true))
                    {
                        if (col.isTrigger || col.sharedMaterial == mat) continue;
                        Undo.RecordObject(col, "Flight setup");
                        col.sharedMaterial = mat;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(col);
                    }
                }
            }

            // 4) カタパルト
            if (Object.FindObjectsOfType<Catapult>(true).Length == 0)
            {
                if (!EnsureCatapultProgram(log) && !catapultRetried)
                {
                    catapultRetried = true;
                    // プログラム作成直後はUdonSharpの読み込みを待ってから設置する
                    File.WriteAllText(FlagPath, "catapult");
                    log.AppendLine("カタパルト: プログラム作成済み。もう一度自動実行して設置します");
                    EditorApplication.delayCall += TryAutoRun;
                }
                else if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(CatapultAssetPath) != null) CreateCatapult(log);
                else log.AppendLine("ERROR: Catapult.asset を作れませんでした");
            }
            else
            {
                foreach (var cat in Object.FindObjectsOfType<Catapult>(true)) PlaceCatapult(cat, log);
            }

            // 5) 機銃・標的（FlightTarget のプログラムができてから）
            if (!EnsureProgram(TargetScriptPath, TargetAssetPath, log) && !targetRetried)
            {
                targetRetried = true;
                File.WriteAllText(FlagPath, "gun");
                log.AppendLine("標的: プログラム作成済み。もう一度自動実行して機銃と標的を設置します");
                EditorApplication.delayCall += TryAutoRun;
            }
            else if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(TargetAssetPath) != null)
            {
                // スクリプトに追加した項目を反映させるため、必ず最新の状態にコンパイルしておく
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
                if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
                    log.AppendLine("ERROR: UdonSharp のコンパイルエラーがあります（Console を確認）");
                else SetupGunsAndTargets(log);
            }
            else log.AppendLine("ERROR: FlightTarget.asset を作れませんでした");

            // 5.5) 描画距離と遠景の霞
            SetupView(log);

            // 6) 雲の層（毎回作り直し）
            CreateClouds(log);

            // 7) 波のある海（OceanFollow のプログラムができてから）
            if (!EnsureProgram(OceanScriptPath, OceanAssetPath, log) && !oceanRetried)
            {
                oceanRetried = true;
                File.WriteAllText(FlagPath, "ocean");
                log.AppendLine("海: プログラム作成済み。もう一度自動実行して設置します");
                EditorApplication.delayCall += TryAutoRun;
            }
            else if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(OceanAssetPath) != null) CreateOcean(log);
            else log.AppendLine("ERROR: OceanFollow.asset を作れませんでした");

            // 8) パイロット同士の無線
            if (!EnsureProgram(RadioScriptPath, RadioAssetPath, log) && !radioRetried)
            {
                radioRetried = true;
                File.WriteAllText(FlagPath, "radio");
                log.AppendLine("無線: プログラム作成済み。もう一度自動実行して設置します");
                EditorApplication.delayCall += TryAutoRun;
            }
            else if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(RadioAssetPath) != null) CreateRadio(log);
            else log.AppendLine("ERROR: PilotRadio.asset を作れませんでした");

            // 8.2) 機体本体をクリックして乗れるように
            if (!EnsureProgram(BoardScriptPath, BoardAssetPath, log) && !boardRetried)
            {
                boardRetried = true;
                File.WriteAllText(FlagPath, "board");
                log.AppendLine("乗る: プログラム作成済み。もう一度自動実行して設置します");
                EditorApplication.delayCall += TryAutoRun;
            }
            else if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(BoardAssetPath) != null) SetupBoarding(log);

            // 8.25) 2 人目の席（ガンナー）
            SetupGunner(log);

            // 8.3) R35 の部品の動き
            if (!EnsureProgram(PartsScriptPath, PartsAssetPath, log) && !partsRetried)
            {
                partsRetried = true;
                File.WriteAllText(FlagPath, "parts");
                log.AppendLine("R35の部品: プログラム作成済み。もう一度自動実行して設置します");
                EditorApplication.delayCall += TryAutoRun;
            }
            else if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(PartsAssetPath) != null) SetupR35Parts(log);

            // 8.5) 大砲を撃ってくる船
            if (!EnsureProgram(ShipScriptPath, ShipAssetPath, log) && !shipRetried)
            {
                shipRetried = true;
                File.WriteAllText(FlagPath, "ship");
                log.AppendLine("船: プログラム作成済み。もう一度自動実行して設置します");
                EditorApplication.delayCall += TryAutoRun;
            }
            else if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ShipAssetPath) != null) SetupWarShips(log);
            else log.AppendLine("ERROR: WarShip.asset を作れませんでした");

            // 8.6) 敵機（古いドローン）
            if (!EnsureProgram(EnemyScriptPath, EnemyAssetPath, log) && !enemyRetried)
            {
                enemyRetried = true;
                File.WriteAllText(FlagPath, "enemy");
                log.AppendLine("敵機: プログラム作成済み。もう一度自動実行して設置します");
                EditorApplication.delayCall += TryAutoRun;
            }
            else if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(EnemyAssetPath) != null) SetupEnemies(log);

            // 8.7) レーダーに出す物
            SetupBaseBuildings(log);   // 基地の建物を壊せるように（レーダーより先）
            SetupTownBuildings(log);   // 人が住む島・アストラル島の建物
            SetupRadarTracks(log);
            SetupBaseDefense(log);   // 基地: アインの警告・対空砲
            SetupSpeeds(log);        // 速さ: プレイヤー 約 600km/h・敵 約 500km/h
            SetupAstralAirport(log); // アストラル空港: 管制室の無線・着陸で補給
            SetupSightseeing(log);   // 遊覧モード: 歓迎の無線・編隊・裏切り・フレア

            // 8.8) 入室・退室の記録、プレイヤーは機体をすり抜ける
            SetupJoinLog(log);
            SetupBoards(log);
            SetupVolumePanel(log);   // main room の音の大きさの設定
            SetupWalkthrough(log);

            // 9) 宇宙エレベーターは使わないので、残っている物を全部消す
            RemoveElevator(log);
            // 無線の声を小さく
            foreach (var pr in Object.FindObjectsOfType<PilotRadio>(true))
            {
                Undo.RecordObject(pr, "Flight setup");
                pr.radioGain = 7f;
                UdonSharpEditorUtility.CopyProxyToUdon(pr);
            }

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i));
            bool saved = EditorSceneManager.SaveOpenScenes();
            log.AppendLine("シーン保存: " + (saved ? "OK" : "失敗"));
            log.AppendLine("DONE");
        }
        catch (System.Exception e)
        {
            log.AppendLine("EXCEPTION: " + e);
        }
        finally
        {
            Directory.CreateDirectory("Backup_flight");
            File.WriteAllText(ReportPath, log.ToString(), Encoding.UTF8);
            Debug.Log("[FlightSetup]\n" + log);
        }
    }

    static bool catapultRetried;
    static bool targetRetried;
    const string TargetScriptPath = "Assets/My script/drone/FlightTarget.cs";
    const string TargetAssetPath = "Assets/My script/drone/FlightTarget.asset";
    const string AudioDir = "Assets/My script/drone/Audio";
    const string FxDir = "Assets/My script/drone/FX";

    static bool EnsureProgram(string scriptPath, string assetPath, StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) return true;
        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (ms == null) { log.AppendLine("ERROR: " + scriptPath + " が見つからない"); return true; }
        var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        prog.sourceCsScript = ms;
        AssetDatabase.CreateAsset(prog, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        log.AppendLine(Path.GetFileName(assetPath) + " 作成・コンパイル");
        return false;
    }

    // ---------------- 機銃・標的 ----------------
    static void SetupGunsAndTargets(StringBuilder log)
    {
        Texture2D tex = GetSoftTexture();
        Material fxMat = GetFxMaterial(tex);
        AudioClip gunClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/gun.wav");
        AudioClip boomClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        if (gunClip == null || boomClip == null) log.AppendLine("WARNING: 効果音が見つからない");

        ParticleSystem impact = EnsureImpact(fxMat, log);
        ParticleSystem water = EnsureWaterSplash(tex, log);
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (SetupGun(c, fxMat, gunClip, impact, log)) n++;
            Undo.RecordObject(c, "Flight setup");
            c.waterSplashFx = water;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UdonBehaviour ubw = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (ubw != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ubw);
        }
        log.AppendLine("機銃: " + n + " 機に設定");

        if (Object.FindObjectsOfType<FlightTarget>(true).Length == 0) CreateTargets(fxMat, boomClip, log);
        else log.AppendLine("標的: 設置済み");

        int k = 0;
        int removed = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car))
            {
                // 人が乗らない機体（しまったドローンなど）のコックピットは消す（シーンを軽く・小さく）
                foreach (Transform holder in new[] { c.transform, c.car != null ? c.car.transform : null })
                {
                    if (holder == null) continue;
                    Transform oc = holder.Find("Cockpit");
                    if (oc != null) { Undo.DestroyObjectImmediate(oc.gameObject); removed++; }
                }
                Undo.RecordObject(c, "Flight setup");
                c.cockpitRoot = null;
                c.radarDots = new Transform[0];
                c.radarDotRenderers = new Renderer[0];
                c.radarMslDots = new Transform[0];
                c.radarMslRenderers = new Renderer[0];
                UdonSharpEditorUtility.CopyProxyToUdon(c);
                continue;
            }
            if (SetupCockpit(c, log)) k++;
        }
        if (removed > 0) log.AppendLine("コックピット: 乗らない機体から " + removed + " 個を削除（軽量化）");
        log.AppendLine("コックピット画面: " + k + " 機に設置");

        SetupCrash(fxMat, boomClip, log);
        SetupDamageFire(fxMat, log);
        SetupMissiles(fxMat, tex, log);
        SetupObjectTargets(fxMat, boomClip, log);
    }

    // ---------------- 墜落 ----------------
    // ダメージを受けると燃える炎と煙（R35 のエンジンのあたり）
    static void SetupDamageFire(Material fxMat, StringBuilder log)
    {
        Material smokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
        if (smokeMat == null) smokeMat = fxMat;
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Transform car = c.car.transform;
            Transform old;
            int guard = 0;
            while ((old = car.Find("DamageFx")) != null && guard++ < 10) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject("DamageFx").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Flight setup");
            root.SetParent(car, false);

            // 燃える場所: 左右のエンジン（無ければ機体の上）
            var spots = new System.Collections.Generic.List<Vector3>();
            foreach (var t in c.car.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Dropship_R35-Engine") || !t.gameObject.activeInHierarchy) continue;
                Bounds eb;
                if (PartBounds(t, out eb)) spots.Add(eb.center);
            }
            if (spots.Count == 0)
            {
                Bounds bd;
                Transform measure = IsOmega(c.car) ? car.Find("OmegaBody") : car;
                if (MeshBounds(measure, "", out bd)) spots.Add(bd.center + Vector3.up * bd.extents.y * 0.5f);
                else spots.Add(car.position);
            }

            var fires = new System.Collections.Generic.List<ParticleSystem>();
            var smokes = new System.Collections.Generic.List<ParticleSystem>();
            int k = 0;
            foreach (var p in spots)
            {
                k++;
                var fire = MakePS("Fire" + k, root, fxMat);
                fire.transform.position = p;
                var fm = fire.main;
                fm.loop = true;
                fm.duration = 1f;
                fm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
                fm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                fm.startSize = 2f;   // 大きさは controller が燃え方に合わせて変える
                fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.9f));
                fm.gravityModifier = -0.4f;
                fm.maxParticles = 300;
                var fsh = fire.shape;
                fsh.enabled = true;
                fsh.shapeType = ParticleSystemShapeType.Sphere;
                fsh.radius = 0.8f;
                var fsol = fire.sizeOverLifetime;
                fsol.enabled = true;
                fsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
                SetFade(fire, new Color(1f, 0.7f, 0.3f), new Color(0.6f, 0.1f, 0.05f));
                fires.Add(fire);

                var smoke = MakePS("Smoke" + k, root, smokeMat);
                smoke.transform.position = p + Vector3.up * 0.5f;
                var sm = smoke.main;
                sm.loop = true;
                sm.duration = 1f;
                sm.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
                sm.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
                sm.startSize = 4.5f;
                sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.15f, 0.15f, 0.7f), new Color(0.35f, 0.35f, 0.35f, 0.6f));
                sm.gravityModifier = -0.08f;
                sm.maxParticles = 400;
                var ssh = smoke.shape;
                ssh.enabled = true;
                ssh.shapeType = ParticleSystemShapeType.Sphere;
                ssh.radius = 0.6f;
                var ssol = smoke.sizeOverLifetime;
                ssol.enabled = true;
                ssol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.5f));
                SetFade(smoke, new Color(0.3f, 0.3f, 0.3f), new Color(0.6f, 0.6f, 0.6f));
                smokes.Add(smoke);
            }
            Undo.RecordObject(c, "Flight setup");
            c.damageFire = fires.ToArray();
            c.damageSmoke = smokes.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        log.AppendLine("燃える演出: " + n + " 機に設置");
    }

}
