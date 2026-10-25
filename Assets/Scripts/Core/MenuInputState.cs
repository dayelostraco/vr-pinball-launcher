using System;

namespace VRLauncher
{
    [Flags]
    public enum MenuButtons
    {
        None = 0, LeftShift = 1, LeftArrow = 2, RightShift = 4, RightArrow = 8,
        Enter = 16, Space = 32, Escape = 64, Favorite = 128, View = 256,
        Surprise = 512, Details = 1024, Setup = 2048, A = 4096, B = 8192,
        X = 16384, Y = 32768, LeftStickClick = 65536, RightStickClick = 131072
    }

    [Flags]
    public enum MenuActions
    {
        None = 0, Previous = 1, Next = 2, Launch = 4, Favorite = 8,
        NextView = 16, PreviousView = 32, PreviousLetter = 64, NextLetter = 128,
        Surprise = 256, Details = 512, Setup = 1024, Quit = 2048
    }

    /// <summary>A single hardware sample. Axes are normalized; triggers range from zero to one.</summary>
    public struct MenuInputFrame
    {
        public MenuButtons Buttons;
        public float LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger;
    }

    /// <summary>
    /// Menu decisions independent of Unity and native input. After suspension, held
    /// controls must be released/centered before they can act on the menu again.
    /// </summary>
    public sealed class MenuInputState
    {
        public const float QuitHoldSeconds = 2f;
        private MenuButtons previous;
        private bool wasEnabled, blockLeftStick, blockRightStick, blockQuit;
        private readonly TriggerLatch leftTrigger = new TriggerLatch();
        private readonly TriggerLatch rightTrigger = new TriggerLatch();
        private readonly StickRepeat stick = new StickRepeat();
        private readonly ViewStickLatch viewStick = new ViewStickLatch();
        private readonly ViewStickLatch letterStick = new ViewStickLatch();
        private readonly HoldTimer quitHold = new HoldTimer(QuitHoldSeconds);

        public float QuitHoldProgress => quitHold.Progress;
        public bool QuitHeld => quitHold.IsHeld;

        /// <param name="enabled">False while a table or desktop setup owns input.</param>
        /// <param name="deltaSeconds">Unscaled time since the preceding sample.</param>
        public MenuActions Update(MenuInputFrame frame, bool enabled, float deltaSeconds)
        {
            if (!enabled)
            {
                wasEnabled = false;
                quitHold.Update(false, 0f);
                return MenuActions.None;
            }

            bool firstFrame = !wasEnabled;
            wasEnabled = true;
            if (firstFrame)
            {
                previous = frame.Buttons;
                blockLeftStick = !Centered(frame.LeftX, frame.LeftY);
                blockRightStick = !Centered(frame.RightX, frame.RightY);
                blockQuit = (frame.Buttons & MenuButtons.Y) != 0;
                // Clear repeat/latch state from the previous session. Triggers are
                // sampled below even on this discarded frame to suppress held presses.
                stick.Update(0f, 0f);
                viewStick.Update(0f, 0f);
                letterStick.Update(0f, 0f);
                leftTrigger.Update(0f);
                rightTrigger.Update(0f);
            }
            MenuButtons pressed = frame.Buttons & ~previous;
            previous = frame.Buttons;
            if (Centered(frame.LeftX, frame.LeftY)) blockLeftStick = false;
            if (Centered(frame.RightX, frame.RightY)) blockRightStick = false;
            if ((frame.Buttons & MenuButtons.Y) == 0) blockQuit = false;
            float lx = blockLeftStick ? 0f : frame.LeftX;
            float ly = blockLeftStick ? 0f : frame.LeftY;
            float rx = blockRightStick ? 0f : frame.RightX;
            float ry = blockRightStick ? 0f : frame.RightY;

            int navigation = 0;
            if (Has(pressed, MenuButtons.LeftShift | MenuButtons.LeftArrow)) navigation--;
            if (Has(pressed, MenuButtons.RightShift | MenuButtons.RightArrow)) navigation++;
            if (leftTrigger.Update(frame.LeftTrigger)) navigation--;
            if (rightTrigger.Update(frame.RightTrigger)) navigation++;
            int view = viewStick.Update(lx, ly);
            int letter = letterStick.Update(rx, ry);
            float horizontal = Math.Abs(lx) >= Math.Abs(ly) ? lx : 0f;
            if (Math.Abs(rx) >= Math.Abs(ry) && Math.Abs(rx) > Math.Abs(horizontal)) horizontal = rx;
            navigation += stick.Update(horizontal, deltaSeconds);
            bool quit = quitHold.Update(!blockQuit && Has(frame.Buttons, MenuButtons.Y), deltaSeconds);

            if (firstFrame) return MenuActions.None;
            // Opening setup owns the entire frame: typing in its fields must not
            // simultaneously launch a table, change a favorite or exit the launcher.
            if (Has(pressed, MenuButtons.Setup)) return MenuActions.Setup;
            MenuActions actions = MenuActions.None;
            if (navigation < 0) actions |= MenuActions.Previous;
            else if (navigation > 0) actions |= MenuActions.Next;
            if (letter < 0) actions |= MenuActions.PreviousLetter;
            else if (letter > 0) actions |= MenuActions.NextLetter;
            if (Has(pressed, MenuButtons.Favorite | MenuButtons.B)) actions |= MenuActions.Favorite;
            if (Has(pressed, MenuButtons.Details | MenuButtons.LeftStickClick)) actions |= MenuActions.Details;
            if (Has(pressed, MenuButtons.View | MenuButtons.X) || view > 0) actions |= MenuActions.NextView;
            else if (view < 0) actions |= MenuActions.PreviousView;
            // Show the random selection before permitting a launch.
            if (Has(pressed, MenuButtons.Surprise | MenuButtons.RightStickClick)) actions |= MenuActions.Surprise;
            else if (Has(pressed, MenuButtons.Enter | MenuButtons.Space | MenuButtons.A)) actions |= MenuActions.Launch;
            if (quit || Has(pressed, MenuButtons.Escape)) actions |= MenuActions.Quit;
            return actions;
        }

        private static bool Has(MenuButtons value, MenuButtons mask) => (value & mask) != 0;
        private static bool Centered(float x, float y) => Math.Abs(x) < 0.3f && Math.Abs(y) < 0.3f;
    }
}
