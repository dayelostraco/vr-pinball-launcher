using System;
using System.Collections.Generic;

namespace VRLauncher
{
    /// <summary>Native window operations and process liveness for one launched session.</summary>
    public interface IWindowFocusTarget
    {
        bool IsRunning { get; }
        IntPtr GetWindow();
        bool IsMinimized(IntPtr window);
        void Refresh();
        void Restore(IntPtr window);
        void SetTopmost(IntPtr window, bool topmost);
        bool Activate(IntPtr window);
    }

    /// <summary>
    /// Bounded focus retries. Yields delays in seconds to the engine scheduler.
    /// Temporary topmost status is always cleared before yielding control again.
    /// </summary>
    public static class WindowFocusSequence
    {
        public static IEnumerable<float> Run(IWindowFocusTarget target, float initialDelay,
            Action<string> info, Action<string> warning)
        {
            info($"Waiting {initialDelay} seconds before focusing VPinballX window...");
            yield return initialDelay;
            if (!target.IsRunning)
            {
                warning("Process exited before window focus could be set");
                yield break;
            }
            bool focused = false;
            for (int attempt = 0; attempt < 10 && !focused; attempt++)
            {
                if (!target.IsRunning) break;
                IntPtr window = IntPtr.Zero;
                bool minimized = false;
                try
                {
                    window = target.GetWindow();
                    if (window != IntPtr.Zero) minimized = target.IsMinimized(window);
                    else
                    {
                        target.Refresh();
                        info($"Window handle not ready yet, attempt {attempt + 1}/10");
                    }
                }
                catch (Exception ex) { warning($"Error getting window handle on attempt {attempt + 1}: {ex.Message}"); }
                if (window != IntPtr.Zero)
                {
                    info($"Found VPinballX main window handle: {window}");
                    if (minimized)
                    {
                        try { target.Restore(window); }
                        catch (Exception ex) { warning("Error restoring window: " + ex.Message); }
                        yield return 0.2f;
                        if (!target.IsRunning) yield break;
                    }
                    try { target.SetTopmost(window, true); }
                    catch (Exception ex) { warning("Error setting window topmost: " + ex.Message); }
                    try
                    {
                        focused = target.Activate(window);
                        if (focused) info("Successfully focused VPinballX window");
                        else warning($"SetForegroundWindow failed on attempt {attempt + 1}");
                    }
                    catch (Exception ex) { warning("Error setting foreground window: " + ex.Message); }
                    finally
                    {
                        try { target.SetTopmost(window, false); }
                        catch (Exception ex) { warning("Error removing topmost flag: " + ex.Message); }
                    }
                }
                if (!focused) yield return 0.5f;
            }
            if (!focused) warning("Could not focus VPinballX window after multiple attempts");
        }
    }
}
