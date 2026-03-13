using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public sealed class AuthenticationRequiredException : LogsParserHttpException
{
    public AuthenticationRequiredException()
    {
    }

    public AuthenticationRequiredException(string message)
        : base(message)
    {
    }

    public AuthenticationRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    private AuthenticationRequiredException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
