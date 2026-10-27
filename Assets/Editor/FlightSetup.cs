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
public static class FlightSetup
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

    static void SetupCrash(Material fxMat, AudioClip boom, StringBuilder log)
    {
        GameObject old = GameObject.Find("CrashExplosion");
        if (old != null) Undo.DestroyObjectImmediate(old);

        var ex = MakePS("CrashExplosion", null, fxMat);
        ConfigureExplosion(ex, 3f, 60);
        var splash = MakePS("Splash", ex.transform, fxMat);
        var sm = splash.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(12f, 30f);
        sm.startSize = new ParticleSystem.MinMaxCurve(2f, 6f);
        sm.startColor = new Color(0.75f, 0.85f, 0.95f, 0.6f);
        sm.gravityModifier = 1.2f;
        sm.maxParticles = 120;
        var se = splash.emission;
        se.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
        var ss = splash.shape;
        ss.enabled = true;
        ss.shapeType = ParticleSystemShapeType.Cone;
        ss.angle = 25f;
        ss.radius = 3f;
        splash.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 上向きに水しぶき
        SetFade(splash, new Color(0.9f, 0.95f, 1f), new Color(0.6f, 0.7f, 0.8f));

        var au = ex.gameObject.AddComponent<AudioSource>();
        au.playOnAwake = false;
        au.clip = boom;
        au.spatialBlend = 1f;
        au.minDistance = 30f;
        au.maxDistance = 2500f;
        au.volume = 1f;

        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        Transform respawn = (putOut != null && putOut.reportPoint != null) ? putOut.reportPoint.transform : null;
        var pool = putOut != null ? putOut.dronePool : null;

        int pooled = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            Undo.RecordObject(c, "Flight setup");
            c.crashFx = ex;
            c.crashAudio = au;
            c.oceanMaterial = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/Ocean.mat");   // 波に乗るため

            // エンジン音（機体に付けて、乗っている間ループ再生。全員に聞こえる）
            if (c.car != null)
            {
                AudioClip eng = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/engine.wav");
                Transform et = c.car.transform.Find("EngineAudio");
                AudioSource ea;
                if (et == null)
                {
                    var eg = new GameObject("EngineAudio");
                    Undo.RegisterCreatedObjectUndo(eg, "Flight setup");
                    eg.transform.SetParent(c.car.transform, false);
                    ea = eg.AddComponent<AudioSource>();
                }
                else ea = et.GetComponent<AudioSource>();
                ea.clip = eng;
                ea.loop = true;
                ea.playOnAwake = false;
                ea.spatialBlend = 1f;
                ea.minDistance = 6f;
                ea.maxDistance = 700f;
                ea.rolloffMode = AudioRolloffMode.Logarithmic;
                ea.dopplerLevel = 0.5f;   // 近くを通り過ぎると音の高さが変わる
                ea.volume = 0.12f;
                c.engineAudio = ea;
                if (Mathf.Approximately(c.engineVolume, 0f)) c.engineVolume = 0.35f;
            }
            if (Mathf.Approximately(c.cmElevator, 0.4f)) c.cmElevator = 0.24f;   // 昇降舵を実機並みに
            // 速度が出すぎないように（最高速 約200km/h）
            if (Mathf.Approximately(c.parasiteDrag, 0.027f)) c.parasiteDrag = 0.035f;
            if (Mathf.Approximately(c.propMaxSpeed, 110f)) c.propMaxSpeed = 90f;
            if (Mathf.Approximately(c.minEngineFactor, 0.35f)) c.minEngineFactor = 0.2f;
            c.respawnPoint = respawn;
            c.homePool = null;
            if (pool != null && pool.Pool != null && c.car != null)
            {
                foreach (var g in pool.Pool) if (g == c.car) { c.homePool = pool; pooled++; break; }
            }
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        }
        log.AppendLine("墜落: 爆発エフェクト作成 / 格納庫に戻す機体 " + pooled + " 機 / 戻す場所 " + (respawn != null ? respawn.position.ToString("F0") : "なし"));
    }

    // ---------------- ミサイル ----------------
    static void SetupMissiles(Material fxMat, Texture2D tex, StringBuilder log)
    {
        GameObject old = GameObject.Find("MissilePool");
        if (old != null) Undo.DestroyObjectImmediate(old);
        GameObject oldS = GameObject.Find("MissileSplash");
        if (oldS != null) Undo.DestroyObjectImmediate(oldS);

        // 海に落ちた時の大きな水柱（機銃の水柱より高く太い）
        Shader alphaSh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        Material waterMat = LoadOrCreateMat("WaterFx", alphaSh);
        var splash = MakePS("MissileSplash", null, waterMat);
        splash.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var sm = splash.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(18f, 40f);
        sm.startSize = new ParticleSystem.MinMaxCurve(2f, 6f);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.92f, 1f, 0.85f), new Color(1f, 1f, 1f, 0.95f));
        sm.gravityModifier = 1f;
        sm.maxParticles = 800;
        var ss = splash.shape;
        ss.enabled = true;
        ss.shapeType = ParticleSystemShapeType.Cone;
        ss.angle = 10f;
        ss.radius = 2f;
        var sol = splash.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.2f));
        SetFade(splash, Color.white, new Color(0.8f, 0.88f, 0.95f));

        // 煙の尾のマテリアル
        Material smoke = LoadOrCreateMat("MissileSmoke", alphaSh);
        smoke.SetTexture("_MainTex", tex);
        smoke.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorUtility.SetDirty(smoke);
        // 本体のマテリアル
        Material body = LoadOrCreateMat("Missile", Shader.Find("Standard"));
        body.SetColor("_Color", new Color(0.55f, 0.58f, 0.6f));
        body.SetFloat("_Glossiness", 0.5f);
        EditorUtility.SetDirty(body);
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/missile.wav");

        var pool = new GameObject("MissilePool");
        Undo.RegisterCreatedObjectUndo(pool, "Flight setup");
        int count = 0;
        int planeNo = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car))
            {
                // 人が乗らない機体（敵のドローン）にはミサイルを作らない（シーンを軽く）
                Undo.RecordObject(c, "Flight setup");
                c.missiles = new Transform[0];
                UdonSharpEditorUtility.CopyProxyToUdon(c);
                PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                continue;
            }
            planeNo++;
            var arr = new Transform[24];   // 無誘導 16 + ロックオン 8 が同時に飛べる数
            for (int k = 0; k < arr.Length; k++)
            {
                var m = new GameObject("Missile_" + planeNo + "_" + (k + 1));
                m.transform.SetParent(pool.transform, false);

                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(cyl.GetComponent<Collider>());
                cyl.name = "Body";
                cyl.transform.SetParent(m.transform, false);
                cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 円柱の軸を前(+Z)に
                cyl.transform.localScale = new Vector3(0.22f, 0.9f, 0.22f);     // 長さ 1.8m
                var cr = cyl.GetComponent<MeshRenderer>();
                cr.sharedMaterial = body;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // 噴射の炎
                var flame = MakePS("Flame", m.transform, fxMat);
                flame.transform.localPosition = new Vector3(0f, 0f, -1f);
                var fm = flame.main;
                fm.loop = true;
                fm.playOnAwake = true;
                fm.simulationSpace = ParticleSystemSimulationSpace.Local;
                fm.startLifetime = 0.12f;
                fm.startSpeed = 0f;
                fm.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.15f), new Color(1f, 0.9f, 0.5f));
                fm.maxParticles = 30;
                var fe = flame.emission;
                fe.rateOverTime = 80f;
                var fs = flame.shape;
                fs.enabled = false;

                // 白い煙の尾
                var tr = m.AddComponent<TrailRenderer>();
                tr.sharedMaterial = smoke;
                tr.time = 2.5f;
                tr.minVertexDistance = 1.5f;
                tr.widthCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 3f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f) },
                          new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;

                // 噴射の音（ミサイルと一緒に動く）
                var au = m.AddComponent<AudioSource>();
                au.clip = clip;
                au.playOnAwake = false;
                au.spatialBlend = 1f;
                au.minDistance = 10f;
                au.maxDistance = 800f;
                au.dopplerLevel = 0.6f;

                m.SetActive(false);
                arr[k] = m.transform;
                count++;
            }
            Undo.RecordObject(c, "Flight setup");
            c.missiles = arr;
            c.maxMissiles = 16;
            c.maxGuided = 8;
            c.missileSplashFx = splash;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        }
        log.AppendLine("ミサイル: " + count + " 発ぶんの見た目を作成（" + planeNo + " 機 × 24）");
    }

    // 爆発の見た目（s = 大きさの倍率）
    static void ConfigureExplosion(ParticleSystem ex, float s, int count)
    {
        var main = ex.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f * Mathf.Pow(s, 0.7f), 22f * Mathf.Pow(s, 0.7f));
        main.startSize = new ParticleSystem.MinMaxCurve(3f * s, 8f * s);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.1f), new Color(1f, 0.85f, 0.3f));
        main.gravityModifier = -0.1f;
        main.maxParticles = count + 20;
        var em = ex.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ex.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 2f * s;
        SetFade(ex, new Color(1f, 0.9f, 0.5f), new Color(0.6f, 0.15f, 0.05f));
        var sol = ex.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
    }

    // ---------------- 撃てる物（潜水艦・建物・戦闘機） ----------------
    static void SetupObjectTargets(Material fxMat, AudioClip boom, StringBuilder log)
    {
        int n = 0;
        var sb = new StringBuilder();

        // 動いている潜水艦
        var mover = Object.FindObjectOfType<Control_submarine>(true);
        if (mover != null && mover.submarine != null && AddObjectTarget(mover.submarine, 40, 60f, fxMat, boom)) { n++; sb.Append(" 潜水艦"); }

        // 島の建物・戦闘機（元のモデル・プレハブの種類で探す）
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            GameObject go = t.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(path)) continue;
            int hp = 0;
            float respawn = 30f;
            if (path.StartsWith("Assets/Houses_Pack/Models/")) hp = 10;
            else if (path == "Assets/My assets/ie.fbx") hp = 6;
            else if (path == "Assets/My assets/torii.fbx") hp = 5;
            else if (path.StartsWith("Assets/Raptor3D/") && path.EndsWith(".prefab")) hp = 8;
            if (hp == 0) continue;
            if (AddObjectTarget(go, hp, respawn, fxMat, boom)) { n++; sb.Append(" " + go.name); }
        }
        log.AppendLine("撃てる物: " + n + " 個追加:" + sb);
    }

    static bool AddObjectTarget(GameObject go, int hp, float respawn, Material fxMat, AudioClip boom)
    {
        if (go.GetComponent<FlightTarget>() != null) return false;

        var rends = new System.Collections.Generic.List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            rends.Add(r);
        }
        if (rends.Count == 0) return false;
        Bounds bd = rends[0].bounds;
        foreach (var r in rends) bd.Encapsulate(r.bounds);

        var cols = new System.Collections.Generic.List<Collider>();
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) cols.Add(c);
        if (cols.Count == 0)
        {
            // 当たり判定が無い物には箱を付ける（弾が当たるように）
            var bc = Undo.AddComponent<BoxCollider>(go);
            Transform tr = go.transform;
            bc.center = tr.InverseTransformPoint(bd.center);
            Vector3 sz = tr.InverseTransformVector(bd.size);
            bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
            cols.Add(bc);
        }

        float s = Mathf.Clamp(bd.extents.magnitude / 8f, 1f, 20f);
        var ex = MakePS("Explosion", go.transform, fxMat);
        ex.transform.position = bd.center;
        ConfigureExplosion(ex, s, 60);
        var au = ex.gameObject.AddComponent<AudioSource>();
        au.playOnAwake = false;
        au.clip = boom;
        au.spatialBlend = 1f;
        au.minDistance = 20f * Mathf.Sqrt(s);
        au.maxDistance = 2500f;
        au.volume = 1f;

        var t = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(go);
        t.renderers = rends.ToArray();
        t.colliders = cols.ToArray();
        t.explosionFx = ex;
        t.explosionAudio = au;
        t.maxHp = hp;
        t.respawnTime = respawn;
        UdonSharpEditorUtility.CopyProxyToUdon(t);
        return true;
    }

    // ---------------- R35 をプールに（ドローンの代わり） ----------------
    const int R35PoolSize = 4;

    static bool IsR35(GameObject go)
    {
        if (go == null) return false;
        return go.transform.Find("Dropship_R35-Engine1") != null || go.name.StartsWith("Dropship_R35") || go.name.StartsWith("R35_") || IsOmega(go);
    }

    // R35 の代わりの Omega FighterG（OmegaBody という見た目を持つ機体）
    static bool IsOmega(GameObject go)
    {
        return go != null && go.transform.Find("OmegaBody") != null;
    }

    const string OmegaFbx = "Assets/Omega_fighterG/Meshes/fighter_black.FBX";
    const float OmegaLength = 14f;   // 機体の長さ（m）。R35 と同じくらい

    // プールの 1 機（R35_4）を Omega FighterG の見た目にする。
    // 見た目の向き・大きさ・椅子の位置は「最初の 1 回だけ」自動で置く（あとで手で直した物は上書きしない）
    static void SetupOmegaTrial(StringBuilder log)
    {
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        if (putOut == null || putOut.dronePool == null || putOut.dronePool.Pool == null) return;
        GameObject target = null;
        foreach (var g in putOut.dronePool.Pool) if (g != null && IsOmega(g)) { target = g; break; }
        bool fresh = target == null;
        if (fresh)
        {
            foreach (var g in putOut.dronePool.Pool) if (g != null && g.name == "R35_4") { target = g; break; }
            if (target == null)
            {
                var pl = putOut.dronePool.Pool;
                if (pl.Length > 0) target = pl[pl.Length - 1];
            }
        }
        if (target == null) { log.AppendLine("Omega: プールに機体が無い"); return; }
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(OmegaFbx);
        if (fbx == null) { log.AppendLine("Omega: " + OmegaFbx + " が見つからない"); return; }
        Transform car = target.transform;
        Undo.RecordObject(target, "Flight setup");
        target.name = "Omega_1";

        // R35 の見た目はしまう（消さない）。R35 の部品の動きも止める
        var rootMr = target.GetComponent<MeshRenderer>();
        if (rootMr != null) { Undo.RecordObject(rootMr, "Flight setup"); rootMr.enabled = false; }
        foreach (Transform ch in car)
            if (ch.name.StartsWith("Dropship")) { Undo.RecordObject(ch.gameObject, "Flight setup"); ch.gameObject.SetActive(false); }
        var parts = target.GetComponent<R35Parts>();
        if (parts != null)
        {
            UdonBehaviour pub = UdonSharpEditorUtility.GetBackingUdonBehaviour(parts);
            Undo.DestroyObjectImmediate(parts);
            if (pub != null) Undo.DestroyObjectImmediate(pub);
        }

        controller c = target.GetComponentInChildren<controller>(true);
        Transform axis = car.Find("FlightAxis");
        Vector3 fwd = axis != null ? axis.forward : car.forward;
        Vector3 up = axis != null ? axis.up : car.up;
        var bc = target.GetComponent<BoxCollider>();
        Vector3 center = bc != null ? car.TransformPoint(bc.center) : car.position;

        if (fresh)
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(fbx, car);
            Undo.RegisterCreatedObjectUndo(body, "Flight setup");
            body.name = "OmegaBody";
            body.transform.localScale = Vector3.one;
            body.transform.rotation = Quaternion.identity;
            // 一番長い水平の向きを機首の向きにそろえる
            Bounds lb;
            MeshBounds(body.transform, "", out lb);
            Vector3 sz = lb.size;
            bool alongX = sz.x > sz.z;
            float len = Mathf.Max(sz.x, sz.z);
            body.transform.rotation = Quaternion.LookRotation(fwd, up) * (alongX ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity);
            float sc = len > 0.01f ? OmegaLength / len : 1f;
            body.transform.localScale = Vector3.one * sc;   // 測った長さは親の大きさ込みなので、そのまま掛ける
            Bounds wb;
            MeshBounds(body.transform, "", out wb);
            body.transform.position += center - wb.center;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // 椅子: 操縦席（Cockipt）の真ん中あたりに仮置き（あとで手で直してください）
            Transform cp = null;
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) if (t.name.ToLower().Contains("cockipt") || t.name.ToLower().Contains("cockpit")) { cp = t; break; }
            Bounds cb;
            if (c != null)
            {
                Undo.RecordObject(c.transform, "Flight setup");
                Vector3 seatPos = (cp != null && PartBounds(cp, out cb)) ? cb.center - up * 0.35f : wb.center + fwd * (wb.extents.magnitude * 0.25f) + up * 0.3f;
                c.transform.position = seatPos;
            }
            // 機銃を機首の先へ
            Transform gun = car.Find("Gun");
            if (gun != null)
            {
                Undo.RecordObject(gun, "Flight setup");
                gun.position = wb.center + fwd * (OmegaLength * 0.5f + 1f);
            }
            log.AppendLine("Omega: " + target.name + " を Omega FighterG にしました（元の長さ " + len.ToString("F1") + " → " + OmegaLength + "m、" + (alongX ? "X" : "Z") + " 軸を機首の向きに）。椅子は仮置きです");
        }
        else
        {
            log.AppendLine("Omega: " + target.name + "（設定済み。見た目と椅子の位置は手で直した物をそのまま使う）");
            CopyOmegaToPool(putOut, target, log);
        }
    }

    // Omega_1（手で直した見た目・椅子・砲塔の位置ごと）を複製して、プールの残りの R35 と入れ替える
    static void CopyOmegaToPool(PutOutDrone putOut, GameObject tmpl, StringBuilder log)
    {
        var pool = putOut.dronePool;
        var arr = pool.Pool;
        int made = 0;
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (var g in arr) if (g != null) names.Add(g.name);
        for (int i = 0; i < arr.Length; i++)
        {
            GameObject g = arr[i];
            if (g == null || IsOmega(g)) continue;
            var copy = Object.Instantiate(tmpl, g.transform.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Flight setup");
            copy.transform.SetPositionAndRotation(g.transform.position, g.transform.rotation);
            copy.transform.localScale = tmpl.transform.localScale;
            copy.transform.SetSiblingIndex(g.transform.GetSiblingIndex());
            int n = 2;
            while (names.Contains("Omega_" + n)) n++;
            copy.name = "Omega_" + n;
            names.Add(copy.name);
            copy.SetActive(g.activeSelf);
            log.AppendLine("Omega: " + g.name + " を " + copy.name + " に入れ替え（Omega_1 の複製）");
            names.Remove(g.name);
            Undo.DestroyObjectImmediate(g);
            arr[i] = copy;
            made++;
        }
        if (made > 0)
        {
            Undo.RecordObject(pool, "Flight setup");
            pool.Pool = arr;
            EditorUtility.SetDirty(pool);

        }
        // プールの外に残っている R35（椅子付き）も数える
        int left = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.car != null && IsR35(c.car) && !IsOmega(c.car)) left++;
        log.AppendLine("Omega: プール " + arr.Length + " 機すべて Omega（今回 " + made + " 機を入れ替え）。プール外の R35: " + left + " 機");
    }

    // Omega: 操縦士の機銃も、機体の下の旋回砲塔（2 本の砲身の先）から撃つ。
    // 操縦士の銃口は砲塔とは別に持つので、ガンナーが砲塔を回していても「＋」の方へ撃てる
    static void PlaceTurretGuns(controller c, Transform turret, StringBuilder log)
    {
        if (c == null || c.car == null || turret == null) return;
        Transform car = c.car.transform;
        Transform gun = car.Find("Gun");
        if (gun == null) return;
        Vector3 left = turret.TransformPoint(new Vector3(-0.13f, 0f, 1.6f));
        Vector3 right = turret.TransformPoint(new Vector3(0.13f, 0f, 1.6f));
        Undo.RecordObject(gun, "Flight setup");
        gun.SetPositionAndRotation(left, turret.rotation);
        Transform gunR = car.Find("Gun_R");
        if (gunR == null)
        {
            var g2 = Object.Instantiate(gun.gameObject, car);
            g2.name = "Gun_R";
            Undo.RegisterCreatedObjectUndo(g2, "Flight setup");
            gunR = g2.transform;
            Transform sight = gunR.Find("GunSight");
            if (sight != null) Object.DestroyImmediate(sight.gameObject);   // 照準は 1 つだけ
            var au2 = gunR.GetComponent<AudioSource>();
            if (au2 != null) Object.DestroyImmediate(au2);                  // 音は片方の銃から
        }
        gunR.SetPositionAndRotation(right, turret.rotation);
        Undo.RecordObject(c, "Flight setup");
        c.gunMuzzle = gun;
        c.gunMuzzle2 = gunR;
        Transform tr2 = gunR.Find("Tracer");
        c.tracerFx2 = tr2 != null ? tr2.GetComponent<ParticleSystem>() : null;
        UdonSharpEditorUtility.CopyProxyToUdon(c);
        log.AppendLine("Omega: 操縦士の機銃を機体の下の砲塔の砲身の先に");
    }

    static void SetupR35Pool(StringBuilder log)
    {
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        if (putOut == null || putOut.dronePool == null) { log.AppendLine("R35プール: 格納庫のプールが見つからない"); return; }
        var pool = putOut.dronePool;

        controller template = null;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.car != null && IsR35(c.car)) { template = c; break; }
        if (template == null) { log.AppendLine("R35プール: 椅子付きの R35 が見つからない"); return; }

        // すでに R35 だけのプールなら何もしない
        bool allR35 = pool.Pool != null && pool.Pool.Length > 0;
        if (pool.Pool != null) foreach (var g in pool.Pool) if (!IsR35(g)) allR35 = false;
        if (allR35) { log.AppendLine("R35プール: 設定済み（" + pool.Pool.Length + " 機）"); }
        else
        {
            var list = new System.Collections.Generic.List<GameObject>();
            list.Add(template.car);
            for (int i = 1; i < R35PoolSize; i++)
            {
                var copy = (GameObject)Object.Instantiate(template.car, template.car.transform.parent);
                copy.name = "R35_" + (i + 1);
                copy.transform.position = template.car.transform.position + Vector3.right * 40f * i;
                Undo.RegisterCreatedObjectUndo(copy, "Flight setup");
                list.Add(copy);
            }
            // 今までのドローンは消さずに非表示（元に戻せるように）
            if (pool.Pool != null)
            {
                foreach (var g in pool.Pool)
                {
                    if (g == null || list.Contains(g)) continue;
                    Undo.RecordObject(g, "Flight setup");
                    g.SetActive(false);
                }
            }
            var mainDrone = GameObject.Find("drone");
            if (mainDrone != null && mainDrone.GetComponentInChildren<controller>(true) != null && !list.Contains(mainDrone))
            {
                Undo.RecordObject(mainDrone, "Flight setup");
                mainDrone.SetActive(false);
            }
            foreach (var g in list)
            {
                Undo.RecordObject(g, "Flight setup");
                g.SetActive(false);   // プールの中の機体は、ボタンで出すまで非表示
            }
            Undo.RecordObject(pool, "Flight setup");
            pool.Pool = list.ToArray();
            EditorUtility.SetDirty(pool);
            log.AppendLine("R35プール: " + list.Count + " 機を格納庫のプールに入れた（ドローンは非表示）");
        }

        Undo.RecordObject(putOut, "Flight setup");
        putOut.NumberOFDrone = pool.Pool.Length;
        UdonSharpEditorUtility.CopyProxyToUdon(putOut);

        // テクスチャを 2048 に（ワールドのダウンロードサイズを抑える）
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/R35" }))
        {
            string tp = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (imp != null && imp.maxTextureSize > 2048)
            {
                imp.maxTextureSize = 2048;
                imp.SaveAndReimport();
            }
        }
    }

    // ---------------- R35 の部品の動き ----------------
    static bool partsRetried;
    const string PartsScriptPath = "Assets/My script/drone/R35Parts.cs";
    const string PartsAssetPath = "Assets/My script/drone/R35Parts.asset";

    static void SetupR35Parts(StringBuilder log)
    {
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car) || c.axisReference == null || IsOmega(c.car)) continue;
            Transform car = c.car.transform;
            Transform ax = c.axisReference;
            Vector3 rightL = car.InverseTransformDirection(ax.right).normalized;
            Vector3 fwdW = ax.forward, upW = ax.up, rightW = ax.right;

            Bounds body = new Bounds(car.position, Vector3.zero);
            Transform bodyT = car.Find("Dropship_R35");
            Bounds tmpB;
            if (PartBounds(bodyT, out tmpB)) body = tmpB;

            Transform e1 = car.Find("Dropship_R35-Engine1"), e2 = car.Find("Dropship_R35-Engine2");
            Transform gl = car.Find("Dropship_R35-Landing-L"), gr = car.Find("Dropship_R35-Landing-R"), gf = car.Find("Dropship_R35-Landing_Front");
            Transform hatch = car.Find("Dropship_Hatch");
            Transform l1 = car.Find("Dropship_R35-Laser"), l2 = car.Find("Dropship_R35-Laser_B");

            var parts = car.GetComponent<R35Parts>();
            if (parts == null) parts = UdonSharpEditor.UdonSharpUndo.AddComponent<R35Parts>(c.car);
            Undo.RecordObject(parts, "Flight setup");
            parts.plane = c;
            parts.seaLevel = seaY;
            parts.lateralAxis = rightL;

            parts.engines = new[] { e1, e2 };
            parts.enginePivots = new[] { PivotOf(car, e1), PivotOf(car, e2) };

            parts.gears = new[] { gl, gr, gf };
            var offs = new Vector3[3];
            Transform[] gs = { gl, gr, gf };
            for (int i = 0; i < 3; i++)
            {
                if (gs[i] == null) continue;
                Bounds gb;
                float h = PartBounds(gs[i], out gb) ? gb.size.y : 1f;
                offs[i] = car.InverseTransformVector(upW * h * 0.7f);
            }
            parts.gearRetractOffsets = offs;
            parts.gearDeployHeight = 20f;

            // ハッチ: 横にあれば上が蝶番で外へ、前後にあれば下が蝶番でスロープのように開く
            parts.hatch = hatch;
            string hatchKind = "なし";
            Bounds hb;
            if (hatch != null && PartBounds(hatch, out hb))
            {
                Vector3 rel = hb.center - body.center;
                float side = Vector3.Dot(rel, rightW), along = Vector3.Dot(rel, fwdW);
                if (Mathf.Abs(side) > Mathf.Abs(along))
                {
                    parts.hatchPivot = car.InverseTransformPoint(hb.center + upW * hb.extents.y);
                    parts.hatchAxis = car.InverseTransformDirection(fwdW).normalized;
                    parts.hatchOpenAngle = side > 0f ? -75f : 75f;
                    hatchKind = side > 0f ? "右の扉" : "左の扉";
                }
                else
                {
                    parts.hatchPivot = car.InverseTransformPoint(hb.center - upW * hb.extents.y);
                    parts.hatchAxis = rightL;
                    parts.hatchOpenAngle = along < 0f ? -70f : 70f;
                    hatchKind = along < 0f ? "後ろのスロープ" : "前のスロープ";
                }
            }

            parts.lasers = new[] { l1, l2 };
            parts.laserRecoil = car.InverseTransformVector(-fwdW * 0.15f);
            UdonSharpEditorUtility.CopyProxyToUdon(parts);
            n++;
            if (n == 1) log.AppendLine("R35の部品: エンジン " + (e1 != null) + "/" + (e2 != null) + " 脚 " + (gl != null) + "/" + (gr != null) + "/" + (gf != null) + " ハッチ " + hatchKind + " レーザー " + (l1 != null) + "/" + (l2 != null));
        }
        log.AppendLine("R35の部品の動き: " + n + " 機に設定");
    }

    static Vector3 PivotOf(Transform car, Transform part)
    {
        if (part == null) return Vector3.zero;
        Bounds b;
        return car.InverseTransformPoint(PartBounds(part, out b) ? b.center : part.position);
    }

    // ---------------- 大きさの測り方（非表示の物でも測れる） ----------------
    // MeshFilter のメッシュの大きさから、ワールドでの範囲を求める（Renderer.bounds は非表示だと 0 になるため）
    static bool MeshBounds(Transform root, string namePrefix, out Bounds bd)
    {
        bd = new Bounds();
        bool any = false;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (!string.IsNullOrEmpty(namePrefix) && !mf.name.StartsWith(namePrefix)) continue;
            bool inGear = false;   // 脚（タイヤ）は機体の大きさに含めない
            for (Transform pt = mf.transform; pt != null && pt != root.parent; pt = pt.parent) if (pt.name == "LandingGear") { inGear = true; break; }
            if (inGear) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 w = mf.transform.TransformPoint(c);
                if (!any) { bd = new Bounds(w, Vector3.zero); any = true; } else bd.Encapsulate(w);
            }
        }
        return any;
    }

    static bool PartBounds(Transform part, out Bounds bd)
    {
        bd = new Bounds();
        if (part == null) return false;
        var mf = part.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;
        Bounds mb = mf.sharedMesh.bounds;
        bool any = false;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 w = part.TransformPoint(c);
            if (!any) { bd = new Bounds(w, Vector3.zero); any = true; } else bd.Encapsulate(w);
        }
        return true;
    }

    // R35 の当たり判定・着地の高さ・操縦中に隠す外側
    static void FixR35(StringBuilder log)
    {
        int n = 0;
        var omegaList = new System.Collections.Generic.List<controller>();
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Transform car = c.car.transform;
            Bounds bd;
            bool omega = IsOmega(c.car);
            if (!MeshBounds(omega ? car.Find("OmegaBody") : car, omega ? "" : "Dropship", out bd)) continue;
            var bc = c.car.GetComponent<BoxCollider>();
            if (bc == null) bc = Undo.AddComponent<BoxCollider>(c.car);
            Undo.RecordObject(bc, "Flight setup");
            bc.center = car.InverseTransformPoint(bd.center);
            Vector3 sz = car.InverseTransformVector(bd.size);
            bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));

            var ext = new System.Collections.Generic.List<Renderer>();
            if (omega) { }   // Omega はガラスの操縦席なので、乗っている間も機体は隠さない
            else foreach (var r in c.car.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("Dropship")) ext.Add(r);

            Undo.RecordObject(c, "Flight setup");
            float halfH = Mathf.Max(bd.center.y - bd.min.y, 0.5f);
            c.gearHeight = halfH + 0.3f;
            c.waterContactHeight = halfH + 0.2f;
            if (omega) omegaList.Add(c);   // 脚はループの後でまとめて
            c.exteriorRenderers = ext.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
            if (n == 1) log.AppendLine("R35の大きさ: " + bd.size.ToString("F1") + "m（当たり判定をこの大きさに）");
        }
        if (n > 0) log.AppendLine("R35の当たり判定・高さ: " + n + " 機を修正");
        SetupOmegaGears(omegaList, log);   // 出し入れできる脚（タイヤ）
    }

    // ---------------- 出し入れできる脚（Omega） ----------------
    // 前輪 1 本（前に畳む）と主脚 2 本（内側に畳む）。タイヤには当たり判定があり、出し切った時だけ機体を支える
    // Omega_1 の脚を手で直したら、その形を他の Omega にコピーする（自動で作った形と違う 1 機を「手で直した物」とみなす）
    const float GearDrop = 1.4f;     // 胴体の一番下からタイヤの一番下まで（m、長さ 14m の機体の時）
    const float GearWheelR = 0.33f;  // タイヤの半径（m、同上）

    static Vector3[] BoxCorners(Transform t, Vector3 center, Vector3 size)
    {
        var a = new Vector3[8];
        for (int i = 0; i < 8; i++)
            a[i] = t.TransformPoint(center + Vector3.Scale(size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
        return a;
    }

    // 脚の見た目（と当たり判定）を作る。sc = 大きさの倍率（機体の長さ / 14m）
    static Transform MakeGear(Transform car, Vector3[] corners, Vector3 fwd, Vector3 up, float sc, PhysicMaterial pm, bool colliders)
    {
        Vector3 right = Vector3.Cross(up, fwd);
        Vector3 cW = Vector3.zero;
        foreach (var p in corners) cW += p;
        cW /= corners.Length;
        float minF = 1e9f, maxF = -1e9f, minR = 1e9f, maxR = -1e9f, minU = 1e9f;
        foreach (var p in corners)
        {
            Vector3 d = p - cW;
            float pf = Vector3.Dot(d, fwd), pr = Vector3.Dot(d, right), pu = Vector3.Dot(d, up);
            minF = Mathf.Min(minF, pf); maxF = Mathf.Max(maxF, pf);
            minR = Mathf.Min(minR, pr); maxR = Mathf.Max(maxR, pr);
            minU = Mathf.Min(minU, pu);
        }
        float len = maxF - minF, wid = maxR - minR, midF = (minF + maxF) * 0.5f;
        var rootGo = new GameObject("LandingGear");
        Undo.RegisterCreatedObjectUndo(rootGo, "Flight setup");
        Transform root = rootGo.transform;
        root.SetParent(car, false);
        root.SetPositionAndRotation(cW, Quaternion.LookRotation(fwd, up));
        SetWorldScaleOne(root);
        int layer = car.gameObject.layer;
        rootGo.layer = layer;

        Material metal = LoadOrCreateMat("GearMetal", Shader.Find("Standard"));
        metal.SetColor("_Color", new Color(0.62f, 0.64f, 0.66f));
        metal.SetFloat("_Metallic", 0.8f);
        metal.SetFloat("_Glossiness", 0.55f);
        EditorUtility.SetDirty(metal);
        Material tire = LoadOrCreateMat("GearTire", Shader.Find("Standard"));
        tire.SetColor("_Color", new Color(0.06f, 0.06f, 0.06f));
        tire.SetFloat("_Metallic", 0f);
        tire.SetFloat("_Glossiness", 0.15f);
        EditorUtility.SetDirty(tire);

        float drop = GearDrop * sc, wr = GearWheelR * sc;
        float side = Mathf.Min(wid * 0.16f, 2.2f * sc);
        string[] names = { "GearNose", "GearLeft", "GearRight" };
        Vector3[] pos = {
            new Vector3(0f, minU + 0.05f * sc, midF + len * 0.30f),
            new Vector3(-side, minU + 0.05f * sc, midF - len * 0.06f),
            new Vector3(side, minU + 0.05f * sc, midF - len * 0.06f) };
        float strutLen = drop - wr;
        for (int i = 0; i < 3; i++)
        {
            var mount = new GameObject(names[i]).transform;
            mount.SetParent(root, false);
            mount.localPosition = pos[i];
            mount.gameObject.layer = layer;
            var pv = new GameObject("Pivot").transform;
            pv.SetParent(mount, false);
            pv.gameObject.layer = layer;
            var strut = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(strut.GetComponent<Collider>());
            strut.name = "Strut";
            strut.layer = layer;
            strut.transform.SetParent(pv, false);
            strut.transform.localPosition = new Vector3(0f, -strutLen * 0.5f, 0f);
            strut.transform.localScale = new Vector3(0.13f * sc, strutLen * 0.5f, 0.13f * sc);
            strut.GetComponent<MeshRenderer>().sharedMaterial = metal;
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(wheel.GetComponent<Collider>());
            wheel.name = "Wheel";
            wheel.layer = layer;
            wheel.transform.SetParent(pv, false);
            wheel.transform.localPosition = new Vector3(0f, -strutLen, 0f);
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.transform.localScale = new Vector3(wr * 2f, (i == 0 ? 0.1f : 0.13f) * sc, wr * 2f);
            wheel.GetComponent<MeshRenderer>().sharedMaterial = tire;
            var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.name = "Hub";
            hub.layer = layer;
            hub.transform.SetParent(pv, false);
            hub.transform.localPosition = new Vector3(0f, -strutLen, 0f);
            hub.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            hub.transform.localScale = new Vector3(wr * 1.1f, (i == 0 ? 0.105f : 0.135f) * sc, wr * 1.1f);
            hub.GetComponent<MeshRenderer>().sharedMaterial = metal;
            if (colliders)
            {
                var colGo = new GameObject("WheelCollider");
                colGo.layer = layer;
                colGo.transform.SetParent(pv, false);
                colGo.transform.localPosition = new Vector3(0f, -strutLen, 0f);
                var scol = colGo.AddComponent<SphereCollider>();
                scol.radius = wr;
                if (pm != null) scol.sharedMaterial = pm;
            }
        }
        return root;
    }

    // 脚の形の「指紋」（位置・回転・大きさを丸めて並べた文字列）。手で直したかどうかの見分けに使う
    static string GearSignature(Transform car, Transform root)
    {
        var sb = new StringBuilder();
        Quaternion inv = Quaternion.Inverse(car.rotation);
        Vector3 rp = car.InverseTransformPoint(root.position);
        Vector3 rr = (inv * root.rotation).eulerAngles;
        sb.Append(rp.x.ToString("F3")).Append(',').Append(rp.y.ToString("F3")).Append(',').Append(rp.z.ToString("F3")).Append(';')
          .Append(rr.x.ToString("F0")).Append(',').Append(rr.y.ToString("F0")).Append(',').Append(rr.z.ToString("F0")).Append('|');
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            Vector3 lp = t.localPosition, le = t.localEulerAngles, ls = t.localScale;
            sb.Append(t.name).Append(':')
              .Append(lp.x.ToString("F3")).Append(',').Append(lp.y.ToString("F3")).Append(',').Append(lp.z.ToString("F3")).Append(';')
              .Append(le.x.ToString("F0")).Append(',').Append(le.y.ToString("F0")).Append(',').Append(le.z.ToString("F0")).Append(';')
              .Append(ls.x.ToString("F3")).Append(',').Append(ls.y.ToString("F3")).Append(',').Append(ls.z.ToString("F3")).Append('|');
        }
        return sb.ToString().Replace("-0.000", "0.000").Replace("360", "0");
    }

    static void OmegaAxes(controller c, out Vector3 fwd, out Vector3 up)
    {
        Transform car = c.car.transform;
        Transform axis = car.Find("FlightAxis");
        fwd = axis != null ? axis.forward : car.forward;
        up = axis != null ? axis.up : car.up;
    }

    // 全 Omega の脚: 無ければ作る / 手で直した 1 機があれば、その形を他にコピー / 最後に controller へ登録
    static void SetupOmegaGears(System.Collections.Generic.List<controller> list, StringBuilder log)
    {
        if (list.Count == 0) return;
        // 1) 手で直した脚を探す
        //    ・脚の形が 1 機だけ他と違う → その 1 機を手で直した物とみなす（2 回目以降に直した時）
        //    ・全部同じ → 揃っているので何もしない
        //    ・自動で作った時の形と違うのが 1 機だけ → それを元にする（最初に直した時）
        var sigs = new System.Collections.Generic.Dictionary<controller, string>();
        var count = new System.Collections.Generic.Dictionary<string, int>();
        var edited = new System.Collections.Generic.List<controller>();
        foreach (var c in list)
        {
            Transform car = c.car.transform;
            Transform g = car.Find("LandingGear");
            if (g == null) continue;
            string sg = GearSignature(car, g);
            sigs[c] = sg;
            count[sg] = count.ContainsKey(sg) ? count[sg] + 1 : 1;
            var bc = c.car.GetComponent<BoxCollider>();
            Vector3 fwd, up;
            OmegaAxes(c, out fwd, out up);
            Transform tmp = MakeGear(car, BoxCorners(car, bc.center, bc.size), fwd, up, 1f, null, true);
            string expect = GearSignature(car, tmp);
            Object.DestroyImmediate(tmp.gameObject);
            if (sg != expect) edited.Add(c);
        }
        controller tmpl = null;
        if (count.Count == 1 && sigs.Count == list.Count) log.AppendLine("脚: 全 " + list.Count + " 機とも同じ形（揃っています）");
        else
        {
            var uniq = new System.Collections.Generic.List<controller>();
            foreach (var kv in sigs) if (count[kv.Value] == 1) uniq.Add(kv.Key);
            if (uniq.Count == 1 && sigs.Count >= 3) tmpl = uniq[0];
            else if (edited.Count == 1) tmpl = edited[0];
            else if (edited.Count > 1)
            {
                var named = edited.FindAll(e => e.car.name == "Omega_1");
                if (named.Count == 1) tmpl = named[0];
                else log.AppendLine("脚: 形の違う脚が " + edited.Count + " 機あり、どれを元にするか決められないので、コピーしませんでした");
            }
        }
        if (tmpl != null) log.AppendLine("脚: " + tmpl.car.name + " の手で直した脚を元にします");

        // 2) コピー / 新しく作る
        foreach (var c in list)
        {
            Transform car = c.car.transform;
            Transform g = car.Find("LandingGear");
            if (tmpl != null && c != tmpl)
            {
                Transform src = tmpl.car.transform.Find("LandingGear");
                if (g != null) Undo.DestroyObjectImmediate(g.gameObject);
                var cp = Object.Instantiate(src.gameObject, car);
                Undo.RegisterCreatedObjectUndo(cp, "Flight setup");
                cp.name = "LandingGear";
                Transform tc = tmpl.car.transform;
                cp.transform.position = car.TransformPoint(tc.InverseTransformPoint(src.position));
                cp.transform.rotation = car.rotation * (Quaternion.Inverse(tc.rotation) * src.rotation);
                cp.transform.localScale = src.localScale;
                log.AppendLine("脚: " + car.name + " に " + tmpl.car.name + " の脚をコピー");
            }
            else if (g == null)
            {
                var bc = c.car.GetComponent<BoxCollider>();
                Vector3 fwd, up;
                OmegaAxes(c, out fwd, out up);
                MakeGear(car, BoxCorners(car, bc.center, bc.size), fwd, up, 1f, bc.sharedMaterial, true);
                log.AppendLine("脚: " + car.name + " に前輪 1・主脚 2 を作りました");
            }
            GatherGear(c, log);
        }
    }

    // LandingGear の中身を controller に登録。タイヤの当たり判定は見た目のタイヤに合わせ直す
    static void GatherGear(controller c, StringBuilder log)
    {
        Transform car = c.car.transform;
        Transform root = car.Find("LandingGear");
        if (root == null) return;
        Vector3 fwd, up;
        OmegaAxes(c, out fwd, out up);
        var bc = c.car.GetComponent<BoxCollider>();
        var pivots = new System.Collections.Generic.List<Transform>();
        var folds = new System.Collections.Generic.List<Vector3>();
        var bases = new System.Collections.Generic.List<Quaternion>();
        var cols = new System.Collections.Generic.List<Collider>();
        var vis = new System.Collections.Generic.List<GameObject>();
        float bottomU = 1e9f;
        foreach (var pv in root.GetComponentsInChildren<Transform>(true))
        {
            if (pv.name != "Pivot") continue;
            string pn = pv.parent != null ? pv.parent.name : "";
            Vector3 fold = pn.Contains("Left") ? new Vector3(0f, 0f, 90f) : (pn.Contains("Right") ? new Vector3(0f, 0f, -90f) : new Vector3(-90f, 0f, 0f));
            Transform wheel = null;
            foreach (var t in pv.GetComponentsInChildren<Transform>(true)) if (t.name == "Wheel") { wheel = t; break; }
            SphereCollider scol = pv.GetComponentInChildren<SphereCollider>(true);
            if (scol == null)
            {
                var colGo = new GameObject("WheelCollider");
                colGo.layer = car.gameObject.layer;
                colGo.transform.SetParent(pv, false);
                scol = colGo.AddComponent<SphereCollider>();
                if (bc != null) scol.sharedMaterial = bc.sharedMaterial;
            }
            Undo.RecordObject(scol, "Flight setup");
            Undo.RecordObject(scol.transform, "Flight setup");
            if (wheel != null)
            {
                // 当たり判定を見た目のタイヤの位置・大きさに
                scol.transform.position = wheel.position;
                float worldR = Mathf.Abs(wheel.lossyScale.x) * 0.5f;
                Vector3 ls = scol.transform.lossyScale;
                float m = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                scol.center = Vector3.zero;
                scol.radius = m > 0.0001f ? worldR / m : worldR;
            }
            Vector3 lsc = scol.transform.lossyScale;
            float wr = scol.radius * Mathf.Max(Mathf.Abs(lsc.x), Mathf.Max(Mathf.Abs(lsc.y), Mathf.Abs(lsc.z)));
            float u = Vector3.Dot(scol.transform.TransformPoint(scol.center) - car.position, up) - wr;
            bottomU = Mathf.Min(bottomU, u);
            pivots.Add(pv); folds.Add(fold); bases.Add(pv.localRotation); cols.Add(scol); vis.Add(pv.gameObject);
        }
        if (pivots.Count == 0) { log.AppendLine("脚: " + car.name + " の LandingGear に Pivot が見つからない"); return; }
        float boxBottomU = 1e9f;
        if (bc != null) foreach (var p in BoxCorners(car, bc.center, bc.size)) boxBottomU = Mathf.Min(boxBottomU, Vector3.Dot(p - car.position, up));
        Undo.RecordObject(c, "Flight setup");
        c.gearPivots = pivots.ToArray();
        c.gearFold = folds.ToArray();
        c.gearBaseRot = bases.ToArray();
        c.gearColliders = cols.ToArray();
        c.gearVisuals = vis.ToArray();
        c.gearRoot = root.gameObject;
        c.gearHeightDown = -bottomU + 0.3f;
        c.gearLift = Mathf.Max(boxBottomU - bottomU, 0f);
        UdonSharpEditorUtility.CopyProxyToUdon(c);
        PrefabUtility.RecordPrefabInstancePropertyModifications(c);
    }

    // 敵の戦闘機の脚をそろえる: 形が 1 機だけ他と違う（＝手で直した）機体の脚を、全機にコピー。
    // 見分けられない時は EnemyFighter_20 を元にする
    // 機体の見た目（モデル）の Transform。機体の根元と見た目の位置がずれている機体があるので、脚はこれを基準にする
    static Transform EnemyModelOf(GameObject f)
    {
        foreach (Transform ch in f.transform)
        {
            if (ch.name == "LandingGear" || ch.name == "Gun" || ch.name == "EnemyBrain") continue;
            if (ch.GetComponentInChildren<Renderer>(true) != null) return ch;
        }
        return f.transform;
    }

    // 敵の脚を EnemyDrone に登録
    static bool GatherEnemyGear(GameObject f)
    {
        Transform root = f.transform.Find("LandingGear");
        var ed = f.GetComponentInChildren<EnemyDrone>(true);
        if (root == null || ed == null) return false;
        var pivots = new System.Collections.Generic.List<Transform>();
        var folds = new System.Collections.Generic.List<Vector3>();
        var bases = new System.Collections.Generic.List<Quaternion>();
        var vis = new System.Collections.Generic.List<GameObject>();
        foreach (var pv in root.GetComponentsInChildren<Transform>(true))
        {
            if (pv.name != "Pivot") continue;
            string pn = pv.parent != null ? pv.parent.name : "";
            pivots.Add(pv);
            folds.Add(pn.Contains("Left") ? new Vector3(0f, 0f, 90f) : (pn.Contains("Right") ? new Vector3(0f, 0f, -90f) : new Vector3(-90f, 0f, 0f)));
            bases.Add(pv.localRotation);
            vis.Add(pv.gameObject);
        }
        Undo.RecordObject(ed, "Flight setup");
        ed.gearPivots = pivots.ToArray();
        ed.gearFold = folds.ToArray();
        ed.gearBaseRot = bases.ToArray();
        ed.gearVisuals = vis.ToArray();
        ed.gearRoot = root.gameObject;
        UdonSharpEditorUtility.CopyProxyToUdon(ed);
        return pivots.Count > 0;
    }

    static void SyncEnemyGears(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var sigs = new System.Collections.Generic.Dictionary<GameObject, string>();
        var count = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var f in fighters)
        {
            Transform g = f.transform.Find("LandingGear");
            if (g == null) continue;
            string sg = GearSignature(EnemyModelOf(f), g);   // 見た目から見た位置で比べる
            sigs[f] = sg;
            count[sg] = count.ContainsKey(sg) ? count[sg] + 1 : 1;
        }
        if (sigs.Count == 0) return;
        if (count.Count == 1) { log.AppendLine("脚（敵）: 全 " + sigs.Count + " 機とも同じ形（揃っています）"); return; }
        GameObject tmpl = null;
        var uniq = new System.Collections.Generic.List<GameObject>();
        foreach (var kv in sigs) if (count[kv.Value] == 1) uniq.Add(kv.Key);
        if (uniq.Count == 1 && sigs.Count >= 3) tmpl = uniq[0];
        else foreach (var f in fighters) if (f.name == "EnemyFighter_1" && sigs.ContainsKey(f)) { tmpl = f; break; }
        if (tmpl == null) { log.AppendLine("脚（敵）: どの機体を元にするか決められないので、コピーしませんでした"); return; }
        Transform src = tmpl.transform.Find("LandingGear");
        Transform sm = EnemyModelOf(tmpl);
        Vector3 relP = sm.InverseTransformPoint(src.position);
        Quaternion relR = Quaternion.Inverse(sm.rotation) * src.rotation;
        int n = 0;
        foreach (var f in fighters)
        {
            if (f == tmpl) continue;
            Transform car = f.transform;
            Transform g = car.Find("LandingGear");
            if (g != null) Undo.DestroyObjectImmediate(g.gameObject);
            var cp = Object.Instantiate(src.gameObject, car);
            Undo.RegisterCreatedObjectUndo(cp, "Flight setup");
            cp.name = "LandingGear";
            Transform m = EnemyModelOf(f);
            cp.transform.position = m.TransformPoint(relP);
            cp.transform.rotation = m.rotation * relR;
            cp.transform.localScale = src.localScale;
            n++;
        }
        log.AppendLine("脚（敵）: " + tmpl.name + " の脚を他の " + n + " 機にコピー（機体の見た目の位置に合わせて）");
    }

    // 敵の戦闘機: 脚の見た目だけ付ける。手で直せるよう、作るのは最初の 1 回だけ
    static void TryEnemyGear(GameObject go, StringBuilder log)
    {
        Transform tr = go.transform;
        if (tr.Find("LandingGear") != null) { return; }
        Bounds wb;
        if (!MeshBounds(tr, "", out wb)) return;
        Vector3 lc = tr.InverseTransformPoint(wb.center);
        Vector3 ls = tr.InverseTransformVector(wb.size);
        ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        float len = Mathf.Abs(Vector3.Dot(tr.TransformVector(new Vector3(0f, 0f, ls.z)), tr.forward));
        MakeGear(tr, BoxCorners(tr, lc, ls), tr.forward, tr.up, Mathf.Max(len / 14f, 0.1f), null, false);
        log.AppendLine("脚: " + go.name + "（敵）に脚を付けました（長さ " + len.ToString("F1") + "m の機体）");
    }

    // ---------------- 敵機（古いドローン） ----------------
    static bool enemyRetried;
    const string EnemyScriptPath = "Assets/My script/drone/EnemyDrone.cs";
    const string EnemyAssetPath = "Assets/My script/drone/EnemyDrone.asset";
    const int EnemyCount = 16;
    const float EnemyScale = 4.5f;   // 見やすいように元の大きさの 4.5 倍

    static void SetupEnemies(StringBuilder log)
    {
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        var poolObjs = (putOut != null && putOut.dronePool != null) ? putOut.dronePool.Pool : null;
        var drones = new System.Collections.Generic.List<controller>();
        var pilots = new System.Collections.Generic.List<controller>();
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null) continue;
            if (IsR35(c.car)) { pilots.Add(c); continue; }
            if (poolObjs != null && System.Array.IndexOf(poolObjs, c.car) >= 0) continue;
            if (c.car.GetComponent<Rigidbody>() == null) continue;
            drones.Add(c);
        }
        drones.Sort((a, b) => string.CompareOrdinal(a.car.name, b.car.name));
        // 「enemy aircraft」の戦闘機があれば、ドローンは全部しまって戦闘機を敵にする
        Transform fighterRoot = FindByNameContains("enemy aircraft");
        if (fighterRoot == null) fighterRoot = FindByNameContains("enemyaircraft");
        if (fighterRoot != null)
        {
            foreach (var dc in drones) { Undo.RecordObject(dc.car, "Flight setup"); dc.car.SetActive(false); }
            EnsureFlyingAwacs(fighterRoot, log);   // 飛ぶ警戒機 3 機（基地の警戒機と同じ見た目）
            EnsureCarrierFighters(fighterRoot, log);   // 空母の戦闘機 4 機（水色）
            EnsureBaseFighters(fighterRoot, log);      // 基地の戦闘機 4 機
            SetupFighters(fighterRoot, putOut, pilots, log);
            SetupParkedAwacs(log);                 // 基地に止まっている警戒機（壊せる）
            return;
        }
        // 足りない分は複製して増やす
        int origCount = drones.Count;
        for (int i = 0; origCount > 0 && drones.Count < EnemyCount; i++)
        {
            GameObject src = drones[i % origCount].car;
            GameObject cp = Object.Instantiate(src, src.transform.parent);
            cp.name = "EnemyDrone_" + (drones.Count + 1);
            Undo.RegisterCreatedObjectUndo(cp, "Flight setup");
            controller cc = cp.GetComponentInChildren<controller>(true);
            if (cc == null) break;
            Undo.RecordObject(cc, "Flight setup");
            cc.car = cp;
            drones.Add(cc);
        }
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;

        int k = 0;
        foreach (var c in drones)
        {
            GameObject car = c.car;
            bool use = k < EnemyCount;
            Undo.RecordObject(car, "Flight setup");
            car.SetActive(use);
            if (!use) { k++; continue; }

            // 人が乗る仕組みを止める（座席・クリックで乗る・ドローン用の重力）
            Undo.RecordObject(c.gameObject, "Flight setup");
            c.gameObject.SetActive(false);
            foreach (var mb in car.GetComponents<UdonSharpBehaviour>())
            {
                if (mb is ajsutGravity || mb is BoardPlane)
                {
                    Undo.RecordObject(mb, "Flight setup");
                    mb.enabled = false;
                    UdonBehaviour bub = UdonSharpEditorUtility.GetBackingUdonBehaviour(mb);
                    if (bub != null) { Undo.RecordObject(bub, "Flight setup"); bub.enabled = false; }
                }
            }
            // 大きさを 3 倍に（元の大きさを子の印に覚えておき、何回実行しても 3 倍のまま）
            Transform baseMark = car.transform.Find("EnemyBaseScale");
            if (baseMark == null)
            {
                var bm = new GameObject("EnemyBaseScale");
                Undo.RegisterCreatedObjectUndo(bm, "Flight setup");
                bm.transform.SetParent(car.transform, false);
                bm.transform.localPosition = car.transform.localScale;
                baseMark = bm.transform;
            }
            Undo.RecordObject(car.transform, "Flight setup");
            car.transform.localScale = baseMark.localPosition * EnemyScale;

            var rb = car.GetComponent<Rigidbody>();
            Undo.RecordObject(rb, "Flight setup");
            rb.useGravity = false;
            rb.isKinematic = false;

            // 頭脳（EnemyDrone と耐久値）を子に作る
            Transform brain = car.transform.Find("EnemyBrain");
            if (brain != null) Undo.DestroyObjectImmediate(brain.gameObject);
            var bg = new GameObject("EnemyBrain");
            Undo.RegisterCreatedObjectUndo(bg, "Flight setup");
            bg.transform.SetParent(car.transform, false);
            brain = bg.transform;

            var rends = new System.Collections.Generic.List<Renderer>();
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                if (r.transform.IsChildOf(c.transform)) continue;   // 座席まわり
                rends.Add(r);
            }
            var cols = new System.Collections.Generic.List<Collider>();
            foreach (var col in car.GetComponents<Collider>()) if (!col.isTrigger) cols.Add(col);

            var ft = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(bg);
            ft.renderers = rends.ToArray();
            ft.colliders = cols.ToArray();
            ft.maxHp = 10;
            ft.missileDamage = 15;
            ft.respawnTime = 25f;
            var ex = MakePS("Explosion", brain, fxMat);
            ConfigureExplosion(ex, 6.5f, 90);   // 大きさに合わせて大きな爆発
            var exAu = ex.gameObject.AddComponent<AudioSource>();
            exAu.clip = boom;
            exAu.playOnAwake = false;
            exAu.spatialBlend = 1f;
            exAu.minDistance = 25f;
            exAu.maxDistance = 2000f;
            ft.explosionFx = ex;
            ft.explosionAudio = exAu;
            UdonSharpEditorUtility.CopyProxyToUdon(ft);

            var ed = UdonSharpEditor.UdonSharpUndo.AddComponent<EnemyDrone>(bg);
            ed.rb = rb;
            ed.target = ft;
            ed.planes = pilots.ToArray();
            Transform gun = car.transform.Find("Gun");
            ed.muzzle = gun;
            ed.tracer = gun != null && gun.Find("Tracer") != null ? gun.Find("Tracer").GetComponent<ParticleSystem>() : null;
            ed.gunAudio = gun != null ? gun.GetComponent<AudioSource>() : null;
            ed.areaCenter = CloudCenter;
            if (putOut != null && putOut.catapultPoint != null) ed.avoidCenter = putOut.catapultPoint.position;   // 出撃地点のまわりには出ない
            ed.seaLevel = seaY;
            UdonSharpEditorUtility.CopyProxyToUdon(ed);

            // 最初の位置: 中心のまわりに散らす（雲の下・高度 200m）
            float ang = k * Mathf.PI * 2f / EnemyCount;
            Vector3 p = CloudCenter + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1800f;
            p.y = seaY + 200f;
            Vector3 fwd = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            Undo.RecordObject(car.transform, "Flight setup");
            car.transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 90f, 0f));
            k++;
        }
        log.AppendLine("敵機: " + Mathf.Min(drones.Count, EnemyCount) + " 機を雲の下に配置（ドローン " + drones.Count + " 機中）/ 狙う相手 " + pilots.Count + " 機");
    }

    static Transform FindByNameContains(string key)
    {
        key = key.ToLower();
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name.ToLower().Contains(key) && t.parent == null) return t;
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name.ToLower().Contains(key)) return t;
        return null;
    }

    // ---------------- 敵の戦闘機（enemy aircraft の中の機体） ----------------
    const int FighterCount = 16;

    static void SetupFighters(Transform root, PutOutDrone putOut, System.Collections.Generic.List<controller> pilots, StringBuilder log)
    {
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        AudioClip gunClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/gun.wav");
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;

        // 機体 = root の直下で見た目を持つ物（無ければ root 自身）
        var fighters = new System.Collections.Generic.List<GameObject>();
        foreach (Transform ch in root)
        {
            if (ch.name == "EnemyBrain" || ch.name == "Gun") continue;
            if (ch.GetComponentInChildren<Renderer>(true) != null) fighters.Add(ch.gameObject);
        }
        if (fighters.Count == 0 && root.GetComponentInChildren<Renderer>(true) != null) fighters.Add(root.gameObject);
        if (fighters.Count == 0) { log.AppendLine("戦闘機: enemy aircraft の中に機体が見つからない"); return; }
        int orig = fighters.Count;
        for (int i = 0; fighters.Count < FighterCount && fighters[0] != root.gameObject; i++)
        {
            GameObject src = fighters[i % orig];
            GameObject cp = Object.Instantiate(src, src.transform.parent);
            Undo.RegisterCreatedObjectUndo(cp, "Flight setup");
            fighters.Add(cp);
        }

        ScaleFighters(fighters, root, log);   // 敵の戦闘機を半分の大きさに（本物の戦闘機くらい）
        Bounds firstB = new Bounds();
        int n = 0, fno = 0;
        foreach (var go in fighters)
        {
            n++;
            Undo.RecordObject(go, "Flight setup");
            go.SetActive(true);
            if (go != root.gameObject && !IsAwacs(go) && !IsCarrierFighter(go) && !IsBaseFighter(go)) go.name = "EnemyFighter_" + (++fno);   // 警戒機・空母の戦闘機は名前を変えない
            Transform tr = go.transform;
            // 古い部品を消して作り直し
            foreach (var nm in new[] { "EnemyBrain", "Gun" })
            {
                Transform old;
                int guard = 0;
                while ((old = tr.Find(nm)) != null && guard++ < 10) Undo.DestroyObjectImmediate(old.gameObject);
            }
            // 影を落とさない（軽量化）
            var rends = new System.Collections.Generic.List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                Undo.RecordObject(r, "Flight setup");
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rends.Add(r);
            }
            // 大きさ（機体の向きの中で）
            Bounds wb;
            if (!MeshBounds(tr, "", out wb)) wb = new Bounds(tr.position, Vector3.one * 10f);
            Vector3 lc = tr.InverseTransformPoint(wb.center);
            Vector3 ls = tr.InverseTransformVector(wb.size);
            ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            if (n == 1) firstB = new Bounds(lc, Vector3.Scale(ls, tr.lossyScale));

            // 物理
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(go);
            Undo.RecordObject(rb, "Flight setup");
            rb.mass = 5000f;
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var cols = new System.Collections.Generic.List<Collider>();
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) if (!col.isTrigger) cols.Add(col);
            if (cols.Count == 0)
            {
                var bc = Undo.AddComponent<BoxCollider>(go);
                bc.center = lc;
                bc.size = ls * 0.8f;
                cols.Add(bc);
            }
            if (go.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null) Undo.AddComponent<VRC.SDK3.Components.VRCObjectSync>(go);

            // 機銃（機首の先）
            var gun = new GameObject("Gun");
            Undo.RegisterCreatedObjectUndo(gun, "Flight setup");
            gun.transform.SetParent(tr, false);
            gun.transform.position = tr.TransformPoint(lc + new Vector3(0f, 0f, ls.z * 0.5f + 1f));
            gun.transform.rotation = tr.rotation;
            SetWorldScaleOne(gun.transform);
            var trc = MakePS("Tracer", gun.transform, fxMat);
            var tm = trc.main;
            tm.startLifetime = 1.4f;
            tm.startSpeed = 500f;
            tm.startSize = 0.4f;
            tm.startColor = new Color(1f, 0.45f, 0.25f, 1f);
            tm.maxParticles = 200;
            var tsh = trc.shape;
            tsh.enabled = false;
            var tiv = trc.inheritVelocity;
            tiv.enabled = true;
            tiv.mode = ParticleSystemInheritVelocityMode.Initial;
            tiv.curve = new ParticleSystem.MinMaxCurve(1f);
            var trr = trc.GetComponent<ParticleSystemRenderer>();
            trr.renderMode = ParticleSystemRenderMode.Stretch;
            trr.velocityScale = 0.025f;
            trr.lengthScale = 2f;
            var gau = gun.AddComponent<AudioSource>();
            gau.clip = gunClip;
            gau.playOnAwake = false;
            gau.spatialBlend = 1f;
            gau.minDistance = 15f;
            gau.maxDistance = 1200f;

            // 頭脳（耐久値と飛び方）
            var bg = new GameObject("EnemyBrain");
            Undo.RegisterCreatedObjectUndo(bg, "Flight setup");
            bg.transform.SetParent(tr, false);
            var ft = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(bg);
            ft.renderers = rends.ToArray();
            ft.colliders = cols.ToArray();
            ft.maxHp = 10;
            ft.missileDamage = 15;
            ft.respawnTime = 25f;
            ft.kind = 0;   // 飛行機
            var ex = MakePS("Explosion", bg.transform, fxMat);
            ConfigureExplosion(ex, 5f, 90);
            var exAu = ex.gameObject.AddComponent<AudioSource>();
            exAu.clip = boom;
            exAu.playOnAwake = false;
            exAu.spatialBlend = 1f;
            exAu.minDistance = 30f;
            exAu.maxDistance = 2500f;
            ft.explosionFx = ex;
            ft.explosionAudio = exAu;
            UdonSharpEditorUtility.CopyProxyToUdon(ft);

            var ed = UdonSharpEditor.UdonSharpUndo.AddComponent<EnemyDrone>(bg);
            ed.rb = rb;
            ed.target = ft;
            ed.planes = pilots.ToArray();
            ed.muzzle = gun.transform;
            ed.tracer = trc;
            ed.gunAudio = gau;
            ed.noseIsZ = true;
            // ダメージの煙と炎
            Material smokeM = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
            if (smokeM == null) smokeM = fxMat;
            var fsm = MakePS("DamageSmoke", bg.transform, smokeM);
            fsm.transform.position = wb.center;
            var fsmm = fsm.main;
            fsmm.loop = true;
            fsmm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            fsmm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            fsmm.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
            fsmm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.15f, 0.15f, 0.7f), new Color(0.35f, 0.35f, 0.35f, 0.6f));
            fsmm.maxParticles = 200;
            var fsol2 = fsm.sizeOverLifetime; fsol2.enabled = true; fsol2.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            SetFade(fsm, new Color(0.25f, 0.25f, 0.25f), new Color(0.5f, 0.5f, 0.5f));
            var ffi = MakePS("DamageFire", bg.transform, fxMat);
            ffi.transform.position = wb.center;
            var ffim = ffi.main;
            ffim.loop = true;
            ffim.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            ffim.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            ffim.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            ffim.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.9f));
            ffim.maxParticles = 150;
            SetFade(ffi, new Color(1f, 0.7f, 0.3f), new Color(0.6f, 0.1f, 0.05f));
            ed.dmgSmoke = fsm;
            ed.dmgFire = ffi;
            // ミサイル（この機体専用の 1 発。見た目は機体の外に置く）
            var mRoot = GameObject.Find("EnemyMissiles");
            if (mRoot == null) { mRoot = new GameObject("EnemyMissiles"); Undo.RegisterCreatedObjectUndo(mRoot, "Flight setup"); }
            Transform oldM = mRoot.transform.Find("Missile_" + go.name);
            if (oldM != null) Undo.DestroyObjectImmediate(oldM.gameObject);
            var em = new GameObject("Missile_" + go.name);
            Undo.RegisterCreatedObjectUndo(em, "Flight setup");
            em.transform.SetParent(mRoot.transform, false);
            var emb = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(emb.GetComponent<Collider>());
            emb.name = "Body";
            emb.transform.SetParent(em.transform, false);
            emb.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            emb.transform.localScale = new Vector3(0.3f, 1.1f, 0.3f);
            Material emMat = LoadOrCreateMat("ShipRocket", Shader.Find("Standard"));
            emb.GetComponent<MeshRenderer>().sharedMaterial = emMat;
            emb.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var efl = MakePS("Flame", em.transform, fxMat);
            efl.transform.localPosition = new Vector3(0f, 0f, -1.2f);
            var eflm = efl.main;
            eflm.loop = true; eflm.playOnAwake = true;
            eflm.simulationSpace = ParticleSystemSimulationSpace.Local;
            eflm.startLifetime = 0.12f; eflm.startSpeed = 0f;
            eflm.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            eflm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.15f), new Color(1f, 0.85f, 0.45f));
            eflm.maxParticles = 30;
            var efle = efl.emission; efle.rateOverTime = 80f;
            var efls = efl.shape; efls.enabled = false;
            var etr = em.AddComponent<TrailRenderer>();
            etr.sharedMaterial = smokeM;
            etr.time = 2.5f;
            etr.minVertexDistance = 2f;
            etr.widthCurve = AnimationCurve.Linear(0f, 0.8f, 1f, 3.5f);
            var eg = new Gradient();
            eg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.75f, 0.75f), 1f) },
                       new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            etr.colorGradient = eg;
            etr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var eau = em.AddComponent<AudioSource>();
            eau.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/missile.wav");
            eau.playOnAwake = false; eau.spatialBlend = 1f; eau.minDistance = 15f; eau.maxDistance = 1500f;
            em.SetActive(false);
            ed.missile = em.transform;
            var splashG = GameObject.Find("MissileSplash");
            var crashG = GameObject.Find("CrashExplosion");
            ed.missileSplash = splashG != null ? splashG.GetComponent<ParticleSystem>() : null;
            ed.missileBoom = crashG != null ? crashG.GetComponent<ParticleSystem>() : null;
            ed.missileBoomAudio = crashG != null ? crashG.GetComponent<AudioSource>() : null;
            ed.areaCenter = CloudCenter;
            ed.seaLevel = seaY;
            if (putOut != null && putOut.catapultPoint != null) ed.avoidCenter = putOut.catapultPoint.position;
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
        }
        var gearFighters = fighters.FindAll(fg => fg != root.gameObject && !IsAwacs(fg));   // 警戒機はもともとタイヤ付きなので脚は付けない
        foreach (var fg in gearFighters) TryEnemyGear(fg, log);   // 敵の脚: 全機（無い機体にだけ作る）
        SyncEnemyGears(gearFighters, log);   // 手で直した 1 機の脚を全機にコピー
        int geared = 0;
        foreach (var fg in gearFighters) if (GatherEnemyGear(fg)) geared++;   // 飛んでいる間はしまう脚として登録
        LimitRegularFighters(fighters, log);
        ConfigureAwacs(fighters, log);
        ConfigureCarrierFighters(fighters, log);
        ConfigureBaseFighters(fighters, log);
        log.AppendLine("脚（敵）: " + geared + " 機に出し入れの動きを設定（地面の近く " + 60 + "m 以内で出す）");
        log.AppendLine("戦闘機: " + fighters.Count + " 機（元 " + orig + " 機）を敵に設定。機体の大きさ " + firstB.size.ToString("F1") + "m（z が機首の向き）。ドローンはしまいました");
    }

    // ---------------- 敵の戦闘機の大きさ ----------------
    const float FighterScale = 1f;   // 大きさは元のまま（一度半分にしたのを戻す）
    static void ScaleFighters(System.Collections.Generic.List<GameObject> fighters, Transform root, StringBuilder log)
    {
        int n = 0;
        foreach (var g in fighters)
        {
            if (g == root.gameObject || IsAwacs(g)) continue;
            Transform t = g.transform;
            float target = FighterScale / (t.parent != null ? t.parent.lossyScale.x : 1f);
            if (Mathf.Abs(t.localScale.x - target) < 0.001f) continue;   // もう半分になっている
            // 止まっている機体が浮かないよう、いちばん下の高さを保ったまま縮める
            float low0 = LowestY(t);
            Undo.RecordObject(t, "Flight setup");
            t.localScale = new Vector3(target, target, target);
            float low1 = LowestY(t);
            if (low0 < float.MaxValue && low1 < float.MaxValue) t.position += Vector3.up * (low0 - low1);
            n++;
        }
        if (n > 0) log.AppendLine("戦闘機: " + n + " 機を " + FighterScale + " 倍の大きさにしました（警戒機はそのまま）");
    }
    static float LowestY(Transform t)
    {
        float y = float.MaxValue;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            y = Mathf.Min(y, r.bounds.min.y);
        }
        return y;
    }

    // ---------------- 基地の戦闘機（military base の滑走路から発進） ----------------
    const int BaseFighterCount = 4;
    const int RegularFighterCount = 10;   // もともと飛んでいる敵の戦闘機の数（残りはしまう）
    static bool IsBaseFighter(GameObject g) { return g != null && g.name.StartsWith("BASE_"); }

    static void EnsureBaseFighters(Transform root, StringBuilder log)
    {
        Transform src = null;
        float bestOff = float.MaxValue;
        foreach (Transform ch in root)
        {
            if (!ch.name.StartsWith("EnemyFighter_")) continue;
            float off = (EnemyModelOf(ch.gameObject).position - ch.position).magnitude;
            if (off < bestOff) { bestOff = off; src = ch; }
        }
        if (src == null) { log.AppendLine("基地の戦闘機: 元にする敵の戦闘機が無い"); return; }
        Transform srcModel = EnemyModelOf(src.gameObject);
        GameObject asset = PrefabUtility.GetCorrespondingObjectFromSource(srcModel.gameObject) as GameObject;
        string ap = asset != null ? AssetDatabase.GetAssetPath(asset) : "";
        int made = 0;
        for (int i = 1; i <= BaseFighterCount; i++)
        {
            if (root.Find("BASE_" + i) != null) continue;
            var go = new GameObject("BASE_" + i);
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            go.transform.SetParent(root, false);
            go.transform.position = root.position + new Vector3(-i * 200f, 400f, 300f);
            go.transform.rotation = Quaternion.identity;
            SetWorldScaleOne(go.transform);
            go.transform.SetAsLastSibling();
            GameObject model = !string.IsNullOrEmpty(ap)
                ? (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ap), go.transform)
                : Object.Instantiate(srcModel.gameObject, go.transform);
            model.transform.localPosition = Quaternion.Inverse(src.rotation) * (srcModel.position - src.position);
            model.transform.localRotation = Quaternion.Inverse(src.rotation) * srcModel.rotation;
            model.transform.localScale = srcModel.localScale;
            made++;
        }
        if (made > 0) log.AppendLine("基地の戦闘機: " + made + " 機を作りました（" + (ap != "" ? ap : srcModel.name) + "）");
    }

    // 地面の高さ（戦闘機・自分の機体には当たらないように）
    static float GroundY(Vector3 p, float fallback)
    {
        float best = float.MinValue;
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, 400f, p.z), Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<Rigidbody>() != null) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return best > float.MinValue ? best : fallback;
    }

    static void ConfigureBaseFighters(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var bf = new System.Collections.Generic.List<EnemyDrone>();
        foreach (var g in fighters)
        {
            if (!IsBaseFighter(g)) continue;
            var ed = g.GetComponentInChildren<EnemyDrone>(true);
            if (ed != null) bf.Add(ed);
        }
        if (bf.Count == 0) return;
        bf.Sort((a, b) => string.CompareOrdinal(a.rb.name, b.rb.name));
        GameObject baseGo = GameObject.Find("military base");
        if (baseGo == null) { log.AppendLine("基地の戦闘機: military base が見つからない"); return; }
        Transform bt = baseGo.transform;
        Physics.SyncTransforms();
        // 基準: military base の中の「BaseAirfield」（最初の 1 回だけ作る。手で動かした位置・向きを使う。青い矢印 = 飛び立つ向き）
        Transform af = bt.Find("BaseAirfield");
        if (af == null)
        {
            var afGo = new GameObject("BaseAirfield");
            Undo.RegisterCreatedObjectUndo(afGo, "Flight setup");
            af = afGo.transform;
            af.SetParent(bt, false);
            Vector3 p0 = new Vector3(2620f, 0f, 411f);
            p0.y = GroundY(p0, -23f);
            af.SetPositionAndRotation(p0, Quaternion.LookRotation(Vector3.left, Vector3.up));
            log.AppendLine("基地の戦闘機: 発進の走り始め BaseAirfield を military base の中に作りました（手で動かせます。Local の青い矢印 = 飛び立つ向き）");
        }
        float hOff = 3f;
        Transform lg = bf[0].rb.transform.Find("LandingGear");
        if (lg != null)
        {
            float lowest = float.MaxValue;
            foreach (var r in lg.GetComponentsInChildren<Renderer>(true)) lowest = Mathf.Min(lowest, r.bounds.min.y);
            if (lowest < float.MaxValue) hOff = bf[0].rb.transform.position.y - lowest;
        }
        Vector3 dir = af.forward; dir.y = 0f;   // 青い矢印（Local の Z）の向きに飛び立つ
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.left;
        dir.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        Vector3 start = af.position; start.y = GroundY(start, af.position.y) + hOff + 0.3f;
        // 走る道の高さを調べて知らせる（でこぼこなら BaseAirfield を動かしてもらう）
        float gmin = float.MaxValue, gmax = float.MinValue;
        for (int k = 0; k <= 10; k++) { float gy = GroundY(af.position + dir * (25f * k), -999f); gmin = Mathf.Min(gmin, gy); gmax = Mathf.Max(gmax, gy); }
        int idx = 0;
        foreach (var ed in bf)
        {
            Undo.RecordObject(ed, "Flight setup");
            ed.carrierBased = true;
            ed.baseAirfield = true;
            ed.carrierRef = af;
            ed.carrierShip = null;
            ed.guidedMissile = false;
            ed.carrierWarnRange = 3000f;
            ed.carrierRange = 6000f;
            ed.launchDelay = 3f + idx * 5f;
            ed.deckRun = 250f;
            Transform ft = ed.rb.transform;
            if (ft.Find("ParkSet") == null)
            {
                // 走り始めの後ろに 2 列（手で動かせば、その位置を使う）
                Vector3 park = af.position - dir * (30f + 25f * (idx / 2)) + side * (idx % 2 == 0 ? -14f : 14f);
                park.y = GroundY(park, af.position.y) + hOff + 0.3f;
                Undo.RecordObject(ft, "Flight setup");
                ft.position = park;
                var mk = new GameObject("ParkSet");
                Undo.RegisterCreatedObjectUndo(mk, "Flight setup");
                mk.transform.SetParent(ft, false);
            }
            // 機首を飛び立つ向きへ（この機体は機首が -X なので 90° 回す）
            Undo.RecordObject(ft, "Flight setup");
            ft.rotation = ed.noseIsZ ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
            ed.parkLocal = af.InverseTransformPoint(ft.position);
            ed.parkRotLocal = Quaternion.Inverse(af.rotation) * ft.rotation;
            { Vector3 fw = ed.noseIsZ ? ft.forward : -ft.right; fw.y = 0f; log.AppendLine("基地の戦闘機: " + ft.name + " 止まる位置 " + ft.position.ToString("F0") + "・飛び立つ向きとの差 " + Vector3.Angle(fw, dir).ToString("F0") + "°"); }
            ed.deckStartLocal = af.InverseTransformPoint(start);
            ed.deckDirLocal = af.InverseTransformDirection(dir);
            if (ed.target != null)
            {
                Undo.RecordObject(ed.target, "Flight setup");
                ed.target.kind = 0;
                UdonSharpEditorUtility.CopyProxyToUdon(ed.target);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
            idx++;
        }
        log.AppendLine("基地の戦闘機: " + bf.Count + " 機を基地に配置（走り始め " + start.ToString("F0") + "・向き " + dir.ToString("F2") + "。3km で発進・6km の中だけ飛ぶ・観光モードでは飛ばない）");
        log.AppendLine("基地の戦闘機: 滑走する 250m の地面の高さ " + gmin.ToString("F1") + "〜" + gmax.ToString("F1") + (gmax - gmin > 3f ? "  ※ でこぼこがあります。BaseAirfield を平らな所へ動かしてください" : "（平ら）"));
    }

    // もともと飛んでいる敵の戦闘機を RegularFighterCount 機に（残りはしまう。編隊の戦闘機は残す）
    static void LimitRegularFighters(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var reg = fighters.FindAll(g => g.name.StartsWith("EnemyFighter_"));
        reg.Sort((a, b) => string.CompareOrdinal(a.name, b.name));   // 編隊（ConfigureAwacs）と同じ順。先頭が編隊になる
        int on = 0, off = 0;
        for (int i = 0; i < reg.Count; i++)
        {
            bool keep = i < RegularFighterCount;
            Undo.RecordObject(reg[i], "Flight setup");
            reg[i].SetActive(keep);
            if (keep) on++; else off++;
        }
        log.AppendLine("戦闘機: もともと飛んでいる戦闘機を " + on + " 機に（" + off + " 機はしまいました）");
    }

    // 建物を壊せるようにする（壊れたら燃えるがれき・復活しない）。kind: 6 = 基地の建物（全部で 100 点）, -1 = 得点なし
    static FlightTarget MakeBuildingTarget(GameObject go, int kind, int hp, Material fxMat, Material smokeMat, AudioClip boom, Material charred, StringBuilder log)
    {
        Transform tr = go.transform;
        foreach (var nm in new[] { "Wreck", "BuildingBoom" })
        {
            Transform old; int guard = 0;
            while ((old = tr.Find(nm)) != null && guard++ < 10) Undo.DestroyObjectImmediate(old.gameObject);
        }
        var rends = new System.Collections.Generic.List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) rends.Add(r);
        if (rends.Count == 0) return null;
        // 大きさ: スキンメッシュの bounds は大きすぎることがあるので、メッシュそのものの大きさから測る
        bool hasB = false; Bounds b = new Bounds(tr.position, Vector3.zero);
        foreach (var r in rends)
        {
            Bounds rb2 = r.bounds;
            var smr = r as SkinnedMeshRenderer;
            if (smr != null && smr.sharedMesh != null) rb2 = XformBounds(smr.transform, smr.sharedMesh.bounds);
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) rb2 = XformBounds(r.transform, mf.sharedMesh.bounds);
            if (!hasB) { b = rb2; hasB = true; } else b.Encapsulate(rb2);
        }
        // 前に付けた大きすぎる箱の当たり判定は付け直す
        foreach (var oldBc in go.GetComponents<BoxCollider>()) { Vector3 ws = Vector3.Scale(oldBc.size, tr.lossyScale); if (ws.x > b.size.x * 1.5f || ws.z > b.size.z * 1.5f) Undo.DestroyObjectImmediate(oldBc); }
        bool big = Mathf.Max(b.size.x, b.size.z) > 25f;
        var cols = new System.Collections.Generic.List<Collider>();
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) cols.Add(c);
        if (cols.Count == 0)
        {
            // 当たり判定が無ければ、大きさに合わせた箱を付ける（弾・ミサイルが当たるように）
            var bc = Undo.AddComponent<BoxCollider>(go);
            bc.center = tr.InverseTransformPoint(b.center);
            Vector3 ls = tr.InverseTransformVector(b.size);
            bc.size = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            cols.Add(bc);
        }
        var ft = go.GetComponent<FlightTarget>();
        if (ft == null) ft = UdonSharpUndo.AddComponent<FlightTarget>(go);
        Undo.RecordObject(ft, "Flight setup");
        ft.kind = kind;
        ft.maxHp = hp;
        ft.missileDamage = 15;
        ft.noRespawn = true;
        ft.respawnTime = 99999f;
        ft.body = null;
        ft.renderers = rends.ToArray();
        ft.colliders = cols.ToArray();

        // 爆発（壊れた瞬間）
        var boomPs = MakePS("BuildingBoom", tr, fxMat);
        boomPs.transform.position = b.center;
        var bm = boomPs.main;
        bm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        bm.startSpeed = new ParticleSystem.MinMaxCurve(8f, 25f);
        bm.startSize = new ParticleSystem.MinMaxCurve(6f, 14f);
        bm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.1f), new Color(1f, 0.85f, 0.4f));
        bm.maxParticles = 200;
        var be = boomPs.emission;
        be.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
        var bsh = boomPs.shape; bsh.enabled = true; bsh.shapeType = ParticleSystemShapeType.Sphere; bsh.radius = Mathf.Max(b.extents.x, b.extents.z) * 0.5f;
        SetFade(boomPs, new Color(1f, 0.8f, 0.4f), new Color(0.3f, 0.1f, 0.05f));
        ft.explosionFx = boomPs;
        var au = Undo.AddComponent<AudioSource>(boomPs.gameObject);
        au.clip = boom; au.playOnAwake = false; au.spatialBlend = 1f; au.minDistance = 40f; au.maxDistance = 2500f; au.volume = 1f;
        au.rolloffMode = AudioRolloffMode.Linear;
        ft.explosionAudio = au;

        // がれき・炎・黒煙（壊れた後だけ出る）
        var wreck = new GameObject("Wreck");
        Undo.RegisterCreatedObjectUndo(wreck, "Flight setup");
        wreck.transform.SetParent(tr, false);
        wreck.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
        wreck.transform.rotation = Quaternion.identity;
        SetWorldScaleOne(wreck.transform);
        var rnd = new System.Random(go.name.GetHashCode());
        float sx = b.size.x, sz = b.size.z, hy = b.size.y;
        int pieces = big ? 14 : 7;
        for (int k = 0; k < pieces; k++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "Rubble";
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.transform.SetParent(wreck.transform, false);
            float fx = (float)rnd.NextDouble() - 0.5f, fz = (float)rnd.NextDouble() - 0.5f;
            bool beam = k % 4 == 0;
            Vector3 sc = beam
                ? new Vector3(0.6f, 0.6f, Mathf.Max(sx, sz) * (0.3f + 0.3f * (float)rnd.NextDouble()))
                : new Vector3(sx * (0.12f + 0.2f * (float)rnd.NextDouble()), hy * (0.05f + 0.12f * (float)rnd.NextDouble()), sz * (0.12f + 0.2f * (float)rnd.NextDouble()));
            c.transform.localScale = sc;
            c.transform.localPosition = new Vector3(fx * sx * 0.8f, sc.y * 0.35f, fz * sz * 0.8f);
            c.transform.localRotation = Quaternion.Euler((float)rnd.NextDouble() * (beam ? 40f : 15f) - (beam ? 20f : 7f), (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 20f - 10f);
            var mr = c.GetComponent<MeshRenderer>();
            mr.sharedMaterial = charred;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        int fires = big ? 3 : 1;
        float fireSize = Mathf.Clamp(Mathf.Max(sx, sz) * 0.12f, 3f, 9f);
        for (int k = 0; k < fires; k++)
        {
            Vector3 lp = new Vector3(((float)rnd.NextDouble() - 0.5f) * sx * 0.6f, 1f, ((float)rnd.NextDouble() - 0.5f) * sz * 0.6f);
            var fire = MakePS("Fire" + k, wreck.transform, fxMat);
            fire.transform.localPosition = lp;
            var fm = fire.main;
            fm.loop = true; fm.playOnAwake = true;
            fm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            fm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            fm.startSize = new ParticleSystem.MinMaxCurve(fireSize * 0.6f, fireSize);
            fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.9f));
            fm.gravityModifier = -0.3f;
            fm.maxParticles = 200;
            var fe = fire.emission; fe.rateOverTime = 25f;
            var fsh = fire.shape; fsh.enabled = true; fsh.shapeType = ParticleSystemShapeType.Sphere; fsh.radius = fireSize * 0.4f;
            var fsol = fire.sizeOverLifetime; fsol.enabled = true; fsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            SetFade(fire, new Color(1f, 0.7f, 0.3f), new Color(0.6f, 0.1f, 0.05f));

            var smoke = MakePS("Smoke" + k, wreck.transform, smokeMat);
            smoke.transform.localPosition = lp + Vector3.up * fireSize;
            var sm = smoke.main;
            sm.loop = true; sm.playOnAwake = true;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
            sm.startSize = new ParticleSystem.MinMaxCurve(fireSize * 1.5f, fireSize * 2.5f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.08f, 0.08f, 0.08f, 0.75f), new Color(0.25f, 0.25f, 0.25f, 0.6f));
            sm.gravityModifier = -0.05f;
            sm.maxParticles = 250;
            var se = smoke.emission; se.rateOverTime = 6f;
            var ssh = smoke.shape; ssh.enabled = true; ssh.shapeType = ParticleSystemShapeType.Sphere; ssh.radius = fireSize * 0.5f;
            var ssol = smoke.sizeOverLifetime; ssol.enabled = true; ssol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.5f));
            SetFade(smoke, new Color(0.2f, 0.2f, 0.2f), new Color(0.5f, 0.5f, 0.5f));
        }
        wreck.SetActive(false);
        ft.wreck = wreck;
        UdonSharpEditorUtility.CopyProxyToUdon(ft);
        if (log != null) log.AppendLine("建物: " + go.name + "（大きさ " + b.size.ToString("F0") + "・耐久 " + ft.maxHp + "・当たり判定 " + cols.Count + " 個）");
        return ft;
    }

    // ---------------- 基地の建物（House_2・Old Hangar）: 壊れたら燃えるがれき・復活しない ----------------
    static System.Collections.Generic.List<FlightTarget> s_baseBuildings = new System.Collections.Generic.List<FlightTarget>();
    static void SetupBaseBuildings(StringBuilder log)
    {
        s_baseBuildings.Clear();
        GameObject baseGo = GameObject.Find("military base");
        if (baseGo == null) { log.AppendLine("基地の建物: military base が見つからない"); return; }
        Vector3 bp = baseGo.transform.position;
        var list = new System.Collections.Generic.List<GameObject>();
        Transform house = FindDeepName(baseGo.transform, "House_2");
        if (house != null) list.Add(house.gameObject);
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (!t.name.StartsWith("Old Hangar")) continue;
            if (t.parent != null && t.parent.name.StartsWith("Old Hangar")) continue;
            Vector3 d = t.position - bp; d.y = 0f;
            if (d.magnitude > 1500f) continue;
            list.Add(t.gameObject);
        }
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        Material smokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
        if (smokeMat == null) smokeMat = fxMat;
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        Material charred = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/Rubble.mat");
        if (charred == null)
        {
            charred = new Material(Shader.Find("Standard"));
            charred.color = new Color(0.16f, 0.14f, 0.13f);
            charred.SetFloat("_Glossiness", 0.05f);
            AssetDatabase.CreateAsset(charred, FxDir + "/Rubble.mat");
        }
        foreach (var go in list)
        {
            var ft = MakeBuildingTarget(go, 6, 30, fxMat, smokeMat, boom, charred, log);   // どちらもミサイル 2 発
            if (ft != null) s_baseBuildings.Add(ft);
        }
        log.AppendLine("基地の建物: " + s_baseBuildings.Count + " 棟を壊せるように（壊れたら燃えるがれき・復活しない・全部壊すと 100 点）");
    }

    // 人が住む島・アストラル島の建物: 壊せる（得点なし・復活しない）。敵がいない所で撃っても攻撃開始にはならず、警告は戦艦などに見つかった時に始まる
    static void SetupTownBuildings(StringBuilder log)
    {
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        Material smokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
        if (smokeMat == null) smokeMat = fxMat;
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        Material charred = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/Rubble.mat");
        foreach (var isl in new[] { new[] { "人がいる島", "人が住む島" }, new[] { "Astral island(アストラル）", "アストラル島" } })
        {
            GameObject root = GameObject.Find(isl[0]);
            if (root == null) { log.AppendLine("建物: " + isl[0] + " が見つからない"); continue; }
            int n = 0, small = 0;
            foreach (Transform ch in root.transform)
            {
                string nm = ch.name;
                bool house = nm.StartsWith("BigHousePrefab") || nm.StartsWith("OldHousePrefab") || (nm.StartsWith("House") && nm.Length > 5 && char.IsDigit(nm[5]));
                if (!house) continue;
                Bounds bb;
                bool isBig = MeshBounds(ch, "", out bb) && Mathf.Max(bb.size.x, bb.size.z) > 25f;
                var ft = MakeBuildingTarget(ch.gameObject, -1, isBig ? 30 : 15, fxMat, smokeMat, boom, charred, null);   // 大きい家はミサイル 2 発、小さい家は 1 発
                if (ft == null) continue;
                n++; if (!isBig) small++;
            }
            log.AppendLine("建物: " + isl[1] + " の建物 " + n + " 棟を壊せるように（小さい家 " + small + " 棟はミサイル 1 発、大きい家は 2 発。得点なし・復活しない）");
        }
    }

    static Bounds XformBounds(Transform t, Bounds lb)
    {
        Vector3 c = lb.center, e = lb.extents;
        Bounds w = new Bounds(t.TransformPoint(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
            w.Encapsulate(t.TransformPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
        return w;
    }

    static Transform FindDeepName(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform ch in root) { var r = FindDeepName(ch, name); if (r != null) return r; }
        return null;
    }

    // ---------------- アストラル空港 ----------------
    static void SetupAstralAirport(StringBuilder log)
    {
        GameObject isl = GameObject.Find("Astral island(アストラル）");
        if (isl == null) { log.AppendLine("アストラル空港: Astral island(アストラル） が見つからない"); return; }
        Transform ap = isl.transform.Find("AstralAirport");
        if (ap == null)
        {
            // 置いてある飛行機（ComPlaneV1）のまん中 = エプロン
            Vector3 sum = Vector3.zero; int k = 0;
            foreach (Transform ch in isl.transform) if (ch.name.StartsWith("ComPlaneV1")) { sum += ch.position; k++; }
            Vector3 p0 = k > 0 ? sum / k : isl.transform.position;
            var g = new GameObject("AstralAirport");
            Undo.RegisterCreatedObjectUndo(g, "Flight setup");
            ap = g.transform;
            ap.SetParent(isl.transform, false);
            ap.position = p0;
            log.AppendLine("アストラル空港: 空港の中心 AstralAirport を " + p0.ToString("F0") + " に作りました（手で動かせます。ここから 900m 以内に止まると補給）");
        }
        AudioClip[] args = new AudioClip[7];
        for (int i = 0; i < 7; i++) args[i] = LoadClip("astral_arg" + (i + 1) + ".wav");
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.astralAirport = ap;
            c.astralIntro = LoadClip("astral_intro.wav");
            c.astralClear = LoadClip("astral_clear.wav");
            c.astralWelcome = LoadClip("astral_welcome.wav");
            c.astralArgs = args;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        var gm = Object.FindObjectOfType<GameMode>(true);
        if (gm != null) { Undo.RecordObject(gm, "Flight setup"); gm.astralAirport = ap; UdonSharpEditorUtility.CopyProxyToUdon(gm); }
        log.AppendLine("アストラル空港: " + n + " 機に設定（5km で管制室の無線・2.5km 以内でフラップを下げてゆっくり飛ぶと着陸の無線・言い合い 7 種）" + (args[0] == null ? "  ※ 音声が見つからない" : "") + (gm == null ? "  ※ GameMode が無い" : ""));
    }

    // ---------------- 速さ（本物の飛行機に近づける） ----------------
    // プレイヤー: ジェット機風（速くても推力が落ちない）。最高速度 約 170m/s（約 600km/h）
    //   最高速度 = 推力と空気抵抗が釣り合う速さ: 0.5 × 1.225 × 170² × 16m² × CD0 0.035 ≒ 9900N
    // 敵: 約 140m/s（約 500km/h）。警戒機 約 100m/s。旋回・ミサイルも速さに合わせる
    static void SetupSpeeds(StringBuilder log)
    {
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.propMaxSpeed = 0f;          // ジェット扱い
            c.maxThrust = 10000f;
            c.fuelRefSpeed = 170f;
            c.pitchRefSpeed = 75f;        // これより速いと G を一定（約 9G）に
            c.rollRefSpeed = 90f;
            c.missileAccel = 150f;        // ミサイルは約 350m/s まで加速
            c.missileBurnTime = 3f;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        int e = 0;
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true))
        {
            Undo.RecordObject(ed, "Flight setup");
            ed.speed = ed.isAwacs ? 100f : 140f;
            ed.maxLateralAcc = ed.isAwacs ? 12f : 40f;    // 旋回の半径 約 500m（戦闘機）
            ed.maxVerticalAcc = ed.isAwacs ? 6f : 15f;
            ed.maxAlongAcc = ed.isAwacs ? 6f : 15f;
            ed.rollRate = 3f;
            ed.fireRange = 700f;
            ed.missileMaxSpeed = 450f;
            ed.missileAccel = 150f;
            ed.missileMinRange = 600f;
            ed.missileMaxRange = 3500f;
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
            e++;
        }
        log.AppendLine("速さ: プレイヤーの機体 " + n + " 機をジェット機風に（最高 約 600km/h・G リミッター）。敵 " + e + " 機を 約 500km/h（警戒機 約 360km/h）に");
    }

    // 基地の守り: アインの警告・ミサイル迎撃の声・基地の大砲を対空砲に
    static void SetupBaseDefense(StringBuilder log)
    {
        GameObject baseGo = GameObject.Find("military base");
        AudioClip[] warn = { LoadClip("ain_warn1.wav"), LoadClip("ain_warn2.wav") };
        AudioClip[] thanks = { LoadClip("ain_thanks1.wav"), LoadClip("ain_thanks2.wav") };
        AudioClip[] icpt = { LoadClip("voice_intercepted1.wav"), LoadClip("voice_intercepted2.wav") };
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.baseCenter = baseGo != null ? baseGo.transform : null;
            c.baseWarnRadius = 3000f;
            c.ainWarn = warn;
            c.ainThanks = thanks;
            c.voiceIntercepted = icpt;
            c.baseBuildings = s_baseBuildings.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        log.AppendLine("基地: アインの警告（3km）・迎撃の声を " + n + " 機に設定" + (warn[0] == null || icpt[0] == null ? "  ※ 音声が見つからない" : ""));
        if (baseGo == null) { log.AppendLine("基地: military base が見つからない"); return; }
        GameMode gm = Object.FindObjectOfType<GameMode>(true);
        int aa = 0;
        foreach (var fc in baseGo.GetComponentsInChildren<FixedCannon>(true))
        {
            Undo.RecordObject(fc, "Flight setup");
            fc.antiAir = true;
            fc.targetMissiles = true;
            fc.gameMode = gm;
            UdonSharpEditorUtility.CopyProxyToUdon(fc);
            aa++;
        }
        log.AppendLine("基地: 大砲 " + aa + " 門を対空砲に（ミサイルも狙う・観光モードでは撃たない）");
    }

    // ---------------- 空母の戦闘機 ----------------
    const int CarrierFighterCount = 4;
    static bool IsCarrierFighter(GameObject g) { return g != null && g.name.StartsWith("CARRIER_"); }

    // 空母の戦闘機 4 機（enemy aircraft の中に CARRIER_1〜4）。見た目は敵の戦闘機と同じ FA_N26 を水色に
    static void EnsureCarrierFighters(Transform root, StringBuilder log)
    {
        // 元にする戦闘機: 根元と見た目が同じ場所にある物（EnemyFighter_1 は見た目が根元から 6km 離れているので使わない）
        Transform src = null;
        float bestOff = float.MaxValue;
        foreach (Transform ch in root)
        {
            if (!ch.name.StartsWith("EnemyFighter_")) continue;
            float off = (EnemyModelOf(ch.gameObject).position - ch.position).magnitude;
            if (off < bestOff) { bestOff = off; src = ch; }
        }
        if (src == null) { log.AppendLine("空母の戦闘機: 元にする敵の戦闘機が無い"); return; }
        Transform srcModel = EnemyModelOf(src.gameObject);
        // 前に作った空母の戦闘機の見た目が、根元から遠くにずれていたら直す（甲板の上に置き直す）
        for (int i = 1; i <= CarrierFighterCount; i++)
        {
            Transform cf = root.Find("CARRIER_" + i);
            if (cf == null) continue;
            Transform cm = EnemyModelOf(cf.gameObject);
            if (cm == cf || (cm.position - cf.position).magnitude < 50f) continue;
            Undo.RecordObject(cm, "Flight setup");
            cm.localPosition = Quaternion.Inverse(src.rotation) * (srcModel.position - src.position);
            cm.localRotation = Quaternion.Inverse(src.rotation) * srcModel.rotation;
            Transform mk = cf.Find("ParkSet");
            if (mk != null) Undo.DestroyObjectImmediate(mk.gameObject);   // 甲板の上に置き直す
            log.AppendLine("空母の戦闘機: " + cf.name + " の見た目が根元から離れていたので直しました");
        }
        GameObject asset = PrefabUtility.GetCorrespondingObjectFromSource(srcModel.gameObject) as GameObject;
        string ap = asset != null ? AssetDatabase.GetAssetPath(asset) : "";
        int made = 0;
        for (int i = 1; i <= CarrierFighterCount; i++)
        {
            if (root.Find("CARRIER_" + i) != null) continue;
            var go = new GameObject("CARRIER_" + i);
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            go.transform.SetParent(root, false);
            go.transform.position = root.position + new Vector3(-i * 200f, 300f, 0f);
            go.transform.rotation = Quaternion.identity;
            SetWorldScaleOne(go.transform);
            go.transform.SetAsLastSibling();
            GameObject model = !string.IsNullOrEmpty(ap)
                ? (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ap), go.transform)
                : Object.Instantiate(srcModel.gameObject, go.transform);
            model.transform.localPosition = Quaternion.Inverse(src.rotation) * (srcModel.position - src.position);
            model.transform.localRotation = Quaternion.Inverse(src.rotation) * srcModel.rotation;
            model.transform.localScale = srcModel.localScale;
            made++;
        }
        if (made > 0) log.AppendLine("空母の戦闘機: " + made + " 機を作りました（" + (ap != "" ? ap : srcModel.name) + "）");
    }

    // 水色に塗る・空母の甲板の位置・追尾ミサイル
    static void ConfigureCarrierFighters(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var cv = new System.Collections.Generic.List<EnemyDrone>();
        foreach (var g in fighters)
        {
            if (!IsCarrierFighter(g)) continue;
            var ed = g.GetComponentInChildren<EnemyDrone>(true);
            if (ed != null) cv.Add(ed);
        }
        if (cv.Count == 0) return;
        cv.Sort((a, b) => string.CompareOrdinal(a.rb.name, b.rb.name));
        WarShip carrier = null;
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true)) if (ws.target != null && ws.target.kind == 2) { carrier = ws; break; }
        if (carrier == null || carrier.model == null) { log.AppendLine("空母の戦闘機: 空母（S10）が見つからない"); return; }

        // 甲板の高さ: 見た目に一時的にメッシュの当たり判定を付けて、上から調べる
        Transform cm = carrier.model.transform;
        Bounds bd;
        if (!MeshBounds(cm, "", out bd)) return;
        bool alongX = bd.size.x >= bd.size.z;
        Vector3 L = alongX ? Vector3.right : Vector3.forward;
        Vector3 Sd = alongX ? Vector3.forward : Vector3.right;
        float len = alongX ? bd.size.x : bd.size.z, wid = alongX ? bd.size.z : bd.size.x;
        var temp = new System.Collections.Generic.List<MeshCollider>();
        foreach (var mf in cm.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
            temp.Add(mf.gameObject.AddComponent<MeshCollider>());
        }
        Physics.SyncTransforms();
        var hs = new System.Collections.Generic.List<float>();
        for (int k = -4; k <= 4; k++)
        {
            Vector3 o = bd.center + L * (k / 10f * len) + Vector3.up * 200f;
            foreach (var h in Physics.RaycastAll(o, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider is MeshCollider && temp.Contains((MeshCollider)h.collider)) { hs.Add(h.point.y); break; }
        }
        foreach (var t in temp) Object.DestroyImmediate(t);
        hs.Sort();
        float deckY = hs.Count > 0 ? hs[hs.Count / 2] : bd.min.y + bd.size.y * 0.4f;   // 真ん中の値（艦橋の上は除く）

        // 機体の中心からタイヤの下までの高さ（甲板の上に置く高さ）
        float hOff = 3f;
        var ed0 = cv[0];
        Transform lg = ed0.rb.transform.Find("LandingGear");
        if (lg != null)
        {
            float lowest = float.MaxValue;
            foreach (var r in lg.GetComponentsInChildren<Renderer>(true)) lowest = Mathf.Min(lowest, r.bounds.min.y);
            if (lowest < float.MaxValue) hOff = ed0.rb.transform.position.y - lowest;
        }

        Transform cr = carrier.transform;
        Vector3 deckStart = bd.center - L * len * 0.35f;
        deckStart.y = deckY + hOff + 0.3f;
        // 発艦の走り始め: 空母の中の「LaunchStart」（最初の 1 回だけ作る。手で動かした位置・向きを使う。青い矢印 = 飛び立つ向き）
        Transform ls = cr.Find("LaunchStart");
        if (ls == null)
        {
            var lsGo = new GameObject("LaunchStart");
            Undo.RegisterCreatedObjectUndo(lsGo, "Flight setup");
            ls = lsGo.transform;
            ls.SetParent(cr, false);
            ls.SetPositionAndRotation(deckStart, Quaternion.LookRotation(L, Vector3.up));
            log.AppendLine("空母の戦闘機: 発艦の走り始め LaunchStart を空母（" + cr.name + "）の中に作りました（手で動かせます）");
        }
        deckStart = ls.position;
        Vector3 launchDir = ls.forward; launchDir.y = 0f;
        if (launchDir.sqrMagnitude < 0.001f) launchDir = L;
        launchDir.Normalize();
        Material blue = CarrierBlueMat(fighters);
        int idx = 0;
        foreach (var ed in cv)
        {
            Undo.RecordObject(ed, "Flight setup");
            ed.carrierBased = true;
            ed.carrierRef = cr;
            ed.carrierShip = carrier;
            ed.guidedMissile = true;
            ed.missileTurnRate = 40f;        // よく曲がる追尾ミサイル（ほかの戦闘機は 14）
            ed.missileBurn = 8f;
            ed.missileInterval = 10f;
            ed.carrierWarnRange = 4000f;
            ed.carrierRange = 6000f;
            ed.launchDelay = idx * 4f;      // 4 秒おきに順番に発艦
            ed.deckRun = len * 0.6f;
            // 止まる場所: 最初の 1 回だけ甲板の前半に左右互い違いで置く。その後は、シーンで手で動かした位置・向きをそのまま使う
            Transform ft = ed.rb.transform;
            if (ft.Find("ParkSet") == null)
            {
                Vector3 park = bd.center + L * len * (0.05f + 0.1f * idx) + Sd * wid * (idx % 2 == 0 ? -0.22f : 0.22f);
                park.y = deckY + hOff + 0.3f;
                Undo.RecordObject(ft, "Flight setup");
                ft.SetPositionAndRotation(park, Quaternion.LookRotation(L, Vector3.up));
                var mk = new GameObject("ParkSet");   // 置いた印（これがあれば手で動かした位置を使う）
                Undo.RegisterCreatedObjectUndo(mk, "Flight setup");
                mk.transform.SetParent(ft, false);
            }
            ed.parkLocal = cr.InverseTransformPoint(ft.position);
            ed.parkRotLocal = Quaternion.Inverse(cr.rotation) * ft.rotation;
            ed.deckStartLocal = cr.InverseTransformPoint(deckStart);
            ed.deckDirLocal = cr.InverseTransformDirection(launchDir);
            if (ed.target != null)
            {
                Undo.RecordObject(ed.target, "Flight setup");
                ed.target.kind = 5;          // 空母の戦闘機（3 点）
                ed.target.respawnTime = 40f;
                UdonSharpEditorUtility.CopyProxyToUdon(ed.target);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
            // 水色に塗る（脚は除く）
            if (blue != null)
                foreach (var r in EnemyModelOf(ed.rb.gameObject).GetComponentsInChildren<MeshRenderer>(true))
                {
                    var ms = r.sharedMaterials;
                    for (int m = 0; m < ms.Length; m++) ms[m] = blue;
                    Undo.RecordObject(r, "Flight setup");
                    r.sharedMaterials = ms;
                }
            idx++;
        }
        log.AppendLine("空母の戦闘機: " + cv.Count + " 機を空母 " + carrier.model.name + " に配置（甲板の高さ " + deckY.ToString("F1") + "・長さ " + len.ToString("F0") + "m。4km で発艦・6km の中だけ飛ぶ・誰もいなければ着艦・追尾ミサイル）");
        if (hs.Count == 0) log.AppendLine("空母の戦闘機: ※ 甲板の高さを調べられなかったので推定です。甲板に埋まる・浮く場合は教えてください");
    }

    // 敵の戦闘機の色を水色にしたマテリアル
    static Material CarrierBlueMat(System.Collections.Generic.List<GameObject> fighters)
    {
        Material srcM = null;
        foreach (var g in fighters)
        {
            if (IsAwacs(g) || IsCarrierFighter(g) || IsBaseFighter(g)) continue;
            var r = EnemyModelOf(g).GetComponentInChildren<MeshRenderer>(true);
            if (r != null && r.sharedMaterial != null) { srcM = r.sharedMaterial; break; }
        }
        if (srcM == null) return null;
        string path = FxDir + "/CarrierFighterBlue.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(srcM); AssetDatabase.CreateAsset(m, path); }
        else m.CopyPropertiesFromMaterial(srcM);
        m.shader = srcM.shader;
        Color blue = new Color(0.55f, 0.82f, 1f, 1f);   // 日本の F-2 のような水色っぽい青
        if (m.HasProperty("_Color")) m.SetColor("_Color", blue);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", blue);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------- 警戒機 ----------------
    const string AwacsPrefab = "Assets/Alstra Infinite/Planes LowPoly/Prefabs/ComPlaneV2.prefab";
    const float AwacsScale = 15f;   // 基地の警戒機と同じ大きさ
    static bool IsAwacs(GameObject g) { return g != null && g.name.StartsWith("AWACS_"); }

    // 飛ぶ警戒機を 3 機（enemy aircraft の中に AWACS_1〜3）。無ければ作る
    static void EnsureFlyingAwacs(Transform root, StringBuilder log)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AwacsPrefab);
        if (asset == null) { log.AppendLine("警戒機: " + AwacsPrefab + " が見つからない"); return; }
        int made = 0;
        for (int i = 1; i <= 3; i++)
        {
            if (root.Find("AWACS_" + i) != null) continue;
            var go = new GameObject("AWACS_" + i);
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            go.transform.SetParent(root, false);
            go.transform.position = root.position + new Vector3(i * 300f, 300f, 0f);
            go.transform.rotation = Quaternion.identity;
            SetWorldScaleOne(go.transform);
            go.transform.SetAsLastSibling();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * AwacsScale;
            made++;
        }
        if (made > 0) log.AppendLine("警戒機: 飛ぶ警戒機を " + made + " 機作りました（機首が +Z の向きの前提。逆なら教えてください）");
    }

    // 飛ぶ警戒機: 攻撃しない・ゆっくり・探す範囲 2 倍・戦闘機 2 機と編隊
    static void ConfigureAwacs(System.Collections.Generic.List<GameObject> fighters, StringBuilder log)
    {
        var awacs = new System.Collections.Generic.List<EnemyDrone>();
        var wings = new System.Collections.Generic.List<EnemyDrone>();
        foreach (var g in fighters)
        {
            var ed = g.GetComponentInChildren<EnemyDrone>(true);
            if (ed == null) continue;
            Undo.RecordObject(ed, "Flight setup");
            ed.isAwacs = false; ed.formationLeader = null; ed.wingmen = null;
            if (IsAwacs(g)) awacs.Add(ed); else if (!IsCarrierFighter(g) && !IsBaseFighter(g) && g.activeSelf) wings.Add(ed);   // 空母の戦闘機は編隊に入れない
        }
        wings.Sort((a, b) => string.CompareOrdinal(a.rb.name, b.rb.name));
        int wi = 0;
        foreach (var aw in awacs)
        {
            aw.isAwacs = true;
            aw.speed = 32f;                     // 戦闘機（44）よりゆっくり
            aw.detectRange = 6000f;             // 戦闘機（3000）の 2 倍
            aw.loseRange = 8000f;
            aw.minAlt = 260f;
            aw.maxAlt = 420f;
            aw.maxLateralAcc = 2.5f;            // 大きな機体なのでゆるく曲がる
            if (aw.target != null)
            {
                Undo.RecordObject(aw.target, "Flight setup");
                aw.target.kind = 4;             // 警戒機（4 点・飛行機として数える）
                aw.target.maxHp = 30;
                aw.target.respawnTime = 60f;
                UdonSharpEditorUtility.CopyProxyToUdon(aw.target);
            }
            // 編隊の位置: 警戒機の大きさから（左右に翼幅くらい・後ろに機体の長さの 0.6 倍）
            Bounds b;
            float side = 60f, back = 50f;
            if (MeshBounds(EnemyModelOf(aw.rb.gameObject), "", out b)) { side = Mathf.Max(b.size.x, b.size.z) * 0.9f; back = Mathf.Max(b.size.x, b.size.z) * 0.6f; }
            var wm = new System.Collections.Generic.List<EnemyDrone>();
            for (int k = 0; k < 2 && wi < wings.Count; k++, wi++)
            {
                EnemyDrone w = wings[wi];
                w.formationLeader = aw;
                w.formationOffset = new Vector3(k == 0 ? -side : side, 0f, -back);
                wm.Add(w);
                UdonSharpEditorUtility.CopyProxyToUdon(w);
            }
            aw.wingmen = wm.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(aw);
            log.AppendLine("警戒機: " + aw.rb.name + " ← 編隊の戦闘機 " + (wm.Count > 0 ? wm[0].rb.name : "なし") + (wm.Count > 1 ? "・" + wm[1].rb.name : "") + "（左右 " + side.ToString("F0") + "m・後ろ " + back.ToString("F0") + "m）");
        }
        foreach (var w in wings) UdonSharpEditorUtility.CopyProxyToUdon(w);
    }

    // 基地に止まっている警戒機: 機銃・ミサイルで壊せる（2 点・60 秒で復活）
    static void SetupParkedAwacs(StringBuilder log)
    {
        Transform grp = FindByNameContains("警戒機");
        if (grp == null) { log.AppendLine("警戒機: 基地の「警戒機」が見つからない"); return; }
        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        int n = 0;
        foreach (Transform pl in grp)
        {
            if (pl.GetComponentInChildren<Renderer>(true) == null) continue;
            var ft = pl.GetComponent<FlightTarget>();
            if (ft == null) ft = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(pl.gameObject);
            Undo.RecordObject(ft, "Flight setup");
            var rends = new System.Collections.Generic.List<Renderer>();
            foreach (var r in pl.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) rends.Add(r);
            var cols = new System.Collections.Generic.List<Collider>();
            foreach (var c in pl.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) cols.Add(c);
            if (cols.Count == 0)
            {
                Bounds wb;
                if (MeshBounds(pl, "", out wb))
                {
                    var bc = Undo.AddComponent<BoxCollider>(pl.gameObject);
                    bc.center = pl.InverseTransformPoint(wb.center);
                    Vector3 sz = pl.InverseTransformVector(wb.size);
                    bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
                    cols.Add(bc);
                }
            }
            ft.renderers = rends.ToArray();
            ft.colliders = cols.ToArray();
            ft.body = null;
            ft.kind = 0;            // 飛行機（2 点）
            ft.maxHp = 15;
            ft.missileDamage = 15;
            ft.respawnTime = 60f;   // しばらくして復活
            Transform oldEx = pl.Find("Explosion");
            if (oldEx != null) Undo.DestroyObjectImmediate(oldEx.gameObject);
            Bounds eb;
            var ex = MakePS("Explosion", pl, fxMat);
            if (MeshBounds(pl, "", out eb)) ex.transform.position = eb.center;
            ConfigureExplosion(ex, 6f, 90);
            var exAu = ex.gameObject.AddComponent<AudioSource>();
            exAu.clip = boom;
            exAu.playOnAwake = false;
            exAu.spatialBlend = 1f;
            exAu.minDistance = 30f;
            exAu.maxDistance = 2500f;
            ft.explosionFx = ex;
            ft.explosionAudio = exAu;
            UdonSharpEditorUtility.CopyProxyToUdon(ft);
            n++;
        }
        log.AppendLine("警戒機: 基地の警戒機 " + n + " 機を壊せるように（2 点・60 秒で復活）");
    }

    // ---------------- レーダーに出す物 ----------------
    static void SetupRadarTracks(StringBuilder log)
    {
        var tracks = new System.Collections.Generic.List<Transform>();
        var types = new System.Collections.Generic.List<int>();
        var fts = new System.Collections.Generic.List<FlightTarget>();
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true))
        {
            if (ed.rb == null || !ed.rb.gameObject.activeInHierarchy) continue;
            tracks.Add(ed.rb.transform); types.Add(0); fts.Add(ed.target);
        }
        var activeShips = new System.Collections.Generic.List<WarShip>();
        // 基地のそばに止まっている艦隊は、レーダーに出さない（5 = 出さない。撃墜は数える）
        GameObject baseGo = GameObject.Find("military base");
        Vector3 basePos = baseGo != null ? baseGo.transform.position : new Vector3(2719f, 0f, 441f);
        int hiddenShips = 0;
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
        {
            if (!ws.gameObject.activeInHierarchy) continue;   // しまった船（Colonial Ship など）は出さない
            Vector3 db = (ws.model != null ? ws.model.transform.position : ws.transform.position) - basePos; db.y = 0f;   // 見た目の船の位置で測る
            bool baseFleet = !ws.roaming && ws.leader == null && db.magnitude < 2500f;
            if (baseFleet) hiddenShips++;
            tracks.Add(ws.transform); types.Add(baseFleet ? 5 : 1); fts.Add(ws.target); activeShips.Add(ws);
        }
        int hiddenPark = 0;
        foreach (var ft in Object.FindObjectsOfType<FlightTarget>(true))
        {
            bool parked = ft.name.StartsWith("ComPlaneV2") || ft.kind == 6;   // 6 = 基地の建物（レーダーには出さない・壊したら数える）   // 基地に止まっている警戒機（レーダーには出さない・撃墜は数える）
            if (ft.body == null && !parked) continue;   // 気球の標的（body 付き）
            if (parked) hiddenPark++;
            tracks.Add(ft.transform); types.Add(parked ? 5 : 2); fts.Add(ft);
        }
        log.AppendLine("レーダー: 基地のそばの艦隊 " + hiddenShips + " 隻・止まっている警戒機 " + hiddenPark + " 機は表示しない");
        foreach (var c in Object.FindObjectsOfType<controller>(true))
            if (c.car != null && IsR35(c.car)) { tracks.Add(c.car.transform); types.Add(3); fts.Add(null); }

        var r35s = new System.Collections.Generic.List<controller>();
        foreach (var c in Object.FindObjectsOfType<controller>(true)) if (c.car != null && IsR35(c.car)) r35s.Add(c);
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.radarTracks = tracks.ToArray();
            c.radarTypes = types.ToArray();
            c.radarTrackTargets = fts.ToArray();
            c.radarPlanes = r35s.ToArray();
            c.radarShips = activeShips.ToArray();
            var fighters = new System.Collections.Generic.List<EnemyDrone>();
            foreach (var ed2 in Object.FindObjectsOfType<EnemyDrone>(true))
                if (ed2.rb != null && ed2.rb.gameObject.activeInHierarchy) fighters.Add(ed2);
            c.radarFighters = fighters.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        log.AppendLine("レーダー: " + tracks.Count + " 個を表示対象に（" + n + " 機）" + (tracks.Count > RadarDotCount ? "  ※ 点の数 " + RadarDotCount + " より多いので、後ろの " + (tracks.Count - RadarDotCount) + " 個は出ません" : ""));
    }

    // ---------------- 機体本体をクリックして乗る ----------------
    static bool boardRetried;
    const string BoardScriptPath = "Assets/My script/drone/BoardPlane.cs";
    const string BoardAssetPath = "Assets/My script/drone/BoardPlane.asset";

    static void SetupBoarding(StringBuilder log)
    {
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null) continue;
            var st = c.GetComponent<VRCStation>();
            if (st == null) continue;
            // クリックを受けるには機体本体に当たり判定が要る
            bool hasCol = false;
            foreach (var col in c.car.GetComponents<Collider>()) if (!col.isTrigger) hasCol = true;
            if (!hasCol) continue;

            // 以前は機体本体に付けていたが、乗っている間に機体ごと（コックピットも）水色に光るので外す
            var oldB = c.car.GetComponent<BoardPlane>();
            if (oldB != null)
            {
                UdonBehaviour oub = UdonSharpEditorUtility.GetBackingUdonBehaviour(oldB);
                Undo.DestroyObjectImmediate(oldB);
                if (oub != null) Undo.DestroyObjectImmediate(oub);
            }
            Transform trig = null;
            bool onHull = false;   // R35: 後ろのハッチの所だけの小さな当たり判定
            if (IsR35(c.car))
            {
                // 以前は胴体（エンジン）の見た目に付けていたが、乗っている間に光って画面全体が水色になるので外す
                foreach (var ob in c.car.GetComponentsInChildren<BoardPlane>(true))
                {
                    if (ob.gameObject.name == "BoardTrigger") continue;
                    GameObject og = ob.gameObject;
                    UdonBehaviour oub2 = UdonSharpEditorUtility.GetBackingUdonBehaviour(ob);
                    Undo.DestroyObjectImmediate(ob);
                    if (oub2 != null) Undo.DestroyObjectImmediate(oub2);
                    foreach (var tcol in og.GetComponents<Collider>()) if (tcol.isTrigger) Undo.DestroyObjectImmediate(tcol);
                }
                onHull = true;
            }
            trig = c.car.transform.Find("BoardTrigger");
            if (trig == null)
            {
                var tg = new GameObject("BoardTrigger");
                Undo.RegisterCreatedObjectUndo(tg, "Flight setup");
                tg.transform.SetParent(c.car.transform, false);
                trig = tg.transform;
            }
            foreach (var tc in trig.GetComponents<Collider>()) Undo.DestroyObjectImmediate(tc);
            if (onHull)
            {
                // ハッチ（後ろのスロープ）の位置に、見た目の無い小さな当たり判定。光るのはこの小さな箱だけ
                Transform hatch = null;
                if (!IsOmega(c.car))
                    foreach (var t2 in c.car.GetComponentsInChildren<Transform>(true)) if (t2.name == "Dropship_Hatch") { hatch = t2; break; }
                Bounds hbd;
                var hb = Undo.AddComponent<BoxCollider>(trig.gameObject);
                hb.isTrigger = true;
                if (hatch != null && PartBounds(hatch, out hbd))
                {
                    hb.center = trig.InverseTransformPoint(hbd.center);
                    Vector3 hs = trig.InverseTransformVector(hbd.size);
                    hb.size = new Vector3(Mathf.Clamp(Mathf.Abs(hs.x), 1.5f, 4f), Mathf.Clamp(Mathf.Abs(hs.y), 1.5f, 3f), Mathf.Clamp(Mathf.Abs(hs.z), 1.5f, 4f));
                }
                else if (IsOmega(c.car))
                {
                    // Omega: 操縦席のまわり（乗り込む所）だけ
                    hb.center = trig.InverseTransformPoint(c.transform.position + Vector3.up * 0.5f);
                    hb.size = new Vector3(1.4f, 1.3f, 1.8f);   // 操縦席のまわりだけ（小さめ）
                }
                else
                {
                    var rbc = c.car.GetComponent<BoxCollider>();
                    Vector3 cc = rbc != null ? rbc.center : Vector3.zero;
                    float hz = rbc != null ? rbc.size.z * 0.5f : 5f;
                    hb.center = cc - new Vector3(0f, 0f, hz);
                    hb.size = new Vector3(3f, 2.5f, 2f);
                }
            }
            else foreach (var src in c.car.GetComponents<Collider>())
            {
                if (src.isTrigger) continue;
                if (src is BoxCollider sb)
                {
                    var nb = Undo.AddComponent<BoxCollider>(trig.gameObject);
                    nb.center = sb.center; nb.size = sb.size * 1.06f + Vector3.one * 0.1f; nb.isTrigger = true;   // 機体の当たり判定より少し大きく（クリックが先に当たるように）
                }
                else if (src is SphereCollider ss)
                {
                    var ns = Undo.AddComponent<SphereCollider>(trig.gameObject);
                    ns.center = ss.center; ns.radius = ss.radius * 1.06f + 0.05f; ns.isTrigger = true;
                }
                break;
            }
            var b = trig.GetComponent<BoardPlane>();
            if (b == null) b = UdonSharpEditor.UdonSharpUndo.AddComponent<BoardPlane>(trig.gameObject);
            Undo.RecordObject(b, "Flight setup");
            b.station = st;
            b.plane = c;
            UdonSharpEditorUtility.CopyProxyToUdon(b);
            UdonBehaviour bub = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (bub != null)
            {
                Undo.RecordObject(bub, "Flight setup");
                bub.interactText = "乗る";
                // 機体の大きさに合わせて、少し離れていても反応するように
                float r = 2f;
                Bounds cb;
                if (MeshBounds(c.car.transform, "", out cb)) r = Mathf.Max(r, cb.extents.magnitude * 0.4f);
                // R35 は機体のすぐそば（3m）まで近づいた時だけ選べる。離れていると光らない
                bub.proximity = onHull ? 3f : Mathf.Clamp(r, 2f, 12f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(bub);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(b);

            Undo.RecordObject(c, "Flight setup");
            c.boarder = b;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
            n++;
        }
        log.AppendLine("乗る: " + n + " 機の本体をクリックで乗れるように設定");
    }

    // ---------------- 新しい機体（椅子付き）を飛べるようにする ----------------
    static void SetupNewAircraft(StringBuilder log)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            GameObject go = t.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/R35/")) continue;

            VRCStation st = null;
            foreach (var s2 in go.GetComponentsInChildren<VRCStation>(true))
                if (s2.gameObject.name != "GunnerSeat") { st = s2; break; }   // 2 人目の席は除く
            if (st == null) { log.AppendLine("新しい機体: " + go.name + " に椅子(VRCStation)が無いのでスキップ"); continue; }
            var c = st.GetComponent<controller>();
            bool isNew = c == null;

            // 見た目の大きさ（椅子は除く）
            Bounds bd = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(st.transform) || r is ParticleSystemRenderer) continue;
                if (!any) { bd = r.bounds; any = true; } else bd.Encapsulate(r.bounds);
            }

            // 物理
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(go);
            rb.mass = 800f;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 当たり判定（無ければ機体の箱）
            bool hasCol = false;
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger || col.transform.IsChildOf(st.transform)) continue;
                hasCol = true;
            }
            if (!hasCol && any)
            {
                var bc = Undo.AddComponent<BoxCollider>(go);
                Transform tr = go.transform;
                bc.center = tr.InverseTransformPoint(bd.center);
                Vector3 sz = tr.InverseTransformVector(bd.size);
                bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
            }

            // 機首の向き = 椅子が向いている方向（水平）
            Transform axis = go.transform.Find("FlightAxis");
            if (axis == null)
            {
                var ag = new GameObject("FlightAxis");
                Undo.RegisterCreatedObjectUndo(ag, "Flight setup");
                ag.transform.SetParent(go.transform, false);
                axis = ag.transform;
            }
            Vector3 f = st.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.001f) f = go.transform.forward;
            axis.position = go.transform.position;
            axis.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);

            // 飛行スクリプト
            if (isNew) c = UdonSharpEditor.UdonSharpUndo.AddComponent<controller>(st.gameObject);
            Undo.RecordObject(c, "Flight setup");
            c.car = go;
            c.axisReference = axis;
            // 機体の大きさに合わせて、着地・着水の判定の高さを決める
            float halfH = any ? Mathf.Max(bd.center.y - bd.min.y, 0.5f) : 1.3f;
            c.gearHeight = halfH + 0.3f;
            c.waterContactHeight = halfH + 0.2f;
            UdonSharpEditorUtility.CopyProxyToUdon(c);

            log.AppendLine("新しい機体: " + go.name + (isNew ? " を飛べるように設定" : " を更新") + " / 大きさ " + (any ? bd.size.ToString("F1") : "?") + " / 機首 " + axis.forward.ToString("F2"));
        }
    }

    // ---------------- 大砲を撃ってくる船 ----------------
    static bool shipRetried;
    const string ShipScriptPath = "Assets/My script/drone/WarShip.cs";
    const string ShipAssetPath = "Assets/My script/drone/WarShip.asset";

    static void SetupWarShips(StringBuilder log)
    {
        var ships = new System.Collections.Generic.List<GameObject>();
        var tankers = new System.Collections.Generic.List<GameObject>();
        var tankerWs = new System.Collections.Generic.List<WarShip>();
        var escortCand = new System.Collections.Generic.List<WarShip>();
        var halfLens = new System.Collections.Generic.Dictionary<WarShip, float>();
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            GameObject go = t.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(path)) continue;
            if (path.Contains("Nokobot/ColonialShip/02_Prefabs/Colonial Ship"))
            {
                // 前の敵の船（Colonial Ship）はしまう
                Transform w0 = go.transform.parent;
                GameObject hide = (w0 != null && w0.GetComponent<WarShip>() != null) ? w0.gameObject : go;
                Undo.RecordObject(hide, "Flight setup");
                hide.SetActive(false);
                continue;
            }
            if (path.Contains("Simple Boats and Tankers/Prefabs/Tanker ship"))
            {
                // 貨物船。別のプレハブの中に入っている物は中から動かせないので、同じ場所に新しく置いて元は非表示
                Transform par = go.transform.parent;
                if (par != null && PrefabUtility.IsPartOfPrefabInstance(par.gameObject))
                {
                    if (!go.activeInHierarchy) continue;   // 置き換え済み
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var nt = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    Undo.RegisterCreatedObjectUndo(nt, "Flight setup");
                    nt.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
                    nt.transform.localScale = go.transform.lossyScale;
                    nt.name = go.name + " cargo";
                    Undo.RecordObject(go, "Flight setup");
                    go.SetActive(false);
                    ships.Add(nt);
                    tankers.Add(nt);
                    log.AppendLine("貨物船: " + go.name + " はプレハブの中にあったので、同じ場所に " + nt.name + " を置いて元は非表示に");
                    continue;
                }
                ships.Add(go);
                tankers.Add(go);
                continue;
            }
            if (!path.Contains("Military vehicles (Sea)/Prefab/")) continue;
            ships.Add(go);
        }
        if (ships.Count == 0) { log.AppendLine("船: シーンに Military vehicles (Sea) の船が見つからない"); return; }

        // 空母 = 名前に carrier / 空母 があるもの。無ければ一番長い種類の船
        string carrierPrefab = null;
        float longest = 0f;
        foreach (var sh in ships)
        {
            Bounds sb2;
            if (!MeshBounds(sh.transform, "", out sb2)) continue;
            float L = Mathf.Max(sb2.size.x, sb2.size.z);
            log.AppendLine("  船の種類: " + sh.name + " (" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sh).Replace("Assets/Military vehicles (Sea)/Prefab/", "") + ") 長さ " + L.ToString("F0") + "m");
            if (L > longest) { longest = L; carrierPrefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sh); }
        }
        var putOutS = Object.FindObjectOfType<PutOutDrone>(true);
        Vector3 avoidPos = (putOutS != null && putOutS.catapultPoint != null) ? putOutS.catapultPoint.position : new Vector3(254f, 0f, 297f);
        var rnd = new System.Random(12345);

        Material fxMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        Material smokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
        AudioClip boom = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/explosion.wav");
        var splashGo = GameObject.Find("MissileSplash");
        var crashGo = GameObject.Find("CrashExplosion");
        var planes = Object.FindObjectsOfType<controller>(true);
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;

        // 空母（S10）の場所: まわりの艦隊（2km 以内）は空母と一緒に動く
        Vector3 cvPos0 = new Vector3(1e9f, 0f, 1e9f);
        foreach (var sh in ships) if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sh).EndsWith("/S10.prefab")) { cvPos0 = sh.transform.position; break; }
        int n = 0;
        foreach (var ship in ships)
        {
            n++;
            // 船を包む親を作る（耐久値・大砲・表示の切り替えはこの親に付ける。見た目は子の船）
            Transform wrap = ship.transform.parent;
            if (wrap == null || wrap.GetComponent<WarShip>() == null)
            {
                var w = new GameObject("WarShip_" + n);
                Undo.RegisterCreatedObjectUndo(w, "Flight setup");
                w.transform.SetParent(ship.transform.parent, false);
                w.transform.position = ship.transform.position;
                w.transform.rotation = Quaternion.identity;
                Undo.SetTransformParent(ship.transform, w.transform, "Flight setup");
                wrap = w.transform;
            }
            else
            {
                // 作り直し（自分で付けた部品だけ消す）
                foreach (var name in new[] { "Muzzles", "Shells", "CannonFx", "Rockets", "RocketLauncher", "Explosion", "AAGun" })
                {
                    Transform old;
                    int guard = 0;
                    while ((old = wrap.Find(name)) != null && guard++ < 50) Undo.DestroyObjectImmediate(old.gameObject);   // 以前の実行で重なった分も消す
                }
                foreach (var c in wrap.GetComponents<Component>())
                {
                    if (c is Transform) continue;
                    if (c is UdonBehaviour || c is UdonSharpBehaviour || c is AudioSource) Undo.DestroyObjectImmediate(c);
                }
            }

            // 戦艦の大きさは全部 scale 2 にそろえる（貨物船は除く）
            if (!tankers.Contains(ship) && (ship.transform.localScale - Vector3.one * 2f).sqrMagnitude > 0.0001f)
            {
                Undo.RecordObject(ship.transform, "Flight setup");
                log.AppendLine("船の大きさ: " + ship.name + "（" + wrap.name + "）scale " + ship.transform.localScale.x.ToString("0.##") + " → 2");
                ship.transform.localScale = Vector3.one * 2f;
            }

            // 見た目の大きさ
            var rends = new System.Collections.Generic.List<Renderer>();
            foreach (var r in ship.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) rends.Add(r);
            if (rends.Count == 0) { log.AppendLine("船: 見た目が見つからない " + ship.name); continue; }
            Bounds bd = rends[0].bounds;
            foreach (var r in rends) bd.Encapsulate(r.bounds);

            // 当たり判定（無ければ船体の箱。帆の部分は除いて低めにする）
            var cols = new System.Collections.Generic.List<Collider>();
            foreach (var c in ship.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) cols.Add(c);
            if (cols.Count == 0)
            {
                var bc = Undo.AddComponent<BoxCollider>(ship);
                Transform st = ship.transform;
                Vector3 hullCenter = new Vector3(bd.center.x, bd.min.y + bd.size.y * 0.3f, bd.center.z);
                Vector3 hullSize = new Vector3(bd.size.x, bd.size.y * 0.6f, bd.size.z);
                bc.center = st.InverseTransformPoint(hullCenter);
                Vector3 sz = st.InverseTransformVector(hullSize);
                bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
                cols.Add(bc);
            }

            // 船の長い向きと横向き
            bool alongX = bd.size.x >= bd.size.z;
            Vector3 lengthDir = alongX ? Vector3.right : Vector3.forward;
            Vector3 sideDir = alongX ? Vector3.forward : Vector3.right;
            float halfLen = (alongX ? bd.size.x : bd.size.z) * 0.5f;
            float halfWidth = (alongX ? bd.size.z : bd.size.x) * 0.5f * 0.45f;   // 帆桁を除いた船体の幅（およそ）
            float deckY = Mathf.Max(seaY + 3f, bd.min.y + 2f);

            // 砲門（左右に 3 門ずつ）
            var muzzles = new GameObject("Muzzles").transform;
            muzzles.SetParent(wrap, false);
            var mlist = new System.Collections.Generic.List<Transform>();
            for (int side = -1; side <= 1; side += 2)
            for (int k = 0; k < 3; k++)
            {
                float f = -0.5f + k * 0.5f;   // 前・中・後ろ
                Vector3 p = new Vector3(bd.center.x, deckY, bd.center.z) + lengthDir * (f * halfLen * 0.8f) + sideDir * (side * halfWidth);
                var m = new GameObject("Cannon_" + (side < 0 ? "L" : "R") + (k + 1)).transform;
                m.SetParent(muzzles, false);
                m.position = p;
                m.rotation = Quaternion.LookRotation(sideDir * side, Vector3.up);
                mlist.Add(m);
            }

            // 砲弾（6 発ぶん）
            var shellRoot = new GameObject("Shells").transform;
            shellRoot.SetParent(wrap, false);
            Material ballMat = LoadOrCreateMat("CannonBall", Shader.Find("Standard"));
            ballMat.SetColor("_Color", new Color(0.12f, 0.12f, 0.12f));
            EditorUtility.SetDirty(ballMat);
            var shells = new Transform[6];
            for (int k = 0; k < shells.Length; k++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(b.GetComponent<Collider>());
                b.name = "Shell_" + (k + 1);
                b.transform.SetParent(shellRoot, false);
                b.transform.localScale = Vector3.one * 0.6f;
                var br = b.GetComponent<MeshRenderer>();
                br.sharedMaterial = ballMat;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var tr = b.AddComponent<TrailRenderer>();
                tr.sharedMaterial = smokeMat;
                tr.time = 0.6f;
                tr.minVertexDistance = 1f;
                tr.widthCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.7f, 0.7f, 0.7f), 0f), new GradientColorKey(new Color(0.5f, 0.5f, 0.5f), 1f) },
                          new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.SetActive(false);
                shells[k] = b.transform;
            }

            // 追尾しないミサイル（3 発ぶん）と発射位置（甲板の中央の少し上）
            var launcher = new GameObject("RocketLauncher").transform;
            launcher.SetParent(wrap, false);
            launcher.position = new Vector3(bd.center.x, deckY + 8f, bd.center.z);
            var rocketRoot = new GameObject("Rockets").transform;
            rocketRoot.SetParent(wrap, false);
            Material rocketMat = LoadOrCreateMat("ShipRocket", Shader.Find("Standard"));
            rocketMat.SetColor("_Color", new Color(0.55f, 0.15f, 0.12f));
            EditorUtility.SetDirty(rocketMat);
            AudioClip mslClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/missile.wav");
            var rockets = new Transform[3];
            for (int k = 0; k < rockets.Length; k++)
            {
                var rk = new GameObject("Rocket_" + (k + 1));
                rk.transform.SetParent(rocketRoot, false);
                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(cyl.GetComponent<Collider>());
                cyl.name = "Body";
                cyl.transform.SetParent(rk.transform, false);
                cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                cyl.transform.localScale = new Vector3(0.35f, 1.2f, 0.35f);   // 長さ 2.4m
                var cr = cyl.GetComponent<MeshRenderer>();
                cr.sharedMaterial = rocketMat;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var flame = MakePS("Flame", rk.transform, fxMat);
                flame.transform.localPosition = new Vector3(0f, 0f, -1.3f);
                var flm = flame.main;
                flm.loop = true;
                flm.playOnAwake = true;
                flm.simulationSpace = ParticleSystemSimulationSpace.Local;
                flm.startLifetime = 0.12f;
                flm.startSpeed = 0f;
                flm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
                flm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.15f), new Color(1f, 0.85f, 0.45f));
                flm.maxParticles = 30;
                var fle = flame.emission;
                fle.rateOverTime = 80f;
                var fls = flame.shape;
                fls.enabled = false;
                var rtr = rk.AddComponent<TrailRenderer>();
                rtr.sharedMaterial = smokeMat;
                rtr.time = 3f;
                rtr.minVertexDistance = 2f;
                rtr.widthCurve = AnimationCurve.Linear(0f, 0.9f, 1f, 4f);
                var rg = new Gradient();
                rg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.75f, 0.75f), 1f) },
                           new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
                rtr.colorGradient = rg;
                rtr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var rau = rk.AddComponent<AudioSource>();
                rau.clip = mslClip;
                rau.playOnAwake = false;
                rau.spatialBlend = 1f;
                rau.minDistance = 15f;
                rau.maxDistance = 1500f;
                rk.SetActive(false);
                rockets[k] = rk.transform;
            }

            // 対空機銃（艦の上・ミサイル発射位置の少し上。赤い曳光弾）
            var aa = new GameObject("AAGun").transform;
            aa.SetParent(wrap, false);
            aa.position = new Vector3(bd.center.x, deckY + 12f, bd.center.z);
            var aaTr = MakePS("Tracer", aa, fxMat);
            var aam = aaTr.main;
            aam.startLifetime = 2f;
            aam.startSpeed = 600f;
            aam.startSize = 0.5f;
            aam.startColor = new Color(1f, 0.3f, 0.2f, 1f);
            aam.maxParticles = 200;
            var aash = aaTr.shape;
            aash.enabled = false;
            var aar = aaTr.GetComponent<ParticleSystemRenderer>();
            aar.renderMode = ParticleSystemRenderMode.Stretch;
            aar.velocityScale = 0.02f;
            aar.lengthScale = 2f;
            var aaAu = aa.gameObject.AddComponent<AudioSource>();
            aaAu.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/gun.wav");
            aaAu.playOnAwake = false;
            aaAu.spatialBlend = 1f;
            aaAu.minDistance = 20f;
            aaAu.maxDistance = 1500f;

            // 発射の閃光と煙
            var fxRoot = new GameObject("CannonFx").transform;
            fxRoot.SetParent(wrap, false);
            var flash = MakePS("Flash", fxRoot, fxMat);
            var fm = flash.main;
            fm.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
            fm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 15f);
            fm.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
            fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f), new Color(1f, 0.9f, 0.5f));
            fm.maxParticles = 40;
            var fe = flash.emission;
            fe.SetBursts(new[] { new ParticleSystem.Burst(0f, 15) });
            var fs = flash.shape;
            fs.enabled = true;
            fs.shapeType = ParticleSystemShapeType.Cone;
            fs.angle = 15f;
            fs.radius = 0.3f;
            var smoke = MakePS("Smoke", flash.transform, smokeMat);
            var sm = smoke.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 10f);
            sm.startSize = new ParticleSystem.MinMaxCurve(3f, 7f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.8f, 0.8f, 0.7f), new Color(0.6f, 0.6f, 0.6f, 0.8f));
            sm.gravityModifier = -0.05f;
            sm.maxParticles = 60;
            var se = smoke.emission;
            se.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });
            var ss = smoke.shape;
            ss.enabled = true;
            ss.shapeType = ParticleSystemShapeType.Cone;
            ss.angle = 20f;
            ss.radius = 0.5f;
            var sol = smoke.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            SetFade(smoke, new Color(0.85f, 0.85f, 0.85f), new Color(0.6f, 0.6f, 0.6f));

            // 発射音
            var au = wrap.gameObject.AddComponent<AudioSource>();
            au.clip = boom;
            au.playOnAwake = false;
            au.spatialBlend = 1f;
            au.minDistance = 30f;
            au.maxDistance = 1500f;

            // 耐久値（機銃・ミサイルで壊せる）
            var target = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(wrap.gameObject);
            target.renderers = rends.ToArray();
            target.colliders = cols.ToArray();
            target.maxHp = 40;
            target.respawnTime = 60f;
            target.missileDamage = 15;
            var ex = MakePS("Explosion", wrap, fxMat);
            ex.transform.position = bd.center;
            ConfigureExplosion(ex, Mathf.Clamp(bd.extents.magnitude / 8f, 1f, 12f), 60);
            var exAu = ex.gameObject.AddComponent<AudioSource>();
            exAu.clip = boom;
            exAu.playOnAwake = false;
            exAu.spatialBlend = 1f;
            exAu.minDistance = 40f;
            exAu.maxDistance = 2500f;
            target.explosionFx = ex;
            target.explosionAudio = exAu;
            UdonSharpEditorUtility.CopyProxyToUdon(target);

            // 大砲・表示の切り替え
            var ws = UdonSharpEditor.UdonSharpUndo.AddComponent<WarShip>(wrap.gameObject);
            ws.planes = planes;
            ws.model = ship;
            ws.target = target;
            ws.cannonMuzzles = mlist.ToArray();
            ws.shells = shells;
            ws.rockets = rockets;
            ws.aaMuzzle = aa;
            ws.aaTracer = aaTr;
            ws.aaAudio = aaAu;
            ws.rocketLauncher = launcher;
            ws.muzzleFx = flash;
            ws.cannonAudio = au;
            ws.splashFx = splashGo != null ? splashGo.GetComponent<ParticleSystem>() : null;
            ws.impactFx = crashGo != null ? crashGo.GetComponent<ParticleSystem>() : null;
            ws.impactAudio = crashGo != null ? crashGo.GetComponent<AudioSource>() : null;
            ws.seaLevel = seaY;

            // 種類（空母か）と、航行（名前に fleet が付いた船・艦隊は止まったまま）
            string lname = ship.name.ToLower();
            bool hinted = lname.Contains("carrier") || ship.name.Contains("空母");
            string spath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ship);
            bool isCarrier = hinted || spath.EndsWith("/S10.prefab");   // S10 が空母
            target.kind = isCarrier ? 2 : 1;
            if (isCarrier) target.maxHp = 80;
            ws.appearDelay = isCarrier ? CarrierAppearDelay : 0f;   // 空母はワールドが始まって 10 分後に出てくる
            UdonSharpEditorUtility.CopyProxyToUdon(target);
            bool fleet = false;
            for (Transform a = ship.transform; a != null; a = a.parent) if (a.name.ToLower().Contains("fleet")) { fleet = true; break; }
            ws.path = new Vector3[0];
            bool isTanker = tankers.Contains(ship);
            ws.roaming = !fleet && !isTanker;   // fleet 以外は、ワールドが始まるたび・沈むたびにランダムな海へ（貨物船は護衛に付いて行く）
            halfLens[ws] = halfLen;
            if (isTanker)
            {
                target.kind = 3;            // 貨物船（1 点）
                target.maxHp = 25;
                target.respawnTime = 120f;  // 撃破されたら 2 分後に復活（護衛が残っていれば、その後ろに）
                ws.unarmed = true;
                tankerWs.Add(ws);
                UdonSharpEditorUtility.CopyProxyToUdon(target);
            }
            else if (ws.roaming && !isCarrier && spath.EndsWith("/S2.prefab")) escortCand.Add(ws);
            ws.avoidCenter = avoidPos;
            ws.avoidRadius = ShipAvoid;
            ws.pathSpeed = isCarrier ? 6f : 8f;
            ws.yawOffset = Mathf.Atan2(lengthDir.x, lengthDir.z) * Mathf.Rad2Deg;
            Vector3 dcv = ship.transform.position - cvPos0; dcv.y = 0f;
            bool cvGroup = fleet && dcv.magnitude < 2000f;   // 空母の艦隊（空母と一緒に動く）
            if (!fleet || cvGroup)
            {
                var wrb = wrap.GetComponent<Rigidbody>();
                if (wrb == null) wrb = Undo.AddComponent<Rigidbody>(wrap.gameObject);
                wrb.isKinematic = true;   // 動く当たり判定は kinematic の方が軽い
                wrb.useGravity = false;
            }
            foreach (var r in rends) { Undo.RecordObject(r, "Flight setup"); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }   // 軽量化

            // 演出（船の見た目の子に入れるので、遠くて船が非表示の時は一緒に止まる）
            foreach (var nm in new[] { "ShipWake", "ShipDamage" })
            {
                Transform oldF; int g2 = 0;
                while ((oldF = ship.transform.Find(nm)) != null && g2++ < 10) Undo.DestroyObjectImmediate(oldF.gameObject);
            }
            Material foamMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/WaterFx.mat");
            if (foamMat == null) foamMat = smokeMat;
            var wakeList = new System.Collections.Generic.List<ParticleSystem>();
            if (ws.roaming || isTanker || cvGroup)
            {
                var wr = new GameObject("ShipWake").transform;
                wr.SetParent(ship.transform, false);
                // 船の後ろの航跡（海面に平らに広がる白い泡）
                var wake = MakePS("Wake", wr, foamMat);
                wake.transform.position = new Vector3(bd.center.x, seaY + 0.6f, bd.center.z) - lengthDir * halfLen * 0.95f;
                var wm = wake.main;
                wm.loop = true;
                wm.startLifetime = new ParticleSystem.MinMaxCurve(7f, 10f);
                wm.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
                wm.startSize = halfWidth * 2.2f;
                wm.startColor = new Color(1f, 1f, 1f, 0.55f);
                wm.maxParticles = 400;
                var we = wake.emission;
                we.rateOverTime = 14f;
                var wsh = wake.shape;
                wsh.enabled = true;
                wsh.shapeType = ParticleSystemShapeType.Sphere;
                wsh.radius = halfWidth * 0.6f;
                var wsol = wake.sizeOverLifetime;
                wsol.enabled = true;
                wsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.8f));
                SetFade(wake, Color.white, new Color(0.85f, 0.92f, 1f));
                wake.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                wakeList.Add(wake);
                // 船首の波（左右に白く立つ）
                var bow = MakePS("BowWave", wr, foamMat);
                bow.transform.position = new Vector3(bd.center.x, seaY + 0.8f, bd.center.z) + lengthDir * halfLen * 0.9f;
                var bm = bow.main;
                bm.loop = true;
                bm.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
                bm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
                bm.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
                bm.startColor = new Color(1f, 1f, 1f, 0.7f);
                bm.gravityModifier = 0.4f;
                bm.maxParticles = 200;
                var be = bow.emission;
                be.rateOverTime = 18f;
                var bsh = bow.shape;
                bsh.enabled = true;
                bsh.shapeType = ParticleSystemShapeType.Cone;
                bsh.angle = 60f;
                bsh.radius = halfWidth * 0.5f;
                bow.transform.rotation = Quaternion.LookRotation(Vector3.up);
                SetFade(bow, Color.white, new Color(0.9f, 0.95f, 1f));
                wakeList.Add(bow);
            }
            // ダメージの煙と炎（甲板の前と後ろ）
            var dr2 = new GameObject("ShipDamage").transform;
            dr2.SetParent(ship.transform, false);
            var smokes = new System.Collections.Generic.List<ParticleSystem>();
            var fires = new System.Collections.Generic.List<ParticleSystem>();
            for (int k = -1; k <= 1; k += 2)
            {
                Vector3 sp = new Vector3(bd.center.x, deckY + 4f, bd.center.z) + lengthDir * (k * halfLen * 0.3f);
                var sm2 = MakePS("Smoke", dr2, smokeMat);
                sm2.transform.position = sp;
                var smm = sm2.main;
                smm.loop = true;
                smm.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                smm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
                smm.startSize = new ParticleSystem.MinMaxCurve(8f, 14f);
                smm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.12f, 0.12f, 0.12f, 0.7f), new Color(0.3f, 0.3f, 0.3f, 0.6f));
                smm.gravityModifier = -0.05f;
                smm.maxParticles = 300;
                var smsh = sm2.shape; smsh.enabled = true; smsh.shapeType = ParticleSystemShapeType.Sphere; smsh.radius = 3f;
                var smsol = sm2.sizeOverLifetime; smsol.enabled = true; smsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.5f));
                SetFade(sm2, new Color(0.25f, 0.25f, 0.25f), new Color(0.55f, 0.55f, 0.55f));
                smokes.Add(sm2);
                var fi2 = MakePS("Fire", dr2, fxMat);
                fi2.transform.position = sp - Vector3.up * 2f;
                var fim = fi2.main;
                fim.loop = true;
                fim.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                fim.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
                fim.startSize = new ParticleSystem.MinMaxCurve(4f, 8f);
                fim.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.9f));
                fim.gravityModifier = -0.3f;
                fim.maxParticles = 300;
                var fish = fi2.shape; fish.enabled = true; fish.shapeType = ParticleSystemShapeType.Sphere; fish.radius = 3f;
                SetFade(fi2, new Color(1f, 0.7f, 0.3f), new Color(0.6f, 0.1f, 0.05f));
                fires.Add(fi2);
            }
            ws.wakeFx = wakeList.ToArray();
            ws.dmgSmoke = smokes.ToArray();
            ws.dmgFire = fires.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(ws);
            log.AppendLine("  → " + (isCarrier ? "空母" : "船") + (fleet ? "（fleet: 止まったまま）" : "（ランダムな海を航行）"));

            log.AppendLine("船: " + ship.name + " 位置 " + ship.transform.position.ToString("F0") + " 大きさ " + bd.size.ToString("F0") + " 砲門 " + mlist.Count + " / 当たり判定 " + cols.Count);
        }
        // 貨物船に護衛の戦艦（S2）を 1 隻ずつ付ける。大きさ（scale）が 2 の S2 を優先
        escortCand.Sort((x, y) => y.model.transform.localScale.x.CompareTo(x.model.transform.localScale.x));
        for (int i = 0; i < tankerWs.Count; i++)
        {
            WarShip tk = tankerWs[i];
            if (i >= escortCand.Count) { log.AppendLine("貨物船: " + tk.model.name + " に付ける S2 が足りない（止まったまま）"); continue; }
            WarShip es = escortCand[i];
            Undo.RecordObject(tk, "Flight setup");
            Undo.RecordObject(es, "Flight setup");
            tk.leader = es;
            tk.followGap = halfLens[es] + halfLens[tk] + 120f;
            es.follower = tk;
            if (es.target != null)
            {
                Undo.RecordObject(es.target, "Flight setup");
                es.target.respawnAfter = tk.target;   // 護衛は、貨物船が撃破されるまで復活しない
                UdonSharpEditorUtility.CopyProxyToUdon(es.target);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(tk);
            UdonSharpEditorUtility.CopyProxyToUdon(es);
            log.AppendLine("貨物船: " + tk.model.name + " ← 護衛 " + es.model.name + "（" + es.gameObject.name + "、scale " + es.model.transform.localScale.x.ToString("0.##") + "、" + tk.followGap.ToString("F0") + "m 後ろを付いて行く）");
        }

        // 全部の船を互いに知らせる（1 人のプレイヤーの近くに出る船の数を数えるため）
        var allWs = Object.FindObjectsOfType<WarShip>(true);
        // 空母のまわりに止まっている艦隊も、空母と一緒に 10 分後に出てくる
        WarShip cvShip = null;
        foreach (var w in allWs) if (w.target != null && w.target.kind == 2) { cvShip = w; break; }
        int cvFleet = 0;
        float fleetR = 200f;
        var cvEsc = new System.Collections.Generic.List<WarShip>();
        if (cvShip != null)
        {
            Vector3 cvPos = cvShip.model != null ? cvShip.model.transform.position : cvShip.transform.position;
            foreach (var w in allWs)
            {
                if (w == cvShip || w.roaming || w.leader != null) continue;
                Vector3 wp = w.model != null ? w.model.transform.position : w.transform.position;
                Vector3 dw = wp - cvPos; dw.y = 0f;
                if (dw.magnitude > 2000f) continue;
                Undo.RecordObject(w, "Flight setup");
                w.appearDelay = CarrierAppearDelay;
                // 艦隊: 空母の向きから見た今の位置を覚えて、その位置を保って一緒に走る
                Quaternion hd = Quaternion.Euler(0f, cvShip.transform.eulerAngles.y + cvShip.yawOffset, 0f);
                Vector3 off = Quaternion.Inverse(hd) * (w.transform.position - cvShip.transform.position);
                off.y = 0f;
                w.leader = cvShip;
                w.useFormation = true;
                w.formationOffset = off;
                fleetR = Mathf.Max(fleetR, off.magnitude);
                cvEsc.Add(w);
                cvFleet++;
            }
            // 空母: ランダムな海に現れて、ランダムな輪のコースを走る（プレイヤーの近くへ移ったりはしない）
            Undo.RecordObject(cvShip, "Flight setup");
            cvShip.roaming = true;
            cvShip.noRelocate = true;
            cvShip.pathMargin = fleetR + 250f;
            cvShip.escorts = cvEsc.ToArray();
            cvShip.path = new Vector3[0];
            log.AppendLine("空母: 戦闘が始まって " + (CarrierAppearDelay / 60f).ToString("F0") + " 分後に、まわりの艦隊 " + cvFleet + " 隻と一緒に出てくる（ランダムな海に現れ、艦隊を組んで走る。艦隊の幅 " + fleetR.ToString("F0") + "m）");
        }
        int roamN = 0;
        foreach (var w in allWs)
        {
            Undo.RecordObject(w, "Flight setup");
            w.allShips = allWs;
            if (w.roaming) roamN++;
            UdonSharpEditorUtility.CopyProxyToUdon(w);
        }
        log.AppendLine("船: 動く船 " + roamN + " 隻（1 人の近くに最大 " + ShipsPerPlayer + " 隻まで）");
    }

    const int ShipsPerPlayer = 5;
    const float CarrierAppearDelay = 120f;   // 戦闘が始まってから空母の艦隊が出てくるまでの秒数（2 分）
    const float ShipAvoid = 2000f;   // 動く船は出撃地点の 2km 以内に入らない

    // 船のコース: 出撃地点から 10km 以上離れ、島に乗り上げない輪
    static Vector3[] MakeShipPath(Vector3 start, float seaY, Vector3 avoid, System.Random rnd, Transform self)
    {
        for (int tries = 0; tries < 60; tries++)
        {
            int n = 6;
            var pts = new Vector3[n];
            float R = 900f + (float)rnd.NextDouble() * 1400f;
            float ca = (float)(rnd.NextDouble() * Mathf.PI * 2f);
            Vector3 c = start + new Vector3(Mathf.Cos(ca), 0f, Mathf.Sin(ca)) * R * 0.6f;
            // 出撃地点の 5km 以内から始まる船は、コースごと 5km の外へ出す
            Vector3 fromA = start - avoid; fromA.y = 0f;
            if (fromA.magnitude < ShipAvoid + R * 1.3f)
            {
                Vector3 dirA = fromA.sqrMagnitude > 1f ? fromA.normalized : new Vector3(Mathf.Cos(ca), 0f, Mathf.Sin(ca));
                if (tries > 20) dirA = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f) * dirA;
                c = new Vector3(avoid.x, start.y, avoid.z) + dirA * (ShipAvoid + R * 1.3f + 500f + (float)rnd.NextDouble() * 2000f);
            }
            float a0 = (float)(rnd.NextDouble() * Mathf.PI * 2f);
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / n + ((float)rnd.NextDouble() - 0.5f) * 0.5f;
                float r = R * (0.7f + (float)rnd.NextDouble() * 0.5f);
                pts[i] = new Vector3(c.x + Mathf.Cos(a) * r, start.y, c.z + Mathf.Sin(a) * r);
            }
            if (PathOk(pts, seaY, avoid, self)) return pts;
        }
        return null;
    }

    static bool PathOk(Vector3[] pts, float seaY, Vector3 avoid, Transform self)
    {
        int n = pts.Length;
        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
            for (int j = 0; j < 12; j++)
            {
                float t = j / 12f, t2 = t * t, t3 = t2 * t;
                Vector3 q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                Vector3 fa = q - avoid; fa.y = 0f;
                if (fa.magnitude < ShipAvoid + 300f) return false;   // 出撃地点の 5km 以内（＋余裕）
                // 島や陸（海面より高い当たり判定）があれば ×
                foreach (var h in Physics.RaycastAll(new Vector3(q.x, seaY + 400f, q.z), Vector3.down, 430f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider == null || h.collider.transform.IsChildOf(self)) continue;
                    if (h.collider.GetComponentInParent<WarShip>() != null) continue;
                    if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;   // 飛行機など
                    if (h.point.y > seaY + 1.5f) return false;
                }
            }
        }
        return true;
    }

    // ---------------- 宇宙ステーションを雲で隠す ----------------
    static bool cloakRetried;

    // ---------------- 2 人目の席（ガンナー） ----------------
    const string GunnerScriptPath = "Assets/My script/drone/GunnerStation.cs";
    const string GunnerAssetPath = "Assets/My script/drone/GunnerStation.asset";

    static void SetupGunner(StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(GunnerAssetPath) == null) { log.AppendLine("ERROR: GunnerStation.asset が無い"); return; }
        Texture2D tex = GetSoftTexture();
        Material fxMat = GetFxMaterial(tex);
        AudioClip gunClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/gun.wav");
        Material metal = LoadOrCreateMat("TurretMetal", Shader.Find("Standard"));
        metal.SetColor("_Color", new Color(0.22f, 0.24f, 0.26f));
        metal.SetFloat("_Metallic", 0.6f);
        metal.SetFloat("_Glossiness", 0.5f);
        EditorUtility.SetDirty(metal);
        Material retMat = UnlitMat("GunnerReticle", new Color(0.35f, 1f, 0.5f, 0.95f), true, 3100);
        retMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        retMat.renderQueue = 3100;
        EditorUtility.SetDirty(retMat);
        Material lockMat = UnlitMat("LockRed", new Color(1f, 0.12f, 0.1f, 1f), true, 3100);

        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car) || c.gunMuzzle == null) continue;
            Transform car = c.car.transform;
            var pst = c.GetComponent<VRCStation>();
            Transform seat = c.transform;
            Transform enter = (pst != null && pst.stationEnterPlayerLocation != null) ? pst.stationEnterPlayerLocation : seat;

            // 作り直す
            Transform old = car.Find("GunnerSeat");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            Transform oldT = car.Find("GunnerTurret");
            bool hadTurret = oldT != null;
            Vector3 oldTPos = hadTurret ? oldT.position : Vector3.zero;
            Quaternion oldTRot = hadTurret ? oldT.rotation : Quaternion.identity;   // 手で動かした砲塔の位置は残す（Omega）
            if (oldT != null) Undo.DestroyObjectImmediate(oldT.gameObject);

            // 席: 操縦席の 1.4m 後ろ、同じ向き
            var gsGo = new GameObject("GunnerSeat");
            Undo.RegisterCreatedObjectUndo(gsGo, "Flight setup");
            gsGo.transform.SetParent(car, false);
            gsGo.transform.SetPositionAndRotation(enter.position - seat.forward * 1.4f + seat.up * 0.1f, seat.rotation);
            SetWorldScaleOne(gsGo.transform);
            var st = Undo.AddComponent<VRCStation>(gsGo);
            st.PlayerMobility = VRC.SDKBase.VRCStation.Mobility.ImmobilizeForVehicle;
            st.disableStationExit = true;   // 降りるのはジャンプ
            st.seated = true;
            st.canUseStationFromStation = false;
            st.stationEnterPlayerLocation = gsGo.transform;
            st.stationExitPlayerLocation = (pst != null && pst.stationExitPlayerLocation != null) ? pst.stationExitPlayerLocation : gsGo.transform;

            // 砲塔: 機首の銃の 1.2m 下（機体の下側）
            var tu = new GameObject("GunnerTurret");
            Undo.RegisterCreatedObjectUndo(tu, "Flight setup");
            tu.transform.SetParent(car, false);
            bool omegaT = IsOmega(c.car);
            Transform axT = car.Find("FlightAxis");
            if (axT == null) axT = car;
            Bounds obd;
            if (omegaT && hadTurret)
                tu.transform.SetPositionAndRotation(oldTPos, oldTRot);
            else if (omegaT && MeshBounds(car.Find("OmegaBody"), "", out obd))
                tu.transform.SetPositionAndRotation(obd.center - axT.up * (obd.extents.y * 0.9f + 0.3f) + axT.forward * 1.5f, axT.rotation);   // Omega: 胴体の下・少し前
            else
                tu.transform.SetPositionAndRotation(c.gunMuzzle.position - car.up * 1.2f - c.gunMuzzle.forward * 1.0f, c.gunMuzzle.rotation);
            SetWorldScaleOne(tu.transform);
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.name = "Ball";
            ball.transform.SetParent(tu.transform, false);
            ball.transform.localScale = Vector3.one * 0.7f;
            ball.GetComponent<MeshRenderer>().sharedMaterial = metal;
            for (int k = -1; k <= 1; k += 2)
            {
                var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(barrel.GetComponent<Collider>());
                barrel.name = "Barrel";
                barrel.transform.SetParent(tu.transform, false);
                barrel.transform.localPosition = new Vector3(k * 0.13f, 0f, 0.8f);
                barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                barrel.transform.localScale = new Vector3(0.09f, 0.6f, 0.09f);
                barrel.GetComponent<MeshRenderer>().sharedMaterial = metal;
            }
            var mz = new GameObject("Muzzle");
            mz.transform.SetParent(tu.transform, false);
            mz.transform.localPosition = new Vector3(0f, 0f, 1.5f);
            var trc = MakePS("Tracer", mz.transform, fxMat);
            var main = trc.main;
            main.startLifetime = 1.6f;
            main.startSpeed = 500f;
            main.startSize = 0.3f;
            main.startColor = new Color(0.5f, 1f, 0.6f, 1f);   // ガンナーの弾は緑
            main.maxParticles = 300;
            var shape = trc.shape;
            shape.enabled = false;
            var iv = trc.inheritVelocity;
            iv.enabled = true;
            iv.mode = ParticleSystemInheritVelocityMode.Initial;
            iv.curve = new ParticleSystem.MinMaxCurve(1f);
            var rend = trc.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.025f;
            rend.lengthScale = 2f;
            var au = mz.AddComponent<AudioSource>();
            au.playOnAwake = false;
            au.clip = gunClip;
            au.spatialBlend = 1f;
            au.minDistance = 10f;
            au.maxDistance = 900f;
            au.volume = 0.6f;
            au.dopplerLevel = 0f;

            // ガンナー本人の照準と表示（頭に付いてくる）
            var hud = new GameObject("GunnerHUD");
            hud.transform.SetParent(gsGo.transform, false);
            float g = 0.035f, th = 0.004f, z = 2f;
            Quad(hud.transform, "R1", new Vector3(g, 0f, z), new Vector2(g, th), retMat);
            Quad(hud.transform, "R2", new Vector3(-g, 0f, z), new Vector2(g, th), retMat);
            Quad(hud.transform, "R3", new Vector3(0f, g, z), new Vector2(th, g), retMat);
            Quad(hud.transform, "R4", new Vector3(0f, -g, z), new Vector2(th, g), retMat);
            Quad(hud.transform, "Dot", new Vector3(0f, 0f, z), new Vector2(th, th), retMat);
            var panel = new GameObject("Panel").transform;
            panel.SetParent(hud.transform, false);
            panel.localPosition = new Vector3(-0.30f, -0.22f, 1.0f);
            var ht = Txt(panel, "Text", "", Vector2.zero, 0.3f, 0.22f, CWhite, TextAlignmentOptions.TopLeft, 0.2f);
            hud.SetActive(false);

            var lb = new GameObject("LockBox").transform;
            lb.SetParent(gsGo.transform, false);
            const float LT = 0.05f;
            Quad(lb, "Top", new Vector3(0f, 0.5f, 0f), new Vector2(1f + LT, LT), lockMat);
            Quad(lb, "Bottom", new Vector3(0f, -0.5f, 0f), new Vector2(1f + LT, LT), lockMat);
            Quad(lb, "Left", new Vector3(-0.5f, 0f, 0f), new Vector2(LT, 1f), lockMat);
            Quad(lb, "Right", new Vector3(0.5f, 0f, 0f), new Vector2(LT, 1f), lockMat);
            lb.gameObject.SetActive(false);

            // 操縦席の背中のレーダー（後ろの席の人にだけ表示）
            var grRoot = new GameObject("GunnerRadar");
            grRoot.transform.SetParent(gsGo.transform, false);
            grRoot.transform.localPosition = Vector3.up * (0.72f + CockpitRaise);
            grRoot.transform.localRotation = Quaternion.identity;
            Material gBg = UnlitMat("CockpitPanel", new Color(0.02f, 0.07f, 0.13f, 0.72f), true, 2990);
            Material gFrame = UnlitMat("CockpitFrame", CCyan, false, 2000);
            Material gAmber = UnlitMat("CockpitAmber", CAmber, false, 2000);
            BuildRadar(c, grRoot.transform, gBg, gFrame, gAmber, true);
            grRoot.SetActive(false);
            // 警告音（操縦席と同じ音）
            AudioSource MakeLoop(string nm, string file, float vol)
            {
                var g = new GameObject(nm);
                g.transform.SetParent(gsGo.transform, false);
                var a = g.AddComponent<AudioSource>();
                a.clip = LoadClip(file);
                a.loop = true; a.playOnAwake = false; a.spatialBlend = 0f; a.volume = vol;
                return a;
            }
            var gWarn = MakeLoop("StallWarnAudio", "stall_warn.wav", 0.35f);
            var gStall = MakeLoop("StallAudio", "stall.wav", 0.4f);
            var gAlarm = MakeLoop("AlarmAudio", "alarm.wav", 0.22f);

            var gst = UdonSharpEditor.UdonSharpUndo.AddComponent<GunnerStation>(gsGo);
            gst.radarRoot = grRoot;
            if (omegaT) PlaceTurretGuns(c, tu.transform, log);
            gst.stallWarnAudio = gWarn;
            gst.stallAudio = gStall;
            gst.alarmAudio = gAlarm;
            gst.plane = c;
            gst.station = st;
            gst.turret = tu.transform;
            gst.muzzle = mz.transform;
            gst.tracer = trc;
            gst.gunAudio = au;
            gst.hudRoot = hud;
            gst.hudText = ht;
            gst.lockBox = lb;
            UdonSharpEditorUtility.CopyProxyToUdon(gst);

            // 音声を後ろの席にも流す仕組み（操縦士が持ち主になって同期）
            Transform oldR = car.Find("VoiceRelay");
            if (oldR != null) Undo.DestroyObjectImmediate(oldR.gameObject);
            var vr = new GameObject("VoiceRelay");
            Undo.RegisterCreatedObjectUndo(vr, "Flight setup");
            vr.transform.SetParent(car, false);
            var vOut = vr.AddComponent<AudioSource>();
            vOut.playOnAwake = false; vOut.spatialBlend = 0f; vOut.volume = 0.9f; vOut.priority = 10;
            var rGo = new GameObject("Radio");
            rGo.transform.SetParent(vr.transform, false);
            var rOut = rGo.AddComponent<AudioSource>();
            rOut.playOnAwake = false; rOut.spatialBlend = 0f; rOut.volume = 0.85f; rOut.priority = 5;
            var clipList = new System.Collections.Generic.List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                string ap = AssetDatabase.GUIDToAssetPath(guid);
                string fn = Path.GetFileName(ap);
                if (!fn.StartsWith("voice_") && !fn.StartsWith("ama_")) continue;
                var ac = AssetDatabase.LoadAssetAtPath<AudioClip>(ap);
                if (ac != null) clipList.Add(ac);
            }
            var relay = UdonSharpEditor.UdonSharpUndo.AddComponent<VoiceRelay>(vr);
            relay.clips = clipList.ToArray();
            relay.gunner = gst;
            relay.voiceOut = vOut;
            relay.radioOut = rOut;
            UdonSharpEditorUtility.CopyProxyToUdon(relay);

            Undo.RecordObject(c, "Flight setup");
            c.gunner = gst;
            c.voiceRelay = relay;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            if (c.boarder != null)
            {
                Undo.RecordObject(c.boarder, "Flight setup");
                c.boarder.station2 = st;
                c.boarder.gunner = gst;
                UdonSharpEditorUtility.CopyProxyToUdon(c.boarder);
                PrefabUtility.RecordPrefabInstancePropertyModifications(c.boarder);
            }
            n++;
        }
        log.AppendLine("ガンナー席: " + n + " 機に設置（操縦席の後ろ・機体の下に旋回砲塔）");
    }

    static AudioClip LoadClip(string file)
    {
        string path = AudioDir + "/" + file;
        var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (c == null && File.Exists(path)) { AssetDatabase.ImportAsset(path); c = AssetDatabase.LoadAssetAtPath<AudioClip>(path); }
        return c;
    }

    // ---------------- 入室・退室の記録 ----------------
    static bool joinRetried;
    const string JoinScriptPath = "Assets/My script/drone/JoinLog.cs";
    const string JoinAssetPath = "Assets/My script/drone/JoinLog.asset";

    static void SetupJoinLog(StringBuilder log)
    {
        JoinLog jl = Object.FindObjectOfType<JoinLog>(true);
        if (jl == null)
        {
            var go = new GameObject("JoinLog");
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            jl = UdonSharpEditor.UdonSharpUndo.AddComponent<JoinLog>(go);
        }
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.cockpitRoot == null) continue;
            Undo.RecordObject(c, "Flight setup");
            c.joinLog = jl;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            n++;
        }
        log.AppendLine("入室記録: " + n + " 機に接続");
    }

    // ---------------- 撃墜ランキングの掲示板（出現地点の前に 4 枚） ----------------
    // ---------------- 音の大きさの設定パネル（main room・掲示板の右） ----------------
    static void SetupVolumePanel(StringBuilder log)
    {
        GameObject rk = GameObject.Find("Rankings");
        if (rk == null) { log.AppendLine("音量: Rankings が無いので置けない"); return; }
        Material back = LoadOrCreateMat("RankingBoard", Shader.Find("Standard"));
        Material edge = UnlitMat("RankingEdge", new Color(0.35f, 0.95f, 1f, 1f), false, 2000);
        Material btnM = UnlitMat("VolumeButton", new Color(0.12f, 0.35f, 0.45f, 1f), false, 2000);

        var root = new GameObject("VolumePanel");
        Undo.RegisterCreatedObjectUndo(root, "Flight setup");
        root.transform.SetParent(rk.transform, false);
        const float W = 2.3f, H = 2.7f;
        root.transform.localPosition = new Vector3(2.85f + 0.35f + W * 0.5f, 1.2f + H * 0.5f, 0f);
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.name = "Plate";
        plate.transform.SetParent(root.transform, false);
        plate.transform.localPosition = new Vector3(0f, 0f, 0.05f);
        plate.transform.localScale = new Vector3(W, H, 0.08f);
        plate.GetComponent<MeshRenderer>().sharedMaterial = back;
        Quad(root.transform, "EdgeT", new Vector3(0f, H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
        Quad(root.transform, "EdgeB", new Vector3(0f, -H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
        Quad(root.transform, "EdgeL", new Vector3(-W * 0.5f, 0f, -0.001f), new Vector2(0.025f, H), edge);
        Quad(root.transform, "EdgeR", new Vector3(W * 0.5f, 0f, -0.001f), new Vector2(0.025f, H), edge);

        var vs = UdonSharpEditor.UdonSharpUndo.AddComponent<VolumeSettings>(root);
        PanelText(root.transform, "Title", "<b>SOUND VOLUME</b>\n<size=60%><color=#7fa8b0>Only for you. Saved when you leave.</color></size>", new Vector3(0f, H * 0.5f - 0.28f, -0.01f), W - 0.2f, 1.0f, TextAlignmentOptions.Center);
        string[] labels = { "ENGINE", "RADIO  (AMA / COMMAND)", "TEAM RADIO  (VOICE CHAT)", "GUIDANCE VOICE", "WEAPONS / EXPLOSIONS", "ALARMS" };
        var vals = new TextMeshPro[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            float y = H * 0.5f - 0.75f - i * 0.33f;
            PanelText(root.transform, "Label" + i, labels[i], new Vector3(-0.42f, y, -0.01f), 1.25f, 0.62f, TextAlignmentOptions.Left);
            vals[i] = PanelText(root.transform, "Value" + i, "100%", new Vector3(0.66f, y, -0.01f), 0.4f, 0.75f, TextAlignmentOptions.Center);
            for (int k = 0; k < 2; k++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 当たり判定付き（押せる）
                b.name = k == 0 ? "Down" + i : "Up" + i;
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = new Vector3(k == 0 ? 0.33f : 0.99f, y, -0.02f);
                b.transform.localScale = new Vector3(0.24f, 0.24f, 0.08f);
                b.GetComponent<MeshRenderer>().sharedMaterial = btnM;
                PanelText(root.transform, b.name + "Mark", k == 0 ? "-" : "+", new Vector3(b.transform.localPosition.x, y, -0.07f), 0.24f, 1.1f, TextAlignmentOptions.Center);
                var vb = UdonSharpEditor.UdonSharpUndo.AddComponent<VolumeButton>(b);
                vb.settings = vs;
                vb.cat = i;
                vb.delta = k == 0 ? -1 : 1;
                UdonSharpEditorUtility.CopyProxyToUdon(vb);
                UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(vb);
                if (ub != null) { ub.interactText = k == 0 ? "音量を下げる" : "音量を上げる"; ub.proximity = 3f; }
            }
        }
        vs.valueTexts = vals;

        // 音の種類ごとに分ける
        var radio = new System.Collections.Generic.HashSet<AudioSource>();
        var voice = new System.Collections.Generic.HashSet<AudioSource>();
        var alarm = new System.Collections.Generic.HashSet<AudioSource>();
        var weapon = new System.Collections.Generic.HashSet<AudioSource>();
        var engine = new System.Collections.Generic.HashSet<AudioSource>();
        System.Action<System.Collections.Generic.HashSet<AudioSource>, AudioSource> add = (set, a) => { if (a != null) set.Add(a); };
        var ctrls = Object.FindObjectsOfType<controller>(true);
        foreach (var c in ctrls)
        {
            add(engine, c.engineAudio);
            add(radio, c.enemyRadio);
            add(voice, c.voiceAudio);
            add(alarm, c.stallWarningAudio); add(alarm, c.stallAudio); add(alarm, c.alarmAudio); add(alarm, c.cockpitAudio);
            add(weapon, c.gunAudio); add(weapon, c.crashAudio);
        }
        foreach (var vr in Object.FindObjectsOfType<VoiceRelay>(true)) { add(voice, vr.voiceOut); add(radio, vr.radioOut); }
        foreach (var gm in Object.FindObjectsOfType<GameMode>(true)) add(radio, gm.radio);
        foreach (var gs in Object.FindObjectsOfType<GunnerStation>(true)) { add(alarm, gs.stallWarnAudio); add(alarm, gs.stallAudio); add(alarm, gs.alarmAudio); add(weapon, gs.gunAudio); }
        // 機体・船・敵機・標的・共有の爆発に付いている残りの音は「武器・爆発」
        var weaponRoots = new System.Collections.Generic.List<Transform>();
        foreach (var c in ctrls) if (c.car != null) weaponRoots.Add(c.car.transform);
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true)) weaponRoots.Add(ws.transform);
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true)) { if (ed.rb != null) weaponRoots.Add(ed.rb.transform); add(weapon, ed.gunAudio); add(weapon, ed.missileBoomAudio); }
        foreach (var ft in Object.FindObjectsOfType<FlightTarget>(true)) add(weapon, ft.explosionAudio);
        foreach (var nm in new[] { "CrashExplosion", "MissileSplash", "MissileExplosion" }) { var g = GameObject.Find(nm); if (g != null) weaponRoots.Add(g.transform); }
        foreach (var t in weaponRoots)
            foreach (var a in t.GetComponentsInChildren<AudioSource>(true))
                if (!engine.Contains(a) && !radio.Contains(a) && !voice.Contains(a) && !alarm.Contains(a)) weapon.Add(a);

        Undo.RecordObject(vs, "Flight setup");
        vs.radioSources = new System.Collections.Generic.List<AudioSource>(radio).ToArray();
        vs.voiceSources = new System.Collections.Generic.List<AudioSource>(voice).ToArray();
        vs.alarmSources = new System.Collections.Generic.List<AudioSource>(alarm).ToArray();
        vs.weaponSources = new System.Collections.Generic.List<AudioSource>(weapon).ToArray();
        UdonSharpEditorUtility.CopyProxyToUdon(vs);
        foreach (var c in ctrls) { Undo.RecordObject(c, "Flight setup"); c.volume = vs; UdonSharpEditorUtility.CopyProxyToUdon(c); }
        foreach (var pr in Object.FindObjectsOfType<PilotRadio>(true)) { Undo.RecordObject(pr, "Flight setup"); pr.volume = vs; UdonSharpEditorUtility.CopyProxyToUdon(pr); }
        log.AppendLine("音量: パネルを掲示板の右に設置（エンジン " + engine.Count + " / 無線 " + radio.Count + " / 案内 " + voice.Count + " / 武器・爆発 " + weapon.Count + " / 警報 " + alarm.Count + " 個の音）");
    }

    static TextMeshPro PanelText(Transform p, string name, string text, Vector3 lp, float w, float size, TextAlignmentOptions al)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.localPosition = lp;
        var t = go.AddComponent<TextMeshPro>();
        t.text = text;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = al;
        t.enableWordWrapping = false;
        t.richText = true;
        t.rectTransform.sizeDelta = new Vector2(w, size * 0.3f);
        return t;
    }

    static void SetupBoards(StringBuilder log)
    {
        JoinLog jl = Object.FindObjectOfType<JoinLog>(true);
        if (jl == null) { log.AppendLine("ランキング: JoinLog が無い"); return; }
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>(true);
        Transform sp = (desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null) ? desc.spawns[0] : (desc != null ? desc.transform : null);
        if (sp == null) { log.AppendLine("ランキング: 出現地点が見つからない"); return; }

        GameObject old = GameObject.Find("Rankings");
        Vector3 keepPos = Vector3.zero; Quaternion keepRot = Quaternion.identity; bool keep = false;
        if (old != null) { keepPos = old.transform.position; keepRot = old.transform.rotation; keep = true; Undo.DestroyObjectImmediate(old); }   // 手で動かした位置はそのまま
        var root = new GameObject("Rankings");
        Undo.RegisterCreatedObjectUndo(root, "Flight setup");
        Vector3 f = sp.forward; f.y = 0f;
        if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
        f.Normalize();
        // mainRoom（最初にいる部屋）の中に置く: 出現地点から前を見て、壁があればその壁の手前に掛ける
        GameObject mainRoom = GameObject.Find("mainRoom");
        if (mainRoom != null) root.transform.SetParent(mainRoom.transform, true);
        if (keep) root.transform.SetPositionAndRotation(keepPos, keepRot);
        else
        {
            Vector3 eye = sp.position + Vector3.up * 1.5f;
            float dist = 6f;
            RaycastHit wh;
            if (Physics.Raycast(eye, f, out wh, 30f, ~((1 << 9) | (1 << 10) | (1 << 18)), QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(wh.distance - 0.15f, 2.5f);
            root.transform.SetPositionAndRotation(sp.position + f * dist, Quaternion.LookRotation(f, Vector3.up));   // 出現地点の方を向く
            log.AppendLine("ランキング: 出現地点 " + sp.position.ToString("F1") + " から前へ " + dist.ToString("F1") + "m" + (dist < 6f || wh.collider != null ? "（壁 " + (wh.collider != null ? wh.collider.name : "-") + " の手前）" : ""));
        }
        if (mainRoom != null)
        {
            Bounds rb = new Bounds(mainRoom.transform.position, Vector3.zero);
            bool anyR = false;
            foreach (var r in mainRoom.GetComponentsInChildren<Renderer>(true)) { if (!anyR) { rb = r.bounds; anyR = true; } else rb.Encapsulate(r.bounds); }
            log.AppendLine("ランキング: mainRoom の中に配置 / 部屋の範囲 " + rb.min.ToString("F0") + "〜" + rb.max.ToString("F0") + (anyR && rb.Contains(root.transform.position + Vector3.up) ? "（部屋の中）" : "（※部屋の外かも。手で動かしてください）"));
        }
        else log.AppendLine("ランキング: mainRoom が見つからないので出現地点の前に配置");

        Material back = LoadOrCreateMat("RankingBoard", Shader.Find("Standard"));
        back.SetColor("_Color", new Color(0.03f, 0.06f, 0.1f));
        back.SetFloat("_Glossiness", 0.3f);
        EditorUtility.SetDirty(back);
        Material edge = UnlitMat("RankingEdge", new Color(0.35f, 0.95f, 1f, 1f), false, 2000);

        // 2 × 2 に並べる: 上 = 今回（ワールドにいる間の最高）、下 = 歴代（自己ベスト）。左 = 1 人用、右 = 2 人用
        string[] titles = { "SOLO  -  THIS SESSION", "DUO  -  THIS SESSION", "SOLO  -  ALL-TIME", "DUO  -  ALL-TIME" };
        const float W = 2.6f, H = 1.7f, GAP = 0.25f;
        for (int k = 0; k < 4; k++)
        {
            int col = k % 2, row = k / 2;
            var b = new GameObject(k < 2 ? (col == 0 ? "SoloBoard" : "DuoBoard") : (col == 0 ? "SoloAllTime" : "DuoAllTime"));
            b.transform.SetParent(root.transform, false);
            b.transform.localPosition = new Vector3((col == 0 ? -1f : 1f) * (W + GAP) * 0.5f, 1.2f + (row == 0 ? H + GAP : 0f) + H * 0.5f, 0f);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            plate.transform.SetParent(b.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            plate.transform.localScale = new Vector3(W, H, 0.08f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = back;
            // 枠
            Quad(b.transform, "EdgeT", new Vector3(0f, H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
            Quad(b.transform, "EdgeB", new Vector3(0f, -H * 0.5f, -0.001f), new Vector2(W, 0.025f), edge);
            Quad(b.transform, "EdgeL", new Vector3(-W * 0.5f, 0f, -0.001f), new Vector2(0.025f, H), edge);
            Quad(b.transform, "EdgeR", new Vector3(W * 0.5f, 0f, -0.001f), new Vector2(0.025f, H), edge);
            // 列の見出し
            var hd = new GameObject("Header");
            hd.transform.SetParent(b.transform, false);
            hd.transform.localPosition = new Vector3(0f, H * 0.5f - 0.42f, -0.01f);
            var ht = hd.AddComponent<TextMeshPro>();
            ht.text = "<pos=68%><color=#ff6a5a>DRONE</color><pos=82%><color=#ffa030>SHIP</color>";
            ht.fontSize = 0.9f;
            ht.alignment = TextAlignmentOptions.TopLeft;
            ht.enableWordWrapping = false;
            ht.richText = true;
            ht.rectTransform.sizeDelta = new Vector2(W - 0.2f, 0.2f);
            // 本文
            var tx = new GameObject("Text");
            tx.transform.SetParent(b.transform, false);
            tx.transform.localPosition = new Vector3(0f, -0.04f, -0.01f);
            var tmp = tx.AddComponent<TextMeshPro>();
            tmp.fontSize = 1.0f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = false;
            tmp.richText = true;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.rectTransform.sizeDelta = new Vector2(W - 0.2f, H - 0.12f);
            tmp.text = titles[k];

            if (k < 2)
            {
                var lb = UdonSharpEditor.UdonSharpUndo.AddComponent<Leaderboard>(b);
                lb.text = tmp;
                lb.title = titles[k];
                UdonSharpEditorUtility.CopyProxyToUdon(lb);
                Undo.RecordObject(jl, "Flight setup");
                if (col == 0) jl.soloBoard = lb; else jl.duoBoard = lb;
            }
            else
            {
                var ab = UdonSharpEditor.UdonSharpUndo.AddComponent<AllTimeBoard>(b);
                ab.text = tmp;
                ab.title = titles[k];
                ab.duo = col == 1;
                UdonSharpEditorUtility.CopyProxyToUdon(ab);
            }
        }
        UdonSharpEditorUtility.CopyProxyToUdon(jl);

        // ハード / 普通モードの切り替えボタン（掲示板の左。全員共通）
        var btnRoot = new GameObject("ModeButton");
        btnRoot.transform.SetParent(root.transform, false);
        btnRoot.transform.localPosition = new Vector3(-(W + GAP) - 1.0f, 0f, 0f);
        var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(post.GetComponent<Collider>());
        post.name = "Post";
        post.transform.SetParent(btnRoot.transform, false);
        post.transform.localPosition = new Vector3(0f, 0.55f, 0.1f);
        post.transform.localScale = new Vector3(0.25f, 1.1f, 0.25f);
        post.GetComponent<MeshRenderer>().sharedMaterial = back;
        var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 当たり判定付き（押せる）
        btn.name = "Button";
        btn.transform.SetParent(btnRoot.transform, false);
        btn.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        btn.transform.localScale = new Vector3(0.9f, 0.45f, 0.2f);
        Material hardM = UnlitMat("ModeHard", new Color(0.9f, 0.15f, 0.12f, 1f), false, 2000);
        Material normM = UnlitMat("ModeNormal", new Color(0.2f, 0.85f, 0.4f, 1f), false, 2000);
        var btnR = btn.GetComponent<MeshRenderer>();
        btnR.sharedMaterial = normM;
        var lab = new GameObject("Label");
        lab.transform.SetParent(btnRoot.transform, false);
        lab.transform.localPosition = new Vector3(0f, 1.85f, -0.02f);
        var labT = lab.AddComponent<TextMeshPro>();
        labT.fontSize = 1.1f;
        labT.alignment = TextAlignmentOptions.Center;
        labT.enableWordWrapping = false;
        labT.richText = true;
        labT.rectTransform.sizeDelta = new Vector2(2.2f, 0.8f);
        labT.text = "NORMAL MODE";
        var gm = UdonSharpEditor.UdonSharpUndo.AddComponent<GameMode>(btn);
        gm.label = labT;
        gm.lamp = btnR;
        gm.hardMat = hardM;
        gm.normalMat = normM;
        gm.sightseeingMat = UnlitMat("ModeSightseeing", new Color(0.2f, 0.55f, 1f, 1f), false, 2000);   // 遊覧モードは青
        var gmPlanes = new System.Collections.Generic.List<controller>();
        foreach (var c in Object.FindObjectsOfType<controller>(true)) if (c.car != null && IsR35(c.car)) gmPlanes.Add(c);
        gm.planes = gmPlanes.ToArray();   // 誰かが飛んでいる間は切り替えできない
        // 「偵察任務」の無線をワールドの全員に流すスピーカー（どこにいても同じ大きさ）
        var gmAu = btn.GetComponent<AudioSource>();
        if (gmAu == null) gmAu = btn.AddComponent<AudioSource>();
        gmAu.playOnAwake = false;
        gmAu.spatialBlend = 0f;
        gmAu.volume = 1f;
        gm.radio = gmAu;
        gm.reconClips = new[] { LoadClip("gaur_recon1.wav"), LoadClip("gaur_recon2.wav") };
        gm.fleetClips = new[] { LoadClip("gaur_fleet1.wav"), LoadClip("gaur_fleet2.wav") };
        gm.fleetAnnounceAt = 60f;   // 戦闘が始まって 1 分後に「艦隊が向かってきている」と流す（2 分後に空母が現れる）
        UdonSharpEditorUtility.CopyProxyToUdon(gm);
        UdonBehaviour gmub = UdonSharpEditorUtility.GetBackingUdonBehaviour(gm);
        if (gmub != null) { gmub.interactText = "モード切り替え"; gmub.proximity = 3f; }
        // 機体と船に知らせる
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            Undo.RecordObject(c, "Flight setup");
            c.gameMode = gm;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }
        foreach (var ws in Object.FindObjectsOfType<WarShip>(true))
        {
            Undo.RecordObject(ws, "Flight setup");
            ws.gameMode = gm;
            UdonSharpEditorUtility.CopyProxyToUdon(ws);
        }
        foreach (var ed in Object.FindObjectsOfType<EnemyDrone>(true))
        {
            Undo.RecordObject(ed, "Flight setup");
            ed.gameMode = gm;
            UdonSharpEditorUtility.CopyProxyToUdon(ed);
        }
        log.AppendLine("ランキング: 掲示板 4 枚とモード切り替えボタンを " + root.transform.position.ToString("F1") + " に設置");
    }

    // ---------------- プレイヤーは機体をすり抜ける ----------------
    // VRChat の Walkthrough レイヤー(17)は「プレイヤーとだけ当たらない」。海・島・弾・ミサイルとは今まで通り当たる
    static void SetupWalkthrough(StringBuilder log)
    {
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (c.car == null || !IsR35(c.car)) continue;
            foreach (var col in c.car.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;   // クリックで乗る当たり判定はそのまま
                if (col.gameObject.layer == 17) continue;
                Undo.RecordObject(col.gameObject, "Flight setup");
                col.gameObject.layer = 17;
                PrefabUtility.RecordPrefabInstancePropertyModifications(col.gameObject);
                n++;
            }
        }
        log.AppendLine("すり抜け: " + n + " 個の当たり判定を Walkthrough レイヤーに");
    }
    const string CloakScriptPath = "Assets/My script/drone/StationCloak.cs";
    const string CloakAssetPath = "Assets/My script/drone/StationCloak.asset";
    const float CloakRadius = 350f;
    const float CloakHideDistance = 2000f;   // これより遠いと高さに関係なく見えない

    static void RemoveElevator(StringBuilder log)
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (var nm in new[] { "elevater_ex", "MainToElavater", "StationCloak", "spaceElavater" })
        {
            GameObject g;
            int guard = 0;
            while ((g = FindAnyByName(nm)) != null && guard++ < 10) { Undo.DestroyObjectImmediate(g); names.Add(nm); }
        }
        // エレベーター用のスクリプトが付いた物（どこかに残っていれば）
        foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn == "RuningElavater" || tn == "DownElavater" || tn == "StationCloak") { names.Add(mb.gameObject.name); Undo.DestroyObjectImmediate(mb.gameObject); }
        }
        // スクリプトの本体も消す
        bool delScripts = false;
        if (AssetDatabase.IsValidFolder("Assets/My script/Elavater")) delScripts = AssetDatabase.DeleteAsset("Assets/My script/Elavater");
        log.AppendLine("宇宙エレベーター: " + (names.Count > 0 ? string.Join(", ", names.ToArray()) : "シーンに残り無し") + " を削除" + (delScripts ? " / スクリプトも削除" : ""));
    }

    static GameObject FindAnyByName(string nm)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true)) if (t.name == nm) return t.gameObject;
        return null;
    }

    static void CreateCloak(StringBuilder log)
    {
        GameObject old = GameObject.Find("StationCloak");
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject root = GameObject.Find("elevater_ex");
        if (root == null) { log.AppendLine("ERROR: elevater_ex が見つからない"); return; }
        Vector3 center = new Vector3(1801.6f, 0f, -148f);
        GameObject eg = GameObject.Find("spaceElavater");
        if (eg != null) center = eg.transform.position;

        // エレベーター周辺（水平 600m 以内）にある描画物だけを対象にする
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            Vector3 p = r.transform.position;
            float dx = p.x - center.x, dz = p.z - center.z;
            if (dx * dx + dz * dz > 600f * 600f) continue;
            if (r is ParticleSystemRenderer) continue;
            list.Add(r);
        }

        var go = new GameObject("StationCloak");
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        go.transform.position = new Vector3(center.x, 0f, center.z);

        // 隠している間に出す雲のかたまり（もこもこ 22 個・軽量）
        Shader puffSh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        Texture2D puff = ImportTex(FxDir + "/cloud_puff.png", false);
        Material puffMat = LoadOrCreateMat("CloudPuff", puffSh);
        puffMat.SetTexture("_MainTex", puff);
        puffMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        puffMat.renderQueue = 3000;
        EditorUtility.SetDirty(puffMat);

        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;
        var bank = new GameObject("CloudBank");
        bank.transform.SetParent(go.transform, false);
        bank.transform.position = new Vector3(center.x, (seaY + CloudBottom) * 0.5f + 20f, center.z);
        var ps = bank.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 4321;
        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = 1000000f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(220f, 380f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.85f), new Color(0.88f, 0.9f, 0.94f, 0.95f));
        main.maxParticles = 30;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(CloakRadius * 1.4f, CloudBottom - seaY - 60f, CloakRadius * 1.4f);
        var pr = bank.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = puffMat;
        pr.sortMode = ParticleSystemSortMode.Distance;
        pr.maxParticleSize = 10f;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        bank.SetActive(false);

        var cloak = UdonSharpEditor.UdonSharpUndo.AddComponent<StationCloak>(go);
        cloak.stationRenderers = list.ToArray();
        cloak.cloudBank = bank;
        cloak.center = center;
        cloak.showAltitude = CloudBottom;
        cloak.showRadius = CloakRadius;
        cloak.hideDistance = CloakHideDistance;
        UdonSharpEditorUtility.CopyProxyToUdon(cloak);
        log.AppendLine("ステーション隠し: 対象 " + list.Count + " 個 / 中心 " + center.ToString("F0") + " / 高さ " + CloudBottom + "m 未満・" + CloakRadius + "m 以上離れると隠す / " + CloakHideDistance + "m 以上は常に隠す");
    }

    // ---------------- 無線 ----------------
    static bool radioRetried;
    const string RadioScriptPath = "Assets/My script/drone/PilotRadio.cs";
    const string RadioAssetPath = "Assets/My script/drone/PilotRadio.asset";

    static void CreateRadio(StringBuilder log)
    {
        GameObject old = GameObject.Find("PilotRadio");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var go = new GameObject("PilotRadio");
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        var radio = UdonSharpEditor.UdonSharpUndo.AddComponent<PilotRadio>(go);
        radio.planes = Object.FindObjectsOfType<controller>(true);
        UdonSharpEditorUtility.CopyProxyToUdon(radio);
        log.AppendLine("無線: " + radio.planes.Length + " 機を登録");
    }

    // ---------------- 波のある海 ----------------
    static bool oceanRetried;
    const string OceanScriptPath = "Assets/My script/drone/OceanFollow.cs";
    const string OceanAssetPath = "Assets/My script/drone/OceanFollow.asset";
    const string OldSeaMatPath = "Assets/My material/sea_water.mat";

    static void CreateOcean(StringBuilder log)
    {
        // 元の海（画像を貼った板）は見た目だけ消す。当たり判定はそのまま残す
        float seaY = -27.7f;
        bool foundY = false;
        int hidden = 0;
        var oldMat = AssetDatabase.LoadAssetAtPath<Material>(OldSeaMatPath);
        if (oldMat != null)
        {
            foreach (var r in Object.FindObjectsOfType<MeshRenderer>(true))
            {
                if (r.sharedMaterial != oldMat) continue;
                if (!foundY) { seaY = r.transform.position.y; foundY = true; }
                if (r.enabled)
                {
                    Undo.RecordObject(r, "Flight setup");
                    r.enabled = false;
                    hidden++;
                }
            }
        }

        GameObject old = GameObject.Find("Ocean");
        if (old != null) Undo.DestroyObjectImmediate(old);

        Shader sh = Shader.Find("FlightUI/Ocean");
        if (sh == null) { log.AppendLine("ERROR: 海のシェーダーが見つからない"); return; }
        Texture2D noise = ImportTex(FxDir + "/cloud_noise.png", true);
        Material m = LoadOrCreateMat("Ocean", sh);
        m.SetTexture("_NoiseTex", noise);
        EditorUtility.SetDirty(m);

        var go = new GameObject("Ocean");
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        go.transform.position = new Vector3(CloudCenter.x, seaY, CloudCenter.z);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = GetOceanMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        var follow = UdonSharpEditor.UdonSharpUndo.AddComponent<OceanFollow>(go);
        follow.seaLevel = seaY;
        UdonSharpEditorUtility.CopyProxyToUdon(follow);

        // 高度表示を「海面からの高さ」にする
        foreach (var c in Object.FindObjectsOfType<controller>(true))
        {
            if (Mathf.Approximately(c.seaLevelY, seaY)) continue;
            Undo.RecordObject(c, "Flight setup");
            c.seaLevelY = seaY;
            UdonSharpEditorUtility.CopyProxyToUdon(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        }

        log.AppendLine("海: 海面 " + seaY.ToString("F1") + "m 元の板 " + hidden + " 枚を非表示 / 波のある海を設置");
    }

    // 中心ほど細かく、外ほど粗い 1 枚の網目（半径 8km・約 6.3 万頂点）
    static Mesh GetOceanMesh()
    {
        string path = FxDir + "/OceanMesh.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        const int N = 250;
        const float R = 8000f;
        const float P = 2.6f;
        var verts = new Vector3[(N + 1) * (N + 1)];
        var normals = new Vector3[verts.Length];
        for (int j = 0; j <= N; j++)
        for (int i = 0; i <= N; i++)
        {
            float u = i / (float)N * 2f - 1f;
            float w = j / (float)N * 2f - 1f;
            float x = Mathf.Sign(u) * R * Mathf.Pow(Mathf.Abs(u), P);
            float z = Mathf.Sign(w) * R * Mathf.Pow(Mathf.Abs(w), P);
            verts[j * (N + 1) + i] = new Vector3(x, 0f, z);
            normals[j * (N + 1) + i] = Vector3.up;
        }
        var tris = new int[N * N * 6];
        int t = 0;
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            int a = j * (N + 1) + i;
            int b = a + 1;
            int c = a + (N + 1);
            int d = c + 1;
            tris[t++] = a; tris[t++] = c; tris[t++] = b;
            tris[t++] = b; tris[t++] = c; tris[t++] = d;
        }
        var mesh = new Mesh();
        mesh.name = "OceanMesh";
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(R * 2f, 40f, R * 2f));
        Directory.CreateDirectory(FxDir);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // ---------------- 描画距離・霞 ----------------
    const float ViewDistance = 12000f;     // カメラの描画距離（m）
    const float FogStart = 1500f;          // ここから霞み始める
    const float FogEnd = 11000f;           // ここで完全に霞む（描画距離より手前にして、切れ目を見せない）
    static readonly Color FogColor = new Color(0.70f, 0.79f, 0.88f, 1f);   // 地平線の空の色に近い薄い青

    static void SetupView(StringBuilder log)
    {
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>(true);
        Camera cam = null;
        if (desc != null && desc.ReferenceCamera != null) cam = desc.ReferenceCamera.GetComponent<Camera>();
        if (cam != null)
        {
            Undo.RecordObject(cam, "Flight setup");
            cam.farClipPlane = ViewDistance;
            log.AppendLine("描画距離: " + cam.name + " → " + ViewDistance + "m");
        }
        else log.AppendLine("WARNING: VRChat の基準カメラ(ReferenceCamera)が見つからない");

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;
        log.AppendLine("霞: " + FogStart + "〜" + FogEnd + "m");
    }

    // ---------------- 雲の層 ----------------
    const float CloudBottom = 430f;   // 下の雲の面の高さ（ここより上は下から見えない）
    const float CloudTop = 600f;      // 上の雲の面の高さ
    const float CloudSize = 12000f;   // 雲の層の広さ（m）
    static readonly Vector3 CloudCenter = new Vector3(150f, 0f, 75f);
    // もこもこの雲（大きな半透明の板）は重いので、軽さ優先で使わない。true にすると出る
    const bool CloudPuffs = false;
    const float ElevatorCoverSize = 2200f;   // エレベーターの上の丸い雲の直径（端は薄くなるので実際は約1.6km）

    static void CreateClouds(StringBuilder log)
    {
        GameObject old = GameObject.Find("CloudLayer");
        if (old != null) Undo.DestroyObjectImmediate(old);

        Shader layerSh = Shader.Find("FlightUI/CloudLayer");
        Shader puffSh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (layerSh == null || puffSh == null) { log.AppendLine("ERROR: 雲のシェーダーが見つからない"); return; }
        Texture2D noise = ImportTex(FxDir + "/cloud_noise.png", true);
        Texture2D puff = ImportTex(FxDir + "/cloud_puff.png", false);

        // 下の面: ところどころに雲（切れ間から空が見える）
        Material lower = LoadOrCreateMat("CloudLower", layerSh);
        lower.SetTexture("_MainTex", noise);
        lower.SetFloat("_Scale", 1800f);
        lower.SetFloat("_Scale2", 450f);
        lower.SetColor("_TopColor", new Color(0.97f, 0.98f, 1f, 1f));
        lower.SetColor("_BottomColor", new Color(0.70f, 0.73f, 0.78f, 1f));
        lower.SetFloat("_Density", 0.95f);
        lower.SetFloat("_MinAlpha", 0f);
        lower.SetFloat("_Coverage", 0.52f);
        lower.SetVector("_Speed", new Vector4(0.004f, 0.002f, 0f, 0f));
        lower.SetFloat("_EdgeFade", 0.3f);
        lower.SetFloat("_ZWriteOn", 0f);
        lower.SetFloat("_Round", 0f);
        lower.renderQueue = 2981;
        EditorUtility.SetDirty(lower);

        // 上の面: 切れ切れの雲。上から見たときの雲海
        Material upper = LoadOrCreateMat("CloudUpper", layerSh);
        upper.SetTexture("_MainTex", noise);
        upper.SetFloat("_Scale", 1300f);
        upper.SetFloat("_Scale2", 380f);
        upper.SetColor("_TopColor", Color.white);
        upper.SetColor("_BottomColor", new Color(0.72f, 0.75f, 0.8f, 1f));
        upper.SetFloat("_Density", 0.9f);
        upper.SetFloat("_MinAlpha", 0f);
        upper.SetFloat("_Coverage", 0.6f);
        upper.SetVector("_Speed", new Vector4(-0.003f, 0.0025f, 0f, 0f));
        upper.SetFloat("_EdgeFade", 0.3f);
        upper.SetFloat("_ZWriteOn", 0f);
        upper.SetFloat("_Round", 0f);
        upper.renderQueue = 2985;

        // 宇宙エレベーターの真上だけ、丸く厚い雲で覆って隠す
        Material cover = LoadOrCreateMat("CloudCover", layerSh);
        cover.SetTexture("_MainTex", noise);
        cover.SetFloat("_Scale", 900f);
        cover.SetFloat("_Scale2", 300f);
        cover.SetColor("_TopColor", new Color(0.97f, 0.98f, 1f, 1f));
        cover.SetColor("_BottomColor", new Color(0.66f, 0.69f, 0.75f, 1f));
        cover.SetFloat("_Density", 1f);
        cover.SetFloat("_MinAlpha", 0.97f);
        cover.SetFloat("_Coverage", 0.4f);
        cover.SetVector("_Speed", new Vector4(0.002f, 0.001f, 0f, 0f));
        cover.SetFloat("_EdgeFade", 0.45f);
        cover.SetFloat("_ZWriteOn", 1f);
        cover.SetFloat("_Round", 1f);
        cover.renderQueue = 2980;
        EditorUtility.SetDirty(cover);
        EditorUtility.SetDirty(upper);

        // 間のもこもこ（雲の中を飛んでいる感じを出す）
        Material puffMat = LoadOrCreateMat("CloudPuff", puffSh);
        puffMat.SetTexture("_MainTex", puff);
        puffMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        puffMat.renderQueue = 3000;
        EditorUtility.SetDirty(puffMat);

        var root = new GameObject("CloudLayer");
        Undo.RegisterCreatedObjectUndo(root, "Flight setup");
        root.transform.position = CloudCenter;

        CloudDeck(root.transform, "LowerDeck", CloudBottom, lower);
        CloudDeck(root.transform, "UpperDeck", CloudTop, upper);

        // エレベーターの位置（見つからなければ既定の位置）
        Vector3 elev = new Vector3(1801.6f, 0f, -148f);
        GameObject eg = GameObject.Find("spaceElavater");
        if (eg != null) elev = eg.transform.position;
        var coverQ = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(coverQ.GetComponent<Collider>());
        coverQ.SetActive(false);   // 宇宙エレベーターは消したので、上の丸い雲は使わない
        coverQ.name = "ElevatorCover";
        coverQ.transform.SetParent(root.transform, true);
        coverQ.transform.position = new Vector3(elev.x, CloudBottom - 10f, elev.z);
        coverQ.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        coverQ.transform.localScale = new Vector3(ElevatorCoverSize, ElevatorCoverSize, 1f);
        var cr = coverQ.GetComponent<MeshRenderer>();
        cr.sharedMaterial = cover;
        cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cr.receiveShadows = false;

        if (!CloudPuffs)
        {
            log.AppendLine("雲の層: ところどころ / 高さ " + CloudBottom + "〜" + CloudTop + "m / エレベーター上の丸い雲 " + elev.ToString("F0"));
            return;
        }

        var go = new GameObject("Puffs");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, (CloudBottom + CloudTop) * 0.5f + 20f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 12345;   // 全員同じ配置になるように固定
        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = 1000000f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(150f, 350f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.45f), new Color(0.9f, 0.92f, 0.95f, 0.75f));
        main.gravityModifier = 0f;
        main.maxParticles = 160;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 150) });
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(CloudSize * 0.66f, CloudTop - CloudBottom - 40f, CloudSize * 0.66f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = puffMat;
        r.sortMode = ParticleSystemSortMode.Distance;
        r.maxParticleSize = 10f;   // 近くで画面いっぱいになっても切れないように
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        log.AppendLine("雲の層: 高さ " + CloudBottom + "〜" + CloudTop + "m 広さ " + CloudSize + "m 中心 " + CloudCenter);
    }

    static void CloudDeck(Transform parent, string name, float y, Material m)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(parent, false);
        q.transform.localPosition = new Vector3(0f, y, 0f);
        q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 表が上を向く
        q.transform.localScale = new Vector3(CloudSize, CloudSize, 1f);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static Material LoadOrCreateMat(string name, Shader sh)
    {
        string path = FxDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != sh) m.shader = sh;
        return m;
    }

    static Texture2D ImportTex(string path, bool repeat)
    {
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            bool changed = false;
            var wm = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (imp.wrapMode != wm) { imp.wrapMode = wm; changed = true; }
            if (!repeat && !imp.alphaIsTransparency) { imp.alphaIsTransparency = true; changed = true; }
            if (changed) imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------- コックピット画面 ----------------
    static readonly Color CCyan = new Color(0.35f, 0.95f, 1f, 1f);
    static readonly Color CCyanDim = new Color(0.35f, 0.95f, 1f, 0.65f);
    static readonly Color CWhite = new Color(0.92f, 1f, 1f, 1f);
    static readonly Color CAmber = new Color(1f, 0.75f, 0.25f, 1f);

    // 画面全体の高さ調整（m）。上げたいときは大きく、下げたいときは小さく
    const float CockpitRaise = -0.12f;   // 身長 1.6m に固定したので、画面の基準（目の位置）を下げる
    const float CockpitScale = 0.85f;    // コックピットの画面・内装を全体に小さく（目の位置を中心に縮める）
    const float ChairScale = 0.8f;       // 椅子の見た目も小さく

    static Material s_consoleMat;
    static Material s_staticMat;   // パネルの赤い砂嵐（Panel() が各パネルに非表示で入れておく）

    static bool SetupCockpit(controller c, StringBuilder log)
    {
        // 内装のマテリアル（暗い金属）
        s_consoleMat = LoadOrCreateMat("CockpitConsole", Shader.Find("Standard"));
        s_consoleMat.SetColor("_Color", new Color(0.07f, 0.08f, 0.1f));
        s_consoleMat.SetFloat("_Metallic", 0.4f);
        s_consoleMat.SetFloat("_Glossiness", 0.45f);
        EditorUtility.SetDirty(s_consoleMat);
        // 砂嵐の模様（白黒のノイズ＋横すじ）を赤く表示する
        Texture2D noise = GenTex("static_noise.png", 128, (u, v) =>
        {
            float h = Mathf.Abs(Mathf.Sin(u * 1273.13f + v * 7841.77f) * 43758.5453f) % 1f;
            float band = (Mathf.Abs(Mathf.Sin(v * 91.7f) * 1531.3f) % 1f) > 0.85f ? 0.6f : 0f;
            float g = Mathf.Clamp01(h * 0.9f + band);
            return new Color(g, g, g, 0.35f + 0.6f * g);
        });
        var nimp = AssetImporter.GetAtPath(FxDir + "/static_noise.png") as TextureImporter;
        if (nimp != null && (nimp.wrapMode != TextureWrapMode.Repeat || nimp.filterMode != FilterMode.Point))
        {
            nimp.wrapMode = TextureWrapMode.Repeat;
            nimp.filterMode = FilterMode.Point;
            nimp.mipmapEnabled = false;
            nimp.SaveAndReimport();
            noise = AssetDatabase.LoadAssetAtPath<Texture2D>(FxDir + "/static_noise.png");
        }
        s_staticMat = TexMat("PanelStatic", noise, new Color(1f, 0.12f, 0.08f, 0.9f), 3001);
        s_staticMat.SetTextureScale("_MainTex", new Vector2(0.5f, 0.5f));
        EditorUtility.SetDirty(s_staticMat);
        Transform seat = c.transform;
        var st = c.GetComponent<VRCStation>();

        // 古い文字だけの HUD は消す（非表示にして参照を外す）
        if (c.hudText != null)
        {
            Undo.RecordObject(c.hudText.gameObject, "Flight setup");
            c.hudText.gameObject.SetActive(false);
            Undo.RecordObject(c, "Flight setup");
            c.hudText = null;
        }

        // 作り直す（レイアウト変更を反映するため毎回）
        // 以前は座席の子に置いていたが、座席の「選択中」ハイライトが画面にかかるので機体本体の子にする
        Transform old = seat.Find("Cockpit");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        if (c.car != null)
        {
            Transform old2 = c.car.transform.Find("Cockpit");
            if (old2 != null) Undo.DestroyObjectImmediate(old2.gameObject);
        }

        Material bg = UnlitMat("CockpitPanel", new Color(0.02f, 0.07f, 0.13f, 0.72f), true, 2990);
        Material frame = UnlitMat("CockpitFrame", CCyan, false, 2000);
        Material barBg = UnlitMat("CockpitBarBG", new Color(0.35f, 0.95f, 1f, 0.15f), true, 2995);
        Material amber = UnlitMat("CockpitAmber", CAmber, false, 2000);
        Material red = UnlitMat("CockpitRed", new Color(1f, 0.2f, 0.15f, 1f), false, 2000);

        Transform hip = (st != null && st.stationEnterPlayerLocation != null) ? st.stationEnterPlayerLocation : seat;
        var rootGo = new GameObject("Cockpit");
        Undo.RegisterCreatedObjectUndo(rootGo, "Flight setup");
        Transform root = rootGo.transform;
        root.SetParent(c.car != null ? c.car.transform : seat, false);
        // 原点 = 座った時の目の位置、+Z = 前
        root.position = hip.position + seat.up * (0.72f + CockpitRaise);
        root.rotation = seat.rotation;
        SetWorldScaleOne(root);
        root.localScale = root.localScale * CockpitScale;
        // 椅子の見た目を小さく（元の大きさを子の印に覚えておき、何回実行しても同じ大きさ）
        foreach (var chairR in seat.GetComponentsInChildren<MeshRenderer>(true))
        {
            Transform ct = chairR.transform;
            if (ct.IsChildOf(root)) continue;
            Transform mark = ct.Find("ChairBaseScale");
            if (mark == null)
            {
                var mk = new GameObject("ChairBaseScale");
                mk.transform.SetParent(ct, false);
                mk.transform.localPosition = ct.localScale;
                mark = mk.transform;
            }
            Undo.RecordObject(ct, "Flight setup");
            ct.localScale = mark.localPosition * ChairScale;
        }

        // 中央: 速度計・高度計（時計のようなメーター）と水平儀
        bool hudStyle = c.car != null && IsOmega(c.car);   // Omega: 機首に重ねる HUD なので、計器を少し上（目の高さ寄り）に
        Transform main = Panel(root, "MainPanel", new Vector3(0f, hudStyle ? -0.02f : -0.17f, 0.80f), 0.46f, 0.32f, bg, frame);
        Material white = UnlitMat("CockpitNeedle", CWhite, false, 2000);
        Material spdFace = TexMat("DialSpeed", GenTex("dial_speed.png", 256, (u, v) => DialPix(u, v, -135f, 270f, 6, 5)), Color.white, 2992);
        Material altFace = TexMat("DialAlt", GenTex("dial_alt.png", 256, (u, v) => DialPix(u, v, 0f, 360f, 10, 5)), Color.white, 2992);
        const float DR = 0.078f;
        Undo.RecordObject(c, "Flight setup");
        Txt(main, "SpdLabel", "SPEED  km/h", new Vector2(-0.15f, 0.135f), 0.16f, 0.12f, CCyanDim, TextAlignmentOptions.Center);
        Txt(main, "AltLabel", "ALTITUDE  m", new Vector2(0.15f, 0.135f), 0.16f, 0.12f, CCyanDim, TextAlignmentOptions.Center);
        var spdLabels = new string[7];
        var spdAngles = new float[7];
        for (int i = 0; i < 7; i++) { spdLabels[i] = (i * 50).ToString(); spdAngles[i] = -135f + i * 45f; }
        var altLabels = new string[10];
        var altAngles = new float[10];
        for (int i = 0; i < 10; i++) { altLabels[i] = i.ToString(); altAngles[i] = i * 36f; }
        c.cpSpdNeedle = Dial(main, "SpdDial", new Vector2(-0.15f, 0.03f), DR, spdFace, spdLabels, spdAngles, white, DR * 0.86f, 0.0045f);
        Transform altDial;
        c.cpAltNeedle = Dial(main, "AltDial", new Vector2(0.15f, 0.03f), DR, altFace, altLabels, altAngles, white, DR * 0.86f, 0.0035f);
        altDial = c.cpAltNeedle.parent;
        // 高度計の短い針（1周 = 10000m）
        var kp = new GameObject("AltNeedleK").transform;
        kp.SetParent(altDial, false);
        kp.localPosition = new Vector3(0.15f, 0.03f, -0.0045f);
        Quad(kp, "Needle", new Vector3(0f, DR * 0.22f, 0f), new Vector2(0.008f, DR * 0.56f), amber);
        c.cpAltNeedleK = kp;
        Txt(altDial, "Unit", "x100", new Vector2(0.15f, 0.03f - DR * 0.42f), 0.05f, 0.07f, CCyanDim, TextAlignmentOptions.Center);
        // 数字でも小さく表示
        c.cpSpeed = Txt(main, "Speed", "0", new Vector2(-0.15f, -0.072f), 0.14f, 0.26f, CWhite, TextAlignmentOptions.Center);
        c.cpAlt = Txt(main, "Alt", "0", new Vector2(0.15f, -0.072f), 0.14f, 0.26f, CWhite, TextAlignmentOptions.Center);

        // 水平儀（傾きに合わせて回る線）と機体マーク（中央の列）
        var att = new GameObject("Attitude").transform;
        att.SetParent(main, false);
        att.localPosition = new Vector3(0f, 0.03f, 0f);
        var hz = new GameObject("Horizon").transform;
        hz.SetParent(att, false);
        hz.localPosition = new Vector3(0f, 0f, -0.0015f);
        Quad(hz, "Line", Vector3.zero, new Vector2(0.11f, 0.003f), frame);
        Quad(hz, "TickL", new Vector3(-0.055f, -0.006f, 0f), new Vector2(0.003f, 0.012f), frame);
        Quad(hz, "TickR", new Vector3(0.055f, -0.006f, 0f), new Vector2(0.003f, 0.012f), frame);
        c.cpHorizon = hz;
        Quad(att, "MarkL", new Vector3(-0.028f, 0f, -0.003f), new Vector2(0.03f, 0.005f), amber);
        Quad(att, "MarkR", new Vector3(0.028f, 0f, -0.003f), new Vector2(0.03f, 0.005f), amber);
        Quad(att, "MarkC", new Vector3(0f, 0f, -0.003f), new Vector2(0.008f, 0.008f), amber);
        c.cpAtt = Txt(main, "Attitude", "", new Vector2(0f, -0.045f), 0.14f, 0.1f, CCyanDim, TextAlignmentOptions.Center);

        c.cpWarn = Txt(main, "Warn", "", new Vector2(0f, -0.105f), 0.42f, 0.3f, CWhite, TextAlignmentOptions.Center);
        c.cpStatus = Txt(main, "Status", "", new Vector2(0f, -0.138f), 0.44f, 0.13f, CCyan, TextAlignmentOptions.Center);

        // 左: 迎角・フラップ・ブレーキ
        Transform left = Panel(root, "LeftPanel", new Vector3(-0.40f, -0.15f, 0.66f), 0.28f, 0.26f, bg, frame);
        Txt(left, "Title", "FLIGHT", new Vector2(0f, 0.105f), 0.26f, 0.15f, CCyan, TextAlignmentOptions.Center);
        c.cpAoaBar = Bar(left, "AoaBar", new Vector2(-0.10f, -0.015f), new Vector2(0.03f, 0.17f), true, barBg, amber);
        float stallFrac = Mathf.Clamp01(c.stallAngle / (c.stallAngle + 5f));
        Quad(left, "StallMark", new Vector3(-0.10f, -0.015f - 0.085f + stallFrac * 0.17f, -0.003f), new Vector2(0.045f, 0.004f), red);
        Txt(left, "AoaLabel", "AoA", new Vector2(0.03f, 0.07f), 0.18f, 0.15f, CCyanDim, TextAlignmentOptions.Left);
        c.cpAoa = Txt(left, "Aoa", "0.0°", new Vector2(0.03f, 0.028f), 0.18f, 0.40f, CWhite, TextAlignmentOptions.Left);
        c.cpFlags = Txt(left, "Flags", "", new Vector2(0.03f, -0.065f), 0.18f, 0.15f, CCyan, TextAlignmentOptions.Left, 0.09f);

        // さらに左: HP・弾数・武器の選択・ロックオン
        Transform wp = Panel(root, "WeaponPanel", new Vector3(-0.62f, -0.15f, 0.48f), 0.28f, 0.30f, bg, frame);
        Txt(wp, "Title", "STATUS / WEAPON", new Vector2(0f, 0.125f), 0.26f, 0.14f, CCyan, TextAlignmentOptions.Center);
        Txt(wp, "HpLabel", "HP", new Vector2(-0.065f, 0.085f), 0.12f, 0.15f, CCyanDim, TextAlignmentOptions.Left);
        c.cpHp = Txt(wp, "Hp", "100", new Vector2(0.03f, 0.075f), 0.2f, 0.42f, CWhite, TextAlignmentOptions.Right);
        c.cpHpBar = Bar(wp, "HpBar", new Vector2(0f, 0.037f), new Vector2(0.24f, 0.014f), false, barBg, frame);
        Quad(wp, "Sep", new Vector3(0f, 0.017f, -0.002f), new Vector2(0.24f, 0.002f), frame);
        Txt(wp, "WpLabel", "WEAPON   [1] [2] [3]", new Vector2(0f, 0.0f), 0.24f, 0.11f, CCyanDim, TextAlignmentOptions.Left);
        c.cpWeapons = Txt(wp, "Weapons", "", new Vector2(0f, -0.055f), 0.24f, 0.17f, CWhite, TextAlignmentOptions.Left, 0.09f);
        c.cpLock = Txt(wp, "Lock", "", new Vector2(0f, -0.122f), 0.26f, 0.16f, CWhite, TextAlignmentOptions.Center);

        // 右: スロットル・昇降率
        Transform rightP = Panel(root, "RightPanel", new Vector3(0.40f, -0.15f, 0.66f), 0.28f, 0.26f, bg, frame);
        Txt(rightP, "Title", "ENGINE", new Vector2(0f, 0.105f), 0.26f, 0.15f, CCyan, TextAlignmentOptions.Center);
        c.cpThrBar = Bar(rightP, "ThrBar", new Vector2(-0.10f, -0.015f), new Vector2(0.03f, 0.17f), true, barBg, frame);
        Txt(rightP, "ThrLabel", "THR", new Vector2(0.03f, 0.07f), 0.18f, 0.15f, CCyanDim, TextAlignmentOptions.Left);
        c.cpThr = Txt(rightP, "Thr", "0%", new Vector2(0.03f, 0.028f), 0.18f, 0.40f, CWhite, TextAlignmentOptions.Left);
        Txt(rightP, "VsLabel", "CLIMB", new Vector2(0.03f, -0.03f), 0.18f, 0.15f, CCyanDim, TextAlignmentOptions.Left);
        c.cpVs = Txt(rightP, "VS", "", new Vector2(0.03f, -0.06f), 0.18f, 0.2f, CWhite, TextAlignmentOptions.Left);
        c.cpFuel = Txt(rightP, "Fuel", "FUEL", new Vector2(0f, -0.112f), 0.27f, 0.21f, CWhite, TextAlignmentOptions.Center);   // 燃料は大きめの文字
        c.voiceLowFuel = LoadClip("voice_lowfuel.wav");
        c.voiceFuelEmpty = LoadClip("voice_fuelempty.wav");
        c.cpGun = null;
        c.cpGunBar = null;

        // さらに右: 入室・退室の記録
        Transform lp = Panel(root, "LogPanel", new Vector3(0.62f, -0.15f, 0.48f), 0.28f, 0.30f, bg, frame);
        Txt(lp, "Title", "PLAYERS / LOG / KILLS", new Vector2(0f, 0.125f), 0.26f, 0.14f, CCyan, TextAlignmentOptions.Center);
        c.cpPlayers = Txt(lp, "Online", "ONLINE  1", new Vector2(0f, 0.095f), 0.24f, 0.13f, CWhite, TextAlignmentOptions.Left);
        Quad(lp, "Sep", new Vector3(0f, 0.077f, -0.002f), new Vector2(0.24f, 0.002f), frame);
        c.cpLog = Txt(lp, "Log", "", new Vector2(0f, -0.02f), 0.25f, 0.1f, CWhite, TextAlignmentOptions.TopLeft, 0.18f);
        // 撃墜数（いちばん下）
        Quad(lp, "Sep2", new Vector3(0f, -0.108f, -0.002f), new Vector2(0.24f, 0.002f), frame);
        Txt(lp, "KillsLabel", "KILLS", new Vector2(0f, -0.12f), 0.24f, 0.09f, CCyanDim, TextAlignmentOptions.Left);
        c.cpKills = Txt(lp, "Kills", "", new Vector2(0.02f, -0.134f), 0.22f, 0.15f, CWhite, TextAlignmentOptions.Right);

        // ロックオンの赤い四角（標的に重ねて表示。何にも隠れないように一番手前に描く）
        Material lockMat = UnlitMat("LockRed", new Color(1f, 0.12f, 0.1f, 1f), true, 3100);
        lockMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        lockMat.renderQueue = 3100;
        EditorUtility.SetDirty(lockMat);
        var lb = new GameObject("LockBox").transform;
        lb.SetParent(root, false);
        const float LT = 0.05f;
        Quad(lb, "Top", new Vector3(0f, 0.5f, 0f), new Vector2(1f + LT, LT), lockMat);
        Quad(lb, "Bottom", new Vector3(0f, -0.5f, 0f), new Vector2(1f + LT, LT), lockMat);
        Quad(lb, "Left", new Vector3(-0.5f, 0f, 0f), new Vector2(LT, 1f), lockMat);
        Quad(lb, "Right", new Vector3(0.5f, 0f, 0f), new Vector2(LT, 1f), lockMat);
        lb.gameObject.SetActive(false);
        c.lockBox = lb;

        // 下: 方位
        // 方位は中央の計器パネルの中に入れる（下の細いパネルは無くして、その分レーダーを上げる）

        // レーダー（中央の下）
        BuildRadar(c, root, bg, frame, amber);

        // コックピットの内装（コンソール・ボタン・操縦桿・窓枠）
        if (c.car == null || !IsOmega(c.car)) BuildInterior(root, s_consoleMat);   // Omega は本物の操縦席があるので、画面だけ出す
        c.cpHdg = Txt(main, "Heading", "HDG 000", new Vector2(0f, -0.074f), 0.14f, 0.12f, CCyan, TextAlignmentOptions.Center);

        // 目の前（目の高さ・約0.9m先）に大きく出すメッセージ
        var center = new GameObject("CenterMessage").transform;
        center.SetParent(root, false);
        center.localPosition = new Vector3(0f, 0.17f, 0.9f);   // カウントダウンなどは少し上に
        center.localRotation = Quaternion.identity;
        c.cpCenter = Txt(center, "Text", "", Vector2.zero, 0.9f, 0.42f, CWhite, TextAlignmentOptions.Center, 0.3f);

        // 起動時の暗転（周りを包む黒い球。内側から見える。雲より後・画面より先に描く）
        var bo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(bo.GetComponent<Collider>());
        bo.name = "Blackout";
        bo.transform.SetParent(root, false);
        bo.transform.localPosition = Vector3.zero;
        bo.transform.localScale = Vector3.one * 3f;   // 半径 1.5m（内装も画面も中に入る）
        Material boMat = UnlitMat("CockpitBlackout", new Color(0f, 0f, 0f, 1f), true, 2987);
        // 内側も描く・手前の物（内装）も含めて全部黒で覆う。画面（2990 以降）だけは上に光って見える
        boMat.SetFloat("_Cull", 0f);
        boMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        boMat.renderQueue = 2987;
        EditorUtility.SetDirty(boMat);
        var bor = bo.GetComponent<MeshRenderer>();
        bor.sharedMaterial = boMat;
        bor.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bor.enabled = false;
        c.blackout = bor;

        // 被弾時に視界を赤く点滅させる球（暗転の球と同じ作り。画面より先に描くので画面は読める）
        var dt2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(dt2.GetComponent<Collider>());
        dt2.name = "DamageTint";
        dt2.transform.SetParent(root, false);
        dt2.transform.localScale = Vector3.one * 3f;
        Material dtMat = UnlitMat("CockpitDamageTint", new Color(1f, 0.05f, 0.02f, 0f), true, 2988);
        dtMat.SetFloat("_Cull", 0f);
        dtMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        dtMat.renderQueue = 2988;
        EditorUtility.SetDirty(dtMat);
        var dtr = dt2.GetComponent<MeshRenderer>();
        dtr.sharedMaterial = dtMat;
        dtr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dtr.enabled = false;
        c.damageTint = dtr;
        // 警報音（ループ）
        var alGo = new GameObject("AlarmAudio");
        alGo.transform.SetParent(root, false);
        var alAu = alGo.AddComponent<AudioSource>();
        alAu.clip = LoadClip("alarm.wav");
        alAu.loop = true;
        alAu.playOnAwake = false;
        alAu.spatialBlend = 0f;
        alAu.volume = 0.22f;
        c.alarmAudio = alAu;
        // コックピットの中の煙（コックピットと一緒に動く）
        Material cabinSmokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/MissileSmoke.mat");
        if (cabinSmokeMat == null) cabinSmokeMat = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/FlightFx.mat");
        var cs = MakePS("CabinSmoke", root, cabinSmokeMat);
        cs.transform.localPosition = new Vector3(0f, -0.5f, 0.55f);
        var csm = cs.main;
        csm.loop = true;
        csm.simulationSpace = ParticleSystemSimulationSpace.Local;
        csm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
        csm.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        csm.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        csm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.2f, 0.2f, 0.35f), new Color(0.35f, 0.35f, 0.35f, 0.25f));
        csm.gravityModifier = -0.02f;
        csm.maxParticles = 60;
        var css = cs.shape;
        css.enabled = true;
        css.shapeType = ParticleSystemShapeType.Box;
        css.scale = new Vector3(1.2f, 0.1f, 0.4f);
        SetFade(cs, new Color(0.3f, 0.3f, 0.3f), new Color(0.45f, 0.45f, 0.45f));
        c.cabinSmoke = cs;
        c.voiceEngineFire = LoadClip("voice_enginefire.wav");
        // 敵（AMA）の無線（本人にだけ聞こえる。自分の音声案内とは別のスピーカー）
        var erGo = new GameObject("EnemyRadio");
        erGo.transform.SetParent(root, false);
        var erAu = erGo.AddComponent<AudioSource>();
        erAu.playOnAwake = false;
        erAu.spatialBlend = 0f;
        erAu.volume = 0.85f;
        erAu.priority = 5;
        c.enemyRadio = erAu;
        c.amaBroadcast = LoadClip("ama_broadcast.wav");
        c.amaWarn1 = LoadClip("ama_warn1.wav");
        c.amaWarn2 = LoadClip("ama_warn2.wav");
        c.amaWarn3 = LoadClip("ama_warn3.wav");
        c.amaFinal = LoadClip("ama_final.wav");
        c.amaEngage = LoadClip("ama_engage.wav");
        c.amaHeavy = new[] { LoadClip("gaur_lost1.wav"), LoadClip("gaur_lost2.wav") };              // ガウリア帝国の司令部（AMA とは別の声）
        c.amaAttacked = new[] { LoadClip("gaur_attacked1.wav"), LoadClip("gaur_attacked2.wav") };
        c.gaurRecon = new[] { LoadClip("gaur_recon1.wav"), LoadClip("gaur_recon2.wav") };
        c.guanWarn = new[] { LoadClip("guan_warn1.wav"), LoadClip("guan_warn2.wav") };   // 空母の艦長（AMA・司令部とは別の声）
        c.voiceGuided = new[] { LoadClip("voice_guided1.wav"), LoadClip("voice_guided2.wav") };
        c.carrier = null;
        c.carrierShip = null;
        foreach (var cws in Object.FindObjectsOfType<WarShip>(true)) if (cws.target != null && cws.target.kind == 2) { c.carrier = cws.transform; c.carrierShip = cws; break; }
        c.amaFired = LoadClip("ama_fired.wav");
        c.voiceCritical = LoadClip("voice_critical.wav");
        c.staticMat = s_staticMat;

        // 起動音（本人にだけ聞こえる 2D の音）
        var bootAu = rootGo.AddComponent<AudioSource>();
        bootAu.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/boot.wav");
        bootAu.playOnAwake = false;
        bootAu.spatialBlend = 0f;
        bootAu.volume = 0.5f;
        c.cockpitAudio = bootAu;

        // 音声案内（英語・本人にだけ聞こえる 2D の音）
        var voGo = new GameObject("VoiceAudio");
        voGo.transform.SetParent(root, false);
        var voAu = voGo.AddComponent<AudioSource>();
        voAu.playOnAwake = false;
        voAu.spatialBlend = 0f;
        voAu.volume = 0.9f;
        voAu.priority = 10;
        c.voiceAudio = voAu;
        var clocks = new AudioClip[12];
        for (int i = 0; i < 12; i++) clocks[i] = LoadClip("voice_clock" + i + ".wav");
        c.voiceClock = clocks;
        c.voiceKill = LoadClip("voice_kill.wav");
        c.voiceOn = LoadClip("voice_on.wav");
        c.voiceOff = LoadClip("voice_off.wav");
        c.voiceTakingFire = LoadClip("voice_takingfire.wav");
        var fireClocks = new AudioClip[12];
        for (int i = 0; i < 12; i++) fireClocks[i] = LoadClip("voice_fire_clock" + i + ".wav");
        c.voiceFireClock = fireClocks;
        var mslClocks = new AudioClip[12];
        for (int i = 0; i < 12; i++) mslClocks[i] = LoadClip("voice_msl_clock" + i + ".wav");
        c.voiceMissileClock = mslClocks;
        c.voiceMissile = LoadClip("voice_missile.wav");
        c.voiceTargetHit = LoadClip("voice_targethit.wav");
        c.voiceImpact = LoadClip("voice_impact.wav");
        c.voiceDestroyed = LoadClip("voice_destroyed.wav");
        c.voiceWelcome = LoadClip("voice_welcome.wav");
        c.voiceLaunching = LoadClip("voice_launching.wav");
        c.voiceGo = LoadClip("voice_go.wav");
        c.voiceApOn = LoadClip("voice_ap_on.wav");
        c.voiceApOff = LoadClip("voice_ap_off.wav");

        // 失速の警告音（本人にだけ聞こえる・ループ）
        var warnGo = new GameObject("StallWarnAudio");
        warnGo.transform.SetParent(root, false);
        var warnAu = warnGo.AddComponent<AudioSource>();
        warnAu.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/stall_warn.wav");
        warnAu.loop = true;
        warnAu.playOnAwake = false;
        warnAu.spatialBlend = 0f;
        warnAu.volume = 0.35f;
        c.stallWarningAudio = warnAu;
        var stallGo = new GameObject("StallAudio");
        stallGo.transform.SetParent(root, false);
        var stallAu = stallGo.AddComponent<AudioSource>();
        stallAu.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/stall.wav");
        stallAu.loop = true;
        stallAu.playOnAwake = false;
        stallAu.spatialBlend = 0f;
        stallAu.volume = 0.4f;
        c.stallAudio = stallAu;

        c.cockpitRoot = rootGo;
        // Omega: 機首で画面が隠れるので、パネルはやめて「ヘッドアップディスプレイ」風に
        // （緑の文字と計器だけ。機体の上に重ねて描くので、機首やガラスに隠れない）
        if (c.car != null && IsOmega(c.car))
        {
            // Omega: R35 の表示を緑の文字だけ（HUD）にして、下に丸い計器
            ConvertToHud(c, root);
            OmegaHudTidy(c, root);
            BuildAnalogPanel(c, root, true);
        }   // Omega: 本物の飛行機のような丸い計器の計器盤
        rootGo.SetActive(false);   // 普段は非表示。座った本人の画面だけで表示される

        UdonSharpEditorUtility.CopyProxyToUdon(c);
        PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
        if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        return true;
    }

    // ---------------- レーダー画面 ----------------
    // 濃い青の円に格子と同心円、外周に方位の目盛り（機首が上になるよう回る）、回る光、飛行機の形のアイコン
    const float RadarScope = 0.74f;   // テクスチャ内の画面の半径（外側は方位の目盛り）

    const int RadarDotCount = 140;

    // ---- レーダーの島の地図: ワールドを上から調べて陸の形の画像を作る（最初の 1 回だけ。作り直す時は画像を消す） ----
    const string RadarMapPng = FxDir + "/radar_map.png";
    const int RadarMapRes = 1024;
    const float RadarMapSize = 28000f;   // 地図の 1 辺（m）。出撃地点が真ん中
    static Vector4 radarMapRect;
    static Material radarMapMatN, radarMapMatH;
    static Material RadarMapMat(bool hud)
    {
        if (hud && radarMapMatH != null) return radarMapMatH;
        if (!hud && radarMapMatN != null) return radarMapMatN;
        Shader sh = Shader.Find("FlightUI/RadarMap");
        if (sh == null) return null;
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        Vector3 c0 = (putOut != null && putOut.catapultPoint != null) ? putOut.catapultPoint.position : new Vector3(254f, 0f, 297f);
        radarMapRect = new Vector4(c0.x, c0.z, RadarMapSize, 0f);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(RadarMapPng);
        if (tex == null) tex = BakeRadarMap(c0);
        string path = FxDir + (hud ? "/RadarMapHUD.mat" : "/RadarMap.mat");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_MainTex", tex);
        m.SetColor("_Color", hud ? new Color(0.45f, 1f, 0.55f, 0.9f) : new Color(0.55f, 0.95f, 0.6f, 0.9f));
        m.SetFloat("_ZTest", hud ? (float)UnityEngine.Rendering.CompareFunction.Always : (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        m.renderQueue = hud ? 3007 : 2992;   // 画面の背景の上・掃く光の下
        EditorUtility.SetDirty(m);
        if (hud) radarMapMatH = m; else radarMapMatN = m;
        return m;
    }

    static Texture2D BakeRadarMap(Vector3 c0)
    {
        Physics.SyncTransforms();
        float seaY = -27.7f;
        var ocean = GameObject.Find("Ocean");
        if (ocean != null) seaY = ocean.transform.position.y;
        int N = RadarMapRes;
        float half = RadarMapSize / 2f, cell = RadarMapSize / N;
        int mask = ~((1 << 4) | (1 << 9) | (1 << 10) | (1 << 18));   // 水・プレイヤー・ミラーは除く
        var land = new bool[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            Vector3 o = new Vector3(c0.x - half + (x + 0.5f) * cell, seaY + 1500f, c0.z - half + (y + 0.5f) * cell);
            RaycastHit h;
            if (Physics.Raycast(o, Vector3.down, out h, 1600f, mask, QueryTriggerInteraction.Ignore)
                && h.collider.attachedRigidbody == null && h.point.y > seaY + 0.5f)
                land[y * N + x] = true;
        }
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        int cnt = 0;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            int i = y * N + x;
            if (!land[i]) { px[i] = new Color32(0, 0, 0, 0); continue; }
            cnt++;
            bool edge = x == 0 || y == 0 || x == N - 1 || y == N - 1 || !land[i - 1] || !land[i + 1] || !land[i - N] || !land[i + N];
            px[i] = edge ? new Color32(255, 255, 255, 255) : new Color32(200, 255, 210, 110);   // 海岸線ははっきり、陸の中は薄く
        }
        tex.SetPixels32(px);
        Directory.CreateDirectory(FxDir);
        File.WriteAllBytes(RadarMapPng, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(RadarMapPng);
        var imp = AssetImporter.GetAtPath(RadarMapPng) as TextureImporter;
        if (imp != null)
        {
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = false;
            imp.maxTextureSize = 1024;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        Debug.Log("[FlightSetup] レーダーの島の地図: 陸 " + cnt + " マス / " + (N * N));
        return AssetDatabase.LoadAssetAtPath<Texture2D>(RadarMapPng);
    }
    static void BuildRadar(controller c, Transform root, Material bg, Material frame, Material amber, bool gunnerSet = false)
    {
        const float S = 0.30f;           // 円の直径（m）
        float R = S / 2f;
        bool hudR = !gunnerSet && c.car != null && IsOmega(c.car);
        Transform rp = Panel(root, "RadarPanel", gunnerSet ? new Vector3(0f, -0.25f, 0.85f) : (hudR ? new Vector3(0.40f, 0.15f, 0.66f) : new Vector3(0f, -0.40f, 0.62f)), 0.33f, 0.33f, bg, frame);
        if (hudR) rp.localScale = Vector3.one * 0.75f;   // Omega: 右の上（下の丸い計器と重ならない）
        Material scope = TexMat("RadarScope", GenTex("radar_scope.png", 512, RadarScopePix), Color.white, 2992);
        Material sweep = TexMat("RadarSweep", GenTex("radar_sweep.png", 256, RadarSweepPix), new Color(0.35f, 0.95f, 1f, 1f), 2993);
        Material rose = TexMat("RadarRose", GenTex("radar_rose.png", 512, RadarRosePix), new Color(0.35f, 0.95f, 1f, 1f), 2994);
        Quad(rp, "Scope", new Vector3(0f, 0f, -0.001f), new Vector2(S, S), scope);
        // 島（上から見た陸の形）。範囲の円と同じ大きさ
        Material mapM = RadarMapMat(hudR);   // Omega の操縦席は緑の HUD（何にも隠れない）
        Renderer mapR = null;
        if (mapM != null)
        {
            var mq = Quad(rp, "IslandMap", new Vector3(0f, 0f, -0.0015f), new Vector2(R * RadarScope * 2f, R * RadarScope * 2f), mapM);
            mapR = mq.GetComponent<Renderer>();
        }
        var sw = Quad(rp, "Sweep", new Vector3(0f, 0f, -0.002f), new Vector2(S, S), sweep);
        var rose_t = new GameObject("Rose").transform;
        rose_t.SetParent(rp, false);
        rose_t.localPosition = new Vector3(0f, 0f, -0.003f);
        Quad(rose_t, "Ticks", Vector3.zero, new Vector2(S, S), rose);
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            string lab = i == 0 ? "N" : (i == 3 ? "E" : (i == 6 ? "S" : (i == 9 ? "W" : (i * 30).ToString("000"))));
            var t = Txt(rose_t, "Deg" + i, lab, new Vector2(Mathf.Sin(a) * R * 0.955f, Mathf.Cos(a) * R * 0.955f), 0.04f, i % 3 == 0 ? 0.085f : 0.06f, i % 3 == 0 ? CWhite : CCyan, TextAlignmentOptions.Center);
            t.transform.localRotation = Quaternion.Euler(0f, 0f, -i * 30f);   // 文字も円に沿って回す
        }
        // 自機（中央・黄色の飛行機）
        Texture2D planeTex = GenTex("icon_plane.png", 64, PlaneIconPix);
        Texture2D shipTex = GenTex("icon_ship.png", 64, ShipIconPix);
        Texture2D ringTex = GenTex("icon_ring.png", 64, RingIconPix);
        Material meMat = TexMat("RadarMe", planeTex, new Color(1f, 0.8f, 0.3f, 1f), 2998);
        Quad(rp, "Me", new Vector3(0f, 0f, -0.006f), new Vector2(0.018f, 0.018f), meMat);
        // 四隅の飾りの文字
        Txt(rp, "Title", "RADAR", new Vector2(-0.125f, 0.15f), 0.08f, 0.1f, CCyan, TextAlignmentOptions.Left);
        Txt(rp, "Range", "RNG 2.0km", new Vector2(0.125f, 0.15f), 0.1f, 0.08f, CCyanDim, TextAlignmentOptions.Right);
        Txt(rp, "Mode", "HDG UP", new Vector2(-0.125f, -0.15f), 0.08f, 0.08f, CCyanDim, TextAlignmentOptions.Left);
        Txt(rp, "Legend", "<color=#ff4a40>ENEMY</color> <color=#ffa030>SHIP</color> <color=#80ff90>ALLY</color>", new Vector2(0.125f, -0.15f), 0.14f, 0.06f, CCyanDim, TextAlignmentOptions.Right);

        var mats = new Material[4];
        mats[0] = TexMat("RadarIconEnemy", planeTex, new Color(1f, 0.3f, 0.25f, 1f), 2998);
        mats[1] = TexMat("RadarIconShip", shipTex, new Color(1f, 0.62f, 0.2f, 1f), 2998);
        mats[2] = TexMat("RadarIconTarget", ringTex, new Color(1f, 0.95f, 0.35f, 1f), 2998);
        mats[3] = TexMat("RadarIconFriend", planeTex, new Color(0.5f, 1f, 0.6f, 1f), 2998);
        var dots = new Transform[RadarDotCount];   // 敵機・船・味方など全部を出せる数（足りないと後ろの方の船や味方が出なかった）
        var drs = new Renderer[RadarDotCount];
        for (int i = 0; i < dots.Length; i++)
        {
            var d = Quad(rp, "Blip", new Vector3(0f, 0f, -0.007f), new Vector2(0.017f, 0.017f), mats[0]);
            d.SetActive(false);
            dots[i] = d.transform;
            drs[i] = d.GetComponent<Renderer>();
        }
        // ミサイルの点（白 = 自分、オレンジ = ほかのプレイヤー、赤 = 船のミサイル）
        Texture2D dotTex = GenTex("icon_dot.png", 32, (u, v) => new Color(1f, 1f, 1f, (u * u + v * v) < 0.8f ? 1f : 0f));
        var mslMats = new Material[3];
        mslMats[0] = TexMat("RadarMslOwn", dotTex, new Color(1f, 1f, 1f, 1f), 2999);
        mslMats[1] = TexMat("RadarMslOther", dotTex, new Color(1f, 0.7f, 0.2f, 1f), 2999);
        mslMats[2] = TexMat("RadarMslEnemy", dotTex, new Color(1f, 0.15f, 0.1f, 1f), 2999);
        var mdots = new Transform[24];
        var mdrs = new Renderer[24];
        for (int i = 0; i < mdots.Length; i++)
        {
            var d = Quad(rp, "MslBlip", new Vector3(0f, 0f, -0.008f), new Vector2(0.008f, 0.008f), mslMats[0]);
            d.SetActive(false);
            mdots[i] = d.transform;
            mdrs[i] = d.GetComponent<Renderer>();
        }

        Undo.RecordObject(c, "Flight setup");
        if (gunnerSet)
        {
            // 後ろの席用（色や範囲は操縦席と共通）
            c.radarMslDotsG = mdots;
            c.radarMslRenderersG = mdrs;
            c.radarDotsG = dots;
            c.radarDotRenderersG = drs;
            c.radarSweepG = sw.transform;
            c.radarRoseG = rose_t;
            c.radarMapG = mapR;
            return;
        }
        c.radarMap = mapR;
        c.radarMapRect = radarMapRect;
        c.radarMslDots = mdots;
        c.radarMslRenderers = mdrs;
        c.radarMslMats = mslMats;
        c.radarDots = dots;
        c.radarDotRenderers = drs;
        c.radarMats = mats;
        c.radarRange = 2000f;
        c.radarRadius = R * RadarScope;
        c.radarSweep = sw.transform;
        c.radarRose = rose_t;
    }

    // 時計のようなメーター（文字盤・数字・針）。針の回転の中心を返す
    static Transform Dial(Transform p, string name, Vector2 ctr, float r, Material face, string[] labels, float[] angles, Material needleMat, float len, float w, float labelSize = 0.085f)
    {
        var d = new GameObject(name).transform;
        d.SetParent(p, false);
        Quad(d, "Face", new Vector3(ctr.x, ctr.y, -0.001f), new Vector2(r * 2f, r * 2f), face);
        for (int i = 0; i < labels.Length; i++)
        {
            float a = angles[i] * Mathf.Deg2Rad;
            Txt(d, "N" + labels[i], labels[i], new Vector2(ctr.x + Mathf.Sin(a) * r * 0.62f, ctr.y + Mathf.Cos(a) * r * 0.62f), 0.04f, labelSize, CWhite, TextAlignmentOptions.Center);
        }
        var pv = new GameObject("Needle").transform;
        pv.SetParent(d, false);
        pv.localPosition = new Vector3(ctr.x, ctr.y, -0.005f);
        Quad(pv, "Bar", new Vector3(0f, len * 0.5f - len * 0.15f, 0f), new Vector2(w, len), needleMat);
        float hub = Mathf.Min(0.01f, r * 0.22f);
        Quad(d, "Hub", new Vector3(ctr.x, ctr.y, -0.006f), new Vector2(hub, hub), needleMat);
        return pv;
    }

    // ---- テクスチャを式で作る（1 回作ったら再利用） ----
    static Texture2D GenTex(string file, int N, System.Func<float, float, Color> fn)
    {
        string path = FxDir + "/" + file;
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
            tex.SetPixel(x, y, fn(u, v));
        }
        Directory.CreateDirectory(FxDir);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null) { imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static readonly Color HudGreen = new Color(0.4f, 1f, 0.5f, 1f);

    // ---------------- 丸い計器の計器盤（Omega） ----------------
    // 上の段: 速度計・姿勢指示器・高度計 / 下の段: 旋回計・方位計・昇降計（本物の飛行機の「6 つの計器」の並び）
    // 左の小さい計器: スロットル・燃料・迎角 / 右: レーダー / 両端: 武器と記録の小さな画面 / 下: 警告の表示
    static void BuildAnalogPanel(controller c, Transform root, bool gaugesOnly = false)
    {
        // gaugesOnly = 丸い計器だけ（文字の情報は緑の HUD に出す時）
        // 戦闘機の計器盤の並び:
        //   上の中央 = 警告灯 / 左 = 四角い画面（HP・武器・燃料・撃墜数）/ 右 = 四角い画面（レーダー・入室記録）
        //   中央 = 丸い計器 6 つ（上: 速度・姿勢・高度 / 下: 旋回・方位・昇降）/ その下 = 迎角・スロットル・燃料
        // 機体の中から見える範囲（目から左右 約 ±19°、下 約 27° まで）に収める
        if (!gaugesOnly)
        foreach (var nm in new[] { "MainPanel", "LeftPanel", "RightPanel", "WeaponPanel", "LogPanel" })
        {
            Transform o = root.Find(nm);
            if (o != null) Object.DestroyImmediate(o.gameObject);
        }
        Material plateMat = LoadOrCreateMat("AnalogPanel", Shader.Find("Standard"));
        plateMat.SetColor("_Color", new Color(0.16f, 0.17f, 0.18f));
        plateMat.SetFloat("_Metallic", 0.35f);
        plateMat.SetFloat("_Glossiness", 0.35f);
        EditorUtility.SetDirty(plateMat);
        Material bezelMat = LoadOrCreateMat("AnalogBezel", Shader.Find("Standard"));
        bezelMat.SetColor("_Color", new Color(0.04f, 0.04f, 0.045f));
        bezelMat.SetFloat("_Metallic", 0.5f);
        bezelMat.SetFloat("_Glossiness", 0.6f);
        EditorUtility.SetDirty(bezelMat);
        Material needle = UnlitMat("AnalogNeedle", new Color(0.95f, 0.95f, 0.92f, 1f), false, 2000);
        Material orange = UnlitMat("AnalogOrange", new Color(1f, 0.55f, 0.1f, 1f), false, 2000);
        Material lcdMat = UnlitMat("AnalogLcd", new Color(0.02f, 0.05f, 0.03f, 1f), false, 2000);
        Material osbMat = UnlitMat("AnalogOsb", new Color(0.30f, 0.31f, 0.32f, 1f), false, 2000);
        Color W = new Color(0.93f, 0.93f, 0.9f, 1f);
        Color LG = new Color(0.5f, 1f, 0.55f, 1f);

        // 計器盤（目の前・少し下。目から 0.52、下に 12°）
        var panel = new GameObject("InstrumentPanel").transform;   // 起動時の「画面が開く」演出を受けない名前
        panel.SetParent(root, false);
        Vector3 pp = gaugesOnly ? new Vector3(0.0296f, -0.0814f, 0.5127f)   // 丸い計器だけ: 目から 0.52・下に 9°・右に 3.3°
                                : new Vector3(0f, -0.126f, 0.505f);   // 目から 0.52・下に 14°
        panel.localPosition = pp;
        panel.localRotation = Quaternion.LookRotation(pp.normalized, Vector3.up);
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.name = "Plate";
        plate.transform.SetParent(panel, false);
        plate.transform.localPosition = gaugesOnly ? new Vector3(0f, 0.005f, 0.012f) : new Vector3(0f, 0f, 0.012f);
        plate.transform.localScale = gaugesOnly ? new Vector3(0.37f, 0.18f, 0.02f) : new Vector3(0.37f, 0.27f, 0.02f);
        plate.GetComponent<MeshRenderer>().sharedMaterial = plateMat;
        plate.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        const float D = 0.048f;     // 中央の丸い計器の直径
        const float d = 0.044f;     // 下の小さい計器
        float R = D / 2f, r = d / 2f;
        float k = R / 0.044f;       // 文字の大きさの倍率
        const float P = 0.054f;     // 計器の間隔
        const float Y1 = 0.062f, Y2 = 0.004f, Y3 = -0.054f;
        Undo.RecordObject(c, "Flight setup");

        // 1) 速度計（0〜300 km/h）
        Material asiM = TexMat("AnalogASI", GenTex("analog_asi.png", 256, (u, v) => AnalogPix(u, v, -135f, 270f, 6, 5,
            new float[] { 70f / 300f, 150f / 300f, 90f / 300f, 220f / 300f, 220f / 300f, 270f / 300f, 278f / 300f, 282f / 300f },
            new Color[] { new Color(0.9f, 0.9f, 0.9f), new Color(0.2f, 0.8f, 0.3f), new Color(0.95f, 0.85f, 0.2f), new Color(0.9f, 0.15f, 0.1f) },
            new float[] { 0.62f, 0.70f, 0.70f, 0.70f })), Color.white, 2992);
        var sl = new string[4]; var sa = new float[4];
        for (int i = 0; i < 4; i++) { sl[i] = (i * 100).ToString(); sa[i] = -135f + i * 90f; }
        Vector2 cA = new Vector2(-P, Y1);
        Bezel(panel, cA, D, bezelMat);
        c.cpSpdNeedle = Dial(panel, "ASI", cA, R, asiM, sl, sa, needle, R * 0.85f, 0.0022f, 0.085f * k);
        Txt(panel, "ASIlab", "KM/H", cA + new Vector2(0f, -R * 0.4f), 0.03f, 0.05f * k, W, TextAlignmentOptions.Center);

        // 2) 姿勢指示器
        Vector2 cB = new Vector2(0f, Y1);
        Bezel(panel, cB, D, bezelMat);
        var ball = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(ball.GetComponent<Collider>());
        ball.name = "AttitudeBall";
        ball.transform.SetParent(panel, false);
        ball.transform.localPosition = new Vector3(cB.x, cB.y, -0.001f);
        ball.transform.localScale = new Vector3(D * 0.92f, D * 0.92f, 1f);
        string ballMatPath = FxDir + "/AttitudeBall.mat";
        Material ballMat = AssetDatabase.LoadAssetAtPath<Material>(ballMatPath);
        if (ballMat == null) { ballMat = new Material(Shader.Find("FlightUI/AttitudeBall")); AssetDatabase.CreateAsset(ballMat, ballMatPath); }
        ballMat.SetTexture("_MainTex", GenTex("attitude_ball.png", 256, AttitudeBallPix));
        ballMat.SetFloat("_Span", 0.5f);
        EditorUtility.SetDirty(ballMat);
        ball.GetComponent<MeshRenderer>().sharedMaterial = ballMat;
        ball.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        c.cpAttDisc = ball.GetComponent<MeshRenderer>();
        Material maskM = TexMat("AnalogAttMask", GenTex("attitude_mask.png", 256, AttitudeMaskPix), Color.white, 2993);
        Quad(panel, "AttMask", new Vector3(cB.x, cB.y, -0.002f), new Vector2(D, D), maskM);
        Quad(panel, "AttWingL", new Vector3(cB.x - R * 0.39f, cB.y, -0.003f), new Vector2(R * 0.5f, R * 0.08f), orange);
        Quad(panel, "AttWingR", new Vector3(cB.x + R * 0.39f, cB.y, -0.003f), new Vector2(R * 0.5f, R * 0.08f), orange);
        Quad(panel, "AttDot", new Vector3(cB.x, cB.y, -0.003f), new Vector2(R * 0.1f, R * 0.1f), orange);

        // 3) 高度計（長い針 = 1 周 1000m、短い針 = 1 周 10000m）
        Material altM = TexMat("AnalogALT", GenTex("analog_alt.png", 256, (u, v) => AnalogPix(u, v, 0f, 360f, 10, 5, null, null, null)), Color.white, 2992);
        var al = new string[10]; var aa = new float[10];
        for (int i = 0; i < 10; i++) { al[i] = i.ToString(); aa[i] = i * 36f; }
        Vector2 cC = new Vector2(P, Y1);
        Bezel(panel, cC, D, bezelMat);
        c.cpAltNeedle = Dial(panel, "ALT", cC, R, altM, al, aa, needle, R * 0.85f, 0.0022f, 0.085f * k);
        var kp = new GameObject("AltNeedleK").transform;
        kp.SetParent(c.cpAltNeedle.parent, false);
        kp.localPosition = new Vector3(cC.x, cC.y, -0.0055f);
        Quad(kp, "Needle", new Vector3(0f, R * 0.25f, 0f), new Vector2(0.0035f, R * 0.55f), needle);
        c.cpAltNeedleK = kp;
        Txt(panel, "ALTlab", "x100m", cC + new Vector2(0f, -R * 0.4f), 0.03f, 0.05f * k, W, TextAlignmentOptions.Center);

        // 4) 旋回計（目盛り = 1 秒に 3° の旋回）
        Material tcM = TexMat("AnalogTC", GenTex("analog_tc.png", 256, TurnFacePix), Color.white, 2992);
        Vector2 cD = new Vector2(-P, Y2);
        Bezel(panel, cD, D, bezelMat);
        Quad(panel, "TCFace", new Vector3(cD.x, cD.y, -0.001f), new Vector2(D, D), tcM);
        var tcp = new GameObject("TurnPlane").transform;
        tcp.SetParent(panel, false);
        tcp.localPosition = new Vector3(cD.x, cD.y, -0.004f);
        Quad(tcp, "Wings", Vector3.zero, new Vector2(R * 1.2f, R * 0.09f), needle);
        Quad(tcp, "Body", new Vector3(0f, R * 0.09f, 0f), new Vector2(R * 0.14f, R * 0.18f), needle);
        Quad(tcp, "Tail", new Vector3(0f, R * 0.2f, 0f), new Vector2(R * 0.05f, R * 0.14f), needle);
        c.cpTurnPlane = tcp;
        Txt(panel, "TCL", "L", cD + new Vector2(-R * 0.62f, -R * 0.32f), 0.01f, 0.07f * k, W, TextAlignmentOptions.Center);
        Txt(panel, "TCR", "R", cD + new Vector2(R * 0.62f, -R * 0.32f), 0.01f, 0.07f * k, W, TextAlignmentOptions.Center);
        Txt(panel, "TClab", "TURN", cD + new Vector2(0f, R * 0.45f), 0.03f, 0.05f * k, W, TextAlignmentOptions.Center);

        // 5) 方位計（円盤が回る。上の三角が今の向き）
        Vector2 cE = new Vector2(0f, Y2);
        Bezel(panel, cE, D, bezelMat);
        Material hdgM = TexMat("AnalogHDG", GenTex("analog_hdg.png", 256, (u, v) => AnalogPix(u, v, 0f, 360f, 12, 6, null, null, null)), Color.white, 2992);
        var card = new GameObject("HdgCard").transform;
        card.SetParent(panel, false);
        card.localPosition = new Vector3(cE.x, cE.y, -0.001f);
        Quad(card, "Card", Vector3.zero, new Vector2(D, D), hdgM);
        string[] hl = { "N", "3", "6", "E", "12", "15", "S", "21", "24", "W", "30", "33" };
        for (int i = 0; i < 12; i++)
        {
            float a2 = i * 30f * Mathf.Deg2Rad;
            var t = Txt(card, "H" + i, hl[i], new Vector2(Mathf.Sin(a2) * R * 0.6f, Mathf.Cos(a2) * R * 0.6f), 0.012f, (i % 3 == 0 ? 0.08f : 0.06f) * k, W, TextAlignmentOptions.Center);
            t.transform.localRotation = Quaternion.Euler(0f, 0f, -i * 30f);
        }
        c.cpHdgCard = card;
        Quad(panel, "HdgLubber", new Vector3(cE.x, cE.y + R * 0.93f, -0.004f), new Vector2(R * 0.1f, R * 0.22f), orange);
        Quad(panel, "HdgPlane", new Vector3(cE.x, cE.y, -0.004f), new Vector2(R * 0.4f, R * 0.07f), orange);
        Quad(panel, "HdgPlaneB", new Vector3(cE.x, cE.y, -0.004f), new Vector2(R * 0.07f, R * 0.45f), orange);

        // 6) 昇降計（±10 m/s。0 が左）
        Material vsiM = TexMat("AnalogVSI", GenTex("analog_vsi.png", 256, (u, v) => AnalogPix(u, v, -260f, 340f, 4, 5, null, null, null)), Color.white, 2992);
        string[] vl = { "10", "5", "0", "5", "10" };
        float[] va = { -260f, -175f, -90f, -5f, 80f };
        Vector2 cF = new Vector2(P, Y2);
        Bezel(panel, cF, D, bezelMat);
        c.cpVsiNeedle = Dial(panel, "VSI", cF, R, vsiM, vl, va, needle, R * 0.85f, 0.0022f, 0.085f * k);
        Txt(panel, "VSIup", "UP", cF + new Vector2(R * 0.05f, R * 0.25f), 0.02f, 0.05f * k, W, TextAlignmentOptions.Center);
        Txt(panel, "VSIdn", "DN", cF + new Vector2(R * 0.05f, -R * 0.3f), 0.02f, 0.05f * k, W, TextAlignmentOptions.Center);

        // 下の段: 迎角・スロットル（エンジン）・燃料
        float kk = r / 0.044f;
        float stallN = Mathf.Clamp01(c.stallAngle / 25f);
        Material aoaM = TexMat("AnalogAOA", GenTex("analog_aoa.png", 256, (u, v) => AnalogPix(u, v, -120f, 240f, 5, 5,
            new float[] { stallN, 1f }, new Color[] { new Color(0.9f, 0.15f, 0.1f) }, new float[] { 0.70f })), Color.white, 2992);
        Vector2 cG = new Vector2(-P, Y3);
        Bezel(panel, cG, d, bezelMat);
        c.cpAoaNeedle = Dial(panel, "AOA", cG, r, aoaM, new[] { "0", "10", "20" }, new[] { -120f, -24f, 72f }, needle, r * 0.85f, 0.002f, 0.085f * kk);
        Txt(panel, "AOAlab", "AoA", cG + new Vector2(0f, -r * 0.45f), 0.03f, 0.05f * kk, W, TextAlignmentOptions.Center);
        Material thrM = TexMat("AnalogTHR", GenTex("analog_thr.png", 256, (u, v) => AnalogPix(u, v, -120f, 240f, 5, 4,
            new float[] { 0.6f, 0.9f, 0.98f, 1f }, new Color[] { new Color(0.2f, 0.8f, 0.3f), new Color(0.9f, 0.15f, 0.1f) }, new float[] { 0.70f, 0.70f })), Color.white, 2992);
        Vector2 cH = new Vector2(0f, Y3);
        Bezel(panel, cH, d, bezelMat);
        c.cpThrNeedle = Dial(panel, "THR", cH, r, thrM, new[] { "0", "50", "100" }, new[] { -120f, 0f, 120f }, needle, r * 0.85f, 0.002f, 0.085f * kk);
        Txt(panel, "THRlab", "THR %", cH + new Vector2(0f, -r * 0.45f), 0.03f, 0.05f * kk, W, TextAlignmentOptions.Center);
        Material fuelM = TexMat("AnalogFUEL", GenTex("analog_fuel.png", 256, (u, v) => AnalogPix(u, v, -60f, 120f, 4, 2,
            new float[] { 0f, 0.2f }, new Color[] { new Color(0.9f, 0.15f, 0.1f) }, new float[] { 0.70f })), Color.white, 2992);
        Vector2 cI = new Vector2(P, Y3);
        Bezel(panel, cI, d, bezelMat);
        c.cpFuelNeedle = Dial(panel, "FUEL", cI, r, fuelM, new[] { "E", "1/2", "F" }, new[] { -60f, 0f, 60f }, needle, r * 0.85f, 0.002f, 0.085f * kk);
        Txt(panel, "FUELlab", "FUEL", cI + new Vector2(0f, -r * 0.45f), 0.03f, 0.05f * kk, W, TextAlignmentOptions.Center);

        if (gaugesOnly) return;

        // 警告灯（上の中央）
        Transform ann = Lcd(panel, "Annunciator", new Vector2(0f, 0.112f), new Vector2(0.16f, 0.03f), lcdMat, bezelMat);
        c.cpWarn = Txt(ann, "Warn", "", new Vector2(0f, 0.005f), 0.155f, 0.075f, Color.white, TextAlignmentOptions.Center);
        c.cpStatus = Txt(ann, "Status", "", new Vector2(0f, -0.009f), 0.155f, 0.04f, LG, TextAlignmentOptions.Center);

        // 左の四角い画面: HP・武器・ロック・フラップなど・燃料・撃墜数・人数
        const float MX = 0.131f, MW = 0.084f, MH = 0.22f, MY = 0.01f;   // 丸い計器と重ならない位置
        Transform lcdL = Mfd(panel, "MfdLeft", new Vector2(-MX, MY), new Vector2(MW, MH), lcdMat, bezelMat, osbMat);
        Txt(lcdL, "HpLab", "HP", new Vector2(-0.03f, 0.093f), 0.02f, 0.05f, LG, TextAlignmentOptions.Left);
        c.cpHp = Txt(lcdL, "Hp", "100", new Vector2(0.008f, 0.09f), 0.065f, 0.09f, LG, TextAlignmentOptions.Right);
        c.cpWeapons = Txt(lcdL, "Weapons", "", new Vector2(0f, 0.052f), 0.082f, 0.045f, LG, TextAlignmentOptions.Left, 0.04f);
        c.cpLock = Txt(lcdL, "Lock", "", new Vector2(0f, 0.018f), 0.082f, 0.045f, LG, TextAlignmentOptions.Center);
        c.cpFlags = Txt(lcdL, "Flags", "", new Vector2(0f, -0.022f), 0.082f, 0.042f, LG, TextAlignmentOptions.Left, 0.045f);
        c.cpFuel = Txt(lcdL, "Fuel", "", new Vector2(0f, -0.058f), 0.082f, 0.04f, LG, TextAlignmentOptions.Center);
        c.cpKills = Txt(lcdL, "Kills", "", new Vector2(0f, -0.078f), 0.082f, 0.04f, LG, TextAlignmentOptions.Center);
        c.cpPlayers = Txt(lcdL, "Online", "ONLINE 1", new Vector2(0f, -0.096f), 0.082f, 0.04f, LG, TextAlignmentOptions.Center);

        // 右の四角い画面: 上 = レーダー、下 = 入室記録
        Transform lcdR = Mfd(panel, "MfdRight", new Vector2(MX, MY), new Vector2(MW, MH), lcdMat, bezelMat, osbMat);
        Transform rp = root.Find("RadarPanel");
        if (rp != null)
        {
            rp.SetParent(lcdR, false);
            rp.localPosition = new Vector3(0f, 0.058f, -0.001f);
            rp.localRotation = Quaternion.identity;
            rp.localScale = Vector3.one * 0.26f;
        }
        c.cpLog = Txt(lcdR, "Log", "", new Vector2(0f, -0.045f), 0.082f, 0.032f, LG, TextAlignmentOptions.TopLeft, 0.12f);

        // 数字だけの表示・パネル用の部品は使わない
        c.cpSpeed = null; c.cpAlt = null; c.cpVs = null; c.cpAtt = null; c.cpHdg = null; c.cpAoa = null; c.cpThr = null;
        c.cpHorizon = null; c.cpThrBar = null; c.cpAoaBar = null; c.cpHpBar = null; c.cpGun = null; c.cpGunBar = null;
        c.staticMat = null;
        c.cpHdgNum = null; c.cpVsNum = null; c.cpFuelPct = null;
    }


    // ---------------- HUD（前の空中に緑の線と文字で表示。計器が読めなくても全部分かる） ----------------
    // 真ん中: 機首の向き（W）・飛んでいる方向（○）・水平線と角度のはしご・傾き
    // 上: 方位のテープ / 左: 速度・迎角・マッハ・G・弾数・スロットル・操縦桿 / 右: 高度・上昇率・HP・各機能の状態
    const float HudL = 1f;   // 目からの距離（コックピットの単位）
    static Vector3 HudAt(float xDeg, float yDeg) { return new Vector3(Mathf.Tan(xDeg * Mathf.Deg2Rad) * HudL, Mathf.Tan(yDeg * Mathf.Deg2Rad) * HudL, HudL); }
    static Material hudLineMat, hudFontMat;
    static Material HudLine()
    {
        string path = FxDir + "/HUD_Line.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = m == null;
        if (create) m = new Material(Shader.Find("FlightUI/UnlitTint"));
        m.SetColor("_Color", new Color(HudGreen.r, HudGreen.g, HudGreen.b, 0.95f));
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3090;
        if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
        return m;
    }
    static TextMeshPro HudTxt(Transform p, string name, string text, Vector3 lp, float w, float size, TextAlignmentOptions al)
    {
        var t = Txt(p, name, text, Vector2.zero, w, size, HudGreen, al);
        t.transform.localPosition = lp;
        if (hudFontMat == null)
        {
            string fp = FxDir + "/HUD_Font.mat";
            hudFontMat = AssetDatabase.LoadAssetAtPath<Material>(fp);
            if (hudFontMat == null)
            {
                hudFontMat = new Material(t.fontSharedMaterial);
                Shader ov = Shader.Find("TextMeshPro/Distance Field Overlay");
                if (ov != null) hudFontMat.shader = ov;
                hudFontMat.renderQueue = 3100;
                AssetDatabase.CreateAsset(hudFontMat, fp);
            }
        }
        t.fontSharedMaterial = hudFontMat;
        return t;
    }
    // 線（太さ w・長さ len・角度 deg）
    static void HudSeg(Transform p, string name, Vector3 lp, float len, float w, float deg)
    {
        var q = Quad(p, name, lp, new Vector2(len, w), hudLineMat);
        q.transform.localRotation = Quaternion.Euler(0f, 0f, deg);
    }

    static void BuildHud(controller c, Transform root)
    {
        Transform old = root.Find("FlightHud");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        hudLineMat = HudLine();
        hudFontMat = null;
        var hud = new GameObject("FlightHud").transform;
        hud.SetParent(root, false);
        Undo.RecordObject(c, "Flight setup");
        const float LW = 0.0025f;          // 線の太さ
        const float TS = 0.17f;            // 数字の大きさ
        const float LS = 0.11f;            // 名前の大きさ

        // 機首の向き（W 字）
        var bore = new GameObject("Boresight").transform;
        bore.SetParent(hud, false);
        bore.localPosition = HudAt(0f, 0f);
        HudSeg(bore, "W1", new Vector3(-0.022f, 0f, 0f), 0.02f, LW, 0f);
        HudSeg(bore, "W2", new Vector3(-0.009f, -0.006f, 0f), 0.014f, LW, 60f);
        HudSeg(bore, "W3", new Vector3(0.009f, -0.006f, 0f), 0.014f, LW, -60f);
        HudSeg(bore, "W4", new Vector3(0.022f, 0f, 0f), 0.02f, LW, 0f);

        // 飛んでいる方向（丸に翼）
        var fpm = new GameObject("FlightPath").transform;
        fpm.SetParent(hud, false);
        fpm.localPosition = HudAt(0f, 0f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f;
            HudSeg(fpm, "C" + i, new Vector3(Mathf.Cos((a + 22.5f) * Mathf.Deg2Rad) * 0.009f, Mathf.Sin((a + 22.5f) * Mathf.Deg2Rad) * 0.009f, 0f), 0.0075f, LW, a + 112.5f);
        }
        HudSeg(fpm, "WingL", new Vector3(-0.02f, 0f, 0f), 0.022f, LW, 0f);
        HudSeg(fpm, "WingR", new Vector3(0.02f, 0f, 0f), 0.022f, LW, 0f);
        HudSeg(fpm, "Fin", new Vector3(0f, 0.014f, 0f), 0.01f, LW, 90f);
        c.hudFpm = fpm;

        // 角度のはしご（世界の水平に合わせて毎フレーム回す）
        var lad = new GameObject("PitchLadder").transform;
        lad.SetParent(hud, false);
        var lines = new System.Collections.Generic.List<Transform>();
        var degs = new System.Collections.Generic.List<float>();
        for (int d = -80; d <= 80; d += 10)
        {
            var ln = new GameObject("P" + d).transform;
            ln.SetParent(lad, false);
            float th = d * Mathf.Deg2Rad;
            ln.localPosition = new Vector3(0f, Mathf.Sin(th), Mathf.Cos(th)) * HudL;
            ln.localRotation = Quaternion.Euler(-d, 0f, 0f);
            if (d == 0)
            {
                HudSeg(ln, "L", new Vector3(-0.15f, 0f, 0f), 0.22f, LW, 0f);
                HudSeg(ln, "R", new Vector3(0.15f, 0f, 0f), 0.22f, LW, 0f);
            }
            else
            {
                float tick = d > 0 ? -0.012f : 0.012f;
                if (d > 0)
                {
                    HudSeg(ln, "L", new Vector3(-0.07f, 0f, 0f), 0.08f, LW, 0f);
                    HudSeg(ln, "R", new Vector3(0.07f, 0f, 0f), 0.08f, LW, 0f);
                }
                else
                {
                    // 下向きは破線
                    for (int k = 0; k < 3; k++)
                    {
                        HudSeg(ln, "L" + k, new Vector3(-0.04f - k * 0.03f, 0f, 0f), 0.018f, LW, 0f);
                        HudSeg(ln, "R" + k, new Vector3(0.04f + k * 0.03f, 0f, 0f), 0.018f, LW, 0f);
                    }
                }
                HudSeg(ln, "TL", new Vector3(-0.11f, tick * 0.5f, 0f), 0.012f, LW, 90f);
                HudSeg(ln, "TR", new Vector3(0.11f, tick * 0.5f, 0f), 0.012f, LW, 90f);
                HudTxt(ln, "NL", Mathf.Abs(d).ToString(), new Vector3(-0.135f, 0f, 0f), 0.04f, 0.1f, TextAlignmentOptions.Right);
                HudTxt(ln, "NR", Mathf.Abs(d).ToString(), new Vector3(0.135f, 0f, 0f), 0.04f, 0.1f, TextAlignmentOptions.Left);
            }
            lines.Add(ln); degs.Add(d);
        }
        c.hudLadder = lad;
        c.hudLadderLines = lines.ToArray();
        c.hudLadderDeg = degs.ToArray();

        // 傾き（上の円弧の目盛りと、空の方向を指す三角）
        var rollS = new GameObject("RollScale").transform;
        rollS.SetParent(hud, false);
        rollS.localPosition = HudAt(0f, 0f);
        float rr = Mathf.Tan(11f * Mathf.Deg2Rad) * HudL;
        foreach (float tk in new[] { 0f, 10f, -10f, 20f, -20f, 30f, -30f, 45f, -45f, 60f, -60f })
        {
            float a = (90f + tk) * Mathf.Deg2Rad;
            float len = (tk == 0f || Mathf.Abs(tk) == 30f || Mathf.Abs(tk) == 60f) ? 0.022f : 0.012f;
            HudSeg(rollS, "T" + tk, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (rr + len * 0.5f), len, LW, 90f + tk);
        }
        var rp = new GameObject("RollPointer").transform;
        rp.SetParent(hud, false);
        rp.localPosition = HudAt(0f, 0f);
        HudSeg(rp, "A", new Vector3(-0.004f, rr - 0.008f, 0f), 0.011f, LW, 60f);
        HudSeg(rp, "B", new Vector3(0.004f, rr - 0.008f, 0f), 0.011f, LW, -60f);
        HudSeg(rp, "C", new Vector3(0f, rr - 0.0125f, 0f), 0.011f, LW, 0f);
        c.hudRollPtr = rp;

        // 方位のテープ（上）
        float tapeY = Mathf.Tan(19f * Mathf.Deg2Rad) * HudL;
        var tape = new GameObject("HeadingTape").transform;
        tape.SetParent(hud, false);
        tape.localPosition = new Vector3(0f, tapeY, HudL);
        int nT = 13;
        var ticks = new Transform[nT];
        var labs = new TextMeshPro[nT];
        for (int i = 0; i < nT; i++)
        {
            var tk = new GameObject("Tick" + i).transform;
            tk.SetParent(tape, false);
            HudSeg(tk, "Bar", new Vector3(0f, 0.008f, 0f), 0.016f, LW, 90f);
            ticks[i] = tk;
            labs[i] = HudTxt(tk, "Num", "", new Vector3(0f, 0.03f, 0f), 0.06f, 0.1f, TextAlignmentOptions.Center);
        }
        HudSeg(tape, "CaretA", new Vector3(-0.005f, -0.008f, 0f), 0.012f, LW, -60f);
        HudSeg(tape, "CaretB", new Vector3(0.005f, -0.008f, 0f), 0.012f, LW, 60f);
        c.hudTapeTicks = ticks;
        c.hudTapeLabels = labs;
        c.hudTapeScale = 0.006f;
        c.hudHdg = HudTxt(tape, "Hdg", "000", new Vector3(0f, -0.035f, 0f), 0.08f, TS, TextAlignmentOptions.Center);
        HudTxt(tape, "HdgLab", "HDG", new Vector3(-0.07f, -0.035f, 0f), 0.06f, LS, TextAlignmentOptions.Right);

        // 左の列: 名前（左寄せ）と数字（右寄せ）
        float xl0 = Mathf.Tan(-36f * Mathf.Deg2Rad) * HudL, xl1 = Mathf.Tan(-21f * Mathf.Deg2Rad) * HudL;
        string[] ln2 = { "SPEED km/h", "AoA °", "MACH", "G", "MAX G", "GUN", "ROCKET", "MISSILE" };
        var vals = new TextMeshPro[ln2.Length];
        for (int i = 0; i < ln2.Length; i++)
        {
            float y = Mathf.Tan((16f - i * 2.6f) * Mathf.Deg2Rad) * HudL;
            HudTxt(hud, "LL" + i, ln2[i], new Vector3(xl0 + 0.05f, y, HudL), 0.1f, LS, TextAlignmentOptions.Left);
            vals[i] = HudTxt(hud, "LV" + i, "", new Vector3(xl1 - 0.04f, y, HudL), 0.08f, i == 0 ? TS * 1.25f : TS, TextAlignmentOptions.Right);
        }
        c.hudSpd = vals[0]; c.hudAoa = vals[1]; c.hudMach = vals[2]; c.hudG = vals[3]; c.hudMaxG = vals[4];
        c.hudGun = vals[5]; c.hudRkt = vals[6]; c.hudMsl = vals[7];
        // スロットル（横の棒）
        float yThr = Mathf.Tan(-5.5f * Mathf.Deg2Rad) * HudL;
        HudTxt(hud, "ThrLab", "THROTTLE", new Vector3(xl0 + 0.05f, yThr + 0.025f, HudL), 0.1f, LS, TextAlignmentOptions.Left);
        float bw = xl1 - xl0;
        var thrBg = new GameObject("ThrFrame").transform;
        thrBg.SetParent(hud, false);
        thrBg.localPosition = new Vector3(xl0, yThr, HudL);
        HudSeg(thrBg, "Top", new Vector3(bw / 2f, 0.008f, 0f), bw, LW, 0f);
        HudSeg(thrBg, "Bot", new Vector3(bw / 2f, -0.008f, 0f), bw, LW, 0f);
        HudSeg(thrBg, "L", new Vector3(0f, 0f, 0f), 0.016f, LW, 90f);
        HudSeg(thrBg, "R", new Vector3(bw, 0f, 0f), 0.016f, LW, 90f);
        var thrFill = new GameObject("ThrFill").transform;
        thrFill.SetParent(thrBg, false);
        Quad(thrFill, "Fill", new Vector3(bw / 2f, 0f, 0f), new Vector2(bw, 0.011f), hudLineMat);
        c.hudThrFill = thrFill;
        // 操縦桿（四角の中の点）・ラダー（下の棒の点）
        float yStk = Mathf.Tan(-14f * Mathf.Deg2Rad) * HudL, xs = (xl0 + xl1) * 0.5f, hs = 0.045f;
        HudTxt(hud, "StkLab", "STICK", new Vector3(xs - hs - 0.03f, yStk + hs - 0.01f, HudL), 0.06f, LS, TextAlignmentOptions.Right);
        var box = new GameObject("StickBox").transform;
        box.SetParent(hud, false);
        box.localPosition = new Vector3(xs, yStk, HudL);
        HudSeg(box, "T", new Vector3(0f, hs, 0f), hs * 2f, LW, 0f);
        HudSeg(box, "B", new Vector3(0f, -hs, 0f), hs * 2f, LW, 0f);
        HudSeg(box, "L", new Vector3(-hs, 0f, 0f), hs * 2f, LW, 90f);
        HudSeg(box, "R", new Vector3(hs, 0f, 0f), hs * 2f, LW, 90f);
        HudSeg(box, "H", Vector3.zero, hs * 2f, LW * 0.5f, 0f);
        HudSeg(box, "V", Vector3.zero, hs * 2f, LW * 0.5f, 90f);
        var dot = Quad(box, "Dot", Vector3.zero, new Vector2(0.012f, 0.012f), hudLineMat).transform;
        c.hudStickDot = dot;
        HudSeg(box, "YawBar", new Vector3(0f, -hs - 0.015f, 0f), hs * 2f, LW, 0f);
        var yd = Quad(box, "YawDot", new Vector3(0f, -hs - 0.015f, 0f), new Vector2(0.006f, 0.014f), hudLineMat).transform;
        c.hudYawDot = yd;
        c.hudStickSize = hs;

        // 右の列
        float xr0 = Mathf.Tan(21f * Mathf.Deg2Rad) * HudL, xr1 = Mathf.Tan(36f * Mathf.Deg2Rad) * HudL;
        string[] rn = { "ALTITUDE m", "CLIMB m/s" };
        var rv = new TextMeshPro[2];
        for (int i = 0; i < 2; i++)
        {
            float y = Mathf.Tan((16f - i * 2.6f) * Mathf.Deg2Rad) * HudL;
            HudTxt(hud, "RL" + i, rn[i], new Vector3(xr0 + 0.05f, y, HudL), 0.1f, LS, TextAlignmentOptions.Left);
            rv[i] = HudTxt(hud, "RV" + i, "", new Vector3(xr1 - 0.04f, y, HudL), 0.08f, i == 0 ? TS * 1.25f : TS, TextAlignmentOptions.Right);
        }
        c.hudAlt = rv[0]; c.hudVs = rv[1];
        // HP（横の棒）
        float yHp = Mathf.Tan(10.2f * Mathf.Deg2Rad) * HudL, bw2 = xr1 - xr0;
        HudTxt(hud, "HpLab", "HP", new Vector3(xr0 + 0.05f, yHp + 0.025f, HudL), 0.1f, LS, TextAlignmentOptions.Left);
        var hpBg = new GameObject("HpFrame").transform;
        hpBg.SetParent(hud, false);
        hpBg.localPosition = new Vector3(xr0, yHp, HudL);
        HudSeg(hpBg, "Top", new Vector3(bw2 / 2f, 0.008f, 0f), bw2, LW, 0f);
        HudSeg(hpBg, "Bot", new Vector3(bw2 / 2f, -0.008f, 0f), bw2, LW, 0f);
        HudSeg(hpBg, "L", Vector3.zero, 0.016f, LW, 90f);
        HudSeg(hpBg, "R", new Vector3(bw2, 0f, 0f), 0.016f, LW, 90f);
        var hpFill = new GameObject("HpFill").transform;
        hpFill.SetParent(hpBg, false);
        Quad(hpFill, "Fill", new Vector3(bw2 / 2f, 0f, 0f), new Vector2(bw2, 0.011f), hudLineMat);
        c.hudHpFill = hpFill;
        // 機能の状態（使っている時は明るく・枠付き）
        string[] fn = { "BRAKE", "FLAPS", "BOOST", "LOCK", "VOICE" };
        var fl = new TextMeshPro[fn.Length];
        for (int i = 0; i < fn.Length; i++)
        {
            float y = Mathf.Tan((6f - i * 2.8f) * Mathf.Deg2Rad) * HudL;
            fl[i] = HudTxt(hud, "F" + fn[i], "", new Vector3((xr0 + xr1) * 0.5f, y, HudL), 0.12f, TS, TextAlignmentOptions.Center);
        }
        c.hudFlags = fl;
        foreach (var t in hud.GetComponentsInChildren<TextMeshPro>(true)) t.fontStyle = FontStyles.Bold;
        foreach (var r in hud.GetComponentsInChildren<MeshRenderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    }

    // Omega の緑の HUD の整理: 速度計・高度計の絵は下の丸い計器に任せて消し、数字を上に詰める
    static void OmegaHudTidy(controller c, Transform root)
    {
        Transform main = root.Find("MainPanel");
        if (main != null)
        {
            foreach (var nm in new[] { "SpdDial", "AltDial" })
            {
                Transform o = main.Find(nm);
                if (o != null) Object.DestroyImmediate(o.gameObject);
            }
            void Move(string nm, float x, float y)
            {
                Transform t = main.Find(nm);
                if (t != null) t.localPosition = new Vector3(x, y, t.localPosition.z);
            }
            Move("SpdLabel", -0.15f, 0.09f);
            Move("AltLabel", 0.15f, 0.09f);
            Move("Speed", -0.15f, 0.03f);
            Move("Alt", 0.15f, 0.03f);
            if (c.cpAtt != null) { var at = c.cpAtt.transform; at.localPosition = new Vector3(0f, -0.01f, at.localPosition.z); }
            Move("Warn", 0f, -0.05f);
            Move("Status", 0f, -0.085f);
        }
        // 横の表示は機体（翼・胴体）に重ならないよう、水平線より上（空の方）へ
        void Place(string nm, Vector3 pos)
        {
            Transform t = root.Find(nm);
            if (t == null) return;
            t.localPosition = pos;
            t.localRotation = Quaternion.LookRotation(pos.normalized, Vector3.up);   // 目の方を向ける
        }
        Place("LeftPanel", new Vector3(-0.40f, 0.10f, 0.66f));     // 迎角・フラップ・ブレーキなど
        Place("WeaponPanel", new Vector3(-0.62f, 0.10f, 0.48f));   // HP・武器
        Place("RightPanel", new Vector3(0.62f, 0.10f, 0.48f));     // スロットル・上昇率・燃料（レーダーの右）
        Place("LogPanel", new Vector3(0.62f, 0.38f, 0.48f));       // 入室記録（右上）
        Undo.RecordObject(c, "Flight setup");
        c.cpSpdNeedle = null; c.cpAltNeedle = null; c.cpAltNeedleK = null;
        // 前に作った線の HUD は使わない
        c.hudFpm = null; c.hudLadder = null; c.hudLadderLines = null; c.hudLadderDeg = null; c.hudRollPtr = null;
        c.hudTapeTicks = null; c.hudTapeLabels = null; c.hudHdg = null; c.hudSpd = null; c.hudAoa = null; c.hudMach = null;
        c.hudG = null; c.hudMaxG = null; c.hudGun = null; c.hudRkt = null; c.hudMsl = null; c.hudAlt = null; c.hudVs = null;
        c.hudThrFill = null; c.hudHpFill = null; c.hudStickDot = null; c.hudYawDot = null; c.hudFlags = null;
        c.cpHdgNum = null; c.cpVsNum = null; c.cpFuelPct = null;
    }

    // 戦闘機の四角い画面（多機能表示装置）: 液晶の周りに押しボタンが並ぶ
    static Transform Mfd(Transform p, string name, Vector2 pos, Vector2 size, Material lcd, Material frame, Material btn)
    {
        Transform t = Lcd(p, name, pos, size, lcd, frame);
        float bw = size.x * 0.13f, bh = 0.006f;
        for (int i = 0; i < 5; i++)
        {
            float x = (i - 2) * size.x * 0.2f;
            Quad(t, "OsbT" + i, new Vector3(x, size.y / 2f + 0.0075f, -0.0005f), new Vector2(bw, bh), btn);
            Quad(t, "OsbB" + i, new Vector3(x, -size.y / 2f - 0.0075f, -0.0005f), new Vector2(bw, bh), btn);
        }
        return t;
    }

    // 計器の黒い縁（円柱）
    static void Bezel(Transform p, Vector2 pos, float dia, Material m)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(b.GetComponent<Collider>());
        b.name = "Bezel";
        b.transform.SetParent(p, false);
        b.transform.localPosition = new Vector3(pos.x, pos.y, 0.00175f);   // 文字盤（-0.001）より奥、計器盤から少し出る
        b.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        b.transform.localScale = new Vector3(dia * 1.14f, 0.00225f, dia * 1.14f);
        var mr = b.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // 小さな液晶画面（黒い縁＋暗い緑の画面）。子に文字を入れる
    static Transform Lcd(Transform p, string name, Vector2 pos, Vector2 size, Material lcd, Material frame)
    {
        var t = new GameObject(name).transform;
        t.SetParent(p, false);
        t.localPosition = new Vector3(pos.x, pos.y, -0.001f);
        var fr = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(fr.GetComponent<Collider>());
        fr.name = "Frame";
        fr.transform.SetParent(t, false);
        fr.transform.localPosition = new Vector3(0f, 0f, 0.005f);
        fr.transform.localScale = new Vector3(size.x + 0.008f, size.y + 0.008f, 0.008f);
        fr.GetComponent<MeshRenderer>().sharedMaterial = frame;
        Quad(t, "Screen", Vector3.zero, size, lcd);
        return t;
    }

    // 丸い計器の文字盤: 黒地に白い目盛り。色の帯（arcs: 始め,終わり の組・0〜1）を内側に
    static Color AnalogPix(float u, float v, float start, float sweep, int majors, int minorsPer, float[] arcs, Color[] arcCols, float[] arcInner)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > 1f) return new Color(0f, 0f, 0f, 0f);
        Color face = new Color(0.05f, 0.05f, 0.055f, 1f);
        Color c = face;
        int total = majors * minorsPer;
        float step = sweep / total;
        float rel = (ClockAngle(u, v) - start + 720f) % 360f;
        bool full = sweep >= 359f;
        // 色の帯
        if (arcs != null && rel <= sweep + 0.5f)
        {
            float f = rel / sweep;
            for (int i = 0; i + 1 < arcs.Length; i += 2)
            {
                float inner = arcInner != null && i / 2 < arcInner.Length ? arcInner[i / 2] : 0.70f;
                if (f >= arcs[i] && f <= arcs[i + 1] && r > inner && r < 0.79f) c = arcCols[i / 2];
            }
        }
        int k = Mathf.RoundToInt(rel / step);
        if (full) k %= total;
        if (k >= 0 && (full || k <= total))
        {
            float dAng = rel - k * step;
            if (dAng > 180f) dAng -= 360f;
            float perp = Mathf.Abs(r * Mathf.Sin(dAng * Mathf.Deg2Rad));
            bool major = k % minorsPer == 0;
            float inner = major ? 0.78f : 0.87f;
            if (r > inner && r < 0.97f && perp < (major ? 0.022f : 0.011f)) c = new Color(0.95f, 0.95f, 0.92f, 1f);
        }
        if (r > 0.975f) c = new Color(0.12f, 0.12f, 0.13f, 1f);   // 外周
        return c;
    }

    // 姿勢指示器の球: 上半分が空（青）、下半分が地面（茶）。白い地平線と 10° ごとの線（縦 1 枚 = 180°）
    static Color AttitudeBallPix(float u, float v)
    {
        float t = (v + 1f) * 0.5f;                  // 0（下）〜1（上）
        float deg = (t - 0.5f) * 180f;              // 地平線からの角度
        Color c = deg >= 0f ? new Color(0.22f, 0.55f, 0.88f) : new Color(0.50f, 0.33f, 0.18f);
        float px = 2f / 256f * 180f;                // 1 ピクセルの角度
        if (Mathf.Abs(deg) < px * 1.5f) return Color.white;
        float m = Mathf.Repeat(deg + 5f, 10f) - 5f;
        float w = Mathf.Abs(deg) % 20f < 1f || Mathf.Abs(deg) % 20f > 19f ? 0.35f : 0.2f;   // 20° ごとは長い線
        if (Mathf.Abs(m) < px * 0.8f && Mathf.Abs(u) < w && Mathf.Abs(deg) < 85f) return Color.white;
        return c;
    }

    // 姿勢指示器の縁: 外側は黒、上半分に傾きの目盛り（0・10・20・30・60°）、中は透明
    static Color AttitudeMaskPix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > 1f) return new Color(0f, 0f, 0f, 0f);
        if (r < 0.80f) return new Color(0f, 0f, 0f, 0f);
        Color c = new Color(0.05f, 0.05f, 0.055f, 1f);
        float ang = ClockAngle(u, v);
        if (ang > 180f) ang -= 360f;
        foreach (float tk in new[] { 0f, 10f, -10f, 20f, -20f, 30f, -30f, 60f, -60f })
        {
            float perp = Mathf.Abs(r * Mathf.Sin((ang - tk) * Mathf.Deg2Rad));
            float len = (tk == 30f || tk == -30f || tk == 60f || tk == -60f) ? 0.86f : 0.90f;
            if (Mathf.Abs(ang - tk) < 20f && r > len && r < 0.98f && perp < (tk == 0f ? 0.03f : 0.016f)) c = tk == 0f ? new Color(1f, 0.55f, 0.1f) : new Color(0.95f, 0.95f, 0.92f);
        }
        return c;
    }

    // 旋回計の文字盤: 黒地に、水平と「1 秒 3°」の目盛り（±20°）
    static Color TurnFacePix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > 1f) return new Color(0f, 0f, 0f, 0f);
        Color c = new Color(0.05f, 0.05f, 0.055f, 1f);
        float ang = Mathf.Atan2(v, u) * Mathf.Rad2Deg;   // 右 = 0、上 = 90
        foreach (float tk in new[] { 0f, -20f, 180f, 200f })
        {
            float dd = Mathf.DeltaAngle(ang, tk);
            float perp = Mathf.Abs(r * Mathf.Sin(dd * Mathf.Deg2Rad));
            if (Mathf.Abs(dd) < 30f && r > 0.72f && r < 0.95f && perp < 0.025f) c = new Color(0.95f, 0.95f, 0.92f, 1f);
        }
        if (r > 0.975f) c = new Color(0.12f, 0.12f, 0.13f, 1f);
        return c;
    }

    static bool fontOvOutlined;
    static void ConvertToHud(controller c, Transform root)
    {
        fontOvOutlined = false;
        // パネルの飾り（背景・箱・角・線・砂嵐）を消す
        var kill = new System.Collections.Generic.List<GameObject>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n == "BG" || n == "Housing" || n == "Corner" || n == "TopEdge" || n == "BottomEdge" || n == "Static" || n == "Sep" || n == "Sep2" || n.EndsWith("BarBG") || n == "Hub")
                kill.Add(t.gameObject);
        }
        foreach (var g in kill) if (g != null) Object.DestroyImmediate(g);

        // 文字: 緑・何にも隠れない（オーバーレイ）
        Material fontOv = null;
        foreach (var tmp in root.GetComponentsInChildren<TextMeshPro>(true))
        {
            if (fontOv == null)
            {
                string fp = FxDir + "/HUD_Font.mat";
                fontOv = AssetDatabase.LoadAssetAtPath<Material>(fp);
                if (fontOv == null)
                {
                    fontOv = new Material(tmp.fontSharedMaterial);
                    Shader ov = Shader.Find("TextMeshPro/Distance Field Overlay");
                    if (ov != null) fontOv.shader = ov;
                    fontOv.renderQueue = 3100;
                    AssetDatabase.CreateAsset(fontOv, fp);
                }
            }
            if (!fontOvOutlined)
            {
                // 黒い縁取り: 明るい空・暗い機体のどちらに重なっても読める
                fontOvOutlined = true;
                fontOv.EnableKeyword("OUTLINE_ON");
                if (fontOv.HasProperty("_OutlineWidth")) fontOv.SetFloat("_OutlineWidth", 0.22f);
                if (fontOv.HasProperty("_OutlineColor")) fontOv.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0.85f));
                if (fontOv.HasProperty("_FaceDilate")) fontOv.SetFloat("_FaceDilate", 0.1f);
                EditorUtility.SetDirty(fontOv);
            }
            tmp.fontSharedMaterial = fontOv;
            tmp.color = new Color(HudGreen.r, HudGreen.g, HudGreen.b, tmp.color.a);
        }

        // 計器・バー・レーダーなどの絵: 緑・オーバーレイ版のマテリアルに
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponent<TextMeshPro>() != null) continue;
            Material m = r.sharedMaterial;
            if (m == null) continue;
            if (m.name == "CockpitBlackout" || m.name == "CockpitDamageTint" || m.name.StartsWith("LockRed") || m.name.StartsWith("HUD_") || m.name.StartsWith("RadarMap")) continue;
            r.sharedMaterial = HudMat(m);
        }
        Undo.RecordObject(c, "Flight setup");
        if (c.radarMats != null) { var a = new Material[c.radarMats.Length]; for (int i = 0; i < a.Length; i++) a[i] = HudMat(c.radarMats[i]); c.radarMats = a; }
        if (c.radarMslMats != null) { var a = new Material[c.radarMslMats.Length]; for (int i = 0; i < a.Length; i++) a[i] = HudMat(c.radarMslMats[i]); c.radarMslMats = a; }
        c.staticMat = null;
    }

    // 元のマテリアルを、何にも隠れない（ZTest Always）・透ける・緑（色付きの記号はそのままの色）の HUD 用にしたもの
    static Material HudMat(Material m)
    {
        if (m == null) return null;
        string path = FxDir + "/HUD_" + m.name + ".mat";
        var h = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = h == null;
        if (create) h = new Material(Shader.Find("FlightUI/UnlitTint"));
        Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        // 計器の文字盤とレーダーの画面は、暗い塗りつぶしを薄くした版を使う
        if (m.name == "DialSpeed") tex = GenTex("dial_speed_hud.png", 256, (u, v) => HudFade(DialPix(u, v, -135f, 270f, 6, 5)));
        else if (m.name == "DialAlt") tex = GenTex("dial_alt_hud.png", 256, (u, v) => HudFade(DialPix(u, v, 0f, 360f, 10, 5)));
        else if (m.name == "RadarScope") tex = GenTex("radar_scope_hud.png", 512, (u, v) => HudFade(RadarScopePix(u, v)));
        Color src = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        bool keepColor = m.name.StartsWith("RadarIcon") || m.name.StartsWith("RadarMsl") || m.name == "CockpitRed" || m.name == "ModeHard";
        Color col = keepColor ? src : new Color(HudGreen.r, HudGreen.g, HudGreen.b, Mathf.Clamp01(src.a) * 0.95f);
        if (tex != null) h.SetTexture("_MainTex", tex);
        h.SetColor("_Color", col);
        h.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        h.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        h.SetFloat("_ZWrite", 0f);
        h.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        h.SetOverrideTag("RenderType", "Transparent");
        h.renderQueue = 3000 + Mathf.Clamp(m.renderQueue - 2985, 1, 99);   // 元の重なり順を保つ
        if (create) AssetDatabase.CreateAsset(h, path);
        else EditorUtility.SetDirty(h);
        return h;
    }

    // 暗い塗りつぶしの部分だけ、ほとんど透明にする（線や目盛りは残す）
    static Color HudFade(Color c)
    {
        float bright = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (bright < 0.25f) c.a *= 0.12f;
        return c;
    }

    static Material TexMat(string name, Texture2D tex, Color col, int queue)
    {
        Material m = UnlitMat(name, col, true, queue);
        m.SetTexture("_MainTex", tex);
        m.SetColor("_Color", col);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.renderQueue = queue;
        EditorUtility.SetDirty(m);
        return m;
    }

    static float Line(float dist, float hw) { return 1f - Mathf.Clamp01((Mathf.Abs(dist) - hw) / 0.006f); }

    // 時計回りに上から測った角度（度, 0〜360）
    static float ClockAngle(float u, float v) { float a = Mathf.Atan2(u, v) * Mathf.Rad2Deg; return a < 0f ? a + 360f : a; }

    static Color DialPix(float u, float v, float start, float sweep, int majors, int minorsPer)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > 1f) return new Color(0f, 0f, 0f, 0f);
        Color face = new Color(0.01f, 0.05f, 0.09f, 0.92f);
        Color cyan = new Color(0.35f, 0.95f, 1f, 1f);
        float ink = Line(r - 0.965f, 0.012f) * 0.9f;   // 外周の輪
        float ink2 = 0f;
        int total = majors * minorsPer;
        float step = sweep / total;
        float rel = (ClockAngle(u, v) - start + 720f) % 360f;
        int k = Mathf.RoundToInt(rel / step);
        bool full = sweep >= 359f;
        if (full) k %= total;
        if (k >= 0 && (full || k <= total))
        {
            float dAng = (rel - k * step);
            if (dAng > 180f) dAng -= 360f;
            float perp = Mathf.Abs(r * Mathf.Sin(dAng * Mathf.Deg2Rad));
            bool major = k % minorsPer == 0;
            float inner = major ? 0.76f : 0.86f;
            if (r > inner && r < 0.95f) { float l = Line(perp, major ? 0.016f : 0.008f); if (major) ink2 = Mathf.Max(ink2, l); else ink = Mathf.Max(ink, l * 0.8f); }
        }
        Color c = Color.Lerp(face, cyan, ink);
        c = Color.Lerp(c, Color.white, ink2);
        float edge = 1f - Mathf.Clamp01((r - 0.99f) / 0.01f);
        c.a = Mathf.Max(face.a, Mathf.Max(ink, ink2)) * edge;
        return c;
    }

    static Color RadarScopePix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float R = RadarScope;
        if (r > R + 0.01f) return new Color(0f, 0f, 0f, 0f);
        Color baseC = Color.Lerp(new Color(0.02f, 0.14f, 0.22f), new Color(0.0f, 0.04f, 0.08f), r / R);   // 真ん中が少し明るい
        float a = 0.93f;
        float ink = 0f;
        // 細かい格子
        float g = 0.0925f;
        float gu = Mathf.Abs(Mathf.Repeat(u + g * 0.5f, g) - g * 0.5f), gv = Mathf.Abs(Mathf.Repeat(v + g * 0.5f, g) - g * 0.5f);
        ink = Mathf.Max(ink, Line(Mathf.Min(gu, gv), 0.0015f) * 0.18f);
        // 同心円 4 本と十字・斜めの線
        for (int i = 1; i <= 4; i++) ink = Mathf.Max(ink, Line(r - R * i / 4f, 0.003f) * (i == 4 ? 0.9f : 0.5f));
        ink = Mathf.Max(ink, Line(Mathf.Min(Mathf.Abs(u), Mathf.Abs(v)), 0.002f) * 0.45f);
        ink = Mathf.Max(ink, Line(Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) * 0.7071f, 0.0015f) * 0.22f);
        Color c = Color.Lerp(baseC, new Color(0.35f, 0.95f, 1f), ink);
        c.a = a * (1f - Mathf.Clamp01((r - R) / 0.01f));
        return c;
    }

    static Color RadarSweepPix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > RadarScope) return new Color(1f, 1f, 1f, 0f);
        float ang = ClockAngle(u, v);
        // 光の先端は真上（0°）。時計回りに回るので、尾は反時計回り側（360° から 300°）
        float behind = (360f - ang) % 360f;
        float a = 0f;
        if (behind < 60f) a = Mathf.Pow(1f - behind / 60f, 2f) * 0.45f;
        if (behind < 1.2f || ang < 0.4f) a = 0.9f;
        return new Color(1f, 1f, 1f, a);
    }

    static Color RadarRosePix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float ink = 0f;
        ink = Mathf.Max(ink, Line(r - 0.765f, 0.004f) * 0.9f);
        ink = Mathf.Max(ink, Line(r - 0.885f, 0.003f) * 0.6f);
        float ang = ClockAngle(u, v);
        int k = Mathf.RoundToInt(ang / 5f);
        float dAng = ang - k * 5f;
        float perp = Mathf.Abs(r * Mathf.Sin(dAng * Mathf.Deg2Rad));
        bool ten = k % 2 == 0, thirty = k % 6 == 0;
        float outer = thirty ? 0.885f : (ten ? 0.85f : 0.82f);
        if (r > 0.77f && r < outer) ink = Mathf.Max(ink, Line(perp, thirty ? 0.006f : 0.003f) * (ten ? 1f : 0.6f));
        return new Color(1f, 1f, 1f, ink);
    }

    // 飛行機の形（上 = 機首）
    static Color PlaneIconPix(float u, float v)
    {
        float au = Mathf.Abs(u);
        bool body = au < 0.1f && v > -0.8f && v < 0.85f - au;
        bool wing = au < 0.9f && v < 0.3f - 0.4f * au && v > 0.0f - 0.32f * au;
        bool tail = au < 0.38f && v < -0.55f - 0.3f * au && v > -0.82f;
        bool nose = v >= 0.75f && v < 0.92f && au < 0.92f - v;
        return new Color(1f, 1f, 1f, (body || wing || tail || nose) ? 1f : 0f);
    }

    // 船の形（上 = 船首）
    static Color ShipIconPix(float u, float v)
    {
        float au = Mathf.Abs(u);
        bool hull = (v > -0.8f && v <= 0.3f && au < 0.34f) || (v > 0.3f && v < 0.9f && au < 0.34f * (0.9f - v) / 0.6f);
        return new Color(1f, 1f, 1f, hull ? 1f : 0f);
    }

    static Color RingIconPix(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        bool on = (r > 0.55f && r < 0.85f) || r < 0.22f;
        return new Color(1f, 1f, 1f, on ? 1f : 0f);
    }

    static Texture2D GetRadarTexture()
    {
        string path = FxDir + "/radar.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        const int N = 256;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            float a = 0f;
            float lw = 2.2f / N;
            foreach (float ring in new[] { 0.33f, 0.66f, 0.99f }) a = Mathf.Max(a, 1f - Mathf.Clamp01(Mathf.Abs(r - ring) / lw));
            if (r < 1f) a = Mathf.Max(a, 0.5f * (1f - Mathf.Clamp01(Mathf.Abs(u) / lw)), 0.5f * (1f - Mathf.Clamp01(Mathf.Abs(v) / lw)));
            if (r < 1f) a = Mathf.Max(a, 0.08f);   // うっすら円の中を塗る
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        Directory.CreateDirectory(FxDir);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null) { imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------- コックピットの内装 ----------------
    static void BuildInterior(Transform root, Material console)
    {
        var inside = new GameObject("Interior").transform;
        inside.SetParent(root, false);
        Material btnC = UnlitMat("CockpitBtnCyan", new Color(0.4f, 0.95f, 1f, 1f), false, 2000);
        Material btnW = UnlitMat("CockpitBtnWhite", new Color(0.85f, 0.95f, 1f, 1f), false, 2000);
        Material btnD = UnlitMat("CockpitBtnDim", new Color(0.12f, 0.35f, 0.4f, 1f), false, 2000);

        // 机（前のコンソール）と左右のコンソール、横の壁
        Box(inside, "Desk", new Vector3(0f, -0.62f, 0.52f), new Vector3(1.5f, 0.06f, 0.62f), Quaternion.Euler(14f, 0f, 0f), console);
        Box(inside, "DashFront", new Vector3(0f, -0.52f, 0.86f), new Vector3(1.1f, 0.34f, 0.08f), Quaternion.identity, console);
        Box(inside, "SideL", new Vector3(-0.64f, -0.44f, 0.38f), new Vector3(0.42f, 0.06f, 0.66f), Quaternion.Euler(0f, 0f, -12f), console);
        Box(inside, "SideR", new Vector3(0.64f, -0.44f, 0.38f), new Vector3(0.42f, 0.06f, 0.66f), Quaternion.Euler(0f, 0f, 12f), console);
        // 横の壁（窓の下・床まで）と床・前の下・後ろの壁で、すき間から外（海）が見えないように囲う
        Box(inside, "WallL", new Vector3(-0.88f, -0.67f, 0.17f), new Vector3(0.04f, 1.08f, 1.6f), Quaternion.identity, console);
        Box(inside, "WallR", new Vector3(0.88f, -0.67f, 0.17f), new Vector3(0.04f, 1.08f, 1.6f), Quaternion.identity, console);
        Box(inside, "Floor", new Vector3(0f, -1.22f, 0.17f), new Vector3(1.8f, 0.04f, 1.6f), Quaternion.identity, console);
        Box(inside, "FrontLower", new Vector3(0f, -0.88f, 0.95f), new Vector3(1.8f, 0.7f, 0.04f), Quaternion.identity, console);
        Box(inside, "RearWall", new Vector3(0f, -0.33f, -0.62f), new Vector3(1.8f, 1.8f, 0.04f), Quaternion.identity, console);
        // 前の角（横のコンソールと正面のパネルの間・窓枠の下）のすき間をふさぐ
        Box(inside, "CornerL", new Vector3(-0.70f, -0.42f, 0.80f), new Vector3(0.38f, 0.42f, 0.34f), Quaternion.Euler(0f, 35f, 0f), console);
        Box(inside, "CornerR", new Vector3(0.70f, -0.42f, 0.80f), new Vector3(0.38f, 0.42f, 0.34f), Quaternion.Euler(0f, -35f, 0f), console);

        // 窓枠（キャノピー）
        float t = 0.035f;
        Rod(inside, "PillarL", new Vector3(-0.52f, -0.27f, 0.88f), new Vector3(-0.34f, 0.5f, 0.62f), t, console);
        Rod(inside, "PillarR", new Vector3(0.52f, -0.27f, 0.88f), new Vector3(0.34f, 0.5f, 0.62f), t, console);
        Rod(inside, "TopBar", new Vector3(-0.34f, 0.5f, 0.62f), new Vector3(0.34f, 0.5f, 0.62f), t, console);
        Rod(inside, "RoofL", new Vector3(-0.34f, 0.5f, 0.62f), new Vector3(-0.42f, 0.55f, -0.2f), t, console);
        Rod(inside, "RoofR", new Vector3(0.34f, 0.5f, 0.62f), new Vector3(0.42f, 0.55f, -0.2f), t, console);
        Rod(inside, "SillL", new Vector3(-0.52f, -0.27f, 0.88f), new Vector3(-0.86f, -0.14f, -0.1f), t, console);
        Rod(inside, "SillR", new Vector3(0.52f, -0.27f, 0.88f), new Vector3(0.86f, -0.14f, -0.1f), t, console);
        Rod(inside, "SideFrameL", new Vector3(-0.86f, -0.14f, 0.2f), new Vector3(-0.42f, 0.55f, 0.15f), t, console);
        Rod(inside, "SideFrameR", new Vector3(0.86f, -0.14f, 0.2f), new Vector3(0.42f, 0.55f, 0.15f), t, console);

        // ボタン（左右のコンソールの上に光るボタンを並べる）
        var rnd = new System.Random(7);
        for (int side = -1; side <= 1; side += 2)
        for (int gx = 0; gx < 3; gx++)
        for (int gz = 0; gz < 5; gz++)
        {
            Material m = rnd.Next(5) == 0 ? btnW : (rnd.Next(3) == 0 ? btnD : btnC);
            float x = side * (0.52f + gx * 0.07f);
            float y = -0.405f - gx * 0.015f * 1f;
            float z = 0.18f + gz * 0.09f;
            var b = Quad(inside, "Button", new Vector3(x, y, z), new Vector2(0.04f, 0.04f), m);
            b.transform.localRotation = Quaternion.Euler(90f, 0f, side * 12f);
        }
        // 正面のボタン列（モニターの下）
        for (int i = 0; i < 10; i++)
        {
            Material m = i % 4 == 0 ? btnW : (i % 3 == 0 ? btnD : btnC);
            var b = Quad(inside, "FrontButton", new Vector3(-0.27f + i * 0.06f, -0.375f, 0.815f), new Vector2(0.035f, 0.022f), m);
            b.transform.localRotation = Quaternion.LookRotation(new Vector3(-0.27f + i * 0.06f, -0.375f, 0.815f).normalized, Vector3.up);
        }

        // 操縦桿
        Box(inside, "StickBase", new Vector3(0f, -0.6f, 0.36f), new Vector3(0.1f, 0.03f, 0.1f), Quaternion.identity, console);
        Rod(inside, "Stick", new Vector3(0f, -0.6f, 0.36f), new Vector3(0f, -0.42f, 0.38f), 0.03f, console);
        var knob = Box(inside, "StickTop", new Vector3(0f, -0.41f, 0.38f), new Vector3(0.05f, 0.07f, 0.05f), Quaternion.identity, console);
        var light = Quad(inside, "StickLight", new Vector3(0f, -0.385f, 0.38f), new Vector2(0.02f, 0.02f), btnC);
        light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    static GameObject Box(Transform parent, string name, Vector3 lp, Vector3 size, Quaternion rot, Material m)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(b.GetComponent<Collider>());
        b.name = name;
        b.transform.SetParent(parent, false);
        b.transform.localPosition = lp;
        b.transform.localRotation = rot;
        b.transform.localScale = size;
        var r = b.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return b;
    }

    static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float thick, Material m)
    {
        Vector3 d = b - a;
        Vector3 upHint = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        return Box(parent, name, (a + b) * 0.5f, new Vector3(thick, thick, d.magnitude), Quaternion.LookRotation(d.normalized, upHint), m);
    }

    static Transform Panel(Transform root, string name, Vector3 pos, float w, float h, Material bg, Material frame)
    {
        var p = new GameObject(name).transform;
        p.SetParent(root, false);
        p.localPosition = pos;
        p.localRotation = Quaternion.LookRotation(pos.normalized, Vector3.up);   // 目の方を向ける
        Quad(p, "BG", Vector3.zero, new Vector2(w, h), bg);
        if (s_consoleMat != null)
        {
            var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(housing.GetComponent<Collider>());
            housing.name = "Housing";
            housing.transform.SetParent(p, false);
            housing.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            housing.transform.localScale = new Vector3(w + 0.04f, h + 0.04f, 0.05f);
            var hr = housing.GetComponent<MeshRenderer>();
            hr.sharedMaterial = s_consoleMat;
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        float L = Mathf.Min(w, h) * 0.22f;
        float t = 0.005f;
        float z = -0.001f;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sy = -1; sy <= 1; sy += 2)
        {
            Quad(p, "Corner", new Vector3(sx * (w / 2f - L / 2f), sy * (h / 2f), z), new Vector2(L, t), frame);
            Quad(p, "Corner", new Vector3(sx * (w / 2f), sy * (h / 2f - L / 2f), z), new Vector2(t, L), frame);
        }
        Quad(p, "TopEdge", new Vector3(0f, h / 2f, z), new Vector2(w * 0.45f, 0.002f), frame);
        Quad(p, "BottomEdge", new Vector3(0f, -h / 2f, z), new Vector2(w * 0.3f, 0.002f), frame);
        if (s_staticMat != null)
        {
            var st = Quad(p, "Static", new Vector3(0f, 0f, -0.009f), new Vector2(w, h), s_staticMat);
            st.SetActive(false);
        }
        return p;
    }

    static GameObject Quad(Transform parent, string name, Vector3 lp, Vector2 size, Material m)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(parent, false);
        q.transform.localPosition = lp;
        q.transform.localRotation = Quaternion.identity;
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return q;
    }

    static TextMeshPro Txt(Transform p, string name, string text, Vector2 center, float width, float size, Color col, TextAlignmentOptions align, float height = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.localPosition = new Vector3(center.x, center.y, -0.004f);
        go.transform.localRotation = Quaternion.identity;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = col;
        tmp.alignment = align;
        tmp.enableWordWrapping = false;
        tmp.richText = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.rectTransform.sizeDelta = new Vector2(width, height > 0f ? height : size * 0.13f);
        return tmp;
    }

    static Transform Bar(Transform p, string name, Vector2 center, Vector2 size, bool vertical, Material bgM, Material fillM)
    {
        Quad(p, name + "BG", new Vector3(center.x, center.y, -0.001f), size, bgM);
        var pivot = new GameObject(name).transform;
        pivot.SetParent(p, false);
        pivot.localPosition = vertical
            ? new Vector3(center.x, center.y - size.y / 2f, -0.002f)
            : new Vector3(center.x - size.x / 2f, center.y, -0.002f);
        Quad(pivot, "Fill", vertical ? new Vector3(0f, size.y / 2f, 0f) : new Vector3(size.x / 2f, 0f, 0f), size, fillM);
        return pivot;
    }

    static Material UnlitMat(string name, Color col, bool transparent, int queue)
    {
        string path = FxDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(FxDir);
        Shader sh = Shader.Find("FlightUI/UnlitTint");
        m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        if (m.HasProperty("_Color")) m.SetColor("_Color", col);
        if (transparent)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        m.renderQueue = queue;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static bool SetupGun(controller c, Material fxMat, AudioClip clip, ParticleSystem impact, StringBuilder log)
    {
        if (c.car == null) return false;
        Transform car = c.car.transform;
        Transform gun = car.Find("Gun");
        if (gun == null)
        {
            Vector3 fwd = c.axisReference != null ? c.axisReference.forward : -car.right;
            Vector3 up = c.axisReference != null ? c.axisReference.up : car.up;
            float r = BodyRadius(c.car);

            var go = new GameObject("Gun");
            Undo.RegisterCreatedObjectUndo(go, "Flight setup");
            go.transform.SetParent(car, false);
            go.transform.SetPositionAndRotation(car.position + fwd * (r + 0.8f), Quaternion.LookRotation(fwd, up));
            SetWorldScaleOne(go.transform);
            gun = go.transform;

            // 曳光弾
            var tr = MakePS("Tracer", gun, fxMat);
            var main = tr.main;
            main.startLifetime = 1.6f;
            main.startSpeed = 500f;
            main.startSize = 0.35f;
            main.startColor = new Color(1f, 0.75f, 0.3f, 1f);
            main.maxParticles = 300;
            var shape = tr.shape;
            shape.enabled = false;
            var iv = tr.inheritVelocity;
            iv.enabled = true;
            iv.mode = ParticleSystemInheritVelocityMode.Initial;
            iv.curve = new ParticleSystem.MinMaxCurve(1f);
            var rend = tr.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.025f;
            rend.lengthScale = 2f;

            // 発射音
            var au = go.AddComponent<AudioSource>();
            au.playOnAwake = false;
            au.clip = clip;
            au.spatialBlend = 1f;
            au.minDistance = 10f;
            au.maxDistance = 900f;
            au.volume = 0.7f;
            au.dopplerLevel = 0f;

            // 照準（60m 先の +）
            var sight = new GameObject("GunSight");
            Undo.RegisterCreatedObjectUndo(sight, "Flight setup");
            sight.transform.SetParent(gun, false);
            sight.transform.localPosition = new Vector3(0f, 0f, 60f);
            sight.transform.localRotation = Quaternion.identity;
            var tmp = sight.AddComponent<TextMeshPro>();
            tmp.text = "+";
            tmp.fontSize = 40f;
            tmp.color = new Color(0.3f, 1f, 0.45f, 0.9f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(10f, 10f);
            sight.SetActive(false);

            log.AppendLine("  機銃取付: " + GetPath(car));
        }

        Undo.RecordObject(c, "Flight setup");
        c.gunMuzzle = gun;
        Transform trT = gun.Find("Tracer");
        c.tracerFx = trT != null ? trT.GetComponent<ParticleSystem>() : null;
        c.gunAudio = gun.GetComponent<AudioSource>();
        Transform sT = gun.Find("GunSight");
        c.gunSight = sT != null ? sT.gameObject : null;
        c.impactFx = impact;
        UdonSharpEditorUtility.CopyProxyToUdon(c);
        PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
        if (ub != null) PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
        return true;
    }

    static ParticleSystem EnsureImpact(Material fxMat, StringBuilder log)
    {
        GameObject existing = GameObject.Find("GunImpact");
        if (existing != null && existing.GetComponent<ParticleSystem>() != null) return existing.GetComponent<ParticleSystem>();

        var ps = MakePS("GunImpact", null, fxMat);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
        main.startColor = new Color(1f, 0.6f, 0.2f, 1f);
        main.gravityModifier = 1f;
        main.maxParticles = 400;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.1f;
        SetFade(ps, new Color(1f, 0.8f, 0.4f), new Color(0.8f, 0.3f, 0.1f));
        log.AppendLine("着弾エフェクト作成");
        return ps;
    }

    // 海面に弾が当たった時の水柱（白い水しぶきが上に吹き上がって落ちる）
    static ParticleSystem EnsureWaterSplash(Texture2D tex, StringBuilder log)
    {
        GameObject old = GameObject.Find("WaterSplash");
        if (old != null) Undo.DestroyObjectImmediate(old);

        Shader sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        Material m = LoadOrCreateMat("WaterFx", sh);
        m.SetTexture("_MainTex", tex);
        m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);

        var ps = MakePS("WaterSplash", null, m);
        ps.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // 上向き
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 18f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.92f, 1f, 0.85f), new Color(1f, 1f, 1f, 0.95f));
        main.gravityModifier = 1.3f;
        main.maxParticles = 600;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.3f;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.5f));
        SetFade(ps, Color.white, new Color(0.8f, 0.88f, 0.95f));
        log.AppendLine("水柱エフェクト作成");
        return ps;
    }

    static void CreateTargets(Material fxMat, AudioClip boom, StringBuilder log)
    {
        var cat = Object.FindObjectOfType<Catapult>(true);
        Vector3 start = cat != null ? cat.transform.position : new Vector3(254f, -7f, 297f);
        Vector3 dir = cat != null ? cat.transform.forward : Vector3.left;
        dir.y = 0f;
        dir.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        Material balloonMat = GetBalloonMaterial();
        int mask = ~((1 << 9) | (1 << 10) | (1 << 17) | (1 << 18));

        var root = new GameObject("FlightTargets");
        Undo.RegisterCreatedObjectUndo(root, "Flight setup");
        Physics.SyncTransforms();

        var sb = new StringBuilder();
        for (int i = 0; i < 12; i++)
        {
            float dist = 300f + i * 130f;
            float lat = (i % 2 == 0 ? 1f : -1f) * (40f + (i % 3) * 45f);
            float alt = 40f + (i % 4) * 35f;
            Vector3 p = start + dir * dist + side * lat;
            p.y = alt;
            // 地形や建物があれば、その 40m 上に置く
            RaycastHit gh;
            if (Physics.Raycast(new Vector3(p.x, 400f, p.z), Vector3.down, out gh, 800f, mask, QueryTriggerInteraction.Ignore))
                p.y = Mathf.Max(p.y, gh.point.y + 40f);
            CreateBalloon(root.transform, "Target_" + (i + 1), p, balloonMat, fxMat, boom);
            sb.Append(" " + p.ToString("F0"));
        }
        log.AppendLine("標的を12個設置:" + sb);
    }

    static void CreateBalloon(Transform parent, string name, Vector3 pos, Material balloonMat, Material fxMat, AudioClip boom)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;

        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = Vector3.one * 8f;
        body.GetComponent<Renderer>().sharedMaterial = balloonMat;

        var basket = GameObject.CreatePrimitive(PrimitiveType.Cube);
        basket.name = "Basket";
        Object.DestroyImmediate(basket.GetComponent<Collider>());
        basket.transform.SetParent(body.transform, false);
        basket.transform.localPosition = new Vector3(0f, -0.65f, 0f);
        basket.transform.localScale = new Vector3(0.12f, 0.08f, 0.12f);

        var au = go.AddComponent<AudioSource>();
        au.playOnAwake = false;
        au.clip = boom;
        au.spatialBlend = 1f;
        au.minDistance = 20f;
        au.maxDistance = 1500f;
        au.volume = 1f;

        var ex = MakePS("Explosion", go.transform, fxMat);
        var main = ex.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 22f);
        main.startSize = new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.1f), new Color(1f, 0.85f, 0.3f));
        main.gravityModifier = -0.1f;
        main.maxParticles = 200;
        var em = ex.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });
        var shape = ex.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 2f;
        SetFade(ex, new Color(1f, 0.9f, 0.5f), new Color(0.6f, 0.15f, 0.05f));
        var sol = ex.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));

        var t = UdonSharpEditor.UdonSharpUndo.AddComponent<FlightTarget>(go);
        t.body = body;
        t.explosionFx = ex;
        t.explosionAudio = au;
        t.maxHp = 5;
        t.respawnTime = 20f;
        UdonSharpEditorUtility.CopyProxyToUdon(t);
    }

    static ParticleSystem MakePS(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        return ps;
    }

    static void SetFade(ParticleSystem ps, Color from, Color to)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    static float BodyRadius(GameObject car)
    {
        Transform ct = car.transform;
        Vector3 ls = ct.lossyScale;
        float s = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        var sph = car.GetComponent<SphereCollider>();
        if (sph != null) return sph.radius * s + ct.TransformVector(sph.center).magnitude;
        var bx = car.GetComponent<BoxCollider>();
        if (bx != null) return Mathf.Max(bx.size.x, bx.size.y, bx.size.z) * 0.5f * s;
        return 1.5f;
    }

    static void SetWorldScaleOne(Transform t)
    {
        if (t.parent == null) { t.localScale = Vector3.one; return; }
        Vector3 ls = t.parent.lossyScale;
        t.localScale = new Vector3(1f / Mathf.Max(Mathf.Abs(ls.x), 1e-4f), 1f / Mathf.Max(Mathf.Abs(ls.y), 1e-4f), 1f / Mathf.Max(Mathf.Abs(ls.z), 1e-4f));
    }

    static Texture2D GetSoftTexture()
    {
        string path = FxDir + "/soft.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(FxDir);
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float dx = (x + 0.5f) / 32f - 1f;
            float dy = (y + 0.5f) / 32f - 1f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            a = a * a * (3f - 2f * a);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material GetFxMaterial(Texture2D tex)
    {
        string path = FxDir + "/FlightFx.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(FxDir);
        // VRChat は Built-in なので URP のシェーダーは使わない
        Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
        var m = new Material(sh);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        // 加算合成（光る弾・炎）
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        else m.mainTexture = tex;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material GetBalloonMaterial()
    {
        string path = FxDir + "/Balloon.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(FxDir);
        Shader sh = Shader.Find("Standard");
        var m = new Material(sh);
        Color red = new Color(0.85f, 0.1f, 0.08f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", red);
        if (m.HasProperty("_Color")) m.SetColor("_Color", red);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
    const string CatapultScriptPath = "Assets/My script/drone/Catapult.cs";
    const string CatapultAssetPath = "Assets/My script/drone/Catapult.asset";

    // true = すでにプログラムがあって使える / false = 今作った（次回設置）
    static bool EnsureCatapultProgram(StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(CatapultAssetPath) != null) return true;
        var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(CatapultScriptPath);
        if (ms == null) { log.AppendLine("ERROR: Catapult.cs が見つからない"); return true; }
        var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        prog.sourceCsScript = ms;
        AssetDatabase.CreateAsset(prog, CatapultAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        log.AppendLine("Catapult.asset 作成・コンパイル");
        return false;
    }

    static void CreateCatapult(StringBuilder log)
    {
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        if (putOut == null || putOut.reportPoint == null) { log.AppendLine("ERROR: 機体の出現地点(PutOutDrone.reportPoint)が見つからない"); return; }
        GameObject sub = GameObject.Find("伊402_完全版");
        if (sub == null) { log.AppendLine("ERROR: 伊402_完全版 が見つからない"); return; }

        Vector3 start = putOut.reportPoint.transform.position;
        Bounds bd = new Bounds(sub.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in sub.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { bd = r.bounds; any = true; } else bd.Encapsulate(r.bounds);
        }

        // 艦の長い方向に沿って、出現地点に近い側の艦首（端）へ向けて射出
        Vector3 dir;
        float edgeDist;
        if (bd.size.x >= bd.size.z)
        {
            float sgn = start.x < bd.center.x ? -1f : 1f;
            dir = new Vector3(sgn, 0f, 0f);
            edgeDist = sgn < 0 ? start.x - bd.min.x : bd.max.x - start.x;
        }
        else
        {
            float sgn = start.z < bd.center.z ? -1f : 1f;
            dir = new Vector3(0f, 0f, sgn);
            edgeDist = sgn < 0 ? start.z - bd.min.z : bd.max.z - start.z;
        }
        float stroke = Mathf.Clamp(edgeDist - 1f, 8f, 40f);

        var go = new GameObject("Catapult");
        Undo.RegisterCreatedObjectUndo(go, "Flight setup");
        go.transform.SetPositionAndRotation(start, Quaternion.LookRotation(dir, Vector3.up));
        go.layer = 17; // Walkthrough: プレイヤーには当たらず、機体には反応する
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(5f, 4f, 5f);

        var cat = UdonSharpEditor.UdonSharpUndo.AddComponent<Catapult>(go);
        cat.strokeLength = stroke;
        cat.launchSpeed = 40f;
        cat.holdTime = 2.5f;   // 「Catapult engaged. Launching.」を言い終わってから射出
        UdonSharpEditorUtility.CopyProxyToUdon(cat);
        PlaceCatapult(cat, log);
        return;

        // 見た目のレール（当たり判定なし）
        float deckY = start.y - 1f;
        foreach (var h in Physics.RaycastAll(start + Vector3.up * 3f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(sub.transform)) { deckY = Mathf.Max(h.point.y, -1000f); break; }
        }
        var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rail.name = "CatapultRail";
        Object.DestroyImmediate(rail.GetComponent<Collider>());
        rail.transform.SetParent(go.transform, true);
        rail.transform.position = new Vector3(start.x, deckY + 0.08f, start.z) + dir * (stroke * 0.5f);
        rail.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        rail.transform.localScale = new Vector3(0.5f, 0.15f, stroke);

        log.AppendLine("カタパルト設置: 位置 " + start + " 方向 " + dir + " 長さ " + stroke.ToString("F1") + "m 艦の大きさ " + bd.size + " 甲板高さ " + deckY.ToString("F2"));
    }

    // カタパルトを甲板の高さ（機体が甲板に乗った時の中心の高さ）に置き、
    // 検知範囲を出現地点の高さまで広げる。レールも甲板に合わせる
    static void PlaceCatapult(Catapult cat, StringBuilder log)
    {
        GameObject sub = GameObject.Find("伊402_完全版");
        var putOut = Object.FindObjectOfType<PutOutDrone>(true);
        Transform t = cat.transform;
        Vector3 spawn = (putOut != null && putOut.reportPoint != null) ? putOut.reportPoint.transform.position : t.position;
        Vector3 dir = t.forward; dir.y = 0f; dir.Normalize();

        // 甲板の高さ
        float deckY = spawn.y - 1f;
        bool hitDeck = false;
        foreach (var h in Physics.RaycastAll(new Vector3(spawn.x, spawn.y + 3f, spawn.z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (sub != null && h.transform.IsChildOf(sub.transform) && h.point.y <= spawn.y + 0.1f && (!hitDeck || h.point.y > deckY)) { deckY = h.point.y; hitDeck = true; }
        }

        // 機体が甲板に乗った時の中心の高さ（機体コライダーの下端から中心まで）
        // 非表示の機体だと bounds が取れないので、コライダーの形から計算する
        // 格納庫から出てくる機体（プールの中）で一番背の高いものに合わせる
        float restH = 1.0f;
        var poolObjs = (putOut != null && putOut.dronePool != null) ? putOut.dronePool.Pool : null;
        foreach (var cc in Object.FindObjectsOfType<controller>(true))
        {
            if (cc.car == null) continue;
            if (poolObjs != null && System.Array.IndexOf(poolObjs, cc.car) < 0) continue;
            Transform ct = cc.car.transform;
            float h = -1f;
            var sph = cc.car.GetComponent<SphereCollider>();
            var bx = cc.car.GetComponent<BoxCollider>();
            if (sph != null)
            {
                Vector3 ls = ct.lossyScale;
                float r = sph.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                h = r - ct.TransformVector(sph.center).y;
            }
            else if (bx != null)
            {
                float hy = Mathf.Abs(ct.TransformVector(new Vector3(0f, bx.size.y * 0.5f, 0f)).y);
                h = hy - ct.TransformVector(bx.center).y;
            }
            if (h > restH) restH = h;
        }

        // 秒読みを 2.5 秒に（音声「Catapult engaged. Launching.」を言い終わってから射出）
        if (cat.holdTime < 2.5f)
        {
            Undo.RecordObject(cat, "Flight setup");
            cat.holdTime = 2.5f;
            UdonSharpEditorUtility.CopyProxyToUdon(cat);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cat);
        }

        // 射出速度を 40 → 45 m/s に（手で変えていたら触らない）
        if (Mathf.Approximately(cat.launchSpeed, 40f))
        {
            Undo.RecordObject(cat, "Flight setup");
            cat.launchSpeed = 45f;
            UdonSharpEditorUtility.CopyProxyToUdon(cat);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cat);
        }

        Undo.RecordObject(t, "Flight setup");
        t.position = new Vector3(spawn.x, deckY + restH + 0.05f, spawn.z);
        t.rotation = Quaternion.LookRotation(dir, Vector3.up);

        // 検知範囲: 甲板付近〜出現地点の少し上まで
        var box = cat.GetComponent<BoxCollider>();
        if (box != null)
        {
            Undo.RecordObject(box, "Flight setup");
            float bottom = -restH - 0.5f;
            float top = Mathf.Max(spawn.y - t.position.y, 0f) + 2f;
            box.isTrigger = true;
            box.size = new Vector3(6f, top - bottom, 6f);
            box.center = new Vector3(0f, (top + bottom) * 0.5f, 0f);
        }

        var rail = t.Find("CatapultRail");
        if (rail != null)
        {
            Undo.RecordObject(rail, "Flight setup");
            rail.position = new Vector3(t.position.x, deckY + 0.08f, t.position.z) + dir * (cat.strokeLength * 0.5f);
            rail.rotation = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 ps = t.lossyScale;
            rail.localScale = new Vector3(0.5f / ps.x, 0.15f / ps.y, cat.strokeLength / ps.z);
        }
        if (putOut != null)
        {
            Undo.RecordObject(putOut, "Flight setup");
            putOut.catapultPoint = t;
            putOut.blockRadius = 30f;
            UdonSharpEditorUtility.CopyProxyToUdon(putOut);
        }
        log.AppendLine("カタパルト配置: 開始点 " + t.position.ToString("F2") + " 甲板 " + deckY.ToString("F2") + (hitDeck ? "" : "(推定)") + " 機体の高さ " + restH.ToString("F2") + " 出現地点 " + spawn.ToString("F2") + " 検知範囲 " + (box != null ? box.size.ToString("F1") : "-") + " 長さ " + cat.strokeLength.ToString("F1") + "m");
    }

    static string GetPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
