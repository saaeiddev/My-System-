using System.Runtime.InteropServices;

namespace MySystem.App.Services;
/// <summary>Native notification icon, global overlay hotkey, and window state notifications.</summary>
public sealed class TrayService : IDisposable
{
    readonly nint hwnd;
    readonly Action show, quit, overlay, minimized;
    readonly SubclassProc callback;
    NotifyIconData data;
    bool started, ownsIcon;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    public bool Available { get; private set; }

    public TrayService(nint hwnd, Action show, Action quit, Action overlay, Action minimized)
    {
        this.hwnd = hwnd;
        this.show = show;
        this.quit = quit;
        this.overlay = overlay;
        this.minimized = minimized;
        callback = WndProc;
    }

    public void Start()
    {
        if (started)
            return;
        started = true;
        data = new()
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = hwnd,
            Id = 1,
            Flags = 7,
            Callback = 0x8001,
            Tip = "My System — real hardware monitoring",
            Icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "MySystem.ico"), 1, 32, 32, 0x10),
            Info = "",
            InfoTitle = ""
        };
        ownsIcon = data.Icon != 0;
        if (data.Icon == 0)
            data.Icon = LoadIcon(0, 32512);
        Available = Shell_NotifyIcon(0, ref data);
        SetWindowSubclass(hwnd, callback, 1, 0);
        RegisterHotKey(hwnd, 0x4D59, 0x4003, 0x4D);
    }

    public void Notify(string title, string message)
    {
        data.Flags = 0x10;
        data.InfoTitle = title.Length > 63 ? title[..63] : title;
        data.Info = message.Length > 255 ? message[..255] : message;
        data.InfoFlags = 1;
        Shell_NotifyIcon(1, ref data);
    }

    nint WndProc(nint h, uint message, nuint w, nint l, nuint id, nuint reference)
    {
        if (message == taskbarCreated)
        {
            data.Flags = 7;
            Available = Shell_NotifyIcon(0, ref data);
            return 0;
        }

        if (message == 0x8001)
        {
            int e = (int)l & 0xffff;
            if (e == 0x0203 || e == 0x0202)
                show();
            if (e == 0x0205)
            {
                var menu = CreatePopupMenu();
                AppendMenu(menu, 0, 1, "Open My System");
                AppendMenu(menu, 0, 2, "Toggle overlay");
                AppendMenu(menu, 0, 3, "Exit");
                GetCursorPos(out var p);
                SetForegroundWindow(hwnd);
                uint selected = TrackPopupMenu(menu, 0x100 | 2, p.X, p.Y, 0, hwnd, 0);
                DestroyMenu(menu);
                if (selected == 1)
                    show();
                if (selected == 2)
                    overlay();
                if (selected == 3)
                    quit();
            }

            return 0;
        }

        if (message == 0x0312 && (int)w == 0x4D59)
        {
            overlay();
            return 0;
        }

        if (message == 5 && (int)w == 1)
            minimized();
        return DefSubclassProc(h, message, w, l);
    }

    public void Dispose()
    {
        if (!started)
            return;
        started = false;
        UnregisterHotKey(hwnd, 0x4D59);
        RemoveWindowSubclass(hwnd, callback, 1);
        Shell_NotifyIcon(2, ref data);
        if (ownsIcon && data.Icon != 0)
            DestroyIcon(data.Icon);
    }

    delegate nint SubclassProc(nint h, uint m, nuint w, nint l, nuint id, nuint reference);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("comctl32.dll")]
    static extern bool SetWindowSubclass(nint h, SubclassProc p, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    static extern bool RemoveWindowSubclass(nint h, SubclassProc p, nuint id);
    [DllImport("comctl32.dll")]
    static extern nint DefSubclassProc(nint h, uint m, nuint w, nint l);
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(nint h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(nint h, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint LoadImage(nint h, string path, uint type, int x, int y, uint flags);
    [DllImport("user32.dll")]
    static extern nint LoadIcon(nint h, nint resource);
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]
    static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")]
    static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(nint hwnd);
}
