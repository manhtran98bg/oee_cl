namespace Rostek.Gateway.Domain.Exceptions;

public sealed class DomainException(string message) : InvalidOperationException(message);
