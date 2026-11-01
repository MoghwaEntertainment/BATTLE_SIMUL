using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ARPG.Core;
using ARPG.Core.Events;
using UnityEngine;

namespace ARPG.Runtime
{
    public enum LogLevel
    {
        Off = 0,
        Key = 1,      // 시작/타격/사망/페이즈/상태이상/종료
        Verbose = 2,  // + 모든 행동 시작·종료·타겟 변경·누적
    }

    /// <summary>
    /// 이벤트 → 문자열. Unity Console 과 화면 로그가 같은 포맷을 쓴다.
    /// </summary>
    public static class EventFormatter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool IsKey(BattleEventType t)
        {
            switch (t)
            {
                case BattleEventType.BattleStarted:
                case BattleEventType.HitResolved:
                case BattleEventType.HitAvoided:
                case BattleEventType.Guarded:
                case BattleEventType.GuardBroken:
                case BattleEventType.Staggered:
                case BattleEventType.StatusApplied:
                case BattleEventType.PhaseChanged:
                case BattleEventType.EntityDied:
                case BattleEventType.BattleEnded:
                case BattleEventType.ActionRejected:
                    return true;
                default:
                    return false;
            }
        }

        public static bool Passes(LogLevel level, in BattleEvent e)
        {
            if (level == LogLevel.Off) return false;
            if (level == LogLevel.Verbose) return true;
            if (e.Type == BattleEventType.ActionStarted && !string.IsNullOrEmpty(e.SkillId)) return true; // 스킬 사용은 핵심
            return IsKey(e.Type);
        }

        private static string Who(BattleStartInfo info, int id)
        {
            var c = info != null ? info.Find(id) : null;
            if (c == null) return id >= 0 ? "#" + id : "-";
            return c.DisplayName + "#" + c.Label;
        }

        private static string N(double v) { return double.IsNaN(v) ? "-" : v.ToString("0.00", Inv); }

        public static string Format(BattleStartInfo info, in BattleEvent e)
        {
            var sb = new StringBuilder(160);
            double sec = info != null && info.TickRate > 0 ? e.Tick / info.TickRate : 0;
            sb.Append("[회차 ").Append(info != null ? info.BattleIndex + 1 : 0).Append("][tick ").Append(e.Tick).Append(" / ").Append(sec.ToString("0.000", Inv)).Append("s] ");
            switch (e.Type)
            {
                case BattleEventType.BattleStarted:
                    sb.Append("전투 시작 ").Append(e.Result).Append(' ').Append(e.Reason);
                    break;
                case BattleEventType.ActionStarted:
                    sb.Append(Who(info, e.ActorId)).Append(" 행동 ").Append(e.ActionId);
                    if (!string.IsNullOrEmpty(e.SkillId)) sb.Append(" (").Append(e.SkillId).Append(')');
                    if (e.Result == "COMBO") sb.Append(" 콤보");
                    if (!double.IsNaN(e.ActorStaminaBefore) && e.ActorStaminaBefore != e.ActorStaminaAfter)
                        sb.Append(" | ST ").Append(N(e.ActorStaminaBefore)).Append(" → ").Append(N(e.ActorStaminaAfter));
                    if (!double.IsNaN(e.ActorFpBefore) && e.ActorFpBefore != e.ActorFpAfter)
                        sb.Append(" | FP ").Append(N(e.ActorFpBefore)).Append(" → ").Append(N(e.ActorFpAfter));
                    if (!string.IsNullOrEmpty(e.Reason)) sb.Append(" | ").Append(e.Reason);
                    break;
                case BattleEventType.HitResolved:
                    sb.Append(Who(info, e.ActorId)).Append(" → ").Append(Who(info, e.TargetId)).Append('\n');
                    sb.Append(e.SkillId);
                    if (e.HitIndex > 0) sb.Append(" ").Append(e.HitIndex).Append("타");
                    if (e.Result == "MISS") sb.Append(" | 빗나감");
                    else
                    {
                        sb.Append(" | 피해 ").Append(N(e.Damage)).Append(" | 대상 HP ").Append(N(e.TargetHpBefore)).Append(" → ").Append(N(e.TargetHpAfter));
                        if (!double.IsNaN(e.PoiseBefore)) sb.Append(" | 강인도 ").Append(N(e.PoiseBefore)).Append(" → ").Append(N(e.PoiseAfter));
                        if (e.Result == "HIT_GROGGY") sb.Append(" (그로기)");
                    }
                    break;
                case BattleEventType.HitAvoided:
                    sb.Append(Who(info, e.TargetId)).Append(" 무적 회피 ← ").Append(Who(info, e.ActorId)).Append(' ').Append(e.SkillId);
                    break;
                case BattleEventType.Guarded:
                    sb.Append(Who(info, e.TargetId)).Append(" 가드 ← ").Append(Who(info, e.ActorId)).Append(' ').Append(e.SkillId)
                      .Append(" | HP 피해 ").Append(N(e.Damage)).Append(" | ").Append(e.Reason);
                    break;
                case BattleEventType.GuardBroken:
                    sb.Append(Who(info, e.ActorId)).Append(" 가드 브레이크 ").Append(e.Reason);
                    break;
                case BattleEventType.Staggered:
                    sb.Append(Who(info, e.ActorId)).Append(" 경직(강인도 붕괴) ").Append(e.Reason);
                    break;
                case BattleEventType.ResourceChanged:
                    sb.Append(Who(info, e.ActorId)).Append(" 스태미나 ").Append(N(e.ActorStaminaBefore)).Append(" → ").Append(N(e.ActorStaminaAfter)).Append(" | ").Append(e.Reason);
                    break;
                case BattleEventType.StatusBuildup:
                    sb.Append(Who(info, e.TargetId)).Append(' ').Append(e.StatusId).Append(" 누적 ").Append(e.Reason);
                    break;
                case BattleEventType.StatusApplied:
                    sb.Append(Who(info, e.ActorId)).Append(' ').Append(e.StatusId).Append(" 발동");
                    if (!double.IsNaN(e.Damage)) sb.Append(" 피해 ").Append(N(e.Damage)).Append(" HP ").Append(N(e.ActorHpBefore)).Append(" → ").Append(N(e.ActorHpAfter));
                    sb.Append(" | ").Append(e.Reason);
                    break;
                case BattleEventType.StatusTick:
                    sb.Append(Who(info, e.ActorId)).Append(' ').Append(e.StatusId).Append(" 피해 ").Append(N(e.Damage)).Append(" HP ").Append(N(e.ActorHpBefore)).Append(" → ").Append(N(e.ActorHpAfter));
                    break;
                case BattleEventType.StatusExpired:
                    sb.Append(Who(info, e.ActorId)).Append(' ').Append(e.StatusId).Append(" 해제");
                    break;
                case BattleEventType.PhaseChanged:
                    sb.Append(Who(info, e.ActorId)).Append(" 페이즈 ").Append(e.Result).Append(" | ").Append(e.Reason);
                    break;
                case BattleEventType.TargetChanged:
                    sb.Append(Who(info, e.ActorId)).Append(" 타겟 → ").Append(Who(info, e.TargetId));
                    break;
                case BattleEventType.EntityDied:
                    sb.Append(Who(info, e.ActorId)).Append(" 사망 (").Append(e.Reason).Append(')');
                    break;
                case BattleEventType.ActionEnded:
                case BattleEventType.ActionInterrupted:
                    sb.Append(Who(info, e.ActorId)).Append(e.Type == BattleEventType.ActionInterrupted ? " 행동 중단 " : " 행동 종료 ").Append(e.ActionId).Append(" | ").Append(e.Reason);
                    break;
                case BattleEventType.ActionRejected:
                    sb.Append(Who(info, e.ActorId)).Append(" 행동 거절 ").Append(e.ActionId).Append(" | ").Append(e.Reason);
                    break;
                case BattleEventType.BattleEnded:
                    sb.Append("전투 종료 ").Append(e.Result).Append(" | ").Append(e.Reason);
                    break;
                default:
                    sb.Append(e.Type);
                    break;
            }
            return sb.ToString();
        }
    }

    /// <summary>Unity Console 출력 + 화면 로그용 최근 줄 보관.</summary>
    public sealed class UnityConsoleSink : IBattleEventSink
    {
        public LogLevel ConsoleLevel = LogLevel.Key;
        public LogLevel ScreenLevel = LogLevel.Key;
        public int ScreenCapacity = 200;
        private BattleStartInfo _info;
        public readonly LinkedList<string> Recent = new LinkedList<string>();

        public void OnBattleStarted(BattleStartInfo info)
        {
            _info = info;
            Recent.Clear();
        }

        public void OnEvent(in BattleEvent e)
        {
            bool toConsole = EventFormatter.Passes(ConsoleLevel, in e);
            bool toScreen = EventFormatter.Passes(ScreenLevel, in e);
            if (!toConsole && !toScreen) return;
            string line = EventFormatter.Format(_info, in e);
            if (toConsole) Debug.Log(line);
            if (toScreen)
            {
                Recent.AddLast(line.Replace('\n', ' '));
                while (Recent.Count > ScreenCapacity) Recent.RemoveFirst();
            }
        }

        public void OnBattleEnded(BattleResult result)
        {
            if (ConsoleLevel != LogLevel.Off)
                Debug.Log("[회차 " + (result.BattleIndex + 1) + "] 결과: " + result.OutcomeText + " | " + result.DurationSec.ToString("0.0", Inv) + "초 | 이벤트 해시 " + StableHash.Hex(result.EventHash));
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    }
}
