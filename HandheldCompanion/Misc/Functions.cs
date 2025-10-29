﻿using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace HandheldCompanion.Functions;

public class User32
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int MessageBox(nint hWnd, string title, string message, uint type);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowLong(nint hWnd, GETWINDOWLONG nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, SETWINDOWPOS uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint FindWindow(string className, string windowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int ShowWindow(nint hWnd, SHOWWINDOW nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint FindWindowEx(nint hWndParent, nint hWndChildAfter, string className, string windowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int ShowWindowAsync(nint hWnd, SHOWWINDOW nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT cursorPos);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint hWnd, out RECT windowRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetClientRect(nint hWnd, out RECT clientRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT Point);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowProc enumWindowProc, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumChildWindows(nint hWndParent, EnumWindowProc enumWindowProc, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint CreateWindowEx(
        WINDOWSTYLE dwExStyle,
        string lpClassName,
        string lpWindowName,
        WINDOWSTYLE dwStyle,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam
    );

    [DllImport("user32.dll", SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterClass(string className, nint hInstance);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GetModuleHandle(string moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint DefWindowProc(nint hWnd, WINDOWMESSAGE uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int DestroyWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessage(out MSG msg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DispatchMessage(ref MSG msg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint RegisterWindowMessage(string message);

    /// <summary>
    /// Use when msg is not in WINDOWMESSAGE enum, like in situations where a new boradcast
    /// message has to be sent
    /// </summary>
    /// <param name="hWnd"></param>
    /// <param name="msg"></param>
    /// <param name="wParam"></param>
    /// <param name="lParam"></param>
    /// <returns></returns>

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SendNotifyMessage(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam
    );

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SendMessage(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam
    );

    [DllImport("user32.dll", SetLastError = true)]
    public static extern void SetTimer(nint hWnd, nint nIdEvent, uint uElapse, TIMERPROC timerProc);

    [DllImport("user32.dll", SetLastError = true)]
    public extern static nint GetAncestor(
      nint hwnd,
      uint gaFlags
    );

    [DllImport("user32.dll", SetLastError = true)]
    public extern static nint GetLastActivePopup(
        nint hWnd
    );

    [DllImport("user32.dll", SetLastError = true)]
    public extern static bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public extern static int AnimateWindow(nint hWnd, uint dwTime, ANIMATEWINDOW dwFlags);

    // return hMonitor or the monitor handle

    [DllImport("user32.dll", SetLastError = true)]
    public extern static nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public extern static nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetParent(nint hWndChild, nint hWndNewParent);
}

public class Shell32
{
    [DllImport("shell32.dll", SetLastError = true)]
    public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern long Shell_NotifyIconGetRect(ref _NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern uint ExtractIconEx(string exePath, int nIconIndex, out nint iconLarge, out nint iconSmall, uint nIcons);
}

public class Kernel32
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll")]
    public static extern uint GetLogicalDriveStringsW(
      uint nBufferLength,
      StringBuilder lpBuffer
    );

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint QueryDosDevice(
      string lpDeviceName,
      StringBuilder lpTargetPath,
      uint ucchMax
    );
}

public class Dwmapi
{
    [DllImport("dwmapi.dll", SetLastError = true)]
    public static extern int DwmSetWindowAttribute(nint hWnd, DWMWINDOWATTRIBUTE attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", SetLastError = true)]
    public static extern int DwmGetWindowAttribute(
        nint hWnd,
        uint dwAttribute,
        nint pvAttribute,
        uint cbAttribute
    );
}

public class Psapi
{
    [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetModuleFileNameEx(nint hProcess, nint hModule, out StringBuilder moduleFileName, uint nSize);
}

/// <summary>
/// Query kernel objects
/// </summary>
public class Ntdll
{
    [DllImport("ntdll.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int NtQuerySystemInformation(SYSTEM_INFORMATION_CLASS infoType, ref SYSTEM_PROCESS_ID_INFORMATION info, uint infoLength, out uint returnLength);

    [DllImport("ntdll.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int NtQuerySystemInformation(SYSTEM_INFORMATION_CLASS infoType, ref SYSTEM_BASIC_INFORMATION info, uint infoLength, out uint returnLength);

    [DllImport("ntdll.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int NtQuerySystemInformation(SYSTEM_INFORMATION_CLASS infoType, nint info, uint infoLength, out uint returnLength);

}

public class Shcore
{
    // retrieves monitor scaling info
    // MONITOR_DEFAULTTONULL
    // 0x00000000
    // Returns NULL.
    // MONITOR_DEFAULTTOPRIMARY
    // 0x00000001
    // Returns a handle to the primary display monitor.
    // MONITOR_DEFAULTTONEAREST
    // 0x00000002
    // Returns a handle to the display monitor that is nearest to the point.
    [DllImport("shcore.dll", SetLastError = true)]
    public static extern int GetScaleFactorForMonitor(nint hMon, out DEVICE_SCALE_FACTOR scaleFactor);

    [DllImport("shcore.dll", SetLastError = true)]
    public static extern int GetDpiForMonitor(nint hMon, MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY);

    // call this with PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE
    // so that all functions like GetSystemMetric() and GetDpiForMonitor()
    // returns the actual/correct values
    [DllImport("shcore.dll", SetLastError = true)]
    public static extern int SetProcessDpiAwareness(PROCESS_DPI_AWARENESS value);
}

public class Iphlpapi
{
    /// <summary>
    /// https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getipnetworkconnectionbandwidthestimates
    /// </summary>
    /// <param name="interfaceIndex"></param>
    /// <param name="adressFamily"></param>
    /// <param name="info"></param>
    /// <returns></returns>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern int GetIpNetworkConnectionBandwidthEstimates(int interfaceIndex, ADRESS_FAMILY adressFamily, out _MIB_IP_NETWORK_CONNECTION_BANDWIDTH_ESTIMATES info);
}

public enum WINDOWSTYLE : uint
{
    WS_OVERLAPPED = 0x00000000,
    WS_POPUP = 0x80000000,
    WS_CHILD = 0x40000000,
    WS_MINIMIZE = 0x20000000,
    WS_VISIBLE = 0x10000000,
    WS_DISABLED = 0x08000000,
    WS_CLIPSIBLINGS = 0x04000000,
    WS_CLIPCHILDREN = 0x02000000,
    WS_MAXIMIZE = 0x01000000,
    WS_BORDER = 0x00800000,
    WS_DLGFRAME = 0x00400000,
    WS_VSCROLL = 0x00200000,
    WS_HSCROLL = 0x00100000,
    WS_SYSMENU = 0x00080000,
    WS_THICKFRAME = 0x00040000,
    WS_GROUP = 0x00020000,
    WS_TABSTOP = 0x00010000,

    WS_MINIMIZEBOX = 0x00020000,
    WS_MAXIMIZEBOX = 0x00010000,

    WS_CAPTION = WS_BORDER | WS_DLGFRAME,
    WS_TILED = WS_OVERLAPPED,
    WS_ICONIC = WS_MINIMIZE,
    WS_SIZEBOX = WS_THICKFRAME,
    WS_TILEDWINDOW = WS_OVERLAPPEDWINDOW,

    WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX,
    WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU,
    WS_CHILDWINDOW = WS_CHILD,

    //Extended Window Styles

    WS_EX_DLGMODALFRAME = 0x00000001,
    WS_EX_NOPARENTNOTIFY = 0x00000004,
    WS_EX_TOPMOST = 0x00000008,
    WS_EX_ACCEPTFILES = 0x00000010,
    WS_EX_TRANSPARENT = 0x00000020,

    //#if(WINVER >= 0x0400)

    WS_EX_MDICHILD = 0x00000040,
    WS_EX_TOOLWINDOW = 0x00000080,
    WS_EX_WINDOWEDGE = 0x00000100,
    WS_EX_CLIENTEDGE = 0x00000200,
    WS_EX_CONTEXTHELP = 0x00000400,

    WS_EX_RIGHT = 0x00001000,
    WS_EX_LEFT = 0x00000000,
    WS_EX_RTLREADING = 0x00002000,
    WS_EX_LTRREADING = 0x00000000,
    WS_EX_LEFTSCROLLBAR = 0x00004000,
    WS_EX_RIGHTSCROLLBAR = 0x00000000,

    WS_EX_CONTROLPARENT = 0x00010000,
    WS_EX_STATICEDGE = 0x00020000,
    WS_EX_APPWINDOW = 0x00040000,

    WS_EX_OVERLAPPEDWINDOW = (WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE),
    WS_EX_PALETTEWINDOW = (WS_EX_WINDOWEDGE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST),

    //#endif /* WINVER >= 0x0400 */

    //#if(WIN32WINNT >= 0x0500)

    WS_EX_LAYERED = 0x00080000,

    //#endif /* WIN32WINNT >= 0x0500 */

    //#if(WINVER >= 0x0500)

    WS_EX_NOINHERITLAYOUT = 0x00100000, // Disable inheritence of mirroring by children
    WS_EX_LAYOUTRTL = 0x00400000, // Right to left mirroring

    //#endif /* WINVER >= 0x0500 */

    //#if(WIN32WINNT >= 0x0500)

    WS_EX_COMPOSITED = 0x02000000,
    WS_EX_NOACTIVATE = 0x08000000

    //#endif /* WIN32WINNT >= 0x0500 */
}

// https://forum.xojo.com/t/dwmgetwindowattribute-windows-declare/86291
// windows enums must start with 1
public enum DWMWINDOWATTRIBUTE : uint
{
    DWMWA_NCRENDERING_ENABLED,
    DWMWA_NCRENDERING_POLICY,
    DWMWA_TRANSITIONS_FORCEDISABLED,
    DWMWA_ALLOW_NCPAINT,
    DWMWA_CAPTION_BUTTON_BOUNDS,
    DWMWA_NONCLIENT_RTL_LAYOUT,
    DWMWA_FORCE_ICONIC_REPRESENTATION,
    DWMWA_FLIP3D_POLICY,
    DWMWA_EXTENDED_FRAME_BOUNDS = 9,
    DWMWA_HAS_ICONIC_BITMAP,
    DWMWA_DISALLOW_PEEK,
    DWMWA_EXCLUDED_FROM_PEEK,
    DWMWA_CLOAK,
    /// <summary>
    /// cloaked := invisible
    /// </summary>
    DWMWA_CLOAKED = 14,
    DWMWA_FREEZE_REPRESENTATION,
    DWMWA_PASSIVE_UPDATE_MODE,
    DWMWA_USE_HOSTBACKDROPBRUSH,
    DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
    DWMWA_WINDOW_CORNER_PREFERENCE = 33,
    DWMWA_BORDER_COLOR,
    DWMWA_CAPTION_COLOR,
    DWMWA_TEXT_COLOR,
    DWMWA_VISIBLE_FRAME_BORDER_THICKNESS,
    DWMWA_SYSTEMBACKDROP_TYPE,
    DWMWA_LAST
}

public enum DWM_CLOAK_STATE
{
    DWM_CLOAKED_APP = 0x00000001,
    DWM_CLOAKED_SHELL = 0x00000002,
    DWM_CLOAKED_INHERITED = 0x00000004
}

public enum DWM_WINDOW_CORNER_PREFERENCE
{
    DWMWCP_DEFAULT = 0,
    DWMWCP_DONOTROUND = 1,
    DWMWCP_ROUND = 2,
    DWMWCP_ROUNDSMALL = 3
}

public enum APPBARMESSAGE
{
    New = 0x00,
    Remove = 0x01,
    QueryPos = 0x02,
    SetPos = 0x03,
    GetState = 0x04,
    GetTaskBarPos = 0x05,
    Activate = 0x06,
    GetAutoHideBar = 0x07,
    SetAutoHideBar = 0x08,
    WindowPosChanged = 0x09,
    SetState = 0x0a
}

public enum APPBARNOTIFY : int
{
    ABN_STATECHANGE = 0,
    ABN_POSCHANGED,
    ABN_FULLSCREENAPP,
    ABN_WINDOWARRANGE
}

public enum APPBARSTATE
{
    AutoHide = 0x01,
    AlwaysOnTop = 0x02
}

public enum SHOWWINDOW
{
    SW_HIDE = 0,
    SW_SHOWNORMAL = 1,
    SW_NORMAL = 1,
    SW_SHOWMINIMIZED = 2,
    SW_SHOWMAXIMIZED = 3,
    SW_MAXIMIZE = 3,
    SW_SHOWNOACTIVATE = 4,
    SW_SHOW = 5,
    SW_MINIMIZE = 6,
    SW_SHOWMINNOACTIVE = 7,
    SW_SHOWNA = 8,
    SW_RESTORE = 9,
    SW_SHOWDEFAULT = 10,
    SW_FORCEMINIMIZE = 11
}

public enum SETWINDOWPOS : uint
{
    SWP_NOSIZE = 0x0001,
    SWP_NOMOVE = 0x0002,
    SWP_NOACTIVATE = 0x0010
}

public enum SWPZORDER : int
{
    HWND_BOTTOM = 1,
    HWND_NOTOPMOST = -2,
    HWND_TOP = 0,
    HWND_TOPMOST = -1
}

public enum GETWINDOWLONG : int
{
    GWL_STYLE = -16,
    GWL_EXSTYLE = -20
}

public enum WINDOWMESSAGE : uint
{

    // Window Message Constants
    WM_ACTIVATE = 0x0006,
    WM_ACTIVATEAPP = 0x001C,
    WM_AFXFIRST = 0x0360,
    WM_AFXLAST = 0x037F,
    WM_APP = 0x8000,
    WM_ASKCBFORMATNAME = 0x030C,
    WM_CANCELJOURNAL = 0x004B,
    WM_CANCELMODE = 0x001F,
    WM_CAPTURECHANGED = 0x0215,
    WM_CHANGECBCHAIN = 0x030D,
    WM_CHANGEUISTATE = 0x0127,
    WM_CHAR = 0x0102,
    WM_CHARTOITEM = 0x002F,
    WM_CHILDACTIVATE = 0x0022,
    WM_CLEAR = 0x0303,
    WM_CLOSE = 0x0010,
    WM_COMMAND = 0x0111,
    WM_COMPACTING = 0x0041,
    WM_COMPAREITEM = 0x0039,
    WM_CONTEXTMENU = 0x007B,
    WM_COPY = 0x0301,
    WM_COPYDATA = 0x004A,
    WM_CREATE = 0x0001,
    WM_CTLCOLORBTN = 0x0135,
    WM_CTLCOLORDLG = 0x0136,
    WM_CTLCOLOREDIT = 0x0133,
    WM_CTLCOLORLISTBOX = 0x0134,
    WM_CTLCOLORMSGBOX = 0x0132,
    WM_CTLCOLORSCROLLBAR = 0x0137,
    WM_CTLCOLORSTATIC = 0x0138,
    WM_CUT = 0x0300,
    WM_DEADCHAR = 0x0103,
    WM_DELETEITEM = 0x002D,
    WM_DESTROY = 0x0002,
    WM_DESTROYCLIPBOARD = 0x0307,
    WM_DEVICECHANGE = 0x0219,
    WM_DEVMODECHANGE = 0x001B,
    WM_DISPLAYCHANGE = 0x007E,
    WM_DRAWCLIPBOARD = 0x0308,
    WM_DRAWITEM = 0x002B,
    WM_DROPFILES = 0x0233,
    WM_ENABLE = 0x000A,
    WM_ENDSESSION = 0x0016,
    WM_ENTERIDLE = 0x0121,
    WM_ENTERMENULOOP = 0x0211,
    WM_ENTERSIZEMOVE = 0x0231,
    WM_ERASEBKGND = 0x0014,
    WM_EXITMENULOOP = 0x0212,
    WM_EXITSIZEMOVE = 0x0232,
    WM_FONTCHANGE = 0x001D,
    WM_GETDLGCODE = 0x0087,
    WM_GETFONT = 0x0031,
    WM_GETHOTKEY = 0x0033,
    WM_GETICON = 0x007F,
    WM_GETMINMAXINFO = 0x0024,
    WM_GETOBJECT = 0x003D,
    WM_GETTEXT = 0x000D,
    WM_GETTEXTLENGTH = 0x000E,
    WM_HANDHELDFIRST = 0x0358,
    WM_HANDHELDLAST = 0x035F,
    WM_HELP = 0x0053,
    WM_HOTKEY = 0x0312,
    WM_HSCROLL = 0x0114,
    WM_HSCROLLCLIPBOARD = 0x030E,
    WM_ICONERASEBKGND = 0x0027,
    WM_IME_CHAR = 0x0286,
    WM_IME_COMPOSITION = 0x010F,
    WM_IME_COMPOSITIONFULL = 0x0284,
    WM_IME_CONTROL = 0x0283,
    WM_IME_ENDCOMPOSITION = 0x010E,
    WM_IME_KEYDOWN = 0x0290,
    WM_IME_KEYLAST = 0x010F,
    WM_IME_KEYUP = 0x0291,
    WM_IME_NOTIFY = 0x0282,
    WM_IME_REQUEST = 0x0288,
    WM_IME_SELECT = 0x0285,
    WM_IME_SETCONTEXT = 0x0281,
    WM_IME_STARTCOMPOSITION = 0x010D,
    WM_INITDIALOG = 0x0110,
    WM_INITMENU = 0x0116,
    WM_INITMENUPOPUP = 0x0117,
    WM_INPUTLANGCHANGE = 0x0051,
    WM_INPUTLANGCHANGEREQUEST = 0x0050,
    WM_KEYDOWN = 0x0100,
    WM_KEYFIRST = 0x0100,
    WM_KEYLAST = 0x0108,
    WM_KEYUP = 0x0101,
    WM_KILLFOCUS = 0x0008,
    WM_LBUTTONDBLCLK = 0x0203,
    WM_LBUTTONDOWN = 0x0201,
    WM_LBUTTONUP = 0x0202,
    WM_MBUTTONDBLCLK = 0x0209,
    WM_MBUTTONDOWN = 0x0207,
    WM_MBUTTONUP = 0x0208,
    WM_MDIACTIVATE = 0x0222,
    WM_MDICASCADE = 0x0227,
    WM_MDICREATE = 0x0220,
    WM_MDIDESTROY = 0x0221,
    WM_MDIGETACTIVE = 0x0229,
    WM_MDIICONARRANGE = 0x0228,
    WM_MDIMAXIMIZE = 0x0225,
    WM_MDINEXT = 0x0224,
    WM_MDIREFRESHMENU = 0x0234,
    WM_MDIRESTORE = 0x0223,
    WM_MDISETMENU = 0x0230,
    WM_MDITILE = 0x0226,
    WM_MEASUREITEM = 0x002C,
    WM_MENUCHAR = 0x0120,
    WM_MENUCOMMAND = 0x0126,
    WM_MENUDRAG = 0x0123,
    WM_MENUGETOBJECT = 0x0124,
    WM_MENURBUTTONUP = 0x0122,
    WM_MENUSELECT = 0x011F,
    WM_MOUSEACTIVATE = 0x0021,
    WM_MOUSEFIRST = 0x0200,
    WM_MOUSEHOVER = 0x02A1,
    WM_MOUSELAST = 0x020D,
    WM_MOUSELEAVE = 0x02A3,
    WM_MOUSEMOVE = 0x0200,
    WM_MOUSEWHEEL = 0x020A,
    WM_MOUSEHWHEEL = 0x020E,
    WM_MOVE = 0x0003,
    WM_MOVING = 0x0216,
    WM_NCACTIVATE = 0x0086,
    WM_NCCALCSIZE = 0x0083,
    WM_NCCREATE = 0x0081,
    WM_NCDESTROY = 0x0082,
    WM_NCHITTEST = 0x0084,
    WM_NCLBUTTONDBLCLK = 0x00A3,
    WM_NCLBUTTONDOWN = 0x00A1,
    WM_NCLBUTTONUP = 0x00A2,
    WM_NCMBUTTONDBLCLK = 0x00A9,
    WM_NCMBUTTONDOWN = 0x00A7,
    WM_NCMBUTTONUP = 0x00A8,
    WM_NCMOUSEHOVER = 0x02A0,
    WM_NCMOUSELEAVE = 0x02A2,
    WM_NCMOUSEMOVE = 0x00A0,
    WM_NCPAINT = 0x0085,
    WM_NCRBUTTONDBLCLK = 0x00A6,
    WM_NCRBUTTONDOWN = 0x00A4,
    WM_NCRBUTTONUP = 0x00A5,
    WM_NCXBUTTONDBLCLK = 0x00AD,
    WM_NCXBUTTONDOWN = 0x00AB,
    WM_NCXBUTTONUP = 0x00AC,
    WM_NCUAHDRAWCAPTION = 0x00AE,
    WM_NCUAHDRAWFRAME = 0x00AF,
    WM_NEXTDLGCTL = 0x0028,
    WM_NEXTMENU = 0x0213,
    WM_NOTIFY = 0x004E,
    WM_NOTIFYFORMAT = 0x0055,
    WM_NULL = 0x0000,
    WM_PAINT = 0x000F,
    WM_PAINTCLIPBOARD = 0x0309,
    WM_PAINTICON = 0x0026,
    WM_PALETTECHANGED = 0x0311,
    WM_PALETTEISCHANGING = 0x0310,
    WM_PARENTNOTIFY = 0x0210,
    WM_PASTE = 0x0302,
    WM_PENWINFIRST = 0x0380,
    WM_PENWINLAST = 0x038F,
    WM_POWER = 0x0048,
    WM_POWERBROADCAST = 0x0218,
    WM_PRINT = 0x0317,
    WM_PRINTCLIENT = 0x0318,
    WM_QUERYDRAGICON = 0x0037,
    WM_QUERYENDSESSION = 0x0011,
    WM_QUERYNEWPALETTE = 0x030F,
    WM_QUERYOPEN = 0x0013,
    WM_QUEUESYNC = 0x0023,
    WM_QUIT = 0x0012,
    WM_RBUTTONDBLCLK = 0x0206,
    WM_RBUTTONDOWN = 0x0204,
    WM_RBUTTONUP = 0x0205,
    WM_RENDERALLFORMATS = 0x0306,
    WM_RENDERFORMAT = 0x0305,
    WM_SETCURSOR = 0x0020,
    WM_SETFOCUS = 0x0007,
    WM_SETFONT = 0x0030,
    WM_SETHOTKEY = 0x0032,
    WM_SETICON = 0x0080,
    WM_SETREDRAW = 0x000B,
    WM_SETTEXT = 0x000C,
    WM_SETTINGCHANGE = 0x001A,
    WM_SHOWWINDOW = 0x0018,
    WM_SIZE = 0x0005,
    WM_SIZECLIPBOARD = 0x030B,
    WM_SIZING = 0x0214,
    WM_SPOOLERSTATUS = 0x002A,
    WM_STYLECHANGED = 0x007D,
    WM_STYLECHANGING = 0x007C,
    WM_SYNCPAINT = 0x0088,
    WM_SYSCHAR = 0x0106,
    WM_SYSCOLORCHANGE = 0x0015,
    WM_SYSCOMMAND = 0x0112,
    WM_SYSDEADCHAR = 0x0107,
    WM_SYSKEYDOWN = 0x0104,
    WM_SYSKEYUP = 0x0105,
    WM_TCARD = 0x0052,
    WM_TIMECHANGE = 0x001E,
    WM_TIMER = 0x0113,
    WM_UNDO = 0x0304,
    WM_UNINITMENUPOPUP = 0x0125,
    WM_USER = 0x0400,
    WM_USERCHANGED = 0x0054,
    WM_VKEYTOITEM = 0x002E,
    WM_VSCROLL = 0x0115,
    WM_VSCROLLCLIPBOARD = 0x030A,
    WM_WINDOWPOSCHANGED = 0x0047,
    WM_WINDOWPOSCHANGING = 0x0046,
    WM_WININICHANGE = 0x001A,
    WM_XBUTTONDBLCLK = 0x020D,
    WM_XBUTTONDOWN = 0x020B,
    WM_XBUTTONUP = 0x020C,
}

public enum MOUSEACTIVATE : uint
{
    MA_ACTIVATE = 0x0001,
    MA_ACTIVATEANDEAT = 0x0002,
    MA_NOACTIVATE = 0x0003,
    MA_NOACTIVATEANDEAT = 0x0004
}

/// <summary>
/// WM_COPYDATA message sent to Taskbar [Shell_TrayWnd] carries data
/// in its lpData field which is one of the three 
/// different types identified by its dwData field
/// </summary>
public enum SHELLTRAYMESSAGE : int
{
    // Resolve to SHELLTRAYDATA struct to use data for ICONUPDATE
    ICONUPDATE = 1,
    APPBAR = 2,
    TRAYICONPOSITION = 3
}

public enum ICONUPDATEACTION : uint
{
    NIM_ADD = 0x00000000,
    NIM_MODIFY = 0x00000001,
    NIM_DELETE = 0x00000002,
    NIM_SETFOCUS = 0x00000003,
    NIM_SETVERSION = 0x00000004
}

public enum NOTIFYICONDATAVALIDITY : uint
{
    NIF_MESSAGE = 0x00000001,
    NIF_ICON = 0x00000002,
    NIF_TIP = 0x00000004,
    NIF_STATE = 0x00000008,
    NIF_INFO = 0x00000010,
    NIF_GUID = 0x00000020,
    NIF_REALTIME = 0x00000040,
    NIF_SHOWTIP = 0x00000080
}

public enum SYSCOMMAND : uint
{
    SC_SIZE = 0xF000,           // Sizes the window
    SC_MOVE = 0xF010,           // Moves the window
    SC_MINIMIZE = 0xF020,       // Minimizes the window
    SC_MAXIMIZE = 0xF030,       // Maximizes the window
    SC_NEXTWINDOW = 0xF040,     // Moves to the next window
    SC_PREVWINDOW = 0xF050,     // Moves to the previous window
    SC_CLOSE = 0xF060,          // Closes the window
    SC_VSCROLL = 0xF070,        // Scrolls vertically
    SC_HSCROLL = 0xF080,        // Scrolls horizontally
    SC_MOUSEMENU = 0xF090,      // Retrieves the window menu as a result of a mouse click
    SC_KEYMENU = 0xF100,        // Retrieves the window menu as a result of a keystroke
    SC_RESTORE = 0xF120,        // Restores the window to its normal position and size
    SC_TASKLIST = 0xF130,       // Activates the Start menu
    SC_SCREENSAVE = 0xF140,     // Executes the screen saver application specified in the [boot] section of the System.ini file
    SC_HOTKEY = 0xF150,         // Activates the window associated with the application-specified hot key
    SC_DEFAULT = 0xF160,        // Selects the default item; the user double-clicked the window menu
    SC_MONITORPOWER = 0xF170,   // Sets the state of the display (supports power-saving features)
    SC_CONTEXTHELP = 0xF180     // Changes the cursor to a question mark with a pointer
}

public enum ANIMATEWINDOW : uint
{
    AW_HOR_POSITIVE = 0x00000001,
    AW_HOR_NEGATIVE = 0x00000002,
    AW_VER_POSITIVE = 0x00000004,
    AW_VER_NEGATIVE = 0x00000008,
    AW_CENTER = 0x00000010,
    AW_HIDE = 0x00010000,
    AW_ACTIVATE = 0x00020000,
    AW_SLIDE = 0x00040000,
    AW_BLEND = 0x00080000
}

/// <summary>
/// System Information Class enumeration for NtQuerySystemInformation
/// Based on Windows NT kernel system information classes
/// </summary>
public enum SYSTEM_INFORMATION_CLASS : uint
{
    SystemBasicInformation = 0x00,
    SystemProcessorInformation = 0x01,
    SystemPerformanceInformation = 0x02,
    SystemTimeOfDayInformation = 0x03,
    SystemPathInformation = 0x04,
    SystemProcessInformation = 0x05,
    SystemCallCountInformation = 0x06,
    SystemDeviceInformation = 0x07,
    SystemProcessorPerformanceInformation = 0x08,
    SystemFlagsInformation = 0x09,
    SystemCallTimeInformation = 0x0A,
    SystemModuleInformation = 0x0B,
    SystemLocksInformation = 0x0C,
    SystemStackTraceInformation = 0x0D,
    SystemPagedPoolInformation = 0x0E,
    SystemNonPagedPoolInformation = 0x0F,
    SystemHandleInformation = 0x10,
    SystemObjectInformation = 0x11,
    SystemPageFileInformation = 0x12,
    SystemVdmInstemulInformation = 0x13,
    SystemVdmBopInformation = 0x14,
    SystemFileCacheInformation = 0x15,
    SystemPoolTagInformation = 0x16,
    SystemInterruptInformation = 0x17,
    SystemDpcBehaviorInformation = 0x18,
    SystemFullMemoryInformation = 0x19,
    SystemLoadGdiDriverInformation = 0x1A,
    SystemUnloadGdiDriverInformation = 0x1B,
    SystemTimeAdjustmentInformation = 0x1C,
    SystemSummaryMemoryInformation = 0x1D,
    SystemMirrorMemoryInformation = 0x1E,
    SystemPerformanceTraceInformation = 0x1F,
    SystemObsolete0 = 0x20,
    SystemExceptionInformation = 0x21,
    SystemCrashDumpStateInformation = 0x22,
    SystemKernelDebuggerInformation = 0x23,
    SystemContextSwitchInformation = 0x24,
    SystemRegistryQuotaInformation = 0x25,
    SystemExtendServiceTableInformation = 0x26,
    SystemPrioritySeperation = 0x27,
    SystemVerifierAddDriverInformation = 0x28,
    SystemVerifierRemoveDriverInformation = 0x29,
    SystemProcessorIdleInformation = 0x2A,
    SystemLegacyDriverInformation = 0x2B,
    SystemCurrentTimeZoneInformation = 0x2C,
    SystemLookasideInformation = 0x2D,
    SystemTimeSlipNotification = 0x2E,
    SystemSessionCreate = 0x2F,
    SystemSessionDetach = 0x30,
    SystemSessionInformation = 0x31,
    SystemRangeStartInformation = 0x32,
    SystemVerifierInformation = 0x33,
    SystemVerifierThunkExtend = 0x34,
    SystemSessionProcessInformation = 0x35,
    SystemLoadGdiDriverInSystemSpace = 0x36,
    SystemNumaProcessorMap = 0x37,
    SystemPrefetcherInformation = 0x38,
    SystemExtendedProcessInformation = 0x39,
    SystemRecommendedSharedDataAlignment = 0x3A,
    SystemComPlusPackage = 0x3B,
    SystemNumaAvailableMemory = 0x3C,
    SystemProcessorPowerInformation = 0x3D,
    SystemEmulationBasicInformation = 0x3E,
    SystemEmulationProcessorInformation = 0x3F,
    SystemExtendedHandleInformation = 0x40,
    SystemLostDelayedWriteInformation = 0x41,
    SystemBigPoolInformation = 0x42,
    SystemSessionPoolTagInformation = 0x43,
    SystemSessionMappedViewInformation = 0x44,
    SystemHotpatchInformation = 0x45,
    SystemObjectSecurityMode = 0x46,
    SystemWatchdogTimerHandler = 0x47,
    SystemWatchdogTimerInformation = 0x48,
    SystemLogicalProcessorInformation = 0x49,
    SystemWow64SharedInformationObsolete = 0x4A,
    SystemRegisterFirmwareTableInformationHandler = 0x4B,
    SystemFirmwareTableInformation = 0x4C,
    SystemModuleInformationEx = 0x4D,
    SystemVerifierTriageInformation = 0x4E,
    SystemSuperfetchInformation = 0x4F,
    SystemMemoryListInformation = 0x50,
    SystemFileCacheInformationEx = 0x51,
    SystemThreadPriorityClientIdInformation = 0x52,
    SystemProcessorIdleCycleTimeInformation = 0x53,
    SystemVerifierCancellationInformation = 0x54,
    SystemProcessorPowerInformationEx = 0x55,
    SystemRefTraceInformation = 0x56,
    SystemSpecialPoolInformation = 0x57,
    SystemProcessIdInformation = 0x58,
    SystemErrorPortInformation = 0x59,
    SystemBootEnvironmentInformation = 0x5A,
    SystemHypervisorInformation = 0x5B,
    SystemVerifierInformationEx = 0x5C,
    SystemTimeZoneInformation = 0x5D,
    SystemImageFileExecutionOptionsInformation = 0x5E,
    SystemCoverageInformation = 0x5F,
    SystemPrefetchPatchInformation = 0x60,
    SystemVerifierFaultsInformation = 0x61,
    SystemSystemPartitionInformation = 0x62,
    SystemSystemDiskInformation = 0x63,
    SystemProcessorPerformanceDistribution = 0x64,
    SystemNumaProximityNodeInformation = 0x65,
    SystemDynamicTimeZoneInformation = 0x66,
    SystemCodeIntegrityInformation = 0x67,
    SystemProcessorMicrocodeUpdateInformation = 0x68,
    SystemProcessorBrandString = 0x69,
    SystemVirtualAddressInformation = 0x6A,
    SystemLogicalProcessorAndGroupInformation = 0x6B,
    SystemProcessorCycleTimeInformation = 0x6C,
    SystemStoreInformation = 0x6D,
    SystemRegistryAppendString = 0x6E,
    SystemAitSamplingValue = 0x6F,
    SystemVhdBootInformation = 0x70,
    SystemCpuQuotaInformation = 0x71,
    SystemNativeBasicInformation = 0x72,
    SystemErrorPortTimeouts = 0x73,
    SystemLowPriorityIoInformation = 0x74,
    SystemBootEntropyInformation = 0x75,
    SystemVerifierCountersInformation = 0x76,
    SystemPagedPoolInformationEx = 0x77,
    SystemSystemPtesInformationEx = 0x78,
    SystemNodeDistanceInformation = 0x79,
    SystemAcpiAuditInformation = 0x7A,
    SystemBasicPerformanceInformation = 0x7B,
    SystemQueryPerformanceCounterInformation = 0x7C,
    SystemSessionBigPoolInformation = 0x7D,
    SystemBootGraphicsInformation = 0x7E,
    SystemScrubPhysicalMemoryInformation = 0x7F,
    SystemBadPageInformation = 0x80,
    SystemProcessorProfileControlArea = 0x81,
    SystemCombinePhysicalMemoryInformation = 0x82,
    SystemEntropyInterruptTimingInformation = 0x83,
    SystemConsoleInformation = 0x84,
    SystemPlatformBinaryInformation = 0x85,
    SystemPolicyInformation = 0x86,
    SystemHypervisorProcessorCountInformation = 0x87,
    SystemDeviceDataInformation = 0x88,
    SystemDeviceDataEnumerationInformation = 0x89,
    SystemMemoryTopologyInformation = 0x8A,
    SystemMemoryChannelInformation = 0x8B,
    SystemBootLogoInformation = 0x8C,
    SystemProcessorPerformanceInformationEx = 0x8D,
    SystemCriticalProcessErrorLogInformation = 0x8E,
    SystemSecureBootPolicyInformation = 0x8F,
    SystemPageFileInformationEx = 0x90,
    SystemSecureBootInformation = 0x91,
    SystemEntropyInterruptTimingRawInformation = 0x92,
    SystemPortableWorkspaceEfiLauncherInformation = 0x93,
    SystemFullProcessInformation = 0x94,
    SystemKernelDebuggerInformationEx = 0x95,
    SystemBootMetadataInformation = 0x96,
    SystemSoftRebootInformation = 0x97,
    SystemElamCertificateInformation = 0x98,
    SystemOfflineDumpConfigInformation = 0x99,
    SystemProcessorFeaturesInformation = 0x9A,
    SystemRegistryReconciliationInformation = 0x9B,
    SystemEdidInformation = 0x9C,
    SystemManufacturingInformation = 0x9D,
    SystemEnergyEstimationConfigInformation = 0x9E,
    SystemHypervisorDetailInformation = 0x9F,
    SystemProcessorCycleStatsInformation = 0xA0,
    SystemVmGenerationCountInformation = 0xA1,
    SystemTrustedPlatformModuleInformation = 0xA2,
    SystemKernelDebuggerFlags = 0xA3,
    SystemCodeIntegrityPolicyInformation = 0xA4,
    SystemIsolatedUserModeInformation = 0xA5,
    SystemHardwareSecurityTestInterfaceResultsInformation = 0xA6,
    SystemSingleModuleInformation = 0xA7,
    SystemAllowedCpuSetsInformation = 0xA8,
    SystemDmaProtectionInformation = 0xA9,
    SystemInterruptCpuSetsInformation = 0xAA,
    SystemSecureBootPolicyFullInformation = 0xAB,
    SystemCodeIntegrityPolicyFullInformation = 0xAC,
    SystemAffinitizedInterruptProcessorInformation = 0xAD,
    SystemRootSiloInformation = 0xAE,
    SystemCpuSetInformation = 0xAF,
    SystemCpuSetTagInformation = 0xB0,
    SystemWin32WerStartCallout = 0xB1,
    SystemSecureKernelProfileInformation = 0xB2,
    SystemCodeIntegrityPlatformManifestInformation = 0xB3,
    SystemInterruptSteeringInformation = 0xB4,
    SystemSuppportedProcessorArchitectures = 0xB5,
    SystemMemoryUsageInformation = 0xB6,
    SystemCodeIntegrityCertificateInformation = 0xB7,
    SystemPhysicalMemoryInformation = 0xB8,
    SystemControlFlowTransition = 0xB9,
    SystemKernelDebuggingAllowed = 0xBA,
    SystemActivityModerationExeState = 0xBB,
    SystemActivityModerationUserSettings = 0xBC,
    SystemCodeIntegrityPoliciesFullInformation = 0xBD,
    SystemCodeIntegrityUnlockInformation = 0xBE,
    SystemIntegrityQuotaInformation = 0xBF,
    SystemFlushInformation = 0xC0,
    SystemProcessorIdleMaskInformation = 0xC1,
    SystemSecureDumpEncryptionInformation = 0xC2,
    SystemWriteConstraintInformation = 0xC3,
    SystemKernelVaShadowInformation = 0xC4,
    SystemHypervisorSharedPageInformation = 0xC5,
    SystemFirmwareBootPerformanceInformation = 0xC6,
    SystemCodeIntegrityVerificationInformation = 0xC7,
    SystemFirmwarePartitionInformation = 0xC8,
    SystemSpeculationControlInformation = 0xC9,
    SystemDmaGuardPolicyInformation = 0xCA,
    SystemEnclaveLaunchControlInformation = 0xCB,
    SystemWorkloadAllowedCpuSetsInformation = 0xCC,
    SystemCodeIntegrityUnlockModeInformation = 0xCD,
    SystemLeapSecondInformation = 0xCE,
    SystemFlags2Information = 0xCF,
    SystemSecurityModelInformation = 0xD0,
    SystemCodeIntegritySyntheticCacheInformation = 0xD1,
    SystemFeatureConfigurationInformation = 0xD2,
    SystemFeatureConfigurationSectionInformation = 0xD3,
    SystemFeatureUsageSubscriptionInformation = 0xD4,
    SystemSecureSpeculationControlInformation = 0xD5,
    MaxSystemInfoClass = 0xD6
}

public enum ADRESS_FAMILY : uint
{
    AF_INET = 2,
    AF_INET6 = 23
}

/// <summary>
/// Used by GetScaleFactorForMonitor()
/// </summary>
public enum DEVICE_SCALE_FACTOR : uint
{
    DEVICE_SCALE_FACTOR_INVALID = 0,
    SCALE_100_PERCENT = 100,
    SCALE_120_PERCENT = 120,
    SCALE_125_PERCENT = 125,
    SCALE_140_PERCENT = 140,
    SCALE_150_PERCENT = 150,
    SCALE_160_PERCENT = 160,
    SCALE_175_PERCENT = 175,
    SCALE_180_PERCENT = 180,
    SCALE_200_PERCENT = 200,
    SCALE_225_PERCENT = 225,
    SCALE_250_PERCENT = 250,
    SCALE_300_PERCENT = 300,
    SCALE_350_PERCENT = 350,
    SCALE_400_PERCENT = 400,
    SCALE_450_PERCENT = 450,
    SCALE_500_PERCENT = 500
}

public enum MONITOR_DPI_TYPE
{
    MDT_EFFECTIVE_DPI = 0,
    MDT_ANGULAR_DPI = 1,
    MDT_RAW_DPI = 2,
    MDT_DEFAULT
}

public enum PROCESS_DPI_AWARENESS
{
    PROCESS_DPI_UNAWARE = 0,
    PROCESS_SYSTEM_DPI_AWARE = 1,
    PROCESS_PER_MONITOR_DPI_AWARE = 2
}


[Flags]
public enum PriorityClass : uint
{
    ABOVE_NORMAL_PRIORITY_CLASS = 0x8000,
    BELOW_NORMAL_PRIORITY_CLASS = 0x4000,
    HIGH_PRIORITY_CLASS = 0x80,
    IDLE_PRIORITY_CLASS = 0x40,
    NORMAL_PRIORITY_CLASS = 0x20,
    PROCESS_MODE_BACKGROUND_BEGIN = 0x100000,
    PROCESS_MODE_BACKGROUND_END = 0x200000,
    REALTIME_PRIORITY_CLASS = 0x100
}

[Flags]
public enum ProcessAccessFlags : uint
{
    All = 0x001F0FFF,
    Terminate = 0x00000001,
    CreateThread = 0x00000002,
    VirtualMemoryOperation = 0x00000008,
    VirtualMemoryRead = 0x00000010,
    VirtualMemoryWrite = 0x00000020,
    DuplicateHandle = 0x00000040,
    CreateProcess = 0x000000080,
    SetQuota = 0x00000100,
    SetInformation = 0x00000200,
    QueryInformation = 0x00000400,
    QueryLimitedInformation = 0x00001000,
    Synchronize = 0x00100000
}


/// <summary>
/// DWORD := uint
/// HWND  := nint
/// PVOID := nint
/// </summary>
/// ------------------------

/// <summary>
/// For controlling the visibility and autohide behaviours of the taksbar
/// </summary>

[StructLayout(LayoutKind.Sequential)]
public struct APPBARDATA
{
    public uint cbSize;
    public nint hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public uint lParam;
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
    public RECT(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public RECT(Rectangle rect) : this(rect.Left, rect.Top, rect.Right, rect.Bottom)
    {
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct WINDOWPLACEMENT
{
    public uint length;
    public uint flags;
    public uint showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
    public RECT rcDevice;
}

/// <summary>
/// struct that applications use to query its tray icon information using
/// Shell_NotifyIcon() and Shell_NotifyIconGetRect(), these functions would then send
/// another internal struct [_NOTIFYICONIDENTIFIERINTERNAL] containing additional items 
/// to Shell_TrayWnd
/// </summary>

[StructLayout(LayoutKind.Sequential)]
public struct _NOTIFYICONIDENTIFIER
{
    public uint cbSize;
    public nint hWnd;
    public uint UID;
    public Guid guidItem;
}

[StructLayout(LayoutKind.Sequential)]
public struct _NOTIFYICONIDENTIFIERINTERNAL
{
    //--------------------
    public int magicNumber;
    public int msg;
    //---------------------
    public int callbackSize;
    //---------------------
    public int padding;
    //---------------------
    public nint hWnd;
    public uint UID;
    public Guid guidItem;
}

[StructLayout(LayoutKind.Sequential)]
public struct WNDCLASSEX
{
    public uint cbSize;
    public uint style;
    public WNDPROC lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCurosr;
    public nint hbrBackground;
    public string lpszMenuName;
    public string lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
public struct COPYDATASTRUCT
{
    public ulong dwData;
    public ulong cbData;
    public nint lpData;
}

[StructLayout(LayoutKind.Explicit)]
public struct TIMEOUTVERSIONUNION
{
    [FieldOffset(0)]
    public uint uTimeout;
    [FieldOffset(0)]
    public uint uVersion;
}

/// <summary>
/// Very delicate struct, you might also notice that hWnd is an uint instead of the usual nint
/// (IntPtr)
/// </summary>

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NOTIFYICONDATA
{
    public uint cbSize;
    /// <summary>
    /// Window handle of the message processing window for the tray icon. This is NOT
    /// the handle to the actual icon's window, the actual icon might not even have a window
    /// to begin with (which is the case with XAML elements)
    /// </summary>
    public uint hWnd;
    public uint uID;
    public uint uFlags;
    /// <summary>
    /// SendMessage(hWnd, uCallbackMessage, ..., ...) 
    /// Wait what ? ......^...
    /// isnt it supposed to be a window message defined in WINDOWMESSAGE such as WM_CONTEXTMENU 
    /// or WM_RIGHTBUTTONDOWN ? well the actual window the gets the WM_RIGHTBUTTONDOWN when 
    /// the icon is rightclicked is the window hoisting the icon TopLevelXamlOverflowWindow
    /// or even Shell_TrayWnd. It then requests the message processing window of the icon (window with handle hWnd)
    /// for a context menu.
    /// </summary>
    public uint uCallbackMessage;
    public uint hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
    public uint dwState;
    public uint dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;
    public TIMEOUTVERSIONUNION uTimeoutOrVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public uint hBalloonIcon;
}

/// <summary>
/// Message Type recieved or send to Taskbar [Shell_TrayWnd]
/// during the WM_COPYDATA event
/// </summary>

[StructLayout(LayoutKind.Sequential)]
public struct SHELLTRAYICONUPDATEDATA
{
    public int dwHz;
    public uint dwMessage;
    public NOTIFYICONDATA nid;
}

/// <summary>
/// Win32 basic window message type used by SendMessage(), GetMessage(), TranslateMessage()
/// DispatchMessage() etc
/// </summary>

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public nint hwnd;
    public WINDOWMESSAGE message;
    public nint wParam;
    public nint lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
public struct UNICODE_STRING
{
    public ushort Length;
    public ushort MaximumLength;
    public nint Buffer;
}

/// <summary>
/// Used by NtQuerySystemInformation in ntdll to query process module paths without 
/// elevated priveleges. Part of the undocumented windows api
/// SYSTEM_INFORMATION_CLASS.SystemProcessIdInformation
/// </summary>

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEM_PROCESS_ID_INFORMATION
{
    public nint ProcessId;
    public UNICODE_STRING ImageName;
}

/// <summary>
/// NtQuerySystemInformation() can use it for basic querrying
/// used with SYSTEM_INFORMATION_CLASS.SystemBasicInformation
/// </summary>

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEM_BASIC_INFORMATION
{
    public uint Reserved;
    public uint TimerResolution;
    public uint PageSize;
    public uint NumberOfPhysicalPages;
    public uint LowestPhysicalPageNumber;
    public uint HighestPhysicalPageNumber;
    public uint AllocationGranularity;
    public UIntPtr MinimumUserModeAddress;
    public UIntPtr MaximumUserModeAddress;
    public UIntPtr ActiveProcessorsAffinityMask;
    public byte NumberOfProcessors;
}

/// <summary>
/// Used by SYSTEM_INFORMATION_CLASS.SystemProcessorPerformanceInformation
/// </summary>

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
{
    public long IdleTime;
    public long KernelTime;
    public long UserTime;
    public long DpcTime;
    public long InterruptTime;
    public uint Reserved2;
}

[StructLayout(LayoutKind.Sequential)]
public struct _NL_BANDWIDTH_INFORMATION
{
    public ulong Bandwidth;
    public ulong Instability;
    public byte BandwidthPeaked;
}

/// <summary>
/// https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_ip_network_connection_bandwidth_estimates
/// </summary>

[StructLayout(LayoutKind.Sequential)]
public struct _MIB_IP_NETWORK_CONNECTION_BANDWIDTH_ESTIMATES
{
    public _NL_BANDWIDTH_INFORMATION InboundBandwidthInformation;
    public _NL_BANDWIDTH_INFORMATION OutboundBandwidthInformation;
}

/// <summary>
/// used by SYSTEM_INFORMATION_CLASS.SystemMemoryUsageInformation
/// https://ntdoc.m417z.com/system_memory_usage_information
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct _SYSTEM_MEMORY_USAGE_INFORMATION
{
    public ulong TotalPhysicalBytes;
    public ulong AvailableBytes;
    public long ResidentAvailableBytes;
    public ulong CommittedBytes;
    public long SharedCommittedBytes;
    public long CommitLimitBytes;
    public long PeakCommitmentBytes;
}

