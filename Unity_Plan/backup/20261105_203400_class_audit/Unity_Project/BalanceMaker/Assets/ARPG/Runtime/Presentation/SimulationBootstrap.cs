using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ARPG.Core;
using ARPG.Core.Batch;
using ARPG.Core.Events;
using ARPG.Core.Simulation;
using ARPG.Core.Tests;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ARPG.Runtime
{
    public enum SimMode
    {
        Observe = 0,
        Batch = 1,
    }

    /// <summary>
    /// 타격/가드/회피/빗나감 이벤트 → 공격 연출(막대·착탄)과 화면 점멸. 화면 효과만 담당하며 판정에 관여하지 않는다.
    /// 공격 연출이 켜져 있으면 투사체가 닿는 순간 점멸하고, 꺼져 있으면 이벤트 즉시 점멸한다.
    /// 투사체 모양은 이벤트의 SkillId 로 스킬을 찾아 첫 Damage_Type 으로 정한다(AttackLooks).
    /// </summary>
    public sealed class ViewEventSink : IBattleEventSink
    {
        public Dictionary<int, CombatantView> Views;
        public AttackFx Fx;
        public DataCatalog Catalog;

        public void OnBattleStarted(BattleStartInfo info) { }

        private AttackLook LookOf(in BattleEvent e)
        {
            SkillDefinition s = null;
            if (Catalog != null && e.SkillId != null) Catalog.Skills.TryGetValue(e.SkillId, out s);
            return AttackLooks.For(s);
        }

        public void OnEvent(in BattleEvent e)
        {
            if (Views == null) return;
            CombatantView v, atk;
            switch (e.Type)
            {
                case BattleEventType.HitResolved:
                    Views.TryGetValue(e.ActorId, out atk);
                    Views.TryGetValue(e.TargetId, out v);
                    if (e.Result == "MISS")
                    {
                        if (Fx != null) Fx.Spawn(AttackFxKind.Miss, LookOf(in e), atk, v, e.Tick, e.DistanceM);
                        break;
                    }
                    if (v == null) break;
                    if (Fx == null || !Fx.Spawn(AttackFxKind.Hit, LookOf(in e), atk, v, e.Tick, e.DistanceM)) v.FlashHit();
                    break;
                case BattleEventType.Guarded:
                    Views.TryGetValue(e.ActorId, out atk);
                    if (!Views.TryGetValue(e.TargetId, out v) || v == null) break;
                    if (Fx == null || !Fx.Spawn(AttackFxKind.Guard, LookOf(in e), atk, v, e.Tick, e.DistanceM)) v.FlashGuard();
                    break;
                case BattleEventType.HitAvoided:
                    Views.TryGetValue(e.ActorId, out atk);
                    Views.TryGetValue(e.TargetId, out v);
                    if (Fx != null && v != null) Fx.Spawn(AttackFxKind.Avoided, LookOf(in e), atk, v, e.Tick, e.DistanceM);
                    break;
                case BattleEventType.StatusApplied:
                case BattleEventType.StatusTick:
                    if (Views.TryGetValue(e.ActorId, out v) && v != null)
                    {
                        v.FlashHit();
                        // 상처는 즉시 피해라 지속 상태가 없으므로 머리 위 '상처' 글자를 전투 시간 1초(30 tick) 동안만 보여 준다
                        if (e.Type == BattleEventType.StatusApplied && e.StatusId == "WOUND") v.WoundTagUntilTick = e.Tick + 30;
                    }
                    break;
            }
        }

        public void OnBattleEnded(BattleResult result) { }
    }

    /// <summary>
    /// 시뮬레이터 장면 루트: 데이터 로드, 시나리오 편집, 관찰/고속 반복 실행, 로그·CSV 저장, 화면 구성.
    /// 전투 상태의 소유자는 Core 의 BattleSimulation 하나이며 이 클래스는 그 상태를 표시만 한다.
    /// </summary>
    public sealed partial class SimulationBootstrap : MonoBehaviour
    {
        public static SimulationBootstrap Instance { get; private set; }
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static readonly double[] SpeedOptions = { 0.5, 1, 2, 4, 8 };

        // 데이터
        private LoadedData _data;
        private List<string> _scenarioFiles = new List<string>();
        private int _scenarioIndex = -1;
        private BattleScenario _scenario = new BattleScenario();
        private string _status = "";

        // 관찰 모드
        private readonly VisualSimulationRunner _runner = new VisualSimulationRunner();
        private BattleSimulation _sim;
        private readonly Dictionary<int, CombatantView> _views = new Dictionary<int, CombatantView>();
        private Transform _viewRoot;
        private readonly UnityConsoleSink _console = new UnityConsoleSink();
        private readonly ViewEventSink _viewSink = new ViewEventSink();
        private CsvEventSink _observeCsv;
        private bool _observeCsvEnabled = true;
        private int _observeBattleIndex;
        private string _observeRunId;
        private string _observeDir;
        private string _lastSavedPath;
        private bool _observeResultSaved;
        private int _speedIndex = 1;
        private int _selectedId = -1;

        // 관찰 모드 자동 다음 회차
        private const float AutoNextDelaySec = 1.2f; // 결과를 볼 수 있게 잠시 멈춤(실제 시간, 배속이 높으면 짧아짐)
        private bool _autoNext;
        private float _autoNextWait;
        private int _autoDone, _autoWins0, _autoWins1, _autoDraws;

        // 고속 반복
        private SimMode _mode = SimMode.Observe;
        private BatchCoordinator _batch;
        private ResultsCsvWriter _batchResults;
        private CsvEventSink _batchEvents;
        private string _batchDir;
        private int _batchEventMode = 1;     // 0 없음, 1 처음 N회, 2 전체
        private int _batchEventFirstN = 3;
        private float _batchBudgetMs = 14f;
        private string _batchReport = "";
        private bool _batchSaved;

        // 화면
        private Camera _cam;
        private CameraRig _rig;
        private DebugDraw _draw;
        private AttackFx _fx;
        private bool _showZones = true;
        private bool _showAttackFx = true;
        private List<SelfTestResult> _tests;
        private Font _uiFont;

        // ------------------------------------------------------------------ 수명주기
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _viewSink.Views = _views;
            BuildEnvironment();
            ReloadData();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_skin != null) Destroy(_skin);
            FinalizeObserveCsv();
            if (_batch != null && !_batch.Finished) _batch.Cancel();
            FinishBatchFiles(true);
        }

        private void OnApplicationQuit()
        {
            FinalizeObserveCsv();
            if (_batch != null && !_batch.Finished) _batch.Cancel();
            FinishBatchFiles(true);
        }

        private void FinalizeObserveCsv()
        {
            if (_observeCsv == null) return;
            if (_sim != null && _sim.Tick == 0)
            {
                _observeCsv.Discard();
                _observeCsv = null;
            }
            else CloseObserveCsv(true);
        }

        private void BuildEnvironment()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                _cam = camGo.AddComponent<Camera>();
            }
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
            _rig = _cam.GetComponent<CameraRig>();
            if (_rig == null) _rig = _cam.gameObject.AddComponent<CameraRig>();

            if (FindAnyObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            }

            var arena = new GameObject("ArenaRoot").transform;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(arena, false);
            ground.transform.localScale = new Vector3(6, 1, 6);
            var gcol = ground.GetComponent<Collider>();
            if (gcol != null) Destroy(gcol);
            CombatantView.SetColor(ground.GetComponent<Renderer>(), new Color(0.22f, 0.23f, 0.25f));

            _viewRoot = new GameObject("CombatantViewRoot").transform;
            var drawRoot = new GameObject("DebugDraw").transform;
            _draw = new DebugDraw(drawRoot);
            _fx = new AttackFx(new GameObject("AttackFxRoot").transform) { Enabled = _showAttackFx };
            _viewSink.Fx = _fx;

            try
            {
                _uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 14);
            }
            catch (Exception)
            {
                _uiFont = null;
            }
        }

        // ------------------------------------------------------------------ 데이터/시나리오
        public void ReloadData()
        {
            StopObserve();
            _data = DataLoader.Load();
            _viewSink.Catalog = _data.Catalog;
            _scenarioFiles = ScenarioStore.ListFiles();
            if (_data.Catalog == null)
            {
                _status = "데이터 로드 실패: " + string.Join(" / ", _data.Errors.ToArray());
                Debug.LogError("[ARPG] " + _status);
                return;
            }
            _status = "데이터 " + _data.Catalog.CharacterList.Count + "명 / 스킬 " + _data.Catalog.Skills.Count + " / dataHash " + _data.Catalog.DataHash
                      + " / 규칙 " + _data.Rules.rulesVersion + "(" + _data.RulesSource + ")";
            if (_data.Errors.Count > 0) _status += " | 경고: " + string.Join(" / ", _data.Errors.ToArray());
            Debug.Log("[ARPG] " + _status);
            if (_scenarioFiles.Count > 0) LoadScenario(_scenarioIndex >= 0 && _scenarioIndex < _scenarioFiles.Count ? _scenarioIndex : 0);
            else
            {
                _scenario = DefaultScenario();
                SyncScenarioTexts();
            }
            PrepareObserve(0);
        }

        private BattleScenario DefaultScenario()
        {
            var sc = new BattleScenario { name = "기본_1대1" };
            if (_data != null && _data.Catalog != null)
            {
                CharacterDefinition pc = null, mon = null;
                foreach (var c in _data.Catalog.CharacterList)
                {
                    if (!c.Enabled) continue;
                    if (!c.IsMonster && pc == null) pc = c;
                    if (c.IsMonster && mon == null) mon = c;
                }
                if (pc != null) sc.GetOrAddTeam(0).members.Add(new ScenarioMember { definitionId = pc.Id, count = 1 });
                if (mon != null) sc.GetOrAddTeam(1).members.Add(new ScenarioMember { definitionId = mon.Id, count = 1 });
            }
            return sc;
        }

        private void LoadScenario(int index)
        {
            if (index < 0 || index >= _scenarioFiles.Count) return;
            string err;
            var sc = ScenarioStore.Load(_scenarioFiles[index], out err);
            if (sc == null)
            {
                _status = "시나리오 읽기 실패: " + err;
                return;
            }
            _scenarioIndex = index;
            _scenario = sc;
            _observeRunId = null;
            ResetAutoTally();
            _scenario.GetOrAddTeam(0);
            _scenario.GetOrAddTeam(1);
            SyncScenarioTexts();
        }

        private void SaveScenario()
        {
            string err;
            string path = ScenarioStore.Save(_scenario, out err);
            if (path == null) { _status = "시나리오 저장 실패: " + err; return; }
            _scenarioFiles = ScenarioStore.ListFiles();
            _scenarioIndex = _scenarioFiles.IndexOf(path);
            _status = "시나리오 저장: " + path;
        }

        private List<string> ValidateScenario()
        {
            if (_data == null || _data.Catalog == null) return new List<string> { "데이터 없음" };
            var errs = _scenario.Validate(_data.Catalog);
            if (_data.Catalog.BlockingIssueCount > 0) errs.Add("데이터 필수 검증 오류 " + _data.Catalog.BlockingIssueCount + "건");
            return errs;
        }

        // ------------------------------------------------------------------ 관찰 모드
        private static string NewRunId(string scenarioName, string mode)
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv) + "_" + mode + "_" + ScenarioStore.MakeSafeFileName(scenarioName ?? "scenario");
        }

        public void PrepareObserve(int battleIndex)
        {
            PrepareObserve(battleIndex, false);
        }

        /// <summary>keepCamera=true: 자동 다음 회차처럼 카메라 위치·추적 대상(같은 EntityId)을 유지한다.</summary>
        public void PrepareObserve(int battleIndex, bool keepCamera)
        {
            int followId = -1;
            if (keepCamera && _rig != null && _rig.Follow != null)
            {
                var fv = _rig.Follow.GetComponent<CombatantView>();
                if (fv != null) followId = fv.EntityId;
            }
            StopObserve();
            _autoNextWait = 0f;
            var errs = ValidateScenario();
            if (errs.Count > 0)
            {
                _status = "시나리오 오류: " + string.Join(" / ", errs.ToArray());
                _autoNext = false;
                return;
            }
            _observeBattleIndex = Math.Max(0, battleIndex);
            if (_observeRunId == null) _observeRunId = NewRunId(_scenario.name, "observe");
            _observeDir = Path.Combine(ArpgPaths.ExportRoot, _observeRunId);
            _observeResultSaved = false;

            var sink = new CompositeEventSink();
            sink.Add(_console);
            sink.Add(_viewSink);
            if (_observeCsvEnabled)
            {
                _observeCsv = new CsvEventSink(Path.Combine(_observeDir, "events_battle_" + (_observeBattleIndex + 1).ToString("0000", Inv) + ".csv"));
                if (_observeCsv.Error != null) _status = "CSV 열기 실패: " + _observeCsv.Error;
                sink.Add(_observeCsv);
            }
            ulong seed = BatchCoordinator.SeedFor(_scenario, _observeBattleIndex);
            _sim = new BattleSimulation(_data.Catalog, _data.Rules, _scenario, _observeRunId, _observeBattleIndex, seed, sink);
            _runner.Begin(_sim);
            _runner.Speed = SpeedOptions[_speedIndex];
            _runner.Paused = true;
            BuildViews();
            if (!keepCamera) FocusCameraOnBattle();
            else if (followId >= 0 && _rig != null)
            {
                CombatantView v;
                _rig.Follow = _views.TryGetValue(followId, out v) && v != null ? v.transform : null;
            }
        }

        public void StopObserve()
        {
            if (_sim != null && !_sim.IsFinished && _sim.Tick == 0)
            {
                // 시작하지 않은 회차는 기록을 남기지 않는다
                if (_observeCsv != null) _observeCsv.Discard();
                _observeCsv = null;
            }
            else if (_sim != null && !_sim.IsFinished)
            {
                _sim.Abort();
                CloseObserveCsv(true);
            }
            else CloseObserveCsv(false);
            _sim = null;
            _runner.Begin(null);
            if (_fx != null) _fx.Clear();
            foreach (var v in _views.Values) if (v != null) Destroy(v.gameObject);
            _views.Clear();
        }

        private void CloseObserveCsv(bool partial)
        {
            if (_observeCsv == null) return;
            string p = _observeCsv.Close(partial);
            if (_observeCsv.Error != null) _status = "CSV 저장 실패: " + _observeCsv.Error;
            else if (p != null)
            {
                _lastSavedPath = p;
                Debug.Log("[ARPG] 이벤트 CSV 저장 완료: " + p + " (" + _observeCsv.Rows + "행" + (partial ? ", 부분 로그" : "") + ")");
            }
            _observeCsv = null;
        }

        private void BuildViews()
        {
            if (_fx != null)
            {
                _fx.Clear();
                if (_data != null && _data.Rules != null) _fx.SetTickSec(_data.Rules.TickSec);
            }
            if (_data != null && _data.Rules != null) CombatantView.TickMs = (float)_data.Rules.TickMs;
            foreach (var v in _views.Values) if (v != null) Destroy(v.gameObject);
            _views.Clear();
            if (_sim == null) return;
            foreach (var c in _sim.Combatants) _views[c.EntityId] = CombatantView.Create(c, _viewRoot);
            if (_selectedId >= _sim.Combatants.Count) _selectedId = -1;
        }

        private void FocusCameraOnBattle()
        {
            if (_sim == null || _rig == null) return;
            double half = (_scenario.spawnDistance > 0 ? _scenario.spawnDistance : 10) * 0.5 + 4;
            _rig.Focus = Vector3.zero;
            _rig.Distance = Mathf.Clamp((float)half * 2.2f, 10f, 80f);
            _rig.Follow = null;
        }

        // ------------------------------------------------------------------ 자동 다음 회차
        /// <summary>자동 다음 회차 켜기/끄기. 켜면 현재 판이 멈춰 있을 때 바로 재생한다.</summary>
        private void SetAutoNext(bool on)
        {
            if (on == _autoNext) return;
            _autoNext = on;
            _autoNextWait = 0f;
            if (!on) return;
            ResetAutoTally();
            if (_sim != null && !_sim.IsFinished) _runner.Paused = false;
        }

        private void ResetAutoTally()
        {
            _autoDone = 0;
            _autoWins0 = 0;
            _autoWins1 = 0;
            _autoDraws = 0;
        }

        private void CountAutoResult(BattleResult r)
        {
            _autoDone++;
            if (r.Reason == BattleEndReason.TeamVictory && r.WinnerTeam == 0) _autoWins0++;
            else if (r.Reason == BattleEndReason.TeamVictory && r.WinnerTeam == 1) _autoWins1++;
            else _autoDraws++;
        }

        /// <summary>판이 끝난 뒤 잠시 기다렸다가 다음 회차를 시작. 회차 번호가 시나리오 '반복' 값에 닿으면 멈춘다.</summary>
        private void UpdateAutoNext()
        {
            if (!_observeResultSaved) return;
            _autoNextWait += Time.unscaledDeltaTime;
            float delay = AutoNextDelaySec / (float)Math.Max(1.0, SpeedOptions[_speedIndex]);
            if (_autoNextWait < delay) return;
            int next = _observeBattleIndex + 1;
            int limit = Math.Max(1, _scenario.runs);
            if (next >= limit)
            {
                _autoNext = false;
                _status = "자동 관찰 완료: 회차 " + limit + "까지 — " + AutoTallyText();
                return;
            }
            PrepareObserve(next, true);
            if (_sim != null) _runner.Paused = false;
        }

        private string AutoTallyText()
        {
            return _autoDone + "판: 아군 승 " + _autoWins0 + " / 적군 승 " + _autoWins1 + " / 무승부·시간초과 " + _autoDraws;
        }

        private void OnObserveFinished()
        {
            if (_observeResultSaved || _sim == null || !_sim.IsFinished) return;
            _observeResultSaved = true;
            CloseObserveCsv(_sim.GetResult().Reason == BattleEndReason.UserAborted);
            try
            {
                var r = _sim.GetResult();
                // 관찰 회차 결과는 회차마다 별도 파일로 남긴다
                string single = Path.Combine(_observeDir, "result_battle_" + (r.BattleIndex + 1).ToString("0000", Inv) + ".csv");
                var w = new ResultsCsvWriter(single);
                w.Write(r);
                w.Close(false);
                WriteManifest(_observeDir, _observeRunId, "observe", 1, 1, 0, false, _lastSavedPath, 0);
                if (_autoNext) CountAutoResult(r);
                _status = "회차 " + (r.BattleIndex + 1) + " 종료: " + r.OutcomeText + " (" + r.DurationSec.ToString("0.0", Inv) + "초) — 저장: " + _observeDir;
            }
            catch (Exception ex)
            {
                _status = "결과 저장 실패: " + ex.Message;
            }
        }

        private void WriteManifest(string dir, string runId, string mode, int requested, int completed, int aborted, bool partial, string eventsCsv, double realSec)
        {
            var m = new RunManifest
            {
                runId = runId,
                createdAt = DateTime.Now.ToString("s", Inv),
                appVersion = UnityEngine.Application.version,
                unityVersion = UnityEngine.Application.unityVersion,
                mode = mode,
                scenarioName = _scenario.name,
                scenarioSignature = _scenario.Signature(),
                masterSeed = _scenario.masterSeed,
                runsRequested = requested,
                runsCompleted = completed,
                runsAborted = aborted,
                partial = partial,
                dataHash = _data.Catalog.DataHash,
                dataGeneratedAt = _data.Catalog.DataGeneratedAt,
                rulesVersion = _data.Rules.rulesVersion,
                rulesHash = StableHash.Hex(StableHash.Fnv1a64(_data.Rules.Describe())),
                rulesSource = _data.RulesSource,
                eventsCsv = eventsCsv,
                realSeconds = realSec,
                notes = "회차 seed = SplitMix64(masterSeed ^ 0xD1B54A32D192ED03*(battleIndex+1))",
                scenario = _scenario,
            };
            SummaryExporter.WriteManifest(Path.Combine(dir, "manifest.json"), m);
        }

        // ------------------------------------------------------------------ 고속 반복
        public void StartBatch()
        {
            if (_batch != null && !_batch.Finished) return;
            StopObserve();
            var errs = ValidateScenario();
            if (errs.Count > 0)
            {
                _batchReport = "시나리오 오류: " + string.Join(" / ", errs.ToArray());
                return;
            }
            _autoNext = false;
            string runId = NewRunId(_scenario.name, "batch");
            _batchDir = Path.Combine(ArpgPaths.ExportRoot, runId);
            _batchResults = new ResultsCsvWriter(Path.Combine(_batchDir, "results.csv"));
            _batchEvents = null;
            if (_batchEventMode > 0)
            {
                int firstN = _batchEventFirstN;
                Func<int, bool> filter = null;
                if (_batchEventMode == 1) filter = i => i < firstN;
                _batchEvents = new CsvEventSink(Path.Combine(_batchDir, "events.csv"), filter);
            }
            var eventsSink = _batchEvents;
            _batch = new BatchCoordinator(_data.Catalog, _data.Rules, _scenario.Clone(), runId, _scenario.runs,
                i => eventsSink, r => _batchResults.Write(r));
            _batchSaved = false;
            _batchReport = "실행 중...";
            _mode = SimMode.Batch;
        }

        private void UpdateBatch()
        {
            if (_batch == null || _batchSaved) return;
            _batch.Run(_batchBudgetMs);
            if (_batch.Finished) FinishBatchFiles(_batch.CancelRequested || _batch.ErrorMessage != null);
        }

        private void FinishBatchFiles(bool partial)
        {
            if (_batch == null || _batchSaved) return;
            _batchSaved = true;
            var s = _batch.Summary;
            try
            {
                if (_batchResults != null) _batchResults.Close(partial);
                string ev = _batchEvents != null ? _batchEvents.Close(partial) : null;
                SummaryExporter.WriteSummary(Path.Combine(_batchDir, partial ? "summary_partial.csv" : "summary.csv"), s, _scenario, _batch.RunId, partial);
                SummaryExporter.WriteSkillStats(Path.Combine(_batchDir, "skill_stats.csv"), s);
                WriteManifest(_batchDir, _batch.RunId, "batch", s.Requested, s.Completed, s.Aborted, partial, ev, s.ElapsedRealSec);
                double lo, hi;
                s.WilsonInterval(0, out lo, out hi);
                _batchReport = (partial ? "[취소/부분 결과] " : "[완료] ")
                    + "완료 " + s.Completed + "/" + s.Requested + " (중단 " + s.Aborted + ", 오류 " + s.Errors + ")\n"
                    + "아군 승 " + s.Wins(0) + " / 적군 승 " + s.Wins(1) + " / 동시 전멸 " + s.MutualWipes + " / 시간 초과 " + s.Timeouts + "\n"
                    + "아군 승률 " + (s.WinRate(0) * 100).ToString("0.0", Inv) + "% (95% CI " + (lo * 100).ToString("0.0", Inv) + "~" + (hi * 100).ToString("0.0", Inv) + "%)"
                    + ", 승패난 전투 중 " + (s.DecisiveWinRate(0) * 100).ToString("0.0", Inv) + "%\n"
                    + "전투 시간 평균 " + s.MeanDuration().ToString("0.0", Inv) + "s / 중앙 " + s.Percentile(0.5).ToString("0.0", Inv) + "s"
                    + " / 처리 " + (s.ElapsedRealSec > 0 ? (s.Completed / s.ElapsedRealSec).ToString("0.0", Inv) : "-") + " 전투/초\n"
                    + "저장: " + _batchDir;
                if (_batch.ErrorMessage != null) _batchReport += "\n오류: " + _batch.ErrorMessage;
                if (s.Completed > 0 && s.Completed < 30) _batchReport += "\n※ 완료 수가 적어 승률을 확정적으로 해석하지 마세요";
                Debug.Log("[ARPG] 반복 실행 " + _batchReport.Replace('\n', ' '));
                if (_batchResults != null && _batchResults.Error != null) _batchReport += "\nresults.csv 오류: " + _batchResults.Error;
                if (_batchEvents != null && _batchEvents.Error != null) _batchReport += "\nevents.csv 오류: " + _batchEvents.Error;
            }
            catch (Exception ex)
            {
                _batchReport = "결과 저장 실패: " + ex.Message;
                Debug.LogError("[ARPG] " + _batchReport);
            }
        }

        // ------------------------------------------------------------------ 매 프레임
        private void Update()
        {
            if (_mode == SimMode.Batch)
            {
                UpdateBatch();
            }
            else if (_sim != null)
            {
                _runner.Speed = SpeedOptions[_speedIndex];
                _runner.Update(Time.unscaledDeltaTime);
                if (_sim.IsFinished)
                {
                    OnObserveFinished();
                    if (_autoNext) UpdateAutoNext();
                }
            }
            HandlePointer();
            UpdateViews();
        }

        private void UpdateViews()
        {
            if (_sim == null)
            {
                if (_draw != null) { _draw.Begin(); _draw.End(); }
                return;
            }
            float alpha = _runner.Alpha;
            long tick = _sim.Tick;
            foreach (var c in _sim.Combatants)
            {
                CombatantView v;
                if (_views.TryGetValue(c.EntityId, out v) && v != null) v.Apply(c, alpha, tick, c.EntityId == _selectedId, _cam);
            }
            UpdateAttackFx(alpha);
            if (_draw == null) return;
            _draw.Begin();
            if (_showZones)
            {
                foreach (var c in _sim.Combatants)
                {
                    if (c.Dead) continue;
                    Vec2 center;
                    double radius;
                    if (_sim.TryGetHitZone(c, out center, out radius))
                    {
                        var a = c.Action;
                        bool active = a.NextHit < a.Skill.HitTimesMs.Length && a.Skill.HitTimesMs[a.NextHit] - a.ElapsedMs <= 120;
                        _draw.Circle(new Vector3((float)center.x, 0, (float)center.y), (float)radius,
                            active ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(1f, 0.85f, 0.2f, 0.6f));
                    }
                }
                if (_selectedId >= 0 && _selectedId < _sim.Combatants.Count)
                {
                    var s = _sim.Combatants[_selectedId];
                    var p = new Vector3((float)s.Pos.x, 0, (float)s.Pos.y);
                    var r = _data.Rules;
                    _draw.Circle(p, (float)r.distMeleeMax, new Color(1f, 1f, 1f, 0.25f), 0.03f);
                    _draw.Circle(p, (float)r.distMidMax, new Color(1f, 1f, 1f, 0.12f), 0.03f);
                    if (s.Target != null && !s.Dead)
                        _draw.Line(p, new Vector3((float)s.Target.Pos.x, 0, (float)s.Target.Pos.y), new Color(1f, 0.95f, 0.3f, 0.7f));
                }
            }
            _draw.End();
        }

        /// <summary>
        /// 공격 연출 시계 = 화면에 표시 중인 전투 시간(tick 단위).
        /// 표시 상태는 Lerp(S(Tick-2), S(Tick-1), alpha) 이므로 표시 시간 = Tick - 2 + alpha (정지 시 alpha=1 → 최신 상태).
        /// 전투가 끝나면 tick 이 멈추므로 남은 연출은 실제 시간 × 배속으로 마저 진행한다.
        /// </summary>
        private void UpdateAttackFx(float alpha)
        {
            if (_fx == null || !_fx.Enabled) return;
            if (_sim.IsFinished)
            {
                if (!_runner.Paused)
                    _fx.AdvanceBy(Time.unscaledDeltaTime * SpeedOptions[_speedIndex] / _data.Rules.TickSec);
                else _fx.AdvanceBy(0);
            }
            else _fx.Advance((double)_sim.Tick - 2.0 + alpha);
        }

        private void HandlePointer()
        {
            if (_rig == null) return;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            Vector2 mp = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            bool overUi = IsPointerOverPanel(new Vector2(mp.x, Screen.height - mp.y));
            _rig.BlockPointer = overUi;
            _rig.BlockKeyboard = GUIUtility.keyboardControl != 0;
            if (mouse != null && !overUi && mouse.leftButton.wasPressedThisFrame && _cam != null)
            {
                Ray ray = _cam.ScreenPointToRay(new Vector3(mp.x, mp.y, 0));
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 500f))
                {
                    var v = hit.collider.GetComponentInParent<CombatantView>();
                    _selectedId = v != null ? v.EntityId : -1;
                }
                else _selectedId = -1;
            }
            if (kb != null && GUIUtility.keyboardControl == 0)
            {
                if (kb.fKey.wasPressedThisFrame && _selectedId >= 0)
                {
                    CombatantView v;
                    if (_views.TryGetValue(_selectedId, out v)) _rig.Follow = v.transform;
                }
                if (kb.spaceKey.wasPressedThisFrame && _sim != null && _mode == SimMode.Observe) _runner.Paused = !_runner.Paused;
                if (kb.periodKey.wasPressedThisFrame && _sim != null && _mode == SimMode.Observe) _runner.StepOnce();
                if (kb.ctrlKey.isPressed)
                {
                    // 글자 크기: Ctrl + / Ctrl -
                    if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) RequestUiScaleStep(1);
                    if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) RequestUiScaleStep(-1);
                }
            }
#endif
        }

        // ------------------------------------------------------------------ Self Test
        private void RunSelfTests()
        {
            _tests = CoreSelfTests.RunAll(_data != null ? _data.Catalog : null, _data != null ? _data.Rules : new CombatRules(), _scenario);
            int pass = 0;
            foreach (var t in _tests)
            {
                if (t.Passed) pass++;
                if (t.Passed) Debug.Log("[ARPG SelfTest] " + t);
                else Debug.LogError("[ARPG SelfTest] " + t);
            }
            _status = "Self Test " + pass + "/" + _tests.Count + " 통과";
        }

        private static void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Directory.CreateDirectory(path);
                UnityEngine.Application.OpenURL(ArpgPaths.ToFileUrl(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ARPG] 폴더 열기 실패: " + ex.Message);
            }
        }
    }

    /// <summary>Simulator/SampleScene 에 부트스트랩이 없으면 플레이 시 자동 생성.</summary>
    public static class SimulatorAutoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (HeadlessBatch.IsBatchRequested) return;
            if (SimulationBootstrap.Instance != null) return;
            string scene = SceneManager.GetActiveScene().name;
            if (scene == "Simulator" || scene == "SampleScene")
                new GameObject("ARPG Simulator").AddComponent<SimulationBootstrap>();
        }
    }
}
