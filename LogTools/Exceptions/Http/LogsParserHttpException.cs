using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public class LogsParserHttpException : LogsParserException
{
    public LogsParserHttpException()
    {
    }

    public LogsParserHttpException(string message)
        : base(message)
    {
    }

    public LogsParserHttpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    protected LogsParserHttpException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
