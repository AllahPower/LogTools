using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public class LogsParserException : Exception
{
    public LogsParserException()
    {
    }

    public LogsParserException(string message)
        : base(message)
    {
    }

    public LogsParserException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    protected LogsParserException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
