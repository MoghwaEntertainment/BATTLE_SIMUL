using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ARPG.Core;
using ARPG.Core.Batch;
using ARPG.Core.Events;

namespace ARPG.Runtime
{
    public static class Csv
    {
        public const int SchemaVersion = 1;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>쉼표·따옴표·개행이 있으면 따옴표로 감싸고 내부 따옴표는 두 번 기록.</summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            bool need = s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0;
            if (!need) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>문화권 무관 '.' 소수점, 고정 정밀도(소수 4자리). NaN 은 빈칸(실제 0 과 구별).</summary>
        public static string Num(double v)
        {
            if (double.IsNaN(v)) return string.Empty;
            if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
            return v.ToString("0.####", Inv);
        }

        public static string Int(long v) { return v.ToString(Inv); }

        public static StreamWriter OpenTemp(string finalPath, out string tempPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
            tempPath = finalPath + ".partial";
            var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            return new StreamWriter(fs, new UTF8Encoding(true)); // UTF-8 BOM: 한글 Excel 호환
        }

        /// <summary>정상 종료 시 임시 파일을 최종 이름으로 전환. 부분 결과면 이름에 _partial 을 붙인다.</summary>
        public static string Commit(StreamWriter w, string tempPath, string finalPath, bool partial)
        {
            w.Flush();
            w.Dispose();
            string target = partial
                ? Path.Combine(Path.GetDirectoryName(finalPath), Path.GetFileNameWithoutExtension(finalPath) + "_partial" + Path.GetExtension(finalPath))
                : finalPath;
            if (File.Exists(target)) File.Delete(target);
            File.Move(tempPath, target);
            return target;
        }
    }

    /// <summary>
    /// 전투 이벤트 CSV. Console 문자열을 다시 파싱하지 않고 구조화 이벤트를 직접 기록한다.
    /// 메모리에 누적하지 않고 버퍼링해 순차 기록한다.
    /// </summary>
    public sealed class CsvEventSink : IBattleEventSink, IDisposable
    {
        public const string Header =
            "schema_version,run_id,battle_index,event_sequence,seed,tick,sim_time_sec," +
            "event_type,actor_id,actor_label,actor_definition_id,actor_name,team_id," +
            "target_id,target_label,target_name,action_id,skill_id,hit_index,damage_type,damage," +
            "actor_hp_before,actor_hp_after,actor_fp_before,actor_fp_after," +
            "actor_stamina_before,actor_stamina_after,target_hp_before,target_hp_after," +
            "poise_before,poise_after,status_id,distance_m,result,reason";

        private readonly string _finalPath;
        private string _tempPath;
        private StreamWriter _w;
        private BattleStartInfo _info;
        private readonly Func<int, bool> _battleFilter;
        private bool _active;
        public long Rows { get; private set; }
        public string Error { get; private set; }
        public string WrittenPath { get; private set; }

        /// <param name="battleFilter">회차 선택(null = 전체)</param>
        public CsvEventSink(string finalPath, Func<int, bool> battleFilter = null)
        {
            _finalPath = finalPath;
            _battleFilter = battleFilter;
            try
            {
                _w = Csv.OpenTemp(finalPath, out _tempPath);
                _w.WriteLine(Header);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _w = null;
            }
        }

        public void OnBattleStarted(BattleStartInfo info)
        {
            _info = info;
            _active = _w != null && (_battleFilter == null || _battleFilter(info.BattleIndex));
        }

        public void OnEvent(in BattleEvent e)
        {
            if (!_active || _w == null) return;
            try
            {
                var a = _info.Find(e.ActorId);
                var t = _info.Find(e.TargetId);
                var sb = new StringBuilder(256);
                sb.Append(Csv.SchemaVersion).Append(',');
                sb.Append(Csv.Esc(_info.RunId)).Append(',');
                sb.Append(_info.BattleIndex).Append(',');
                sb.Append(e.Sequence).Append(',');
                sb.Append(_info.Seed).Append(',');
                sb.Append(e.Tick).Append(',');
                sb.Append(Csv.Num(e.Tick / _info.TickRate)).Append(',');
                sb.Append(e.Type).Append(',');
                sb.Append(e.ActorId >= 0 ? e.ActorId.ToString(CultureInfo.InvariantCulture) : string.Empty).Append(',');
                sb.Append(a != null ? Csv.Esc(a.Label) : string.Empty).Append(',');
                sb.Append(a != null ? Csv.Esc(a.DefinitionId) : string.Empty).Append(',');
                sb.Append(a != null ? Csv.Esc(a.DisplayName) : string.Empty).Append(',');
                sb.Append(a != null ? a.TeamId.ToString(CultureInfo.InvariantCulture) : string.Empty).Append(',');
                sb.Append(e.TargetId >= 0 ? e.TargetId.ToString(CultureInfo.InvariantCulture) : string.Empty).Append(',');
                sb.Append(t != null ? Csv.Esc(t.Label) : string.Empty).Append(',');
                sb.Append(t != null ? Csv.Esc(t.DisplayName) : string.Empty).Append(',');
                sb.Append(Csv.Esc(e.ActionId)).Append(',');
                sb.Append(Csv.Esc(e.SkillId)).Append(',');
                sb.Append(e.HitIndex > 0 ? e.HitIndex.ToString(CultureInfo.InvariantCulture) : string.Empty).Append(',');
                sb.Append(Csv.Esc(e.DamageType)).Append(',');
                sb.Append(Csv.Num(e.Damage)).Append(',');
                sb.Append(Csv.Num(e.ActorHpBefore)).Append(',').Append(Csv.Num(e.ActorHpAfter)).Append(',');
                sb.Append(Csv.Num(e.ActorFpBefore)).Append(',').Append(Csv.Num(e.ActorFpAfter)).Append(',');
                sb.Append(Csv.Num(e.ActorStaminaBefore)).Append(',').Append(Csv.Num(e.ActorStaminaAfter)).Append(',');
                sb.Append(Csv.Num(e.TargetHpBefore)).Append(',').Append(Csv.Num(e.TargetHpAfter)).Append(',');
                sb.Append(Csv.Num(e.PoiseBefore)).Append(',').Append(Csv.Num(e.PoiseAfter)).Append(',');
                sb.Append(Csv.Esc(e.StatusId)).Append(',');
                sb.Append(Csv.Num(e.DistanceM)).Append(',');
                sb.Append(Csv.Esc(e.Result)).Append(',');
                sb.Append(Csv.Esc(e.Reason));
                _w.WriteLine(sb.ToString());
                Rows++;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _active = false;
            }
        }

        public void OnBattleEnded(BattleResult result) { }

        /// <summary>파일을 닫고 최종 이름으로 전환. partial=true 이면 '_partial' 표시.</summary>
        public string Close(bool partial)
        {
            if (_w == null) return null;
            try
            {
                WrittenPath = Csv.Commit(_w, _tempPath, _finalPath, partial);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            _w = null;
            return WrittenPath;
        }

        /// <summary>기록할 가치가 없는 파일(진행 0 tick)을 저장하지 않고 삭제.</summary>
        public void Discard()
        {
            if (_w == null) return;
            try
            {
                _w.Dispose();
                if (File.Exists(_tempPath)) File.Delete(_tempPath);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            _w = null;
        }

        public void Dispose()
        {
            if (_w != null) Close(true);
        }
    }

    /// <summary>회차별 한 행 results.csv (스트리밍 기록).</summary>
    public sealed class ResultsCsvWriter : IDisposable
    {
        private readonly string _finalPath;
        private string _tempPath;
        private StreamWriter _w;
        // [2026-11-06 작업 목록 D] 같은 폴더에 개체별 결과(entity_<결과 파일 이름>)를 함께 기록한다. 모든 판, 판×개체 1줄.
        private readonly string _entityFinalPath;
        private string _entityTempPath;
        private StreamWriter _ew;
        public string Error { get; private set; }
        public string WrittenPath { get; private set; }
        public string EntityWrittenPath { get; private set; }

        public ResultsCsvWriter(string finalPath)
        {
            _finalPath = finalPath;
            try
            {
                _w = Csv.OpenTemp(finalPath, out _tempPath);
                _w.WriteLine("schema_version,run_id,battle_index,seed,scenario,data_hash,rules_version,outcome,end_reason,winner_team," +
                             "ticks,duration_sec,team0_alive,team0_count,team0_hp,team0_max_hp,team0_damage_dealt," +
                             "team1_alive,team1_count,team1_hp,team1_max_hp,team1_damage_dealt,event_count,event_hash");
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            try
            {
                _entityFinalPath = Path.Combine(Path.GetDirectoryName(finalPath) ?? string.Empty, "entity_" + Path.GetFileName(finalPath));
                _ew = Csv.OpenTemp(_entityFinalPath, out _entityTempPath);
                _ew.WriteLine("schema_version,run_id,battle_index,entity_id,label,team_id,definition_id,class_name,alive,died,death_tick," +
                              "alive_sec,max_hp,hp_end,damage_dealt,damage_taken,hits_landed,hits_taken,hits_guarded,hits_avoided," +
                              "stagger_received,resource_blocked");
            }
            catch (Exception ex)
            {
                if (Error == null) Error = ex.Message;
                _ew = null;
            }
        }

        public void Write(BattleResult r)
        {
            if (_w == null) return;
            try
            {
                var sb = new StringBuilder();
                sb.Append(Csv.SchemaVersion).Append(',').Append(Csv.Esc(r.RunId)).Append(',').Append(r.BattleIndex).Append(',').Append(r.Seed).Append(',');
                sb.Append(Csv.Esc(r.ScenarioName)).Append(',').Append(Csv.Esc(r.DataHash)).Append(',').Append(Csv.Esc(r.RulesVersion)).Append(',');
                sb.Append(Csv.Esc(r.OutcomeText)).Append(',').Append(r.Reason).Append(',').Append(r.WinnerTeam >= 0 ? r.WinnerTeam.ToString(CultureInfo.InvariantCulture) : string.Empty).Append(',');
                sb.Append(r.Ticks).Append(',').Append(Csv.Num(r.DurationSec)).Append(',');
                for (int t = 0; t < 2; t++)
                {
                    sb.Append(r.AliveCount(t)).Append(',').Append(r.MemberCount(t)).Append(',');
                    sb.Append(Csv.Num(r.TeamHp(t))).Append(',').Append(Csv.Num(r.TeamMaxHp(t))).Append(',').Append(Csv.Num(r.TeamDamageDealt(t))).Append(',');
                }
                sb.Append(r.EventCount).Append(',').Append(StableHash.Hex(r.EventHash));
                _w.WriteLine(sb.ToString());
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            WriteEntities(r);
        }

        private void WriteEntities(BattleResult r)
        {
            if (_ew == null) return;
            try
            {
                double ticksPerSec = r.DurationSec > 0 ? r.Ticks / r.DurationSec : 30.0;
                var sb = new StringBuilder();
                foreach (var c in r.Combatants)
                {
                    bool died = !c.Alive && c.DeathTick >= 0;
                    double aliveSec = died ? c.DeathTick / ticksPerSec : r.DurationSec;
                    sb.Length = 0;
                    sb.Append(Csv.SchemaVersion).Append(',').Append(Csv.Esc(r.RunId)).Append(',').Append(r.BattleIndex).Append(',');
                    sb.Append(c.EntityId).Append(',').Append(Csv.Esc(c.Label)).Append(',').Append(c.TeamId).Append(',');
                    sb.Append(Csv.Esc(c.DefinitionId)).Append(',').Append(Csv.Esc(c.Name)).Append(',');
                    sb.Append(c.Alive ? 1 : 0).Append(',').Append(died ? 1 : 0).Append(',').Append(died ? Csv.Int(c.DeathTick) : string.Empty).Append(',');
                    sb.Append(Csv.Num(aliveSec)).Append(',').Append(Csv.Num(c.MaxHp)).Append(',').Append(Csv.Num(Math.Max(0, c.Hp))).Append(',');
                    sb.Append(Csv.Num(c.DamageDealt)).Append(',').Append(Csv.Num(c.DamageTaken)).Append(',');
                    sb.Append(c.HitsLanded).Append(',').Append(c.HitsTaken).Append(',').Append(c.HitsGuarded).Append(',').Append(c.HitsAvoided).Append(',');
                    sb.Append(c.StaggerCount).Append(',').Append(c.ResourceBlockedCount);
                    _ew.WriteLine(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                if (Error == null) Error = ex.Message;
            }
        }

        public string Close(bool partial)
        {
            if (_ew != null)
            {
                try { EntityWrittenPath = Csv.Commit(_ew, _entityTempPath, _entityFinalPath, partial); }
                catch (Exception ex) { if (Error == null) Error = ex.Message; }
                _ew = null;
            }
            if (_w == null) return null;
            try { WrittenPath = Csv.Commit(_w, _tempPath, _finalPath, partial); }
            catch (Exception ex) { Error = ex.Message; }
            _w = null;
            return WrittenPath;
        }

        public void Dispose() { if (_w != null || _ew != null) Close(true); }
    }

    [Serializable]
    public sealed class RunManifest
    {
        public int schemaVersion = Csv.SchemaVersion;
        public string runId;
        public string createdAt;
        public string appVersion;
        public string unityVersion;
        public string mode;
        public string scenarioName;
        public string scenarioSignature;
        public long masterSeed;
        public int runsRequested;
        public int runsCompleted;
        public int runsAborted;
        public bool partial;
        public string dataHash;
        public string dataGeneratedAt;
        public string rulesVersion;
        public string rulesHash;
        public string rulesSource;
        public string eventsCsv;
        public double realSeconds;
        public string notes;
        public BattleScenario scenario;
    }

    public static class SummaryExporter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string WriteSummary(string path, BatchSummary s, BattleScenario sc, string runId, bool partial)
        {
            string tmp;
            var w = Csv.OpenTemp(path, out tmp);
            w.WriteLine("metric,value");
            Action<string, string> row = (k, v) => w.WriteLine(Csv.Esc(k) + "," + Csv.Esc(v));
            row("run_id", runId);
            row("scenario", sc.name);
            row("partial", partial ? "true" : "false");
            row("runs_requested", s.Requested.ToString(Inv));
            row("runs_completed", s.Completed.ToString(Inv));
            row("runs_aborted", s.Aborted.ToString(Inv));
            row("runs_error", s.Errors.ToString(Inv));
            row("team0_wins", s.Wins(0).ToString(Inv));
            row("team1_wins", s.Wins(1).ToString(Inv));
            row("mutual_wipe", s.MutualWipes.ToString(Inv));
            row("timeout", s.Timeouts.ToString(Inv));
            double lo, hi;
            s.WilsonInterval(0, out lo, out hi);
            row("team0_win_rate(완료 기준)", Csv.Num(s.WinRate(0)));
            row("team0_win_rate_95ci_low", Csv.Num(lo));
            row("team0_win_rate_95ci_high", Csv.Num(hi));
            row("team0_decisive_win_rate(승패난 전투 기준)", Csv.Num(s.DecisiveWinRate(0)));
            row("duration_mean_sec", Csv.Num(s.MeanDuration()));
            row("duration_median_sec", Csv.Num(s.Percentile(0.5)));
            row("duration_p10_sec", Csv.Num(s.Percentile(0.1)));
            row("duration_p90_sec", Csv.Num(s.Percentile(0.9)));
            for (int t = 0; t < 2; t++)
            {
                row("team" + t + "_avg_alive", Csv.Num(s.AvgAlive(t)));
                row("team" + t + "_avg_hp_ratio", Csv.Num(s.AvgHpRatio(t)));
                row("team" + t + "_avg_damage_dealt", Csv.Num(s.AvgDamage(t)));
            }
            row("total_ticks", s.TotalTicks.ToString(Inv));
            row("real_seconds", Csv.Num(s.ElapsedRealSec));
            row("battles_per_sec", Csv.Num(s.ElapsedRealSec > 0 ? (s.Completed + s.Aborted) / s.ElapsedRealSec : 0));
            row("ticks_per_sec", Csv.Num(s.ElapsedRealSec > 0 ? s.TotalTicks / s.ElapsedRealSec : 0));
            if (s.Completed < 30) row("note", "완료 전투 수가 적어 승률을 확정적으로 해석하지 마세요");
            return Csv.Commit(w, tmp, path, false);
        }

        public static string WriteSkillStats(string path, BatchSummary s)
        {
            string tmp;
            var w = Csv.OpenTemp(path, out tmp);
            w.WriteLine("team_id,skill_id,uses,hits,guarded,avoided,damage_total,damage_per_use,hits_per_use");
            var keys = new List<string>(s.SkillTotals.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                var st = s.SkillTotals[k];
                w.WriteLine(st.TeamId + "," + Csv.Esc(st.SkillId) + "," + st.Uses + "," + st.Hits + "," + st.Guarded + "," + st.Avoided + "," +
                            Csv.Num(st.Damage) + "," + Csv.Num(st.Uses > 0 ? st.Damage / st.Uses : 0) + "," + Csv.Num(st.Uses > 0 ? st.Hits / (double)st.Uses : 0));
            }
            return Csv.Commit(w, tmp, path, false);
        }

        public static string WriteManifest(string path, RunManifest m)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".partial";
            File.WriteAllText(tmp, UnityEngine.JsonUtility.ToJson(m, true), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return path;
        }
    }
}
