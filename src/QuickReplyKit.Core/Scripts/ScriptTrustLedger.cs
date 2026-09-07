using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuickReplyKit.Core.Scripts;

/// <summary>
/// Local-only acceptance ledger. A script is trusted only when its id maps to SHA256 of
/// <c>language + newline + expression</c>; changing executable text or language invalidates trust.
/// </summary>
public sealed class ScriptTrustLedger
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private Dictionary<string, string> _trusted;

    private sealed class Dto
    {
        public int Version { get; set; }
        public Dictionary<string, string>? Trusted { get; set; }
    }

    private ScriptTrustLedger(string path, Dictionary<string, string>? trusted = null)
    {
        _path = path;
        _trusted = trusted ?? new(StringComparer.Ordinal);
    }

    public static ScriptValueResult<ScriptTrustLedger> Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Fail(path ?? "", "path", "The trust ledger path must not be blank.");

        try
        {
            if (!File.Exists(path)) return ScriptValueResult<ScriptTrustLedger>.Ok(new(path));

            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Options);
            if (dto?.Version != 1 || dto.Trusted is null ||
                dto.Trusted.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || !ValidHash(pair.Value)))
                return Fail(path, "invalid-ledger", "The trust ledger is malformed.");

            return ScriptValueResult<ScriptTrustLedger>.Ok(
                new(path, new Dictionary<string, string>(dto.Trusted, StringComparer.Ordinal)));
        }
        catch (Exception ex)
        {
            return Fail(path, "read-failed", ex.Message);
        }
    }

    public bool IsTrusted(ScriptDefinition? definition)
    {
        if (!ScriptDefinitionFile.Validate(definition).Success ||
            !_trusted.TryGetValue(definition!.Id, out var hash)) return false;

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(hash), Convert.FromHexString(ComputeFingerprint(definition)));
        }
        catch { return false; }
    }

    public ScriptOperationResult Trust(ScriptDefinition definition)
    {
        var validation = ScriptDefinitionFile.Validate(definition);
        if (!validation.Success) return validation;

        var next = new Dictionary<string, string>(_trusted, StringComparer.Ordinal)
        {
            [definition.Id] = ComputeFingerprint(definition),
        };
        return Persist(next);
    }

    public ScriptOperationResult Revoke(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ScriptOperationResult.Fail("trust ", "id", "The id must not be blank.");

        var next = new Dictionary<string, string>(_trusted, StringComparer.Ordinal);
        next.Remove(id);
        return Persist(next);
    }

    public static string ComputeFingerprint(ScriptDefinition definition)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(definition.Language + "\n" + definition.Expression);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private ScriptOperationResult Persist(Dictionary<string, string> next)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(new Dto { Version = 1, Trusted = next }, Options));
            _trusted = next;
            return ScriptOperationResult.Ok();
        }
        catch (Exception ex)
        {
            return ScriptOperationResult.Fail("trust ", "write-failed", ex.Message);
        }
    }

    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static ScriptValueResult<ScriptTrustLedger> Fail(string path, string code, string error) =>
        new(false, new ScriptTrustLedger(path), $"[script trust error: {code}]", error);
}
