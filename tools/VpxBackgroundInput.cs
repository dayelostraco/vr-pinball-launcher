using System;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class VpxBackgroundInput
{
    private static readonly string HostDirectory = AppDomain.CurrentDomain.BaseDirectory;
    private const string Hint = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";

    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);

    internal static void Log(string directory, string message)
    {
        try { File.AppendAllText(Path.Combine(directory, "VpxBackgroundInput.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static bool IsPlayerWindow(string className, string title)
    {
        return className == "VPPlayer" || title.StartsWith("Visual Pinball Player", StringComparison.OrdinalIgnoreCase);
    }

    internal static void FocusPlayer(Process process)
    {
        // The launcher tracks this windowless host, so it cannot focus the child.
        // Wait for the play window rather than focusing the editor/loading dialog.
        var timer = Stopwatch.StartNew();
        FocusUntilReady(() => process.HasExited, () => timer.ElapsedMilliseconds, () => {
            IntPtr player = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                if (owner != (uint)process.Id || !IsWindowVisible(window)) return true;
                var className = new StringBuilder(256);
                var title = new StringBuilder(512);
                GetClassName(window, className, className.Capacity);
                GetWindowText(window, title, title.Capacity);
                if (IsPlayerWindow(className.ToString(), title.ToString()))
                    player = window;
                return true;
            }, IntPtr.Zero);
            return player;
        }, player => {
                if (IsIconic(player)) ShowWindow(player, 9);
                SetForegroundWindow(player);
        }, GetForegroundWindow, () => Thread.Sleep(500), message => Log(HostDirectory, message));
    }

    // Clock values are elapsed milliseconds. Require the same window for two
    // seconds and stop after the first successful handoff; never fight Alt-Tab.
    // Callbacks isolate native effects without changing the standalone host build.
    internal static void FocusUntilReady(Func<bool> exited, Func<long> milliseconds,
        Func<IntPtr> findPlayer, Action<IntPtr> focus, Func<IntPtr> foreground,
        Action wait, Action<string> log)
    {
        IntPtr lastWindow = IntPtr.Zero;
        long visibleSince = 0;
        while (!exited() && milliseconds() < 120000)
        {
            IntPtr player = findPlayer();
            if (player != lastWindow) { lastWindow = player; visibleSince = milliseconds(); }
            if (player != IntPtr.Zero && milliseconds() - visibleSince >= 2000)
            {
                focus(player);
                if (foreground() == player)
                {
                    log("Focused VPX player hwnd=" + player);
                    return; // Startup handoff only; never fight later user window switches.
                }
            }
            wait();
        }
        log(exited() ? "VPX exited before focus handoff" : "Timed out waiting for VPX player focus");
    }

    internal static string Quote(string value)
    {
        var result = new StringBuilder();
        result.Append('"');
        int slashes = 0;
        foreach (char ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            if (ch == '"')
            {
                result.Append('\\', slashes * 2 + 1);
                result.Append('"');
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes);
            result.Append(ch);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        result.Append('"');
        return result.ToString();
    }

    private static int Main(string[] args)
    {
        return Execute(args, HostDirectory, FocusPlayer);
    }

    internal static int Execute(string[] args, string directory, Action<Process> focus)
    {
        try { return Run(args, directory, focus); }
        catch (Exception ex)
        {
            Log(directory, "Launch failed: " + ex.Message);
            return 1;
        }
    }

    private static int Run(string[] args, string directory, Action<Process> focus)
    {
        bool selfTest = args.Length == 1 && args[0] == "--self-test";
        var start = new ProcessStartInfo();
        start.UseShellExecute = false;
        start.CreateNoWindow = selfTest;
        start.EnvironmentVariables[Hint] = "1";
        if (selfTest)
        {
            start.FileName = Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\WindowsPowerShell\v1.0\powershell.exe";
            start.Arguments = "-NoProfile -NonInteractive -Command \"if ($env:SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS -eq '1') { exit 0 } else { exit 42 }\"";
            start.WorkingDirectory = Environment.CurrentDirectory;
        }
        else
        {
            string pathFile = Path.Combine(directory, "VpxBackgroundInput.vpx-path.txt");
            if (!File.Exists(pathFile))
            {
                Log(directory, "Missing " + pathFile + "; run install-vpx-background-input.ps1 first.");
                return 2;
            }
            string vpxPath = File.ReadAllText(pathFile).Trim();
            if (!Path.IsPathRooted(vpxPath) || !File.Exists(vpxPath)
                || string.Equals(Path.GetFullPath(vpxPath), System.Reflection.Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase))
            {
                Log(directory, "Invalid VPX executable path: " + vpxPath);
                return 2;
            }
            start.FileName = vpxPath;
            // Honor launcher profiles using workingDirectory="table".
            start.WorkingDirectory = Environment.CurrentDirectory;
            var commandLine = new StringBuilder();
            foreach (string arg in args)
            {
                if (commandLine.Length > 0) commandLine.Append(' ');
                commandLine.Append(Quote(arg));
            }
            start.Arguments = commandLine.ToString();
        }
        using (Process process = Process.Start(start))
        {
            if (!selfTest)
            {
                Log(directory, "Started VPX pid=" + process.Id);
                try { AllowSetForegroundWindow((uint)process.Id); focus(process); }
                catch (Exception ex) { Log(directory, "Focus handoff failed: " + ex.Message); }
            }
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
