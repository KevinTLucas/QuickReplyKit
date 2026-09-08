using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Threading;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Services;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Tests;

public class AsyncScriptIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 14, 5, 6, TimeSpan.FromHours(-5));

    private sealed class FakeInsert : IInsertService
    {
        public readonly List<(string Mode, ResponseItem Item)> Calls = [];
        public void InsertRich(ResponseItem item) => Calls.Add(("rich", item));
        public void InsertPlain(ResponseItem item) => Calls.Add(("plain", item));
        public void Copy(ResponseItem item) => Calls.Add(("copy", item));
    }

    private sealed class ThrowingInsert : IInsertService
    {
        public void InsertRich(ResponseItem item) => throw new InvalidOperationException("insert failure");
        public void InsertPlain(ResponseItem item) => throw new InvalidOperationException("insert failure");
        public void Copy(ResponseItem item) => throw new InvalidOperationException("insert failure");
    }

    [Fact]
    public void LauncherNativeAction_ReturnsImmediately_AndDuplicateIsIgnored()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = SaveTrustedScript(temp, "Slow",
                "Start-Sleep -Milliseconds 500; [Console]::Out.Write('done')");
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"),
                html: "<p>{{script:Slow}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            var vm = new LauncherViewModel(library, insert, new AppConfig(), () => Now);
            int hidden = 0;
            vm.RequestHide += () => hidden++;

            var timer = Stopwatch.StartNew();
            vm.TriggerDefaultInsert();
            timer.Stop();
            Task first = vm.PendingAction;
            vm.TriggerDefaultInsert();

            Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(200), timer.Elapsed.ToString());
            await first;
            await vm.PreviewCompletion;
            Assert.Equal("<p>done</p>", Assert.Single(insert.Calls).Item.Html);
            Assert.Equal(1, hidden);
            Assert.Single(library.Recent());
        });
    }

    [Fact]
    public void LauncherPreview_StaleNativeResultCannotReplaceLatestSelection()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = SaveTrustedScript(temp, "Slow",
                "Start-Sleep -Milliseconds 700; [Console]::Out.Write('STALE')");
            SaveTrustedScript(temp, "Fast", "[Console]::Out.Write('LATEST')", trust);
            TestFiles.WriteResponse(temp.Of("lib", "A.json"), html: "<p>{{script:Slow}}</p>");
            TestFiles.WriteResponse(temp.Of("lib", "B.json"), html: "<p>{{script:Fast}}</p>");
            using var library = Service(temp, trust);
            var vm = new LauncherViewModel(library, new FakeInsert(), new AppConfig(), () => Now);
            Task stale = vm.PreviewCompletion;

            vm.SelectedResult = vm.Results.Single(row => row.Name == "B");
            Task latest = vm.PreviewCompletion;
            await Task.WhenAll(stale, latest);

            string text = new TextRange(vm.PreviewDocument.ContentStart,
                vm.PreviewDocument.ContentEnd).Text;
            Assert.Contains("LATEST", text);
            Assert.DoesNotContain("STALE", text);
        });
    }

    [Fact]
    public void LauncherPlainCopyAndCompose_AllAwaitNativeResolution()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = SaveTrustedScript(temp, "Value", "[Console]::Out.Write('resolved')");
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"),
                html: "<p>{{script:Value}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            var vm = new LauncherViewModel(library, insert, new AppConfig(), () => Now);
            await vm.PreviewCompletion;

            await vm.TriggerPlainInsertAsync();
            vm.CopyCommand.Execute(null);
            await vm.PendingAction;
            string composed = await vm.ResolveComposedAsync(vm.SelectedItem!);
            vm.InsertComposed("<p>{{script:Value}}</p>", plain: false);
            await vm.PendingAction;

            Assert.Equal(["plain", "copy", "rich"], insert.Calls.Select(call => call.Mode));
            Assert.All(insert.Calls, call => Assert.Equal("<p>resolved</p>", call.Item.Html));
            Assert.Equal("<p>resolved</p>", composed);
        });
    }

    [Fact]
    public void LauncherActions_BlockFailedScriptInsteadOfPastingItsMarker()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = SaveTrustedScript(temp, "Denied", "Write-Error 'Access is denied: secret'");
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"), html: "<p>{{script:Denied}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            var vm = new LauncherViewModel(library, insert, new AppConfig(), () => Now);
            var errors = new List<string>();
            int hidden = 0;
            vm.RequestScriptError += errors.Add;
            vm.RequestHide += () => hidden++;
            await vm.PreviewCompletion;

            await vm.TriggerDefaultInsertAsync();
            await vm.TriggerPlainInsertAsync();
            vm.CopyCommand.Execute(null);
            await vm.PendingAction;
            string? composed = await vm.TryResolveComposedAsync(vm.SelectedItem!);
            vm.InsertComposed("<p>{{script:Denied}}</p>", plain: false);
            await vm.PendingAction;

            Assert.Empty(insert.Calls);
            Assert.Equal(5, errors.Count);
            Assert.All(errors, error => Assert.Contains("signed-in user", error, StringComparison.OrdinalIgnoreCase));
            Assert.All(errors, error => Assert.Contains("Denied", error, StringComparison.Ordinal));
            Assert.Null(composed);
            Assert.Equal(0, hidden);
        });
    }

    [Fact]
    public void LauncherUntrustedScript_ShowsReasonAndDoesNotPasteHideOrMarkUsed()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = ScriptTrustLedger.Open(temp.Of("trust.json")).Value!;
            var definition = new ScriptDefinition("needs-review", "Needs Review",
                ScriptDefinitionFile.NativeLanguage, true, "[Console]::Out.Write('must-not-run')");
            Assert.True(ScriptDefinitionFile.Save(
                temp.Of("lib", "Needs Review" + ScriptDefinitionFile.Extension), definition).Success);
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"), html: "<p>{{script:Needs Review}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            var vm = new LauncherViewModel(library, insert, new AppConfig(), () => Now);
            string? error = null;
            bool hidden = false;
            vm.RequestScriptError += value => error = value;
            vm.RequestHide += () => hidden = true;

            await vm.TriggerDefaultInsertAsync();

            Assert.Empty(insert.Calls);
            Assert.Contains("untrusted", error, StringComparison.OrdinalIgnoreCase);
            Assert.False(hidden);
            Assert.Empty(library.Recent());
        });
    }

    [Fact]
    public void ShippedFridayExample_ResolvesThroughPreviewAndRealPasteCommand()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            string asset = TestFiles.RepoAsset(Path.Combine("assets", "DefaultLibrary", "Examples", "Scripts",
                "Example Full-Trust Random Friday Greeting" + ScriptDefinitionFile.Extension));
            var definition = Assert.IsType<ScriptDefinition>(ScriptDefinitionFile.Load(asset).Value);
            Assert.True(ScriptDefinitionFile.Save(
                temp.Of("lib", Path.GetFileName(asset)), definition).Success);
            var trust = ScriptTrustLedger.Open(temp.Of("trust.json")).Value!;
            Assert.True(trust.Trust(definition).Success);
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"),
                html: "<p>{{script:Example Full-Trust Random Friday Greeting}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            var friday = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.FromHours(-5));
            var vm = new LauncherViewModel(library, insert, new AppConfig(), () => friday);
            await vm.PreviewCompletion;

            await vm.TriggerDefaultInsertAsync();

            string html = Assert.Single(insert.Calls).Item.Html;
            Assert.Contains("Happy Friday!", html, StringComparison.Ordinal);
            Assert.DoesNotContain("[script", html, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void LauncherPaste_ReusesFreshNativePreviewResult()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = SaveTrustedScript(temp, "Count",
                "$p=Join-Path $env:QRK_LIBRARY_ROOT 'count.txt'; " +
                "$n=if(Test-Path -LiteralPath $p){[int][IO.File]::ReadAllText($p)}else{0}; " +
                "$n++; [IO.File]::WriteAllText($p,$n); [Console]::Out.Write($n)");
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"), html: "<p>{{script:Count}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            var vm = new LauncherViewModel(library, insert, new AppConfig(), () => Now);
            await vm.PreviewCompletion;

            await vm.TriggerDefaultInsertAsync();

            Assert.Equal("1", File.ReadAllText(temp.Of("lib", "count.txt")));
            Assert.Equal("<p>1</p>", Assert.Single(insert.Calls).Item.Html);
        });
    }

    [Fact]
    public void LauncherPaste_WhilePreviewRunsAwaitsTheSamePowerShellProcess()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = SaveTrustedScript(temp, "Count",
                "$p=Join-Path $env:QRK_LIBRARY_ROOT 'count.txt'; Start-Sleep -Milliseconds 350; " +
                "$n=if(Test-Path -LiteralPath $p){[int][IO.File]::ReadAllText($p)}else{0}; " +
                "$n++; [IO.File]::WriteAllText($p,$n); [Console]::Out.Write($n)");
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"), html: "<p>{{script:Count}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            var vm = new LauncherViewModel(library, insert, new AppConfig(), () => Now);

            vm.TriggerDefaultInsert();
            await vm.PendingAction;

            Assert.Equal("1", File.ReadAllText(temp.Of("lib", "count.txt")));
            Assert.Equal("<p>1</p>", Assert.Single(insert.Calls).Item.Html);
        });
    }

    [Fact]
    public async Task LibraryAsyncResolve_CancellationReachesRunnerAndKillsProcess()
    {
        using var temp = new TempDir();
        string probe = temp.Of("lib", "after-cancel.tmp");
        var trust = SaveTrustedScript(temp, "Slow",
            "Start-Sleep -Milliseconds 900; Set-Content -LiteralPath " +
            "(Join-Path $env:QRK_LIBRARY_ROOT 'after-cancel.tmp') -Value 'bad'");
        using var library = Service(temp, trust);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.ResolveTemplateAsync("{{script:Slow}}", Now, stop.Token));
        await Task.Delay(1100);

        Assert.False(File.Exists(probe));
    }

    [Fact]
    public void InstantHotkeyFailure_IsObservedAndDoesNotCrash()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = SaveTrustedScript(temp, "Fails", "throw 'customer-secret'");
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"),
                html: "<p>{{script:Fails}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            using var hotkeys = new HotkeyService(new AppConfig(), library, insert, () => Now);
            string? error = null;
            hotkeys.ScriptFailed += value => error = value;

            hotkeys.InstantPaste("Answer.json", plain: false);
            await hotkeys.PendingPaste;

            Assert.True(hotkeys.PendingPaste.IsCompletedSuccessfully);
            Assert.Empty(insert.Calls);
            Assert.NotNull(error);
            Assert.DoesNotContain("customer-secret", error, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void InstantHotkey_DisposeCancelsNativeWorkAndPreventsPaste()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = SaveTrustedScript(temp, "Slow",
                "Start-Sleep -Milliseconds 900; [Console]::Out.Write('late')");
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"),
                html: "<p>{{script:Slow}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            var hotkeys = new HotkeyService(new AppConfig(), library, insert, () => Now);

            hotkeys.InstantPaste("Answer.json", plain: false);
            Task pending = hotkeys.PendingPaste;
            hotkeys.Dispose();
            await pending;

            Assert.Empty(insert.Calls);
        });
    }

    [Fact]
    public void InstantHotkey_DuplicateWhileNativeWorkRuns_IsIgnored()
    {
        Sta(async () =>
        {
            using var temp = new TempDir();
            var trust = SaveTrustedScript(temp, "Slow",
                "Start-Sleep -Milliseconds 500; [Console]::Out.Write('once')");
            TestFiles.WriteResponse(temp.Of("lib", "Answer.json"),
                html: "<p>{{script:Slow}}</p>");
            using var library = Service(temp, trust);
            var insert = new FakeInsert();
            using var hotkeys = new HotkeyService(new AppConfig(), library, insert, () => Now);

            hotkeys.InstantPaste("Answer.json", plain: false);
            Task first = hotkeys.PendingPaste;
            hotkeys.InstantPaste("Answer.json", plain: false);
            await first;

            Assert.Equal("<p>once</p>", Assert.Single(insert.Calls).Item.Html);
        });
    }

    private static ScriptTrustLedger SaveTrustedScript(TempDir temp, string name, string source,
        ScriptTrustLedger? trust = null)
    {
        trust ??= ScriptTrustLedger.Open(temp.Of("trust.json")).Value!;
        var definition = new ScriptDefinition(name, name, ScriptDefinitionFile.NativeLanguage,
            true, source);
        Assert.True(ScriptDefinitionFile.Save(
            temp.Of("lib", name + ScriptDefinitionFile.Extension), definition).Success);
        Assert.True(trust.Trust(definition).Success);
        return trust;
    }

    private static LibraryService Service(TempDir temp, ScriptTrustLedger trust) =>
        new(temp.Of("lib"), recentPath: temp.Of("recent.json"), watch: false, scriptTrust: trust);

    private static void Sta(Func<Task> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            Task task;
            try { task = test(); }
            catch (Exception ex) { failure = ex; return; }
            _ = task.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background),
                TaskScheduler.Default);
            Dispatcher.Run();
            try { task.GetAwaiter().GetResult(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA test timed out.");
        if (failure is not null) throw failure;
    }
}
