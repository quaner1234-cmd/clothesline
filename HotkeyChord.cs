namespace Clothesline;

/// <summary>全局热键。注册失败时按 Ctrl+Alt+L、再 Ctrl+Alt+T 回退；用户选定的组合仍写在设置里。</summary>
public sealed class HotkeyChord
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public bool Ctrl { get; }
    public bool Alt { get; }
    public bool Shift { get; }
    public bool Win { get; }
    public string Key { get; }

    public HotkeyChord(bool ctrl, bool alt, bool shift, bool win, string key)
    {
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
        Win = win;
        Key = CanonicalKey(key) ?? key?.Trim() ?? "";
    }

    public bool HasModifier => Ctrl || Alt || Shift || Win;

    public uint? VirtualKey => TryVirtualKey(Key);

    public bool IsRegisterable => HasModifier && VirtualKey != null;

    public uint ModifierFlags =>
        (Ctrl ? ModControl : 0) |
        (Alt ? ModAlt : 0) |
        (Shift ? ModShift : 0) |
        (Win ? ModWin : 0) |
        ModNoRepeat;

    public string Label
    {
        get
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            parts.Add(string.IsNullOrEmpty(Key) ? "?" : Key);
            return string.Join("+", parts);
        }
    }

    public static HotkeyChord FromSettings(AppSettings settings) =>
        new(settings.HotkeyCtrl, settings.HotkeyAlt, settings.HotkeyShift, settings.HotkeyWin, settings.HotkeyKey);

    public void WriteTo(AppSettings settings)
    {
        settings.HotkeyCtrl = Ctrl;
        settings.HotkeyAlt = Alt;
        settings.HotkeyShift = Shift;
        settings.HotkeyWin = Win;
        settings.HotkeyKey = Key;
    }

    public bool SameBinding(HotkeyChord other) =>
        Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift && Win == other.Win
        && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    /// <summary>首选在前。与首选不同的 Ctrl+Alt+L、Ctrl+Alt+T 依次作为冲突回退。</summary>
    public static IReadOnlyList<HotkeyChord> RegistrationOrder(HotkeyChord preferred)
    {
        var list = new List<HotkeyChord>();
        void Add(HotkeyChord chord)
        {
            if (list.Any(existing => existing.SameBinding(chord))) return;
            list.Add(chord);
        }

        Add(preferred);
        Add(new HotkeyChord(true, true, false, false, "L"));
        Add(new HotkeyChord(true, true, false, false, "T"));
        return list;
    }

    public static uint? TryVirtualKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
            return null;
        }

        if ((key[0] == 'F' || key[0] == 'f') && int.TryParse(key.AsSpan(1), out int f) && f is >= 1 and <= 24)
            return (uint)(0x70 + f - 1);

        if (key.StartsWith("Num", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(3), out int n) && n is >= 0 and <= 9)
            return (uint)(0x60 + n);

        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase)) return 0x20;
        return null;
    }

    public static string? CanonicalKey(string? key)
    {
        if (TryVirtualKey(key) == null || string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        if (key.Length == 1) return key.ToUpperInvariant();
        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase)) return "Space";
        if (key.StartsWith("Num", StringComparison.OrdinalIgnoreCase))
            return "Num" + key.Substring(3);
        if (key[0] == 'F' || key[0] == 'f')
            return "F" + int.Parse(key.AsSpan(1)).ToString();
        return key;
    }
}
