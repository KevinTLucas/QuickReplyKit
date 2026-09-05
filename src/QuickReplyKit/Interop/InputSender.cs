using System.Runtime.InteropServices;

namespace QuickReplyKit.Interop;

/// <summary>Synthesizes Ctrl+V via SendInput, leaving no modifier stuck down.</summary>
internal static class InputSender
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        // KEYBDINPUT is smaller than the INPUT union (MOUSEINPUT); one ulong pads the struct out
        // to the full union size so Marshal.SizeOf matches the native 40-byte layout on x64.
        public KEYBDINPUT ki;
        private readonly ulong _pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void SendCtrlV()
    {
        var inputs = new[]
        {
            Key(VK_CONTROL, down: true),
            Key(VK_V, down: true),
            Key(VK_V, down: false),
            Key(VK_CONTROL, down: false),
        };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            System.Diagnostics.Debug.WriteLine($"[QuickReplyKit] SendInput sent {sent}/{inputs.Length}, err={Marshal.GetLastWin32Error()}");
    }

    private static INPUT Key(ushort vk, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? 0 : KEYEVENTF_KEYUP },
    };
}
