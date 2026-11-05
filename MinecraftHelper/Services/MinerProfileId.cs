using System;
using System.Linq;

namespace MinecraftHelper.Services;

internal static class MinerProfileId
{
    public const string Default = "default";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Default;

        string normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
            .Take(48)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? Default : normalized;
    }
}

