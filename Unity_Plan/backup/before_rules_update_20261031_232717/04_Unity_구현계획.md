# Unity 구현 계획

작성일: 2026-10-31. 이 문서는 개발 착수용 설계안이다. Unity 프로젝트나 기능을 구현·실행한 결과 보고서는 아니다. 기존 규칙의 근거와 미확정 항목은 `01_기존자료_분석정리.md`를 먼저 참고한다.

## 1. 목표와 초기 선택

**같은 전투를 화면으로 관찰하거나 화면 없이 빠르게 계산하는 도구**를 만든다. 최초 대상은 Windows 데스크톱, AI 자동 전투, 평평한 전장이다. 좌표 계산은 XZ 평면에서 하고 화면에는 3D 캡슐과 큐브를 사용한다. 높낮이·실제 물리 반동·복잡한 장애물은 첫 단계에서 제외한다.

Unity 버전은 개발을 시작할 때 설치 가능한 지원 중 LTS에서 하나를 고정하고 정확한 에디터/패키지 버전을 저장한다. 문서의 API 근거는 Unity 6.0 공식 문서로 확인했다. 아래 구조는 특정 렌더 파이프라인 기능에 의존하지 않는다.

| 요구사항 | 구현 위치 | 최소 완료 기준 |
| --- | --- | --- |
| 캡슐·기본 도형 캐릭터 | CombatantView 프리팹 | 서로 다른 팀과 개체를 구분하고 위치·회전·공격 상태 표시 |
| Unity 로그 | UnityConsoleSink | 회차·tick·행위자·대상·피해·자원 변화를 Console에서 확인 |
| CSV 저장 | CsvEventSink / 결과 Exporter | 한글 정상, Excel에서 열림, Console과 같은 이벤트를 추적 가능 |
| HP/FP/스태미나 바 | WorldSpaceResourceBars | 머리 위 현재/최대값과 비율, 사용하지 않는 자원 표기 |
| 배속 | VisualSimulationRunner | 0.5/1/2/4/8배 제안, 같은 시드의 최종 결과 유지 |
| 비시각 고속 실행 | BatchSimulationRunner | 캐릭터 오브젝트 없이 N회 실행, 진행·취소·집계·파일 저장 |

## 2. 프로젝트 폴더와 장면

```text
Unity_ARPG_Simulator/
  Assets/ARPG/
    Core/
      Definitions/ State/ AI/ Actions/ Combat/ Spatial/ Effects/ Events/
    Application/
      Scenarios/ Runners/ Results/
    Infrastructure/
      Data/ Logging/ Export/
    Presentation/
      Views/ UI/ Camera/ DebugDraw/
    Editor/
      DataImport/ Validation/
    Tests/
      EditMode/ PlayMode/
    Scenes/
      Simulator.unity
      BatchBootstrap.unity
    Prefabs/
      CombatantView.prefab
      ResourceBars.prefab
    Data/
      Definitions/ Scenarios/ VisualProfiles/
  Tools/
    ExcelExport/
  Docs/
```

**Core와 Application은 Unity 오브젝트 없이 실행 가능한 C# 영역으로 둔다.** `.asmdef`로 참조 방향을 관리한다. Editor의 데이터 검증 코드는 플레이어 빌드에 포함하지 않는다. 처음에는 Simulator 장면 하나에서 시각/비시각 모드를 전환해도 되며, 독립 실행이 필요해질 때 BatchBootstrap을 추가한다.

Simulator 장면 구성:

- SimulationBootstrap: 데이터 로드, 시나리오 생성, 실행기와 로그 구독 연결.
- ArenaRoot: 바닥 Plane, 팀별 스폰 표식, 화면용 격자.
- CameraRig: 사선 또는 탑다운 시점, 확대·이동.
- CombatantViewRoot: 표시용 캐릭터 인스턴스.
- ScreenCanvas: 시나리오/시드/반복수, 시작·정지·단계 진행·재시작, 배속, CSV 저장, 결과.

## 3. 엑셀에서 Unity로 가져오기

### 3.1 첫 단계는 최종 스탯 중심

초기에는 EX_PC와 Monster의 계산 완료값을 사용한다. 성장·장비 생성기를 Unity 안에 동시에 다시 만들면 피해 공식 오류인지 스탯 생성 오류인지 구별하기 어렵다. 생성기는 기존 Excel에 두고, 검증된 출력 스냅샷을 사용한다. 이후 필요할 때 StatsBuilder를 확장한다.

데이터 흐름:

```text
선택된 원본 엑셀
→ 필드/단위/참조 검증
→ 버전이 붙은 정규화 JSON
→ 읽기 전용 DataCatalog
→ BattleScenario
→ 전투 실행
```

### 3.2 입력 목록

| 정규화 출력 | 원본 |
| --- | --- |
| abilities.json | ABILITY / Ability |
| characters.json | EX_PC_STAT / EX_PC, MONSTER / Monster |
| skills.json | SKILL / SKILL |
| movesets.json | ITEM / Equip_Moveset, MONSTER / Monster_Moveset |
| ai.json | AI / ACTION_DEF, AI_BEHAVIOR, AI_PHASE, AI_MAP |
| localization.json | LOCALIZE_CONTENTS / LOCALIZE_CONTENTS |
| manifest.json | 스키마 버전, 원본 해시, 출력 해시, 규칙 버전, 변환 도구 버전 |

전체 장비 생성까지 확장할 때 Item_Master, Equip_Stats, Equip_Enhance, Scaling_Grade, PLAYER_LEVEL, STAT_LEVEL을 추가한다. 백업과 `~` 참조 사본은 입력 목록에서 제외하되 참조 검증용으로 비교할 수 있다.

### 3.3 변환 규칙

1. 테이블별 헤더 행을 명시한다. SKILL과 EX_PC/Monster처럼 1~2행 복합 헤더는 `Cost.HP`, `Animation.Hit_Time`처럼 경로로 펼친다.
2. `is_use=All`만 활성 대상으로 삼고 빈값/None의 처리는 명시한다. ID는 화면 이름과 분리해 문자열로 보존한다.
3. Excel 수식 자체를 Unity에서 평가하지 않는다. 수식이 있는 입력은 계산 엔진에서 재계산·저장한 스냅샷을 사용하거나 검증된 별도 계산 코드로 생성한다. Python의 수식 캐시 읽기는 재계산이 아니다.
4. `#REF!`, `#N/A`, 필수 필드 누락, 중복 ID, 존재하지 않는 Skill_Id/Moveset_ID/AI 프로필을 보고한다. 표시 이름 오류는 경고+명시적 대체 이름을 허용할 수 있으나 전투 필수 참조 오류는 중단한다.
5. Ability·Motion_Value·Trigger_Chance의 십만분율, AI의 백분율, 시간 ms, 거리 m를 필드별로 변환한다. Ani_Speed의 설명/값 충돌은 변환 설정에 기록한다.
6. SKILL 타입·대상·피해 종류는 실제 문자열과 허용 목록을 명시적으로 매핑한다. 다중 값은 배열로 변환한다.
7. 파일 하나라도 필수 검증 실패 시 새로운 데이터 묶음을 배포하지 않는다. 기존 정상 묶음은 유지하고 실패 보고서를 남긴다.

기존 AI 매핑의 이름 파싱은 Importer에서 한 번 수행하고 정규화 캐릭터에는 AiProfileId를 직접 저장한다. 시나리오는 데이터 해시를 함께 기록해 다음 데이터 갱신 후 같은 이름으로 다른 전투를 실행하는 혼동을 막는다.

## 4. 전투 시간과 배속

### 4.1 30Hz 논리 시간

전투 시간은 `tick / 30.0`이다. AI는 3 tick마다 평가하여 문서의 10Hz와 맞춘다. 스킬의 ms는 원본 값을 보존하고, 논리 시간과 비교할 때 최초 도달 tick에서 발동한다. 예를 들어 300ms는 9 tick, 800ms는 24 tick이다. `ceil(ms × 30 / 1000)`을 기본으로 하되 1333.333…ms처럼 프레임 변환에서 생긴 오차는 변환기의 허용오차/원본 프레임 우선 규칙으로 다룬다.

행동 속도 변경은 매 tick 쌓는 행동 진행 시간에 반영한다. 타격은 ‘시간과 정확히 같은가’가 아니라 `이전 진행 시간 < 타격 시점 <= 현재 진행 시간`으로 검사해 빨라져도 누락되지 않게 한다. 시작 시점 0의 이벤트는 행동 시작 처리에서 정확히 한 번 발행한다.

### 4.2 관찰 모드 배속

```text
매 화면 프레임:
  일시정지면 UI/카메라만 갱신하고 종료
  accumulator += unscaledDeltaTime × speed
  처리 tick 수가 프레임 예산 이내인 동안:
    accumulator가 1/30초 미만이거나 전투가 끝났으면 중지
    simulation.Step()
    accumulator -= 1/30초
  최신 전투 상태로 화면 갱신
```

`Time.unscaledDeltaTime`은 timeScale의 영향을 받지 않는 시간이다. 이를 이용하고 전투 배속은 자체 speed 하나로 관리한다. [Unity 공식 API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-unscaledDeltaTime.html)

`Time.timeScale`까지 함께 곱하면 배속이 이중 적용될 수 있으므로 이 설계에서는 전투 배속 목적으로 변경하지 않는다. tick이 밀리면 누적 시간을 보존하거나 명시적으로 실제 재생 배속을 낮추고 ‘처리 지연’을 표시한다. **전투 tick을 건너뛰어서 속도를 맞추지 않는다.** 일시정지·취소 UI는 배속과 무관하게 동작한다.

화면 위치는 최근 두 상태 사이를 보간할 수 있다. 처리 지연이 큰 경우 보간 비율을 제한하고 최신 상태를 보여준다. 보간된 Transform이 코어의 공격 거리나 위치를 바꾸지 않게 한다.

## 5. 캡슐·기본 도형 시각화

CombatantView 프리팹의 제안 구성:

```text
CombatantView
  Body       : Capsule 또는 Cube
  Head       : Sphere(선택)
  Weapon     : 길게 늘린 Cube
  Shield     : 납작한 Cube(선택)
  FacingMark : 전방 표시용 도형
  Selection  : 바닥 선택 링
  BarsAnchor : 머리 높이 + 여백
    ResourceBars
```

- 아군/적군을 색과 이름표로 구별한다. 클래스별 Body 비율과 무기 모양은 별도 VisualProfile로 관리한다.
- 공격 시작 시 무기 도형 회전, 회피 시 짧은 이동/색 변화, 피격 시 점멸, 사망 시 눕히거나 회색 처리한다.
- 모션의 기준은 Core의 행동 단계와 진행률이다. AnimationEvent가 피해를 발생시키는 구조는 사용하지 않는다.
- 첫 구현은 코어의 원/부채꼴/선분 판정으로 계산하고 그 범위를 화면에도 그린다. 캡슐 Collider는 클릭 선택 등에 쓸 수 있으나 `OnTriggerEnter`를 유일한 피해 판정으로 삼지 않는다.
- 같은 평면이라도 개체 반경, 겹침 허용 여부, 이동 차단 순서를 명시한다. MVP는 장애물 없는 전장과 간단한 겹침 해소로 시작하되 비시각 모드에도 동일 계산을 적용한다.

## 6. 머리 위 HP·FP·스태미나 바

World Space Canvas 안에 3개의 Image Filled 또는 Slider를 놓는다. HP 빨강, FP 파랑, 스태미나 초록을 기본으로 하고 짧은 라벨/수치를 함께 제공한다. 카메라 쪽을 보게 회전시키고 위치는 BarsAnchor를 따른다.

계산은 `최댓값 > 0 ? clamp(현재값 / 최댓값, 0, 1) : 0`이다. 최댓값 0을 무한으로 자동 처리하지 않는다. ResourcePolicy를 `Finite / Unlimited / Disabled`로 명시하고, 정책에 따라 비활성 바 또는 ∞를 표시한다. **이 정책은 신규 제안이며 몬스터 FP/스태미나 0의 의미 확정이 먼저다.**

자원 감소 애니메이션은 화면 효과로만 사용한다. 실제 판정은 즉시 변경된 코어 값이다. 선택한 캐릭터는 정확한 수치, 나머지는 바 위주로 표시하고 다수 개체에서는 화면 갱신 빈도를 낮출 수 있다. 최대치 변경, 사망, FP 미사용도 테스트한다.

## 7. 전투 이벤트와 CSV

### 7.1 로그의 원천

전투 코어는 ActionStarted, ActionRejected, HitResolved, ResourceChanged, StatusApplied, StatusExpired, PhaseChanged, EntityDied, BattleEnded 등의 구조화 이벤트를 만든다. Console과 CSV는 이 이벤트의 별도 구독자다. Console 문자열을 다시 파싱해서 CSV를 만들지 않는다.

이벤트 CSV 권장 열:

```text
schema_version,run_id,battle_index,event_sequence,seed,tick,sim_time_sec,
event_type,actor_id,actor_definition_id,actor_name,team_id,
target_id,target_name,action_id,skill_id,damage_type,damage,
actor_hp_before,actor_hp_after,actor_fp_before,actor_fp_after,
actor_stamina_before,actor_stamina_after,target_hp_before,target_hp_after,
poise_before,poise_after,status_id,distance_m,result,reason
```

어느 개체의 자원인지 컬럼 이름으로 구별한다. 관련 없는 값은 빈칸으로 두고 실제 0과 구별한다. 시작 스냅샷과 데이터 버전을 별도 메타데이터로 기록한다. tick과 event_sequence로 같은 시점의 사건 순서를 추적한다.

Console 예시:

```text
[회차 12][tick 90 / 3.000s] 검사#A02 → 도적#B01
SWORD_LIGHT_ATK_001 | 피해 61.00 | 대상 HP 99.00 → 38.00
```

### 7.2 저장과 다운로드 UX

데스크톱 MVP의 ‘다운로드’는 **CSV 파일 저장**으로 구현한다. 기본 폴더는 `Application.persistentDataPath/Exports/<run_id>/`이고, 결과 화면에 저장 경로와 ‘폴더 열기’를 제공한다. 해당 API는 실행 사이에 유지할 데이터를 저장하는 위치를 제공한다. [Unity 공식 API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-persistentDataPath.html)

- UTF-8 BOM으로 한글 Excel 호환성을 확보한다. 쉼표·따옴표·개행이 있는 필드는 따옴표로 감싸고 내부 따옴표는 두 번 기록한다.
- 소수점은 문화권과 무관한 `.`로 저장하고 출력 정밀도를 고정한다. 이름과 ID는 별도 필드다.
- 이벤트 전체를 메모리에 누적하지 않고 버퍼를 사용해 순차 기록한다. 긴 실행은 파일을 분할할 수 있다.
- 임시 파일에 쓰고 정상 종료 시 최종 이름으로 전환한다. 취소 시 완료 결과는 보존하고 부분 로그임을 표시한다.
- 저장 실패·디스크 공간 부족은 화면에 표시한다. ‘저장 완료’는 파일 닫기/flush가 끝난 후 표시한다.
- 실험별 `events.csv`(선택), `results.csv`, `summary.csv`, `manifest.json`을 저장하는 구성을 제안한다.
- 추후 WebGL 배포를 선택하면 브라우저 다운로드 연동이 별도로 필요하다. 현재 계획은 웹 다운로드 API 구현까지 포함하지 않는다.

### 7.3 로그 성능 정책

관찰 모드는 핵심 이벤트 Console 출력, 필요 시 상세 출력으로 설정한다. 비시각 반복 모드는 Console 상세 출력을 기본 끄고 요약·오류만 출력한다. CSV는 전체/선택 회차/요약만 중 선택한다. 집계는 언제나 원본 이벤트를 소비하므로 기록 옵션이 승률을 바꾸지 않는다. 기록 on/off의 성능을 각각 측정한다.

## 8. 비시각 고속 실행

### 8.1 Unity 앱 안의 빠른 계산 모드

렌더러만 끄는 수준을 넘어서 View·Canvas·시각 효과를 생성하지 않는다. 동일 Core의 Step을 실제 시간 대기 없이 반복 호출한다. 초기 구현은 메인 스레드에서 프레임별 시간 예산만큼 계산하고 UI에 제어를 돌려주는 방식이 간단하다. 매 프레임 진행률·취소 요청을 처리하고 취소는 tick 경계에서 반영한다.

각 전투 시작 시 상태·쿨다운·효과·타격 기록·난수를 초기화한다. 회차별 seed는 masterSeed와 battleIndex를 사용하는 명시적 고정 알고리즘으로 생성한다. 프레임 시간이나 스레드 실행 순서에서 seed를 만들지 않는다.

성능이 부족하면 전투 단위 작업을 백그라운드로 옮긴다. 작업 스레드에서는 Unity API를 호출하지 않고 데이터/결과만 큐로 전달한다. 병렬화 시 각 전투가 독립 난수와 상태를 가지고 결과 파일 순서는 battleIndex 기준으로 고정한다. 처음부터 병렬화를 전제로 복잡하게 만들 필요는 없다.

### 8.2 화면 없는 실행 파일

Unity 데스크톱 플레이어는 `-batchmode -nographics`로 그래픽 장치 초기화를 생략하는 headless 실행이 가능하다. 이는 전투 로직 자체를 고속화하거나 자동 반복해 주는 기능은 아니며, 별도의 BatchSimulationRunner가 필요하다. [Unity 공식 매뉴얼](https://docs.unity3d.com/6000.0/Documentation/Manual/desktop-headless-mode.html)

```text
ARPGSimulator.exe -batchmode -nographics -logFile batch.log
  --scenario boss_test.json --runs 1000 --seed 12345 --output results
```

위에서 `--scenario`, `--runs`, `--seed`, `--output`은 **직접 구현할 사용자 정의 인자**이며 Unity 내장 옵션이 아니다. 예시는 가독성을 위해 줄을 나눈 것이다. 배치 부트스트랩은 파라미터 검증→실행→결과 파일 닫기→종료 코드를 담당한다. 성공 0, 입력 오류/실행 실패는 0이 아닌 코드로 구분한다. 무한 루프를 막는 최대 tick과 취소/오류 종료 정책은 두 모드에서 동일하다.

## 9. 구현 순서와 단계별 완료 기준

| 단계 | 구현 내용 | 다음 단계로 넘어갈 조건 |
| --- | --- | --- |
| 0. 규칙/데이터 확정 | 현재본 선택, 단위 사전, 자원 정책, 가드 계수, 필수 ID 검증 | 플레이어 1종·근접 적 1종·스킬 1개의 입력을 모호함 없이 로드 |
| 1. 최소 전투 코어 | 30Hz, 초기 상태, 접근·단일 공격·피해·사망·제한시간 | 고정 시드로 같은 결과, 저장된 피해 예제와 정해진 허용오차 내 비교 |
| 2. 도형 관찰 | 바닥·카메라·캡슐·무기 도형·선택·3종 자원 바 | 1대1 전투와 상태 변화가 눈으로 확인됨 |
| 3. 조작과 기록 | 시작/정지/1 tick/재시작·배속·Console·CSV | 배속별 결과 일치, CSV 한글/특수문자·값·이벤트 순서 확인 |
| 4. 반복 실행 | 비시각 runner·seed 목록·진행률·취소·집계 | 같은 입력에서 관찰/비시각 결과 일치, N회 종료와 요약 저장 |
| 5. 기존 규칙 확장 | 폭포 AI·무브셋·회피·가드·패리·강인도·상태이상 | 규칙별 사례 검증, 자원·쿨다운·면역 경계 테스트 통과 |
| 6. 다대다·보스 | 타겟 전환·범위 공격·콤보·보스 페이즈 | 같은 이름 여러 개체 식별, 한 타격 중복 없음, 보스 경계/연계 재현 |
| 7. 운용 개선 | 독립 headless, A/B 비교, 스킬별 집계, 성능 최적화 | 대표 시나리오의 성능과 메모리 측정, 반복 간 상태 누수 없음 |

**요청된 UI·실행 기능의 최소 버전은 단계 0~4**, 기존 기획 전투 규칙을 넓게 반영하는 버전은 단계 5~6까지다. 단계 0~4 완료를 전체 ARPG 규칙 이식 완료라고 부르지 않는다. 첫 시나리오는 원거리/버프 데이터가 필요 없는 근접 1대1을 선택하고, 이후 기존 결과에 등장하는 검사 대 다수 몬스터, 튜토리얼 보스로 확장한다.

## 10. 검증 계획

| 검증 | 입력/방법 | 기대 결과 |
| --- | --- | --- |
| 단위 변환 | Motion_Value=100000, Chance_Pct=50, Hit_Time=300ms | 배율 1.0, 확률 0.5, 9 tick |
| 시간 경계 | 타격 시점 직전/도달/직후, 속도 변경, 0ms | 정확히 한 번 실행, 빨라져도 누락 없음 |
| 자원 | HP 비용 경계, FP 부족, 스태미나 0/소량/충분, 최대값 0 | 확정한 자원 정책과 일치, 0으로 나누지 않음 |
| 피해 | 대미지메이커 기대값과 공격/감소율 변경 | 공식 단계와 반올림 위치에 따른 값 일치 |
| 방어 | 무적·패리·가드·가드 브레이크 | HP/스태미나/강인도별 예상 변화 |
| AI | 동일 조건 50% 두 행, 100% 폴백, 쿨다운, 미사용 행동 | 폭포 방식, 잘못된 가중치 추첨 없음 |
| 상태이상 | 임계값·면역·효과 만료·재피격 | 정의된 시점의 발동·지속·해제 |
| 모드 동등성 | 같은 데이터·규칙·시나리오·seed로 1배/8배/비시각 | 벽시계/화면 보간을 제외한 이벤트와 최종 상태 일치 |
| 로그 | Console·CSV 공통 EventSequence 비교 | 동일 사건을 추적 가능, 집계는 필터와 무관 |
| 파일 | 한글·쉼표·따옴표·개행·저장 실패·취소 | 정상 CSV 및 정확한 성공/실패/부분 결과 표시 |
| 다대다 | 같은 캐릭터 3명, 동시 공격, 같은 대상 재접촉 | 개체 구분, 규칙대로 동시 교환, 중복 타격 없음 |
| 반복 초기화 | 같은 시드 전투를 연속 재실행 | 이전 회차의 HP·버프·쿨다운·타격 기록이 남지 않음 |
| 성능 | 대표 1대1/다대다, 로그 on/off 각각 측정 | 초당 전투 수·tick 수·메모리·파일 크기 기록 |

기존 엔진 소스와 원래 seed가 없으므로 과거 승률 0%/적군 65승을 그대로 재현하는 테스트부터 만들지 않는다. 먼저 계산 예제와 명시 규칙을 기준으로 검증하고, 새 코어 내부에서 시각/비시각 동등성을 확보한다.

## 11. 개발 전에 결정할 규칙 목록

| 결정 항목 | 제안 기본값 또는 처리 |
| --- | --- |
| 가드 피해 계수 | 최신 전투공식 그림의 10을 기준 후보로 지정, 100 버전과 혼용 금지 |
| 스태미나 부족 | 원문처럼 잔량 > 0이면 시도 허용, 비용 차감 후 0 보정 여부 확정 |
| HP 소모 | 자기 사망 허용 여부와 HP/FP 검사·차감 순서 확정 |
| 몬스터 FP/스태미나 0 | 명시적 Disabled/Unlimited/Finite 정책으로 정의 후 데이터에 기록 |
| 행동 실패 | 스킬이 불가능하면 다음 AI 행을 평가하고 최종 대기 폴백 |
| 동일 tick 교환 | 후보 수집 후 해결하는 동시 교환 제안, 로그에 해결 순서 고정 |
| 다단히트 | 타격 시점 배열 도입. 모션 배율이 전체 합인지 타격당인지 원본 설명에 맞춰 확정 |
| 상태이상 | 누적 감소율·중독 첫 tick·상처 면역 시간·빙결 적용 범위 명시 |
| 차지/캔슬 0 | 0이 즉시 허용인지 비활성 표식인지 구별하는 필드 도입 |
| 보스 조건식 | 원본의 숫자 조건·OR 표기를 현재 AI 문법과 매핑한 뒤 사용 |

이 목록은 현재 문서 작성을 멈추게 하는 질문이 아니라 구현 착수 단계의 결정 기록이다. 미정인 값은 코드 곳곳의 임시 상수로 넣지 말고 CombatRules와 데이터 검증 보고서에 모아 둔다.
