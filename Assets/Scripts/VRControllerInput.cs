using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// Maps VR controller joystick buttons to keyboard events for VPinballX
    /// Uses Windows SendInput API to simulate keyboard presses
    /// Based on Unity's joystick button detection
    /// </summary>
    public class VRControllerInput : MonoBehaviour
    {
        #region Windows API Structures and Imports

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Virtual key codes
        private const ushort VK_LSHIFT = 0xA0;      // Left Shift
        private const ushort VK_RSHIFT = 0xA1;      // Right Shift
        private const ushort VK_RETURN = 0x0D;      // Enter/Return
        private const ushort VK_1 = 0x31;           // 1 key
        private const ushort VK_5 = 0x35;           // 5 key
        private const ushort VK_NUMPAD2 = 0x62;     // Numpad 2
        private const ushort VK_NUMPAD5 = 0x65;     // Numpad 5
        private const ushort VK_NUMPAD8 = 0x68;     // Numpad 8

        #endregion

        [Tooltip("Legacy joystick-to-keyboard bridge, scoped to the launched VPX process")]
        public bool enableControllerInput = true;
        public bool debugLogging = false;
        private TableLauncher tableLauncher;
        internal Func<ushort, bool, bool> KeySink { get; set; }
        internal Func<uint, INPUT[], int, uint> NativeSend { get; set; } = SendInput;
        private readonly int[] buttons = { 0, 1, 2, 3, 4, 5, 14, 15 };
        private readonly ushort[] virtualKeys = { VK_RETURN, VK_5, VK_NUMPAD8, VK_NUMPAD2, VK_1, VK_NUMPAD5, VK_LSHIFT, VK_RSHIFT };
        private readonly HeldKey[] keys = { new HeldKey(), new HeldKey(), new HeldKey(), new HeldKey(), new HeldKey(), new HeldKey(), new HeldKey(), new HeldKey() };

        void Start() { tableLauncher = FindFirstObjectByType<TableLauncher>(); }

        void Update()
        {
            Tick(button => Input.GetKey(KeyCode.JoystickButton0 + button), tableLauncher != null && tableLauncher.OwnsForegroundWindow);
        }

        internal void Tick(Func<int, bool> readButton, bool foregroundOwned)
        {
            bool allowed = enableControllerInput && foregroundOwned;
            for (int i = 0; i < keys.Length; i++)
            {
                ushort key = virtualKeys[i];
                keys[i].Update(readButton(buttons[i]), allowed, down => SendKey(key, down));
            }
        }

        private bool SendKey(ushort key, bool down)
        {
            if (KeySink != null) return KeySink(key, down);
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = key;
            inputs[0].u.ki.dwFlags = down ? KEYEVENTF_KEYDOWN : KEYEVENTF_KEYUP;
            bool sent = NativeSend(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
            if (!sent) Debug.LogWarning("Legacy controller key injection failed: " + Marshal.GetLastWin32Error());
            else if (debugLogging) Debug.Log($"Legacy controller key {key}: {(down ? "down" : "up")}");
            return sent;
        }

        internal void ReleaseKeys()
        {
            for (int i = 0; i < keys.Length; i++)
            {
                ushort key = virtualKeys[i];
                keys[i].Release(down => SendKey(key, down));
            }
        }

        // Releasing owned keys is safe even after focus leaves VPX. Never synthesize a new down event there.
        void OnDisable() { ReleaseKeys(); }
        void OnDestroy() { ReleaseKeys(); }
    }
}
