# 전투 규칙 구현 매핑 (rules.json)

모든 수치는 `Assets/StreamingAssets/ARPG/rules.json` 에서 바꿀 수 있다(코드 재컴파일 불필요, 플레이 중 `데이터 다시 읽기`).
출처 표기: **확정**=사용자 확정(Unity_Plan 11장) / **기획**=기획 엑셀 / **로그**=기존 4.전투시뮬레이터 결과 / **해석**=문서 문구를 구현 규칙으로 옮긴 것 / **임시**=근거 없음, 검토 필요.

## 1. 시간·실행 순서

| 항목 | 구현 | 출처 |
| --- | --- | --- |
| 논리 시간 | 30Hz tick, AI 3 tick(10Hz)마다 판단 | 확정/기획 |
| 타격 시점 | `이전 진행 < Hit_Time <= 현재 진행`(오차 1e-6ms), 한 tick 에 여러 시점 통과 시 모두 처리. 300ms→행동 9번째 tick | 확정 |
| 행동 속도 | Ani_Speed × 페이즈 Speed_Mult × (빙결 시 0.5) 를 행동 진행 시간에 반영 | 기획/확정 |
| tick 순서 | 상태·회복 → AI(타겟/의도 수집) → 승인·차감·시작 → 진행·이동 → 겹침 해소 → 타격 동시 해결 → 강제 중단 → 행동 종료 → 사망 → 승패 | 모듈설계 5장 |
| 동시 교환 | 같은 tick 에 성립한 타격은 모두 해결, 사망은 해결 후 확정(동시 전멸 가능) | 확정 |
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
| 무적 | DODGE 0.10~0.45초 무적(회피 0.7초, 3m 후방, 스태미나 50) | 임시 |
| 가드 | BLOCK(DEFENCE 스킬) 동안 전방 180도 공격을 가드. HP 피해 = 피해 × (1 − GUARD_[타입]_NEGATION/100000). 스태미나 피해 = 피해 × **10** / GUARD_STRENGTH. 스태미나 < 0 → 0 으로 만들고 가드 브레이크(기절 90f, 그로기) | 확정/기획 |
| BLOCK 유지 | Ani_Time=0 인 방어 스킬은 `blockHoldSecWhenAniTimeZero=1.0초` 유지 | 임시 |
| 가드 시 강인도/상태이상 | 적용하지 않음(순서도 가드 분기에 없음) | 해석 |
| 패리 | 패리 프레임을 가진 스킬 데이터가 없어 미구현 | — |
| 강인도 | 공격 강인도 = STAGGER_BUILDUP × 모션밸류. 방어자 스킬의 Add_Poise(Poise_Start~End)가 타격마다 그만큼 흡수. 0 이하 → 경직 30f, 강인도 최대치로 초기화, 행동 강제 중단 | 기획/로그/해석 |
| 강인도 회복 | 마지막 피격 5초 후 회복 시작. 회복 속도 정보가 없어 즉시 최대치(`poiseRecoveryPerSec=-1`) | 기획/임시 |
| 상태이상 누적 | 유발 = [종류]_BUILDUP × 모션밸류 × (1 + [종류]_BUILDUP_PCT). 게이지 ≥ 저항 → 발동, 게이지 0 | 기획/해석 |
| 게이지 감소 | 쌓인 직후부터 초당 5(매 tick 5/30), 0 미만 없음, 같은 tick 신규분은 다음 tick 부터 | 확정 |
| 상처 | 최대 HP 15% 즉시 피해, 즉시 재누적 가능(면역 없음) | 기획/확정 |
| 중독 | 10초, 발동 +1초부터 1초 간격 최대 HP 5% × 10회, 마지막 피해 후 만료, 효과 중 면역 | 기획/확정 |
| 빙결 | 5초, 이동·공격·구르기만 50% 감속(회복·쿨다운·중독 주기 유지), 효과 중 면역 | 기획/확정 |
| 기절 | 누적 스탯이 없어 가드 브레이크로만 발생(90f) | 기획 |

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
5. 상태이상·강인도 유발에 모션밸류를 곱하는지.
6. 투사체 테이블(속도·수명·반경) — 현재 즉발.
7. 버프/디버프 테이블(Buff_ID) — 현재 효과 없음.
8. 튜토리얼 보스 패턴 시트(조건 1~5, 후방 판정)의 AI 테이블 반영 — 현재 AI_BOSS_DRAGON 페이즈만 사용.
