using System;
using System.Collections.Generic;
using ARPG.Core.AI;
using ARPG.Core.Combat;
using ARPG.Core.Events;

namespace ARPG.Core.Simulation
{
    public interface IBattleSimulation
    {
        bool IsFinished { get; }
        long Tick { get; }
        /// <summary>항상 전투 시간 1/tickRate 초 진행. 벽시계·화면 배속을 받지 않는다.</summary>
        void Step();
        BattleResult GetResult();
    }

    /// <summary>
    /// 순수 C# 전투 코어. 한 tick 순서(모듈설계 5장):
    ///  1) 상태 만료·회복·지속 피해, 사망 확정
    ///  2) AI 판단 tick 이면 타겟 갱신 + 행동 의도 수집(같은 시점 상태 기준)
    ///  3) 자원/쿨다운 재검사 후 승인·차감·행동 시작
    ///  4) 행동 타임라인·이동·추적 진행, 타격 시점 도달 수집
    ///  5) 겹침 해소
    ///  6) 수집된 타격을 고정 순서로 동시 해결(동시 교환)
    ///  7) 강제 중단·행동 종료·사망·승패 확정
    /// </summary>
    public sealed class BattleSimulation : IBattleSimulation
    {
        private readonly DataCatalog _cat;
        private readonly CombatRules _r;
        private readonly BattleScenario _scenario;
        private readonly CompositeEventSink _sink = new CompositeEventSink();
        private readonly SkillStatsCollector _skillStats = new SkillStatsCollector();
        private readonly EventHashSink _hash = new EventHashSink();
        private readonly DeterministicRandom _rng;
        private readonly List<Combatant> _all = new List<Combatant>();
        private readonly AiContextAdapter _ctx;
        private readonly long _maxTicks;

        private readonly List<PendingHit> _pendingHits = new List<PendingHit>();
        private readonly List<Decision> _decisions = new List<Decision>();
        private readonly List<Combatant> _interruptList = new List<Combatant>();
        private readonly List<string> _interruptReason = new List<string>();
        private readonly List<int> _interruptPriority = new List<int>();
        private readonly List<PendingState> _pendingStates = new List<PendingState>();
        private readonly List<Combatant> _targetsBuffer = new List<Combatant>();
        private readonly HashSet<int> _aliveTeamsBuffer = new HashSet<int>();
        private readonly List<PendingBuff> _pendingBuffs = new List<PendingBuff>();
        private readonly List<ActiveBuff> _expiredBuffer = new List<ActiveBuff>();

        private long _tick;
        private long _seq;
        private int _actionSeq;
        private bool _finished;
        private BattleResult _result;

        public readonly string RunId;
        public readonly int BattleIndex;
        public readonly ulong Seed;
        public readonly BattleStartInfo StartInfo;

        public IReadOnlyList<Combatant> Combatants { get { return _all; } }
        public long Tick { get { return _tick; } }
        public double TimeSec { get { return _tick / (double)_r.tickRate; } }
        public bool IsFinished { get { return _finished; } }
        public CombatRules Rules { get { return _r; } }
        public long MaxTicks { get { return _maxTicks; } }
        public long RandomDraws { get { return _rng.DrawCount; } }

        private struct PendingHit
        {
            public Combatant Attacker;
            public ActionInstance Action;
            public int HitIndex;
        }

        /// <summary>적중 시(ON_HIT) 버프. 같은 tick 의 타격을 모두 해결한 뒤에 건다(동시 교환 유지).</summary>
        private struct PendingBuff
        {
            public Combatant Caster;
            public SkillDefinition Skill;
            public Combatant HitTarget;
        }

        /// <summary>같은 tick 에 일어난 상태 전환. 그 tick 의 타격을 모두 해결한 뒤 FlushPendingStates 에서 적용한다.</summary>
        private enum PendingStateKind
        {
            Stagger,     // 강인도 붕괴 → 경직
            GuardBreak,  // 가드 스태미나 0 → 기절(그로기)
            Status,      // 상태이상 발동(기절·중독·빙결). 상처는 즉시 피해라 여기 오지 않음
        }

        private struct PendingState
        {
            public PendingStateKind Kind;
            public StatusKind Status;
            public Combatant Target;
            public Combatant Source;
            public string SkillId;
        }

        private sealed class Plan
        {
            public string ActionId;
            public ActionDefinition Def;
            public ActionKind Kind;
            public SkillDefinition Skill;
            public MovesetNode Node;
            public bool IsChain;
            public MoveMode Move;
            public double Speed;
            public double CostHp, CostFp, CostSt;
            public int RuleIndex;
            public string Note;
        }

        private struct Decision
        {
            public Combatant C;
            public Plan P;
        }

        /// <param name="catalog">AI 행 정렬(SortRules)이 끝난 카탈로그</param>
        public BattleSimulation(DataCatalog catalog, CombatRules rules, BattleScenario scenario, string runId,
                                int battleIndex, ulong seed, IBattleEventSink externalSink)
        {
            _cat = catalog;
            _r = rules;
            _scenario = scenario;
            RunId = runId;
            BattleIndex = battleIndex;
            Seed = seed;
            _rng = new DeterministicRandom(seed);
            _ctx = new AiContextAdapter(this);
            double maxSec = scenario.maxBattleSec > 0 ? scenario.maxBattleSec : rules.maxBattleSec;
            _maxTicks = (long)Math.Round(maxSec * rules.tickRate);

            _sink.Add(_skillStats);
            _sink.Add(_hash);
            if (externalSink != null) _sink.Add(externalSink);

            SpawnCombatants();

            StartInfo = new BattleStartInfo
            {
                RunId = runId,
                BattleIndex = battleIndex,
                Seed = seed,
                ScenarioName = scenario.name,
                DataHash = catalog.DataHash,
                RulesVersion = rules.rulesVersion,
                TickRate = rules.tickRate,
            };
            foreach (var c in _all)
            {
                StartInfo.Combatants.Add(new CombatantInfo
                {
                    EntityId = c.EntityId,
                    Label = c.Label,
                    TeamId = c.TeamId,
                    DefinitionId = c.Def.Id,
                    DisplayName = c.Def.DisplayName,
                    IsMonster = c.IsMonster,
                    MaxHp = c.MaxHp,
                    MaxFp = c.MaxFp,
                    MaxStamina = c.MaxStamina,
                });
            }
            _sink.OnBattleStarted(StartInfo);
            var e = NewEvent(BattleEventType.BattleStarted, -1);
            e.Result = scenario.name;
            e.Reason = "seed=" + seed + " data=" + catalog.DataHash + " rules=" + rules.rulesVersion;
            Emit(ref e);

            foreach (var c in _all) UpdatePhase(c, true);
        }

        // ------------------------------------------------------------------ 생성
        private void SpawnCombatants()
        {
            var teams = new List<ScenarioTeam>(_scenario.teams);
            teams.Sort((a, b) => a.teamId.CompareTo(b.teamId));
            double half = (_scenario.spawnDistance > 0 ? _scenario.spawnDistance : _r.spawnDistance) * 0.5;
            double spacing = _scenario.spawnSpacing > 0 ? _scenario.spawnSpacing : _r.spawnSpacing;
            int id = 0;
            for (int ti = 0; ti < teams.Count; ti++)
            {
                var team = teams[ti];
                var defs = new List<CharacterDefinition>();
                foreach (var m in team.members)
                {
                    CharacterDefinition def;
                    if (m.count <= 0 || !_cat.Characters.TryGetValue(m.definitionId ?? string.Empty, out def) || !def.Enabled) continue;
                    for (int k = 0; k < m.count; k++) defs.Add(def);
                }
                if (defs.Count == 0) continue;
                double side = (ti % 2 == 0) ? -1 : 1;
                double rowOffset = (ti / 2) * 4.0;
                char letter = (char)('A' + Math.Min(team.teamId, 25));
                for (int k = 0; k < defs.Count; k++)
                {
                    var def = defs[k];
                    double z = (k - (defs.Count - 1) * 0.5) * spacing;
                    var c = new Combatant
                    {
                        EntityId = id++,
                        Label = letter + (k + 1).ToString("00"),
                        TeamId = team.teamId,
                        Def = def,
                        IsMonster = def.IsMonster,
                        BaseStats = def.Stats,
                        Stats = (double[])def.Stats.Clone(), // 버프가 바꾸는 개체 전용 복사본(정의는 수정하지 않음)
                        Radius = _r.bodyRadius,
                        Pos = new Vec2(side * (half + rowOffset), z),
                        Facing = new Vec2(-side, 0),
                    };
                    c.PrevPos = c.Pos;
                    c.PrevFacing = c.Facing;
                    c.MaxHp = Math.Max(1, def.Stat(StatId.HP));
                    c.Hp = c.MaxHp;
                    c.MaxFp = Math.Max(0, def.Stat(StatId.FP));
                    c.Fp = c.MaxFp;
                    c.MaxStamina = Math.Max(0, def.Stat(StatId.STAMINA));
                    c.Stamina = c.MaxStamina;
                    c.StaminaRegenPerSec = Math.Max(0, def.Stat(StatId.STAMINA_RECOVERY));
                    c.ResourcesManaged = !(c.IsMonster && _r.monsterIgnoreSkillCosts);
                    c.MaxPoise = Math.Max(0, def.Stat(StatId.STAGGER_ENDURANCE));
                    c.Poise = c.MaxPoise;
                    _cat.AiProfiles.TryGetValue(def.AiProfileId ?? string.Empty, out c.Ai);
                    _all.Add(c);
                }
            }
        }

        // ------------------------------------------------------------------ Step
        public void Step()
        {
            if (_finished) return;
            long t = _tick;
            for (int i = 0; i < _all.Count; i++)
            {
                _all[i].PrevPos = _all[i].Pos;
                _all[i].PrevFacing = _all[i].Facing;
            }

            UpdateTimers(t);
            ConfirmDeaths(t, "상태이상");
            if (CheckEnd(t)) return;

            if (t % Math.Max(1, _r.aiTickInterval) == 0)
            {
                UpdateTargets(t);
                CollectDecisions(t);
            }
            ApplyDecisions(t);
            AdvanceActions(t);
            ResolveOverlaps();
            ResolveHits(t);
            ApplyInterrupts(t);
            FinishActions(t);
            ConfirmDeaths(t, "피격");
            if (CheckEnd(t)) return;

            _tick = t + 1;
            if (_tick >= _maxTicks) EndBattle(BattleEndReason.Timeout, -1);
        }

        /// <summary>사용자 중단. 완료 결과와 구별되도록 UserAborted 로 종료한다.</summary>
        public void Abort()
        {
            if (!_finished) EndBattle(BattleEndReason.UserAborted, -1);
        }

        public BattleResult GetResult()
        {
            if (!_finished) throw new InvalidOperationException("전투가 끝나지 않았습니다");
            return _result;
        }

        public ulong CurrentEventHash { get { return _hash.Hash; } }

        // ------------------------------------------------------------------ 1) 시간 처리
        private void UpdateTimers(long t)
        {
            double decay = _r.statusDecayPerSec / _r.tickRate;
            int secTicks = _r.SecToTicks(1.0);
            for (int i = 0; i < _all.Count; i++)
            {
                var c = _all[i];
                if (c.Dead) continue;

                // 버프 지속 시간 만료(이번 tick 의 판단·타격은 해제된 능력치로 계산)
                ExpireBuffs(c, t);

                // [확정] 누적 게이지: 쌓인 직후부터 초당 5 감소, 0 미만 없음. 같은 tick 신규 누적분은 다음 tick 부터 감소.
                for (int k = 0; k < c.Buildup.Length; k++)
                {
                    if (c.Buildup[k] > 0) c.Buildup[k] = Math.Max(0, c.Buildup[k] - decay);
                }

                // [확정] 중독: 발동 1초 뒤 첫 피해, 이후 1초 간격, 마지막 피해 후 만료
                if (c.PoisonTicksLeft > 0 && t >= c.PoisonNextTick)
                {
                    double before = c.Hp;
                    double dmg = c.MaxHp * _r.poisonMaxHpRatioPerSec;
                    c.Hp = Math.Max(0, c.Hp - dmg);
                    c.DamageTaken += before - c.Hp;
                    var e = NewEvent(BattleEventType.StatusTick, c.EntityId);
                    e.StatusId = "POISON";
                    e.Damage = dmg;
                    e.ActorHpBefore = before;
                    e.ActorHpAfter = c.Hp;
                    e.Reason = "남은 " + (c.PoisonTicksLeft - 1);
                    Emit(ref e);
                    c.PoisonTicksLeft--;
                    c.PoisonNextTick += secTicks;
                    if (c.PoisonTicksLeft <= 0)
                    {
                        c.PoisonNextTick = -1;
                        EmitStatus(BattleEventType.StatusExpired, c, "POISON", "지속 종료");
                    }
                }

                if (c.FrostEndTick >= 0 && t >= c.FrostEndTick)
                {
                    c.FrostEndTick = -1;
                    EmitStatus(BattleEventType.StatusExpired, c, "FROST", "지속 종료");
                }

                if (c.StunUntilTick >= 0 && t >= c.StunUntilTick)
                {
                    c.StunUntilTick = -1;
                    c.StunFromGuardBreak = false;
                    EmitStatus(BattleEventType.StatusExpired, c, "STUN", "기절 종료");
                }

                if (c.StaggerUntilTick >= 0 && t >= c.StaggerUntilTick)
                {
                    c.StaggerUntilTick = -1;
                    c.PoiseBrokenUntilTick = t + _r.SecToTicks(_r.poiseBrokenWindowSec);
                }

                // [기획] 강인도는 마지막 피격 5초 후 회복 시작
                if (c.Poise < c.MaxPoise && c.PoiseRecoverTick >= 0 && t >= c.PoiseRecoverTick)
                {
                    if (_r.poiseRecoveryPerSec < 0) c.Poise = c.MaxPoise;
                    else c.Poise = Math.Min(c.MaxPoise, c.Poise + _r.poiseRecoveryPerSec / _r.tickRate);
                    if (c.Poise >= c.MaxPoise) c.PoiseRecoverTick = -1;
                }

                // 스태미나 회복(음수에서도 회복. 바 표시만 0으로 제한)
                if (c.ResourcesManaged && c.StaminaRegenPerSec > 0 && c.Stamina < c.MaxStamina)
                {
                    bool acting = c.IsLocked(t) || (c.Action != null && !c.Action.IsSoft);
                    if (!acting || _r.staminaRegenWhileActing)
                        c.Stamina = Math.Min(c.MaxStamina, c.Stamina + c.StaminaRegenPerSec / _r.tickRate);
                }
            }
        }

        // ------------------------------------------------------------------ 2) 타겟/AI
        private void UpdateTargets(long t)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var c = _all[i];
                if (c.Dead) continue;
                Combatant nearest = null;
                double nd = double.MaxValue;
                for (int j = 0; j < _all.Count; j++)
                {
                    var e = _all[j];
                    if (e.Dead || e.TeamId == c.TeamId) continue;
                    double d = Vec2.Distance(c.Pos, e.Pos);
                    if (d < nd - 1e-12) { nd = d; nearest = e; }
                }
                Combatant cur = c.Target;
                Combatant next;
                if (cur != null && !cur.Dead)
                {
                    double cd = Vec2.Distance(c.Pos, cur.Pos);
                    if (cd > _r.aggroReleaseRange) next = (nearest != null && nd <= _r.perceptionRange) ? nearest : null;
                    else if (nearest != null && nearest != cur && nd < cd * _r.targetSwitchRatio) next = nearest;
                    else next = cur;
                }
                else next = (nearest != null && nd <= _r.perceptionRange) ? nearest : null;

                if (next != cur)
                {
                    c.Target = next;
                    var e = NewEvent(BattleEventType.TargetChanged, c.EntityId);
                    e.TargetId = next != null ? next.EntityId : -1;
                    e.DistanceM = next != null ? Vec2.Distance(c.Pos, next.Pos) : double.NaN;
                    Emit(ref e);
                }
                c.Perceived = c.Target != null;
            }
        }

        private bool CanDecide(Combatant c, long t)
        {
            if (c.Dead || c.IsLocked(t) || c.Ai == null) return false;
            return c.Action == null || c.Action.InCancelWindow;
        }

        private void CollectDecisions(long t)
        {
            _decisions.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                var c = _all[i];
                if (!CanDecide(c, t)) continue;
                UpdatePhase(c, false);
                var p = Decide(c, t);
                if (p != null) _decisions.Add(new Decision { C = c, P = p });
            }
        }

        private void UpdatePhase(Combatant c, bool initial)
        {
            if (c.Ai == null || c.Ai.Phases.Count == 0)
            {
                c.Phase = 1;
                c.PhaseDmgMult = 1;
                c.PhaseSpeedMult = 1;
                return;
            }
            double hp = c.HpPct;
            AiPhase found = null;
            foreach (var p in c.Ai.Phases)
            {
                if (hp >= p.HpMinPct - 1e-9 && hp <= p.HpMaxPct + 1e-9) { found = p; break; }
            }
            if (found == null) return; // 구간 사이(예: 50~51%)는 현재 페이즈 유지
            if (!initial && found.PhaseNo == c.Phase) return;
            bool changed = found.PhaseNo != c.Phase || initial;
            c.Phase = found.PhaseNo;
            c.PhaseDmgMult = found.DmgMult;
            c.PhaseSpeedMult = found.SpeedMult;
            if (!changed) return;
            var e = NewEvent(BattleEventType.PhaseChanged, c.EntityId);
            e.Result = "PHASE_" + found.PhaseNo;
            e.Reason = (initial ? "시작 " : "") + found.Name + " HP " + hp.ToString("0.0") + "% dmg×" + found.DmgMult + " speed×" + found.SpeedMult;
            e.ActorHpBefore = c.Hp;
            e.ActorHpAfter = c.Hp;
            Emit(ref e);
            if (!initial && found.EnterActionId != ActionIds.NONE) c.PendingForcedActionId = found.EnterActionId;
        }

        private Plan Decide(Combatant c, long t)
        {
            _ctx.Bind(c, t);
            if (c.PendingForcedActionId != null)
            {
                string forced = c.PendingForcedActionId;
                c.PendingForcedActionId = null;
                string fail;
                var fp = BuildPlan(c, forced, null, true, t, out fail);
                if (fp != null)
                {
                    fp.RuleIndex = 0;
                    fp.Note = "페이즈 진입 강제 행동";
                    return fp;
                }
            }
            var rules = c.Ai.Rules;
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Enabled) continue;
                if (rule.PhaseNo != 0 && rule.PhaseNo != c.Phase) continue;
                if (!rule.Condition1.Evaluate(_ctx) || !rule.Condition2.Evaluate(_ctx)) continue;
                if (rule.ChancePct <= 0) continue;
                if (IsOnCooldown(c, rule.ActionId, t)) continue;
                string fail;
                var p = BuildPlan(c, rule.ActionId, rule.ForcedSkillId, false, t, out fail);
                if (p == null) continue; // [확정] 자원 등 실행 불가 행동은 후보에서 제외 → 다음 행
                if (rule.ChancePct < 100)
                {
                    double roll = _rng.NextUnit() * 100.0;
                    if (roll >= rule.ChancePct) continue; // 폭포: 확률 실패 시 다음 행
                }
                p.RuleIndex = rule.BehaviorIndex;
                p.Note = rule.Note;
                return p;
            }
            string f2;
            var idle = BuildPlan(c, ActionIds.IDLE, null, true, t, out f2);
            if (idle != null)
            {
                idle.RuleIndex = 0;
                idle.Note = "후보 없음 대기";
            }
            return idle;
        }

        internal bool IsOnCooldown(Combatant c, string actionId, long t)
        {
            long ready;
            return c.CooldownReadyTick.TryGetValue(actionId, out ready) && t < ready;
        }

        private string ComboPrev(Combatant c, long t)
        {
            var a = c.Action;
            if (a != null && a.Kind == ActionKind.Skill && a.InCancelWindow && a.NodeId != null) return a.NodeId;
            if (a == null && t <= c.ComboExpireTick) return c.ComboNodeId;
            return "NONE";
        }

        private bool IsInstantRanged(SkillDefinition s) { return s.EffectiveRange > _r.instantRangedThreshold; }

        private Plan BuildPlan(Combatant c, string actionId, string forcedSkillId, bool ignoreRange, long t, out string fail)
        {
            fail = null;
            ActionDefinition def;
            _cat.Actions.TryGetValue(actionId ?? string.Empty, out def);
            if (def == null && actionId != ActionIds.IDLE) { fail = "ACTION_DEF 없음"; return null; }
            if (def != null && !def.Enabled) { fail = "비활성 행동"; return null; }
            var p = new Plan { ActionId = actionId, Def = def };
            bool ignoreCosts = c.IsMonster && _r.monsterIgnoreSkillCosts;

            if ((def != null && def.IsInput) || forcedSkillId != null)
            {
                SkillDefinition sk = null;
                MovesetNode node = null;
                bool chain = false;
                if (forcedSkillId != null) _cat.Skills.TryGetValue(forcedSkillId, out sk);
                else
                {
                    node = _cat.ResolveMoveset(c.Def.MovesetId, ComboPrev(c, t), def.InputSlot, out chain);
                    if (node == null) { fail = "무브셋 슬롯 없음 " + def.InputSlot; return null; }
                    sk = node.Skill;
                }
                if (sk == null) { fail = "스킬 없음"; return null; }
                if (!sk.Enabled || !sk.Valid) { fail = "스킬 비활성/검증 실패 " + sk.Id; return null; }
                p.Skill = sk;
                p.Node = node;
                p.IsChain = chain;
                p.Kind = sk.Type == SkillType.DEFENCE ? ActionKind.Block : ActionKind.Skill;
                if (!ignoreCosts)
                {
                    p.CostHp = sk.CostHp;
                    p.CostFp = sk.CostFp;
                    p.CostSt = sk.CostStamina;
                }
                if (p.Kind == ActionKind.Skill && sk.IsAttack && sk.HasHits && !ignoreRange)
                {
                    var tg = c.Target;
                    if (tg == null || tg.Dead) { fail = "대상 없음"; return null; }
                    double d = Vec2.Distance(c.Pos, tg.Pos);
                    double reach = c.Radius + (IsInstantRanged(sk) ? sk.EffectiveRange : sk.Reach) + tg.Radius;
                    if (d > reach + 1e-9) { fail = "사거리 밖"; return null; }
                }
            }
            else
            {
                bool hasTarget = c.Target != null && !c.Target.Dead;
                switch (actionId)
                {
                    case ActionIds.IDLE:
                        p.Kind = ActionKind.Idle;
                        break;
                    case ActionIds.WALK_APPROACH:
                        if (!hasTarget) { fail = "대상 없음"; return null; }
                        p.Kind = ActionKind.Move; p.Move = MoveMode.Approach; p.Speed = _r.walkSpeed;
                        break;
                    case ActionIds.RUN_APPROACH:
                        if (!hasTarget) { fail = "대상 없음"; return null; }
                        p.Kind = ActionKind.Move; p.Move = MoveMode.Approach; p.Speed = _r.runSpeed;
                        break;
                    case ActionIds.STRAFE_L:
                        if (!hasTarget) { fail = "대상 없음"; return null; }
                        p.Kind = ActionKind.Move; p.Move = MoveMode.StrafeLeft; p.Speed = _r.strafeSpeed;
                        break;
                    case ActionIds.STRAFE_R:
                        if (!hasTarget) { fail = "대상 없음"; return null; }
                        p.Kind = ActionKind.Move; p.Move = MoveMode.StrafeRight; p.Speed = _r.strafeSpeed;
                        break;
                    case ActionIds.KEEP_DISTANCE:
                        if (!hasTarget) { fail = "대상 없음"; return null; }
                        p.Kind = ActionKind.Move; p.Move = MoveMode.Away; p.Speed = _r.keepDistanceSpeed;
                        break;
                    case ActionIds.BACKSTEP:
                        p.Kind = ActionKind.Backstep;
                        if (!ignoreCosts) p.CostSt = _r.backstepStaminaCost;
                        break;
                    case ActionIds.DODGE:
                        p.Kind = ActionKind.Dodge;
                        if (!ignoreCosts) p.CostSt = _r.dodgeStaminaCost;
                        break;
                    default:
                        fail = "미구현 BUILTIN " + actionId; // USE_POTION 등
                        return null;
                }
            }

            var chk = ResourceRules.Check(c.Hp, c.Fp, c.Stamina, p.CostHp, p.CostFp, p.CostSt, _r);
            if (chk != ResourceCheck.Ok)
            {
                fail = ResourceRules.Describe(chk);
                c.ResourceBlockedCount++;
                return null;
            }
            return p;
        }

        // ------------------------------------------------------------------ 3) 승인·시작
        private void ApplyDecisions(long t)
        {
            for (int i = 0; i < _decisions.Count; i++)
            {
                var d = _decisions[i];
                var c = d.C;
                if (c.Dead || c.IsLocked(t)) continue;
                if (c.Action != null && !c.Action.InCancelWindow) continue;
                if (c.Action != null && d.P.Kind == ActionKind.Idle && _r.idleDoesNotCancel) continue;
                var chk = ResourceRules.Check(c.Hp, c.Fp, c.Stamina, d.P.CostHp, d.P.CostFp, d.P.CostSt, _r);
                if (chk != ResourceCheck.Ok)
                {
                    var e = NewEvent(BattleEventType.ActionRejected, c.EntityId);
                    e.ActionId = d.P.ActionId;
                    e.SkillId = d.P.Skill != null ? d.P.Skill.Id : null;
                    e.Result = "REJECTED";
                    e.Reason = ResourceRules.Describe(chk);
                    FillActor(ref e, c);
                    Emit(ref e);
                    continue;
                }
                StartAction(c, d.P, t);
            }
            _decisions.Clear();
        }

        private void StartAction(Combatant c, Plan p, long t)
        {
            if (c.Action != null) EndAction(c, c.Action, t, BattleEventType.ActionEnded, "캔슬 연결");

            double hpB = c.Hp, fpB = c.Fp, stB = c.Stamina;
            c.Hp -= p.CostHp;
            c.Fp -= p.CostFp;
            c.Stamina -= p.CostSt; // [확정] 전체 비용 차감, 음수 보존
            c.StaminaSpent += p.CostSt;

            var a = new ActionInstance
            {
                InstanceId = ++_actionSeq,
                ActionId = p.ActionId,
                Kind = p.Kind,
                Skill = p.Skill,
                NodeId = p.Node != null ? p.Node.NodeId : null,
                IsComboChain = p.IsChain,
                AiRuleIndex = p.RuleIndex,
                Reason = p.Note,
                StartTick = t,
                Move = p.Move,
                MoveSpeed = p.Speed,
                StartFacing = c.Facing,
                TargetAtStart = c.Target,
            };
            var tg = c.Target != null && !c.Target.Dead ? c.Target : null;
            Vec2 toTarget = tg != null ? (tg.Pos - c.Pos).Normalized : Vec2.Zero;
            Vec2 away = toTarget.SqrLength > 0 ? -toTarget : -c.Facing;
            switch (p.Kind)
            {
                case ActionKind.Idle:
                case ActionKind.Move:
                    a.DurationMs = _r.softActionSec * 1000.0;
                    break;
                case ActionKind.Backstep:
                    a.DurationMs = _r.backstepSec * 1000.0;
                    a.FixedDir = away;
                    a.TotalDistance = _r.backstepDistance;
                    break;
                case ActionKind.Dodge:
                    a.DurationMs = _r.dodgeSec * 1000.0;
                    a.FixedDir = away;
                    a.TotalDistance = _r.dodgeDistance;
                    a.IFrameStartMs = _r.dodgeIFrameStartSec * 1000.0;
                    a.IFrameEndMs = _r.dodgeIFrameEndSec * 1000.0;
                    break;
                case ActionKind.Skill:
                    a.DurationMs = Math.Max(p.Skill.AniTimeMs, _r.TickMs);
                    a.CancelMs = p.Skill.CancelTimeMs; // [확정] 0 = 끝까지 수행
                    a.TotalDistance = p.Skill.MoveDistance;
                    a.ChargeMs = p.Skill.IsChargeAttack ? Math.Max(0, _r.aiChargeMs) : 0;
                    a.ElapsedMs = -a.ChargeMs;
                    if (toTarget.SqrLength > 0) c.Facing = toTarget;
                    a.StartFacing = c.Facing;
                    break;
                case ActionKind.Block:
                    a.DurationMs = p.Skill.AniTimeMs > 0 ? p.Skill.AniTimeMs : _r.blockHoldSecWhenAniTimeZero * 1000.0;
                    a.CancelMs = p.Skill.CancelTimeMs;
                    if (toTarget.SqrLength > 0) c.Facing = toTarget;
                    a.StartFacing = c.Facing;
                    break;
            }
            c.Action = a;

            if (p.Def != null && p.Def.CooldownSec > 0)
                c.CooldownReadyTick[p.ActionId] = t + _r.SecToTicks(p.Def.CooldownSec);
            c.RecentActions.Add(p.ActionId);
            while (c.RecentActions.Count > Math.Max(1, _r.recentActionMemory)) c.RecentActions.RemoveAt(0);
            c.LastActionId = p.ActionId;
            c.ComboNodeId = p.Node != null ? p.Node.NodeId : "NONE";
            c.ComboExpireTick = -1;
            c.LastAiRule = p.RuleIndex;
            c.LastAiNote = p.Note ?? string.Empty;

            var e = NewEvent(BattleEventType.ActionStarted, c.EntityId);
            e.ActionId = p.ActionId;
            e.SkillId = p.Skill != null ? p.Skill.Id : null;
            e.TargetId = tg != null ? tg.EntityId : -1;
            e.DistanceM = tg != null ? Vec2.Distance(c.Pos, tg.Pos) : double.NaN;
            e.ActorHpBefore = hpB; e.ActorHpAfter = c.Hp;
            e.ActorFpBefore = fpB; e.ActorFpAfter = c.Fp;
            e.ActorStaminaBefore = stB; e.ActorStaminaAfter = c.Stamina;
            e.Result = p.IsChain ? "COMBO" : "START";
            e.Reason = (p.RuleIndex > 0 ? "AI#" + p.RuleIndex + " " : "") + (p.Note ?? string.Empty);
            Emit(ref e);

            // 시전 시(ON_CAST) 버프: 비용 차감·행동 시작 직후
            if (p.Skill != null && p.Skill.Buff != null && p.Skill.EffectiveBuffTrigger == BuffTrigger.OnCast)
                TriggerBuff(c, p.Skill, null, t);
        }

        private void EndAction(Combatant c, ActionInstance a, long t, BattleEventType type, string reason)
        {
            if (c.Action == a) c.Action = null;
            var e = NewEvent(type, c.EntityId);
            e.ActionId = a.ActionId;
            e.SkillId = a.Skill != null ? a.Skill.Id : null;
            e.Result = type == BattleEventType.ActionInterrupted ? "INTERRUPTED" : "END";
            e.Reason = reason;
            Emit(ref e);
        }

        // ------------------------------------------------------------------ 4) 진행·이동·타격 수집
        private double ActionSpeed(Combatant c, ActionInstance a, long t)
        {
            bool frost = c.FrostActive(t);
            switch (a.Kind)
            {
                case ActionKind.Skill:
                case ActionKind.Block:
                    return (a.Skill != null ? a.Skill.AniSpeed : 1.0) * c.PhaseSpeedMult * (frost ? _r.frostSpeedMult : 1.0);
                case ActionKind.Dodge:
                    return frost ? _r.frostSpeedMult : 1.0; // [확정] 빙결: 이동·공격·구르기만 감속
                default:
                    return 1.0;
            }
        }

        private static double Frac(double ms, double total)
        {
            if (total <= 0) return 1;
            return Math.Max(0, Math.Min(1, ms / total));
        }

        private void AdvanceActions(long t)
        {
            _pendingHits.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                var c = _all[i];
                if (c.Dead) continue;
                var a = c.Action;
                if (a == null || c.IsLocked(t)) continue;
                bool frost = c.FrostActive(t);
                double prev = a.ElapsedMs;
                a.RealElapsedMs += _r.TickMs; // 구르기 무적은 빙결과 무관한 논리 경과 시간 기준
                double now = prev + _r.TickMs * ActionSpeed(c, a, t);
                a.ElapsedMs = now;
                a.HasAdvanced = true;
                var tg = c.Target != null && !c.Target.Dead ? c.Target : null;

                switch (a.Kind)
                {
                    case ActionKind.Idle:
                        FaceTarget(c, tg);
                        break;
                    case ActionKind.Move:
                        MoveSoft(c, a, tg, frost);
                        break;
                    case ActionKind.Backstep:
                    {
                        double delta = a.TotalDistance * (Frac(now, a.DurationMs) - Frac(prev, a.DurationMs)) * (frost ? _r.frostSpeedMult : 1.0);
                        c.Pos = c.Pos + a.FixedDir * delta;
                        FaceTarget(c, tg);
                        break;
                    }
                    case ActionKind.Dodge:
                    {
                        double delta = a.TotalDistance * (Frac(now, a.DurationMs) - Frac(prev, a.DurationMs));
                        c.Pos = c.Pos + a.FixedDir * delta;
                        break;
                    }
                    case ActionKind.Block:
                        FaceTarget(c, tg);
                        break;
                    case ActionKind.Skill:
                        AdvanceSkill(c, a, tg, prev, now);
                        break;
                }
            }
        }

        private static void FaceTarget(Combatant c, Combatant tg)
        {
            if (tg == null) return;
            var d = (tg.Pos - c.Pos).Normalized;
            if (d.SqrLength > 0) c.Facing = d;
        }

        private void MoveSoft(Combatant c, ActionInstance a, Combatant tg, bool frost)
        {
            if (tg == null) return;
            Vec2 to = tg.Pos - c.Pos;
            double d = to.Length;
            Vec2 dir = d > 1e-9 ? to * (1.0 / d) : c.Facing;
            double step = a.MoveSpeed * _r.TickSec * (frost ? _r.frostSpeedMult : 1.0);
            switch (a.Move)
            {
                case MoveMode.Approach:
                {
                    double stop = c.Radius + tg.Radius + _r.approachStopSurfaceDist;
                    double room = d - stop;
                    if (room > 0) c.Pos = c.Pos + dir * Math.Min(step, room);
                    break;
                }
                case MoveMode.StrafeLeft:
                case MoveMode.StrafeRight:
                {
                    Vec2 tangent = a.Move == MoveMode.StrafeLeft ? dir.PerpLeft : -dir.PerpLeft;
                    Vec2 np = c.Pos + tangent * step;
                    Vec2 back = (np - tg.Pos).Normalized;
                    if (back.SqrLength > 0 && d > 1e-9) np = tg.Pos + back * d; // 같은 반경 유지
                    c.Pos = np;
                    break;
                }
                case MoveMode.Away:
                    c.Pos = c.Pos - dir * step;
                    break;
            }
            FaceTarget(c, tg);
        }

        private void AdvanceSkill(Combatant c, ActionInstance a, Combatant tg, double prev, double now)
        {
            var s = a.Skill;
            // 추적: Tracking_Start~End 구간에서 대상 방향으로 회전(시작 방향 기준 ±Tracking_Angle/2)
            if (tg != null && now >= s.TrackingStartMs - 1e-6 && prev <= s.TrackingEndMs + 1e-6 && s.TrackingAngleDeg > 0)
            {
                Vec2 want = (tg.Pos - c.Pos).Normalized;
                if (want.SqrLength > 0)
                {
                    double limit = s.TrackingAngleDeg * 0.5;
                    double ang = Vec2.SignedAngleDeg(a.StartFacing, want);
                    if (Math.Abs(ang) > limit) want = a.StartFacing.Rotate(Math.Sign(ang) * limit).Normalized;
                    c.Facing = want;
                }
            }
            // 전진(Move_Distance): 시작 ~ 첫 타격 시점 동안 정면으로 이동, 대상과 겹치지 않게 제한
            if (a.TotalDistance > 0)
            {
                double end = s.FirstHitMs > 0 ? s.FirstHitMs : a.DurationMs;
                double delta = a.TotalDistance * (Frac(now, end) - Frac(Math.Max(prev, 0), end));
                if (delta > 0)
                {
                    if (tg != null)
                    {
                        double gap = Vec2.Distance(c.Pos, tg.Pos) - (c.Radius + tg.Radius);
                        double along = Vec2.Dot(c.Facing, (tg.Pos - c.Pos).Normalized);
                        if (along > 0.5) delta = Math.Min(delta, Math.Max(0, gap));
                    }
                    c.Pos = c.Pos + c.Facing * delta;
                }
            }
            // 타격 시점 도달 검사: 이전 진행 < 시점 <= 현재 진행. 한 tick 에 여러 시점을 넘으면 모두 처리.
            if (s.IsAttack && s.HasHits)
            {
                var hits = s.HitTimesMs;
                while (a.NextHit < hits.Length && hits[a.NextHit] <= now + 1e-6)
                {
                    _pendingHits.Add(new PendingHit { Attacker = c, Action = a, HitIndex = a.NextHit });
                    a.NextHit++;
                }
            }
        }

        // ------------------------------------------------------------------ 5) 겹침 해소
        private void ResolveOverlaps()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var a = _all[i];
                if (a.Dead) continue;
                for (int j = i + 1; j < _all.Count; j++)
                {
                    var b = _all[j];
                    if (b.Dead) continue;
                    Vec2 delta = b.Pos - a.Pos;
                    double d = delta.Length;
                    double min = a.Radius + b.Radius;
                    if (d >= min - 1e-9) continue;
                    Vec2 n = d > 1e-9 ? delta * (1.0 / d) : new Vec2(1, 0);
                    double push = (min - d) * 0.5;
                    a.Pos = a.Pos - n * push;
                    b.Pos = b.Pos + n * push;
                }
            }
            double h = _r.arenaHalfSize;
            for (int i = 0; i < _all.Count; i++)
            {
                var c = _all[i];
                c.Pos = new Vec2(Math.Max(-h, Math.Min(h, c.Pos.x)), Math.Max(-h, Math.Min(h, c.Pos.y)));
            }
        }

        // ------------------------------------------------------------------ 6) 타격 해결(동시 교환)
        private void ResolveHits(long t)
        {
            // _pendingHits 는 공격자 EntityId → 행동 인스턴스 → 타격 번호 순으로 수집됨.
            for (int i = 0; i < _pendingHits.Count; i++)
            {
                var ph = _pendingHits[i];
                var A = ph.Attacker;
                var s = ph.Action.Skill;
                GatherTargets(A, ph.Action, s, _targetsBuffer);
                if (_targetsBuffer.Count == 0)
                {
                    var e = NewEvent(BattleEventType.HitResolved, A.EntityId);
                    e.ActionId = ph.Action.ActionId;
                    e.SkillId = s.Id;
                    e.HitIndex = ph.HitIndex + 1;
                    e.Result = "MISS";
                    var tg = ph.Action.TargetAtStart;
                    if (tg != null) { e.TargetId = tg.EntityId; e.DistanceM = Vec2.Distance(A.Pos, tg.Pos); }
                    Emit(ref e);
                    continue;
                }
                for (int k = 0; k < _targetsBuffer.Count; k++) ApplyHit(A, ph.Action, s, ph.HitIndex, _targetsBuffer[k], t);
            }
            _pendingHits.Clear();
            // [확정 2026-11-03] 상태 전환은 이 tick 의 타격을 모두 해결한 뒤 일괄 적용(같은 tick 의 다른 타격은 tick 시작 상태로 계산)
            FlushPendingStates(t);
            FlushPendingBuffs(t);
        }

        private void GatherTargets(Combatant A, ActionInstance a, SkillDefinition s, List<Combatant> outList)
        {
            outList.Clear();
            if (IsInstantRanged(s))
            {
                var P = a.TargetAtStart != null && !a.TargetAtStart.Dead ? a.TargetAtStart : A.Target;
                if (P == null || P.Dead) return;
                Vec2 to = P.Pos - A.Pos;
                double d = to.Length;
                if (d > A.Radius + s.EffectiveRange + P.Radius + 1e-9) return;
                if (d > 1e-9 && Vec2.Dot(A.Facing, to * (1.0 / d)) < 0) return; // 전방 반원
                outList.Add(P);
                for (int i = 0; i < _all.Count; i++)
                {
                    var e = _all[i];
                    if (e == P || e.Dead || e.TeamId == A.TeamId) continue;
                    if (Vec2.Distance(e.Pos, P.Pos) <= s.HitRange + e.Radius + 1e-9) outList.Add(e);
                }
                outList.Sort((x, y) => x.EntityId.CompareTo(y.EntityId));
                return;
            }
            Vec2 center = A.Pos + A.Facing * (A.Radius + Math.Max(s.EffectiveRange - s.HitRange, 0));
            for (int i = 0; i < _all.Count; i++)
            {
                var e = _all[i];
                if (e.Dead || e.TeamId == A.TeamId) continue;
                if (Vec2.Distance(center, e.Pos) <= s.HitRange + e.Radius + 1e-9) outList.Add(e);
            }
        }

        /// <summary>근접 판정 원(화면 표시용). 즉발 원거리는 대상 중심.</summary>
        public bool TryGetHitZone(Combatant A, out Vec2 center, out double radius)
        {
            center = A.Pos;
            radius = 0;
            var a = A.Action;
            if (a == null || !a.IsAttackWithHits) return false;
            var s = a.Skill;
            if (IsInstantRanged(s))
            {
                var P = a.TargetAtStart != null && !a.TargetAtStart.Dead ? a.TargetAtStart : A.Target;
                if (P == null) return false;
                center = P.Pos;
                radius = s.HitRange;
                return true;
            }
            center = A.Pos + A.Facing * (A.Radius + Math.Max(s.EffectiveRange - s.HitRange, 0));
            radius = s.HitRange;
            return true;
        }

        private bool InGuardArc(Combatant defender, Vec2 attackerPos)
        {
            Vec2 to = attackerPos - defender.Pos;
            if (to.SqrLength < 1e-12) return true;
            double ang = Math.Abs(Vec2.SignedAngleDeg(defender.Facing, to.Normalized));
            return ang <= _r.guardFrontAngleDeg * 0.5 + 1e-9;
        }

        private void ApplyHit(Combatant A, ActionInstance a, SkillDefinition s, int hitIndex, Combatant T, long t)
        {
            double mv = s.MotionValues[hitIndex];
            double dist = Vec2.Distance(A.Pos, T.Pos);
            var e = NewEvent(BattleEventType.HitResolved, A.EntityId);
            e.TargetId = T.EntityId;
            e.ActionId = a.ActionId;
            e.SkillId = s.Id;
            e.HitIndex = hitIndex + 1;
            e.DistanceM = dist;

            // 무적(회피 프레임): 상호작용 없음
            if (T.InIFrame)
            {
                T.HitsAvoided++;
                e.Type = BattleEventType.HitAvoided;
                e.Result = "IFRAME";
                e.TargetHpBefore = T.Hp;
                e.TargetHpAfter = T.Hp;
                Emit(ref e);
                return;
            }

            bool groggy = T.IsStunned(t);
            double rnd = _rng.Range(_r.randomMin, DamageFormula.RandomUpper(A.Stats, _r));
            var dmg = DamageFormula.Compute(A.Stats, T.Stats, s, mv, rnd, a.ChargeMs, dist, groggy, A.PhaseDmgMult, _r);
            e.DamageType = dmg.PrimaryType;
            e.Damage = dmg.Total;
            double hpBefore = T.Hp;
            e.TargetHpBefore = hpBefore;

            // 가드: HP 피해와 스태미나 피해를 분리
            if (T.IsGuarding && !T.IsStunned(t) && InGuardArc(T, A.Pos))
            {
                double hpD = dmg.Total * DamageFormula.GuardHpRatio(T.Stats, s, _r);
                T.Hp = Math.Max(0, T.Hp - hpD);
                double applied = hpBefore - T.Hp;
                A.DamageDealt += applied;
                T.DamageTaken += applied;
                T.HitsGuarded++;
                e.Type = BattleEventType.Guarded;
                e.Damage = hpD;
                e.TargetHpAfter = T.Hp;
                e.Result = "GUARD";
                e.Reason = "원피해 " + dmg.Total.ToString("0.00") + " rnd=" + rnd.ToString("0.0000");
                Emit(ref e);

                bool staminaApplies = !T.IsMonster || _r.monsterGuardBreakByStamina;
                if (staminaApplies)
                {
                    double stD = DamageFormula.GuardStaminaDamage(dmg.Total, T.Stat(StatId.GUARD_STRENGTH), _r);
                    double sb = T.Stamina;
                    T.Stamina -= stD;
                    bool broke = T.Stamina < 0;
                    if (broke) T.Stamina = 0; // [기획] 충돌 처리 순서도: 방어자 스태미나 < 0 → 0, 가드 브레이크
                    var r = NewEvent(BattleEventType.ResourceChanged, T.EntityId);
                    r.TargetId = A.EntityId;
                    r.SkillId = s.Id;
                    r.Damage = stD;
                    r.ActorStaminaBefore = sb;
                    r.ActorStaminaAfter = T.Stamina;
                    r.Reason = "가드 스태미나 피해 = 피해량×" + _r.guardStaminaFactor + "/가드강도";
                    Emit(ref r);
                    // 가드 브레이크(기절)는 tick 끝에 적용: 같은 tick 의 다른 타격은 가드가 유지된 상태로 계산
                    if (broke) QueueState(PendingStateKind.GuardBreak, StatusKind.Stun, T, A, s.Id, t);
                }
                return;
            }

            T.Hp = Math.Max(0, T.Hp - dmg.Total);
            double dealt = hpBefore - T.Hp;
            A.DamageDealt += dealt;
            T.DamageTaken += dealt;
            A.HitsLanded++;
            T.HitsTaken++;
            e.TargetHpAfter = T.Hp;
            e.Result = groggy ? "HIT_GROGGY" : "HIT";
            e.Reason = "rnd=" + rnd.ToString("0.0000") + (dmg.ChargeMult != 1 ? " charge×" + dmg.ChargeMult.ToString("0.000") : "")
                     + (dmg.RangedMult != 1 ? " ranged×" + dmg.RangedMult.ToString("0.00") : "");

            // 강인도
            double poiseAtk = A.Stat(StatId.STAGGER_BUILDUP) * (_r.poiseDamageUsesMotionValue ? mv : 1.0);
            bool staggered = false;
            // [기획 확정] 경직 중 강인도 무적: 경직이 시작될 때 강인도를 최대치로 채우고,
            // 경직이 끝날 때까지는 맞아도 HP 피해만 받으며 강인도는 깎이지 않는다(재경직 없음).
            bool poiseImmune = _r.poiseImmuneWhileStaggered && T.IsStaggered(t);
            if (poiseImmune && poiseAtk > 0 && T.MaxPoise > 0)
            {
                e.PoiseBefore = T.Poise;
                e.PoiseAfter = T.Poise;
                e.Reason += " | 경직 중 강인도 무적";
            }
            else if (poiseAtk > 0 && T.MaxPoise > 0)
            {
                double bonus = T.ActivePoiseBonus;
                double pb = T.Poise;
                T.Poise -= Math.Max(0, poiseAtk - bonus);
                T.PoiseRecoverTick = t + _r.SecToTicks(_r.poiseRecoveryDelaySec);
                e.PoiseBefore = pb;
                e.PoiseAfter = T.Poise;
                if (T.Poise <= 1e-9) staggered = true;
            }
            Emit(ref e);

            // 경직 발동(강인도 최대치 회복 포함)은 tick 끝에 적용: 같은 tick 의 다른 타격은 경직 전 상태로 계산
            if (staggered) QueueState(PendingStateKind.Stagger, StatusKind.Stun, T, A, s.Id, t);

            // 기절 유발(%)은 균형 공격력에 곱한다(2.ARPG_어빌리티 어빌리티목록 F51).
            // 상태이상 누적은 경직 중 강인도 무적과 별개이며, 가드·회피한 타격은 이 지점에 오지 않는다.
            // 발동(기절·중독·빙결)은 QueueState 로 tick 끝에 적용되므로 같은 tick 의 다음 타격은 발동 전 상태(그로기 아님)로 계산된다.
            ApplyBuildup(A, T, s, mv, t, StatusKind.Stun, StatId.STAGGER_BUILDUP, StatId.STUN_BUILDUP_PCT, StatId.STUN_RESIST);
            ApplyBuildup(A, T, s, mv, t, StatusKind.Wound, StatId.WOUND_BUILDUP, StatId.WOUND_BUILDUP_PCT, StatId.WOUND_RESIST);
            ApplyBuildup(A, T, s, mv, t, StatusKind.Poison, StatId.POISON_BUILDUP, StatId.POISON_BUILDUP_PCT, StatId.POISON_RESIST);
            ApplyBuildup(A, T, s, mv, t, StatusKind.Frost, StatId.FROST_BUILDUP, StatId.FROST_BUILDUP_PCT, StatId.FROST_RESIST);

            // 적중 시(ON_HIT) 버프·디버프: 가드·무적 회피는 적중이 아니므로 제외. 같은 tick 타격을 다 해결한 뒤 건다.
            if (s.Buff != null && s.EffectiveBuffTrigger == BuffTrigger.OnHit)
                _pendingBuffs.Add(new PendingBuff { Caster = A, Skill = s, HitTarget = T });
        }

        // ------------------------------------------------------------------ 버프(BUFF 표 + SKILL Buff_ID)
        /// <summary>ON_HIT 버프를 수집 순서대로 건다. 자신·아군 대상은 같은 스킬당 한 번만(여러 명을 맞혀도 1회).</summary>
        private void FlushPendingBuffs(long t)
        {
            for (int i = 0; i < _pendingBuffs.Count; i++)
            {
                var pb = _pendingBuffs[i];
                bool toEnemy = pb.Skill.EffectiveBuffTarget == BuffTarget.Enemy;
                if (!toEnemy)
                {
                    bool dup = false;
                    for (int j = 0; j < i; j++)
                        if (_pendingBuffs[j].Caster == pb.Caster && _pendingBuffs[j].Skill == pb.Skill) { dup = true; break; }
                    if (dup) continue;
                }
                TriggerBuff(pb.Caster, pb.Skill, toEnemy ? pb.HitTarget : null, t);
            }
            _pendingBuffs.Clear();
        }

        /// <summary>
        /// 스킬의 버프를 대상에게 건다.
        /// 대상: SELF = 시전자 / ENEMY = 적중 대상(ON_HIT) 또는 현재 타겟(사거리 안) / ALLY = 같은 팀 전원(자신 포함, Hit_Range 가 있으면 그 반경 안).
        /// 확률: Trigger_Chance(1 미만일 때만 난수 사용). 지속: Buff_Duration(ms) → tick.
        /// </summary>
        private void TriggerBuff(Combatant caster, SkillDefinition s, Combatant hitTarget, long t)
        {
            var b = s.Buff;
            if (b == null || caster.Dead) return;
            double chance = s.BuffChance > 0 ? s.BuffChance : (_r.buffZeroChanceAlways ? 1.0 : 0.0);
            if (chance <= 0) return;
            if (chance < 1.0 && _rng.NextUnit() >= chance) return; // 100% 면 난수를 쓰지 않음(기존 결과·해시 유지)
            long end = t + Math.Max(1, CombatRules.MsToTicks(s.BuffDurationMs, _r.tickRate));
            switch (s.EffectiveBuffTarget)
            {
                case BuffTarget.Self:
                    ApplyBuff(caster, b, caster, s, t, end);
                    break;
                case BuffTarget.Enemy:
                {
                    var tg = hitTarget ?? BuffEnemyTarget(caster, s);
                    if (tg != null && !tg.Dead) ApplyBuff(tg, b, caster, s, t, end);
                    break;
                }
                case BuffTarget.Ally:
                    for (int i = 0; i < _all.Count; i++)
                    {
                        var al = _all[i];
                        if (al.Dead || al.TeamId != caster.TeamId) continue;
                        if (al != caster && s.HitRange > 0 && Vec2.Distance(caster.Pos, al.Pos) > s.HitRange + al.Radius + 1e-9) continue;
                        ApplyBuff(al, b, caster, s, t, end);
                    }
                    break;
            }
        }

        /// <summary>시전·종료 시 적 대상: 현재 타겟. 스킬에 사거리가 있으면 그 안에 있을 때만.</summary>
        private Combatant BuffEnemyTarget(Combatant caster, SkillDefinition s)
        {
            var tg = caster.Target;
            if (tg == null || tg.Dead) return null;
            if (s.EffectiveRange <= 0 && s.HitRange <= 0) return tg;
            double reach = caster.Radius + (IsInstantRanged(s) ? s.EffectiveRange : s.Reach) + tg.Radius;
            return Vec2.Distance(caster.Pos, tg.Pos) <= reach + 1e-9 ? tg : null;
        }

        /// <summary>같은 Buff_ID 가 이미 있으면 지속 시간만 새로 시작(중첩 없음), 없으면 추가하고 능력치를 다시 계산.</summary>
        private void ApplyBuff(Combatant T, BuffDefinition b, Combatant src, SkillDefinition s, long t, long end)
        {
            ActiveBuff ab = null;
            for (int i = 0; i < T.Buffs.Count; i++)
            {
                if (T.Buffs[i].Def.Id == b.Id) { ab = T.Buffs[i]; break; }
            }
            bool refresh = ab != null;
            if (!refresh)
            {
                ab = new ActiveBuff { Def = b };
                T.Buffs.Add(ab);
            }
            ab.SourceId = src.EntityId;
            ab.SkillId = s.Id;
            ab.StartTick = t;
            ab.EndTick = end;
            T.BuffsApplied++;
            double hpB = T.Hp;
            if (!refresh) RecalcStats(T);
            var e = NewEvent(BattleEventType.BuffApplied, T.EntityId);
            e.TargetId = src.EntityId;
            e.SkillId = s.Id;
            e.StatusId = "BUFF_" + b.Id;
            e.Result = refresh ? "REFRESH" : "APPLY";
            e.ActorHpBefore = hpB;
            e.ActorHpAfter = T.Hp;
            e.Reason = b.Describe() + " | " + (s.BuffDurationMs / 1000.0).ToString("0.###") + "초";
            Emit(ref e);
        }

        private void ExpireBuffs(Combatant c, long t)
        {
            if (c.Buffs.Count == 0) return;
            _expiredBuffer.Clear();
            for (int i = 0; i < c.Buffs.Count; )
            {
                if (t >= c.Buffs[i].EndTick)
                {
                    _expiredBuffer.Add(c.Buffs[i]);
                    c.Buffs.RemoveAt(i);
                }
                else i++;
            }
            if (_expiredBuffer.Count == 0) return;
            double hpB = c.Hp;
            RecalcStats(c);
            for (int i = 0; i < _expiredBuffer.Count; i++)
            {
                var ab = _expiredBuffer[i];
                var e = NewEvent(BattleEventType.BuffExpired, c.EntityId);
                e.TargetId = ab.SourceId;
                e.SkillId = ab.SkillId;
                e.StatusId = "BUFF_" + ab.Def.Id;
                e.Result = "EXPIRE";
                e.ActorHpBefore = hpB;
                e.ActorHpAfter = c.Hp;
                e.Reason = (ab.Def.Name ?? e.StatusId) + " 지속 종료";
                Emit(ref e);
            }
            _expiredBuffer.Clear();
        }

        /// <summary>
        /// 능력치 = 원래 값 + 걸려 있는 버프 수치(걸린 순서대로 합산). 피해·강인도·상태이상 계산은 모두 이 값을 쓴다.
        /// 최대 HP/FP/스태미나/강인도가 줄면 현재값을 새 최대치로 자르고, 늘면 rules.buffMaxIncreaseRaisesCurrent 가 true 일 때
        /// 현재값도 같은 만큼 늘린다(HP 0 인 개체는 늘리지 않음).
        /// </summary>
        private void RecalcStats(Combatant c)
        {
            Array.Copy(c.BaseStats, c.Stats, c.Stats.Length);
            for (int i = 0; i < c.Buffs.Count; i++)
            {
                var d = c.Buffs[i].Def;
                for (int k = 0; k < d.Stats.Length; k++) c.Stats[(int)d.Stats[k]] += d.Values[k];
            }
            bool raise = _r.buffMaxIncreaseRaisesCurrent;
            double old = c.MaxHp;
            c.MaxHp = Math.Max(1, c.Stat(StatId.HP));
            c.Hp = AdjustCurrent(c.Hp, old, c.MaxHp, raise && c.Hp > 0);
            old = c.MaxFp;
            c.MaxFp = Math.Max(0, c.Stat(StatId.FP));
            c.Fp = AdjustCurrent(c.Fp, old, c.MaxFp, raise);
            old = c.MaxStamina;
            c.MaxStamina = Math.Max(0, c.Stat(StatId.STAMINA));
            c.Stamina = AdjustCurrent(c.Stamina, old, c.MaxStamina, raise);
            old = c.MaxPoise;
            c.MaxPoise = Math.Max(0, c.Stat(StatId.STAGGER_ENDURANCE));
            c.Poise = AdjustCurrent(c.Poise, old, c.MaxPoise, raise);
            c.StaminaRegenPerSec = Math.Max(0, c.Stat(StatId.STAMINA_RECOVERY));
        }

        private static double AdjustCurrent(double cur, double oldMax, double newMax, bool raiseOnIncrease)
        {
            if (newMax > oldMax) return raiseOnIncrease ? cur + (newMax - oldMax) : cur;
            return Math.Min(cur, newMax);
        }

        private bool IsImmune(Combatant T, StatusKind k, long t)
        {
            switch (k)
            {
                // 같은 tick 에 이미 발동이 예약된 상태이상은 그 tick 의 추가 누적을 받지 않는다(효과 중 면역과 같은 취급)
                case StatusKind.Poison: return T.PoisonActive || HasPendingState(T, PendingStateKind.Status, k);
                case StatusKind.Frost: return T.FrostActive(t) || HasPendingState(T, PendingStateKind.Status, k);
                case StatusKind.Stun: return T.IsStunned(t) || HasPendingState(T, PendingStateKind.Status, k);
                default: return false; // [확정] 상처: 즉시 재누적 가능
            }
        }

        private void ApplyBuildup(Combatant A, Combatant T, SkillDefinition s, double mv, long t,
                                  StatusKind kind, StatId buildStat, StatId pctStat, StatId resistStat)
        {
            double build = A.Stat(buildStat) * (_r.statusBuildupUsesMotionValue ? mv : 1.0) * (1 + A.Stat(pctStat) / _r.statScale);
            if (build <= 0 || IsImmune(T, kind, t)) return;
            int k = (int)kind;
            double before = T.Buildup[k];
            T.Buildup[k] += build;
            double resist = T.Stat(resistStat);
            string sid = EnumText.StatusName(kind);
            var e = NewEvent(BattleEventType.StatusBuildup, A.EntityId);
            e.TargetId = T.EntityId;
            e.SkillId = s.Id;
            e.StatusId = sid;
            e.Reason = before.ToString("0.0") + "→" + T.Buildup[k].ToString("0.0") + " / 저항 " + resist.ToString("0.0");
            Emit(ref e);
            if (resist - T.Buildup[k] > 1e-9) return;

            // 발동: 게이지 0. 상처는 즉시 피해(상태 전환이 아님), 기절·중독·빙결은 tick 끝에 걸린다(QueueState).
            T.Buildup[k] = 0;
            if (kind != StatusKind.Wound)
            {
                QueueState(PendingStateKind.Status, kind, T, A, s.Id, t);
                return;
            }
            var ap = NewEvent(BattleEventType.StatusApplied, T.EntityId);
            ap.TargetId = A.EntityId;
            ap.StatusId = sid;
            ap.SkillId = s.Id;
            double hb = T.Hp;
            double dmg = T.MaxHp * _r.woundMaxHpRatio;
            T.Hp = Math.Max(0, T.Hp - dmg);
            A.DamageDealt += hb - T.Hp;
            T.DamageTaken += hb - T.Hp;
            ap.Damage = dmg;
            ap.ActorHpBefore = hb;
            ap.ActorHpAfter = T.Hp;
            ap.Reason = "최대 HP " + (_r.woundMaxHpRatio * 100) + "% 즉시 피해";
            Emit(ref ap);
        }

        // ------------------------------------------------------------------ 같은 tick 상태 전환(tick 끝 일괄 적용)
        /// <summary>
        /// [확정 2026-11-03] 경직·가드 브레이크·기절·중독·빙결의 발동은 그 tick 의 타격을 모두 해결한 뒤 한꺼번에 적용한다.
        /// 그래서 같은 tick 에 맞는 다른 타격은 tick 시작 때 상태(가드 유지, 그로기 아님, 경직 전)로 계산된다.
        /// 같은 대상의 같은 전환은 한 번만 예약하며, 가드 브레이크와 기절 누적은 같은 '기절'로 보아 먼저 예약된 쪽만 쓴다.
        /// rules.deferStateChangesToTickEnd=false 면 이전처럼 즉시 적용한다.
        /// </summary>
        private void QueueState(PendingStateKind kind, StatusKind status, Combatant target, Combatant source, string skillId, long t)
        {
            var ps = new PendingState { Kind = kind, Status = status, Target = target, Source = source, SkillId = skillId };
            if (!_r.deferStateChangesToTickEnd)
            {
                ApplyState(ps, t);
                return;
            }
            if (HasPendingState(target, kind, status)) return;
            _pendingStates.Add(ps);
        }

        private static bool IsStunState(PendingStateKind kind, StatusKind status)
        {
            return kind == PendingStateKind.GuardBreak || (kind == PendingStateKind.Status && status == StatusKind.Stun);
        }

        private bool HasPendingState(Combatant target, PendingStateKind kind, StatusKind status)
        {
            bool stun = IsStunState(kind, status);
            for (int i = 0; i < _pendingStates.Count; i++)
            {
                var p = _pendingStates[i];
                if (p.Target != target) continue;
                if (stun && IsStunState(p.Kind, p.Status)) return true;
                if (p.Kind == kind && (kind != PendingStateKind.Status || p.Status == status)) return true;
            }
            return false;
        }

        private void FlushPendingStates(long t)
        {
            for (int i = 0; i < _pendingStates.Count; i++) ApplyState(_pendingStates[i], t);
            _pendingStates.Clear();
        }

        private void ApplyState(PendingState ps, long t)
        {
            var T = ps.Target;
            if (T.Dead || T.Hp <= 0) return; // 이 tick 에 사망 확정될 대상에는 상태를 걸지 않는다
            int sourceId = ps.Source != null ? ps.Source.EntityId : -1;
            switch (ps.Kind)
            {
                case PendingStateKind.Stagger:
                {
                    double pb = T.Poise;
                    T.Poise = T.MaxPoise; // [기획 확정] 경직이 시작될 때 강인도를 최대치로 채운다
                    T.StaggerUntilTick = t + _r.staggerTicks;
                    T.StaggerCount++;
                    QueueInterrupt(T, "강인도 붕괴", 1);
                    var st = NewEvent(BattleEventType.Staggered, T.EntityId);
                    st.TargetId = sourceId;
                    st.SkillId = ps.SkillId;
                    st.PoiseBefore = pb;
                    st.PoiseAfter = T.Poise;
                    st.Result = "STAGGER";
                    st.Reason = _r.staggerTicks + "tick 경직";
                    Emit(ref st);
                    break;
                }
                case PendingStateKind.GuardBreak:
                {
                    if (T.IsStunned(t)) return;
                    T.StunUntilTick = t + _r.stunTicks;
                    T.StunFromGuardBreak = true;
                    QueueInterrupt(T, "가드 브레이크", 2);
                    var g = NewEvent(BattleEventType.GuardBroken, T.EntityId);
                    g.TargetId = sourceId;
                    g.StatusId = "STUN";
                    g.Result = "GUARD_BREAK";
                    g.Reason = _r.stunTicks + "tick 기절(가드 브레이크 그로기)";
                    Emit(ref g);
                    break;
                }
                case PendingStateKind.Status:
                {
                    var ap = NewEvent(BattleEventType.StatusApplied, T.EntityId);
                    ap.TargetId = sourceId;
                    ap.StatusId = EnumText.StatusName(ps.Status);
                    ap.SkillId = ps.SkillId;
                    switch (ps.Status)
                    {
                        case StatusKind.Stun:
                            if (T.IsStunned(t)) return;
                            T.StunUntilTick = t + _r.stunTicks;
                            T.StunFromGuardBreak = false;
                            QueueInterrupt(T, "기절 누적 발동", 2);
                            ap.Reason = _r.stunTicks + "tick 기절";
                            break;
                        case StatusKind.Poison:
                            T.PoisonTicksLeft = (int)Math.Round(_r.poisonDurationSec);
                            T.PoisonNextTick = t + _r.SecToTicks(_r.poisonFirstDelaySec);
                            ap.Reason = _r.poisonDurationSec + "초, 초당 최대 HP " + (_r.poisonMaxHpRatioPerSec * 100) + "%, 첫 피해 +" + _r.poisonFirstDelaySec + "초";
                            break;
                        case StatusKind.Frost:
                            T.FrostEndTick = t + _r.SecToTicks(_r.frostDurationSec);
                            ap.Reason = _r.frostDurationSec + "초, 이동·공격·구르기 ×" + _r.frostSpeedMult;
                            break;
                        default:
                            return;
                    }
                    Emit(ref ap);
                    break;
                }
            }
        }

        /// <summary>강제 중단 예약. 같은 tick 에 여러 사유가 겹치면 우선순위가 높은 사유를 남긴다(기절 2 > 경직 1).</summary>
        private void QueueInterrupt(Combatant c, string reason, int priority = 0)
        {
            for (int i = 0; i < _interruptList.Count; i++)
            {
                if (_interruptList[i] != c) continue;
                if (priority > _interruptPriority[i])
                {
                    _interruptReason[i] = reason;
                    _interruptPriority[i] = priority;
                }
                return;
            }
            _interruptList.Add(c);
            _interruptReason.Add(reason);
            _interruptPriority.Add(priority);
        }

        // ------------------------------------------------------------------ 7) 중단·종료·사망
        private void ApplyInterrupts(long t)
        {
            for (int i = 0; i < _interruptList.Count; i++)
            {
                var c = _interruptList[i];
                // [확정] 강제 중단 시 이후 타격·다음 콤보 연결 취소, 콤보 시작 노드로 초기화
                if (c.Action != null) EndAction(c, c.Action, t, BattleEventType.ActionInterrupted, _interruptReason[i]);
                c.ComboNodeId = "NONE";
                c.ComboExpireTick = -1;
            }
            _interruptList.Clear();
            _interruptReason.Clear();
            _interruptPriority.Clear();
        }

        private void FinishActions(long t)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var c = _all[i];
                var a = c.Action;
                if (c.Dead || a == null || !a.IsFinished) continue;
                if (a.Kind == ActionKind.Skill && a.NodeId != null)
                    c.ComboExpireTick = t + _r.SecToTicks(_r.comboContinueWindowSec);
                else
                {
                    c.ComboNodeId = "NONE";
                    c.ComboExpireTick = -1;
                }
                EndAction(c, a, t, BattleEventType.ActionEnded, "완료");
                // 동작 완료 시(ON_END) 버프: 끝까지 수행한 경우만(강제 중단·캔슬 연결은 제외)
                var sk = a.Skill;
                if (sk != null && sk.Buff != null && sk.EffectiveBuffTrigger == BuffTrigger.OnEnd)
                    TriggerBuff(c, sk, null, t);
            }
        }

        private void ConfirmDeaths(long t, string cause)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var c = _all[i];
                if (c.Dead || c.Hp > 0) continue;
                c.Dead = true;
                c.DeathTick = t;
                c.Action = null;
                c.StaggerUntilTick = -1;
                c.StunUntilTick = -1;
                var e = NewEvent(BattleEventType.EntityDied, c.EntityId);
                e.ActorHpBefore = c.Hp;
                e.ActorHpAfter = c.Hp;
                e.Result = "DEAD";
                e.Reason = cause;
                Emit(ref e);
            }
        }

        private bool CheckEnd(long t)
        {
            if (_finished) return true;
            int aliveTeams = 0, lastTeam = -1;
            var seen = _aliveTeamsBuffer;
            seen.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                var c = _all[i];
                if (c.Dead || seen.Contains(c.TeamId)) continue;
                seen.Add(c.TeamId);
                aliveTeams++;
                lastTeam = c.TeamId;
            }
            if (aliveTeams >= 2) return false;
            _tick = t + 1;
            if (aliveTeams == 1) EndBattle(BattleEndReason.TeamVictory, lastTeam);
            else EndBattle(BattleEndReason.MutualWipe, -1);
            return true;
        }

        private void EndBattle(BattleEndReason reason, int winner)
        {
            _finished = true;
            var e = NewEvent(BattleEventType.BattleEnded, -1);
            e.Result = reason.ToString();
            e.Reason = winner >= 0 ? "winner=team" + winner : EnumText.EndReasonKo(reason);
            Emit(ref e);

            var res = new BattleResult
            {
                RunId = RunId,
                BattleIndex = BattleIndex,
                Seed = Seed,
                ScenarioName = _scenario.name,
                DataHash = _cat.DataHash,
                RulesVersion = _r.rulesVersion,
                Reason = reason,
                WinnerTeam = winner,
                Ticks = _tick,
                DurationSec = _tick / (double)_r.tickRate,
                EventHash = _hash.Hash,
                EventCount = _hash.Count,
            };
            foreach (var c in _all)
            {
                res.Combatants.Add(new CombatantSummary
                {
                    EntityId = c.EntityId,
                    Label = c.Label,
                    TeamId = c.TeamId,
                    DefinitionId = c.Def.Id,
                    Name = c.Def.DisplayName,
                    Alive = !c.Dead,
                    Hp = c.Hp, MaxHp = c.MaxHp,
                    Fp = c.Fp, MaxFp = c.MaxFp,
                    Stamina = c.Stamina, MaxStamina = c.MaxStamina,
                    DamageDealt = c.DamageDealt,
                    DamageTaken = c.DamageTaken,
                    HitsLanded = c.HitsLanded,
                    HitsTaken = c.HitsTaken,
                    HitsGuarded = c.HitsGuarded,
                    HitsAvoided = c.HitsAvoided,
                    StaggerCount = c.StaggerCount,
                    ResourceBlockedCount = c.ResourceBlockedCount,
                    DeathTick = c.DeathTick,
                });
            }
            foreach (var kv in _skillStats.Stats) res.SkillStats[kv.Key] = kv.Value;
            _result = res;
            _sink.OnBattleEnded(res);
        }

        // ------------------------------------------------------------------ 이벤트
        private BattleEvent NewEvent(BattleEventType type, int actorId)
        {
            return BattleEvent.Create(++_seq, _tick, type, actorId);
        }

        private void Emit(ref BattleEvent e)
        {
            _sink.OnEvent(in e);
        }

        private void EmitStatus(BattleEventType type, Combatant c, string statusId, string reason)
        {
            var e = NewEvent(type, c.EntityId);
            e.StatusId = statusId;
            e.Reason = reason;
            Emit(ref e);
        }

        private static void FillActor(ref BattleEvent e, Combatant c)
        {
            e.ActorHpBefore = c.Hp; e.ActorHpAfter = c.Hp;
            e.ActorFpBefore = c.Fp; e.ActorFpAfter = c.Fp;
            e.ActorStaminaBefore = c.Stamina; e.ActorStaminaAfter = c.Stamina;
        }

        // ------------------------------------------------------------------ AI 상황 변수
        internal bool ComputeIncomingAttack(Combatant c)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var e = _all[i];
                if (e.Dead || e.TeamId == c.TeamId || e.Target != c) continue;
                var a = e.Action;
                if (a == null || !a.IsAttackWithHits) continue;
                var hits = a.Skill.HitTimesMs;
                if (a.NextHit >= hits.Length) continue;
                double speed = a.Skill.AniSpeed * e.PhaseSpeedMult;
                double remainMs = (hits[a.NextHit] - a.ElapsedMs) / Math.Max(1e-6, speed);
                if (remainMs < 0 || remainMs > _r.incomingAttackWindowMs) continue;
                double reach = e.Radius + (IsInstantRanged(a.Skill) ? a.Skill.EffectiveRange : a.Skill.Reach) + c.Radius;
                if (Vec2.Distance(e.Pos, c.Pos) > reach) continue;
                return true;
            }
            return false;
        }

        private sealed class AiContextAdapter : IAiContext
        {
            private readonly BattleSimulation _sim;
            private Combatant _c;
            private long _t;
            private double _dist;
            private bool _incomingComputed, _incoming;

            public AiContextAdapter(BattleSimulation sim) { _sim = sim; }

            public void Bind(Combatant c, long t)
            {
                _c = c;
                _t = t;
                _dist = c.Target != null && !c.Target.Dead ? Vec2.Distance(c.Pos, c.Target.Pos) : 9999.0;
                _incomingComputed = false;
            }

            public double DistM { get { return _dist; } }
            public string DistBand
            {
                get
                {
                    if (_dist <= _sim._r.distMeleeMax) return "melee";
                    if (_dist <= _sim._r.distMidMax) return "mid";
                    return "far";
                }
            }
            public bool Perceived { get { return _c.Perceived; } }
            public double HpPct { get { return _c.HpPct; } }
            public bool PoiseBroken { get { return _t < _c.PoiseBrokenUntilTick || _c.IsStaggered(_t); } }
            public double StaminaPct
            {
                get
                {
                    if (!_c.ResourcesManaged) return _sim._r.monsterStaminaPctForAi;
                    return _c.MaxStamina > 0 ? _c.Stamina / _c.MaxStamina * 100.0 : 100.0;
                }
            }
            public int Phase { get { return _c.Phase; } }
            public string TargetStateName
            {
                get { return _c.Target != null && !_c.Target.Dead ? _c.Target.StateFor(_t).ToString() : "idle"; }
            }
            public bool TargetHealRecover { get { return false; } } // 물약 미결(비활성)
            public bool TargetAttackRecover
            {
                get
                {
                    var tg = _c.Target;
                    return tg != null && !tg.Dead && tg.Action != null && tg.Action.InAttackRecovery;
                }
            }
            public bool IncomingAttack
            {
                get
                {
                    if (!_incomingComputed)
                    {
                        _incoming = _sim.ComputeIncomingAttack(_c);
                        _incomingComputed = true;
                    }
                    return _incoming;
                }
            }
            public string LastAction { get { return _c.LastActionId; } }
            public bool ActionUsedRecent(string actionId, int n)
            {
                var list = _c.RecentActions;
                for (int i = list.Count - 1, k = 0; i >= 0 && k < n; i--, k++)
                    if (list[i] == actionId) return true;
                return false;
            }
            public bool OnCooldown(string actionId) { return _sim.IsOnCooldown(_c, actionId, _t); }
        }
    }
}
