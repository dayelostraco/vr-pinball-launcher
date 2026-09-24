using System;
using System.Globalization;

namespace VRLauncher
{
    /// <summary>"3 days ago" style wording for the info plate.</summary>
    public static class RelativeTime
    {
        public static string Format(DateTime lastUtc, DateTime nowUtc)
        {
            TimeSpan ago = nowUtc - lastUtc;
            if (ago < TimeSpan.FromMinutes(1)) return "just now";
            if (ago < TimeSpan.FromHours(1)) return Plural((int)ago.TotalMinutes, "minute");
            if (ago < TimeSpan.FromHours(24)) return Plural((int)ago.TotalHours, "hour");
            if (ago < TimeSpan.FromHours(48)) return "yesterday";
            if (ago < TimeSpan.FromDays(14)) return Plural((int)ago.TotalDays, "day");
            return "on " + lastUtc.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        }

        private static string Plural(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
    }
}
