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
    public const int MaxEntries = 512;
    public const int MaxFileChars = 524_288;
    public const int MaxFileBytes = MaxFileChars * 4;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    private sealed record Dto(int Version, Dictionary<string, string> Trusted);

    private ScriptTrustLedger(string path) => _path = path;

    public static ScriptValueResult<ScriptTrustLedger> Open() => Open(AppPaths.ScriptTrustPath);

    internal static ScriptValueResult<ScriptTrustLedger> Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Fail(path ?? "", "path", "The trust ledger path must not be blank.");

        try
        {
            path = Path.GetFullPath(path);
            using var held = Hold(path);
            var loaded = Read(path);
            return loaded.Success
                ? ScriptValueResult<ScriptTrustLedger>.Ok(new(path))
                : new(false, new(path), loaded.Marker, loaded.Error);
        }
        catch (Exception ex)
        {
            return Fail(path, "read-failed", ex.Message);
        }
    }

    public bool IsTrusted(ScriptDefinition? definition)
    {
        if (!ScriptDefinitionFile.Validate(definition).Success) return false;
        try
        {
            using var held = Hold(_path);
            var loaded = Read(_path);
            if (!loaded.Success || !loaded.Value!.TryGetValue(definition!.Id, out string? hash)) return false;
            return string.Equals(hash, ComputeFingerprint(definition), StringComparison.Ordinal);
        }
        catch { return false; }
    }

    public ScriptOperationResult Trust(ScriptDefinition definition)
    {
        var validation = ScriptDefinitionFile.Validate(definition);
        if (!validation.Success) return validation;
        return Mutate(next => next[definition.Id] = ComputeFingerprint(definition));
    }

    public ScriptOperationResult Revoke(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ScriptOperationResult.Fail("trust ", "id", "The id must not be blank.");
        return Mutate(next => next.Remove(id));
    }

    public static string ComputeFingerprint(ScriptDefinition definition)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(definition.Language + "\n" + definition.Expression);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private ScriptOperationResult Mutate(Action<Dictionary<string, string>> change)
    {
        try
        {
            using var held = Hold(_path);
            var loaded = Read(_path);
            if (!loaded.Success) return new(false, loaded.Marker, loaded.Error);

            var next = loaded.Value!;
            change(next);
            if (next.Count > MaxEntries)
                return ScriptOperationResult.Fail("trust ", "entry-limit", $"The trust ledger exceeds {MaxEntries} entries.");

            string json = JsonSerializer.Serialize(new Dto(1, next), WriteOptions);
            if (json.Length > MaxFileChars || Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
                return ScriptOperationResult.Fail("trust ", "file-limit", "The trust ledger exceeds its storage limit.");
            ScriptFile.WriteAtomic(_path, json);
            return ScriptOperationResult.Ok();
        }
        catch (Exception ex)
        {
            return ScriptOperationResult.Fail("trust ", "write-failed", ex.Message);
        }
    }

    private static ScriptValueResult<Dictionary<string, string>> Read(string path)
    {
        if (!File.Exists(path))
            return ScriptValueResult<Dictionary<string, string>>.Ok(new(StringComparer.Ordinal));

        try
        {
            string json = ScriptFile.ReadBounded(path, MaxFileBytes, MaxFileChars);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Invalid("The trust ledger must be a JSON object.");

            var top = document.RootElement.EnumerateObject().ToList();
            if (top.Count != 2 || top.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
                top.Any(p => p.Name is not ("version" or "trusted")))
                return Invalid("The trust ledger contains missing, duplicate, or unknown fields.");
            if (!document.RootElement.TryGetProperty("version", out var version) ||
                !version.TryGetInt32(out int value) || value != 1 ||
                !document.RootElement.TryGetProperty("trusted", out var trusted) ||
                trusted.ValueKind != JsonValueKind.Object)
                return Invalid("The trust ledger schema is invalid.");

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in trusted.EnumerateObject())
            {
                if (result.Count >= MaxEntries || !ValidId(entry.Name) ||
                    entry.Value.ValueKind != JsonValueKind.String || !ValidHash(entry.Value.GetString()) ||
                    !result.TryAdd(entry.Name, entry.Value.GetString()!))
                    return Invalid("The trust ledger contains invalid or duplicate entries.");
            }
            return ScriptValueResult<Dictionary<string, string>>.Ok(result);
        }
        catch (InvalidDataException ex) when (ex.Message == "file-limit")
        {
            return ScriptValueResult<Dictionary<string, string>>.Fail("trust ", "file-limit",
                "The trust ledger exceeds its storage limit.");
        }
        catch (JsonException ex)
        {
            return Invalid(ex.Message);
        }
        catch (Exception ex)
        {
            return ScriptValueResult<Dictionary<string, string>>.Fail("trust ", "read-failed", ex.Message);
        }
    }

    private static IDisposable Hold(string path)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        var mutex = new Mutex(false, "Local\\QuickReplyKit.ScriptTrust." + key);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
        return new HeldMutex(mutex);
    }

    private sealed class HeldMutex(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            try { mutex.ReleaseMutex(); }
            finally { mutex.Dispose(); }
        }
    }

    private static bool ValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= ScriptDefinitionFile.MaxIdChars && !id.Any(char.IsControl);

    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static ScriptValueResult<Dictionary<string, string>> Invalid(string error) =>
        ScriptValueResult<Dictionary<string, string>>.Fail("trust ", "invalid-ledger", error);

    private static ScriptValueResult<ScriptTrustLedger> Fail(string path, string code, string error) =>
        new(false, new ScriptTrustLedger(path), $"[script trust error: {code}]", error);
}
