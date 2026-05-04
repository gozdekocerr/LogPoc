namespace Core.Application.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public sealed class ValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ValidationException(IReadOnlyList<string> errors)
        : base(string.Join("; ", errors))
    {
        Errors = errors;
    }
}

public sealed class DuplicateEmailException : Exception
{
    public string Email      { get; }
    public Guid   ExistingId { get; }

    public DuplicateEmailException(string email, Guid existingId)
        : base($"'{email}' e-posta adresi zaten Id={existingId} müşterisine aittir.")
    {
        Email      = email;
        ExistingId = existingId;
    }
}

public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
