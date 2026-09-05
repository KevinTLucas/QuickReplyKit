namespace QuickReplyKit.Interop;

/// <summary>
/// A parsed global-hotkey gesture: the RegisterHotKey modifier flags plus a virtual-key code.
/// Pure and side-effect free so the parser can be unit-tested without any Win32 registration.
/// </summary>
public readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    // RegisterHotKey MOD_* flags.
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>
    /// Parse a gesture such as "Ctrl+Alt+Space" or "Alt+Shift+Q". Requires exactly one main
    /// (non-modifier) key; any unknown token, a missing main key, or two main keys fails.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0;
        uint? vk = null;

        foreach (var raw in text.Split('+'))
        {
            var token = raw.Trim();
            if (token.Length == 0) return false;   // e.g. "Ctrl+" or "Ctrl++Space"

            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; continue;
                case "alt": mods |= MOD_ALT; continue;
                case "shift": mods |= MOD_SHIFT; continue;
                case "win" or "windows" or "meta" or "super": mods |= MOD_WIN; continue;
            }

            if (vk.HasValue) return false;   // a second main key is not allowed
            if (!TryKey(token, out uint code)) return false;
            vk = code;
        }

        if (vk is null) return false;   // modifiers only, no main key
        gesture = new HotkeyGesture(mods, vk.Value);
        return true;
    }

    private static bool TryKey(string token, out uint vk)
    {
        vk = 0;

        // Single letter A-Z or digit 0-9 → the VK equals the uppercase ASCII code.
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
        }

        // Function keys F1-F24.
        if ((token[0] is 'f' or 'F') && token.Length is 2 or 3 &&
            int.TryParse(token.AsSpan(1), out int n) && n is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + (n - 1));   // VK_F1 = 0x70
            return true;
        }

        if (Named.TryGetValue(token.ToLowerInvariant(), out uint named)) { vk = named; return true; }
        return false;
    }

    private static readonly Dictionary<string, uint> Named = new()
    {
        ["space"] = 0x20, ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09,
        ["esc"] = 0x1B, ["escape"] = 0x1B, ["backspace"] = 0x08,
        ["ins"] = 0x2D, ["insert"] = 0x2D, ["del"] = 0x2E, ["delete"] = 0x2E,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pgup"] = 0x21,
        ["pagedown"] = 0x22, ["pgdn"] = 0x22,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
    };
}
