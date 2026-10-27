using System;
using System.Linq;
using System.Reflection;

namespace CutoutEqD.App
{
    /// <summary>Version and build details stamped into the assembly at build time (see Directory.Build.props).</summary>
    internal static class BuildInfo
    {
        private static readonly Assembly Asm = typeof(BuildInfo).Assembly;

        /// <summary>Release version, e.g. "1.1.0".</summary>
        public static string Version { get; } = Asm.GetName().Version?.ToString(3) ?? "";

        /// <summary>Short git commit the build came from, or "" when built outside a git checkout.</summary>
        public static string Commit { get; } = ReadCommit();

        /// <summary>UTC build date, yyyy-MM-dd.</summary>
        public static string Date { get; } = Asm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value ?? "";

        /// <summary>e.g. "1.1.0 (3f2a9c1, 2026-10-27)" — for the status bar, PDF report and support questions.</summary>
        public static string Full
        {
            get
            {
                var extra = string.Join(", ", new[] { Commit, Date }.Where(s => s.Length > 0));
                return extra.Length == 0 ? Version : $"{Version} ({extra})";
            }
        }

        private static string ReadCommit()
        {
            // The .NET SDK appends "+<commit sha>" to InformationalVersion when building from git.
            var info = Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            int plus = info.IndexOf('+');
            if (plus < 0) return "";
            var sha = info.Substring(plus + 1);
            return sha.Length > 7 ? sha.Substring(0, 7) : sha;
        }
    }
}
