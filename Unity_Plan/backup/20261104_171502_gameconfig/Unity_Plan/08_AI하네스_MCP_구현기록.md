# AI 하네스·MCP 구현 기록 및 상세 인수인계

기준일: 2026-11-03. 이 기록은 Unity_Plan에 통합했다. 00_문서안내.md → 07_현재구현_인수인계.md → 이 파일 → 최근 artifacts 보고서 순서로 확인한다.

## 완료·검증

- [x] 설정·실행·MCP 연결 안내: Manual/AIHarness_MCP_사용설명서.html
- [x] 환경 점검, 원본 Excel/manifest 해시 대조
- [x] 기존 변환기를 staging에서 검증(실제 JSON·엑셀 변경 없음)
- [x] .NET 10 CoreRunner: 실제 Unity Core 소스 직접 컴파일
- [x] 합성·실제 데이터 테스트 20개 통과
- [x] Python 경계 검사 5개 통과
- [x] 대표 시나리오 `guardian vs all`, seed 12345, 10회 완료. 같은 조건으로 다시 실행해 회차별 시드·승패·종료 tick이 전부 일치함
- [x] 통합 check 통과: 오류 0, 기존 비활성 11, 경고 39
- [x] 실행 보고서·입력 해시·시나리오 해시·시드 저장
- [x] 루트 AGENTS.md에 시작/검증/이어하기 절차 추가
- [x] 공식 SDK v1 기반 MCP 서버·클라이언트 smoke 검사 코드·설정 예시 작성
- [x] Unity 비대화식 자체 검사 진입점 HarnessBatch.Verify 작성

## 미완료·장애

- [ ] MCP SDK 설치 및 stdio 통신 smoke 검사
  - 현재 기본 Python에는 mcp가 없다.
  - `.venv` 생성의 ensurepip가 실패했고 pip가 없는 venv만 생성되었다.
  - 기본 Python의 pip --python 우회 설치도 `No matching distribution found for mcp<2,>=1.12`로 실패했다.
  - PyPI URL 명시·pip 설정 제외 시도도 동일했다. 정확한 원인은 확인되지 않았다. 패키지가 존재하지 않는다고 단정하지 않는다.
  - 실제 앱에 MCP가 연결된 상태가 아니다. 서버 코드는 문법 검사만 완료했고 SDK 실행 검증은 남았다.
- [ ] Unity Editor 실제 컴파일·자체 검사
  - Editor 실행은 시도했으나 180초 내 테스트 보고서가 생성되지 않아 실패했다.
  - 로그에 `Licensing is not yet initialized`와 BIOS 정보 조회의 `System.UnauthorizedAccessException`이 기록되었다.
  - 이 때문에 프로젝트 코드 컴파일/Unity 테스트 성공 여부를 확인하지 못했다. 라이선스 문제의 최종 원인은 미확인이다.
  - Unity Hub에서 해당 Editor의 정상 실행·라이선스 상태를 확인한 뒤 프로젝트를 닫고 unity-test를 재실행한다.
- [x] PNG 구조도 갱신: 2026-11-03 16시 문서 정합성 정리 때 SVG 와 같은 기준으로 재생성(09 참고). 당시 이 하네스 작업 환경에는 렌더러가 없었음.
- [ ] AI 클라이언트 설정 등록·도구 호출 확인: 로컬 stdio 지원 여부에 따라 등록 필요.

## 검증 근거

보고서는 버전 관리에서 제외한 Tools/AIHarness/artifacts 아래에 있다.

- `20261103T052826Z_check_b563b2a8.json`: 통합 검증 통과
- `20261103T052828Z_run_4bc7fca4.json`: 10회 전투 완료
- `20261103T053039Z_run_51a1b205.json`: 동일 10회 결과 재현
- `20261103T053125Z_unity_a9b97acb.json`: Unity 실행 실패 및 로그 위치
- 전투 결과는 `guardian vs all` 한 시나리오의 실행 확인용이며 일반적인 밸런스 결론이 아니다.
- dataHash `77e7b4b0c284ca44`, rules `2026.11.03-r4`.

## 다음 작업 명령

```powershell
python Tools/AIHarness/harness.py doctor
python Tools/AIHarness/test_harness.py
python Tools/AIHarness/harness.py check
```

그다음 MCP 설치·연결을 우선 완료한다.

```powershell
python -m pip --python Tools/AIHarness/.venv/Scripts/python.exe install -r Tools/AIHarness/requirements.txt
Tools/AIHarness/.venv/Scripts/python.exe Tools/AIHarness/smoke_mcp.py
```

클라이언트가 로컬 stdio MCP를 지원하면 mcp.config.example.json 또는 mcp.codex.example.toml을 참고해 등록한다.
현재 세션에 도구가 보이지 않으면 등록 완료라고 보고하지 않는다.
그다음 단계는 기준 실행과 수정 후 실행을 비교하는 기능, 이벤트 추적 조회 기능이다.

## 변경 보존

작업 시작 시 `3.PC_스탯.xlsm`에 사용자 변경이 있었다. 수정하지 않았다.
이전 기절 수정·문서 변경이 커밋되지 않은 상태였고 그대로 보존했다.
이번 작업에서 전투 코어 규칙·런타임 데이터는 변경하지 않았다.
구조 문서/도식 백업: Unity_Plan/backup/20261103_142915_ai_harness/
커밋·푸시하지 않았다.

## 2026-11-03 문서 형식 변경

- 사람용 설정·사용 안내를 `Manual/AIHarness_MCP_사용설명서.html`로 옮김. 목차·명령 복사·반응형 표·인쇄 스타일 적용.
- README.md는 AI용 문서 인덱스로 축소, STATUS.md는 진행 기록으로 유지.
- AGENTS.md에 사람용 HTML / AI 기록용 MD 원칙 추가.
- 전투 코드·데이터 변경 없음.
- 문서 변경 후 확인: HTML 구조·목차 링크·외부 의존성 없음 확인, check=True (`20261103T060610Z_check_701a7640`).

## 2026-11-03 15:08 KST 공통 기록 연결

- 매뉴얼 통합 시작 화면: `Manual/index.html`.
- AI 공통 시작점: `AI_CONTEXT.md`, 변경 이력: `CHANGELOG.md`.
- 제공업체에 관계없이 위 공통 기록을 확인·갱신하도록 AGENTS.md에 명시.

## 2026-11-03 15:44 KST 기준 위치 통합

이 파일이 하네스 상세 기록의 기준본이다. 이전 Tools/AIHarness/STATUS.md는 안내만 남긴다.
문서 내 과거 경로는 당시 기록이며 현재 위치는 00_문서안내.md를 따른다.
