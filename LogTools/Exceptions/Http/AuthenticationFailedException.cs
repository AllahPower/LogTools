using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public sealed class AuthenticationFailedException : LogsParserHttpException
{
    public AuthenticationFailedException()
    {
    }

    public AuthenticationFailedException(string message)
        : base(message)
    {
    }

    public AuthenticationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    private AuthenticationFailedException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
