namespace Tracto.Designer.Core.Mcp;

public sealed record McpToolResult(bool Success, string Content, string? Error = null);
