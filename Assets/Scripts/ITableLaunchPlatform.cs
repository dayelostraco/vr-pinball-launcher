using System.Diagnostics;
using System.Collections;

namespace VRLauncher
{
    /// <summary>
    /// Side effects at the launch boundary. Tests substitute XR/focus operations
    /// and use isolated dummy processes and logs; production owns the real session.
    /// </summary>
    internal interface ITableLaunchPlatform
    {
        string RuntimeLogPath { get; }
        Process StartProcess(ProcessStartInfo startInfo);
        void StopXR();
        void StartXR();
        void FocusWindow();
    }

    /// <summary>XR manager lifecycle; initialization yields between loader attempts.</summary>
    internal interface IXrSession
    {
        bool Initialized { get; }
        bool HasLoader { get; }
        IEnumerator Initialize();
        void Start();
        void Stop();
        void Deinitialize();
    }
}
