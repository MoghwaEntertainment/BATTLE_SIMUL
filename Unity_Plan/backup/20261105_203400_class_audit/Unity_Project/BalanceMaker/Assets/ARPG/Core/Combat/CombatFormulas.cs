using System;

namespace ARPG.Core.Combat
{
    /// <summary>피해 계산 결과(상세 보기·CSV 용).</summary>
    public struct DamageBreakdown
    {
        public double Total;        // 최종 피해량(모든 피해 타입 합 × 페이즈 배율)
        public double RandomRatio;  // 이번 타격에 쓰인 랜덤 비율(0.9 ~ 상한)
        public double ChargeMult;   // 차지 배율(차지 공격이 아니면 1)
        public double RangedMult;   // 사격 거리 감쇄 배율(RANGED 타입이 아니면 1)
        public bool Groggy;         // 방어자가 그로기(기절) 상태였는지
        public string PrimaryType;  // 대표 피해 타입(로그·가드 계산 표시용)
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
        /// <summary>피해 타입 → 공격자의 [타입] 공격력 스탯. 목록에 없는 타입(PHYSICAL 등)은 물리 공격력.</summary>
        public static StatId AttackStat(DamageType t)
        {
            switch (t)
            {
                case DamageType.SLASH: return StatId.SLASH_ATTACK;          // 베기
                case DamageType.THRUST: return StatId.THRUST_ATTACK;        // 찌르기
                case DamageType.STRIKE: return StatId.STRIKE_ATTACK;        // 타격
                case DamageType.RANGED: return StatId.RANGED_ATTACK;        // 사격
                case DamageType.MAGIC: return StatId.MAGIC_ATTACK;          // 마법
                case DamageType.FIRE: return StatId.FIRE_ATTACK;            // 화염
                case DamageType.WATER: return StatId.WATER_ATTACK;          // 물(냉기)
                case DamageType.LIGHTNING: return StatId.LIGHTNING_ATTACK;  // 번개
                case DamageType.HOLY: return StatId.HOLY_ATTACK;            // 신성
                default: return StatId.PHYSICAL_ATTACK;                     // 그 외 = 물리
            }
        }

        /// <summary>피해 타입 → 공격자의 [타입] 공격력 증가% 스탯(10만분율).</summary>
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
                default: return StatId.PHYSICAL_ATTACK_PCT;                 // 그 외 = 물리 공격력 증가%
            }
        }

        /// <summary>
        /// 피해 타입 → 방어자의 [타입] 피해감소율 스탯(10만분율).
        /// RANGED 전용 감소율 열이 없으므로 물리 감소율을 사용한다.
        /// </summary>
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
                default: return StatId.PHYSICAL_NEGATION;                   // RANGED·PHYSICAL = 물리 감소율
            }
        }

        /// <summary>피해 타입 → 방어자의 가드 시 [타입] 감소율 스탯(10만분율). RANGED 는 물리 가드 감소율.</summary>
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
                default: return StatId.GUARD_PHYSICAL_NEGATION;             // RANGED·PHYSICAL = 물리 가드 감소율
            }
        }

        /// <summary>랜덤 피해 비율 상한 = (1.1 + 치명 공격력 증가) × (1 + 치명 공격력 증가%).</summary>
        public static double RandomUpper(double[] attacker, CombatRules r)
        {
            double crit = attacker[(int)StatId.CRITICAL_ATTACK] / r.statScale;         // 치명 공격력 증가(10만분율 → 배율)
            double critPct = attacker[(int)StatId.CRITICAL_ATTACK_PCT] / r.statScale;  // 치명 공격력 증가%(10만분율 → 배율)
            return (r.randomMaxBase + crit) * (1 + critPct);                           // 기본 상한 1.1(rules.randomMaxBase)에 치명 반영
        }

        /// <summary>
        /// 사격 거리 감쇄: ROUNDDOWN(1 - MIN(LOG10(MAX(거리 - 기본거리, 1)) / 분모 × (1 - 경감), 상한), 2).
        /// 현재 rules 값(기본거리 9m, 분모 1.4, 경감 0, 상한 0.95)이면 9m 이내 1.00, 19m 0.28, 최저 0.05.
        /// </summary>
        public static double RangedFalloff(double distanceM, CombatRules r)
        {
            double over = Math.Max(distanceM - r.rangedBaseDistance, 1.0);    // 기본 거리를 넘은 만큼(최소 1 → LOG10 = 0)
            double reduce = Math.Min(Math.Log10(over) / r.rangedFalloffDenominator * (1 - r.rangedFalloffMitigation), r.rangedFalloffCap); // 감쇄율, 상한으로 제한
            double v = 1 - reduce;                                             // 남는 배율
            return Math.Floor(v * 100 + 1e-9) / 100.0;                         // 소수 둘째 자리 내림(엑셀 ROUNDDOWN, 부동소수 오차 보정)
        }

        /// <summary>
        /// 차지 배율: 1 + (2/10000) × (1000/최대 차지 시간) × MAX(0, MIN(차지 시간, 최대)). 최대 0 이면 1.
        /// 최대까지 모으면 1.2배. AI 는 rules.aiChargeMs(현재 0)만큼 모으므로 지금은 항상 1.
        /// </summary>
        public static double ChargeMultiplier(double chargeMs, double maxChargeMs)
        {
            if (maxChargeMs <= 0) return 1.0;                                   // 최대 차지 시간이 없으면 배율 없음
            return 1 + (2.0 / 10000.0) * (1000.0 / maxChargeMs) * Math.Max(0, Math.Min(chargeMs, maxChargeMs)); // 0 ~ 최대 사이로 자른 차지 시간에 비례
        }

        /// <summary>
        /// 한 번의 타격(한 대상) 피해량 계산. 스킬의 피해 타입마다 따로 계산해 더한다.
        /// randomRatio 는 호출하는 쪽(BattleSimulation.ApplyHit)이 결정적 난수로 뽑아 넘긴다.
        /// phaseDamageMult 는 보스 페이즈 피해 배율(페이즈 없으면 1).
        /// </summary>
        public static DamageBreakdown Compute(
            double[] attacker, double[] defender, SkillDefinition skill, double motionValue,
            double randomRatio, double chargeMs, double distanceM, bool defenderGroggy,
            double phaseDamageMult, CombatRules r, double groggyMultOverride = -1)
        {
            var b = new DamageBreakdown { RandomRatio = randomRatio, ChargeMult = 1, RangedMult = 1, Groggy = defenderGroggy }; // 결과 기본값
            double total = 0;                                                   // 타입별 피해 합계
            for (int i = 0; i < skill.DamageTypes.Length; i++)                  // 스킬에 적힌 피해 타입마다
            {
                DamageType t = skill.DamageTypes[i];
                double atk = attacker[(int)AttackStat(t)];                      // (1) 그 타입 공격력
                if (atk <= 0) continue;                                         // 공격력 0 인 타입은 계산하지 않음
                if (b.PrimaryType == null) b.PrimaryType = t.ToString();        // 처음 계산된 타입 = 대표 타입
                double pct = attacker[(int)AttackPctStat(t)] / r.statScale;     // (2) 공격력 증가%(10만분율 → 배율)
                double d = motionValue * atk * (1 + pct);                       // (1)(2) 모션 밸류 × 공격력 × (1 + 증가%)
                if (t == DamageType.RANGED)
                {
                    b.RangedMult = RangedFalloff(distanceM, r);                 // (3) 사격 거리 감쇄
                    d *= b.RangedMult;
                }
                if (skill.IsChargeAttack)
                {
                    b.ChargeMult = ChargeMultiplier(chargeMs, r.maxChargeMs);   // (4) 차지 배율
                    d *= b.ChargeMult;
                }
                d *= randomRatio;                                               // (5)(6) 랜덤 비율(치명 포함 상한)
                if (defenderGroggy) d *= groggyMultOverride >= 0 ? groggyMultOverride : r.groggyDamageMult;                    // (7) 그로기 대상 추가 피해(rules 1.05)
                double neg = defender[(int)NegationStat(t)] / r.statScale;      // (8) 방어자 그 타입 피해감소율
                d *= (1 - neg);                                                 // (9) 감소 적용
                total += Math.Max(0, d);                                        // 음수 피해는 0 으로 막고 합산
            }
            if (b.PrimaryType == null && skill.DamageTypes.Length > 0) b.PrimaryType = skill.DamageTypes[0].ToString(); // 모든 타입 공격력이 0 이면 첫 타입을 대표로 표시
            b.Total = total * phaseDamageMult;                                  // 보스 페이즈 피해 배율
            return b;
        }

        /// <summary>가드 성공 시 HP 피해 비율(1 - 가드 감소율). 다중 타입이면 첫 타입 기준.</summary>
        public static double GuardHpRatio(double[] defender, SkillDefinition skill, CombatRules r)
        {
            DamageType t = skill.DamageTypes.Length > 0 ? skill.DamageTypes[0] : DamageType.PHYSICAL; // 첫 피해 타입(없으면 물리)
            double g = defender[(int)GuardNegationStat(t)] / r.statScale;       // 그 타입 가드 감소율(10만분율 → 배율)
            return Math.Max(0, 1 - g);                                          // 통과하는 비율(음수면 0)
        }

        /// <summary>[확정] 스태미나 피해량 = 피해량 × 10 / 가드 강도.</summary>
        public static double GuardStaminaDamage(double damage, double guardStrength, CombatRules r)
        {
            if (guardStrength <= 0) return double.PositiveInfinity;            // 가드 강도 0 이면 무한 → 스태미나가 바로 바닥나 가드 브레이크
            return damage * r.guardStaminaFactor / guardStrength;               // 계수 10(rules.guardStaminaFactor)
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
