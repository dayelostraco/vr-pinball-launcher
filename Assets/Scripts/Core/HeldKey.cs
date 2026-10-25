using System;

namespace VRLauncher
{
    /// <summary>Owns one synthetic key. After suspension, require a physical release before rearming.</summary>
    public sealed class HeldKey
    {
        private bool sent;
        private bool blocked = true;

        public void Update(bool down, bool allowed, Func<bool, bool> send)
        {
            if (!allowed)
            {
                Release(send);
                blocked = down;
                return;
            }
            if (blocked)
            {
                blocked = down;
                return;
            }
            if (down != sent && send(down)) sent = down;
        }

        public void Release(Func<bool, bool> send)
        {
            if (sent && send(false)) sent = false;
            blocked = true;
        }
    }
}
