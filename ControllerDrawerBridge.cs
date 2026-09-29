using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

internal static class ControllerDrawerBridge
{
    private const uint BrokenShortcutMask = 0x40000800;
    private const int MinimumSendIntervalMs = 750;
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

    private static readonly ConcurrentQueue<uint> Events = new ConcurrentQueue<uint>();
    private static readonly AutoResetEvent EventReady = new AutoResetEvent(false);
    private static readonly ButtonCallback Callback = OnButton;
    private static volatile bool _stopping;
    private static readonly object LogLock = new object();
    private static string _logPath;

    private static void Log(string format, params object[] values)
    {
        string line = String.Format(format, values);
        Console.WriteLine(line);
        Console.Out.Flush();
        if (String.IsNullOrEmpty(_logPath))
            return;
        lock (LogLock)
            File.AppendAllText(_logPath, line + Environment.NewLine);
    }

    private static void OnButton(uint code)
    {
        Events.Enqueue(code);
        EventReady.Set();
    }

    private static IntPtr RequireExport(IntPtr module, string name)
    {
        IntPtr address = GetProcAddress(module, name);
        if (address == IntPtr.Zero)
            throw new EntryPointNotFoundException(name);
        return address;
    }

    private static T GetDelegate<T>(IntPtr module, string name) where T : class
    {
        return (T)(object)Marshal.GetDelegateForFunctionPointer(
            RequireExport(module, name), typeof(T));
    }

    private static int Main(string[] args)
    {
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge.log");

        string controllerPath;
        string settingPath;
        string drawerExecutable;
        if (args.Length == 0)
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Lenovo", "LegionSpace");
            controllerPath = Directory.GetFiles(root, "LEGOKZHandle.dll", SearchOption.AllDirectories)
                .Where(path => path.IndexOf("\\SapientiaUsb\\", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            settingPath = Directory.GetFiles(root, "LegionSetting.dll", SearchOption.AllDirectories)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            drawerExecutable = Directory.GetFiles(root, "LegionSettingMenu.exe", SearchOption.AllDirectories)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        else if (args.Length == 2)
        {
            controllerPath = args[0];
            settingPath = args[1];
            drawerExecutable = Path.Combine(Path.GetDirectoryName(settingPath), "LegionSettingMenu.exe");
        }
        else
        {
            Console.Error.WriteLine("Usage: ControllerDrawerBridge.exe [<LEGOKZHandle.dll> <LegionSetting.dll>]");
            return 2;
        }

        if (String.IsNullOrEmpty(controllerPath) || String.IsNullOrEmpty(settingPath) ||
            String.IsNullOrEmpty(drawerExecutable) || !File.Exists(controllerPath) ||
            !File.Exists(settingPath) || !File.Exists(drawerExecutable))
        {
            Console.Error.WriteLine("The installed Lenovo controller or setting DLL was not found.");
            return 2;
        }

        controllerPath = Path.GetFullPath(controllerPath);
        settingPath = Path.GetFullPath(settingPath);

        SetDllDirectory(Path.GetDirectoryName(controllerPath));
        IntPtr controllerModule = LoadLibrary(controllerPath);
        if (controllerModule == IntPtr.Zero)
        {
            Console.Error.WriteLine("Controller LoadLibrary failed: {0}", Marshal.GetLastWin32Error());
            return 3;
        }

        SetDllDirectory(Path.GetDirectoryName(settingPath));
        IntPtr settingModule = LoadLibrary(settingPath);
        if (settingModule == IntPtr.Zero)
        {
            Console.Error.WriteLine("Setting LoadLibrary failed: {0}", Marshal.GetLastWin32Error());
            FreeLibrary(controllerModule);
            return 3;
        }

        var init = GetDelegate<InitDelegate>(controllerModule, "Init");
        var register = GetDelegate<RegisterDelegate>(controllerModule, "SetTestButtonBackFunc");
        var free = GetDelegate<FreeDelegate>(controllerModule, "FreeSapientiaUsb");
        var send = GetDelegate<SendMessageDelegate>(settingModule, "SendMsg2App");

        int initialized = init();
        bool registered = register(Callback);
        if (initialized != 1 || !registered)
        {
            Console.Error.WriteLine("Controller initialization failed: INIT={0} CALLBACK={1}", initialized, registered);
            free();
            return 4;
        }

        Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            _stopping = true;
            EventReady.Set();
        };

        Log("{0:O} READY INIT={1} CALLBACK={2} PID={3}",
            DateTime.Now, initialized, registered, System.Diagnostics.Process.GetCurrentProcess().Id);

        long lastSendTicks = 0;
        try
        {
            while (!_stopping)
            {
                EventReady.WaitOne(1000);
                uint code;
                while (Events.TryDequeue(out code))
                {
                    if ((code & BrokenShortcutMask) != BrokenShortcutMask)
                        continue;

                    long now = DateTime.UtcNow.Ticks;
                    if (lastSendTicks != 0 &&
                        TimeSpan.FromTicks(now - lastSendTicks).TotalMilliseconds < MinimumSendIntervalMs)
                        continue;

                    lastSendTicks = now;
                    bool sent = SendDrawerMessage(send, drawerExecutable);
                    Log(
                        "{0:O} RECOVERY CODE=0x{1:X8} SENT={2}",
                        DateTime.Now,
                        code,
                        sent);
                }
            }
        }
        finally
        {
            free();
            FreeLibrary(settingModule);
            FreeLibrary(controllerModule);
            GC.KeepAlive(Callback);
        }

        return 0;
    }

    private static bool SendDrawerMessage(SendMessageDelegate send, string drawerExecutable)
    {
        if (TrySendDrawerMessage(send))
            return true;

        if (Process.GetProcessesByName("LegionSettingMenu").Length == 0)
        {
            var startInfo = new ProcessStartInfo(drawerExecutable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(drawerExecutable),
                CreateNoWindow = true
            };
            Process.Start(startInfo);
            Log("{0:O} DRAWER_PROCESS_STARTED", DateTime.Now);
        }

        for (int attempt = 1; attempt <= 10; attempt++)
        {
            Thread.Sleep(200);
            if (TrySendDrawerMessage(send))
            {
                Log("{0:O} IPC_RETRY_SUCCESS ATTEMPT={1}", DateTime.Now, attempt);
                return true;
            }
        }

        return false;
    }

    private static bool TrySendDrawerMessage(SendMessageDelegate send)
    {
        IntPtr application = Marshal.StringToCoTaskMemUni(DrawerApplication);
        IntPtr message = Marshal.StringToCoTaskMemUni(DrawerToggleMessage);
        try
        {
            return send(application, message);
        }
        finally
        {
            Marshal.FreeCoTaskMem(application);
            Marshal.FreeCoTaskMem(message);
        }
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
}
