using HandheldCompanion.Functions;
using HandheldCompanion.Shared;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using WpfScreenHelper.Enum;
using static HandheldCompanion.Utils.ProcessUtils;
using static HandheldCompanion.WinAPI;
using static PInvoke.Kernel32;
using Brush = System.Windows.Media.Brush;

namespace HandheldCompanion;


public static class WinAPI
{
    private const int MONITOR_OFF = 2;

    public const int SW_MAXIMIZE = 3;


    //public enum WINDOWSTYLE : uint
    //{
    //    WS_OVERLAPPED = 0x00000000,
    //    WS_POPUP = 0x80000000,
    //    WS_CHILD = 0x40000000,
    //    WS_MINIMIZE = 0x20000000,
    //    WS_VISIBLE = 0x10000000,
    //    WS_DISABLED = 0x08000000,
    //    WS_CLIPSIBLINGS = 0x04000000,
    //    WS_CLIPCHILDREN = 0x02000000,
    //    WS_MAXIMIZE = 0x01000000,
    //    WS_BORDER = 0x00800000,
    //    WS_DLGFRAME = 0x00400000,
    //    WS_VSCROLL = 0x00200000,
    //    WS_HSCROLL = 0x00100000,
    //    WS_SYSMENU = 0x00080000,
    //    WS_THICKFRAME = 0x00040000,
    //    WS_GROUP = 0x00020000,
    //    WS_TABSTOP = 0x00010000,

    //    WS_MINIMIZEBOX = 0x00020000,
    //    WS_MAXIMIZEBOX = 0x00010000,

    //    WS_CAPTION = WS_BORDER | WS_DLGFRAME,
    //    WS_TILED = WS_OVERLAPPED,
    //    WS_ICONIC = WS_MINIMIZE,
    //    WS_SIZEBOX = WS_THICKFRAME,
    //    WS_TILEDWINDOW = WS_OVERLAPPEDWINDOW,

    //    WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX,
    //    WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU,
    //    WS_CHILDWINDOW = WS_CHILD,

    //    //Extended Window Styles

    //    WS_EX_DLGMODALFRAME = 0x00000001,
    //    WS_EX_NOPARENTNOTIFY = 0x00000004,
    //    WS_EX_TOPMOST = 0x00000008,
    //    WS_EX_ACCEPTFILES = 0x00000010,
    //    WS_EX_TRANSPARENT = 0x00000020,

    //    //#if(WINVER >= 0x0400)

    //    WS_EX_MDICHILD = 0x00000040,
    //    WS_EX_TOOLWINDOW = 0x00000080,
    //    WS_EX_WINDOWEDGE = 0x00000100,
    //    WS_EX_CLIENTEDGE = 0x00000200,
    //    WS_EX_CONTEXTHELP = 0x00000400,

    //    WS_EX_RIGHT = 0x00001000,
    //    WS_EX_LEFT = 0x00000000,
    //    WS_EX_RTLREADING = 0x00002000,
    //    WS_EX_LTRREADING = 0x00000000,
    //    WS_EX_LEFTSCROLLBAR = 0x00004000,
    //    WS_EX_RIGHTSCROLLBAR = 0x00000000,

    //    WS_EX_CONTROLPARENT = 0x00010000,
    //    WS_EX_STATICEDGE = 0x00020000,
    //    WS_EX_APPWINDOW = 0x00040000,

    //    WS_EX_OVERLAPPEDWINDOW = (WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE),
    //    WS_EX_PALETTEWINDOW = (WS_EX_WINDOWEDGE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST),

    //    //#endif /* WINVER >= 0x0400 */

    //    //#if(WIN32WINNT >= 0x0500)

    //    WS_EX_LAYERED = 0x00080000,

    //    //#endif /* WIN32WINNT >= 0x0500 */

    //    //#if(WINVER >= 0x0500)

    //    WS_EX_NOINHERITLAYOUT = 0x00100000, // Disable inheritence of mirroring by children
    //    WS_EX_LAYOUTRTL = 0x00400000, // Right to left mirroring

    //    //#endif /* WINVER >= 0x0500 */

    //    //#if(WIN32WINNT >= 0x0500)

    //    WS_EX_COMPOSITED = 0x02000000,
    //    WS_EX_NOACTIVATE = 0x08000000

    //    //#endif /* WIN32WINNT >= 0x0500 */
    //}

    //public enum GETWINDOWLONG : int
    //{
    //    GWL_STYLE = -16,
    //    GWL_EXSTYLE = -20
    //}

    //public enum SETWINDOWPOS : uint
    //{
    //    SWP_NOSIZE = 0x0001,
    //    SWP_NOMOVE = 0x0002,
    //    SWP_NOZORDER = 0x0004,
    //    SWP_NOACTIVATE = 0x0010,
    //    SWP_FRAMECHANGED = 0x0020,
    //    SWP_SHOWWINDOW = 0x0040
    //}

    //public enum SWPZORDER : int
    //{
    //    HWND_BOTTOM = 1,
    //    HWND_NOTOPMOST = -2,
    //    HWND_TOP = 0,
    //    HWND_TOPMOST = -1
    //}

    //public enum SYSCOMMAND : uint
    //{
    //    SC_SIZE = 0xF000,           // Sizes the window
    //    SC_MOVE = 0xF010,           // Moves the window
    //    SC_MINIMIZE = 0xF020,       // Minimizes the window
    //    SC_MAXIMIZE = 0xF030,       // Maximizes the window
    //    SC_NEXTWINDOW = 0xF040,     // Moves to the next window
    //    SC_PREVWINDOW = 0xF050,     // Moves to the previous window
    //    SC_CLOSE = 0xF060,          // Closes the window
    //    SC_VSCROLL = 0xF070,        // Scrolls vertically
    //    SC_HSCROLL = 0xF080,        // Scrolls horizontally
    //    SC_MOUSEMENU = 0xF090,      // Retrieves the window menu as a result of a mouse click
    //    SC_KEYMENU = 0xF100,        // Retrieves the window menu as a result of a keystroke
    //    SC_RESTORE = 0xF120,        // Restores the window to its normal position and size
    //    SC_TASKLIST = 0xF130,       // Activates the Start menu
    //    SC_SCREENSAVE = 0xF140,     // Executes the screen saver application specified in the [boot] section of the System.ini file
    //    SC_HOTKEY = 0xF150,         // Activates the window associated with the application-specified hot key
    //    SC_DEFAULT = 0xF160,        // Selects the default item; the user double-clicked the window menu
    //    SC_MONITORPOWER = 0xF170,   // Sets the state of the display (supports power-saving features)
    //    SC_CONTEXTHELP = 0xF180     // Changes the cursor to a question mark with a pointer
    //}


    //public enum WINDOWMESSAGE : uint
    //{

    //    // Window Message Constants
    //    WM_ACTIVATE = 0x0006,
    //    WM_ACTIVATEAPP = 0x001C,
    //    WM_AFXFIRST = 0x0360,
    //    WM_AFXLAST = 0x037F,
    //    WM_APP = 0x8000,
    //    WM_ASKCBFORMATNAME = 0x030C,
    //    WM_CANCELJOURNAL = 0x004B,
    //    WM_CANCELMODE = 0x001F,
    //    WM_CAPTURECHANGED = 0x0215,
    //    WM_CHANGECBCHAIN = 0x030D,
    //    WM_CHANGEUISTATE = 0x0127,
    //    WM_CHAR = 0x0102,
    //    WM_CHARTOITEM = 0x002F,
    //    WM_CHILDACTIVATE = 0x0022,
    //    WM_CLEAR = 0x0303,
    //    WM_CLOSE = 0x0010,
    //    WM_COMMAND = 0x0111,
    //    WM_COMPACTING = 0x0041,
    //    WM_COMPAREITEM = 0x0039,
    //    WM_CONTEXTMENU = 0x007B,
    //    WM_COPY = 0x0301,
    //    WM_COPYDATA = 0x004A,
    //    WM_CREATE = 0x0001,
    //    WM_CTLCOLORBTN = 0x0135,
    //    WM_CTLCOLORDLG = 0x0136,
    //    WM_CTLCOLOREDIT = 0x0133,
    //    WM_CTLCOLORLISTBOX = 0x0134,
    //    WM_CTLCOLORMSGBOX = 0x0132,
    //    WM_CTLCOLORSCROLLBAR = 0x0137,
    //    WM_CTLCOLORSTATIC = 0x0138,
    //    WM_CUT = 0x0300,
    //    WM_DEADCHAR = 0x0103,
    //    WM_DELETEITEM = 0x002D,
    //    WM_DESTROY = 0x0002,
    //    WM_DESTROYCLIPBOARD = 0x0307,
    //    WM_DEVICECHANGE = 0x0219,
    //    WM_DEVMODECHANGE = 0x001B,
    //    WM_DISPLAYCHANGE = 0x007E,
    //    WM_DRAWCLIPBOARD = 0x0308,
    //    WM_DRAWITEM = 0x002B,
    //    WM_DROPFILES = 0x0233,
    //    WM_ENABLE = 0x000A,
    //    WM_ENDSESSION = 0x0016,
    //    WM_ENTERIDLE = 0x0121,
    //    WM_ENTERMENULOOP = 0x0211,
    //    WM_ENTERSIZEMOVE = 0x0231,
    //    WM_ERASEBKGND = 0x0014,
    //    WM_EXITMENULOOP = 0x0212,
    //    WM_EXITSIZEMOVE = 0x0232,
    //    WM_FONTCHANGE = 0x001D,
    //    WM_GETDLGCODE = 0x0087,
    //    WM_GETFONT = 0x0031,
    //    WM_GETHOTKEY = 0x0033,
    //    WM_GETICON = 0x007F,
    //    WM_GETMINMAXINFO = 0x0024,
    //    WM_GETOBJECT = 0x003D,
    //    WM_GETTEXT = 0x000D,
    //    WM_GETTEXTLENGTH = 0x000E,
    //    WM_HANDHELDFIRST = 0x0358,
    //    WM_HANDHELDLAST = 0x035F,
    //    WM_HELP = 0x0053,
    //    WM_HOTKEY = 0x0312,
    //    WM_HSCROLL = 0x0114,
    //    WM_HSCROLLCLIPBOARD = 0x030E,
    //    WM_ICONERASEBKGND = 0x0027,
    //    WM_IME_CHAR = 0x0286,
    //    WM_IME_COMPOSITION = 0x010F,
    //    WM_IME_COMPOSITIONFULL = 0x0284,
    //    WM_IME_CONTROL = 0x0283,
    //    WM_IME_ENDCOMPOSITION = 0x010E,
    //    WM_IME_KEYDOWN = 0x0290,
    //    WM_IME_KEYLAST = 0x010F,
    //    WM_IME_KEYUP = 0x0291,
    //    WM_IME_NOTIFY = 0x0282,
    //    WM_IME_REQUEST = 0x0288,
    //    WM_IME_SELECT = 0x0285,
    //    WM_IME_SETCONTEXT = 0x0281,
    //    WM_IME_STARTCOMPOSITION = 0x010D,
    //    WM_INITDIALOG = 0x0110,
    //    WM_INITMENU = 0x0116,
    //    WM_INITMENUPOPUP = 0x0117,
    //    WM_INPUTLANGCHANGE = 0x0051,
    //    WM_INPUTLANGCHANGEREQUEST = 0x0050,
    //    WM_KEYDOWN = 0x0100,
    //    WM_KEYFIRST = 0x0100,
    //    WM_KEYLAST = 0x0108,
    //    WM_KEYUP = 0x0101,
    //    WM_KILLFOCUS = 0x0008,
    //    WM_LBUTTONDBLCLK = 0x0203,
    //    WM_LBUTTONDOWN = 0x0201,
    //    WM_LBUTTONUP = 0x0202,
    //    WM_MBUTTONDBLCLK = 0x0209,
    //    WM_MBUTTONDOWN = 0x0207,
    //    WM_MBUTTONUP = 0x0208,
    //    WM_MDIACTIVATE = 0x0222,
    //    WM_MDICASCADE = 0x0227,
    //    WM_MDICREATE = 0x0220,
    //    WM_MDIDESTROY = 0x0221,
    //    WM_MDIGETACTIVE = 0x0229,
    //    WM_MDIICONARRANGE = 0x0228,
    //    WM_MDIMAXIMIZE = 0x0225,
    //    WM_MDINEXT = 0x0224,
    //    WM_MDIREFRESHMENU = 0x0234,
    //    WM_MDIRESTORE = 0x0223,
    //    WM_MDISETMENU = 0x0230,
    //    WM_MDITILE = 0x0226,
    //    WM_MEASUREITEM = 0x002C,
    //    WM_MENUCHAR = 0x0120,
    //    WM_MENUCOMMAND = 0x0126,
    //    WM_MENUDRAG = 0x0123,
    //    WM_MENUGETOBJECT = 0x0124,
    //    WM_MENURBUTTONUP = 0x0122,
    //    WM_MENUSELECT = 0x011F,
    //    WM_MOUSEACTIVATE = 0x0021,
    //    WM_MOUSEFIRST = 0x0200,
    //    WM_MOUSEHOVER = 0x02A1,
    //    WM_MOUSELAST = 0x020D,
    //    WM_MOUSELEAVE = 0x02A3,
    //    WM_MOUSEMOVE = 0x0200,
    //    WM_MOUSEWHEEL = 0x020A,
    //    WM_MOUSEHWHEEL = 0x020E,
    //    WM_MOVE = 0x0003,
    //    WM_MOVING = 0x0216,
    //    WM_NCACTIVATE = 0x0086,
    //    WM_NCCALCSIZE = 0x0083,
    //    WM_NCCREATE = 0x0081,
    //    WM_NCDESTROY = 0x0082,
    //    WM_NCHITTEST = 0x0084,
    //    WM_NCLBUTTONDBLCLK = 0x00A3,
    //    WM_NCLBUTTONDOWN = 0x00A1,
    //    WM_NCLBUTTONUP = 0x00A2,
    //    WM_NCMBUTTONDBLCLK = 0x00A9,
    //    WM_NCMBUTTONDOWN = 0x00A7,
    //    WM_NCMBUTTONUP = 0x00A8,
    //    WM_NCMOUSEHOVER = 0x02A0,
    //    WM_NCMOUSELEAVE = 0x02A2,
    //    WM_NCMOUSEMOVE = 0x00A0,
    //    WM_NCPAINT = 0x0085,
    //    WM_NCRBUTTONDBLCLK = 0x00A6,
    //    WM_NCRBUTTONDOWN = 0x00A4,
    //    WM_NCRBUTTONUP = 0x00A5,
    //    WM_NCXBUTTONDBLCLK = 0x00AD,
    //    WM_NCXBUTTONDOWN = 0x00AB,
    //    WM_NCXBUTTONUP = 0x00AC,
    //    WM_NCUAHDRAWCAPTION = 0x00AE,
    //    WM_NCUAHDRAWFRAME = 0x00AF,
    //    WM_NEXTDLGCTL = 0x0028,
    //    WM_NEXTMENU = 0x0213,
    //    WM_NOTIFY = 0x004E,
    //    WM_NOTIFYFORMAT = 0x0055,
    //    WM_NULL = 0x0000,
    //    WM_PAINT = 0x000F,
    //    WM_PAINTCLIPBOARD = 0x0309,
    //    WM_PAINTICON = 0x0026,
    //    WM_PALETTECHANGED = 0x0311,
    //    WM_PALETTEISCHANGING = 0x0310,
    //    WM_PARENTNOTIFY = 0x0210,
    //    WM_PASTE = 0x0302,
    //    WM_PENWINFIRST = 0x0380,
    //    WM_PENWINLAST = 0x038F,
    //    WM_POWER = 0x0048,
    //    WM_POWERBROADCAST = 0x0218,
    //    WM_PRINT = 0x0317,
    //    WM_PRINTCLIENT = 0x0318,
    //    WM_QUERYDRAGICON = 0x0037,
    //    WM_QUERYENDSESSION = 0x0011,
    //    WM_QUERYNEWPALETTE = 0x030F,
    //    WM_QUERYOPEN = 0x0013,
    //    WM_QUEUESYNC = 0x0023,
    //    WM_QUIT = 0x0012,
    //    WM_RBUTTONDBLCLK = 0x0206,
    //    WM_RBUTTONDOWN = 0x0204,
    //    WM_RBUTTONUP = 0x0205,
    //    WM_RENDERALLFORMATS = 0x0306,
    //    WM_RENDERFORMAT = 0x0305,
    //    WM_SETCURSOR = 0x0020,
    //    WM_SETFOCUS = 0x0007,
    //    WM_SETFONT = 0x0030,
    //    WM_SETHOTKEY = 0x0032,
    //    WM_SETICON = 0x0080,
    //    WM_SETREDRAW = 0x000B,
    //    WM_SETTEXT = 0x000C,
    //    WM_SETTINGCHANGE = 0x001A,
    //    WM_SHOWWINDOW = 0x0018,
    //    WM_SIZE = 0x0005,
    //    WM_SIZECLIPBOARD = 0x030B,
    //    WM_SIZING = 0x0214,
    //    WM_SPOOLERSTATUS = 0x002A,
    //    WM_STYLECHANGED = 0x007D,
    //    WM_STYLECHANGING = 0x007C,
    //    WM_SYNCPAINT = 0x0088,
    //    WM_SYSCHAR = 0x0106,
    //    WM_SYSCOLORCHANGE = 0x0015,
    //    WM_SYSCOMMAND = 0x0112,
    //    WM_SYSDEADCHAR = 0x0107,
    //    WM_SYSKEYDOWN = 0x0104,
    //    WM_SYSKEYUP = 0x0105,
    //    WM_TCARD = 0x0052,
    //    WM_TIMECHANGE = 0x001E,
    //    WM_TIMER = 0x0113,
    //    WM_UNDO = 0x0304,
    //    WM_UNINITMENUPOPUP = 0x0125,
    //    WM_USER = 0x0400,
    //    WM_USERCHANGED = 0x0054,
    //    WM_VKEYTOITEM = 0x002E,
    //    WM_VSCROLL = 0x0115,
    //    WM_VSCROLLCLIPBOARD = 0x030A,
    //    WM_WINDOWPOSCHANGED = 0x0047,
    //    WM_WINDOWPOSCHANGING = 0x0046,
    //    WM_WININICHANGE = 0x001A,
    //    WM_XBUTTONDBLCLK = 0x020D,
    //    WM_XBUTTONDOWN = 0x020B,
    //    WM_XBUTTONUP = 0x020C,
    //}


    //public const int WS_EX_NOACTIVATE = 0x08000000;


    //// https://forum.xojo.com/t/dwmgetwindowattribute-windows-declare/86291
    //// windows enums must start with 1
    //public enum DWMWINDOWATTRIBUTE : uint
    //{
    //    DWMWA_NCRENDERING_ENABLED,
    //    DWMWA_NCRENDERING_POLICY,
    //    DWMWA_TRANSITIONS_FORCEDISABLED,
    //    DWMWA_ALLOW_NCPAINT,
    //    DWMWA_CAPTION_BUTTON_BOUNDS,
    //    DWMWA_NONCLIENT_RTL_LAYOUT,
    //    DWMWA_FORCE_ICONIC_REPRESENTATION,
    //    DWMWA_FLIP3D_POLICY,
    //    DWMWA_EXTENDED_FRAME_BOUNDS = 9,
    //    DWMWA_HAS_ICONIC_BITMAP,
    //    DWMWA_DISALLOW_PEEK,
    //    DWMWA_EXCLUDED_FROM_PEEK,
    //    DWMWA_CLOAK,
    //    /// <summary>
    //    /// cloaked := invisible
    //    /// </summary>
    //    DWMWA_CLOAKED = 14,
    //    DWMWA_FREEZE_REPRESENTATION,
    //    DWMWA_PASSIVE_UPDATE_MODE,
    //    DWMWA_USE_HOSTBACKDROPBRUSH,
    //    DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
    //    DWMWA_WINDOW_CORNER_PREFERENCE = 33,
    //    DWMWA_BORDER_COLOR,
    //    DWMWA_CAPTION_COLOR,
    //    DWMWA_TEXT_COLOR,
    //    DWMWA_VISIBLE_FRAME_BORDER_THICKNESS,
    //    DWMWA_SYSTEMBACKDROP_TYPE,
    //    DWMWA_LAST
    //}


    [DllImport("dwmapi.dll", SetLastError = true)]
    public static extern int DwmSetWindowAttribute(nint hWnd, DWMWINDOWATTRIBUTE attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", SetLastError = true)]
    public static extern int DwmGetWindowAttribute(
        nint hWnd,
        uint dwAttribute,
        nint pvAttribute,
        uint cbAttribute
    );


    [DllImport("shell32.dll", SetLastError = true)]
    public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern long Shell_NotifyIconGetRect(ref _NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern uint ExtractIconEx(string exePath, int nIconIndex, out nint iconLarge, out nint iconSmall, uint nIcons);

    [DllImport("kernel32.dll")]
    public static extern int GetProcessInformation(
        nint hProcess,
        PROCESS_INFORMATION_CLASS ProcessInformationClass,
        nint ProcessInformation,
        int ProcessInformationSize);

    [DllImport("kernel32.dll")]
    public static extern int SetProcessInformation(
        nint hProcess,
        PROCESS_INFORMATION_CLASS ProcessInformationClass,
        nint ProcessInformation,
        int ProcessInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(
        uint processAccess,
        bool bInheritHandle,
        uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowThreadProcessId(
        nint hWnd,
        out int lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int SetPriorityClass(nint hProcess, int dwPriorityClass);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern nint DeferWindowPos(nint hWinPosInfo, nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern nint BeginDeferWindowPos(int nNumWindows);

    [DllImport("user32.dll")]
    public static extern bool EndDeferWindowPos(nint hWinPosInfo);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern uint GetWindowLong(nint hWnd, int nIndex);


    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern nint GetActiveWindow();


    public struct POINTSTRUCT
    {
        public int x;

        public int y;

        public POINTSTRUCT(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public static int GetWindowProcessId(nint hWnd)
    {
        int pid;
        GetWindowThreadProcessId(hWnd, out pid);
        return pid;
    }

    public static nint GetforegroundWindow()
    {
        return GetForegroundWindow();
    }

    public static Brush BrushFromHex(string hexColorString)
    {
        if (hexColorString == "transparent")
        {
            return new SolidColorBrush(Colors.Transparent);
        }
        System.Drawing.Color color = System.Drawing.ColorTranslator.FromHtml(hexColorString);
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
    }

    public static List<string> GetStyleListFromUInt(uint styleUInt)
    {
        WINDOWSTYLE styles = (WINDOWSTYLE)styleUInt;
        List<string> styleList = new();
        foreach (WINDOWSTYLE style in Enum.GetValues(typeof(WINDOWSTYLE)))
        {
            if (styles.HasFlag(style))
            {
                styleList.Add(style.ToString());
            }
        }
        return styleList;
    }

    public static List<string> GetStylesFromHwnd(nint hWnd)
    {
        uint stylesUInt = GetWindowLong(hWnd, (int)GETWINDOWLONG.GWL_STYLE);
        //Logger.Log($"GetStylesFromHwnd(): {Marshal.GetLastWin32Error()}");
        return GetStyleListFromUInt(stylesUInt);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(nint hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool IsProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern nint MonitorFromRect(ref RECT lprc, MonitorOptions dwFlags);

    [DllImport("shcore.dll", CharSet = CharSet.Auto)]
    public static extern nint GetDpiForMonitor([In] nint hmonitor, [In] DpiType dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern nint MonitorFromPoint(POINTSTRUCT pt, MonitorDefault flags);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    private const int CS_DROPSHADOW = 0x00020000;

    [DllImport("user32.dll")]
    public static extern int SetClassLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern int GetClassLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(nint hwnd);


    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern uint FormatMessage(uint dwFlags, nint lpSource, uint dwMessageId, uint dwLanguageId, out string lpBuffer, uint nSize, nint Arguments);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int SendMessage(nint hWnd, uint hMsg, nint wParam, nint lParam);


    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();



    [Flags]
    public enum MonitorOptions : uint
    {
        MONITOR_DEFAULTTONULL = 0x00000000,
        MONITOR_DEFAULTTOPRIMARY = 0x00000001,
        MONITOR_DEFAULTTONEAREST = 0x00000002
    }

    public enum DpiType
    {
        EFFECTIVE = 0,
        ANGULAR = 1,
        RAW = 2,
        DEFAULT
    }

    public enum MonitorDefault
    {
        MONITOR_DEFAULTTONEAREST = 2,
        MONITOR_DEFAULTTONULL = 0,
        MONITOR_DEFAULTTOPRIMARY = 1
    }

    public static nint GetScreenHandle(Screen screen)
    {
        RECT rect = new RECT(screen.Bounds);
        nint hMonitor = MonitorFromRect(ref rect, MonitorOptions.MONITOR_DEFAULTTONEAREST);
        return hMonitor;
    }

    public static void LockScreen()
    {
        LockWorkStation();
    }


    public static void TurnOffScreen()
    {
        Form f = new Form();
        nint result = SendMessage(f.Handle, (uint)WINDOWMESSAGE.WM_SYSCOMMAND, (int)SYSCOMMAND.SC_MONITORPOWER, MONITOR_OFF);

        if (result == nint.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            string message = "";
            uint formatFlags = 0x00001000 | 0x00000200 | 0x00000100 | 0x00000080;
            uint formatResult = FormatMessage(formatFlags, nint.Zero, (uint)error, 0, out message, 0, nint.Zero);
            if (formatResult == 0)
            {
                message = "Unknown error.";
            }
            LogManager.LogError($"Failed to turn off screen. Error code: {error}. {message}");
        }
    }

    public static void MakeBorderless(nint hWnd, bool IsBorderless)
    {
        var exStyles = GetWindowLong(hWnd, (int)GETWINDOWLONG.GWL_STYLE);

        if (IsBorderless)
        {
            // Remove the border, caption, and system menu styles
            var newStyles = exStyles & ~((uint)WINDOWSTYLE.WS_BORDER | (uint)WINDOWSTYLE.WS_CAPTION | (uint)WINDOWSTYLE.WS_SYSMENU | (uint)WINDOWSTYLE.WS_THICKFRAME | (uint)WINDOWSTYLE.WS_MINIMIZEBOX | (uint)WINDOWSTYLE.WS_MAXIMIZEBOX);
            SetWindowLong(hWnd, (int)GETWINDOWLONG.GWL_STYLE, (int)newStyles);
        }
        else
        {
            // Restore the border, caption, and system menu styles
            var newStyle = exStyles | (uint)WINDOWSTYLE.WS_BORDER | (uint)WINDOWSTYLE.WS_CAPTION | (uint)WINDOWSTYLE.WS_SYSMENU | (uint)WINDOWSTYLE.WS_THICKFRAME | (uint)WINDOWSTYLE.WS_MINIMIZEBOX | (uint)WINDOWSTYLE.WS_MAXIMIZEBOX;
            SetWindowLong(hWnd, (int)GETWINDOWLONG.GWL_STYLE, (int)newStyle);
        }
    }

    public static void MoveWindow(nint hWnd, Screen targetScreen, WindowPositions position)
    {
        if (hWnd == nint.Zero)
            return;

        // get current screen
        Screen currentScreen = Screen.FromHandle(hWnd);
        if ((targetScreen is null || currentScreen.DeviceName.Equals(targetScreen.DeviceName)) && position == WindowPositions.Center)
            return;

        if (targetScreen is null)
            targetScreen = currentScreen;

        // WpfScreenHelper.Screen WpfScreen = WpfScreenHelper.Screen.AllScreens.FirstOrDefault(s => s.DeviceName.Equals(targetScreen.DeviceName));
        // nint monitor = GetScreenHandle(targetScreen);
        // double taskbarHeight = SystemParameters.MaximizedPrimaryScreenHeight - SystemParameters.FullPrimaryScreenHeight;
        Rectangle workingArea = targetScreen.WorkingArea;

        double newWidth = workingArea.Width;
        double newHeight = workingArea.Height;
        double newX = 0;
        double newY = 0;

        switch (position)
        {
            case WindowPositions.Left:
                newWidth /= 2;
                newX = workingArea.Left;
                newY = workingArea.Top;
                break;
            case WindowPositions.Top:
                newHeight /= 2;
                newX = workingArea.Left;
                newY = workingArea.Top;
                break;
            case WindowPositions.Right:
                newWidth /= 2;
                newX = workingArea.Right - newWidth;
                newY = workingArea.Top;
                break;
            case WindowPositions.Bottom:
                newHeight /= 2;
                newX = workingArea.Left;
                newY = workingArea.Top + newHeight;
                break;
            case WindowPositions.TopLeft:
                newWidth /= 2;
                newHeight /= 2;
                newX = workingArea.Left;
                newY = workingArea.Top;
                break;
            case WindowPositions.TopRight:
                newWidth /= 2;
                newHeight /= 2;
                newX = workingArea.Right - newWidth;
                newY = workingArea.Top;
                break;
            case WindowPositions.BottomRight:
                newWidth /= 2;
                newHeight /= 2;
                newX = workingArea.Right - newWidth;
                newY = workingArea.Bottom - newHeight;
                break;
            case WindowPositions.BottomLeft:
                newWidth /= 2;
                newHeight /= 2;
                newX = workingArea.Left;
                newY = workingArea.Bottom - newHeight;
                break;
            default:
            case WindowPositions.Maximize:
                newX = workingArea.Left;
                newY = workingArea.Top;
                break;
        }

        ShowWindow(hWnd, 9);
        MoveWindow(hWnd, (int)newX, (int)newY, (int)newWidth, (int)newHeight, true);

        if (position == WindowPositions.Maximize)
            ShowWindow(hWnd, 3);
    }
}