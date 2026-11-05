# Excel → JSON 변환기

```
python Tools/ExcelExport/export_tables.py                       # 기본: ../../Table_ARPG → Assets/StreamingAssets/ARPG/Data
python Tools/ExcelExport/export_tables.py --tables <Table_ARPG 경로> --out <출력 폴더>
```

- 입력: SKILL.xlsm(SKILL), EX_PC_STAT.xlsm(EX_PC), MONSTER.xlsm(Monster, Monster_Moveset), ITEM.xlsm(Equip_Moveset), AI.xlsx(ACTION_DEF, AI_BEHAVIOR, AI_PHASE, AI_MAP).
  선택: BUFF.xlsx(BUFF) + ABILITY.xlsm(Ability) — BUFF.xlsx 가 없으면 버프 없이 변환한다.
- 백업 폴더와 `~` 참조 사본은 읽지 않는다.
- 출력: characters.json, skills.json, movesets.json, ai.json, buffs.json, manifest.json, validation_report.md.
- 필요 패키지: openpyxl (`ARPG_BATTLESIMULATOR/requirements.txt` 에 포함).

## 버프 표 (1.2.0)

`Table_ARPG/BUFF.xlsx` 의 `BUFF` 시트: 한 줄 = 버프 하나.

| 열 | 뜻 |
| --- | --- |
| `Buff_ID` | 버프 번호(1 이상, 중복 불가). SKILL 의 `Buff_ID` 가 가리킨다. |
| `is_use` | `All` 이면 사용 |
| `~Name` | 이름(로그 표시용) |
| `Buff_AbilityN` | 바꿀 능력치 = ABILITY 표 `Index` (예: 17 물리 공격력). 0·빈칸 = 사용 안 함 |
| `Buff_AbilityN_Stat` | 더할 수치. EX_PC 스탯과 같은 단위(감소율·% 는 10만분율, 10% = 10000). 음수 = 감소 |
| `~AbilityN_Name` | 참고용 이름 자동 표시(읽지 않음) |

- N 은 몇 개든 된다(`Buff_Ability6` / `Buff_Ability6_Stat` 열을 추가하면 자동으로 읽음).
- ABILITY `ENUM_Name` 이 캐릭터 스탯 열(EX_PC 의 `Ability.*`)에 있어야 적용된다. 면역·행동 제한(88~104번)처럼 스탯 열이 없는 능력치는 경고 후 무시.
- 스킬의 Damage_Type 에 없는 타입의 공격력을 바꾸면(예: 물리 공격력인데 PHYSICAL 스킬이 없음) "지금은 피해에 영향 없음" 경고.
- SKILL 쪽 연결 열(`Additional_Effect`): `Buff_ID`, `Buff_Duration`(ms, 0 이면 안 걸림), `Buff_Target`(SELF/ENEMY/ALLY, 0 = Skill_Type 기본값),
  `Trigger_Condition`(ON_CAST/ON_HIT/ON_END, 0 = 기본값), `Trigger_Chance`(10만분율, 0 = 항상). 숫자로 저장된 enum 은 템플릿 순서로 이름을 찾는다.
  지원하지 않는 값(ON_KILL·ON_GUARD·ON_DAMAGED, STACK·DOT 등)은 경고한다.

## AI·스킬 불일치 경고 (1.1.0)

변환할 때 AI_BEHAVIOR 의 거리 조건과, 그 AI 를 쓰는 캐릭터의 무브셋 스킬 사거리를 맞춰 보고 맞지 않으면 `validation_report.md` 의 **경고**에 `[AI·스킬 불일치]` 로 남긴다(변환은 막지 않음).

- 기준은 시뮬레이터와 같다: `rules.json` 의 `distMeleeMax`(근접 끝), `distMidMax`(중거리 끝), `bodyRadius`, `instantRangedThreshold`. 다른 rules.json 을 쓰려면 `--rules <경로>`.
- 도달 거리(두 몸 중심 사이) = 몸 반지름×2 + (사거리가 즉발 원거리 기준을 넘으면 사거리, 아니면 max(사거리, 판정) + 전진 거리).
- 판정
  - **항상 사거리 밖**: 조건 구간의 가장 가까운 거리보다 도달 거리가 짧음 → 그 행은 시뮬레이터에서 늘 '사거리 밖'으로 건너뜀.
  - **대부분 사거리 밖**: 조건 구간의 절반 미만만 닿음(far 구간의 끝은 인식 거리 `perceptionRange` 로 계산).
  - **슬롯 없음 / 스킬 비활성**: 무브셋에 그 입력 슬롯이 없거나 슬롯 스킬이 모두 비활성이라 실행되지 않음.
- 같은 AI 행·같은 판정·같은 수치는 무브셋이 달라도 한 줄로 묶고, 해당 무브셋·캐릭터 이름·수를 함께 적는다.
- 거리 조건이 없거나 붙어 있을 때도 참인 행(`dist==melee`, `dist!=far`, `dist_m<=3` 등)은 검사하지 않는다.
