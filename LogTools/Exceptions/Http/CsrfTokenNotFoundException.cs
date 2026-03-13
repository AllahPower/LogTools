using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public sealed class CsrfTokenNotFoundException : LogsParserHttpException
{
    public CsrfTokenNotFoundException()
    {
    }

    public CsrfTokenNotFoundException(string message)
        : base(message)
    {
    }

    public CsrfTokenNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    private CsrfTokenNotFoundException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
