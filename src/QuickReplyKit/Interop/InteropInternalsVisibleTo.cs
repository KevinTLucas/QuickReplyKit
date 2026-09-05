using System.Runtime.CompilerServices;

// The Phase 3 interop (ClipboardWriter, InputSender, ForegroundWindow, ClipboardBackup) is
// internal to the app assembly. Expose it to the test project so QA can drive the raw-Win32
// clipboard write directly and assert the history-exclusion formats are set.
[assembly: InternalsVisibleTo("QuickReplyKit.Tests")]
