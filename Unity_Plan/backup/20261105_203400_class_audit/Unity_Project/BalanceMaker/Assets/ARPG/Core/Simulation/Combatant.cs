using System.Collections.Generic;

namespace ARPG.Core.Simulation
{
    /// <summary>실행 중 행동 1회분. 행동 진행 시간(ms)은 행동 속도 배율을 반영해 누적한다.</summary>
    public sealed class ActionInstance
    {
        public int InstanceId;
        public string ActionId;          // AI Action_Id (LIGHT_ATK, DODGE ...)
        public ActionKind Kind;
        public SkillDefinition Skill;
        public string NodeId;            // 무브셋 노드
        public bool IsComboChain;
        public int AiRuleIndex;          // Behavior_Index (강제/폴백이면 0)
        public string Reason;

        public long StartTick;
        public double RealElapsedMs;     // 논리 tick 경과 ms. 행동 속도·빙결과 무관한 무적 판정 시계.
        public double ElapsedMs;         // 행동 진행 시간(차지 중에는 음수)
        public bool HasAdvanced;
        public double DurationMs;
        public double CancelMs;          // 0 = 끝까지 수행
        public int NextHit;              // 다음에 처리할 타격 번호(0부터)
        public double ChargeMs;

        public MoveMode Move;
        public double MoveSpeed;         // m/s
        public Vec2 FixedDir;            // 회피/백스텝 방향
        public double TotalDistance;     // 회피/백스텝/스킬 전진 거리
        public Vec2 StartFacing;
        public Combatant TargetAtStart;

        public double IFrameStartMs = -1, IFrameEndMs = -1;

        public bool InCancelWindow { get { return CancelMs > 0 && ElapsedMs >= CancelMs - 1e-6; } }
        public bool IsFinished { get { return ElapsedMs >= DurationMs - 1e-6; } }
        public bool IsSoft { get { return Kind == ActionKind.Idle || Kind == ActionKind.Move; } }
        public double Progress01 { get { return DurationMs > 0 ? System.Math.Max(0, System.Math.Min(1, ElapsedMs / DurationMs)) : 1; } }

        public bool IsAttackWithHits { get { return Kind == ActionKind.Skill && Skill != null && Skill.IsAttack && Skill.HasHits; } }

        /// <summary>마지막 타격 이후(공격 후딜) 구간인가.</summary>
        public bool InAttackRecovery
        {
            get { return IsAttackWithHits && NextHit >= Skill.HitTimesMs.Length; }
        }
    }

    /// <summary>개체에 걸린 지속 버프. 같은 Buff_ID 재적용은 시간·시전자·적용 순서를 갱신한다.</summary>
    public sealed class ActiveBuff
    {
        public BuffDefinition Def;
        public int SourceId;      // 건 개체 EntityId
        public string SkillId;    // 건 스킬
        public long StartTick;
        public long ApplyOrder;   // Most recently applied/refreshed taunt wins.
        public long EndTick;      // 이 tick 의 시간 처리에서 해제
    }

    public sealed class SkillStat
    {
        public int TeamId;
        public string SkillId;
        public long Uses;
        public long Hits;
        public long Guarded;
        public long Avoided;
        public double Damage;
    }

    /// <summary>전투별 가변 상태. 정의(CharacterDefinition)는 공유하되 수정하지 않는다.</summary>
    public sealed class Combatant
    {
        public int EntityId;
        public string Label;
        public int TeamId;
        public CharacterDefinition Def;
        public bool IsMonster;
        public double[] BaseStats;       // Def.Stats 참조(읽기 전용, 버프 없는 원래 값)
        public double[] Stats;           // 이 개체 전용 복사본 = BaseStats + 걸려 있는 버프 수치
        public readonly List<ActiveBuff> Buffs = new List<ActiveBuff>();
        public long BuffsApplied;        // 받은 버프·디버프 횟수(갱신 포함)
        public double Radius;

        public Vec2 Pos, PrevPos;
        public Vec2 Facing, PrevFacing;

        public double Hp, MaxHp, Fp, MaxFp, Stamina, MaxStamina, StaminaRegenPerSec;
        public bool ResourcesManaged;    // 몬스터 FP/스태미나 미관리 시 false

        public double Poise, MaxPoise;
        public long PoiseRecoverTick = -1;

        public bool Dead;
        public long DeathTick = -1;

        public ActionInstance Action;
        public long StaggerUntilTick = -1;
        public long StunUntilTick = -1;
        public bool StunFromGuardBreak;
        public long PoiseBrokenUntilTick = -1;

        // 상태이상
        public readonly double[] Buildup = new double[4];
        public long PoisonNextTick = -1;
        public long PoisonEndTick = -1;
        public int PoisonTicksLeft;
        public long FrostEndTick = -1;

        // AI
        public AiProfile Ai;
        public Combatant Target;
        public bool Perceived;
        public int Phase = 1;
        public double PhaseDmgMult = 1;
        public double PhaseSpeedMult = 1;
        public string PendingForcedActionId;
        public readonly Dictionary<string, long> CooldownReadyTick = new Dictionary<string, long>();
        public readonly List<string> RecentActions = new List<string>();
        public string LastActionId = string.Empty;
        public string ComboNodeId = "NONE";
        public long ComboExpireTick = -1;
        public int LastAiRule;
        public string LastAiNote = string.Empty;

        // 누적 통계
        public double DamageDealt, DamageTaken, StaminaSpent;
        public long HitsLanded, HitsTaken, HitsGuarded, HitsAvoided, StaggerCount, ResourceBlockedCount;

        public bool Alive { get { return !Dead; } }
        public bool IsStunned(long tick) { return tick < StunUntilTick; }
        public bool IsStaggered(long tick) { return tick < StaggerUntilTick; }
        public bool IsLocked(long tick) { return IsStunned(tick) || IsStaggered(tick); }
        public bool PoisonActive { get { return PoisonEndTick >= 0 || PoisonTicksLeft > 0; } }
        public bool FrostActive(long tick) { return tick < FrostEndTick; }
        public double HpPct { get { return MaxHp > 0 ? Hp / MaxHp * 100.0 : 0; } }

        public double Stat(StatId id) { return Stats[(int)id]; }

        public string Name { get { return Def.DisplayName; } }

        public TargetState StateFor(long tick)
        {
            if (IsStunned(tick)) return StunFromGuardBreak ? TargetState.guard_broken : TargetState.staggered;
            if (IsStaggered(tick)) return TargetState.staggered;
            if (Action != null)
            {
                switch (Action.Kind)
                {
                    case ActionKind.Skill: return Action.Skill != null && Action.Skill.IsAttack ? TargetState.attacking : TargetState.idle;
                    case ActionKind.Dodge: return TargetState.rolling;
                    case ActionKind.Block: return TargetState.guarding;
                }
            }
            return TargetState.idle;
        }

        public bool InIFrame
        {
            get
            {
                return Action != null && Action.Kind == ActionKind.Dodge
                    && Action.RealElapsedMs >= Action.IFrameStartMs - 1e-6 && Action.RealElapsedMs <= Action.IFrameEndMs + 1e-6;
            }
        }

        public bool IsGuarding { get { return Action != null && Action.Kind == ActionKind.Block; } }

        /// <summary>현재 스킬의 강인도 보너스(Add_Poise, Poise_Start~End 구간).</summary>
        public double ActivePoiseBonus
        {
            get
            {
                var a = Action;
                if (a == null || a.Kind != ActionKind.Skill || a.Skill == null || a.Skill.AddPoise <= 0) return 0;
                if (a.ElapsedMs >= a.Skill.PoiseStartMs - 1e-6 && a.ElapsedMs <= a.Skill.PoiseEndMs + 1e-6) return a.Skill.AddPoise;
                return 0;
            }
        }
    }
}
