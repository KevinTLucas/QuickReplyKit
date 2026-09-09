# QuickReplyKit - Development And Distribution

This is the maintainer guide. The root [`README.md`](../README.md) is the end-user product guide; implementation
contracts belong in [`ARCHITECTURE.md`](../ARCHITECTURE.md), and the live handoff state belongs in
[`STATUS.md`](STATUS.md).

## Maintainer read order

1. This file: toolchain, commands, packaging, and dependency inventory.
2. [`STATUS.md`](STATUS.md): exact audited baseline, completed work, remaining work, and known limits.
3. [`../ARCHITECTURE.md`](../ARCHITECTURE.md): design contracts and code map.
4. [`PROJECT-BRIEF.md`](PROJECT-BRIEF.md): original requirements.
5. [`BACKLOG.md`](BACKLOG.md): unfinished requested work only.

Root `AGENTS.md` and `CLAUDE.md` are local operating instructions and intentionally ignored by Git.

## Toolchain

- Windows 11 x64 or a Microsoft-supported Windows 10 Enterprise/LTSC x64 development machine.
- .NET 10 SDK. On the primary workstation, `dotnet.exe` is under `C:\Program Files\dotnet`.
- WPF/C# targeting `net10.0-windows` with `UseWPF=true`.
- No third-party runtime/NuGet dependencies in the product projects: use the BCL, WPF, or native Windows APIs.
- The test project uses `Microsoft.NET.Test.Sdk`, xUnit, the Visual Studio xUnit runner, and `coverlet.collector` as
  development-only NuGet packages. None is published with the application.

The solution contains:

- `src/QuickReplyKit.Core`: headless library, storage, search, templates/scripts, backup/import, and text logic.
- `src/QuickReplyKit`: WPF views/view-models, desktop integration, Win32 interop, and services.
- `src/QuickReplyKit.Tests`: xUnit unit, integration, UI-contract, security, and Windows interop tests.

See Architecture for the detailed subsystem map rather than duplicating it here.

## Build, test, and run

PowerShell:

```powershell
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages" # optional explicit/offline cache

dotnet build QuickReplyKit.slnx -c Release -p:NuGetAudit=false
dotnet test QuickReplyKit.slnx -c Release --no-build -p:NuGetAudit=false
dotnet run --project src\QuickReplyKit -- --show
```

`NuGetAudit=false` avoids an online advisory lookup in offline/sandboxed environments; it does not suppress compiler
or test failures. On another machine, omit the workstation-specific `NUGET_PACKAGES` line or point it at that
machine's package cache.

The app normally starts in the system tray. `--show` opens the launcher immediately.

## Verification switches

The shipping executable includes bounded self-test entry points:

```powershell
QuickReplyKit.exe --selftest-paste
QuickReplyKit.exe --selftest-paste-rich
QuickReplyKit.exe --selftest-paste-ui
QuickReplyKit.exe --selftest-foreground
```

- `--selftest-paste`: clipboard snapshot/write/paste/restore path.
- `--selftest-paste-rich`: byte-correct CF_HTML plus plain-text alternative.
- `--selftest-paste-ui`: launcher view-model command through the real insert service.
- `--selftest-foreground`: foreground target monitoring.

The Notepad-based gates create one isolated temporary document, undo the paste, and close that exact window normally.
Run them sequentially. Never close or alter an unrelated user Notepad window.

QA display switches used by repository screenshot tools include `--show`, `--library <path>`, `--size WxH`,
`--theme Light|Dark|System`, `--show-editor`, `--show-about`, `--show-backup`, `--show-onboarding`, `--show-trash`, and
`--show-hotkeys`. They must use controlled temporary data and must not persist test-only product state.

Repository helpers:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\shoot.ps1 `
  -Out "$env:TEMP\launcher.png" `
  -AppArgs "--show --theme Dark --library C:\Temp\QuickReplyKit-QA\Library"

powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture-window.ps1 `
  -TitleContains "QuickReplyKit" -Out "$env:TEMP\launcher.png"
```

UI QA uses these repository scripts and direct inspection of test-created windows. Do not use GPT Computer Use or
UIAutomation. Close every created window and process; do not touch unrelated user windows.

## Portable publish

Build the canonical self-contained, single-file x64 package from a clean, tested `main` tip:

```powershell
dotnet publish src\QuickReplyKit\QuickReplyKit.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none `
  -p:NuGetAudit=false `
  -o publish
```

Distribution choices:

- `publish\QuickReplyKit.exe` alone: complete application/runtime, empty first-run library.
- `release\QuickReplyKit.exe` plus `release\DefaultLibrary\Examples\`: same executable plus optional first-run sample responses.

The repository tracks a curated distributable `release` folder containing `QuickReplyKit.exe` and
`DefaultLibrary\Examples`. Rebuild it from `publish\` after each product/runtime merge.

The project copies `assets\DefaultLibrary` to publish output. It is data, not a runtime dependency. An existing
non-empty user library is not reseeded or overwritten.

After every product/runtime/asset merge to `main`, rerun the integrated Release build/tests and rebuild
`publish\QuickReplyKit.exe` from that exact product tip. A docs-only merge does not require a new executable. Record
the test count, product commit, byte size, and SHA-256 in `STATUS.md`.

## Runtime requirements and dependency inventory

The published `win-x64` executable is self-contained. A target PC needs Windows 11 x64 or a
[Microsoft-supported Windows 10 Enterprise/LTSC x64 release](https://learn.microsoft.com/dotnet/core/install/windows).
It does not need a separate .NET runtime, SDK, Visual C++ Redistributable, or NuGet package.

The single file bundles the .NET 10 runtime, WPF, application assemblies, and native runtime libraries. Representative
bundled native components include:

- `coreclr.dll` and `clrjit.dll`;
- `hostfxr.dll` and `hostpolicy.dll`;
- `vcruntime140_cor3.dll` (the runtime's private C++ support);
- `PresentationNative_cor3.dll` and `wpfgfx_cor3.dll`;
- `D3DCompiler_47_cor3.dll`;
- `System.IO.Compression.Native.dll`.

The app also calls standard Windows components already present on supported Windows versions:

- `user32.dll`, `kernel32.dll`, and `gdi32.dll` for windows, input, and graphics;
- `dwmapi.dll` for title-bar theme integration;
- `shell32.dll` and `ole32.dll` for Explorer, clipboard, and shell services;
- Windows DirectX/DirectWrite components used by WPF.

There are no third-party redistributable DLLs. Single-file native components extract to a per-user temporary location
when the application starts. Trimming remains disabled because WPF is not trim-safe.

## Branch and review workflow

1. Start from clean, green `main`.
2. Create one concise, human-readable branch/worktree for one feature or fix.
3. Keep changes scoped; use existing BCL/native patterns and do not add NuGet packages.
4. Build and test. UI work also needs representative Light/Dark/compact inspection.
5. Have a separate agent independently review correctness, security, accessibility, UX, performance, and regression
   risk as appropriate.
6. Fix blockers on the feature branch, rerun the full suite, then merge `--no-ff` only when green.
7. Update `STATUS.md` and `BACKLOG.md`; update README/Architecture/this guide when their contracts changed.
8. Rebuild the portable executable after a product merge, then push the tested docs-inclusive `main`.

Before merge, verify scope with:

```powershell
git log --oneline main..BRANCH
git diff --name-status main...BRANCH
```

Use only the configured local Git identity. Do not add AI/vendor co-author trailers.

## Documentation ownership

- `README.md`: end-user installation, workflows, safety, troubleshooting, screenshots.
- `docs/DEVELOPMENT.md`: build, test, publish, runtime dependencies, contributor workflow.
- `ARCHITECTURE.md`: current implementation contracts and code map.
- `docs/STATUS.md`: exact current baseline and cold-pickup state.
- `docs/BACKLOG.md`: unfinished requested work only.
- `docs/PROJECT-BRIEF.md`: preserved original requirements.

A change is not complete until code, tests, and these handoff documents agree.
