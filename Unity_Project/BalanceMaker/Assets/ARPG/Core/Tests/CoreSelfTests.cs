using System;
using System.Collections.Generic;
using System.Globalization;
using ARPG.Core.AI;
using ARPG.Core.Batch;
using ARPG.Core.Combat;
using ARPG.Core.Events;
using ARPG.Core.Simulation;

namespace ARPG.Core.Tests
{
    public sealed class SelfTestResult
    {
        public string Name;
        public bool Passed;
        public string Detail;
        public override string ToString() { return (Passed ? "[PASS] " : "[FAIL] ") + Name + (string.IsNullOrEmpty(Detail) ? "" : " - " + Detail); }
    }

    /// <summary>
    /// 규칙 사례 검증(구현계획 10장). Unity 메뉴 ARPG/Self Test 또는 시뮬레이터 UI 에서 실행한다.
    /// 외부 테스트 패키지 없이 순수 C# 으로 동작한다.
    /// </summary>
    public static class CoreSelfTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<SelfTestResult> RunAll(DataCatalog realCatalog, CombatRules realRules, BattleScenario realScenario)
        {
            var list = new List<SelfTestResult>();
            Run(list, "단위 변환 ms→tick (300→9, 800→24, 0→0, 1333.333→40)", TestTicks);
            Run(list, "자원 선검사 HP/FP/스태미나 표(11.3)", TestResourceTable);
            Run(list, "대미지메이커 예시 재현(검사37→기사단장 61.002041, 검사44 81.018593)", TestDamageMaker);
            Run(list, "랜덤 상한 = (1.1+치명)×(1+치명%) → 1.101", TestRandomUpper);
            Run(list, "사격 거리 감쇄(시트 수식, ROUNDDOWN 2자리)", TestRangedFalloff);
            Run(list, "차지 배율(0ms=1.0, 1500/3000=1.1, 최대 0 분모 방지)", TestCharge);
            Run(list, "가드 스태미나 피해 = 피해량×10/가드강도", TestGuardStamina);
            Run(list, "AI 조건식 해석", TestConditions);
            Run(list, "다단히트 100/150/200, 합 450, 타격 tick 8/17/26", TestMultiHitSim);
            Run(list, "캔슬 0 = 동작 끝까지(800ms 행동은 tick 23 에 종료)", TestCancelZero);
            Run(list, "상태이상 게이지 60 → 1초 뒤 55", TestBuildupDecay);
            Run(list, "중독: 발동 직후 피해 없음, +1초부터 1초 간격 10회", TestPoison);
            Run(list, "스태미나 20 에서 비용 50 → -30 보존, 1 미만 동안 재사용 불가", TestStaminaNegative);
            Run(list, "동시 교환 + 강제 중단 시 이후 타격 취소·콤보 초기화", TestSimultaneousAndInterrupt);
            Run(list, "AI 폭포: 확률 0 / 쿨다운 / 자원 부족 행은 건너뜀", TestWaterfallSkips);
            if (realCatalog != null && realScenario != null)
            {
                Run(list, "결정성: 같은 시드 2회 이벤트 해시 일치", () => TestDeterminism(realCatalog, realRules, realScenario));
                Run(list, "모드 동등성: 구독자 유무와 무관하게 결과 동일", () => TestSinkIndependence(realCatalog, realRules, realScenario));
            }
            return list;
        }

        private static void Run(List<SelfTestResult> list, string name, Func<string> test)
        {
            try
            {
                string fail = test();
                list.Add(new SelfTestResult { Name = name, Passed = fail == null, Detail = fail });
            }
            catch (Exception ex)
            {
                list.Add(new SelfTestResult { Name = name, Passed = false, Detail = ex.GetType().Name + ": " + ex.Message });
            }
        }

        private static bool Near(double a, double b, double tol = 1e-6) { return Math.Abs(a - b) <= tol; }

        // ------------------------------------------------------------------ 순수 함수
        private static string TestTicks()
        {
            if (CombatRules.MsToTicks(300, 30) != 9) return "300ms";
            if (CombatRules.MsToTicks(800, 30) != 24) return "800ms";
            if (CombatRules.MsToTicks(0, 30) != 0) return "0ms";
            if (CombatRules.MsToTicks(1333.3333333, 30) != 40) return "1333.33ms";
            return null;
        }

        private static string TestResourceTable()
        {
            var r = new CombatRules();
            if (ResourceRules.Check(50, 50, 100, 49, 50, 0, r) != ResourceCheck.Ok) return "HP50/FP50 비용 49/50 은 가능해야 함";
            if (ResourceRules.Check(50, 50, 100, 50, 50, 0, r) == ResourceCheck.Ok) return "HP 비용 50 은 불가해야 함";
            if (ResourceRules.Check(50, 50, 100, 49, 51, 0, r) == ResourceCheck.Ok) return "FP 비용 51 은 불가해야 함";
            if (ResourceRules.Check(50, 50, 20, 0, 0, 50, r) != ResourceCheck.Ok) return "스태미나 20/비용 50 은 가능해야 함";
            if (ResourceRules.Check(50, 50, 0, 0, 0, 50, r) == ResourceCheck.Ok) return "스태미나 0 은 불가";
            if (ResourceRules.Check(50, 50, -5, 0, 0, 50, r) == ResourceCheck.Ok) return "스태미나 음수는 불가";
            if (ResourceRules.Check(50, 50, 0.5, 0, 0, 50, r) == ResourceCheck.Ok) return "스태미나 0.5 는 불가(1 이상 기준)";
            if (ResourceRules.Check(50, 50, -30, 0, 0, 0, r) != ResourceCheck.Ok) return "비용 0 행동은 스태미나 제한 없음";
            return null;
        }

        private static double[] Stats() { return new double[(int)StatId.COUNT]; }

        private static SkillDefinition SimpleSkill(DamageType t, double mv)
        {
            return new SkillDefinition
            {
                Id = "T", Enabled = true, Valid = true, Type = SkillType.ATTACK, Slot = "LIGHT_ATK",
                HitTimesMs = new[] { 300.0 }, MotionValues = new[] { mv }, HitCount = 1,
                DamageTypes = new[] { t }, AniSpeed = 1, AniTimeMs = 800,
            };
        }

        private static string TestDamageMaker()
        {
            var r = new CombatRules();
            var atk = Stats();
            var def = Stats();
            atk[(int)StatId.SLASH_ATTACK] = 91.00232142857143;
            def[(int)StatId.SLASH_NEGATION] = 33000;
            var d = DamageFormula.Compute(atk, def, SimpleSkill(DamageType.SLASH, 1.0), 1.0, 1.0005, 0, 2, false, 1, r);
            if (!Near(d.Total, 61.002041, 1e-5)) return "검사37: " + d.Total.ToString("R", Inv);
            atk[(int)StatId.SLASH_ATTACK] = 102.17136428571429;
            atk[(int)StatId.SLASH_ATTACK_PCT] = 8571.42857142857;
            def[(int)StatId.SLASH_NEGATION] = 27000;
            d = DamageFormula.Compute(atk, def, SimpleSkill(DamageType.SLASH, 1.0), 1.0, 1.0005, 0, 2, false, 1, r);
            if (!Near(d.Total, 81.018593, 1e-5)) return "검사44: " + d.Total.ToString("R", Inv);
            d = DamageFormula.Compute(atk, def, SimpleSkill(DamageType.SLASH, 1.0), 1.0, 1.0005, 0, 2, true, 1, r);
            if (!Near(d.Total, 81.018593 * 1.05, 1e-5)) return "그로기 1.05 배";
            return null;
        }

        private static string TestRandomUpper()
        {
            var r = new CombatRules();
            var atk = Stats();
            atk[(int)StatId.CRITICAL_ATTACK] = 100;
            double u = DamageFormula.RandomUpper(atk, r);
            return Near(u, 1.101, 1e-12) ? null : "상한 " + u.ToString("R", Inv);
        }

        private static string TestRangedFalloff()
        {
            var r = new CombatRules();
            if (!Near(DamageFormula.RangedFalloff(10, r), 1.0)) return "10m";
            if (!Near(DamageFormula.RangedFalloff(19, r), 0.28)) return "19m 경감 0: " + DamageFormula.RangedFalloff(19, r);
            r.rangedFalloffMitigation = 0.15;
            if (!Near(DamageFormula.RangedFalloff(19, r), 0.39)) return "19m 경감 0.15: " + DamageFormula.RangedFalloff(19, r);
            if (!Near(DamageFormula.RangedFalloff(30, r), 0.19)) return "30m";
            r.rangedFalloffMitigation = 0;
            if (!Near(DamageFormula.RangedFalloff(100, r), 0.05)) return "상한 0.95";
            return null;
        }

        private static string TestCharge()
        {
            if (!Near(DamageFormula.ChargeMultiplier(0, 3000), 1.0)) return "0ms";
            if (!Near(DamageFormula.ChargeMultiplier(1500, 3000), 1.1)) return "1500ms";
            if (!Near(DamageFormula.ChargeMultiplier(9999, 3000), 1.2)) return "최대 초과";
            if (!Near(DamageFormula.ChargeMultiplier(500, 0), 1.0)) return "최대 0";
            return null;
        }

        private static string TestGuardStamina()
        {
            var r = new CombatRules();
            if (!Near(DamageFormula.GuardStaminaDamage(100, 100, r), 10)) return "100/100";
            if (!Near(DamageFormula.GuardStaminaDamage(250, 50, r), 50)) return "250/50";
            return null;
        }

        private sealed class FakeCtx : IAiContext
        {
            public double DistM { get; set; }
            public string DistBand { get; set; }
            public bool Perceived { get; set; }
            public double HpPct { get; set; }
            public bool PoiseBroken { get; set; }
            public double StaminaPct { get; set; }
            public int Phase { get; set; }
            public string TargetStateName { get; set; }
            public bool TargetHealRecover { get; set; }
            public bool TargetAttackRecover { get; set; }
            public bool IncomingAttack { get; set; }
            public string LastAction { get; set; }
            public List<string> Recent = new List<string>();
            public bool ActionUsedRecent(string actionId, int n)
            {
                for (int i = Recent.Count - 1, k = 0; i >= 0 && k < n; i--, k++) if (Recent[i] == actionId) return true;
                return false;
            }
            public bool OnCooldown(string actionId) { return actionId == "HEAVY_ATK"; }
        }

        private static string TestConditions()
        {
            var ctx = new FakeCtx { DistM = 2.0, DistBand = "melee", Perceived = true, HpPct = 25, StaminaPct = 10, Phase = 1, TargetStateName = "guarding", LastAction = "LIGHT_ATK" };
            ctx.Recent.Add("IDLE");
            ctx.Recent.Add("LIGHT_ATK");
            string err;
            AiCondition c;
            var cases = new[]
            {
                new KeyValuePair<string, bool>("dist_m<=3", true),
                new KeyValuePair<string, bool>("dist==melee", true),
                new KeyValuePair<string, bool>("dist!=far", true),
                new KeyValuePair<string, bool>("perceived==false", false),
                new KeyValuePair<string, bool>("stamina_pct<20", true),
                new KeyValuePair<string, bool>("hp_pct<=30 & dist==melee", true),
                new KeyValuePair<string, bool>("target_state==guarding", true),
                new KeyValuePair<string, bool>("action_used_recent(LIGHT_ATK,2)", true),
                new KeyValuePair<string, bool>("action_used_recent(HEAVY_ATK,2)", false),
                new KeyValuePair<string, bool>("on_cooldown(HEAVY_ATK)", true),
                new KeyValuePair<string, bool>("", true),
            };
            foreach (var kv in cases)
            {
                if (!AiCondition.TryParse(kv.Key, out c, out err)) return "해석 실패: " + kv.Key + " " + err;
                if (c.Evaluate(ctx) != kv.Value) return "평가 오류: " + kv.Key;
            }
            if (AiCondition.TryParse("unknown_var==1", out c, out err)) return "알 수 없는 변수를 거부해야 함";
            if (AiCondition.TryParse("dist==close", out c, out err)) return "dist 값 검증";
            return null;
        }

        // ------------------------------------------------------------------ 합성 카탈로그 시뮬레이션
        private sealed class EventRecorder : IBattleEventSink
        {
            public readonly List<BattleEvent> Events = new List<BattleEvent>();
            public void OnBattleStarted(BattleStartInfo info) { }
            public void OnEvent(in BattleEvent e) { Events.Add(e); }
            public void OnBattleEnded(BattleResult result) { }
        }

        private static SkillDto MakeSkill(string id, double[] hits, double[] mv, double ani, double cancel, double stamina)
        {
            return new SkillDto
            {
                id = id, enabled = true, valid = true, skillType = "ATTACK", slot = "LIGHT_ATK", aniSpeed = 1, aniTimeMs = ani,
                hitTimesMs = hits, motionValues = mv, hitCount = hits.Length, cancelTimeMs = cancel,
                effectiveRange = 2, hitRange = 1.5, damageTypes = new[] { "SLASH" }, costStamina = stamina, trackingAngleDeg = 180,
                trackingStartMs = 0, trackingEndMs = 300,
            };
        }

        private static DataCatalog BuildSynthetic(Action<double[]> atkStats, Action<double[]> dummyStats, string dummyAi,
                                                  double[] hits, double[] mv, double cancel, double stamina, double lightCooldown,
                                                  string dummyGroup = "TG")
        {
            var names = Enum.GetNames(typeof(StatId));
            var statNames = new List<string>();
            foreach (var n in names) if (n != "COUNT") statNames.Add(n);
            double[] sa = new double[statNames.Count];
            double[] sd = new double[statNames.Count];
            sa[(int)StatId.HP] = 100000; sd[(int)StatId.HP] = 100000;
            sa[(int)StatId.SLASH_ATTACK] = 100;
            atkStats(sa);
            dummyStats(sd);
            var chars = new CharacterFileDto
            {
                statNames = statNames.ToArray(),
                characters = new[]
                {
                    new CharacterDto { id = "ATK", enabled = true, displayName = "공격자", movesetId = "TG", aiProfileId = "T_AI", stats = sa },
                    new CharacterDto { id = "DUMMY", enabled = true, displayName = "허수아비", movesetId = dummyGroup, aiProfileId = dummyAi, stats = sd },
                },
            };
            var skills = new SkillFileDto { skills = new[] { MakeSkill("T_ATK", hits, mv, 800, cancel, stamina) } };
            var moves = new MovesetFileDto
            {
                nodes = new[]
                {
                    new MovesetNodeDto { nodeId = "TG_L1", enabled = true, groupId = "TG", prevNodeId = "NONE", inputSlot = "LIGHT_ATK", skillId = "T_ATK" },
                },
            };
            var ai = new AiFileDto
            {
                actions = new[]
                {
                    new ActionDefDto { actionId = "IDLE", enabled = true, actionType = "BUILTIN", inputSlot = "NONE" },
                    new ActionDefDto { actionId = "LIGHT_ATK", enabled = true, actionType = "INPUT", inputSlot = "LIGHT_ATK", cooldownSec = lightCooldown },
                    new ActionDefDto { actionId = "HEAVY_ATK", enabled = true, actionType = "INPUT", inputSlot = "HEAVY_ATK", cooldownSec = 0 },
                },
                behaviors = new[]
                {
                    new BehaviorDto { behaviorIndex = 1, enabled = true, aiId = "T_AI", phaseNo = 1, evalOrder = 10, condition1 = "dist==melee", chancePct = 100, actionId = "LIGHT_ATK", skillId = "NONE" },
                    new BehaviorDto { behaviorIndex = 2, enabled = true, aiId = "T_AI", phaseNo = 1, evalOrder = 20, chancePct = 100, actionId = "IDLE", skillId = "NONE" },
                    new BehaviorDto { behaviorIndex = 3, enabled = true, aiId = "T_IDLE", phaseNo = 1, evalOrder = 10, chancePct = 100, actionId = "IDLE", skillId = "NONE" },
                },
                phases = new PhaseDto[0],
                maps = new AiMapDto[0],
            };
            var cat = DataCatalog.Build(chars, skills, moves, ai, new DataManifestDto { dataHash = "synthetic" });
            cat.SortRules(true);
            return cat;
        }

        private static BattleScenario Duel(double distance)
        {
            var sc = new BattleScenario { name = "selftest", masterSeed = 7, maxBattleSec = 20, spawnDistance = distance, spawnSpacing = 1 };
            sc.GetOrAddTeam(0).members.Add(new ScenarioMember { definitionId = "ATK", count = 1 });
            sc.GetOrAddTeam(1).members.Add(new ScenarioMember { definitionId = "DUMMY", count = 1 });
            return sc;
        }

        private static CombatRules FixedRandomRules()
        {
            var r = new CombatRules { randomMin = 1.0, randomMaxBase = 1.0 };
            return r;
        }

        private static string TestMultiHitSim()
        {
            var cat = BuildSynthetic(a => { }, d => { }, "T_IDLE", new[] { 300.0, 600.0, 900.0 }, new[] { 1.0, 1.5, 2.0 }, 0, 0, 1000);
            // 900ms 타격이 Ani_Time(800) 보다 늦지 않도록 스킬 시간을 늘린다
            cat.Skills["T_ATK"].AniTimeMs = 1000;
            var rec = new EventRecorder();
            var sim = new BattleSimulation(cat, FixedRandomRules(), Duel(2.0), "t", 0, 1, rec);
            for (int i = 0; i < 40; i++) sim.Step();
            var dmg = new List<double>();
            var ticks = new List<long>();
            foreach (var e in rec.Events)
            {
                if (e.Type == BattleEventType.HitResolved && e.TargetId >= 0 && e.ActorId == 0)
                {
                    dmg.Add(e.Damage);
                    ticks.Add(e.Tick);
                }
            }
            if (dmg.Count != 3) return "타격 수 " + dmg.Count;
            if (!Near(dmg[0], 100) || !Near(dmg[1], 150) || !Near(dmg[2], 200)) return "피해 " + string.Join(",", dmg);
            if (!Near(dmg[0] + dmg[1] + dmg[2], 450)) return "합계";
            if (ticks[0] != 8 || ticks[1] != 17 || ticks[2] != 26) return "타격 tick " + string.Join(",", ticks);
            return null;
        }

        private static string TestCancelZero()
        {
            var cat = BuildSynthetic(a => { }, d => { }, "T_IDLE", new[] { 300.0 }, new[] { 1.0 }, 0, 0, 1000);
            var rec = new EventRecorder();
            var sim = new BattleSimulation(cat, FixedRandomRules(), Duel(2.0), "t", 0, 1, rec);
            for (int i = 0; i < 30; i++) sim.Step();
            foreach (var e in rec.Events)
            {
                if (e.ActorId != 0) continue;
                if (e.Type == BattleEventType.ActionEnded && e.ActionId == "LIGHT_ATK")
                    return e.Tick == 23 && e.Reason == "완료" ? null : "종료 tick " + e.Tick + " 사유 " + e.Reason;
            }
            return "LIGHT_ATK 종료 이벤트 없음";
        }

        private static string TestBuildupDecay()
        {
            var cat = BuildSynthetic(a => { a[(int)StatId.FROST_BUILDUP] = 60; },
                                     d => { d[(int)StatId.FROST_RESIST] = 1000; }, "T_IDLE", new[] { 300.0 }, new[] { 1.0 }, 0, 0, 1000);
            var sim = new BattleSimulation(cat, FixedRandomRules(), Duel(2.0), "t", 0, 1, null);
            for (int i = 0; i <= 8; i++) sim.Step(); // tick 8 에서 타격
            var dummy = sim.Combatants[1];
            if (!Near(dummy.Buildup[(int)StatusKind.Frost], 60)) return "누적 직후 " + dummy.Buildup[(int)StatusKind.Frost];
            for (int i = 9; i <= 38; i++) sim.Step(); // 30 tick = 1초
            if (!Near(dummy.Buildup[(int)StatusKind.Frost], 55, 1e-9)) return "1초 뒤 " + dummy.Buildup[(int)StatusKind.Frost].ToString("R", Inv);
            return null;
        }

        private static string TestPoison()
        {
            var cat = BuildSynthetic(a => { a[(int)StatId.POISON_BUILDUP] = 1000; },
                                     d => { d[(int)StatId.POISON_RESIST] = 100; }, "T_IDLE", new[] { 300.0 }, new[] { 1.0 }, 0, 0, 1000);
            var rec = new EventRecorder();
            var sim = new BattleSimulation(cat, FixedRandomRules(), Duel(2.0), "t", 0, 1, rec);
            for (int i = 0; i < 400; i++) sim.Step();
            long applied = -1;
            var tickDamage = new List<long>();
            long expired = -1;
            foreach (var e in rec.Events)
            {
                if (e.StatusId != "POISON") continue;
                if (e.Type == BattleEventType.StatusApplied) applied = e.Tick;
                if (e.Type == BattleEventType.StatusTick) tickDamage.Add(e.Tick);
                if (e.Type == BattleEventType.StatusExpired) expired = e.Tick;
            }
            if (applied != 8) return "발동 tick " + applied;
            if (tickDamage.Count != 10) return "피해 횟수 " + tickDamage.Count;
            if (tickDamage[0] != applied + 30) return "첫 피해 tick " + tickDamage[0];
            for (int i = 1; i < tickDamage.Count; i++) if (tickDamage[i] - tickDamage[i - 1] != 30) return "간격";
            if (expired != tickDamage[9]) return "마지막 피해 후 만료 tick " + expired;
            return null;
        }

        private static string TestStaminaNegative()
        {
            var cat = BuildSynthetic(a => { a[(int)StatId.STAMINA] = 20; a[(int)StatId.STAMINA_RECOVERY] = 30; },
                                     d => { }, "T_IDLE", new[] { 300.0 }, new[] { 1.0 }, 0, 50, 0);
            var rec = new EventRecorder();
            var sim = new BattleSimulation(cat, FixedRandomRules(), Duel(2.0), "t", 0, 1, rec);
            for (int i = 0; i < 120; i++) sim.Step();
            var starts = new List<BattleEvent>();
            foreach (var e in rec.Events) if (e.Type == BattleEventType.ActionStarted && e.ActorId == 0 && e.ActionId == "LIGHT_ATK") starts.Add(e);
            if (starts.Count < 2) return "재시작 없음(회복 후 재사용 기대)";
            if (!Near(starts[0].ActorStaminaBefore, 20) || !Near(starts[0].ActorStaminaAfter, -30)) return "첫 사용 " + starts[0].ActorStaminaBefore + "→" + starts[0].ActorStaminaAfter;
            // 두 번째 시작 시점의 스태미나는 반드시 1 이상
            if (starts[1].ActorStaminaBefore < 1 - 1e-9) return "1 미만에서 재사용 " + starts[1].ActorStaminaBefore;
            // 행동 중 회복 정지(기본값) 기준: 800ms 행동은 tick 23 에 끝나고 tick 24 부터 초당 30 회복.
            // -30 → 1 에는 31 tick 이 필요하므로 tick 54 이전 재사용은 오류.
            if (starts[1].Tick < 54) return "너무 이른 재사용 tick " + starts[1].Tick;
            return null;
        }

        private static string TestSimultaneousAndInterrupt()
        {
            // 두 개체가 같은 tick 에 서로 첫 타격. 공격자의 강인도 피해가 상대를 경직시켜 상대의 2·3타가 취소되어야 함.
            var cat = BuildSynthetic(
                a => { a[(int)StatId.STAGGER_BUILDUP] = 100; a[(int)StatId.STAGGER_ENDURANCE] = 1e9; },
                d => { d[(int)StatId.STAGGER_ENDURANCE] = 50; d[(int)StatId.SLASH_ATTACK] = 100; },
                "T_AI", new[] { 300.0, 600.0, 900.0 }, new[] { 1.0, 1.0, 1.0 }, 0, 0, 1000);
            cat.Skills["T_ATK"].AniTimeMs = 1000;
            var rec = new EventRecorder();
            var sim = new BattleSimulation(cat, FixedRandomRules(), Duel(2.0), "t", 0, 1, rec);
            for (int i = 0; i < 40; i++) sim.Step();
            int atkHits = 0, dummyHits = 0;
            bool interrupted = false;
            long firstAtk = -1, firstDummy = -1;
            foreach (var e in rec.Events)
            {
                if (e.Type == BattleEventType.HitResolved && e.TargetId >= 0)
                {
                    if (e.ActorId == 0) { atkHits++; if (firstAtk < 0) firstAtk = e.Tick; }
                    if (e.ActorId == 1) { dummyHits++; if (firstDummy < 0) firstDummy = e.Tick; }
                }
                if (e.Type == BattleEventType.ActionInterrupted && e.ActorId == 1) interrupted = true;
            }
            if (firstAtk != 8 || firstDummy != 8) return "첫 타격 tick " + firstAtk + "/" + firstDummy;
            if (dummyHits != 1) return "상대 타격 수 " + dummyHits + " (1 기대: 동시 1타 유지, 이후 취소)";
            if (!interrupted) return "강제 중단 이벤트 없음";
            if (sim.Combatants[1].ComboNodeId != "NONE") return "콤보 초기화 안 됨";
            return null;
        }

        private static string TestWaterfallSkips()
        {
            // 쿨다운 1000초 LIGHT_ATK: 첫 사용 후 쿨다운 중이므로 다음 판단은 IDLE 행으로 넘어가야 함
            var cat = BuildSynthetic(a => { }, d => { }, "T_IDLE", new[] { 300.0 }, new[] { 1.0 }, 0, 0, 1000);
            var rec = new EventRecorder();
            var sim = new BattleSimulation(cat, FixedRandomRules(), Duel(2.0), "t", 0, 1, rec);
            for (int i = 0; i < 60; i++) sim.Step();
            int light = 0, idle = 0;
            foreach (var e in rec.Events)
            {
                if (e.Type != BattleEventType.ActionStarted || e.ActorId != 0) continue;
                if (e.ActionId == "LIGHT_ATK") light++;
                if (e.ActionId == "IDLE") idle++;
            }
            if (light != 1) return "LIGHT_ATK " + light + "회";
            if (idle == 0) return "쿨다운 중 다음 행(IDLE)으로 넘어가지 않음";
            return null;
        }

        // ------------------------------------------------------------------ 실제 데이터
        private static ulong RunHash(DataCatalog cat, CombatRules rules, BattleScenario sc, IBattleEventSink sink, out BattleResult result)
        {
            var sim = new BattleSimulation(cat, rules, sc, "selftest", 0, BatchCoordinator.SeedFor(sc, 0), sink);
            while (!sim.IsFinished) sim.Step();
            result = sim.GetResult();
            return result.EventHash;
        }

        private static string TestDeterminism(DataCatalog cat, CombatRules rules, BattleScenario sc)
        {
            BattleResult r1, r2;
            ulong h1 = RunHash(cat, rules, sc, null, out r1);
            ulong h2 = RunHash(cat, rules, sc, null, out r2);
            if (h1 != h2) return "해시 불일치 " + StableHash.Hex(h1) + " / " + StableHash.Hex(h2);
            if (r1.Ticks != r2.Ticks || r1.WinnerTeam != r2.WinnerTeam) return "결과 불일치";
            return null;
        }

        private static string TestSinkIndependence(DataCatalog cat, CombatRules rules, BattleScenario sc)
        {
            BattleResult r1, r2;
            ulong h1 = RunHash(cat, rules, sc, null, out r1);
            ulong h2 = RunHash(cat, rules, sc, new EventRecorder(), out r2);
            if (h1 != h2) return "구독자 연결 시 결과가 달라짐";
            return null;
        }
    }
}
