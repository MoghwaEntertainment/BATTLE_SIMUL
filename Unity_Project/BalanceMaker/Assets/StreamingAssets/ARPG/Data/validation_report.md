# 데이터 변환 검증 보고서

- 생성: 2026-11-06T16:22:16
- 변환 도구: 1.6.0
- dataHash: 8b104d48d08c7e27
- gameConfigEntries: 11
- characters: 1411
- charactersEnabled: 1411
- skills: 1826
- skillsInvalid: 11
- movesetNodes: 1769
- aiBehaviors: 267
- buffs: 11
- buffsEnabled: 11
- skillsWithBuff: 9
- 배포 차단 오류: 0 / 레코드 비활성: 11 / 경고: 38 / 정보: 12

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

## 경고 (38)

- `SKILL.xlsm!SKILL!A162` TALISMAN_WEAPON_ART_006: Skill_Type 이 숫자 2 로 저장됨 -> 템플릿 enum 순서로 DEBUFF 매핑
- `SKILL.xlsm!SKILL!A162` TALISMAN_WEAPON_ART_006: DEBUFF 스킬이나 Buff_ID=0 -> 효과 없이 동작만 실행
- `SKILL.xlsm!SKILL!A163` TALISMAN_WEAPON_ART_007: Skill_Type 이 숫자 2 로 저장됨 -> 템플릿 enum 순서로 DEBUFF 매핑
- `SKILL.xlsm!SKILL!A163` TALISMAN_WEAPON_ART_007: DEBUFF 스킬이나 Buff_ID=0 -> 효과 없이 동작만 실행
- `SKILL.xlsm!SKILL!A164` TALISMAN_WEAPON_ART_008: Skill_Type 이 숫자 1 로 저장됨 -> 템플릿 enum 순서로 BUFF 매핑
- `SKILL.xlsm!SKILL!A164` TALISMAN_WEAPON_ART_008: BUFF 스킬이나 Buff_ID=0 -> 효과 없이 동작만 실행
- `SKILL.xlsm!SKILL!A1851` PRIEST_SKILL_001: Buff_ID=5 이지만 Buff_Duration=0 -> 버프가 걸리지 않음
- `SKILL.xlsm!SKILL!A1852` PRIEST_SKILL_002: Buff_ID=11 이지만 Buff_Duration=0 -> 버프가 걸리지 않음
- `SKILL.xlsm!SKILL` 값이 완전히 같은 중복 행 24개 (예: SWORD_LIGHT_ATK_001 A3/A166, 범위 A166~A189) -> 첫 행만 사용
- `EX_PC_STAT.xlsm!EX_PC!D` ~Name 이 #N/A 인 PC 636개 -> 표시 이름으로 ~구분용 사용(전투 ID 는 Index)
- `BUFF.xlsx!BUFF 버프 1` 17 물리 공격력(PHYSICAL_ATTACK) 를 바꾸지만 Damage_Type PHYSICAL 인 스킬이 없음 -> 지금은 피해에 영향 없음 (피해는 스킬의 Damage_Type 별 공격력으로 계산)
- `GameConfig.xlsm!GameConfig!A13` NOTICE_RANGE: 변환하지만 현재 전투 규칙 연결은 없음
- `AI.xlsx!AI_BEHAVIOR #11` [AI·스킬 불일치] AI_20001 (dist==mid → RUN_ATK 30%): 거리 조건 2.5m 초과~6m 인데 RUN_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 SWORD_001 / 스킬 예: SWORD_RUN_ATK_001 / 대상 검사_레벨_1, 검사_레벨_10, 검사_레벨_100, 검사_레벨_101, 검사_레벨_102, 검사_레벨_103 외 624종 (캐릭터 630개)
- `AI.xlsx!AI_BEHAVIOR #110` [AI·스킬 불일치] AI_BEAST (dist==far → RUN_ATK 40%): 거리 조건 6m 초과~∞ 인데 RUN_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10121, MONSTER_10122, MONSTER_10124, MONSTER_10125 외 5개 / 스킬 예: MONSTER_10121_RUN_ATK_1 / 대상 개, 늑대, 백호 (캐릭터 72개)
- `AI.xlsx!AI_BEHAVIOR #112` [AI·스킬 불일치] AI_BEAST (dist==mid → RUN_ATK 35%): 거리 조건 2.5m 초과~6m 인데 RUN_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10121, MONSTER_10122, MONSTER_10124, MONSTER_10125 외 5개 / 스킬 예: MONSTER_10121_RUN_ATK_1 / 대상 개, 늑대, 백호 (캐릭터 72개)
- `AI.xlsx!AI_BEHAVIOR #113` [AI·스킬 불일치] AI_BEAST (dist==mid → JUMP_LIGHT_ATK 25%): 거리 조건 2.5m 초과~6m 인데 JUMP_LIGHT_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10121, MONSTER_10122, MONSTER_10124, MONSTER_10125 외 5개 / 스킬 예: MONSTER_10121_JUMP_LIGHT_ATK_1 / 대상 개, 늑대, 백호 (캐릭터 72개)
- `AI.xlsx!AI_BEHAVIOR #177` [AI·스킬 불일치] AI_BOSS_DRAGON (dist==far → WEAPON_ART 30%): 거리 조건 6m 초과~∞ 인데 WEAPON_ART 최대 도달 5.3m (사거리 2 / 판정 2.5 / 전진 2 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10160, MONSTER_10183, MONSTER_10185, MONSTER_10186 외 9개 / 스킬 예: MONSTER_10160_WEAPON_ART_1 / 대상 고룡, 뇌룡, 데몬 로드, 빙룡, 재앙룡, 화염룡 (캐릭터 104개)
- `AI.xlsx!AI_BEHAVIOR #178` [AI·스킬 불일치] AI_BOSS_DRAGON (dist==far → JUMP_HEAVY_ATK 35%): 거리 조건 6m 초과~∞ 인데 JUMP_HEAVY_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10160, MONSTER_10183, MONSTER_10185, MONSTER_10186 외 9개 / 스킬 예: MONSTER_10160_JUMP_HEAVY_ATK_1 / 대상 고룡, 뇌룡, 데몬 로드, 빙룡, 재앙룡, 화염룡 (캐릭터 104개)
- `AI.xlsx!AI_BEHAVIOR #181` [AI·스킬 불일치] AI_BOSS_DRAGON (dist==mid → RUN_ATK 30%): 거리 조건 2.5m 초과~6m 인데 RUN_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10160, MONSTER_10183, MONSTER_10185, MONSTER_10186 외 9개 / 스킬 예: MONSTER_10160_RUN_ATK_1 / 대상 고룡, 뇌룡, 데몬 로드, 빙룡, 재앙룡, 화염룡 (캐릭터 104개)
- `AI.xlsx!AI_BEHAVIOR #188` [AI·스킬 불일치] AI_BOSS_DRAGON (dist==far → WEAPON_ART 45%): 거리 조건 6m 초과~∞ 인데 WEAPON_ART 최대 도달 5.3m (사거리 2 / 판정 2.5 / 전진 2 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10160, MONSTER_10183, MONSTER_10185, MONSTER_10186 외 9개 / 스킬 예: MONSTER_10160_WEAPON_ART_1 / 대상 고룡, 뇌룡, 데몬 로드, 빙룡, 재앙룡, 화염룡 (캐릭터 104개)
- `AI.xlsx!AI_BEHAVIOR #189` [AI·스킬 불일치] AI_BOSS_DRAGON (dist==far → JUMP_HEAVY_ATK 45%): 거리 조건 6m 초과~∞ 인데 JUMP_HEAVY_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10160, MONSTER_10183, MONSTER_10185, MONSTER_10186 외 9개 / 스킬 예: MONSTER_10160_JUMP_HEAVY_ATK_1 / 대상 고룡, 뇌룡, 데몬 로드, 빙룡, 재앙룡, 화염룡 (캐릭터 104개)
- `AI.xlsx!AI_BEHAVIOR #192` [AI·스킬 불일치] AI_BOSS_DRAGON (dist==mid → RUN_ATK 35%): 거리 조건 2.5m 초과~6m 인데 RUN_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10160, MONSTER_10183, MONSTER_10185, MONSTER_10186 외 9개 / 스킬 예: MONSTER_10160_RUN_ATK_1 / 대상 고룡, 뇌룡, 데몬 로드, 빙룡, 재앙룡, 화염룡 (캐릭터 104개)
- `AI.xlsx!AI_BEHAVIOR #193` [AI·스킬 불일치] AI_BOSS_DRAGON (dist==mid → JUMP_LIGHT_ATK 30%): 거리 조건 2.5m 초과~6m 인데 JUMP_LIGHT_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10160, MONSTER_10183, MONSTER_10185, MONSTER_10186 외 9개 / 스킬 예: MONSTER_10160_JUMP_LIGHT_ATK_1 / 대상 고룡, 뇌룡, 데몬 로드, 빙룡, 재앙룡, 화염룡 (캐릭터 104개)
- `AI.xlsx!AI_BEHAVIOR #95` [AI·스킬 불일치] AI_CASTER (dist==far → WEAPON_ART 60%): 거리 조건 6m 초과~∞ 인데 WEAPON_ART 최대 도달 5.3m (사거리 2 / 판정 2.5 / 전진 2 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10113, MONSTER_10116, MONSTER_10117, MONSTER_10120 외 4개 / 스킬 예: MONSTER_10113_WEAPON_ART_1 / 대상 대마도사, 리치, 마도국 마법사, 신성국 신관 (캐릭터 64개)
- `AI.xlsx!AI_BEHAVIOR #96` [AI·스킬 불일치] AI_CASTER (dist==far → LIGHT_ATK 30%): 거리 조건 6m 초과~∞ 인데 LIGHT_ATK 최대 도달 2.8m (사거리 2 / 판정 1.5 / 전진 0 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10113, MONSTER_10116, MONSTER_10117, MONSTER_10120 외 4개 / 스킬 예: MONSTER_10113_LIGHT_ATK_1 / 대상 대마도사, 리치, 마도국 마법사, 신성국 신관 (캐릭터 64개)
- `AI.xlsx!AI_BEHAVIOR #139` [AI·스킬 불일치] AI_HEAVY (dist!=melee → JUMP_HEAVY_ATK 15%): 거리 조건 2.5m 초과~∞ 인데 JUMP_HEAVY_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 5%만 닿음). 무브셋 MONSTER_10130, MONSTER_10131, MONSTER_10134, MONSTER_10137 외 9개 / 스킬 예: MONSTER_10130_JUMP_HEAVY_ATK_1 / 대상 골렘, 곰, 트롤 (캐릭터 104개)
- `AI.xlsx!AI_BEHAVIOR #45` [AI·스킬 불일치] AI_MELEE_ELITE (dist==far → JUMP_HEAVY_ATK 30%): 거리 조건 6m 초과~∞ 인데 JUMP_HEAVY_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10105, MONSTER_10109, MONSTER_10110, MONSTER_10115 외 2개 / 스킬 예: MONSTER_10105_JUMP_HEAVY_ATK_1 / 대상 기사 단장, 데스 나이트, 도적 대장, 왕국군 대장 (캐릭터 48개)
- `AI.xlsx!AI_BEHAVIOR #48` [AI·스킬 불일치] AI_MELEE_ELITE (dist==mid → RUN_ATK 30%): 거리 조건 2.5m 초과~6m 인데 RUN_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10105, MONSTER_10109, MONSTER_10110, MONSTER_10115 외 2개 / 스킬 예: MONSTER_10105_RUN_ATK_1 / 대상 기사 단장, 데스 나이트, 도적 대장, 왕국군 대장 (캐릭터 48개)
- `AI.xlsx!AI_BEHAVIOR #49` [AI·스킬 불일치] AI_MELEE_ELITE (dist==mid → JUMP_LIGHT_ATK 25%): 거리 조건 2.5m 초과~6m 인데 JUMP_LIGHT_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10105, MONSTER_10109, MONSTER_10110, MONSTER_10115 외 2개 / 스킬 예: MONSTER_10105_JUMP_LIGHT_ATK_1 / 대상 기사 단장, 데스 나이트, 도적 대장, 왕국군 대장 (캐릭터 48개)
- `AI.xlsx!AI_BEHAVIOR #30` [AI·스킬 불일치] AI_MELEE_STD (dist==mid → RUN_ATK 20%): 거리 조건 2.5m 초과~6m 인데 RUN_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10101, MONSTER_10104, MONSTER_10108, MONSTER_10111 외 7개 / 스킬 예: MONSTER_10101_RUN_ATK_1 / 대상 도적 검병, 신성국 성기사, 악마병, 왕국군 검병 (캐릭터 88개)
- `AI.xlsx!AI_BEHAVIOR #162` [AI·스킬 불일치] AI_PREDATOR (dist==far → JUMP_HEAVY_ATK 35%): 거리 조건 6m 초과~∞ 인데 JUMP_HEAVY_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10144, MONSTER_10147, MONSTER_10152, MONSTER_10181 외 3개 / 스킬 예: MONSTER_10144_JUMP_HEAVY_ATK_1 / 대상 그리핀, 비룡 (캐릭터 56개)
- `AI.xlsx!AI_BEHAVIOR #164` [AI·스킬 불일치] AI_PREDATOR (dist==mid → JUMP_LIGHT_ATK 30%): 거리 조건 2.5m 초과~6m 인데 JUMP_LIGHT_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10144, MONSTER_10147, MONSTER_10152, MONSTER_10181 외 3개 / 스킬 예: MONSTER_10144_JUMP_LIGHT_ATK_1 / 대상 그리핀, 비룡 (캐릭터 56개)
- `AI.xlsx!AI_BEHAVIOR #165` [AI·스킬 불일치] AI_PREDATOR (dist==mid → RUN_ATK 30%): 거리 조건 2.5m 초과~6m 인데 RUN_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10144, MONSTER_10147, MONSTER_10152, MONSTER_10181 외 3개 / 스킬 예: MONSTER_10144_RUN_ATK_1 / 대상 그리핀, 비룡 (캐릭터 56개)
- `AI.xlsx!AI_BEHAVIOR #80` [AI·스킬 불일치] AI_RANGED (dist==far → LIGHT_ATK 70%): 거리 조건 6m 초과~∞ 인데 LIGHT_ATK 최대 도달 6.8m (사거리 2 / 판정 6 / 전진 0 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 3%만 닿음). 무브셋 MONSTER_10103 / 스킬 예: MONSTER_10103_LIGHT_ATK_1 / 대상 도적 활병 (캐릭터 8개)
- `AI.xlsx!AI_BEHAVIOR #80` [AI·스킬 불일치] AI_RANGED (dist==far → LIGHT_ATK 70%): 거리 조건 6m 초과~∞ 인데 LIGHT_ATK 최대 도달 2.8m (사거리 2 / 판정 1.5 / 전진 0 + 몸 반지름 0.4×2) → 항상 사거리 밖. 무브셋 MONSTER_10106 / 스킬 예: MONSTER_10106_LIGHT_ATK_1 / 대상 왕국군 활병 (캐릭터 8개)
- `AI.xlsx!AI_BEHAVIOR #84` [AI·스킬 불일치] AI_RANGED (dist==mid → LIGHT_ATK 40%): 거리 조건 2.5m 초과~6m 인데 LIGHT_ATK 최대 도달 2.8m (사거리 2 / 판정 1.5 / 전진 0 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 9%만 닿음). 무브셋 MONSTER_10106 / 스킬 예: MONSTER_10106_LIGHT_ATK_1 / 대상 왕국군 활병 (캐릭터 8개)
- `AI.xlsx!AI_BEHAVIOR #67` [AI·스킬 불일치] AI_SPEAR (dist==mid → LIGHT_ATK 45%): 거리 조건 2.5m 초과~6m 인데 LIGHT_ATK 최대 도달 2.8m (사거리 2 / 판정 1.5 / 전진 0 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 9%만 닿음). 무브셋 MONSTER_10102, MONSTER_10107 / 스킬 예: MONSTER_10102_LIGHT_ATK_1 / 대상 도적 창병, 왕국군 창병 (캐릭터 16개)
- `AI.xlsx!AI_BEHAVIOR #127` [AI·스킬 불일치] AI_VENOM (dist==mid → RUN_ATK 40%): 거리 조건 2.5m 초과~6m 인데 RUN_ATK 최대 도달 3.8m (사거리 2 / 판정 1.5 / 전진 1 + 몸 반지름 0.4×2) → 대부분 사거리 밖(조건 구간의 37%만 닿음). 무브셋 MONSTER_10123, MONSTER_10127, MONSTER_10133, MONSTER_10135 외 2개 / 스킬 예: MONSTER_10123_RUN_ATK_1 / 대상 독거미, 전갈 (캐릭터 48개)

## 정보 (12)

- `SKILL.xlsm!Template_SKILL!K26` Hit_Count 설명에 '모션 밸류 / 타격 횟수' 옛 규칙이 남아 있음 -> 사용자 확정 규칙(타격별 Motion_Value, 나누지 않음)을 적용
- `SKILL.xlsm!SKILL!A26` SWORD_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A52` HAMMER_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A78` SPEAR_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A104` BOW_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A130` STAFF_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL!A156` TALISMAN_BLOCK_001: 플레이어 방어 동작 Cancel_Time=0 (사용자 임시값) -> 공통 규칙(끝까지 수행) 적용
- `SKILL.xlsm!SKILL` DEFENCE 스킬 102개가 Ani_Time=0 -> rules.json 의 blockHoldSecWhenAniTimeZero(임시값)로 가드 유지
- `SKILL.xlsm!SKILL` 몬스터 스킬 96개에 FP 비용이 남아 있음(예: MONSTER_10101_WEAPON_ART_1). rules.json monsterIgnoreSkillCosts=true 이면 실행 시 무시
- `SKILL.xlsm!SKILL` 플레이어 스킬 181개 중 스태미나 비용 50: 163개
- `BUFF.xlsx` 버프 11개(활성 11), SKILL 에서 Buff_ID 를 쓰는 스킬 9개
- `AI.xlsx!AI_BEHAVIOR` AI·스킬 불일치 경고 26건: 거리 기준 rules.json (근접 ≤2.5m, 중거리 ≤6m, 몸 반지름 0.4m, 즉발 원거리 사거리 >6m). 원거리 끝은 인식 거리 30m 로 보고 계산
