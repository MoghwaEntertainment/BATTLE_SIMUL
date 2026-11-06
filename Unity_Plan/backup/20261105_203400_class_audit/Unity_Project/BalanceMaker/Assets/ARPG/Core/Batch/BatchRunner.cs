using System;
using System.Collections.Generic;
using System.Diagnostics;
using ARPG.Core.Events;
using ARPG.Core.Simulation;

namespace ARPG.Core.Batch
{
    /// <summary>반복 결과 집계. 사용자 중단·오류는 분모에서 제외하고 횟수만 따로 센다.</summary>
    public sealed class BatchSummary
    {
        public int Requested;
        public int Completed;      // 승패/동시 전멸/시간 초과로 끝난 유효 전투
        public int Aborted;
        public int Errors;
        public int MutualWipes;
        public int Timeouts;
        public readonly SortedDictionary<int, int> TeamWins = new SortedDictionary<int, int>();
        public readonly SortedDictionary<int, double> TeamAliveSum = new SortedDictionary<int, double>();
        public readonly SortedDictionary<int, double> TeamHpRatioSum = new SortedDictionary<int, double>();
        public readonly SortedDictionary<int, double> TeamDamageSum = new SortedDictionary<int, double>();
        public readonly List<double> Durations = new List<double>();
        public readonly Dictionary<string, SkillStat> SkillTotals = new Dictionary<string, SkillStat>();
        public long TotalTicks;
        public double ElapsedRealSec;

        public void Add(BattleResult r)
        {
            if (r.Reason == BattleEndReason.UserAborted) { Aborted++; return; }
            if (r.Reason == BattleEndReason.Error) { Errors++; return; }
            Completed++;
            TotalTicks += r.Ticks;
            Durations.Add(r.DurationSec);
            if (r.Reason == BattleEndReason.TeamVictory)
            {
                int w;
                TeamWins.TryGetValue(r.WinnerTeam, out w);
                TeamWins[r.WinnerTeam] = w + 1;
            }
            else if (r.Reason == BattleEndReason.MutualWipe) MutualWipes++;
            else if (r.Reason == BattleEndReason.Timeout) Timeouts++;

            var teams = new HashSet<int>();
            foreach (var c in r.Combatants) teams.Add(c.TeamId);
            foreach (int t in teams)
            {
                if (!TeamWins.ContainsKey(t)) TeamWins[t] = 0;
                Accumulate(TeamAliveSum, t, r.AliveCount(t));
                double max = r.TeamMaxHp(t);
                Accumulate(TeamHpRatioSum, t, max > 0 ? r.TeamHp(t) / max : 0);
                Accumulate(TeamDamageSum, t, r.TeamDamageDealt(t));
            }
            foreach (var kv in r.SkillStats)
            {
                SkillStat s;
                if (!SkillTotals.TryGetValue(kv.Key, out s))
                {
                    s = new SkillStat { TeamId = kv.Value.TeamId, SkillId = kv.Value.SkillId };
                    SkillTotals[kv.Key] = s;
                }
                s.Uses += kv.Value.Uses;
                s.Hits += kv.Value.Hits;
                s.Guarded += kv.Value.Guarded;
                s.Avoided += kv.Value.Avoided;
                s.Damage += kv.Value.Damage;
            }
        }

        private static void Accumulate(SortedDictionary<int, double> d, int k, double v)
        {
            double cur;
            d.TryGetValue(k, out cur);
            d[k] = cur + v;
        }

        public int Wins(int team)
        {
            int w;
            return TeamWins.TryGetValue(team, out w) ? w : 0;
        }

        /// <summary>승률 = 팀 승리 / 완료된 유효 전투(무승부·시간 초과 포함).</summary>
        public double WinRate(int team) { return Completed > 0 ? Wins(team) / (double)Completed : 0; }

        /// <summary>보조 지표: 승패가 난 전투 중 승률.</summary>
        public double DecisiveWinRate(int team)
        {
            int decisive = 0;
            foreach (var kv in TeamWins) decisive += kv.Value;
            return decisive > 0 ? Wins(team) / (double)decisive : 0;
        }

        /// <summary>Wilson 95% 신뢰구간.</summary>
        public void WilsonInterval(int team, out double lo, out double hi)
        {
            int n = Completed;
            if (n == 0) { lo = 0; hi = 0; return; }
            double p = WinRate(team), z = 1.959963984540054;
            double den = 1 + z * z / n;
            double center = (p + z * z / (2 * n)) / den;
            double half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / den;
            lo = Math.Max(0, center - half);
            hi = Math.Min(1, center + half);
        }

        public double MeanDuration()
        {
            if (Durations.Count == 0) return 0;
            double s = 0;
            foreach (var d in Durations) s += d;
            return s / Durations.Count;
        }

        public double Percentile(double q)
        {
            if (Durations.Count == 0) return 0;
            var sorted = new List<double>(Durations);
            sorted.Sort();
            double pos = (sorted.Count - 1) * q;
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }

        public double AvgAlive(int team)
        {
            double v;
            return Completed > 0 && TeamAliveSum.TryGetValue(team, out v) ? v / Completed : 0;
        }

        public double AvgHpRatio(int team)
        {
            double v;
            return Completed > 0 && TeamHpRatioSum.TryGetValue(team, out v) ? v / Completed : 0;
        }

        public double AvgDamage(int team)
        {
            double v;
            return Completed > 0 && TeamDamageSum.TryGetValue(team, out v) ? v / Completed : 0;
        }
    }

    /// <summary>
    /// 비시각 반복 실행. 같은 Core Step 을 실제 시간 대기 없이 반복 호출한다.
    /// Run(budgetMs) 를 프레임마다 호출하면 UI 에 제어를 돌려주며 진행하고, headless 에서는 끝날 때까지 호출한다.
    /// 취소는 tick 경계에서 반영되며 진행 중 회차는 UserAborted 로 기록된다.
    /// </summary>
    public sealed class BatchCoordinator
    {
        private readonly DataCatalog _cat;
        private readonly CombatRules _rules;
        private readonly BattleScenario _scenario;
        private readonly Func<int, IBattleEventSink> _sinkFactory;
        private readonly Action<BattleResult> _onResult;
        private readonly Stopwatch _total = new Stopwatch();

        public readonly string RunId;
        public readonly int Runs;
        public readonly BatchSummary Summary = new BatchSummary();
        public int NextIndex { get; private set; }
        public bool Finished { get; private set; }
        public bool CancelRequested { get; private set; }
        public BattleSimulation Current { get; private set; }
        public string ErrorMessage { get; private set; }
        public double Progress01 { get { return Runs > 0 ? (NextIndex + (Current != null && Current.MaxTicks > 0 ? Current.Tick / (double)Current.MaxTicks : 0)) / Runs : 1; } }
        public double ElapsedSec { get { return _total.Elapsed.TotalSeconds; } }

        public BatchCoordinator(DataCatalog cat, CombatRules rules, BattleScenario scenario, string runId, int runs,
                                Func<int, IBattleEventSink> sinkFactory, Action<BattleResult> onResult)
        {
            _cat = cat;
            _rules = rules;
            _scenario = scenario;
            RunId = runId;
            Runs = Math.Max(0, runs);
            _sinkFactory = sinkFactory;
            _onResult = onResult;
            Summary.Requested = Runs;
        }

        public static ulong SeedFor(BattleScenario sc, int battleIndex)
        {
            return DeterministicRandom.SeedForBattle(unchecked((ulong)sc.masterSeed), battleIndex);
        }

        public void Cancel() { CancelRequested = true; }

        /// <summary>budgetMs 동안 진행. budgetMs &lt;= 0 이면 끝까지 실행.</summary>
        public void Run(double budgetMs)
        {
            if (Finished) return;
            _total.Start();
            var sw = Stopwatch.StartNew();
            try
            {
                while (!Finished)
                {
                    if (CancelRequested)
                    {
                        if (Current != null)
                        {
                            Current.Abort();
                            Record(Current.GetResult());
                            Current = null;
                        }
                        Finished = true;
                        break;
                    }
                    if (Current == null)
                    {
                        if (NextIndex >= Runs) { Finished = true; break; }
                        IBattleEventSink sink = _sinkFactory != null ? _sinkFactory(NextIndex) : null;
                        Current = new BattleSimulation(_cat, _rules, _scenario, RunId, NextIndex, SeedFor(_scenario, NextIndex), sink);
                    }
                    // 배치 간 Stopwatch 확인 비용을 줄이기 위해 64 tick 단위로 검사
                    for (int i = 0; i < 64 && !Current.IsFinished; i++) Current.Step();
                    if (Current.IsFinished)
                    {
                        Record(Current.GetResult());
                        Current = null;
                        NextIndex++;
                    }
                    if (budgetMs > 0 && sw.Elapsed.TotalMilliseconds >= budgetMs) break;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.ToString();
                Summary.Errors++;
                Finished = true;
            }
            finally
            {
                _total.Stop();
                Summary.ElapsedRealSec = _total.Elapsed.TotalSeconds;
            }
        }

        private void Record(BattleResult r)
        {
            Summary.Add(r);
            if (_onResult != null) _onResult(r);
        }
    }
}
