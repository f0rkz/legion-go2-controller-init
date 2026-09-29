using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class ControllerEventMonitor
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ButtonCallback(uint code);

    private static readonly ConcurrentQueue<uint> Events = new ConcurrentQueue<uint>();
    private static readonly ButtonCallback Callback = OnButton;

    private static string _dllPath = string.Empty;

    private static void OnButton(uint code)
    {
        Events.Enqueue(code);
    }

    private static IntPtr LoadExport(IntPtr module, string name)
    {
        IntPtr address = GetProcAddress(module, name);
        if (address == IntPtr.Zero)
            throw new EntryPointNotFoundException(name);
        return address;
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: ControllerEventMonitor.exe <LEGOKZHandle.dll>");
            return 2;
        }

        _dllPath = Path.GetFullPath(args[0]);
        IntPtr module = LoadLibrary(_dllPath);
        if (module == IntPtr.Zero)
        {
            Console.Error.WriteLine("LoadLibrary failed: {0}", Marshal.GetLastWin32Error());
            return 3;
        }

        var init = (InitDelegate)Marshal.GetDelegateForFunctionPointer(
            LoadExport(module, "Init"), typeof(InitDelegate));
        var register = (RegisterDelegate)Marshal.GetDelegateForFunctionPointer(
            LoadExport(module, "SetTestButtonBackFunc"), typeof(RegisterDelegate));
        var free = (FreeDelegate)Marshal.GetDelegateForFunctionPointer(
            LoadExport(module, "FreeSapientiaUsb"), typeof(FreeDelegate));

        int initialized = init();
        bool registered = register(Callback);
        Console.WriteLine("READY INIT={0} CALLBACK={1}", initialized, registered);
        Console.Out.Flush();

        DateTime deadline = DateTime.UtcNow.AddMinutes(3);
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                uint code;
                while (Events.TryDequeue(out code))
                {
                    Console.WriteLine(
                        "{0:O} BUTTON_CODE={1} HEX=0x{1:X8}",
                        DateTime.Now,
                        code);
                    Console.Out.Flush();
                }
                Thread.Sleep(20);
            }
        }
        finally
        {
            free();
            FreeLibrary(module);
            GC.KeepAlive(Callback);
        }

        return initialized == 1 && registered ? 0 : 4;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool RegisterDelegate(ButtonCallback callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeDelegate();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string fileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);
}
