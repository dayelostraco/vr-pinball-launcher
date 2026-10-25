using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;

namespace VRLauncher
{
    /// <summary>
    /// Samples keyboard and XR controllers, then dispatches menu decisions.
    /// Global keyboard polling supports VR when the desktop window lacks focus;
    /// the session owner must suppress it while setup or a table owns input.
    /// </summary>
    public sealed class LauncherInput : MonoBehaviour
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // Ordering matches the first twelve flags in MenuButtons.
        private static readonly int[] VirtualKeys =
            { 0xA0, 0x25, 0xA1, 0x27, 0x0D, 0x20, 0x1B, 0x46, 0x56, 0x52, 0x49, 0x71 };

        public const float QuitHoldSeconds = MenuInputState.QuitHoldSeconds;
        public event Action Previous;
        public event Action Next;
        public event Action Launch;
        public event Action ToggleFavorite;
        public event Action CycleView;
        public event Action PreviousView;
        public event Action<int> JumpLetter;
        public event Action SurpriseMe;
        public event Action ToggleDetails;
        public event Action OpenSetup;
        public event Action Quit;

        public bool InputEnabled { get; set; } = true;
        public Func<bool> IsSuppressed { get; set; }
        public float QuitHoldProgress => state.QuitHoldProgress;
        public bool QuitHeld => state.QuitHeld;

        private readonly MenuInputState state = new MenuInputState();
        private IMenuController left;
        private IMenuController right;
        internal Func<int, bool> KeyPressed { get; set; } = key => (GetAsyncKeyState(key) & 0x8000) != 0;
        internal Func<XRNode, IMenuController> GetController { get; set; } = node => new MenuController(InputDevices.GetDeviceAtXRNode(node));

        // Keep sampling replaceable for deterministic adapter tests without
        // injecting operating-system keystrokes or requiring a connected headset.
        internal Func<MenuInputFrame> ReadInput { get; set; }

        private void Update() => Tick(Time.unscaledDeltaTime);

        internal void Tick(float deltaSeconds)
        {
            bool enabled = InputEnabled && !(IsSuppressed?.Invoke() ?? false);
            MenuInputFrame frame = enabled ? (ReadInput != null ? ReadInput() : Sample()) : default;
            MenuActions actions = state.Update(frame, enabled, deltaSeconds);
            if ((actions & MenuActions.Setup) != 0) { OpenSetup?.Invoke(); return; }
            if ((actions & MenuActions.Previous) != 0) Previous?.Invoke();
            if ((actions & MenuActions.Next) != 0) Next?.Invoke();
            if ((actions & MenuActions.PreviousLetter) != 0) JumpLetter?.Invoke(-1);
            if ((actions & MenuActions.NextLetter) != 0) JumpLetter?.Invoke(1);
            if ((actions & MenuActions.Favorite) != 0) ToggleFavorite?.Invoke();
            if ((actions & MenuActions.Details) != 0) ToggleDetails?.Invoke();
            if ((actions & MenuActions.NextView) != 0) CycleView?.Invoke();
            if ((actions & MenuActions.PreviousView) != 0) PreviousView?.Invoke();
            if ((actions & MenuActions.Surprise) != 0) SurpriseMe?.Invoke();
            if ((actions & MenuActions.Launch) != 0) Launch?.Invoke();
            if ((actions & MenuActions.Quit) != 0) Quit?.Invoke();
        }

        internal MenuInputFrame Sample()
        {
            var frame = new MenuInputFrame();
            for (int i = 0; i < VirtualKeys.Length; i++)
                if (KeyPressed(VirtualKeys[i])) frame.Buttons |= (MenuButtons)(1 << i);
            // Devices can arrive late or disconnect while VPX owns the XR session.
            if (left == null || !left.IsValid) left = GetController(XRNode.LeftHand);
            if (right == null || !right.IsValid) right = GetController(XRNode.RightHand);
            if (left.IsValid)
            {
                frame.LeftTrigger = left.Trigger;
                Vector2 axis = left.Axis;
                frame.LeftX = axis.x;
                frame.LeftY = axis.y;
                if (Pressed(left, CommonUsages.primaryButton)) frame.Buttons |= MenuButtons.X;
                if (Pressed(left, CommonUsages.secondaryButton)) frame.Buttons |= MenuButtons.Y;
                if (Pressed(left, CommonUsages.primary2DAxisClick)) frame.Buttons |= MenuButtons.LeftStickClick;
            }
            if (right.IsValid)
            {
                frame.RightTrigger = right.Trigger;
                Vector2 axis = right.Axis;
                frame.RightX = axis.x;
                frame.RightY = axis.y;
                if (Pressed(right, CommonUsages.primaryButton)) frame.Buttons |= MenuButtons.A;
                if (Pressed(right, CommonUsages.secondaryButton)) frame.Buttons |= MenuButtons.B;
                if (Pressed(right, CommonUsages.primary2DAxisClick)) frame.Buttons |= MenuButtons.RightStickClick;
            }
            return frame;
        }

        private static bool Pressed(IMenuController device, InputFeatureUsage<bool> feature) => device.Pressed(feature);

        private sealed class MenuController : IMenuController
        {
            private readonly InputDevice device;
            internal MenuController(InputDevice device) { this.device = device; }
            public bool IsValid => device.isValid;
            public float Trigger => device.TryGetFeatureValue(CommonUsages.trigger, out float value) ? value : 0;
            public Vector2 Axis => device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 value) ? value : Vector2.zero;
            public bool Pressed(InputFeatureUsage<bool> feature) => device.TryGetFeatureValue(feature, out bool value) && value;
        }
    }

    /// <summary>Read-only controller sample; invalid devices are reacquired next frame.</summary>
    internal interface IMenuController
    {
        bool IsValid { get; }
        float Trigger { get; }
        Vector2 Axis { get; }
        bool Pressed(InputFeatureUsage<bool> feature);
    }
}
