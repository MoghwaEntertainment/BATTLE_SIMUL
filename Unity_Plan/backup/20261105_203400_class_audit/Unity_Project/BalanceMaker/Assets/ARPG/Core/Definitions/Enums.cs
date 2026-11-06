namespace ARPG.Core
{
    public enum DamageType
    {
        PHYSICAL = 0,
        SLASH = 1,
        THRUST = 2,
        STRIKE = 3,
        RANGED = 4,
        MAGIC = 5,
        FIRE = 6,
        WATER = 7,
        LIGHTNING = 8,
        HOLY = 9,
    }

    public enum SkillType
    {
        ATTACK = 0,
        BUFF = 1,
        DEBUFF = 2,
        DEFENCE = 3,
    }

    /// <summary>실행 중 행동의 종류. AI 행동(Action_Id)과 스킬을 실행 단위로 묶는다.</summary>
    public enum ActionKind
    {
        None = 0,
        Idle,
        Move,       // WALK/RUN/STRAFE/KEEP_DISTANCE
        Backstep,
        Dodge,
        Skill,      // ATTACK/BUFF/DEBUFF 스킬
        Block,      // DEFENCE 스킬
    }

    public enum MoveMode
    {
        None = 0,
        Approach,
        StrafeLeft,
        StrafeRight,
        Away,
    }

    public enum StatusKind
    {
        Stun = 0,   // 기절
        Wound = 1,  // 상처
        Poison = 2, // 중독
        Frost = 3,  // 빙결
    }

    /// <summary>SKILL Buff_Target. Default = 미설정(0) → Skill_Type 으로 결정(BUFF 는 자신, 그 외는 적).</summary>
    public enum BuffTarget
    {
        Default = 0,
        Self,
        Enemy,
        Ally,
    }

    /// <summary>SKILL Trigger_Condition 중 시뮬레이터가 처리하는 것. Default(NONE) = ATTACK 은 적중 시, 그 외는 시전 시.</summary>
    public enum BuffTrigger
    {
        Default = 0,
        OnCast,
        OnHit,
        OnEnd,
        Unsupported,   // ON_KILL / ON_GUARD / ON_DAMAGED 등(미구현, 걸리지 않음)
    }

    public enum BattleEndReason
    {
        None = 0,
        TeamVictory,      // 한 팀 전멸
        MutualWipe,       // 동시 전멸
        Timeout,          // 제한 시간
        UserAborted,      // 사용자 중단
        Error,            // 실행 오류
    }

    public enum TargetState
    {
        idle,
        attacking,
        healing,
        rolling,
        guarding,
        staggered,
        guard_broken,
    }

    public static class ActionIds
    {
        public const string IDLE = "IDLE";
        public const string WALK_APPROACH = "WALK_APPROACH";
        public const string RUN_APPROACH = "RUN_APPROACH";
        public const string STRAFE_L = "STRAFE_L";
        public const string STRAFE_R = "STRAFE_R";
        public const string BACKSTEP = "BACKSTEP";
        public const string KEEP_DISTANCE = "KEEP_DISTANCE";
        public const string DODGE = "DODGE";
        public const string USE_POTION = "USE_POTION";
        public const string NONE = "NONE";
    }

    public static class EnumText
    {
        public static bool TryParseDamageType(string s, out DamageType t)
        {
            switch ((s ?? string.Empty).Trim())
            {
                case "PHYSICAL": t = DamageType.PHYSICAL; return true;
                case "SLASH": t = DamageType.SLASH; return true;
                case "THRUST": t = DamageType.THRUST; return true;
                case "STRIKE": t = DamageType.STRIKE; return true;
                case "RANGED": t = DamageType.RANGED; return true;
                case "MAGIC": t = DamageType.MAGIC; return true;
                case "FIRE": t = DamageType.FIRE; return true;
                case "WATER": t = DamageType.WATER; return true;
                case "LIGHTNING": t = DamageType.LIGHTNING; return true;
                case "HOLY": t = DamageType.HOLY; return true;
                default: t = DamageType.PHYSICAL; return false;
            }
        }

        public static bool TryParseSkillType(string s, out SkillType t)
        {
            switch ((s ?? string.Empty).Trim())
            {
                case "ATTACK": t = SkillType.ATTACK; return true;
                case "BUFF": t = SkillType.BUFF; return true;
                case "DEBUFF": t = SkillType.DEBUFF; return true;
                case "DEFENCE": t = SkillType.DEFENCE; return true;
                default: t = SkillType.ATTACK; return false;
            }
        }

        public static BuffTarget ParseBuffTarget(string s)
        {
            switch ((s ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "SELF": return BuffTarget.Self;
                case "ENEMY": return BuffTarget.Enemy;
                case "ALLY": return BuffTarget.Ally;
                default: return BuffTarget.Default; // NONE, 0, 빈칸
            }
        }

        public static BuffTrigger ParseBuffTrigger(string s)
        {
            switch ((s ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "": case "0": case "NONE": return BuffTrigger.Default;
                case "ON_CAST": return BuffTrigger.OnCast;
                case "ON_HIT": return BuffTrigger.OnHit;
                case "ON_END": return BuffTrigger.OnEnd;
                default: return BuffTrigger.Unsupported;
            }
        }

        public static string StatusName(StatusKind k)
        {
            switch (k)
            {
                case StatusKind.Stun: return "STUN";
                case StatusKind.Wound: return "WOUND";
                case StatusKind.Poison: return "POISON";
                case StatusKind.Frost: return "FROST";
                default: return k.ToString();
            }
        }

        public static string EndReasonKo(BattleEndReason r)
        {
            switch (r)
            {
                case BattleEndReason.TeamVictory: return "전멸 승리";
                case BattleEndReason.MutualWipe: return "동시 전멸";
                case BattleEndReason.Timeout: return "시간 초과";
                case BattleEndReason.UserAborted: return "사용자 중단";
                case BattleEndReason.Error: return "오류";
                default: return "진행 중";
            }
        }
    }
}
