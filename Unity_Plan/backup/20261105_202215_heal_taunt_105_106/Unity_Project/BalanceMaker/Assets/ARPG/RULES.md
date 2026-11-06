# 전투 규칙 구현 매핑 (rules.json)

기본 규칙의 수치는 `Assets/StreamingAssets/ARPG/rules.json` 에서 바꿀 수 있다(코드 재컴파일 불필요, 플레이 중 `데이터 다시 읽기`).
출처 표기: **확정**=사용자 확정(Unity_Plan 11장) / **기획**=기획 엑셀 / **로그**=기존 4.전투시뮬레이터 결과 / **해석**=문서 문구를 구현 규칙으로 옮긴 것 / **임시**=근거 없음, 검토 필요.

## GameConfig 우선 적용 (2026-11-04, r7)


GameConfig.xlsm의 GameConfig 시트(1행 헤더, is_use=All)를 기존 변환기 1.3.0이 gameconfig.json으로 변환한다.
RATE는 100000으로 나눠 JSON value에 저장하고 rawValue도 보존한다. FLAT는 그대로 저장하며 *_TIME은 초다.
우선순위: C# 기본값 → rules.json → gameconfig.json의 활성 항목. 항목이 없거나 비활성이면 해당 기존 규칙을 유지한다.
추가 피해 RATE 0은 1배, 5000은 1.05배다. FROST_SLOWDOWN_PCT는 감속 비율이므로 속도 배율은 1-RATE다.
기절과 가드 브레이크는 시간·추가 피해를 독립 관리하며, GameConfig가 없을 때만 가드 설정 -1이 기존 stun/groggy 설정으로 폴백한다.
중독은 1초 뒤부터 매초 피해. 소수 초도 지원(2.5초이면 +1/+2초 2회 피해, +2.5초 종료). 기간 끝의 남은 소수 구간에 비례 피해를 추가하지 않는다.
시간은 ceil(초×tickRate)로 tick 변환한다. 잘못된 단위·중복·음수·미계산 수식·비유한 수는 변환 차단, 이전 JSON 유지. 알 수 없는 키는 변환·경고하고 현재 전투 연결은 하지 않는다.
원본 Excel은 읽기만 하며 저장하지 않는다. 변환 후 Unity의 데이터 다시 읽기 또는 전투 재시작으로 반영한다.

| Index | Value_Type | 입력값 | 효과 | 연결 필드 |
|---|---|---:|---|---|
| STUN_TIME | FLAT | 3 | 기절 3초 | stunTicks=90 |
| STUN_ADD_DAMAGE_PCT | RATE | 0 | 기절 추가 피해 0% | groggyDamageMult=1 |
| WOUND_DAMAGE_PCT | RATE | 10000 | 최대 HP 10% 즉시 피해 | woundMaxHpRatio=0.1 |
| POISON_TIME | FLAT | 10 | 중독 10초 | poisonDurationSec=10 |
| POISON_DAMAGE_PCT | RATE | 2000 | 초당 최대 HP 2% | poisonMaxHpRatioPerSec=0.02 |
| FROST_TIME | FLAT | 5 | 빙결 5초 | frostDurationSec=5 |
| FROST_SLOWDOWN_PCT | RATE | 50000 | 동작 속도 50% 감소 | frostSpeedMult=0.5 |
| STAGGER_TIME | FLAT | 0.5 | 경직 0.5초 | staggerTicks=15 |
| GUARD_BREAK_TIME | FLAT | 3 | 가드 브레이크 3초 | guardBreakTicks=90 |
| GUARD_BREAK_ADD_DAMAGE_PCT | RATE | 0 | 가드 브레이크 추가 피해 0% | guardBreakDamageMult=1 |

아래 기존 수치·예시는 기본값/역사적 근거이며 위 표의 활성 GameConfig가 우선한다.

## 1. 시간·실행 순서

| 항목 | 구현 | 출처 |
| --- | --- | --- |
| 논리 시간 | 30Hz tick, AI 3 tick(10Hz)마다 판단 | 확정/기획 |
| 타격 시점 | `이전 진행 < Hit_Time <= 현재 진행`(오차 1e-6ms), 한 tick 에 여러 시점 통과 시 모두 처리. 300ms→행동 9번째 tick | 확정 |
| 행동 속도 | Ani_Speed × 페이즈 Speed_Mult × (빙결 시 0.5) 를 행동 진행 시간에 반영 | 기획/확정 |
| tick 순서 | 상태·회복 → AI(타겟/의도 수집) → 승인·차감·시작 → 진행·이동 → 겹침 해소 → 타격 동시 해결 → 상태 전환·적중 시 버프 일괄 적용 → 강제 중단 → 행동 종료 → 사망 → 승패 | 모듈설계 5장 |
| 동시 교환 | 같은 tick 에 성립한 타격은 모두 해결, 사망은 해결 후 확정(동시 전멸 가능) | 확정 |
| 상태 전환 시점 | 경직·가드 브레이크·기절·중독·빙결의 발동은 그 tick 의 타격을 모두 해결한 뒤 일괄 적용(`FlushPendingStates`). 같은 tick 의 다른 타격은 tick 시작 상태로 계산: 가드 유지, 그로기 추가 피해 없음, 경직 전 강인도 처리. 같은 대상의 같은 전환은 1회만, 가드 브레이크와 기절 누적은 같은 기절로 보아 먼저 예약된 쪽만 적용. 발동이 예약된 상태이상은 그 tick 의 추가 누적을 받지 않음. 그 tick 에 HP 0 이 된 대상에는 걸지 않음. 상처는 즉시 피해라 바로 적용. 강제 중단 사유는 기절 > 경직 순으로 남김. `deferStateChangesToTickEnd=true`(false = 이전처럼 즉시 적용) | 확정(2026-11-03) |
| 종료 | 한 팀 생존=승리, 전원 사망=동시 전멸, 제한 시간(기본 300초)=시간 초과 | 로그 |

## 2. 자원

| 항목 | 구현 | 출처 |
| --- | --- | --- |
| HP/FP 선검사 | `HP-비용 >= 1`, `FP-비용 >= 0`, 실패 시 어떤 자원도 차감하지 않음 | 확정 |
| 스태미나 | 비용 > 0 이면 현재값 >= 1 일 때 시작, 전체 비용 차감 후 음수 보존. 1 미만이면 소모 행동 후보에서 제외 | 확정 |
| AI 자원 처리 | 자원 부족 행은 후보에서 제외하고 다음 행 평가(실패 모션 없음). 건너뛴 횟수는 개체 통계 `자원부족 건너뜀` | 확정 |
| 스태미나 회복 | STAMINA_RECOVERY/초. 공격·회피·가드·백스텝·경직 중 정지(`staminaRegenWhileActing=false`) | 임시 |
| 몬스터 FP/스태미나 | 관리하지 않음(`monsterIgnoreSkillCosts=true`): 비용 무시, AI stamina_pct=100(`monsterStaminaPctForAi`), 가드 스태미나 브레이크 없음(`monsterGuardBreakByStamina=false`) | 확정 방향 + 임시 |

## 3. 피해 (1.ARPG_전투공식 5-1, 대미지메이커 H~V열)

```
타입별 피해 = 모션밸류[타격] × [타입]_ATTACK × (1 + [타입]_ATTACK_PCT/100000)
           × 사격 감쇄(RANGED) × 차지 배율(CHARGE_ATK)
           × 랜덤[0.9, (1.1 + CRITICAL_ATTACK/100000) × (1 + CRITICAL_ATTACK_PCT/100000)]
           × 1.05(대상 그로기/가드 브레이크)
           × (1 - [타입]_NEGATION/100000)
최종 = Σ(스킬 Damage_Type 에 적힌 타입) × 페이즈 Dmg_Mult
```

- 대미지메이커 예시(검사37→기사단장 61.002041, 검사44 81.018593)를 Self Test 로 재현한다.
- 스킬 Damage_Type 에 적힌 타입만 계산(몬스터의 속성 공격력 스탯은 해당 타입 스킬일 때만 사용). **해석**
- RANGED 는 RANGED_ATTACK/RANGED_ATTACK_PCT 를 쓰고 감소율은 PHYSICAL_NEGATION(전용 열 없음). **해석**
- 사격 감쇄: 기본 거리 9m, 분모 1.4, 상한 0.95, 소수 둘째 자리 버림. 비거리 감쇄 경감은 캐릭터 스탯이 없어 0. **기획/임시**
- 차지: 최대 3000ms. AI 의 실제 충전시간 `aiChargeMs=0` → 기본 배율. **확정**
- HP_PCT/FP_PCT/STAMINA_PCT 등은 EX_PC/Monster 의 최종값에 이미 반영된 것으로 보고 다시 곱하지 않는다. FINAL_DAMAGE_PCT/FINAL_NEGATION_PCT 는 현재 전부 0이라 미적용. **해석**
- Accuracy 는 사용하지 않는다(AI README 확정).

## 4. 타격 판정(평면 XZ)

- 모든 개체 반경 `bodyRadius=0.4m`. 거리 조건(dist, dist_m)은 **중심 간 거리**.
- 근접: 판정 원 중심 = 공격자 위치 + 정면 × (반경 + max(Effective_Range − Hit_Range, 0)), 반지름 Hit_Range. 대상 몸 원과 겹치면 적중. → 정면 도달 거리는 몸 표면 기준 Effective_Range. **해석**
- AI 사거리 필터: 중심 거리 ≤ 공격자 반경 + max(Effective_Range, Hit_Range) + Move_Distance + 대상 반경. **해석**
- Effective_Range > 6m(`instantRangedThreshold`) 는 투사체 마스터가 없어 **즉발 원거리**: 대상이 사거리·전방 반원 안이면 적중, Hit_Range 안의 다른 적도 적중. **임시**
- Move_Distance: 시작~첫 타격 시점 동안 정면 이동(대상과 겹치지 않게 제한). Tracking: Tracking_Start~End 구간에 시작 방향 기준 ±Tracking_Angle/2 안에서 대상 방향으로 회전. **해석**

## 5. 다단히트

- Hit_Time / Motion_Value 는 쉼표 목록, 같은 순서끼리 한 타격. 각 타격 배율 = Motion_Value[i]/100000, **Hit_Count 로 나누지 않음**. **확정**
- 길이 불일치(Hit_Count ≠ 목록 길이) 스킬은 변환기에서 `valid=false` 로 내보내고 실행에서 제외(검증 보고서 '레코드 비활성'). **확정 규칙의 적용**
- 현재 SKILL 에 Hit_Count 2~5 인데 목록이 1개인 플레이어 무기 기술 11개가 있다 → 데이터 보정 필요.

## 6. 방어·강인도·상태이상

| 항목 | 구현 | 출처 |
| --- | --- | --- |
| 무적 | DODGE 시작 후 논리 경과 시간 0.10~0.45초 무적. RealElapsedMs로 판정하므로 빙결 중에도 같은 구간(명목상 0.35초). 동작 ElapsedMs만 감속(회피 기본 0.7초→전 구간 빙결 1.4초). 3m 후방, 스태미나 50 | 임시 |
| 가드 | BLOCK(DEFENCE 스킬) 동안 전방 180도 공격을 가드. HP 피해 = 피해 × (1 − GUARD_[타입]_NEGATION/100000). 스태미나 피해 = 피해 × **10** / GUARD_STRENGTH. 스태미나 < 0 → 0 으로 만들고 가드 브레이크(기절 90f, 그로기) | 확정/기획 |
| BLOCK 유지 | Ani_Time=0 인 방어 스킬은 `blockHoldSecWhenAniTimeZero=1.0초` 유지 | 임시 |
| 가드 시 강인도/상태이상 | 적용하지 않음(순서도 가드 분기에 없음) | 해석 |
| 패리 | 패리 프레임을 가진 스킬 데이터가 없어 미구현 | — |
| 강인도 | 공격 강인도 = STAGGER_BUILDUP × 모션밸류(강인도 피해 계산 순서도. 순서도의 '+ 스킬 강인도 공격' 항은 SKILL 표에 열이 없어 0). 방어자 스킬의 Add_Poise(Poise_Start~End)가 타격마다 그만큼 흡수. 0 이하 → 경직 30f, 강인도 최대치로 초기화, 행동 강제 중단 | 기획/로그/해석 |
| 경직 중 강인도 무적 | 경직(30f) 동안 맞으면 HP 피해만 받고 강인도는 깎이지 않음 → 경직 중 재경직 없음. 경직 시작 때 채운 최대치를 그대로 유지. `poiseImmuneWhileStaggered=true`(false 면 이전 동작: 경직 중에도 강인도 감소·재경직, 경직 시간은 그 타격부터 다시 30f) | 확정(2026-11-01) |
| 강인도 회복 | 마지막 피격 5초 후 회복 시작. 회복 속도 정보가 없어 즉시 최대치(`poiseRecoveryPerSec=-1`) | 기획/임시 |
| 상태이상 누적 | 유발 = [종류]_BUILDUP × 모션밸류 × (1 + [종류]_BUILDUP_PCT). 게이지 ≥ 저항 → 발동(기절·중독·빙결은 tick 끝에 적용), 게이지 0. 순서도의 '+ 스킬 유발' 항은 SKILL 표에 열이 없어 0 | 기획(전투공식 전투 프로세스 5-3 순서도)/해석(발동 시 게이지 0) |
| 게이지 감소 | 쌓인 직후부터 초당 5(매 tick 5/30), 0 미만 없음, 같은 tick 신규분은 다음 tick 부터 | 확정 |
| 상처 | 최대 HP 15% 즉시 피해, 즉시 재누적 가능(면역 없음) | 기획/확정 |
| 중독 | 10초, 발동 +1초부터 1초 간격 최대 HP 5% × 10회, 마지막 피해 후 만료, 효과 중 면역 | 기획/확정 |
| 빙결 | 5초, 이동·공격·구르기만 50% 감속(회복·쿨다운·중독 주기 유지), 효과 중 면역 | 기획/확정 |
| 기절 | 균형 공격력(STAGGER_BUILDUP) × 모션밸류 × (1 + STUN_BUILDUP_PCT/100000)를 누적, STUN_RESIST 도달 시 게이지 0·행동 중단·90tick(3초) 기절. 초당 5 감소·기절 중 재누적 면역. 가드 브레이크도 기존대로 기절 부여. AI 판정 상태는 누적 기절 = staggered, 가드 브레이크 기절 = guard_broken(AI 조건 값에 기절이 없음). 화면 정보창·머리 위 글자는 '기절'로 우선 표시 | 기획/해석(AI 상태값) |

## 6-1. 버프·디버프 (2026-11-02, BUFF.xlsx)

| 항목 | 구현 | 출처 |
| --- | --- | --- |
| 표 형식 | `BUFF.xlsx` BUFF 시트. 한 줄 = Buff_ID 하나, `Buff_AbilityN`(ABILITY Index) + `Buff_AbilityN_Stat`(수치) 쌍 N개 | 확정(사용자 지정 형식) |
| 능력치 연결 | ABILITY Index → ENUM_Name → 캐릭터 스탯(EX_PC `Ability.*` 열). 스탯 열이 없는 능력치(면역·행동 제한 등)는 무시 | 해석 |
| 적용 방식 | 개체마다 스탯 복사본을 두고 `원래 값 + 걸린 버프 수치 합`. 피해·가드·강인도·상태이상 계산이 모두 이 값을 씀. 캐릭터 정의는 바꾸지 않음 | 해석 |
| 수치 단위 | EX_PC 스탯과 같은 단위로 그대로 더함(감소율·% 는 10만분율). 음수 = 디버프 | 해석 |
| 거는 스킬 | SKILL `Buff_ID`(0 = 없음), `Buff_Duration`(ms, 0 이면 걸지 않음) | 기획(Template_SKILL) |
| 발동 시점 | `Trigger_Condition`: ON_CAST = 행동 시작(비용 차감 직후), ON_HIT = 적중(가드·무적 회피 제외, 같은 tick 타격을 모두 해결한 뒤), ON_END = 끝까지 수행했을 때. 0/NONE = 타격 있는 ATTACK 은 ON_HIT, 그 외 ON_CAST. ON_KILL·ON_GUARD·ON_DAMAGED 는 미구현(걸리지 않음) | 기획 + 해석 |
| 대상 | `Buff_Target`: SELF = 시전자, ENEMY = 적중 대상(ON_HIT) 또는 현재 타겟(스킬 사거리 안), ALLY = 같은 팀 전원(자신 포함, Hit_Range>0 이면 시전자 중심 그 반경 안). 0/NONE = BUFF 스킬은 SELF, 그 외 ENEMY. 자신·아군 대상 ON_HIT 은 여러 명을 맞혀도 스킬당 1회 | 기획 + 해석 |
| 확률 | `Trigger_Chance` 10만분율. 0(미입력) = 항상(`buffZeroChanceAlways=true`). 100% 미만일 때만 난수 사용 | 기획 + 임시 |
| 같은 버프 재적용 | 중첩 없음, 지속 시간만 새로 시작(로그 `갱신`). 서로 다른 Buff_ID 는 수치가 더해짐 | 임시 |
| Trigger_Type | NONE/DURATION 만 지원(지속 시간 동안 능력치 변경). INSTANT·STACK·DOT 은 경고 후 DURATION 으로 처리 | 임시 |
| 만료 | 지속 tick = ceil(Buff_Duration × 30 / 1000). 걸린 tick + 지속 tick 의 시간 처리(1단계)에서 해제 | 해석 |
| 최대 자원 변화 | 버프로 최대 HP/FP/스태미나/강인도가 줄면 현재값을 새 최대치로 자르고, 늘면 현재값도 같은 만큼 증가(`buffMaxIncreaseRaisesCurrent=true`, HP 0 개체는 제외) | 임시 |
| 이벤트 | `BuffApplied`(APPLY/REFRESH), `BuffExpired`. StatusId = `BUFF_<id>`. Console 핵심 로그·CSV 에 기록 | — |

- 피해는 스킬의 Damage_Type 별 공격력으로 계산하므로, 그 타입의 공격력을 바꿔야 피해가 달라진다. 예: 검 스킬은 SLASH 라서 물리 공격력(17, PHYSICAL_ATTACK) 버프는 지금 피해에 영향이 없다(변환기가 경고).

## 7. AI·무브셋

- AI_BEHAVIOR 폭포: 공통(Phase_No=0) 행 → 현재 페이즈 행 순서로, 각각 Eval_Order → Behavior_Index 정렬(`commonRowsFirst`). 조건1 AND 조건2 → 확률(Chance_Pct 0이면 건너뜀, 100이면 난수 미사용) → 쿨다운 → 실행 가능 여부(자원·사거리·무브셋). 실행 불가 행은 확률 추첨 전에 제외. 후보가 없으면 IDLE. **README + 확정 + 해석**
- BUILTIN 수치(config.py 부재): 걷기 2m/s, 달리기 4m/s(ACTION_DEF 설명), 좌우 이동·거리 유지 2m/s, 백스텝 2m/0.5초, 이동·대기 행동 0.5초 단위(기존 로그 15f 간격). **기획/로그/임시**
- 타겟: 최근접, 인지 18m, 해제 30m, `새 거리 < 기존 × 0.8` 이면 전환. **기획**
- incoming_attack: 나를 노리는 적의 다음 타격까지 0.5초 이하 + 사거리 안. target_attack_recover: 대상의 마지막 타격 이후 구간. poise_broken: 경직 종료 후 0.5초. target_heal_recover: 물약 미구현으로 항상 false. **임시/해석**
- 페이즈: HP% 가 범위에 들어가는 페이즈(구간 사이는 유지). 진입 시 Enter_Action_Id 를 쿨다운·사거리 무시하고 1회 실행. **기획**
- 몬스터 AI: 이름 → AI_MAP → 없으면 Kind 폴백(변환기에서 확정, characters.json 에 aiProfileId 저장). **기획**
- 콤보: 같은 입력 슬롯 연계는 Prev_Node_ID 로 찾는다. 이전 공격의 캔슬 구간 안이거나 공격 종료 후 0.3초(`comboContinueWindowSec`) 안에 다음 INPUT 행동을 시작하면 이어지고, 그 외(이동·대기·피격 중단 등)에는 루트 노드로 초기화. **확정 + 임시**
- 캔슬: Cancel_Time=0 은 끝까지 수행. Cancel_Time>0 이면 그 이후 AI 가 다른 행동을 고르면 캔슬(IDLE 선택은 현재 동작 유지). 강제 중단(경직·기절)은 언제나 가능. **확정 + 해석**
- BACKSTAB / RIPOSTE / USE_POTION 은 ACTION_DEF is_use 비활성 그대로 미사용.

## 8. 아직 결정이 필요한 항목

1. Hit_Count 다단히트 11개 스킬의 Hit_Time·Motion_Value 목록 입력.
2. 몬스터 가드 시 스태미나 브레이크 여부, 몬스터 stamina_pct 조건값(현재 100 고정).
3. 회피·백스텝·BLOCK 유지시간·스태미나 비용 등 BUILTIN 수치(현재 임시값).
4. 강인도 회복 속도, 경직 시간의 스킬별 차이(현재 30f 고정).
5. (해결 2026-11-03) 상태이상·강인도 유발에 모션밸류를 곱한다 — 전투공식 '전투 프로세스' 5-3 상태이상 순서도와 강인도 피해 계산 순서도에 명시. 순서도의 '+ 스킬 유발 / 스킬 강인도 공격' 항은 SKILL 표에 열이 없어 현재 0.
6. 투사체 테이블(속도·수명·반경) — 현재 즉발.
7. 버프/디버프 — BUFF 표·능력치 변경은 구현(6-1). 남은 결정: 같은 버프 중첩(STACK)·지속 피해(DOT)·즉시형(INSTANT) 규칙, ON_KILL/ON_GUARD/ON_DAMAGED 발동, 회복·FP 소모량 증가처럼 스탯 열이 없는 효과, 물리 공격력이 하위 타입(참격·관통·타격)에도 반영되는지.
8. 튜토리얼 보스 패턴 시트(조건 1~5, 후방 판정)의 AI 테이블 반영 — 현재 AI_BOSS_DRAGON 페이즈만 사용.
9. (확인 필요, 현재 영향 없음) 경직 유발%(STAGGER_BUILDUP_PCT)가 강인도 계산에 쓰이지 않음. 상처·중독·빙결 유발%는 어빌리티목록에서 '가산값'인데 공통 계산식은 곱으로 처리(기절 유발%만 '곱'이 기획). 현재 데이터의 네 값이 모두 0 이라 결과에는 영향 없음.

기절 누적 근거: `2.ARPG_어빌리티.xlsx` 어빌리티목록 F33(균형 공격력은 경직·기절 유발), F51(기절 유발 비율은 균형 공격력에 곱함). 모션밸류·감소·면역도 기획이다: `1.ARPG_전투공식.xlsx` 전투 프로세스 5-3 상태이상 발생 순서도(유발 = 무기 유발 × 모션 밸류 + 스킬 유발, 방어자 상태 값은 시간당 자동 감소, 저항 − 상태 ≤ 0 이면 발생)와 I373(효과 종료까지 면역). 발동 시 게이지를 0 으로 되돌리는 것만 해석.

2026-11-03 사용자 확정: 구르기 무적 구간은 빙결로 늘리지 않는다. 무적 시계는 벽시계가 아니라 배속·일시정지·1tick 진행을 따르는 논리 tick 경과 시간이다. 빙결이 중간에 발동/해제되어도 무적 시계는 그대로 진행한다. 수치 0.10~0.45초는 기존 설정이며 30Hz에서 tick 단위로 판정한다.
