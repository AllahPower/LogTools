using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace LogsParser.Infrastructure;

internal static class ThrowHelper
{
    public static void ThrowIfNullOrWhiteSpace(
        [NotNull] string? argument,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(argument, paramName);
#else
        ArgumentNullException.ThrowIfNull(argument, paramName);

        if (string.IsNullOrWhiteSpace(argument))
        {
            throw new ArgumentException(
                "The value cannot be an empty string or composed entirely of whitespace.",
                paramName);
        }
#endif
    }
}
