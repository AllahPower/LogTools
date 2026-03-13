using System.Runtime.Serialization;

namespace LogsParser.Exceptions;

[Serializable]
public sealed class AccountConfigurationException : LogsParserHttpException
{
    public AccountConfigurationException()
    {
    }

    public AccountConfigurationException(string message)
        : base(message)
    {
    }

    public AccountConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    private AccountConfigurationException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
