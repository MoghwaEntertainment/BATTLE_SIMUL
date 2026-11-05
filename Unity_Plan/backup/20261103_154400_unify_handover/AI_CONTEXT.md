# AI 공통 작업 재개 안내

## 먼저 읽기

1. AGENTS.md — 프로젝트 공통 작업 규칙
2. CHANGELOG.md — 실제 변경 이력(대화 대신 파일을 기준으로 확인)
3. 작업 영역의 현재 상태. 하네스: Tools/AIHarness/STATUS.md

## 문서 위치

- 사람용 매뉴얼 전체: Manual/index.html
- 전투 시뮬레이터: Manual/ARPG_전투시뮬레이터_사용설명서.html
- AI 하네스·MCP: Manual/AIHarness_MCP_사용설명서.html
- AI 기록: Markdown. 사람용 안내: HTML. 새로운 안내도 위 목록 화면에 연결한다.

## 현재 상태 (2026-11-03)

- Unity ARPG 시뮬레이터, Table_ARPG Excel → JSON → 순수 C# 전투 코어.
- 기절 누적 연결 작업과 하네스·MCP 코드 작업이 커밋되지 않은 상태. 실제 diff를 확인한다.
- 하네스: 실제 C# 테스트 20개·Python 경계 검사 5개 통과. 대표 시나리오 10회 및 동일 시드 재현 확인.
- 미완료: MCP SDK 설치·통신 및 클라이언트 연결. Unity 자체 검사는 라이선스 초기화/BIOS 접근 오류와 시간 초과로 미검증. PNG 구조도는 이전 버전.
- 사용자 변경이 있던 3.PC_스탯.xlsm은 보존한다. 임의로 되돌리지 않는다.
- 상세 보고서 ID·재시도 명령: Tools/AIHarness/STATUS.md.

## 작업을 마칠 때

CHANGELOG.md에 목적·변경 파일·실제 검증·실패/미검증·다음 작업을 추가한다.
현재 상태가 바뀌면 이 문서와 관련 상세 상태 기록도 갱신한다.
다른 AI가 직전 대화를 보지 못해도 이어갈 수 있도록 쓴다.
