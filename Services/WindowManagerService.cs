using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace ztools.Services
{
    public sealed class WindowManagerService : INotifyPropertyChanged, IDisposable
    {
        // ── Singleton ────────────────────────────────────────────────────────
        public static WindowManagerService Instance { get; } = new WindowManagerService();

        // ── INotifyPropertyChanged ───────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── IsEnabled ────────────────────────────────────────────────────────
        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            private set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }

        // ── HotkeyModifier ───────────────────────────────────────────────────
        private string _hotkeyModifier = "Alt";
        public string HotkeyModifier
        {
            get => _hotkeyModifier;
            set
            {
                if (_hotkeyModifier == value) return;
                _hotkeyModifier = value;
                OnPropertyChanged(nameof(HotkeyModifier));
            }
        }

        // ── Hook ─────────────────────────────────────────────────────────────
        private IntPtr _hookHandle = IntPtr.Zero;
        private Thread? _hookThread;
        private NativeMethods.LowLevelMouseProc? _hookCallback; // field keeps delegate alive

        // ── Session ──────────────────────────────────────────────────────────
        private enum SessionKind { None, Move, Resize }
        private SessionKind _session = SessionKind.None;
        private IntPtr _targetWindow = IntPtr.Zero;
        private int _startMouseX, _startMouseY;
        private int _startWinX, _startWinY;
        private int _startWinW, _startWinH;

        // ── Preview overlay (four thin layered windows = the four border edges) ──
        // Created once on first use, hidden when not needed.
        private const int BorderPx = 3;          // visual border thickness in pixels
        private readonly IntPtr[] _borders = new IntPtr[4]; // top, bottom, left, right
        private bool _bordersCreated = false;
        private bool _bordersVisible = false;

        // SWP_HIDEWINDOW — synchronously hides without going through the message queue
        private const uint SWP_HIDEWINDOW = 0x0080;

        // Keeps the DefWindowProc delegate alive so GC cannot collect it.
        private static NativeMethods.DefWindowProcDelegate? _defWndProcDelegate;

        // ── Constructor ──────────────────────────────────────────────────────
        private WindowManagerService() { }

        // ── Public API ───────────────────────────────────────────────────────
        public void Enable()
        {
            if (_isEnabled) return;

            _hookCallback = HookCallback;
            _hookThread = new Thread(() =>
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(null);
                _hookHandle = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_MOUSE_LL, _hookCallback!, hMod, 0);

                NativeMethods.MSG msg;
                while (NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessage(ref msg);
                }
            })
            { IsBackground = true, Name = "WM-Hook" };

#pragma warning disable CA1416
            _hookThread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
            _hookThread.Start();
            IsEnabled = true;
        }

        public void Disable()
        {
            if (!_isEnabled) return;

            HideBorders();

            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }

            if (_hookThread != null)
            {
                NativeMethods.PostThreadMessage(
                    (uint)_hookThread.ManagedThreadId,
                    NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _hookThread.Join(500);
                _hookThread = null;
            }

            _hookCallback = null;
            _session = SessionKind.None;
            _targetWindow = IntPtr.Zero;
            IsEnabled = false;
        }

        public void Dispose() => Disable();

        // ── Hotkey detection (hook thread) ───────────────────────────────────
        private bool IsHotkeyDown() => _hotkeyModifier switch
        {
            "Alt" => IsVkDown(NativeMethods.VK_LMENU) || IsVkDown(NativeMethods.VK_RMENU),
            "Ctrl" => IsVkDown(NativeMethods.VK_LCONTROL) || IsVkDown(NativeMethods.VK_RCONTROL),
            "Shift" => IsVkDown(NativeMethods.VK_LSHIFT) || IsVkDown(NativeMethods.VK_RSHIFT),
            "Win" => IsVkDown(NativeMethods.VK_LWIN) || IsVkDown(NativeMethods.VK_RWIN),
            _ => false,
        };

        private static bool IsVkDown(int vk) =>
            (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

        // ── Hook callback (hook thread) ──────────────────────────────────────
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

            int msg = wParam.ToInt32();

            // If a resize session is active and the hotkey is no longer held,
            // abort the session immediately (user released the modifier key first).
            if (_session == SessionKind.Resize && !IsHotkeyDown())
            {
                HideBorders();
                _session = SessionKind.None;
                _targetWindow = IntPtr.Zero;
            }

            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                    if (IsHotkeyDown() && TryBeginSession(SessionKind.Move))
                        return new IntPtr(1);
                    // Any non-hotkey click while borders are visible → clean up.
                    HideBorders();
                    break;

                case NativeMethods.WM_RBUTTONDOWN:
                    if (IsHotkeyDown() && TryBeginSession(SessionKind.Resize))
                        return new IntPtr(1);
                    break;

                // Do NOT swallow button-up — apps need it to release mouse capture.
                case NativeMethods.WM_LBUTTONUP:
                    if (_session == SessionKind.Move)
                    {
                        HideBorders();
                        _session = SessionKind.None;
                        _targetWindow = IntPtr.Zero;
                    }
                    break;

                case NativeMethods.WM_RBUTTONUP:
                    if (_session == SessionKind.Resize)
                        EndResizeSession();
                    else
                        // Stale borders from a previous session — always clean up.
                        HideBorders();
                    break;

                // MUST NOT swallow WM_MOUSEMOVE — doing so stops the OS updating
                // the cursor position; GetCursorPos would return stale coords.
                case NativeMethods.WM_MOUSEMOVE:
                    if (_session == SessionKind.Move)
                        HandleMove();
                    else if (_session == SessionKind.Resize)
                        HandleResizePreview();
                    break;
            }

            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // ── Session begin (hook thread) ──────────────────────────────────────
        private bool TryBeginSession(SessionKind kind)
        {
            // Always clean up any leftover borders from a previous session before
            // starting a new one.  This covers the race where WM_LBUTTONDOWN
            // arrives before WM_RBUTTONUP (OS event ordering is not guaranteed
            // during rapid button transitions), so the Resize→Move switch would
            // otherwise leave the preview rectangle on screen indefinitely.
            HideBorders();

            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor)) return false;

            IntPtr hwnd = NativeMethods.WindowFromPoint(cursor);
            if (hwnd == IntPtr.Zero) return false;

            // Operate on the root top-level window, not a child control.
            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero) hwnd = root;

            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect)) return false;

            _targetWindow = hwnd;
            _startMouseX = cursor.x;
            _startMouseY = cursor.y;
            _startWinX = rect.left;
            _startWinY = rect.top;
            _startWinW = rect.right - rect.left;
            _startWinH = rect.bottom - rect.top;
            _session = kind;

            if (kind == SessionKind.Resize)
                ShowBorders(_startWinX, _startWinY, _startWinW, _startWinH);

            return true;
        }

        // ── Move (hook thread) ───────────────────────────────────────────────
        private void HandleMove()
        {
            if (_targetWindow == IntPtr.Zero) return;
            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor)) return;

            NativeMethods.SetWindowPos(
                _targetWindow, IntPtr.Zero,
                _startWinX + (cursor.x - _startMouseX),
                _startWinY + (cursor.y - _startMouseY),
                0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }

        // ── Resize preview (hook thread) ─────────────────────────────────────
        private void HandleResizePreview()
        {
            if (_targetWindow == IntPtr.Zero) return;
            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor)) return;

            int newW = Math.Max(8, _startWinW + (cursor.x - _startMouseX));
            int newH = Math.Max(8, _startWinH + (cursor.y - _startMouseY));
            ShowBorders(_startWinX, _startWinY, newW, newH);
        }

        // ── End resize (hook thread) ─────────────────────────────────────────
        private void EndResizeSession()
        {
            if (_targetWindow == IntPtr.Zero) return;

            int newW = _startWinW;
            int newH = _startWinH;
            if (NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
            {
                newW = Math.Max(8, _startWinW + (cursor.x - _startMouseX));
                newH = Math.Max(8, _startWinH + (cursor.y - _startMouseY));
            }

            HideBorders();

            NativeMethods.SetWindowPos(
                _targetWindow, IntPtr.Zero,
                0, 0, newW, newH,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);

            _session = SessionKind.None;
            _targetWindow = IntPtr.Zero;
        }

        // ── Layered border windows ────────────────────────────────────────────
        // Instead of drawing on the screen DC (which flickers with XOR), we create
        // four always-on-top transparent windows — one per edge of the preview rect.
        // Each is WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW so they are
        // invisible to the user (no taskbar entry, no click-through issues) and are
        // filled with a solid cyan colour at full opacity via UpdateLayeredWindow.
        // Moving/resizing them is a single SetWindowPos call — no repaint needed.

        private void EnsureBordersCreated()
        {
            if (_bordersCreated) return;

            // Register a minimal window class (once per process).
            // Use a static flag so multiple Enable/Disable cycles don't re-register.
            const string cls = "ZToolsPreviewBorder";
            // Obtain a stable function pointer to DefWindowProc for the WNDCLASSEX.
            // We keep it in a static field so the delegate is never GC-collected.
            if (_defWndProcDelegate == null)
                _defWndProcDelegate = NativeMethods.DefWindowProc;
            IntPtr defWndProcPtr = Marshal.GetFunctionPointerForDelegate(_defWndProcDelegate);

            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = defWndProcPtr,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = NativeMethods.GetModuleHandle(null),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = cls,
                hIconSm = IntPtr.Zero,
            };
            // RegisterClassEx returns 0 if the class is already registered — ignore.
            NativeMethods.RegisterClassEx(ref wc);

            const uint exStyle =
                NativeMethods.WS_EX_LAYERED |   // supports per-pixel alpha / colour key
                NativeMethods.WS_EX_TRANSPARENT |   // clicks fall through to windows below
                NativeMethods.WS_EX_TOOLWINDOW |   // no taskbar button
                NativeMethods.WS_EX_NOACTIVATE;    // never steals focus

            const uint style = NativeMethods.WS_POPUP;

            for (int i = 0; i < 4; i++)
            {
                _borders[i] = NativeMethods.CreateWindowEx(
                    exStyle, cls, "",
                    style,
                    0, 0, 1, 1,
                    IntPtr.Zero, IntPtr.Zero,
                    NativeMethods.GetModuleHandle(null),
                    IntPtr.Zero);

                if (_borders[i] == IntPtr.Zero) continue;

                // Paint the window solid white (RGB 255,255,255) at 220/255 opacity.
                // Colour key is not used here — we use per-window alpha.
                NativeMethods.SetLayeredWindowAttributes(
                    _borders[i],
                    0,          // crKey  — unused when LWA_ALPHA is set
                    220,        // bAlpha — 0=transparent … 255=opaque
                    NativeMethods.LWA_ALPHA);
            }

            _bordersCreated = true;
        }

        /// <summary>
        /// Positions the four border strips around the given rectangle (screen pixels)
        /// and makes them visible.  Safe to call from any thread.
        /// </summary>
        private void ShowBorders(int x, int y, int w, int h)
        {
            EnsureBordersCreated();
            if (_borders[0] == IntPtr.Zero) return;

            // HWND_TOPMOST so the preview stays above all other windows.
            IntPtr topmost = new IntPtr(-1); // HWND_TOPMOST

            const uint flags =
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_SHOWWINDOW;

            int b = BorderPx;

            // 0 — top edge
            NativeMethods.SetWindowPos(_borders[0], topmost,
                x, y,
                w, b,
                flags);

            // 1 — bottom edge
            NativeMethods.SetWindowPos(_borders[1], topmost,
                x, y + h - b,
                w, b,
                flags);

            // 2 — left edge (between top and bottom strips)
            NativeMethods.SetWindowPos(_borders[2], topmost,
                x, y + b,
                b, h - 2 * b,
                flags);

            // 3 — right edge
            NativeMethods.SetWindowPos(_borders[3], topmost,
                x + w - b, y + b,
                b, h - 2 * b,
                flags);

            _bordersVisible = true;
        }

        /// <summary>Hides the four border strips.  Safe to call from any thread.</summary>
        private void HideBorders()
        {
            // Always attempt hide even if _bordersVisible is false — guards against
            // the race where the flag was cleared but the windows are still painted.
            _bordersVisible = false;

            for (int i = 0; i < 4; i++)
            {
                if (_borders[i] == IntPtr.Zero) continue;

                // ShowWindow(SW_HIDE) is the most reliable way to hide a window.
                // The previous SWP_HIDEWINDOW path could fail silently for TOPMOST
                // layered windows when combined with SWP_NOZORDER in certain race
                // conditions (e.g. modifier released before right button, or no
                // further mouse events arriving after session end).
                NativeMethods.ShowWindow(_borders[i], NativeMethods.SW_HIDE);

                // Belt-and-suspenders: also move the window off-screen at 1×1 so
                // that even if ShowWindow is somehow deferred, the window occupies
                // no visible area. Do NOT use SWP_NOZORDER here — we intentionally
                // drop out of the TOPMOST band so a subsequent ShowBorders() call
                // that re-applies HWND_TOPMOST starts from a clean state.
                NativeMethods.SetWindowPos(
                    _borders[i], IntPtr.Zero,
                    -32000, -32000, 1, 1,
                    NativeMethods.SWP_NOACTIVATE |
                    SWP_HIDEWINDOW);
            }
        }

        // ── P/Invoke ─────────────────────────────────────────────────────────
        private static class NativeMethods
        {
            // Hook
            public const int WH_MOUSE_LL = 14;

            // Mouse messages
            public const int WM_MOUSEMOVE = 0x0200;
            public const int WM_LBUTTONDOWN = 0x0201;
            public const int WM_LBUTTONUP = 0x0202;
            public const int WM_RBUTTONDOWN = 0x0204;
            public const int WM_RBUTTONUP = 0x0205;
            public const uint WM_QUIT = 0x0012;

            // SetWindowPos flags
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOZORDER = 0x0004;
            public const uint SWP_NOACTIVATE = 0x0010;
            public const uint SWP_SHOWWINDOW = 0x0040;

            // GetAncestor
            public const uint GA_ROOT = 2;

            // Virtual key codes
            public const int VK_LSHIFT = 0xA0;
            public const int VK_RSHIFT = 0xA1;
            public const int VK_LCONTROL = 0xA2;
            public const int VK_RCONTROL = 0xA3;
            public const int VK_LMENU = 0xA4;
            public const int VK_RMENU = 0xA5;
            public const int VK_LWIN = 0x5B;
            public const int VK_RWIN = 0x5C;

            // Window styles
            public const uint WS_POPUP = 0x80000000;
            public const uint WS_EX_LAYERED = 0x00080000;
            public const uint WS_EX_TRANSPARENT = 0x00000020;
            public const uint WS_EX_TOOLWINDOW = 0x00000080;
            public const uint WS_EX_NOACTIVATE = 0x08000000;

            // SetLayeredWindowAttributes flags
            public const uint LWA_ALPHA = 0x00000002;

            // ShowWindow commands
            public const int SW_HIDE = 0;

            // ── Structs ──────────────────────────────────────────────────────

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT { public int x; public int y; }

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT { public int left, top, right, bottom; }

            [StructLayout(LayoutKind.Sequential)]
            public struct MSG
            {
                public IntPtr hwnd;
                public uint message;
                public IntPtr wParam;
                public IntPtr lParam;
                public uint time;
                public POINT pt;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            public struct WNDCLASSEX
            {
                public uint cbSize;
                public uint style;
                public IntPtr lpfnWndProc;
                public int cbClsExtra;
                public int cbWndExtra;
                public IntPtr hInstance;
                public IntPtr hIcon;
                public IntPtr hCursor;
                public IntPtr hbrBackground;
                [MarshalAs(UnmanagedType.LPTStr)]
                public string? lpszMenuName;
                [MarshalAs(UnmanagedType.LPTStr)]
                public string? lpszClassName;
                public IntPtr hIconSm;
            }

            // ── Delegates ────────────────────────────────────────────────────

            public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

            // ── Imports ──────────────────────────────────────────────────────

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(
                int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            public static extern IntPtr CallNextHookEx(
                IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern short GetAsyncKeyState(int vKey);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetCursorPos(out POINT lpPoint);

            [DllImport("user32.dll")]
            public static extern IntPtr WindowFromPoint(POINT point);

            [DllImport("user32.dll")]
            public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWindowPos(
                IntPtr hWnd, IntPtr hWndInsertAfter,
                int x, int y, int cx, int cy, uint uFlags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetLayeredWindowAttributes(
                IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

            [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

            [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            public static extern IntPtr CreateWindowEx(
                uint dwExStyle,
                string lpClassName,
                string lpWindowName,
                uint dwStyle,
                int x, int y, int nWidth, int nHeight,
                IntPtr hWndParent,
                IntPtr hMenu,
                IntPtr hInstance,
                IntPtr lpParam);

            // DefWindowProc delegate type — kept as a field to prevent GC collection.
            [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Auto)]
            public delegate IntPtr DefWindowProcDelegate(
                IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

            // DefWindowProc — used as the window procedure for the border class.
            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr DefWindowProc(
                IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern int GetMessage(
                out MSG lpMsg, IntPtr hWnd,
                uint wMsgFilterMin, uint wMsgFilterMax);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool TranslateMessage(ref MSG lpMsg);

            [DllImport("user32.dll")]
            public static extern IntPtr DispatchMessage(ref MSG lpMsg);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PostThreadMessage(
                uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr GetModuleHandle(string? lpModuleName);
        }
    }
}
