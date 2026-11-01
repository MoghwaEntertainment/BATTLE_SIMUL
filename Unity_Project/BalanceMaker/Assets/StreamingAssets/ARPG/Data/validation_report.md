# 데이터 변환 검증 보고서

- 생성: 2026-11-01T16:05:28
- 변환 도구: 1.0.0
- dataHash: e3b32153437ff369
- characters: 1398
- charactersEnabled: 1398
- skills: 1808
- skillsInvalid: 11
- movesetNodes: 1751
- aiBehaviors: 200
- 배포 차단 오류: 0 / 레코드 비활성: 11 / 경고: 8 / 정보: 10

## 배포 차단 오류 (0)


## 레코드 비활성(실행 제외) (11)

- `SKILL.xlsm!SKILL!A28` SWORD_WEAPON_ART_002: Hit_Count=3, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A33` SWORD_WEAPON_ART_007: Hit_Count=3, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A56` HAMMER_WEAPON_ART_004: Hit_Count=4, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A58` HAMMER_WEAPON_ART_006: Hit_Count=2, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A80` SPEAR_WEAPON_ART_002: Hit_Count=5, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A86` SPEAR_WEAPON_ART_008: Hit_Count=3, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A106` BOW_WEAPON_ART_002: Hit_Count=2, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A108` BOW_WEAPON_ART_004: Hit_Count=3, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A113` BOW_WEAPON_ART_009: Hit_Count=4, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A132` STAFF_WEAPON_ART_002: Hit_Count=3, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성
- `SKILL.xlsm!SKILL!A134` STAFF_WEAPON_ART_004: Hit_Count=5, Hit_Time 1개, Motion_Value 1개 -> 목록 길이 불일치. 임의 분할/복제하지 않고 스킬 비활성

## 경고 (8)

- `SKILL.xlsm!SKILL!A162` TALISMAN_WEAPON_ART_006: Skill_Type 이 숫자 2 로 저장됨 -> 템플릿 enum 순서로 DEBUFF 매핑
- `SKILL.xlsm!SKILL!A162` TALISMAN_WEAPON_ART_006: DEBUFF 스킬이나 Buff_ID=0 -> 효과 없이 동작만 실행
- `SKILL.xlsm!SKILL!A163` TALISMAN_WEAPON_ART_007: Skill_Type 이 숫자 2 로 저장됨 -> 템플릿 enum 순서로 DEBUFF 매핑
- `SKILL.xlsm!SKILL!A163` TALISMAN_WEAPON_ART_007: DEBUFF 스킬이나 Buff_ID=0 -> 효과 없이 동작만 실행
- `SKILL.xlsm!SKILL!A164` TALISMAN_WEAPON_ART_008: Skill_Type 이 숫자 1 로 저장됨 -> 템플릿 enum 순서로 BUFF 매핑
- `SKILL.xlsm!SKILL!A164` TALISMAN_WEAPON_ART_008: BUFF 스킬이나 Buff_ID=0 -> 효과 없이 동작만 실행
- `SKILL.xlsm!SKILL` 값이 완전히 같은 중복 행 24개 (예: SWORD_LIGHT_ATK_001 A3/A166, 범위 A166~A189) -> 첫 행만 사용
- `EX_PC_STAT.xlsm!EX_PC!D` ~Name 이 #N/A 인 PC 630개 -> 표시 이름으로 ~구분용 사용(전투 ID 는 Index)

## 정보 (10)

- `SKILL.xlsm!Template_SKILL!K26` Hit_Count 설명에 '모션 밸류 / 타격 횟수' 옛 규칙이 남아 있음 -> 사용자 확정 규칙(타격별 Motion_Value, 나누지 않음)을 적용
- `SKILL.xlsm!SKILL!A26` SWORD_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A52` HAMMER_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A78` SPEAR_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A104` BOW_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A130` STAFF_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A156` TALISMAN_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL` DEFENCE 스킬 102개가 Ani_Time=0 -> rules.json 의 blockHoldSecWhenAniTimeZero(임시값)로 가드 유지
- `SKILL.xlsm!SKILL` 몬스터 스킬 96개에 FP 비용이 남아 있음(예: MONSTER_10101_WEAPON_ART_1). rules.json monsterIgnoreSkillCosts=true 이면 실행 시 무시
- `SKILL.xlsm!SKILL` 플레이어 스킬 163개 중 스태미나 비용 50: 163개
