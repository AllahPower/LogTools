using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public sealed class RateLimitExceededException : LogsParserHttpException
{
    public RateLimitExceededException()
    {
    }

    public RateLimitExceededException(string message, int retryAfterSeconds)
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    public RateLimitExceededException(string message, int retryAfterSeconds, DateTimeOffset? resetAt)
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
        ResetAt = resetAt;
    }

    public RateLimitExceededException(string message, int retryAfterSeconds, Exception innerException)
        : base(message, innerException)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    public int RetryAfterSeconds { get; }

    public DateTimeOffset? ResetAt { get; }

    [Obsolete("Formatter-based serialization is obsolete.")]
    private RateLimitExceededException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        RetryAfterSeconds = info.GetInt32(nameof(RetryAfterSeconds));
        var resetTicks = info.GetInt64(nameof(ResetAt));
        ResetAt = resetTicks >= 0 ? new DateTimeOffset(resetTicks, TimeSpan.Zero) : null;
    }

#pragma warning disable CS0809
    [Obsolete("Formatter-based serialization is obsolete.")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
#pragma warning restore CS0809
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue(nameof(RetryAfterSeconds), RetryAfterSeconds);
        info.AddValue(nameof(ResetAt), ResetAt?.Ticks ?? -1L);
        base.GetObjectData(info, context);
    }
}
