using Clothesline;

int failed = 0;

void Check(bool cond, string name)
{
    if (cond)
    {
        Console.WriteLine("ok  " + name);
        return;
    }

    Console.Error.WriteLine("FAIL " + name);
    failed++;
}

var root = Path.Combine(Path.GetTempPath(), "clothesline-logic-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var shots = Path.Combine(root, "Screenshots");
    Directory.CreateDirectory(shots);

    var created = AppSettings.Load(root, shots);
    Check(created.MaxPegs == AppSettings.DefaultMaxPegs, "default max pegs is 20");
    Check(created.HistoryLimit == AppSettings.DefaultHistoryLimit, "default history limit is 50");
    Check(created.ClipboardWatchEnabled, "clipboard watch defaults on");
    Check(created.HotkeyCtrl && created.HotkeyAlt && created.HotkeyKey == "T", "default hotkey Ctrl+Alt+T");
    Check(created.WatchFolders.Count == 1 && created.WatchFolders[0] == Path.GetFullPath(shots), "default watch folder");
    Check(File.Exists(AppSettings.FilePath(root)), "settings.json created");

    created.MaxPegs = 0;
    created.ClipboardMinWidth = -3;
    created.ClipboardMinHeight = -1;
    created.WatchFolders.Add(shots);
    created.WatchFolders.Add("  ");
    created.Normalize();
    Check(created.MaxPegs == 1, "max pegs clamps up to 1");
    Check(created.ClipboardMinWidth == 0 && created.ClipboardMinHeight == 0, "negative min size clamps to 0");
    Check(created.WatchFolders.Count == 1, "blank and duplicate watch folders drop");

    created.MaxPegs = 9999;
    created.Normalize();
    Check(created.MaxPegs == AppSettings.MaxPegsLimit, "max pegs clamps to limit");

    created.MaxPegs = 12;
    created.HistoryLimit = 30;
    created.ClipboardWatchEnabled = false;
    created.ClipboardMinWidth = 80;
    created.ClipboardMinHeight = 40;
    created.HotkeyShift = true;
    created.HotkeyKey = "f5";
    created.WatchFolders = new List<string> { shots };
    created.Normalize();
    created.Save(root);

    var loaded = AppSettings.Load(root, shots);
    Check(loaded.MaxPegs == 12, "max pegs roundtrip");
    Check(loaded.HistoryLimit == 30, "history limit roundtrip");
    Check(!loaded.ClipboardWatchEnabled, "clipboard toggle roundtrip");
    Check(loaded.ClipboardMinWidth == 80 && loaded.ClipboardMinHeight == 40, "min size roundtrip");
    Check(loaded.HotkeyShift && loaded.HotkeyKey == "f5", "hotkey fields roundtrip");
    Check(!loaded.ClipboardAccepts(200, 200), "disabled clipboard rejects any size");

    loaded.ClipboardWatchEnabled = true;
    Check(!loaded.ClipboardAccepts(0, 10), "zero dimension rejected");
    Check(!loaded.ClipboardAccepts(79, 40), "width below min rejected");
    Check(!loaded.ClipboardAccepts(80, 39), "height below min rejected");
    Check(loaded.ClipboardAccepts(80, 40), "size at min accepted");
    Check(loaded.ClipboardAccepts(800, 600), "larger image accepted");

    var emptyDir = Path.Combine(root, "empty-watch");
    Directory.CreateDirectory(emptyDir);
    File.WriteAllText(AppSettings.FilePath(emptyDir), """
        {
          "maxPegs": 7,
          "clipboardWatchEnabled": true,
          "clipboardMinWidth": 0,
          "clipboardMinHeight": 0,
          "hotkeyCtrl": true,
          "hotkeyAlt": true,
          "hotkeyShift": false,
          "hotkeyWin": false,
          "hotkeyKey": "T",
          "watchFolders": []
        }
        """);
    var emptied = AppSettings.Load(emptyDir, shots);
    Check(emptied.WatchFolders.Count == 0, "saved empty watch list stays empty");
    Check(emptied.MaxPegs == 7, "camelCase settings load");
    Check(emptied.HistoryLimit == AppSettings.DefaultHistoryLimit, "missing historyLimit stays default");
    var historyClamp = new AppSettings { HistoryLimit = 0 };
    historyClamp.Normalize();
    Check(historyClamp.HistoryLimit == 1, "history limit clamps up to 1");
    historyClamp.HistoryLimit = 9000;
    historyClamp.Normalize();
    Check(historyClamp.HistoryLimit == AppSettings.HistoryLimitMax, "history limit clamps to max");

    File.WriteAllText(AppSettings.FilePath(emptyDir), "{");
    var corruptThrew = false;
    try { AppSettings.Load(emptyDir, shots); }
    catch (InvalidDataException) { corruptThrew = true; }
    Check(corruptThrew, "corrupt settings.json throws");
    Check(File.ReadAllText(AppSettings.FilePath(emptyDir)) == "{", "corrupt settings.json is not overwritten");

    var keep = Path.Combine(root, "keep.png");
    var gone = Path.Combine(root, "gone.png");
    File.WriteAllText(keep, "img");
    File.WriteAllText(gone, "img");
    PegStore.Save(root, new[]
    {
        new PegRecord { FilePath = keep, Angle = 1.5 },
        new PegRecord { FilePath = gone, Angle = -2 },
    });
    File.Delete(gone);
    var pegs = PegStore.Load(root);
    Check(pegs.Count == 1 && pegs[0].FilePath == keep && pegs[0].Angle == 1.5, "missing peg file dropped on load");

    var list = new List<string> { "a", "b", "c", "d" };
    PegStore.TrimOldest(list, 2);
    Check(list.Count == 2 && list[0] == "c" && list[1] == "d", "trim drops oldest");
    var trimThrew = false;
    try { PegStore.TrimOldest(list, 0); }
    catch (ArgumentOutOfRangeException) { trimThrew = true; }
    Check(trimThrew, "trim rejects non-positive capacity");

    File.WriteAllText(PegStore.FilePath(root), "{");
    var pegCorrupt = false;
    try { PegStore.Load(root); }
    catch (InvalidDataException) { pegCorrupt = true; }
    Check(pegCorrupt, "corrupt pegs.json throws");

    var preferred = new HotkeyChord(true, true, false, false, "t");
    Check(preferred.Label == "Ctrl+Alt+T", "default label");
    Check(preferred.VirtualKey == (uint)'T', "letter virtual key");
    Check(preferred.IsRegisterable, "ctrl+alt+t registerable");
    Check((preferred.ModifierFlags & HotkeyChord.ModNoRepeat) != 0, "norepeat flag set");

    var order = HotkeyChord.RegistrationOrder(preferred);
    Check(order.Count == 2 && order[0].Key == "T" && order[1].Key == "L", "fallback appends L without duplicating T");

    var custom = new HotkeyChord(true, false, true, false, "F5");
    var customOrder = HotkeyChord.RegistrationOrder(custom);
    Check(customOrder.Count == 3
        && customOrder[0].Label == "Ctrl+Shift+F5"
        && customOrder[1].Label == "Ctrl+Alt+L"
        && customOrder[2].Label == "Ctrl+Alt+T", "custom hotkey falls back to L then T");
    Check(custom.VirtualKey == 0x74, "F5 virtual key");

    var bare = new HotkeyChord(false, false, false, false, "A");
    Check(!bare.IsRegisterable, "modifier required");
    Check(HotkeyChord.TryVirtualKey("nope") == null, "unknown key rejected");
    Check(HotkeyChord.TryVirtualKey("Num3") == 0x63, "numpad virtual key");
    Check(new HotkeyChord(true, false, false, true, "space").Label == "Ctrl+Win+Space", "space label");

    var folder = Path.Combine(root, "drop-folder");
    Directory.CreateDirectory(folder);
    var folderUrl = new Uri(folder).AbsoluteUri;
    Check(DropLocation.FromShellUrl(folderUrl) == DropKind.Folder, "existing folder url");
    Check(DropLocation.FromShellUrl("::{645FF040-5081-101B-9F08-00AA002F954E}") == DropKind.RecycleBin, "recycle clsid url");
    Check(DropLocation.FromShellUrl("file:///C:/no/such/clothesline-drop") == DropKind.Other, "missing folder url is not a folder");
    Check(DropLocation.FromShellUrl("") == DropKind.Other, "empty shell url");
    Check(DropLocation.FromShellUrl("http://example.test") == DropKind.Other, "non-file shell url");
    var threw = false;
    try { _ = DropLocation.FromShellUrl("file:///%zz"); }
    catch { threw = true; }
    Check(!threw, "bad file url does not throw");

    var h1 = Path.Combine(root, "h1.png");
    var h2 = Path.Combine(root, "h2.png");
    var h3 = Path.Combine(root, "h3.png");
    File.WriteAllText(h1, "a");
    File.WriteAllText(h2, "b");
    File.WriteAllText(h3, "c");
    HistoryStore.Remember(root, h1, 1, 10);
    HistoryStore.Remember(root, h2, 2, 10);
    HistoryStore.Remember(root, h1, 1.5, 10);
    var hist = HistoryStore.Load(root);
    Check(hist.Count == 2 && hist[0].FilePath == h1 && hist[0].Angle == 1.5 && hist[1].FilePath == h2, "history newest first and dedupes path");
    HistoryStore.Remember(root, h3, 3, 2);
    hist = HistoryStore.Load(root);
    Check(hist.Count == 2 && hist[0].FilePath == h3 && hist[1].FilePath == h1, "history cap drops oldest");
    File.Delete(h3);
    hist = HistoryStore.Load(root);
    Check(hist.Count == 1 && hist[0].FilePath == h1, "history load drops missing file");
    HistoryStore.Remember(root, Path.Combine(root, "no-such-history.png"), 0, 10);
    hist = HistoryStore.Load(root);
    Check(hist.Count == 1 && hist[0].FilePath == h1, "missing file is not recorded");
    HistoryStore.Forget(root, h1);
    Check(HistoryStore.Load(root).Count == 0, "history forget removes entry");
    var limitThrew = false;
    try { HistoryStore.Remember(root, h2, 0, 0); }
    catch (ArgumentOutOfRangeException) { limitThrew = true; }
    Check(limitThrew, "history rejects non-positive limit");
    File.WriteAllText(HistoryStore.FilePath(root), "{");
    var historyCorrupt = false;
    try { HistoryStore.Load(root); }
    catch (InvalidDataException) { historyCorrupt = true; }
    Check(historyCorrupt, "corrupt history.json throws");
    Check(File.ReadAllText(HistoryStore.FilePath(root)) == "{", "corrupt history.json is not overwritten by load");
    File.WriteAllText(h2, "b");
    HistoryStore.Remember(root, h2, 4, 10);
    Check(File.Exists(HistoryStore.FilePath(root) + ".bad"), "remember quarantines corrupt history");
    hist = HistoryStore.Load(root);
    Check(hist.Count == 1 && hist[0].FilePath == h2 && hist[0].Angle == 4, "remember starts fresh after quarantine");
    File.WriteAllText(h1, "a");
    HistoryStore.Remember(root, h1, 1, 10);
    HistoryStore.Trim(root, 1);
    hist = HistoryStore.Load(root);
    Check(hist.Count == 1 && hist[0].FilePath == h1, "history trim keeps newest");

    Check(PegZoom.BaseWidth == 184 && PegZoom.BaseHeight == 216, "peg thumbnail is double 92x108");
    Check(PegZoom.Decide(true) == PegZoom.Gesture.ZoomPeg, "wheel over a peg zooms");
    Check(PegZoom.Decide(false) == PegZoom.Gesture.ScrollLine, "wheel over the rope scrolls");
    Check(Math.Abs(PegZoom.Next(1, 120) - 1.1) < 1e-9, "wheel up zooms in");
    Check(Math.Abs(PegZoom.Next(1, -120) - (1 / 1.1)) < 1e-9, "wheel down zooms out");
    Check(PegZoom.Next(PegZoom.Max, 120) == PegZoom.Max, "zoom stops at 2");
    Check(PegZoom.Next(PegZoom.Min, -120) == PegZoom.Min, "zoom stops at 0.5");
    Check(PegZoom.Next(4, 0) == PegZoom.Max, "zero delta still clamps");
    Check(PegZoom.Next(0.1, 0) == PegZoom.Min, "zero delta clamps up to the minimum");
    Check(Math.Abs(PegZoom.Next(double.NaN, 120) - 1.1) < 1e-9, "non-finite zoom starts from the default");
    Check(PegZoom.CardExtra == 42, "glass card chrome is 42");
    Check(RopeCurve.Y(0, 800) == RopeCurve.EndY && RopeCurve.Y(800, 800) == RopeCurve.EndY, "rope ends stay high");
    Check(RopeCurve.Y(400, 800) == RopeCurve.EndY + RopeCurve.Sag, "rope mid sags");
    Check(RopeCurve.Sag > 7, "mid sag is deeper than the old curve");
    Check(Math.Abs(RopeCurve.Y(200, 800) - RopeCurve.Y(600, 800)) < 1e-9, "rope sag is symmetric");
    Check(RopeCurve.Y(-20, 800) == RopeCurve.EndY, "rope x before the line clamps");
    Check(RopeCurve.Y(50, 0) == RopeCurve.EndY + RopeCurve.Sag, "missing width uses the sag");
    Check(RopeCurve.MaxHang == RopeCurve.EndY + RopeCurve.Sag - RopeCurve.NotchY, "max hang is the mid notch drop");
    Check(PegZoom.LineHeight(1) == 216 + 42 + 56 + RopeCurve.MaxHang, "line height fits the glass card and the sag");
    Check(PegZoom.LineHeight(2) == (216 + 42) * 2 + 56 + RopeCurve.MaxHang, "line grows with the largest zoom");
    Check(PegZoom.LineHeight(0.5) == (216 + 42) * 0.5 + 56 + RopeCurve.MaxHang, "line shrinks when zoomed out");
    Check(PegZoom.LineHeight(9) == PegZoom.LineHeight(PegZoom.Max), "line height clamps zoom");

    Check(EdgeHold.Milliseconds == 500, "top edge waits half a second");
    var edge = new EdgeHold();
    Check(!edge.Due(), "top edge does not reveal before the pointer arrives");
    edge.Enter();
    Check(edge.Due(), "holding the top edge reveals the line");
    Check(!edge.Due(), "one hold reveals only once");
    edge.Enter();
    edge.Leave();
    Check(!edge.Due(), "leaving the top edge early cancels the reveal");
    edge.Leave();
    edge.Enter();
    edge.Enter();
    Check(edge.Pending && edge.Due() && !edge.Pending, "a second enter still waits for one hold");

    var menuHold = new MenuHold();
    Check(menuHold.AllowHide(false), "pointer outside hides when no photo menu is open");
    Check(!menuHold.AllowHide(true), "pointer inside keeps the line when no photo menu is open");
    menuHold.Opened();
    Check(menuHold.Open, "opening the photo menu holds the line");
    Check(!menuHold.AllowHide(false), "an open photo menu blocks hide when the pointer looks outside");
    Check(!menuHold.AllowHide(true), "an open photo menu blocks hide when the pointer stays inside");
    Check(!menuHold.Closed(true), "closing the menu over the rope does not arm hide");
    Check(!menuHold.Open && menuHold.AllowHide(false), "after the menu closes, a later leave can hide");
    menuHold.Opened();
    Check(menuHold.Closed(false), "closing the menu after the pointer left arms hide");
    Check(!menuHold.Open && !menuHold.AllowHide(true), "a closed menu still keeps the line while the pointer is inside");

    var fullHd125 = WorkAreaSpan.ToDip(0, 0, 1920, 1080, 1.25, 1.25);
    Check(Math.Abs(fullHd125.Width - 1536) < 1e-6 && Math.Abs(fullHd125.Height - 864) < 1e-6, "125 percent scale keeps the full work area in dips");
    Check(fullHd125.Left == 0 && fullHd125.Top == 0, "primary work area starts at the origin");
    var second = WorkAreaSpan.ToDip(1920, 0, 3840, 1080, 1, 1);
    Check(second.Left == 1920 && second.Width == 1920, "a monitor that does not start at zero keeps its origin");
    var badScale = WorkAreaSpan.ToDip(10, 20, 11, 21, 0, double.NaN);
    Check(badScale.Width == 1 && badScale.Height == 1 && badScale.Left == 10 && badScale.Top == 20, "bad dpi scale falls back to one pixel per dip");

    Check(SwingTiming.DelayMs(0, 1) == 0, "one peg starts swinging with the slide");
    Check(SwingTiming.DelayMs(-3, 4) == SwingTiming.DelayMs(0, 4), "a negative peg index starts with the first");
    int previous = -1;
    bool ordered = true;
    bool duringSlide = true;
    for (int i = 0; i < 12; i++)
    {
        int delay = SwingTiming.DelayMs(i, 12);
        if (delay < previous) ordered = false;
        if (delay < 0 || delay >= SwingTiming.SlideDownMs) duringSlide = false;
        previous = delay;
    }
    Check(ordered, "swing delays follow the peg order");
    Check(duringSlide, "every peg starts swinging before the line finishes dropping");
    Check(SwingTiming.DelayMs(11, 12) == SwingTiming.SlideDownMs - 80, "the last peg still leads the landing");

    Check(OcrActions.Portion("abcdef", 2, 3) == "cde", "copy uses the selection");
    Check(OcrActions.Portion("abcdef", 0, 0) == "abcdef", "copy uses all text when nothing is selected");
    Check(OcrActions.Portion("abcdef", -1, 2) == "abcdef", "bad selection copies all");
    Check(OcrActions.Portion("abcdef", 4, 5) == "abcdef", "selection past the end copies all");
    Check(OcrActions.Portion(null, 0, 1) == "", "null text copies empty");
    var url = OcrActions.BingSimplifiedChinese("你好 A", out bool truncated);
    Check(!truncated, "short text is not truncated");
    Check(url.StartsWith("https://www.bing.com/translator?", StringComparison.Ordinal), "translate opens bing translator");
    Check(url.Contains("from=auto", StringComparison.Ordinal) && url.Contains("to=zh-Hans", StringComparison.Ordinal), "translate targets simplified chinese");
    Check(url.Contains("text=" + Uri.EscapeDataString("你好 A"), StringComparison.Ordinal), "translate query is escaped");
    var exact = new string('字', OcrActions.TranslateLimit);
    var exactUrl = OcrActions.BingSimplifiedChinese(exact, out bool exactCut);
    Check(!exactCut && exactUrl.Contains("text=" + Uri.EscapeDataString(exact), StringComparison.Ordinal), "translate keeps exactly 1000 characters");
    var oneOver = OcrActions.BingSimplifiedChinese(exact + "多", out bool oneOverCut);
    Check(oneOverCut && oneOver.Contains("text=" + Uri.EscapeDataString(exact), StringComparison.Ordinal), "translate drops the 1001st character");
    var longText = new string('字', OcrActions.TranslateLimit + 10);
    var longUrl = OcrActions.BingSimplifiedChinese(longText, out bool cut);
    Check(cut, "long text is truncated for the translate url");
    Check(longUrl.Contains("text=" + Uri.EscapeDataString(new string('字', OcrActions.TranslateLimit)), StringComparison.Ordinal), "translate url keeps the first 1000");
    Check(!longUrl.Contains(Uri.EscapeDataString(new string('字', OcrActions.TranslateLimit + 1)), StringComparison.Ordinal), "translate url drops the rest");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

return failed == 0 ? 0 : 1;
