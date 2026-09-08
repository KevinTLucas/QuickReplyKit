namespace QuickReplyKit.Core.Scripts;

/// <summary>
/// Portable script definition stored as <c>*.qrkscript.json</c> with exactly the fields
/// <c>id</c>, <c>name</c>, <c>language</c>, <c>enabled</c>, and <c>expression</c>.
/// Trust is intentionally absent because it is local state and must never travel with a definition.
/// </summary>
public sealed record ScriptDefinition(
    string Id,
    string Name,
    string Language,
    bool Enabled,
    string Expression);

public sealed record ScriptOperationResult(bool Success, string Marker, string? Error)
{
    internal static ScriptOperationResult Ok() => new(true, "", null);

    internal static ScriptOperationResult Fail(string area, string code, string error) =>
        new(false, $"[script {area}error: {code}]", error);
}

public sealed record ScriptValueResult<T>(bool Success, T? Value, string Marker, string? Error)
{
    internal static ScriptValueResult<T> Ok(T value) => new(true, value, "", null);

    internal static ScriptValueResult<T> Fail(string area, string code, string error) =>
        new(false, default, $"[script {area}error: {code}]", error);
}

public sealed record ScriptEvaluationResult(bool Success, string Output, string? Error)
{
    internal static ScriptEvaluationResult Ok(string output) => new(true, output, null);

    internal static ScriptEvaluationResult Fail(string code, string error) =>
        new(false, $"[script error: {code}]", error);
}
