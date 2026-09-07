using System.IO;
using System.IO.Compression;
using System.Text;
using QuickReplyKit.Core.ImportExport;

namespace QuickReplyKit.Tests;

public class ImportPlanTests
{
    private const string Valid =
        "{\"schema\":1,\"app\":\"\",\"tags\":[],\"html\":\"<p>x</p>\",\"favorite\":false}";

    [Fact]
    public void Plan_ClassifiesMixedZip_AndExecutionMatchesPerItemChoices()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string source = t.Of("mixed.zip");
        Write(lib, "Same.json", Valid);
        Write(lib, "Changed.json", Valid.Replace("x", "old"));
        Write(lib, "Elsewhere.json", Valid.Replace("x", "duplicate"));
        MakeZip(source,
            ("Same.json", Valid),
            ("Changed.json", Valid.Replace("x", "new")),
            ("Duplicate.json", Valid.Replace("x", "duplicate")),
            ("New.json", Valid.Replace("x", "brand new")),
            ("Broken.json", "not json"));

        var plan = LibraryPorter.PlanImport(lib, source, "");

        Assert.Equal(new[]
        {
            ImportClassification.Identical, ImportClassification.Conflict,
            ImportClassification.DuplicateElsewhere, ImportClassification.New, ImportClassification.Invalid,
        }, plan.Entries.Select(e => e.Classification));
        var decisions = plan.Entries.ToDictionary(e => e.Id, e => e.Classification switch
        {
            ImportClassification.Conflict => ImportAction.KeepBoth,
            ImportClassification.DuplicateElsewhere or ImportClassification.New => ImportAction.Import,
            _ => ImportAction.Skip,
        });

        var result = LibraryPorter.Import(plan, decisions);

        Assert.Equal(new[]
        {
            ImportOutcomeKind.Skipped, ImportOutcomeKind.KeptBoth, ImportOutcomeKind.Imported,
            ImportOutcomeKind.Imported, ImportOutcomeKind.Invalid,
        }, result.Outcomes.Select(o => o.Kind));
        Assert.Equal("Changed (imported).json", result.Outcomes[1].DestinationPath);
        Assert.True(File.Exists(Path.Combine(lib, "Changed (imported).json")));
        Assert.True(File.Exists(Path.Combine(lib, "Duplicate.json")));
        Assert.True(File.Exists(Path.Combine(lib, "New.json")));
        Assert.False(File.Exists(Path.Combine(lib, "Broken.json")));
    }

    [Theory]
    [InlineData(ConflictPolicy.Skip)]
    [InlineData(ConflictPolicy.Overwrite)]
    [InlineData(ConflictPolicy.KeepBoth)]
    public void Identical_IsAlwaysNoOp(ConflictPolicy policy)
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string source = t.Of("Same.json");
        Write(lib, "Same.json", Valid);
        File.WriteAllText(source, Valid);
        DateTime stamp = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Local);
        File.SetLastWriteTime(Path.Combine(lib, "Same.json"), stamp);

        var result = LibraryPorter.Import(lib, source, "", policy);

        Assert.Equal(0, result.Imported);
        Assert.Equal(ImportOutcomeKind.Skipped, Assert.Single(result.Outcomes).Kind);
        Assert.Equal(stamp, File.GetLastWriteTime(Path.Combine(lib, "Same.json")));
        Assert.Single(Directory.GetFiles(lib));
    }

    [Fact]
    public void UnsafeAndInvalidEntries_DoNotBlockValidSibling_AndSidecarIsIgnored()
    {
        using var t = new TempDir();
        string source = t.Of("mixed.zip");
        MakeZip(source,
            ("../../outside.json", Valid),
            ("CON.json", Valid),
            ("Broken.json", "{"),
            (".qrkfolder.json", "{}"),
            ("Good.json", Valid));

        var plan = LibraryPorter.PlanImport(t.Of("lib"), source, "Target");

        Assert.Equal(4, plan.Entries.Count);
        Assert.Equal(2, plan.Entries.Count(e => e.Classification == ImportClassification.Unsafe));
        Assert.Single(plan.Entries, e => e.Classification == ImportClassification.Invalid);
        Assert.Single(plan.Entries, e => e.Classification == ImportClassification.New);
        var result = LibraryPorter.Import(plan);
        Assert.Equal(1, result.Imported);
        Assert.True(File.Exists(t.Of("lib", "Target", "Good.json")));
        Assert.False(File.Exists(t.Of("outside.json")));
    }

    [Theory]
    [InlineData("../bad")]
    [InlineData("C:/bad")]
    [InlineData("bad:name")]
    [InlineData("bad. ")]
    public void UnsafeTargetCategory_IsRejectedBeforeAnyDestinationRead(string target)
    {
        using var t = new TempDir();
        string source = t.Of("Good.json");
        File.WriteAllText(source, Valid);

        var entry = Assert.Single(LibraryPorter.PlanImport(t.Of("lib"), source, target).Entries);

        Assert.Equal(ImportClassification.Unsafe, entry.Classification);
    }

    [Fact]
    public void DuplicateIncomingDestination_IsDeterministicallyUnsafe()
    {
        using var t = new TempDir();
        string source = t.Of("dupe.zip");
        MakeZip(source, ("A.json", Valid), ("a.JSON", Valid.Replace("x", "other")));

        var plan = LibraryPorter.PlanImport(t.Of("lib"), source, "");

        Assert.Equal(ImportClassification.New, plan.Entries[0].Classification);
        Assert.Equal(ImportClassification.Unsafe, plan.Entries[1].Classification);
    }

    [Fact]
    public void SourceChangeAfterPreview_FailsClosed()
    {
        using var t = new TempDir();
        string source = t.Of("Good.json");
        File.WriteAllText(source, Valid);
        var plan = LibraryPorter.PlanImport(t.Of("lib"), source, "");
        File.WriteAllText(source, Valid.Replace("x", "changed later"));

        var outcome = Assert.Single(LibraryPorter.Import(plan).Outcomes);

        Assert.Equal(ImportOutcomeKind.Stale, outcome.Kind);
        Assert.False(File.Exists(t.Of("lib", "Good.json")));
    }

    [Fact]
    public void DestinationChangeAfterPreview_SkipsOnlyStaleRow()
    {
        using var t = new TempDir();
        string source = t.Of("two.zip");
        MakeZip(source, ("A.json", Valid), ("B.json", Valid.Replace("x", "b")));
        string lib = t.Of("lib");
        var plan = LibraryPorter.PlanImport(lib, source, "");
        Write(lib, "A.json", Valid.Replace("x", "someone else"));

        var result = LibraryPorter.Import(plan);

        Assert.Equal(ImportOutcomeKind.Stale, result.Outcomes[0].Kind);
        Assert.Equal(ImportOutcomeKind.Imported, result.Outcomes[1].Kind);
        Assert.True(File.Exists(Path.Combine(lib, "B.json")));
    }

    [Fact]
    public void OversizedEntry_IsInvalidWithoutBlockingSibling()
    {
        using var t = new TempDir();
        string source = t.Of("large.zip");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "Large.json", new string('x', checked((int)LibraryPorter.MaxEntryBytes + 1)));
            WriteEntry(zip, "Good.json", Valid);
        }

        var plan = LibraryPorter.PlanImport(t.Of("lib"), source, "");

        Assert.Equal(ImportClassification.Invalid, plan.Entries[0].Classification);
        Assert.Equal(ImportClassification.New, plan.Entries[1].Classification);
    }

    [Theory]
    [InlineData("NUL.txt.json")]
    [InlineData("LPT1.foo.json")]
    [InlineData("COM9.response.json")]
    public void ExtendedWindowsDeviceNames_AreUnsafe(string name)
    {
        using var t = new TempDir();
        string source = t.Of("reserved.zip");
        MakeZip(source, (name, Valid));

        var entry = Assert.Single(LibraryPorter.PlanImport(t.Of("lib"), source, "").Entries);

        Assert.Equal(ImportClassification.Unsafe, entry.Classification);
    }

    [Fact]
    public void AggregateDeclaredSize_IsRejectedAsOneArchiveBeforeEntryReads()
    {
        using var t = new TempDir();
        string source = t.Of("aggregate.zip");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
            for (int i = 0; i < 11; i++)
                WriteEntry(zip, $"Large{i}.json", new string('x', checked((int)LibraryPorter.MaxEntryBytes)));

        var entry = Assert.Single(LibraryPorter.PlanImport(t.Of("lib"), source, "").Entries);

        Assert.Equal(ImportClassification.Invalid, entry.Classification);
        Assert.Contains("100 MB", entry.Message);
    }

    [Fact]
    public void LibraryRootReplacedByLinkAfterPreview_FailsClosed()
    {
        using var t = new TempDir();
        string source = t.Of("Good.json");
        string root = t.Of("lib");
        string outside = t.Of("outside");
        File.WriteAllText(source, Valid);
        Directory.CreateDirectory(outside);
        var plan = LibraryPorter.PlanImport(root, source, "");
        try { Directory.CreateSymbolicLink(root, outside); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }
        catch (PlatformNotSupportedException) { return; }

        var outcome = Assert.Single(LibraryPorter.Import(plan).Outcomes);

        Assert.Equal(ImportOutcomeKind.Stale, outcome.Kind);
        Assert.False(File.Exists(Path.Combine(outside, "Good.json")));
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void MakeZip(string path, params (string Name, string Content)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries) WriteEntry(zip, entry.Name, entry.Content);
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
