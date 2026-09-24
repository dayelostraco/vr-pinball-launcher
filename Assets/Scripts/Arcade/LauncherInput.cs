using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace VRLauncher
{
    /// <summary>
    /// Reads the keyboard and both XR controllers and raises menu actions. Keys are read with
    /// GetAsyncKeyState because the launcher window is often not focused in VR. While
    /// <see cref="InputEnabled"/> is false nothing fires; when it is re-enabled, buttons that are
    /// already down (for example a VPX key still held on return) are ignored until released.
    /// </summary>
    public sealed class LauncherInput : MonoBehaviour
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_RETURN = 0x0D;
        private const int VK_SPACE = 0x20;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_LEFT = 0x25;
        private const int VK_RIGHT = 0x27;
        private const int VK_F = 0x46;
        private const int VK_V = 0x56;

        public const float QuitHoldSeconds = 2f;

        public event Action Previous;
        public event Action Next;
        public event Action Launch;
        public event Action ToggleFavorite;
        public event Action CycleView;
        public event Action Quit;

        public bool InputEnabled { get; set; } = true;
        public float QuitHoldProgress => quitHold.Progress;
        public bool QuitHeld => quitHold.IsHeld;

        private readonly Dictionary<int, ButtonEdge> keys = new Dictionary<int, ButtonEdge>();
        private readonly TriggerLatch leftTrigger = new TriggerLatch();
        private readonly TriggerLatch rightTrigger = new TriggerLatch();
        private readonly ButtonEdge aButton = new ButtonEdge();
        private readonly ButtonEdge bButton = new ButtonEdge();
        private readonly ButtonEdge xButton = new ButtonEdge();
        private readonly StickRepeat stick = new StickRepeat();
        private readonly HoldTimer quitHold = new HoldTimer(QuitHoldSeconds);
        private XRInputDevice left;
        private XRInputDevice right;
        private bool wasEnabled;

        private void Update()
        {
            if (!InputEnabled)
            {
                wasEnabled = false;
                quitHold.Update(false, 0f);
                return;
            }

            // First frame after re-enabling: record what is already held without acting on it.
            Poll(raise: wasEnabled);
            wasEnabled = true;
        }

        private void Poll(bool raise)
        {
            EnsureControllers();
            float dt = Time.unscaledDeltaTime;
            int nav = 0;

            // Non-short-circuit '|' so every detector sees every frame.
            if (KeyEdge(VK_LSHIFT) | KeyEdge(VK_LEFT)) nav -= 1;
            if (KeyEdge(VK_RSHIFT) | KeyEdge(VK_RIGHT)) nav += 1;
            bool launch = KeyEdge(VK_RETURN) | KeyEdge(VK_SPACE);
            bool favorite = KeyEdge(VK_F);
            bool view = KeyEdge(VK_V);
            bool quit = KeyEdge(VK_ESCAPE);

            float stickX = 0f;
            bool yHeld = false;

            if (left.isValid)
            {
                if (left.TryGetFeatureValue(XRCommonUsages.trigger, out float lt) && leftTrigger.Update(lt)) nav -= 1;
                if (left.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool x) && xButton.Update(x)) view = true;
                yHeld = left.TryGetFeatureValue(XRCommonUsages.secondaryButton, out bool y) && y;
                if (left.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 ls)) stickX = ls.x;
            }

            if (right.isValid)
            {
                if (right.TryGetFeatureValue(XRCommonUsages.trigger, out float rt) && rightTrigger.Update(rt)) nav += 1;
                if (right.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool a) && aButton.Update(a)) launch = true;
                if (right.TryGetFeatureValue(XRCommonUsages.secondaryButton, out bool b) && bButton.Update(b)) favorite = true;
                if (right.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 rs) && Mathf.Abs(rs.x) > Mathf.Abs(stickX)) stickX = rs.x;
            }

            nav += stick.Update(stickX, dt);
            if (quitHold.Update(yHeld, dt)) quit = true;

            if (!raise) return;

            if (nav < 0) Previous?.Invoke();
            else if (nav > 0) Next?.Invoke();
            if (favorite) ToggleFavorite?.Invoke();
            if (view) CycleView?.Invoke();
            if (launch) Launch?.Invoke();
            if (quit) Quit?.Invoke();
        }

        private bool KeyEdge(int vk)
        {
            if (!keys.TryGetValue(vk, out ButtonEdge edge))
            {
                edge = new ButtonEdge();
                keys[vk] = edge;
            }
            return edge.Update((GetAsyncKeyState(vk) & 0x8000) != 0);
        }

        /// <summary>(Re)acquires the controllers; they can connect late or drop out.</summary>
        private void EnsureControllers()
        {
            if (!left.isValid) left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!right.isValid) right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }
    }
}
