using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VRLauncher
{
    /// <summary>Read-only native query avoids a Microsoft.Win32.Registry dependency in Unity's .NET profile.</summary>
    internal static class WindowsRomRegistry
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW")]
        private static extern int RegGetValue(IntPtr key, string subkey, string value, uint flags,
                                             out uint type, StringBuilder data, ref uint bytes);

        internal static string ReadRomPaths()
        {
            var data = new StringBuilder(32768);
            uint bytes = (uint)data.Capacity * 2;
            uint type;
            int result = RegGetValue(new IntPtr(unchecked((int)0x80000001)),
                @"Software\Freeware\Visual PinMame\globals", "rompath", 6, out type, data, ref bytes);
            return result == 0 ? data.ToString() : "";
        }
    }
}
