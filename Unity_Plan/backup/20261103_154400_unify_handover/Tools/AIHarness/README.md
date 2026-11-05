# AI 하네스 문서 인덱스 (AI용)

- 사람용 설정·사용 안내: `../../Manual/AIHarness_MCP_사용설명서.html`
- AI 진행·장애·이어하기 기록: `STATUS.md`
- 작업 시작 시 STATUS.md를 읽고 실제 코드·최근 실행 보고서와 대조한다.
- 사람이 읽는 안내를 새로 작성하거나 수정할 때 HTML로 제공한다. 진행 기록·구현 판단·다음 작업은 Markdown으로 유지한다.
- HTML의 설치·명령 정보는 현재 구현과 맞춰 갱신한다. 현재 상태 표시를 수정할 때 STATUS.md의 근거를 확인한다.
- 검증 명령: `python Tools/AIHarness/harness.py check`
- Python 경계 검사: `python Tools/AIHarness/test_harness.py`
- MCP 통신 검사: `.venv/Scripts/python.exe smoke_mcp.py` (SDK 설치 필요)
- 사람용 HTML은 외부 의존성 없이 열리도록 유지한다.
