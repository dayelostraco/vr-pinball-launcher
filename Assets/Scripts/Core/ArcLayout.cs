using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>
    /// Where the cabinets stand: on an arc around the player (at the origin, looking along +Z),
    /// one slot per offset, positive offsets to the right. Offsets may be fractional while the
    /// arc is gliding between tables.
    /// </summary>
    public static class ArcLayout
    {
        public const int MaxOffset = 3;
        public const float Radius = 2.2f;
        public const float SpacingDegrees = 22f;

        public static Vector3 SlotPosition(float offset)
        {
            float angle = offset * SpacingDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle) * Radius, 0f, Mathf.Cos(angle) * Radius);
        }

        /// <summary>Turns a cabinet so its front (-Z) faces the player at the origin.</summary>
        public static Quaternion SlotRotation(float offset) => Quaternion.Euler(0f, offset * SpacingDegrees, 0f);

        /// <summary>
        /// The offsets to show for a list of <paramref name="itemCount"/> tables: up to 3 each
        /// side, never so many that the wrap-around shows a table twice.
        /// </summary>
        public static IReadOnlyList<int> VisibleOffsets(int itemCount)
        {
            if (itemCount <= 0) return Array.Empty<int>();
            int left = Math.Min(MaxOffset, (itemCount - 1) / 2);
            int right = Math.Min(MaxOffset, itemCount - 1 - left);
            return Enumerable.Range(-left, left + right + 1).ToList();
        }
    }
}
