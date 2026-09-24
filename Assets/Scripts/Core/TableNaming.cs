using System.Text.RegularExpressions;

namespace VRLauncher
{
    /// <summary>
    /// Parses and normalises table file names of the community form
    /// "Title (Manufacturer Year) optional decoration".
    /// </summary>
    public static class TableNaming
    {
        // A "(Manufacturer Year)" group, e.g. "(Bally 1995)".
        private static readonly Regex YearGroupRegex =
            new Regex(@"\([^)]*\b(?:19|20)\d{2}\b[^)]*\)", RegexOptions.Compiled);

        private static readonly Regex YearTokenRegex =
            new Regex(@"\b(?:19|20)\d{2}\b", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex =
            new Regex(@"\s+", RegexOptions.Compiled);

        // VPX VR-room conversions prefix the title; wheel art never does.
        private static readonly Regex VrRoomPrefixRegex =
            new Regex(@"^vr\s*room\s+", RegexOptions.Compiled);

        // Lazy title, then the first parenthesised "Maker Year" group that actually ends in a year,
        // so "Spider-Man (Vault Edition) (Stern 2016)" keeps "(Vault Edition)" in the title.
        private static readonly Regex TitleMakerYearRegex =
            new Regex(@"^(?<title>.*?)\s*\((?<maker>[^()]*?)\s+(?<year>(?:19|20)\d{2})\)", RegexOptions.Compiled);

        // "Simpsons Pinball Party, The" -> "The Simpsons Pinball Party".
        private static readonly Regex TrailingArticleRegex =
            new Regex(@"^(?<rest>.+),\s*(?<article>The|A|An)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Splits a table file stem into a display title, manufacturer and year.
        /// </summary>
        public static ParsedName Parse(string stem)
        {
            string cleaned = WhitespaceRegex.Replace((stem ?? string.Empty).Replace('_', ' '), " ").Trim();
            Match match = TitleMakerYearRegex.Match(cleaned);
            if (!match.Success || match.Groups["title"].Value.Trim().Length == 0)
            {
                return new ParsedName(DisplayTitle(cleaned), null, 0);
            }

            return new ParsedName(
                DisplayTitle(match.Groups["title"].Value.Trim()),
                match.Groups["maker"].Value.Trim(),
                int.Parse(match.Groups["year"].Value));
        }

        /// <summary>
        /// Case/punctuation-insensitive form: underscores become spaces,
        /// parentheses are dropped, whitespace is collapsed, and a leading
        /// "VR ROOM" marker is removed.
        /// </summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string result = name.Replace('_', ' ').Replace("(", "").Replace(")", "");
            result = WhitespaceRegex.Replace(result, " ").Trim().ToLowerInvariant();
            result = VrRoomPrefixRegex.Replace(result, "");

            return result.Trim();
        }

        /// <summary>
        /// Reduces a name to "title manufacturer year", discarding the author
        /// and version decoration that follows it.
        /// </summary>
        public static string TitleYearSignature(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            // Preferred form: truncate after the "(Manufacturer Year)" group.
            Match group = YearGroupRegex.Match(name);
            if (group.Success)
            {
                return Normalize(name.Substring(0, group.Index + group.Length));
            }

            // Underscore-separated names have no parentheses to anchor on, so
            // fall back to the last year token. Using the last one keeps titles
            // that begin with a year (e.g. "2001 (Gottlieb 1971)") intact.
            string normalized = Normalize(name);
            MatchCollection years = YearTokenRegex.Matches(normalized);
            if (years.Count > 0)
            {
                Match last = years[years.Count - 1];
                return normalized.Substring(0, last.Index + last.Length).Trim();
            }

            return normalized;
        }

        private static string DisplayTitle(string title)
        {
            Match match = TrailingArticleRegex.Match(title);
            return match.Success ? $"{match.Groups["article"].Value} {match.Groups["rest"].Value}" : title;
        }
    }

    /// <summary>
    /// A table name split into its parts. Manufacturer is null and Year is 0 when the
    /// name carries no "(Manufacturer Year)" group.
    /// </summary>
    public readonly struct ParsedName
    {
        public readonly string Title;
        public readonly string Manufacturer;
        public readonly int Year;

        public ParsedName(string title, string manufacturer, int year)
        {
            Title = title;
            Manufacturer = manufacturer;
            Year = year;
        }
    }
}
