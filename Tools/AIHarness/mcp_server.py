"""Local stdio MCP adapter. stdout is reserved for the MCP protocol."""
from mcp.server.fastmcp import FastMCP
from harness import doctor, validate_tables, core, list_scenarios, get_character, get_report

mcp = FastMCP("ARPG Balance Harness")


@mcp.tool()
def check_environment() -> dict:
    """Check local dependencies, data freshness, and Unity location."""
    return doctor()


@mcp.tool()
def scenarios() -> list:
    """List available scenario IDs. Use an exact ID when running battles."""
    return list_scenarios()


@mcp.tool()
def character(query: str, limit: int = 10) -> dict:
    """Search exported characters by exact ID or name; return named stats."""
    return get_character(query, limit)


@mcp.tool()
def validate_excel() -> dict:
    """Validate saved Excel values into staging; never overwrite runtime JSON."""
    return validate_tables()


@mcp.tool()
def test_core(scenario: str = "") -> dict:
    """Compile and test the actual C# core, including real-data determinism."""
    return core("test", scenario)


@mcp.tool()
def run_battles(scenario: str, runs: int = 10, seed: int = 12345) -> dict:
    """Run actual C# battles with fixed seed; writes a local result report."""
    return core("run", scenario, runs, seed)


@mcp.tool()
def execution_report(report_id: str) -> dict:
    """Read a prior harness report by ID returned by an execution tool."""
    return get_report(report_id)


if __name__ == "__main__":
    mcp.run(transport="stdio")
