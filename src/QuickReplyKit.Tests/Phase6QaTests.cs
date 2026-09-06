using System.IO;
using System.Threading;
using Microsoft.Win32;
using QuickReplyKit.Core;
using QuickReplyKit.Services;

namespace QuickReplyKit.Tests;

/// <summary>
/// Phase-6 QA: independent tests probing the seams the implementation added — the startup
/// registry toggle, <see cref="LibraryService.Repoint"/>'s watcher re-attach, and repoint
/// robustness. Existing suites already cover HotkeyGesture.Format round-trips, WindowPlacement
/// geometry, and AppConfig persistence; these fill the remaining gaps.
/// </summary>
public class StartupRegistrationTests : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickReplyKit";

    // StartupRegistration can only write the real HKCU "QuickReplyKit" Run value (no test-only
    // name), so snapshot whatever is there and restore it exactly when the test finishes.
    private readonly object? _saved;
    private readonly RegistryValueKind _savedKind;

    public StartupRegistrationTests()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        _saved = key!.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        _savedKind = _saved is null ? RegistryValueKind.Unknown : key.GetValueKind(ValueName);
    }

    public void Dispose()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (_saved is null) key!.DeleteValue(ValueName, throwOnMissingValue: false);
        else key!.SetValue(ValueName, _saved, _savedKind);
    }

    [Fact]
    public void SetEnabledTrue_WritesExePath_And_IsEnabledReflectsIt()
    {
        StartupRegistration.SetEnabled(true);

        Assert.True(StartupRegistration.IsEnabled());
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        Assert.Equal($"\"{Environment.ProcessPath}\"", key!.GetValue(ValueName) as string);
    }

    [Fact]
    public void SetEnabledFalse_RemovesValue_And_IsEnabledIsFalse()
    {
        StartupRegistration.SetEnabled(true);
        StartupRegistration.SetEnabled(false);

        Assert.False(StartupRegistration.IsEnabled());
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        Assert.Null(key?.GetValue(ValueName));
    }

    [Fact]
    public void SetEnabled_IsIdempotent_DoubleSet_And_DoubleClear_DoNotThrow()
    {
        StartupRegistration.SetEnabled(true);
        StartupRegistration.SetEnabled(true);   // second enable: overwrite, no throw
        Assert.True(StartupRegistration.IsEnabled());

        StartupRegistration.SetEnabled(false);
        StartupRegistration.SetEnabled(false);  // clearing a missing value must not throw
        Assert.False(StartupRegistration.IsEnabled());
    }

    [Fact]
    public void IsEnabled_IsFalse_WhenValueAbsent()
    {
        StartupRegistration.SetEnabled(false);  // ensure absent
        Assert.False(StartupRegistration.IsEnabled());
    }
}

/// <summary>
/// Repoint gaps not covered by <see cref="LibraryServiceTests"/>: the live watcher must follow the
/// new root (and go quiet on the old one), and repointing to a missing/empty folder must be safe.
/// </summary>
public class RepointWatcherTests
{
    // The watcher fires on a thread-pool thread; give it generous time and keep assertions robust.
    private const int WaitMs = 5000;

    [Fact]
    public void Repoint_WatcherFollowsNewRoot_AndIgnoresOldRoot()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Of("libA"));
        Directory.CreateDirectory(dir.Of("libB"));
        TestFiles.WriteResponse(dir.Of("libA", "A.json"));

        using var svc = new LibraryService(dir.Of("libA"), seedSourceDir: null,
            recentPath: dir.Of("recent.json"), watch: true);

        svc.Repoint(dir.Of("libB"));

        // A change in the NEW root wakes the watcher and rebuilds the snapshot.
        var firedForNew = new ManualResetEventSlim();
        void OnNew() => firedForNew.Set();
        svc.Updated += OnNew;
        TestFiles.WriteResponse(dir.Of("libB", "B.json"));
        Assert.True(firedForNew.Wait(WaitMs), "watcher did not fire for a change in the new root");
        Assert.Contains(svc.Snapshot.AllResponses, r => r.Name == "B");
        svc.Updated -= OnNew;

        // A change in the OLD root must NOT wake the watcher any more (old watcher was disposed).
        var firedForOld = new ManualResetEventSlim();
        svc.Updated += () => firedForOld.Set();
        TestFiles.WriteResponse(dir.Of("libA", "StaleA.json"));
        Assert.False(firedForOld.Wait(1200), "watcher still fires for the abandoned old root");
        // And the snapshot (rooted at libB) never picked up the old-root file.
        Assert.DoesNotContain(svc.Snapshot.AllResponses, r => r.Name == "StaleA");
    }

    [Fact]
    public void Repoint_ToMissingDirectory_DoesNotThrow_AndSnapshotIsEmpty()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("libA", "A.json"));

        using var svc = new LibraryService(dir.Of("libA"), seedSourceDir: null,
            recentPath: dir.Of("recent.json"), watch: true);
        Assert.Single(svc.Snapshot.AllResponses);

        // A path that doesn't exist: scan yields nothing and the watcher re-attach must be safe.
        var missing = dir.Of("does-not-exist");
        var ex = Record.Exception(() => svc.Repoint(missing));
        Assert.Null(ex);
        Assert.Empty(svc.Snapshot.AllResponses);
    }

    [Fact]
    public void Repoint_ToEmptyDirectory_DoesNotThrow_AndSnapshotIsEmpty()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("libA", "A.json"));
        Directory.CreateDirectory(dir.Of("empty"));

        using var svc = new LibraryService(dir.Of("libA"), seedSourceDir: null,
            recentPath: dir.Of("recent.json"), watch: true);

        var ex = Record.Exception(() => svc.Repoint(dir.Of("empty")));
        Assert.Null(ex);
        Assert.Empty(svc.Snapshot.AllResponses);
    }
}
