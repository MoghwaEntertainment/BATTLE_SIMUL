# ARPG 전투 밸런스 시뮬레이터 (Unity)

## 바로 실행

1. Unity 에서 `Unity_Project/BalanceMaker` 를 연다(6000.6, URP, Input System).
2. `Assets/Scenes/SampleScene` 에서 **Play** — 시뮬레이터가 자동으로 생성된다.
   또는 메뉴 `ARPG > 1. Simulator 장면 만들기` 로 `Assets/ARPG/Scenes/Simulator.unity` 를 만들어 사용.
3. 왼쪽 패널에서 시나리오를 고르고 `▶ 시작/재개`.

## 화면

- **관찰 모드**: 캡슐(몸)·큐브(무기·정면 표시·방패)·바닥 링(팀 색) 표시. 머리 위 HP(빨강)/FP(파랑)/스태미나(초록) 바.
  - 사용하지 않는 자원(몬스터 FP·스태미나)은 회색 바. 스태미나가 음수이면 바는 0, 배경이 붉게 바뀌고 정보창·로그·CSV 에는 실제 음수가 기록된다.
  - 색: 흰색 점멸=피격, 청록=회피 무적, 주황=경직, 하늘=빙결, 보라=중독, 노란 링=기절, 회색=사망.
  - 노랑 원=공격 판정 범위(선딜), 빨강 원=곧 타격. 선택 개체는 2.5m/6m 거리 대역과 대상 선 표시.
  - 구르기(회피): 회피 시간 동안 몸이 X축(좌우 축)으로 한 바퀴(360°) 돈다. 회피 방향이 정면 쪽이면 앞구르기, 뒤쪽이면 뒷구르기. 바닥에 붙어 구르도록 높이를 맞추고 중간에 살짝 웅크리며, 무기는 잠시 숨긴다.
  - 공격 자세: 선딜 동안 몸을 살짝 뒤로 젖혔다가(−8°) 첫 타격 직전 100ms 에 앞으로 숙이며(+22°) 0.18m 내딛고, 타격마다 조금 더 숙인 뒤 후딜 동안 원위치. 무기 휘두르기와 함께 표시된다.
  - 공격 이펙트(막대·착탄, 기본 켬): 타격이 확정되면 공격자 무기 높이에서 대상 몸통으로 막대가 날아가고(30m/s, 0.1~0.35초), 닿는 순간 착탄 구체와 대상 점멸이 나온다.
    - 노랑=명중, 청록=가드(방패 점멸), 옅은 하늘=회피 무적, 회색=빗나감(정면으로 날아가 사라짐).
    - 시계는 화면에 표시 중인 전투 시간이라 일시정지 시 멈추고 배속·1 tick 진행을 따른다. 끄면 이벤트 즉시 점멸(이전 동작).
    - 화면 전용이며 판정·로그·CSV·결과에는 영향이 없다(이미 코어에서 확정된 이벤트로만 생성).
  - 시작/정지, 1 tick 진행, 재시작(같은 seed), 다음 회차, 배속 0.5/1/2/4/8.
  - 자동 다음 회차(토글): 판이 끝나면 1.2초(배속이 높으면 그만큼 짧게) 뒤 다음 회차를 자동 재생. 회차 번호가 시나리오 `반복` 값에 닿으면 멈춘다. 누적 승패를 패널에 표시하고, 카메라 위치·추적 대상은 유지한다.
  - 좌클릭 선택 → 오른쪽 정보창(ID, 자원 실제값, 강인도, 상태이상 게이지, 걸린 버프와 남은 tick, 현재 행동·진행, AI 행 번호, 페이즈, 쿨다운, 누적 통계).
  - 카메라: 우드래그 회전, 휠 확대, 중드래그/WASD 이동, Q/E 회전, F 추적, Space 재생/정지, `.` 1 tick.
  - 글자 크기: 왼쪽 패널 제목 옆 `글자 - 100% +` 또는 Ctrl + / Ctrl - (80~200%, 8단계). 글자·버튼·패널 폭·정보창·로그창·머리 위 이름이 함께 커지며, 값은 PlayerPrefs(`ARPG.UiScale`)에 저장되어 다음 실행에도 유지된다.
    - 글꼴 크기를 직접 바꾸므로 확대해도 글자가 흐려지지 않는다. Game 뷰 상단의 Scale 슬라이더는 화면을 늘리기만 해서 흐려지므로 1x 로 두는 것을 권장.
- **고속 반복(비시각)**: 캐릭터 오브젝트를 만들지 않고 N회 실행. 진행률·취소·프레임 예산 조절.
- **Self Test**: 규칙 사례(자원 선검사, 대미지메이커 재현, 다단히트, 상태이상 시간, 동시 교환, 폭포 AI)와 결정성 검사.

## 로그와 CSV

- Unity Console: `끔 / 핵심 / 상세` 선택. 형식 예: `[회차 1][tick 39 / 1.300s] 검사_레벨_37#A01 → 1회차_기사 단장_레벨_37#B01 SWORD_RUN_ATK_001 1타 | 피해 62.54 | 대상 HP 1747.00 → 1684.46`
- 저장 위치: `Application.persistentDataPath/Exports/<run_id>/` (패널의 `Export 폴더` 버튼).
  - 관찰: `events_battle_0001.csv`, `result_battle_0001.csv`, `manifest.json` (도중 재시작 시 `_partial`).
  - 고속 반복: `results.csv`(회차별 1행), `summary.csv`(승률·95% 신뢰구간·전투 시간 분위수·처리 속도), `skill_stats.csv`, `events.csv`(선택), `manifest.json`.
- CSV 는 UTF-8 BOM(한글 Excel 호환), 소수점 `.`, 빈칸=해당 없음(0 과 구별). 임시 파일에 쓰고 닫을 때 최종 이름으로 바꾼다.

## 화면 없는 실행

```
BalanceMaker.exe -batchmode -nographics -logFile batch.log -arpgBatch ^
  --scenario 01_근접1대1_검사37_vs_기사단장37 --runs 1000 --seed 12345 --output results --events first:3
```

`-arpgBatch`, `--scenario`, `--runs`, `--seed`, `--output`, `--events none|all|first:N`, `--maxsec` 은 직접 구현한 인자. 종료 코드 0=성공, 2=입력 오류, 3=실행 오류.

## 데이터 갱신

- 엑셀 수정 후 메뉴 `ARPG > 2. 엑셀 → JSON 변환 실행` (또는 `python Tools/ExcelExport/export_tables.py`).
  상위 폴더의 `ARPG_BATTLESIMULATOR/venv` 파이썬을 우선 사용하며 openpyxl 이 필요하다.
- 결과: `Assets/StreamingAssets/ARPG/Data/*.json`, `manifest.json`(원본/출력 해시, dataHash), `validation_report.md`.
- openpyxl 은 엑셀에 저장된 **수식 캐시값**을 읽는다(재계산 아님). 엑셀에서 저장 후 변환할 것.
- 필수 오류(시트 누락·값이 다른 중복 ID)가 있으면 기존 JSON 을 그대로 둔다. 레코드 단위 오류는 해당 스킬/캐릭터만 비활성.
- 시나리오: `Assets/StreamingAssets/ARPG/Scenarios/*.json` (화면에서 편집 후 `저장`). 변환기는 기존 시나리오 파일을 덮어쓰지 않는다.

## 버프

- 표: `Table_ARPG/BUFF.xlsx` 의 `BUFF` 시트. `Buff_ID` 하나에 `Buff_AbilityN`(ABILITY Index) + `Buff_AbilityN_Stat`(더할 수치) 쌍을 여러 개 넣는다.
- 연결: SKILL 의 `Buff_ID`, `Buff_Duration`(ms), `Buff_Target`, `Trigger_Condition`, `Trigger_Chance`.
- 변환하면 `buffs.json` 이 생기고, 버프가 걸리면 로그에 `BUFF_<id> 적용/갱신/해제`, 정보창에 남은 tick 이 나온다. 규칙은 `RULES.md` 6-1.

## 구조

```
Assets/ARPG/
  Core/        순수 C#(UnityEngine 미참조, ARPG.Core.asmdef)
    Definitions/ 데이터 DTO, DataCatalog(읽기 전용), StatId, 시나리오
    Rules/       CombatRules(rules.json)
    Simulation/  BattleSimulation(Step=1/30초), Combatant, ActionInstance
    Combat/      피해·자원 공식
    AI/          조건식 해석기
    Events/      BattleEvent, IBattleEventSink
    Results/     BattleResult, 스킬 집계
    Batch/       BatchCoordinator, BatchSummary
    Tests/       CoreSelfTests
  Runtime/     Unity 연결(ARPG.Runtime.asmdef)
    Infrastructure/ JSON 로더, CSV/Console 출력
    Application/    관찰 배속 실행기, headless 진입점
    Presentation/   부트스트랩·UI(IMGUI)·캡슐 뷰·자원 바·카메라
  Editor/      메뉴(장면 생성, 엑셀 변환, Self Test)
  RULES.md     규칙 매핑과 미결 항목
```

관찰·고속·headless 는 모두 같은 `BattleSimulation.Step()` 과 같은 회차 seed(`masterSeed`, `battleIndex`)를 쓰므로 같은 데이터·규칙·시나리오에서 결과가 같다. 화면 보간은 표시 전용이다.
