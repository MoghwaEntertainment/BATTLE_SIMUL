using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ARPG.Core;
using ARPG.Core.Simulation;
using UnityEngine;

namespace ARPG.Runtime
{
    /// <summary>
    /// 화면 UI(IMGUI). Input System 전용 프로젝트에서도 EventSystem 설정 없이 동작한다.
    /// 시나리오 편집·시작/정지/1 tick/재시작·배속·로그 수준·CSV·고속 반복·Self Test·선택 개체 정보창.
    /// </summary>
    public sealed partial class SimulationBootstrap
    {
        private const float LeftWidth = 430f;
        private const float RightWidth = 340f;
        private Rect _leftRect, _rightRect, _logRect, _pickerRect = new Rect(460, 80, 520, 560);
        private Vector2 _leftScroll, _rightScroll, _logScroll, _pickerScroll;
        // 로그창: 오래된 줄이 위, 최신이 아래. 맨 아래를 보고 있으면 새 줄을 따라 내려가고, 위로 올려 읽는 중이면 멈춘다
        private const int LogLines = 60;
        private const float LogBottomFollowScroll = 1e7f;
        private bool _logFollow = true;
        private long _logSeenVersion = -1;
        private float _logContentH;
        private bool _pickerOpen;
        private int _pickerTeam;
        private string _pickerSearch = "";
        private bool _pickerMonsters = true, _pickerPcs = true;
        private string _seedText = "12345", _runsText = "100", _maxSecText = "300", _spawnText = "10", _firstNText = "3", _nameText = "";
        private bool _showLog = true;
        private GUIStyle _wrap, _small, _bold, _mono, _center;
        private GUIStyle _pickRow, _nameTag;   // 캐릭터 선택 줄(한 줄 고정) · 머리 위 이름(줄바꿈 없음, 잘림 없음)

        // 화면 배율(글자·버튼·패널 크기). 글꼴 크기를 직접 키워 글자가 흐려지지 않게 한다.
        private static readonly float[] UiScaleSteps = { 0.8f, 0.9f, 1f, 1.15f, 1.3f, 1.5f, 1.75f, 2f };
        private const string UiScalePrefKey = "ARPG.UiScale";
        private const int BaseFontSize = 14;
        private const int BaseSmallFontSize = 11;
        private int _uiScaleIndex = -1;
        private int _requestedUiScaleIndex = -1;
        private float _uiScale = 1f;
        private float _appliedUiScale = -1f;
        private GUISkin _skin;
        private readonly Dictionary<GUIStyle, float> _baseFixedHeight = new Dictionary<GUIStyle, float>();
        private float _leftW = LeftWidth, _rightW = RightWidth;
        private readonly List<CharacterDefinition> _pickerList = new List<CharacterDefinition>();
        private string _pickerCacheKey;
        // 캐릭터 선택 창 가상 스크롤: 전체 목록을 다 보여 주되, 화면에 보이는 줄만 버튼으로 그린다.
        // 보이는 범위는 Layout 단계에서만 정한다(같은 프레임의 Layout·Repaint·입력 이벤트가 같은 개수의 줄을 그려야 GUILayout 오류가 없다)
        private int _pickerFirst, _pickerLast;
        private float _pickerRowH = 22f, _pickerViewH = 400f;
        private static readonly GUIContent PickerProbe = new GUIContent("가Ag");
        private readonly GUIContent _nameContent = new GUIContent();

        private void SyncScenarioTexts()
        {
            _seedText = _scenario.masterSeed.ToString(Inv);
            _runsText = _scenario.runs.ToString(Inv);
            _maxSecText = _scenario.maxBattleSec.ToString(Inv);
            _spawnText = _scenario.spawnDistance.ToString(Inv);
            _nameText = _scenario.name;
        }

        private bool IsPointerOverPanel(Vector2 guiPos)
        {
            if (_leftRect.Contains(guiPos)) return true;
            if (_selectedId >= 0 && _rightRect.Contains(guiPos)) return true;
            if (_showLog && _logRect.Contains(guiPos)) return true;
            if (_pickerOpen && _pickerRect.Contains(guiPos)) return true;
            return false;
        }

        private void EnsureStyles()
        {
            if (_skin == null)
            {
                // 기본 skin 을 직접 바꾸지 않도록 복사본을 쓴다
                _skin = Instantiate(GUI.skin);
                _skin.name = "ARPG_UISkin";
                if (_uiFont != null) _skin.font = _uiFont;
                _wrap = new GUIStyle(_skin.label) { wordWrap = true };
                _small = new GUIStyle(_skin.label) { wordWrap = true };
                _bold = new GUIStyle(_skin.label) { fontStyle = FontStyle.Bold };
                _mono = new GUIStyle(_skin.label) { wordWrap = false };
                _center = new GUIStyle(_skin.label) { alignment = TextAnchor.MiddleCenter };
                _pickRow = new GUIStyle(_skin.label) { wordWrap = false, clipping = TextClipping.Clip, margin = new RectOffset(4, 4, 0, 0) };
                _nameTag = new GUIStyle(_skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow,
                    padding = new RectOffset(2, 2, 1, 1), margin = new RectOffset(0, 0, 0, 0)
                };
                _baseFixedHeight.Clear();
                foreach (var st in TextStyles()) if (st != null && !_baseFixedHeight.ContainsKey(st)) _baseFixedHeight[st] = st.fixedHeight;
                _appliedUiScale = -1f;
            }
            if (_uiScaleIndex < 0)
            {
                float saved = 1f;
                try { saved = PlayerPrefs.GetFloat(UiScalePrefKey, 1f); } catch (Exception) { saved = 1f; }
                _uiScaleIndex = NearestUiScaleIndex(saved);
                _requestedUiScaleIndex = _uiScaleIndex;
                _uiScale = UiScaleSteps[_uiScaleIndex];
            }
            // 배율 변경은 Layout 단계에서만 반영(Layout/Repaint 사이에 크기가 바뀌면 GUILayout 오류)
            if (Event.current.type == EventType.Layout && _requestedUiScaleIndex >= 0 && _requestedUiScaleIndex != _uiScaleIndex)
            {
                _uiScaleIndex = _requestedUiScaleIndex;
                _uiScale = UiScaleSteps[_uiScaleIndex];
                try { PlayerPrefs.SetFloat(UiScalePrefKey, _uiScale); PlayerPrefs.Save(); } catch (Exception) { }
            }
            if (!Mathf.Approximately(_appliedUiScale, _uiScale)) ApplyUiScaleToStyles();
        }

        private void ApplyUiScaleToStyles()
        {
            _appliedUiScale = _uiScale;
            int normal = Mathf.Max(8, Mathf.RoundToInt(BaseFontSize * _uiScale));
            int small = Mathf.Max(8, Mathf.RoundToInt(BaseSmallFontSize * _uiScale));
            // Toolbar 버튼(ButtonLeft/Mid/Right) 등 customStyles 포함. 높이가 고정된 스타일은 같은 비율로 키워 글자가 잘리지 않게 한다
            foreach (var st in TextStyles())
            {
                if (st == null) continue;
                st.fontSize = normal;
                float fh;
                if (_baseFixedHeight.TryGetValue(st, out fh) && fh > 0) st.fixedHeight = Mathf.Round(fh * _uiScale);
            }
            _wrap.fontSize = normal;
            _bold.fontSize = normal;
            _small.fontSize = small;
            _mono.fontSize = small;
            _center.fontSize = small;
            _pickRow.fontSize = normal;
            _nameTag.fontSize = small;
        }

        private IEnumerable<GUIStyle> TextStyles()
        {
            yield return _skin.label;
            yield return _skin.button;
            yield return _skin.toggle;
            yield return _skin.textField;
            yield return _skin.textArea;
            yield return _skin.box;
            yield return _skin.window;
            if (_skin.customStyles != null)
                foreach (var st in _skin.customStyles) yield return st;
        }

        private static int NearestUiScaleIndex(float scale)
        {
            int best = 2;
            float bestD = float.MaxValue;
            for (int i = 0; i < UiScaleSteps.Length; i++)
            {
                float d = Mathf.Abs(UiScaleSteps[i] - scale);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>배율 단계 변경 요청(다음 Layout 단계에서 반영). delta=0 이면 100% 로.</summary>
        private void RequestUiScaleStep(int delta)
        {
            int cur = _requestedUiScaleIndex >= 0 ? _requestedUiScaleIndex : (_uiScaleIndex >= 0 ? _uiScaleIndex : 2);
            int next = delta == 0 ? NearestUiScaleIndex(1f) : Mathf.Clamp(cur + delta, 0, UiScaleSteps.Length - 1);
            _requestedUiScaleIndex = next;
        }

        /// <summary>배율이 적용된 고정 폭.</summary>
        private GUILayoutOption W(float px)
        {
            return GUILayout.Width(Mathf.Round(px * _uiScale));
        }

        private float S(float px)
        {
            return Mathf.Round(px * _uiScale);
        }

        private void DrawUiScaleControl()
        {
            GUILayout.Label("글자", W(34));
            GUI.enabled = _uiScaleIndex > 0;
            if (GUILayout.Button("-", W(28))) RequestUiScaleStep(-1);
            GUI.enabled = true;
            GUILayout.Label(Mathf.RoundToInt(_uiScale * 100f) + "%", _center, W(46));
            GUI.enabled = _uiScaleIndex < UiScaleSteps.Length - 1;
            if (GUILayout.Button("+", W(28))) RequestUiScaleStep(1);
            GUI.enabled = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            GUI.skin = _skin;
            _leftW = Mathf.Min(S(LeftWidth), Screen.width * 0.6f);
            _rightW = Mathf.Min(S(RightWidth), Screen.width * 0.45f);
            _leftRect = new Rect(8, 8, _leftW, Screen.height - 16);
            _rightRect = new Rect(Screen.width - _rightW - 8, 8, _rightW, Mathf.Min(S(560), Screen.height - 16));
            float logH = Mathf.Min(S(182), Screen.height * 0.45f);
            _logRect = new Rect(_leftW + 16, Screen.height - logH - 8, Mathf.Max(200, Screen.width - _leftW - _rightW - 40), logH);

            DrawEntityLabels();
            DrawTopBar();
            GUI.Box(_leftRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(_leftRect.x + 6, _leftRect.y + 6, _leftRect.width - 12, _leftRect.height - 12));
            _leftScroll = GUILayout.BeginScrollView(_leftScroll);
            DrawLeftPanel();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (_selectedId >= 0 && _sim != null && _selectedId < _sim.Combatants.Count) DrawRightPanel(_sim.Combatants[_selectedId]);
            if (_showLog && _mode == SimMode.Observe) DrawLogPanel();
            if (_pickerOpen)
            {
                _pickerRect.width = Mathf.Min(S(520), Screen.width - 16);
                _pickerRect.height = Mathf.Min(S(560), Screen.height - 16);
                _pickerRect.x = Mathf.Clamp(_pickerRect.x, 0, Mathf.Max(0, Screen.width - _pickerRect.width));
                _pickerRect.y = Mathf.Clamp(_pickerRect.y, 0, Mathf.Max(0, Screen.height - _pickerRect.height));
                _pickerRect = GUI.Window(7301, _pickerRect, DrawPicker, GUIContent.none);
            }
        }

        // ------------------------------------------------------------------ 상단 바
        private void DrawTopBar()
        {
            var r = new Rect(_leftW + 16, 8, Mathf.Max(200, Screen.width - _leftW - _rightW - 40), S(26));
            string text;
            if (_mode == SimMode.Batch && _batch != null)
                text = "고속 반복 " + _batch.NextIndex + "/" + _batch.Runs + (_batch.Finished ? " 완료" : " 실행 중");
            else if (_sim != null)
            {
                text = "회차 " + (_observeBattleIndex + 1) + " | tick " + _sim.Tick + " / " + _sim.TimeSec.ToString("0.000", Inv) + "s"
                     + " | " + SpeedOptions[_speedIndex].ToString("0.#", Inv) + "배속" + (_runner.Paused ? " (일시정지)" : "")
                     + (_runner.Lagging ? " | 처리 지연" : "")
                     + (_autoNext ? " | 자동 다음 회차" : "");
                if (_sim.IsFinished) text += " | 결과: " + _sim.GetResult().OutcomeText;
            }
            else text = "준비되지 않음";
            GUI.Box(r, text);
        }

        // ------------------------------------------------------------------ 좌측 패널
        private void DrawLeftPanel()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("ARPG 전투 밸런스 시뮬레이터", _bold);
            GUILayout.FlexibleSpace();
            DrawUiScaleControl();
            GUILayout.EndHorizontal();
            GUILayout.Label(_status, _small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("데이터 다시 읽기")) ReloadData();
            if (GUILayout.Button("검증 보고서")) UnityEngine.Application.OpenURL(ArpgPaths.ToFileUrl(ArpgPaths.ValidationReport));
            if (GUILayout.Button("Export 폴더")) OpenFolder(ArpgPaths.ExportRoot);
            GUILayout.EndHorizontal();
            if (_data == null || _data.Catalog == null) return;

            if (_data.Catalog.Issues.Count > 0)
                GUILayout.Label("로드 검증: 오류 " + _data.Catalog.BlockingIssueCount + " / 경고 " + (_data.Catalog.Issues.Count - _data.Catalog.BlockingIssueCount) + " (Console 참고)", _small);

            GUILayout.Space(6);
            GUILayout.Label("■ 시나리오", _bold);
            GUILayout.BeginHorizontal();
            GUI.enabled = _scenarioFiles.Count > 0 && !BatchRunning;
            if (GUILayout.Button("◀", W(28))) { LoadScenario((_scenarioIndex - 1 + _scenarioFiles.Count) % _scenarioFiles.Count); PrepareObserve(0); }
            GUILayout.Label(_scenarioIndex >= 0 ? (_scenarioIndex + 1) + "/" + _scenarioFiles.Count : "-", W(44));
            if (GUILayout.Button("▶", W(28))) { LoadScenario((_scenarioIndex + 1) % _scenarioFiles.Count); PrepareObserve(0); }
            GUI.enabled = !BatchRunning;
            string newName = GUILayout.TextField(_nameText);
            if (newName != _nameText) { _nameText = newName; _scenario.name = newName; }
            if (GUILayout.Button("저장", W(44))) SaveScenario();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_scenario.description)) GUILayout.Label(_scenario.description, _small);

            bool changed = false;
            foreach (var team in _scenario.teams)
            {
                GUILayout.BeginHorizontal();
                GUI.color = CombatantView.TeamColor(team.teamId);
                GUILayout.Label("팀 " + team.teamId + " " + team.name, _bold);
                GUI.color = Color.white;
                if (GUILayout.Button("+ 추가", W(60))) { _pickerOpen = true; _pickerTeam = team.teamId; }
                GUILayout.EndHorizontal();
                for (int i = 0; i < team.members.Count; i++)
                {
                    var m = team.members[i];
                    CharacterDefinition def;
                    _data.Catalog.Characters.TryGetValue(m.definitionId ?? "", out def);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((def != null ? def.DisplayName : "(없음)") + "  [" + m.definitionId + "]", _small);
                    if (GUILayout.Button("-", W(22))) { m.count = Math.Max(0, m.count - 1); changed = true; }
                    GUILayout.Label("×" + m.count, W(30));
                    if (GUILayout.Button("+", W(22))) { m.count++; changed = true; }
                    if (GUILayout.Button("x", W(22))) { team.members.RemoveAt(i); changed = true; GUILayout.EndHorizontal(); break; }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("masterSeed", W(80));
            changed |= LongField(ref _seedText, v => _scenario.masterSeed = v);
            GUILayout.Label("반복", W(32));
            changed |= IntField(ref _runsText, v => _scenario.runs = Math.Max(1, v));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("제한(초)", W(80));
            changed |= DoubleField(ref _maxSecText, v => _scenario.maxBattleSec = Math.Max(1, v));
            GUILayout.Label("거리", W(32));
            changed |= DoubleField(ref _spawnText, v => _scenario.spawnDistance = Math.Max(1, v));
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            if (changed && _mode == SimMode.Observe) { _observeRunId = null; PrepareObserve(0); }

            GUILayout.Space(6);
            int mode = GUILayout.Toolbar((int)_mode, new[] { "관찰 모드", "고속 반복(비시각)" });
            if (mode != (int)_mode && !BatchRunning)
            {
                _mode = (SimMode)mode;
                if (_mode == SimMode.Observe) PrepareObserve(_observeBattleIndex);
                else { _autoNext = false; StopObserve(); }
            }

            if (_mode == SimMode.Observe) DrawObserveControls();
            else DrawBatchControls();

            GUILayout.Space(8);
            GUILayout.Label("■ 검증", _bold);
            GUI.enabled = !BatchRunning;
            if (GUILayout.Button("Self Test 실행 (규칙 사례·결정성)")) RunSelfTests();
            GUI.enabled = true;
            if (_tests != null)
            {
                foreach (var t in _tests)
                {
                    GUI.color = t.Passed ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.5f);
                    GUILayout.Label(t.ToString(), _small);
                }
                GUI.color = Color.white;
            }
            GUILayout.Space(6);
            GUILayout.Label("조작: 좌클릭 선택 · 우드래그 회전 · 휠 확대 · 중드래그/WASD 이동 · Q/E 회전 · F 추적 · Space 재생/정지 · . 1 tick · Ctrl +/- 글자 크기", _small);
        }

        private bool BatchRunning { get { return _batch != null && !_batch.Finished; } }

        private void DrawObserveControls()
        {
            GUILayout.Label("■ 관찰", _bold);
            GUILayout.BeginHorizontal();
            GUI.enabled = _sim != null && !_sim.IsFinished;
            if (GUILayout.Button(_runner.Paused ? "▶ 시작/재개" : "⏸ 정지")) _runner.Paused = !_runner.Paused;
            if (GUILayout.Button("1 tick")) _runner.StepOnce();
            GUI.enabled = true;
            if (GUILayout.Button("재시작(같은 seed)")) PrepareObserve(_observeBattleIndex);
            if (GUILayout.Button("다음 회차")) PrepareObserve(_observeBattleIndex + 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool auto = GUILayout.Toggle(_autoNext, "자동 다음 회차 (회차 " + Math.Max(1, _scenario.runs) + "까지)");
            if (auto != _autoNext) SetAutoNext(auto);
            GUILayout.EndHorizontal();
            if (_autoNext || _autoDone > 0)
                GUILayout.Label((_autoNext ? "자동 진행 중 · " : "자동 관찰 결과 · ") + AutoTallyText(), _small);

            GUILayout.BeginHorizontal();
            GUILayout.Label("배속", W(36));
            var labels = new string[SpeedOptions.Length];
            for (int i = 0; i < labels.Length; i++) labels[i] = SpeedOptions[i].ToString("0.#", Inv) + "x";
            _speedIndex = GUILayout.Toolbar(_speedIndex, labels);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Console", W(56));
            _console.ConsoleLevel = (LogLevel)GUILayout.Toolbar((int)_console.ConsoleLevel, new[] { "끔", "핵심", "상세" });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("화면 로그", W(56));
            _console.ScreenLevel = (LogLevel)GUILayout.Toolbar((int)_console.ScreenLevel, new[] { "끔", "핵심", "상세" });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool csv = GUILayout.Toggle(_observeCsvEnabled, "이벤트 CSV 기록");
            if (csv != _observeCsvEnabled) { _observeCsvEnabled = csv; PrepareObserve(_observeBattleIndex); }
            _showLog = GUILayout.Toggle(_showLog, "로그창");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            _showZones = GUILayout.Toggle(_showZones, "판정 범위 표시");
            bool fx = GUILayout.Toggle(_showAttackFx, "공격 이펙트(막대·착탄)");
            if (fx != _showAttackFx)
            {
                _showAttackFx = fx;
                if (_fx != null)
                {
                    _fx.Enabled = fx;
                    if (!fx) _fx.Clear();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("CSV 는 회차가 끝날 때 저장되며, 도중 재시작/다음 회차는 _partial 로 저장됩니다.", _small);
            if (!string.IsNullOrEmpty(_lastSavedPath))
            {
                GUILayout.Label("최근 저장: " + _lastSavedPath, _small);
            }
            if (GUILayout.Button("저장 폴더 열기")) OpenFolder(_observeDir ?? ArpgPaths.ExportRoot);

            if (_sim != null)
            {
                GUILayout.Space(4);
                foreach (var c in _sim.Combatants)
                {
                    GUILayout.BeginHorizontal();
                    GUI.color = c.Dead ? Color.gray : CombatantView.TeamColor(c.TeamId);
                    if (GUILayout.Button(c.Label, W(42))) _selectedId = c.EntityId;
                    GUI.color = Color.white;
                    GUILayout.Label(c.Def.DisplayName + "  HP " + c.Hp.ToString("0", Inv) + "/" + c.MaxHp.ToString("0", Inv)
                                    + (c.ResourcesManaged && c.MaxStamina > 0 ? "  ST " + c.Stamina.ToString("0.0", Inv) : "")
                                    + (c.Dead ? "  사망" : ""), _small);
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void DrawBatchControls()
        {
            GUILayout.Label("■ 고속 반복(캐릭터 오브젝트 없이 N회)", _bold);
            GUILayout.Label("반복 " + _scenario.runs + "회, masterSeed " + _scenario.masterSeed + ", 제한 " + _scenario.maxBattleSec + "초", _small);
            GUI.enabled = !BatchRunning;
            GUILayout.BeginHorizontal();
            GUILayout.Label("이벤트 CSV", W(70));
            _batchEventMode = GUILayout.Toolbar(_batchEventMode, new[] { "없음", "처음 N회", "전체" });
            GUILayout.EndHorizontal();
            if (_batchEventMode == 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("N", W(70));
                IntField(ref _firstNText, v => _batchEventFirstN = Math.Max(1, v));
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("프레임 예산 " + _batchBudgetMs.ToString("0", Inv) + "ms", W(120));
            _batchBudgetMs = GUILayout.HorizontalSlider(_batchBudgetMs, 4f, 60f);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            GUI.enabled = !BatchRunning;
            if (GUILayout.Button("실행")) StartBatch();
            GUI.enabled = BatchRunning;
            if (GUILayout.Button("취소")) _batch.Cancel();
            GUI.enabled = true;
            if (GUILayout.Button("결과 폴더 열기")) OpenFolder(_batchDir ?? ArpgPaths.ExportRoot);
            GUILayout.EndHorizontal();

            if (_batch != null)
            {
                Rect r = GUILayoutUtility.GetRect(10, S(18), GUILayout.ExpandWidth(true));
                GUI.Box(r, GUIContent.none);
                var old = GUI.color;
                GUI.color = new Color(0.3f, 0.8f, 0.4f);
                GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01((float)_batch.Progress01), r.height - 4), Texture2D.whiteTexture);
                GUI.color = old;
                var s = _batch.Summary;
                GUILayout.Label("진행 " + _batch.NextIndex + "/" + _batch.Runs + " | 경과 " + _batch.ElapsedSec.ToString("0.0", Inv) + "s"
                                + " | 아군 승 " + s.Wins(0) + " / 적군 승 " + s.Wins(1) + " / 무승부 " + (s.MutualWipes + s.Timeouts), _small);
            }
            if (!string.IsNullOrEmpty(_batchReport)) GUILayout.Label(_batchReport, _wrap);
        }

        private bool LongField(ref string text, Action<long> apply)
        {
            string t = GUILayout.TextField(text, W(110));
            if (t == text) return false;
            text = t;
            long v;
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) { apply(v); return true; }
            return false;
        }

        private bool IntField(ref string text, Action<int> apply)
        {
            string t = GUILayout.TextField(text, W(70));
            if (t == text) return false;
            text = t;
            int v;
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) { apply(v); return true; }
            return false;
        }

        private bool DoubleField(ref string text, Action<double> apply)
        {
            string t = GUILayout.TextField(text, W(70));
            if (t == text) return false;
            text = t;
            double v;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { apply(v); return true; }
            return false;
        }

        // ------------------------------------------------------------------ 선택 개체 정보창
        private void DrawRightPanel(Combatant c)
        {
            GUI.Box(_rightRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(_rightRect.x + 6, _rightRect.y + 6, _rightRect.width - 12, _rightRect.height - 12));
            _rightScroll = GUILayout.BeginScrollView(_rightScroll);
            long t = _sim.Tick;
            var sb = new StringBuilder();
            GUILayout.Label(c.Label + " " + c.Def.DisplayName, _bold);
            sb.Append("EntityId ").Append(c.EntityId).Append(" / 정의 ").Append(c.Def.Id).Append(" / 팀 ").Append(c.TeamId).Append('\n');
            sb.Append("AI ").Append(c.Def.AiProfileId);
            if (c.Def.SourceAiId != c.Def.AiProfileId) sb.Append(" (원본 ").Append(c.Def.SourceAiId).Append(')');
            sb.Append(" / 무브셋 ").Append(c.Def.MovesetId).Append('\n');
            sb.Append("HP ").Append(c.Hp.ToString("0.00", Inv)).Append(" / ").Append(c.MaxHp.ToString("0.00", Inv)).Append('\n');
            if (c.MaxFp > 0) sb.Append("FP ").Append(c.Fp.ToString("0.00", Inv)).Append(" / ").Append(c.MaxFp.ToString("0.00", Inv)).Append('\n');
            else sb.Append("FP 미사용(최대 0)\n");
            if (c.ResourcesManaged && c.MaxStamina > 0)
                sb.Append("스태미나 ").Append(c.Stamina.ToString("0.00", Inv)).Append(" / ").Append(c.MaxStamina.ToString("0.00", Inv))
                  .Append(c.Stamina < 1 ? "  (1 미만: 소모 행동 불가)" : "").Append('\n');
            else sb.Append("스태미나 미관리(몬스터)\n");
            sb.Append("강인도 ").Append(c.Poise.ToString("0.0", Inv)).Append(" / ").Append(c.MaxPoise.ToString("0.0", Inv)).Append('\n');
            sb.Append("상태: ").Append(c.Dead ? "사망" : c.StateFor(t).ToString());
            if (c.IsStunned(t)) sb.Append(" 기절(").Append(c.StunUntilTick - t).Append("t)");
            if (c.IsStaggered(t)) sb.Append(" 경직(").Append(c.StaggerUntilTick - t).Append("t)");
            if (c.PoisonActive) sb.Append(" 중독(").Append(c.PoisonTicksLeft).Append("회)");
            if (c.FrostActive(t)) sb.Append(" 빙결(").Append(c.FrostEndTick - t).Append("t)");
            sb.Append('\n');
            if (c.Buffs.Count > 0)
            {
                sb.Append("버프:");
                foreach (var ab in c.Buffs)
                    sb.Append(' ').Append(ab.Def.Name).Append("(#").Append(ab.Def.Id).Append(", ").Append(Math.Max(0, ab.EndTick - t)).Append("t)");
                sb.Append('\n');
            }
            sb.Append("누적 기절 ").Append(c.Buildup[(int)StatusKind.Stun].ToString("0.0", Inv)).Append("/").Append(c.Stat(StatId.STUN_RESIST).ToString("0", Inv))
              .Append("  상처 ").Append(c.Buildup[(int)StatusKind.Wound].ToString("0.0", Inv)).Append("/").Append(c.Stat(StatId.WOUND_RESIST).ToString("0", Inv))
              .Append("  중독 ").Append(c.Buildup[(int)StatusKind.Poison].ToString("0.0", Inv)).Append("/").Append(c.Stat(StatId.POISON_RESIST).ToString("0", Inv))
              .Append("  빙결 ").Append(c.Buildup[(int)StatusKind.Frost].ToString("0.0", Inv)).Append("/").Append(c.Stat(StatId.FROST_RESIST).ToString("0", Inv)).Append('\n');
            var a = c.Action;
            if (a != null)
            {
                sb.Append("행동 ").Append(a.ActionId);
                if (a.Skill != null) sb.Append(" (").Append(a.Skill.Id).Append(')');
                sb.Append('\n').Append("  진행 ").Append(a.ElapsedMs.ToString("0", Inv)).Append(" / ").Append(a.DurationMs.ToString("0", Inv)).Append("ms");
                if (a.CancelMs > 0) sb.Append(" 캔슬 ").Append(a.CancelMs.ToString("0", Inv));
                else if (a.Kind == ActionKind.Skill || a.Kind == ActionKind.Block) sb.Append(" 캔슬 0(끝까지)");
                if (a.Skill != null && a.Skill.HitTimesMs.Length > 0) sb.Append(" 타격 ").Append(a.NextHit).Append("/").Append(a.Skill.HitTimesMs.Length);
                if (a.IsComboChain) sb.Append(" 콤보");
                sb.Append('\n');
            }
            else sb.Append("행동 없음\n");
            sb.Append("AI 행 #").Append(c.LastAiRule).Append(' ').Append(c.LastAiNote).Append('\n');
            sb.Append("페이즈 ").Append(c.Phase).Append(" (피해×").Append(c.PhaseDmgMult.ToString("0.##", Inv)).Append(", 속도×").Append(c.PhaseSpeedMult.ToString("0.##", Inv)).Append(")\n");
            sb.Append("대상 ").Append(c.Target != null ? c.Target.Label + " " + c.Target.Def.DisplayName : "없음");
            if (c.Target != null) sb.Append(" / 거리 ").Append(Vec2.Distance(c.Pos, c.Target.Pos).ToString("0.00", Inv)).Append("m");
            sb.Append('\n');
            sb.Append("콤보 노드 ").Append(c.ComboNodeId).Append('\n');
            sb.Append("최근 행동 ").Append(string.Join(",", c.RecentActions.ToArray())).Append('\n');
            sb.Append("쿨다운:");
            foreach (var kv in c.CooldownReadyTick) if (kv.Value > t) sb.Append(' ').Append(kv.Key).Append('(').Append(((kv.Value - t) / (double)_data.Rules.tickRate).ToString("0.0", Inv)).Append("s)");
            sb.Append('\n');
            sb.Append("누적 피해 ").Append(c.DamageDealt.ToString("0", Inv)).Append(" / 받은 피해 ").Append(c.DamageTaken.ToString("0", Inv)).Append('\n');
            sb.Append("명중 ").Append(c.HitsLanded).Append(" 피격 ").Append(c.HitsTaken).Append(" 가드 ").Append(c.HitsGuarded).Append(" 회피 ").Append(c.HitsAvoided)
              .Append(" 경직 ").Append(c.StaggerCount).Append(" 자원부족 건너뜀 ").Append(c.ResourceBlockedCount);
            GUILayout.Label(sb.ToString(), _small);
            if (GUILayout.Button("카메라 추적(F)"))
            {
                CombatantView v;
                if (_views.TryGetValue(c.EntityId, out v)) _rig.Follow = v.transform;
            }
            if (GUILayout.Button("선택 해제")) _selectedId = -1;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ 로그창
        private void DrawLogPanel()
        {
            GUI.Box(_logRect, GUIContent.none);
            var area = new Rect(_logRect.x + 4, _logRect.y + 4, _logRect.width - 8, _logRect.height - 8);
            var lines = _console.Recent;
            if (lines.Count == 0) _logFollow = true;                       // 새 회차(로그 비움) → 다시 맨 아래 따라가기

            // 새 줄이 들어왔고 맨 아래를 보고 있었다면 맨 아래로(스크롤 위치는 Layout 단계에서만 바꾼다)
            if (Event.current.type == EventType.Layout && _console.Version != _logSeenVersion)
            {
                _logSeenVersion = _console.Version;
                if (_logFollow) _logScroll.y = LogBottomFollowScroll;      // 큰 값 = 맨 아래(스크롤 뷰가 끝으로 맞춤)
            }

            GUILayout.BeginArea(area);
            _logScroll = GUILayout.BeginScrollView(_logScroll);
            // 최근 LogLines 줄을 오래된 것부터(위) 최신(아래) 순서로
            var node = lines.Last;
            for (int n = 1; node != null && node.Previous != null && n < LogLines; n++) node = node.Previous;
            for (; node != null; node = node.Next) GUILayout.Label(node.Value, _mono);
            if (Event.current.type == EventType.Repaint && lines.Count > 0) _logContentH = GUILayoutUtility.GetLastRect().yMax;
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // 사용자가 위로 올려 읽는 중인지 판단(맨 아래에서 4px 이내면 따라가기 유지)
            if (Event.current.type == EventType.Repaint)
                _logFollow = _logScroll.y >= Mathf.Max(0f, _logContentH - area.height) - 4f;

            // 위로 올려 둔 동안에는 '최신으로' 버튼
            if (!_logFollow && lines.Count > 0)
            {
                var b = new Rect(_logRect.xMax - S(96) - 22, _logRect.yMax - S(24) - 22, S(96), S(24)); // 오른쪽·아래 스크롤바를 피한 위치
                if (GUI.Button(b, "▼ 최신으로"))
                {
                    _logFollow = true;
                    _logScroll.y = LogBottomFollowScroll;
                }
            }
        }

        // ------------------------------------------------------------------ 머리 위 이름
        private void DrawEntityLabels()
        {
            if (_sim == null || _cam == null) return;
            foreach (var c in _sim.Combatants)
            {
                CombatantView v;
                if (!_views.TryGetValue(c.EntityId, out v) || v == null) continue;
                Vector3 sp = _cam.WorldToScreenPoint(v.transform.position + Vector3.up * 2.7f);
                if (sp.z < 0) continue;
                // 칸 크기를 글자에 맞춘다(이전: 폭 140·높이 18 고정 → 긴 이름이 두 줄로 접히고 위아래가 잘림)
                _nameContent.text = c.Label + " " + c.Def.DisplayName;
                Vector2 size = _nameTag.CalcSize(_nameContent);
                var r = new Rect(Mathf.Round(sp.x - size.x * 0.5f), Mathf.Round(Screen.height - sp.y - size.y), Mathf.Ceil(size.x), Mathf.Ceil(size.y));
                GUI.color = new Color(0f, 0f, 0f, 0.8f);                       // 1px 그림자: 밝은 바닥 위에서도 읽히게
                GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), _nameContent, _nameTag);
                GUI.color = c.Dead ? Color.gray : Color.white;
                GUI.Label(r, _nameContent, _nameTag);
            }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ 캐릭터 선택 창
        private void DrawPicker(int id)
        {
            GUILayout.Label("캐릭터 추가 → 팀 " + _pickerTeam + "  (이 줄을 끌어 이동)", _bold);
            float dragH = GUILayoutUtility.GetLastRect().yMax;
            GUILayout.BeginHorizontal();
            GUILayout.Label("검색", W(36));
            _pickerSearch = GUILayout.TextField(_pickerSearch ?? "");
            _pickerPcs = GUILayout.Toggle(_pickerPcs, "PC", W(44));
            _pickerMonsters = GUILayout.Toggle(_pickerMonsters, "몬스터", W(64));
            if (GUILayout.Button("닫기", W(48))) _pickerOpen = false;
            GUILayout.EndHorizontal();
            string key = _pickerSearch + "|" + _pickerPcs + "|" + _pickerMonsters;
            bool layout = Event.current.type == EventType.Layout;
            if (key != _pickerCacheKey && layout)
            {
                // 목록은 Layout 단계에서만 다시 만든다(개수 제한 없음 — 보이는 줄만 그리므로 수천 개도 부담 없음)
                _pickerCacheKey = key;
                _pickerList.Clear();
                string q = (_pickerSearch ?? "").Trim();
                foreach (var c in _data.Catalog.CharacterList)
                {
                    if (!c.Enabled) continue;
                    if (c.IsMonster ? !_pickerMonsters : !_pickerPcs) continue;
                    if (q.Length > 0 && c.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && c.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    _pickerList.Add(c);
                }
                _pickerScroll.y = 0f;                                          // 검색이 바뀌면 맨 위부터
            }
            GUILayout.Label("전체 " + _pickerList.Count + "개", _small);
            if (layout)
            {
                _pickerRowH = Mathf.Max(8f, Mathf.Ceil(_pickRow.CalcHeight(PickerProbe, 10000f)));
                int total = _pickerList.Count;
                _pickerFirst = Mathf.Clamp(Mathf.FloorToInt(_pickerScroll.y / _pickerRowH), 0, total);
                _pickerLast = Mathf.Min(total, _pickerFirst + Mathf.CeilToInt(_pickerViewH / _pickerRowH) + 2);
            }
            _pickerScroll = GUILayout.BeginScrollView(_pickerScroll);
            int count = _pickerList.Count;
            int from = Mathf.Min(_pickerFirst, count), to = Mathf.Min(_pickerLast, count);
            if (from > 0) GUILayout.Space(from * _pickerRowH);                // 위쪽 안 보이는 줄 자리
            for (int i = from; i < to; i++)
            {
                var c = _pickerList[i];
                if (GUILayout.Button(c.DisplayName + "  [" + c.Id + "]  Lv" + c.Level + "  HP " + c.Stat(StatId.HP).ToString("0", Inv) + "  " + c.AiProfileId, _pickRow, GUILayout.Height(_pickerRowH)))
                {
                    var team = _scenario.GetOrAddTeam(_pickerTeam);
                    ScenarioMember existing = null;
                    foreach (var m in team.members) if (m.definitionId == c.Id) existing = m;
                    if (existing != null) existing.count++;
                    else team.members.Add(new ScenarioMember { definitionId = c.Id, count = 1 });
                    _pickerOpen = false;
                    _observeRunId = null;
                    if (_mode == SimMode.Observe) PrepareObserve(0);
                }
            }
            if (to < count) GUILayout.Space((count - to) * _pickerRowH);      // 아래쪽 안 보이는 줄 자리(스크롤 길이 유지)
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _pickerViewH = Mathf.Max(_pickerRowH, GUILayoutUtility.GetLastRect().height);
            GUI.DragWindow(new Rect(0, 0, 10000, dragH));
        }
    }
}
