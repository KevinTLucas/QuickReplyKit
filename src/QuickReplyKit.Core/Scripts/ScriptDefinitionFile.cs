using System.IO;
using System.Text.Json;

namespace QuickReplyKit.Core.Scripts;

/// <summary>Strict, nonthrowing storage for the portable <c>.qrkscript.json</c> schema.</summary>
public static class ScriptDefinitionFile
{
    public const string Extension = ".qrkscript.json";
    public const string Language = "qrk-expr-1";
    public const int MaxIdChars = 128;
    public const int MaxNameChars = 128;
    public const int MaxFileChars = 16_384;

    private static readonly HashSet<string> Fields =
        ["id", "name", "language", "enabled", "expression"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record Dto(string Id, string Name, string Language, bool Enabled, string Expression);

    public static ScriptValueResult<ScriptDefinition> Parse(string? json)
    {
        if (json is null)
            return Fail("invalid-json", "The definition is null.");
        if (json.Length > MaxFileChars)
            return Fail("file-limit", $"The definition exceeds {MaxFileChars} characters.");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Fail("schema", "The definition must be a JSON object.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!Fields.Contains(property.Name))
                    return Fail("schema", $"Unknown field '{property.Name}'.");
                if (!seen.Add(property.Name))
                    return Fail("schema", $"Duplicate field '{property.Name}'.");
            }

            if (!Fields.SetEquals(seen))
                return Fail("schema", "The definition is missing one or more required fields.");

            var root = document.RootElement;
            if (!TryString(root, "id", out var id) || !TryString(root, "name", out var name) ||
                !TryString(root, "language", out var language) ||
                !TryString(root, "expression", out var expression) ||
                !root.TryGetProperty("enabled", out var enabledElement) ||
                enabledElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Fail("schema", "Definition fields have invalid types.");

            var definition = new ScriptDefinition(id!, name!, language!, enabledElement.GetBoolean(), expression!);
            var validation = Validate(definition);
            return validation.Success
                ? ScriptValueResult<ScriptDefinition>.Ok(definition)
                : new(false, null, validation.Marker, validation.Error);
        }
        catch (JsonException ex)
        {
            return Fail("invalid-json", ex.Message);
        }
        catch (Exception ex)
        {
            return Fail("read-failed", ex.Message);
        }
    }

    public static ScriptValueResult<ScriptDefinition> Load(string path)
    {
        if (!HasExpectedExtension(path))
            return Fail("extension", $"Definitions must use the {Extension} extension.");
        try { return Parse(File.ReadAllText(path)); }
        catch (Exception ex) { return Fail("read-failed", ex.Message); }
    }

    public static ScriptOperationResult Save(string path, ScriptDefinition definition)
    {
        if (!HasExpectedExtension(path))
            return ScriptOperationResult.Fail("definition ", "extension", $"Definitions must use the {Extension} extension.");

        var validation = Validate(definition);
        if (!validation.Success) return validation;

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var dto = new Dto(definition.Id, definition.Name, definition.Language,
                definition.Enabled, definition.Expression);
            File.WriteAllText(path, JsonSerializer.Serialize(dto, WriteOptions));
            return ScriptOperationResult.Ok();
        }
        catch (Exception ex)
        {
            return ScriptOperationResult.Fail("definition ", "write-failed", ex.Message);
        }
    }

    public static ScriptOperationResult Validate(ScriptDefinition? definition)
    {
        if (definition is null)
            return ScriptOperationResult.Fail("definition ", "schema", "The definition is null.");
        if (!ValidLabel(definition.Id, MaxIdChars))
            return ScriptOperationResult.Fail("definition ", "id", "The id must be nonblank, bounded, and contain no control characters.");
        if (!ValidLabel(definition.Name, MaxNameChars))
            return ScriptOperationResult.Fail("definition ", "name", "The name must be nonblank, bounded, and contain no control characters.");
        if (!string.Equals(definition.Language, Language, StringComparison.Ordinal))
            return ScriptOperationResult.Fail("definition ", "language", $"The language must be '{Language}'.");

        var expression = ScriptExpression.Validate(definition.Expression);
        return expression.Success
            ? ScriptOperationResult.Ok()
            : new(false, expression.Marker, expression.Error);
    }

    private static bool TryString(JsonElement root, string name, out string? value)
    {
        value = null;
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String &&
               (value = element.GetString()) is not null;
    }

    private static bool ValidLabel(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);

    private static bool HasExpectedExtension(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    private static ScriptValueResult<ScriptDefinition> Fail(string code, string error) =>
        ScriptValueResult<ScriptDefinition>.Fail("definition ", code, error);
}
