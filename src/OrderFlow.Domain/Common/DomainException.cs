namespace OrderFlow.Domain.Common;

/// <summary>A business rule was violated. Mapped to a 4xx response at the API boundary.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DomainException()
    {
    }
}
