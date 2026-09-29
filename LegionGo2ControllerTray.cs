using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed class ControllerBridge : IDisposable
{
    private const uint BrokenShortcutMask = 0x40000800;
    private const int MinimumToggleIntervalMs = 750;
    private const string DrawerApplication = "LegionSettingMenu.exe";
    private const string DrawerToggleMessage =
        "{\"msgType\":0,\"languageValue\":2,\"businessName\":0," +
        "\"businessValue\":\"0\",\"businessValue2\":\"\"}";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ButtonCallback(uint code);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool RegisterDelegate(ButtonCallback callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SendMessageDelegate(IntPtr application, IntPtr message);

    private readonly ConcurrentQueue<uint> _events = new ConcurrentQueue<uint>();
    private readonly AutoResetEvent _eventReady = new AutoResetEvent(false);
    private readonly object _logLock = new object();
    private readonly ButtonCallback _callback;
    private readonly string _logPath;

    private IntPtr _controllerModule;
    private IntPtr _settingModule;
    private FreeDelegate _free;
    private SendMessageDelegate _send;
    private Thread _worker;
    private string _drawerExecutable;
    private volatile bool _stopping;
    private volatile string _status = "Starting";

    public ControllerBridge()
    {
        _callback = OnButton;
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray.log");
    }

    public string Status
    {
        get { return _status; }
    }

    public string LogPath
    {
        get { return _logPath; }
    }

    public void Start()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Lenovo", "LegionSpace");
        string controllerPath = Directory.GetFiles(
                root, "LEGOKZHandle.dll", SearchOption.AllDirectories)
            .Where(path => path.IndexOf("\\SapientiaUsb\\", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        string settingPath = Directory.GetFiles(
                root, "LegionSetting.dll", SearchOption.AllDirectories)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        _drawerExecutable = Directory.GetFiles(
                root, "LegionSettingMenu.exe", SearchOption.AllDirectories)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (String.IsNullOrEmpty(controllerPath) || String.IsNullOrEmpty(settingPath) ||
            String.IsNullOrEmpty(_drawerExecutable))
            throw new FileNotFoundException("The installed Lenovo controller components were not found.");

        SetDllDirectory(Path.GetDirectoryName(controllerPath));
        _controllerModule = LoadLibrary(controllerPath);
        if (_controllerModule == IntPtr.Zero)
            throw new InvalidOperationException(
                "Controller LoadLibrary failed: " + Marshal.GetLastWin32Error());

        SetDllDirectory(Path.GetDirectoryName(settingPath));
        _settingModule = LoadLibrary(settingPath);
        if (_settingModule == IntPtr.Zero)
            throw new InvalidOperationException(
                "Setting LoadLibrary failed: " + Marshal.GetLastWin32Error());

        InitDelegate init = GetDelegate<InitDelegate>(_controllerModule, "Init");
        RegisterDelegate register = GetDelegate<RegisterDelegate>(
            _controllerModule, "SetTestButtonBackFunc");
        _free = GetDelegate<FreeDelegate>(_controllerModule, "FreeSapientiaUsb");
        _send = GetDelegate<SendMessageDelegate>(_settingModule, "SendMsg2App");

        int initialized = init();
        bool registered = register(_callback);
        if (initialized != 1 || !registered)
            throw new InvalidOperationException(String.Format(
                "Controller initialization failed: INIT={0} CALLBACK={1}",
                initialized, registered));

        _status = "Controller active";
        Log("READY INIT={0} CALLBACK={1} PID={2}",
            initialized, registered, Process.GetCurrentProcess().Id);
        _worker = new Thread(WorkerLoop);
        _worker.IsBackground = true;
        _worker.Name = "Legion drawer recovery";
        _worker.Start();
    }

    public void RequestToggle()
    {
        _events.Enqueue(BrokenShortcutMask);
        _eventReady.Set();
    }

    private void OnButton(uint code)
    {
        _events.Enqueue(code);
        _eventReady.Set();
    }

    private void WorkerLoop()
    {
        long lastToggleTicks = 0;
        while (!_stopping)
        {
            _eventReady.WaitOne(1000);
            uint code;
            while (_events.TryDequeue(out code))
            {
                if ((code & BrokenShortcutMask) != BrokenShortcutMask)
                    continue;

                long now = DateTime.UtcNow.Ticks;
                if (lastToggleTicks != 0 &&
                    TimeSpan.FromTicks(now - lastToggleTicks).TotalMilliseconds < MinimumToggleIntervalMs)
                    continue;

                lastToggleTicks = now;
                try
                {
                    Log("FOREGROUND T=0 {0}", DescribeForegroundWindow());
                    bool sent = SendDrawerMessage();
                    _status = sent ? "Controller active" : "Drawer IPC unavailable";
                    Log("RECOVERY CODE=0x{0:X8} SENT={1}", code, sent);
                    int elapsed = 0;
                    int[] samples = { 50, 150, 300, 600 };
                    foreach (int sample in samples)
                    {
                        Thread.Sleep(sample - elapsed);
                        elapsed = sample;
                        Log("FOREGROUND T={0} {1}", sample, DescribeForegroundWindow());
                    }
                }
                catch (Exception exception)
                {
                    _status = "Recovery error";
                    Log("RECOVERY_ERROR {0}", exception);
                }
            }
        }
    }

    private bool SendDrawerMessage()
    {
        if (TrySendDrawerMessage())
            return true;

        if (Process.GetProcessesByName("LegionSettingMenu").Length == 0)
        {
            Process.Start(new ProcessStartInfo(_drawerExecutable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(_drawerExecutable),
                CreateNoWindow = true
            });
            Log("DRAWER_PROCESS_STARTED");
        }

        for (int attempt = 1; attempt <= 10; attempt++)
        {
            Thread.Sleep(200);
            if (TrySendDrawerMessage())
            {
                Log("IPC_RETRY_SUCCESS ATTEMPT={0}", attempt);
                return true;
            }
        }
        return false;
    }

    private bool TrySendDrawerMessage()
    {
        IntPtr application = Marshal.StringToCoTaskMemUni(DrawerApplication);
        IntPtr message = Marshal.StringToCoTaskMemUni(DrawerToggleMessage);
        try
        {
            return _send(application, message);
        }
        finally
        {
            Marshal.FreeCoTaskMem(application);
            Marshal.FreeCoTaskMem(message);
        }
    }

    private void Log(string format, params object[] values)
    {
        string line = DateTime.Now.ToString("O") + " " + String.Format(format, values);
        lock (_logLock)
            File.AppendAllText(_logPath, line + Environment.NewLine);
    }

    private static string DescribeForegroundWindow()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return "HWND=0";

        var className = new StringBuilder(256);
        var title = new StringBuilder(256);
        GetClassName(window, className, className.Capacity);
        GetWindowText(window, title, title.Capacity);
        uint processId;
        GetWindowThreadProcessId(window, out processId);
        string processName = "unknown";
        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
        }
        return String.Format(
            "HWND=0x{0:X} PID={1} PROCESS={2} CLASS={3} TITLE={4}",
            window.ToInt64(), processId, processName, className, title);
    }

    private static T GetDelegate<T>(IntPtr module, string name) where T : class
    {
        IntPtr address = GetProcAddress(module, name);
        if (address == IntPtr.Zero)
            throw new EntryPointNotFoundException(name);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
    }

    public void Dispose()
    {
        _stopping = true;
        _eventReady.Set();
        if (_worker != null)
            _worker.Join(2000);
        if (_free != null)
            _free();
        if (_settingModule != IntPtr.Zero)
            FreeLibrary(_settingModule);
        if (_controllerModule != IntPtr.Zero)
            FreeLibrary(_controllerModule);
        GC.KeepAlive(_callback);
        _eventReady.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string fileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string pathName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ControllerBridge _bridge;
    private readonly NotifyIcon _icon;
    private readonly Icon _applicationIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly System.Windows.Forms.Timer _timer;

    public TrayApplicationContext()
    {
        _bridge = new ControllerBridge();
        _statusItem = new ToolStripMenuItem("Starting...");
        _statusItem.Enabled = false;

        var toggleItem = new ToolStripMenuItem("Toggle side drawer");
        toggleItem.Click += delegate { _bridge.RequestToggle(); };
        var logItem = new ToolStripMenuItem("Open log");
        logItem.Click += delegate { Process.Start("notepad.exe", _bridge.LogPath); };
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += delegate { ExitThread(); };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(toggleItem);
        menu.Items.Add(logItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "controller-bridge.ico");
        _applicationIcon = File.Exists(iconPath)
            ? new Icon(iconPath)
            : Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _icon = new NotifyIcon();
        _icon.Icon = _applicationIcon;
        _icon.Text = "Legion Go 2 Controller Bridge";
        _icon.ContextMenuStrip = menu;
        _icon.Visible = true;
        _icon.DoubleClick += delegate { _bridge.RequestToggle(); };

        try
        {
            _bridge.Start();
            _icon.ShowBalloonTip(
                2000, "Legion Go 2 Controller Bridge", "Controller bridge is active.",
                ToolTipIcon.Info);
        }
        catch (Exception exception)
        {
            _statusItem.Text = "Initialization failed";
            _icon.ShowBalloonTip(
                5000, "Controller bridge failed", exception.Message, ToolTipIcon.Error);
        }

        _timer = new System.Windows.Forms.Timer();
        _timer.Interval = 1000;
        _timer.Tick += delegate
        {
            _statusItem.Text = _bridge.Status;
            string text = "Legion Go 2: " + _bridge.Status;
            _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        };
        _timer.Start();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        if (!Object.ReferenceEquals(_applicationIcon, SystemIcons.Application))
            _applicationIcon.Dispose();
        _bridge.Dispose();
        base.ExitThreadCore();
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool created;
        using (var singleInstance = new Mutex(
            true, "Local\\LegionGo2ControllerBridge", out created))
        {
            if (!created)
                return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());
        }
    }
}
