using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public sealed class TwoFactorAuthenticationException : LogsParserHttpException
{
    public TwoFactorAuthenticationException()
    {
    }

    public TwoFactorAuthenticationException(string message)
        : base(message)
    {
    }

    public TwoFactorAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    private TwoFactorAuthenticationException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
