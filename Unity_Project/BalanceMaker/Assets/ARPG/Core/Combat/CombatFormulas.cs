using System;

namespace ARPG.Core.Combat
{
    /// <summary>피해 계산 결과(상세 보기·CSV 용).</summary>
    public struct DamageBreakdown
    {
        public double Total;
        public double RandomRatio;
        public double ChargeMult;
        public double RangedMult;
        public bool Groggy;
        public string PrimaryType;
    }

    /// <summary>
    /// 1.ARPG_전투공식.xlsx '전투 프로세스' 5-1 HP 피해량 계산 및 4.전투시뮬레이터 대미지메이커(H~V열)의 단계를 따른다.
    ///  (1) 기본 = 모션 밸류 × [타입] 공격력
    ///  (2) × (1 + [타입] 공격력 증가%)
    ///  (3) 사격 배율(RANGED) (4) 차지 배율(CHARGE_ATK)
    ///  (5)(6) × 랜덤 비율 [0.9, (1.1 + 치명)×(1 + 치명%)]
    ///  (7) 그로기/가드 브레이크 대상 × 1.05
    ///  (8)(9) × (1 - 방어자 [타입] 피해감소율)
    /// 스킬 Damage_Type 에 입력된 타입만 계산한다(Template_SKILL Damage_Type 설명).
    /// 스탯의 %/감소율은 10만분율 값이다.
    /// </summary>
    public static class DamageFormula
    {
        public static StatId AttackStat(DamageType t)
        {
            switch (t)
            {
                case DamageType.SLASH: return StatId.SLASH_ATTACK;
                case DamageType.THRUST: return StatId.THRUST_ATTACK;
                case DamageType.STRIKE: return StatId.STRIKE_ATTACK;
                case DamageType.RANGED: return StatId.RANGED_ATTACK;
                case DamageType.MAGIC: return StatId.MAGIC_ATTACK;
                case DamageType.FIRE: return StatId.FIRE_ATTACK;
                case DamageType.WATER: return StatId.WATER_ATTACK;
                case DamageType.LIGHTNING: return StatId.LIGHTNING_ATTACK;
                case DamageType.HOLY: return StatId.HOLY_ATTACK;
                default: return StatId.PHYSICAL_ATTACK;
            }
        }

        public static StatId AttackPctStat(DamageType t)
        {
            switch (t)
            {
                case DamageType.SLASH: return StatId.SLASH_ATTACK_PCT;
                case DamageType.THRUST: return StatId.THRUST_ATTACK_PCT;
                case DamageType.STRIKE: return StatId.STRIKE_ATTACK_PCT;
                case DamageType.RANGED: return StatId.RANGED_ATTACK_PCT;
                case DamageType.MAGIC: return StatId.MAGIC_ATTACK_PCT;
                case DamageType.FIRE: return StatId.FIRE_ATTACK_PCT;
                case DamageType.WATER: return StatId.WATER_ATTACK_PCT;
                case DamageType.LIGHTNING: return StatId.LIGHTNING_ATTACK_PCT;
                case DamageType.HOLY: return StatId.HOLY_ATTACK_PCT;
                default: return StatId.PHYSICAL_ATTACK_PCT;
            }
        }

        /// <summary>RANGED 전용 감소율 열이 없으므로 물리 감소율을 사용한다.</summary>
        public static StatId NegationStat(DamageType t)
        {
            switch (t)
            {
                case DamageType.SLASH: return StatId.SLASH_NEGATION;
                case DamageType.THRUST: return StatId.THRUST_NEGATION;
                case DamageType.STRIKE: return StatId.STRIKE_NEGATION;
                case DamageType.MAGIC: return StatId.MAGIC_NEGATION;
                case DamageType.FIRE: return StatId.FIRE_NEGATION;
                case DamageType.WATER: return StatId.WATER_NEGATION;
                case DamageType.LIGHTNING: return StatId.LIGHTNING_NEGATION;
                case DamageType.HOLY: return StatId.HOLY_NEGATION;
                default: return StatId.PHYSICAL_NEGATION;
            }
        }

        public static StatId GuardNegationStat(DamageType t)
        {
            switch (t)
            {
                case DamageType.SLASH: return StatId.GUARD_SLASH_NEGATION;
                case DamageType.THRUST: return StatId.GUARD_THRUST_NEGATION;
                case DamageType.STRIKE: return StatId.GUARD_STRIKE_NEGATION;
                case DamageType.MAGIC: return StatId.GUARD_MAGIC_NEGATION;
                case DamageType.FIRE: return StatId.GUARD_FIRE_NEGATION;
                case DamageType.WATER: return StatId.GUARD_WATER_NEGATION;
                case DamageType.LIGHTNING: return StatId.GUARD_LIGHTNING_NEGATION;
                case DamageType.HOLY: return StatId.GUARD_HOLY_NEGATION;
                default: return StatId.GUARD_PHYSICAL_NEGATION;
            }
        }

        /// <summary>랜덤 피해 비율 상한 = (1.1 + 치명 공격력 증가) × (1 + 치명 공격력 증가%).</summary>
        public static double RandomUpper(double[] attacker, CombatRules r)
        {
            double crit = attacker[(int)StatId.CRITICAL_ATTACK] / r.statScale;
            double critPct = attacker[(int)StatId.CRITICAL_ATTACK_PCT] / r.statScale;
            return (r.randomMaxBase + crit) * (1 + critPct);
        }

        /// <summary>사격 거리 감쇄: ROUNDDOWN(1 - MIN(LOG10(MAX(거리 - 기본거리, 1)) / 분모 × (1 - 경감), 상한), 2).</summary>
        public static double RangedFalloff(double distanceM, CombatRules r)
        {
            double over = Math.Max(distanceM - r.rangedBaseDistance, 1.0);
            double reduce = Math.Min(Math.Log10(over) / r.rangedFalloffDenominator * (1 - r.rangedFalloffMitigation), r.rangedFalloffCap);
            double v = 1 - reduce;
            return Math.Floor(v * 100 + 1e-9) / 100.0;
        }

        /// <summary>차지 배율: 1 + (2/10000) × (1000/최대 차지 시간) × MAX(0, MIN(차지 시간, 최대)). 최대 0 이면 1.</summary>
        public static double ChargeMultiplier(double chargeMs, double maxChargeMs)
        {
            if (maxChargeMs <= 0) return 1.0;
            return 1 + (2.0 / 10000.0) * (1000.0 / maxChargeMs) * Math.Max(0, Math.Min(chargeMs, maxChargeMs));
        }

        public static DamageBreakdown Compute(
            double[] attacker, double[] defender, SkillDefinition skill, double motionValue,
            double randomRatio, double chargeMs, double distanceM, bool defenderGroggy,
            double phaseDamageMult, CombatRules r)
        {
            var b = new DamageBreakdown { RandomRatio = randomRatio, ChargeMult = 1, RangedMult = 1, Groggy = defenderGroggy };
            double total = 0;
            for (int i = 0; i < skill.DamageTypes.Length; i++)
            {
                DamageType t = skill.DamageTypes[i];
                double atk = attacker[(int)AttackStat(t)];
                if (atk <= 0) continue;
                if (b.PrimaryType == null) b.PrimaryType = t.ToString();
                double pct = attacker[(int)AttackPctStat(t)] / r.statScale;
                double d = motionValue * atk * (1 + pct);
                if (t == DamageType.RANGED)
                {
                    b.RangedMult = RangedFalloff(distanceM, r);
                    d *= b.RangedMult;
                }
                if (skill.IsChargeAttack)
                {
                    b.ChargeMult = ChargeMultiplier(chargeMs, r.maxChargeMs);
                    d *= b.ChargeMult;
                }
                d *= randomRatio;
                if (defenderGroggy) d *= r.groggyDamageMult;
                double neg = defender[(int)NegationStat(t)] / r.statScale;
                d *= (1 - neg);
                total += Math.Max(0, d);
            }
            if (b.PrimaryType == null && skill.DamageTypes.Length > 0) b.PrimaryType = skill.DamageTypes[0].ToString();
            b.Total = total * phaseDamageMult;
            return b;
        }

        /// <summary>가드 성공 시 HP 피해 비율(1 - 가드 감소율). 다중 타입이면 첫 타입 기준.</summary>
        public static double GuardHpRatio(double[] defender, SkillDefinition skill, CombatRules r)
        {
            DamageType t = skill.DamageTypes.Length > 0 ? skill.DamageTypes[0] : DamageType.PHYSICAL;
            double g = defender[(int)GuardNegationStat(t)] / r.statScale;
            return Math.Max(0, 1 - g);
        }

        /// <summary>[확정] 스태미나 피해량 = 피해량 × 10 / 가드 강도.</summary>
        public static double GuardStaminaDamage(double damage, double guardStrength, CombatRules r)
        {
            if (guardStrength <= 0) return double.PositiveInfinity;
            return damage * r.guardStaminaFactor / guardStrength;
        }
    }

    public enum ResourceCheck
    {
        Ok = 0,
        HpInsufficient,
        FpInsufficient,
        StaminaInsufficient,
    }

    /// <summary>
    /// [확정] 자원 선검사(구현계획 11.3).
    /// HP: 현재 - 비용 >= 1, FP: 현재 - 비용 >= 0, 스태미나: 비용 > 0 이면 현재 >= 1 (음수 허용, 전체 비용 차감).
    /// 하나라도 실패하면 모든 비용을 차감하지 않는다.
    /// </summary>
    public static class ResourceRules
    {
        public static ResourceCheck Check(double hp, double fp, double stamina, double costHp, double costFp, double costStamina, CombatRules r)
        {
            if (costHp > 0 && hp - costHp < r.minHpRemainAfterCost - 1e-9) return ResourceCheck.HpInsufficient;
            if (costFp > 0 && fp - costFp < r.minFpRemainAfterCost - 1e-9) return ResourceCheck.FpInsufficient;
            if (costStamina > 0 && stamina < r.minStaminaToStart - 1e-9) return ResourceCheck.StaminaInsufficient;
            return ResourceCheck.Ok;
        }

        public static string Describe(ResourceCheck c)
        {
            switch (c)
            {
                case ResourceCheck.HpInsufficient: return "HP 부족(차감 후 1 미만)";
                case ResourceCheck.FpInsufficient: return "FP 부족(차감 후 0 미만)";
                case ResourceCheck.StaminaInsufficient: return "스태미나 1 미만";
                default: return "OK";
            }
        }
    }
}
