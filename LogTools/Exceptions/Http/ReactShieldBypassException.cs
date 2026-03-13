using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public sealed class ReactShieldBypassException : LogsParserHttpException
{
    public ReactShieldBypassException()
    {
    }

    public ReactShieldBypassException(string message)
        : base(message)
    {
    }

    public ReactShieldBypassException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    private ReactShieldBypassException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
