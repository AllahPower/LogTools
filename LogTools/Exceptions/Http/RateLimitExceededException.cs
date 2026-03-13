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

    public RateLimitExceededException(string message, int retryAfterSeconds, Exception innerException)
        : base(message, innerException)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    public int RetryAfterSeconds { get; }

    [Obsolete("Formatter-based serialization is obsolete.")]
    private RateLimitExceededException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        RetryAfterSeconds = info.GetInt32(nameof(RetryAfterSeconds));
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue(nameof(RetryAfterSeconds), RetryAfterSeconds);
        base.GetObjectData(info, context);
    }
}
