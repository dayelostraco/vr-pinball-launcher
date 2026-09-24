using System;

namespace VRLauncher
{
    /// <summary>
    /// Turns an analog trigger into single presses: fires when it rises above the press
    /// threshold and re-arms only after it drops below the release threshold.
    /// </summary>
    public sealed class TriggerLatch
    {
        public const float PressThreshold = 0.7f;
        public const float ReleaseThreshold = 0.4f;
        private bool held;

        public bool Update(float value)
        {
            if (!held && value > PressThreshold)
            {
                held = true;
                return true;
            }
            if (held && value < ReleaseThreshold)
            {
                held = false;
            }
            return false;
        }
    }

    /// <summary>Fires on the frame a button goes down.</summary>
    public sealed class ButtonEdge
    {
        private bool was;

        public bool Update(bool down)
        {
            bool fired = down && !was;
            was = down;
            return fired;
        }
    }

    /// <summary>
    /// Thumbstick browsing: one step when the stick is pushed, then repeats while held,
    /// getting faster the longer it is held. Returns -1, 0 or +1 for this frame.
    /// </summary>
    public sealed class StickRepeat
    {
        public const float PressThreshold = 0.6f;
        public const float ReleaseThreshold = 0.3f;
        public const float InitialDelay = 0.45f;
        public const float StartInterval = 0.22f;
        public const float MinInterval = 0.07f;
        public const float Acceleration = 0.85f;

        private int direction;
        private float timer;
        private float interval;

        public int Update(float x, float deltaSeconds)
        {
            if (direction == 0)
            {
                if (x > PressThreshold) direction = 1;
                else if (x < -PressThreshold) direction = -1;
                else return 0;

                timer = InitialDelay;
                interval = StartInterval;
                return direction;
            }

            if (Math.Abs(x) < ReleaseThreshold || Math.Sign(x) != direction)
            {
                direction = 0;
                return 0;
            }

            timer -= deltaSeconds;
            if (timer > 0f) return 0;

            timer += interval;
            interval = Math.Max(MinInterval, interval * Acceleration);
            return direction;
        }
    }

    /// <summary>Fires once when a button has been held for the given time.</summary>
    public sealed class HoldTimer
    {
        private readonly float seconds;
        private float held;
        private bool fired;

        public HoldTimer(float seconds)
        {
            this.seconds = seconds;
        }

        public float Progress => Math.Min(held / seconds, 1f);
        public bool IsHeld => held > 0f;

        public bool Update(bool down, float deltaSeconds)
        {
            if (!down)
            {
                held = 0f;
                fired = false;
                return false;
            }

            held += deltaSeconds;
            if (!fired && held >= seconds)
            {
                fired = true;
                return true;
            }
            return false;
        }
    }
}
