using System;
using System.Text;

namespace ARPG.Core
{
    /// <summary>
    /// 전투 규칙 상수. StreamingAssets/ARPG/rules.json 이 있으면 덮어쓴다.
    /// 출처 표기: [확정]=사용자 확정, [기획]=기획 엑셀, [로그]=기존 결과 로그, [임시]=근거 없음(검토 필요).
    /// 각 값의 근거 설명은 Assets/ARPG/RULES.md 에 정리되어 있다.
    /// </summary>
    [Serializable]
    public sealed class CombatRules
    {
        public string rulesVersion = "2026.11.01-r1";

        // ---- 시간 ----
        public int tickRate = 30;                 // [확정] 30Hz
        public int aiTickInterval = 3;            // [기획] AI 10Hz = 3 tick
        public double maxBattleSec = 300;         // [로그] 기존 300초 무승부

        // ---- 전장/공간 ----
        public double arenaHalfSize = 30;         // [임시] 평평한 정사각 전장 반폭(m)
        public double bodyRadius = 0.4;           // [임시] 모든 개체 반경(m)
        public double spawnDistance = 10;         // [로그] 기존 로그 초기 거리 약 10m
        public double spawnSpacing = 1.5;         // [임시]

        // ---- AI 전역(config.py 대체) ----
        public double perceptionRange = 18;       // [기획] AI README
        public double aggroReleaseRange = 30;     // [기획]
        public double targetSwitchRatio = 0.8;    // [기획] 20% 히스테리시스: 새 거리 < 기존 × 0.8
        public double distMeleeMax = 2.5;         // [기획]
        public double distMidMax = 6.0;           // [기획]
        public int recentActionMemory = 8;
        public double incomingAttackWindowMs = 500;   // [임시] 상대 타격까지 남은 시간이 이 이하면 incoming_attack
        public double poiseBrokenWindowSec = 0.5;     // [임시] 경직 종료 후 poise_broken=true 유지 시간
        public bool commonRowsFirst = true;           // [해석] Phase_No=0 행을 먼저(표의 위→아래 순서)

        // ---- 이동(BUILTIN) ----
        public double walkSpeed = 2.0;            // [기획] ACTION_DEF 설명 2m/s
        public double runSpeed = 4.0;             // [기획] ACTION_DEF 설명 4m/s
        public double strafeSpeed = 2.0;          // [임시]
        public double keepDistanceSpeed = 2.0;    // [임시]
        public double softActionSec = 0.5;        // [로그] 기존 로그의 판단 간격 15f
        public double approachStopSurfaceDist = 0.8;  // [임시] 접근 정지 거리(몸 표면 간)
        public double backstepDistance = 2.0;     // [임시]
        public double backstepSec = 0.5;          // [임시]
        public double backstepStaminaCost = 0;    // [임시]
        public double dodgeDistance = 3.0;        // [임시]
        public double dodgeSec = 0.7;             // [임시]
        public double dodgeIFrameStartSec = 0.1;  // [임시]
        public double dodgeIFrameEndSec = 0.45;   // [임시]
        public double dodgeStaminaCost = 50;      // [임시] 플레이어 스킬 비용 50과 동일하게 가정

        // ---- 자원 ----
        public double minStaminaToStart = 1.0;    // [확정] 스태미나 소모 행동은 현재값 1 이상
        public double minHpRemainAfterCost = 1.0; // [확정] HP - 비용 >= 1
        public double minFpRemainAfterCost = 0.0; // [확정] FP - 비용 >= 0
        public bool staminaRegenWhileActing = false;   // [임시] 공격/회피/가드 중 스태미나 회복 정지
        public bool monsterIgnoreSkillCosts = true;    // [확정 방향] 몬스터 FP/스태미나 관리 안 함
        public double monsterStaminaPctForAi = 100;    // [임시] 몬스터 stamina_pct 조건값(미관리=가득 참)
        public bool monsterGuardBreakByStamina = false;// [미확정] 몬스터 가드 스태미나 브레이크

        // ---- 피해 ----
        public double randomMin = 0.9;            // [기획] RANDBETWEEN(0.9, (1.1+치명)*(1+치명%))
        public double randomMaxBase = 1.1;        // [기획]
        public double groggyDamageMult = 1.05;    // [기획] 그로기/가드 브레이크 대상 추가 피해
        public double statScale = 100000;         // [기획] 10만분율 스탯
        public double rangedBaseDistance = 9;     // [기획] 전투프로세스_사격비거리!A3
        public double rangedFalloffDenominator = 1.4; // [기획] D5
        public double rangedFalloffMitigation = 0.0;  // [기획:예시 0.15, 캐릭터 스탯 없음] 비거리 감쇄 경감
        public double rangedFalloffCap = 0.95;    // [기획]
        public double maxChargeMs = 3000;         // [기획] 전투프로세스_차지시간!A3
        public double aiChargeMs = 0;             // [확정] 실제 충전시간 0 = 기본 배율
        public double instantRangedThreshold = 6; // [임시] Effective_Range 초과 스킬은 즉발 원거리(투사체 마스터 없음)

        // ---- 가드/패리/강인도 ----
        public double guardStaminaFactor = 10;    // [확정] 스태미나 피해 = 피해량 × 10 / 가드 강도
        public double guardFrontAngleDeg = 180;   // [임시] 전방 180도만 가드
        public double blockHoldSecWhenAniTimeZero = 1.0; // [임시] BLOCK Ani_Time=0 일 때 가드 유지
        public int staggerTicks = 30;             // [로그] 기존 로그 '강인도 붕괴 30f'
        public double poiseRecoveryDelaySec = 5;  // [기획] 피격 후 5초 뒤 회복
        public double poiseRecoveryPerSec = -1;   // [임시] -1 = 회복 시작 시 즉시 최대치
        public bool statusBuildupUsesMotionValue = true; // [해석] 유발 = 무기 유발 × 모션 밸류
        public bool poiseDamageUsesMotionValue = true;   // [해석] 강인도 공격력 = 무기 강인도 × 모션 밸류

        // ---- 상태이상 ----
        public int stunTicks = 90;                // [기획] 기절 90f
        public double statusDecayPerSec = 5;      // [확정]
        public double woundMaxHpRatio = 0.15;     // [기획]
        public double poisonMaxHpRatioPerSec = 0.05; // [기획]
        public double poisonDurationSec = 10;     // [기획]
        public double poisonFirstDelaySec = 1;    // [확정]
        public double frostDurationSec = 5;       // [기획]
        public double frostSpeedMult = 0.5;       // [확정] 이동/공격/구르기만

        // ---- 콤보/캔슬 ----
        public double comboContinueWindowSec = 0.3;   // [임시] 공격 종료 후 다음 입력까지 콤보 유지
        public bool idleDoesNotCancel = true;         // [해석] 캔슬 구간에서 IDLE 선택은 현재 동작 유지

        public double TickSec { get { return 1.0 / tickRate; } }
        public double TickMs { get { return 1000.0 / tickRate; } }
        public long MaxTicks { get { return (long)Math.Round(maxBattleSec * tickRate); } }
        public int SecToTicks(double sec) { return (int)Math.Ceiling(sec * tickRate - 1e-9); }

        /// <summary>ms → 최초 도달 tick (ceil). 예: 300ms→9, 800ms→24.</summary>
        public static int MsToTicks(double ms, int tickRate)
        {
            return (int)Math.Ceiling(ms * tickRate / 1000.0 - 1e-9);
        }

        public CombatRules Clone()
        {
            return (CombatRules)MemberwiseClone();
        }

        /// <summary>재현성 기록용 문자열(필드명=값). 해시 계산에 사용.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            foreach (var f in typeof(CombatRules).GetFields())
            {
                object v = f.GetValue(this);
                string s = v is double d ? d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                                         : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                sb.Append(f.Name).Append('=').Append(s).Append(';');
            }
            return sb.ToString();
        }
    }
}
