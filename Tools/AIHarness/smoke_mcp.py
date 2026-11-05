"""Verify real stdio initialization, discovery and a tool call using SDK client."""
import asyncio
import sys
from pathlib import Path
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


async def main():
    params = StdioServerParameters(command=sys.executable, args=[str(Path(__file__).with_name("mcp_server.py"))])
    async with stdio_client(params) as (reader, writer):
        async with ClientSession(reader, writer) as session:
            await session.initialize()
            tools = await session.list_tools()
            assert {"check_environment", "run_battles", "test_core"} <= {t.name for t in tools.tools}
            result = await session.call_tool("scenarios", {})
            assert not result.isError
            print("PASS: MCP initialize, tools/list, scenarios call")


if __name__ == "__main__":
    asyncio.run(main())
