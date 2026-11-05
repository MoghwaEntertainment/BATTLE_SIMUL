# Project instructions

- 확인되지 않은 추측을 확정된 규칙처럼 말하지 않는다. 원본·코드·실행 결과로 확인한다.
- 모든 AI는 작업 시작 시 `AI_CONTEXT.md`, `CHANGELOG.md`의 최근 항목, 작업 영역의 상태 기록을 읽는다. 하네스 작업은 `Tools/AIHarness/STATUS.md`와 `Tools/AIHarness/README.md`도 읽는다.
- 현재 전투 본체는 `Unity_Project/BalanceMaker/Assets/ARPG/Core`, 원본 데이터는 `Table_ARPG`다.
- 확정 규칙·원본 기획·구현을 위한 해석·임시값을 구별한다. 판단 근거의 파일/셀을 기록한다.
- 변경 후 `python Tools/AIHarness/harness.py check`를 실행한다. Unity 검증은 `unity-test`로 별도 수행하고 실행하지 못했으면 명시한다.
- 검증용 변환은 staging만 사용한다. Excel 수식은 openpyxl이 재계산하지 않으므로 저장된 캐시를 사용한다.
- 기존 사용자 변경을 덮어쓰지 않는다. 작업 후 STATUS.md에 완료/실패/미검증/다음 명령을 기록한다.
- 코드 구조를 바꾸면 `Unity_Plan/06_구조_도식.md`와 도식 원본을 갱신한다. 갱신 전 문서와 diagrams 폴더를 `Unity_Plan/backup/날짜_설명`으로 백업한다.

- 문서 형식: 사람이 읽는 매뉴얼·설정 안내·설명 문서는 `Manual/`의 HTML로 작성한다. AI 작업 지침·진행 기록·이어하기 정보는 Markdown으로 작성한다.

## 공통 변경 기록 (AI 종류와 관계없이 적용)

- 작업 완료·중단 전에 루트 `CHANGELOG.md`에 한국 시각, 요청/목적, 실제 변경 파일, 검증 결과, 미완료·장애, 다음 작업을 추가한다. 코드·데이터·문서·설정 변경을 모두 포함한다.
- 기존 기록은 지우거나 덮어쓰지 않는다. 잘못된 기록은 새 정정 항목으로 남긴다. 실행하지 않은 검사와 미검증 상태를 구분한다.
- 현재 상태와 다음 작업은 `AI_CONTEXT.md`에 갱신한다. 상세 작업 상태가 있으면 해당 파일 경로를 연결한다.
- 이전 대화의 작업을 소급 기록할 때는 소급 기록이라고 표시하고, 확인된 파일·보고서만 근거로 쓴다.
- 사람용 문서는 프로젝트의 `Manual/`에 모으고 `Manual/index.html`에서 찾을 수 있도록 링크를 추가한다. 다른 위치에 사본을 늘리지 않는다.
- 사람용 HTML / AI용 MD 규칙은 AI 제공업체와 관계없이 유지한다.
