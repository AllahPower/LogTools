using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public sealed class HtmlParsingException : LogsParserException
{
    public HtmlParsingException()
    {
    }

    public HtmlParsingException(string message)
        : base(message)
    {
    }

    public HtmlParsingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    private HtmlParsingException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
