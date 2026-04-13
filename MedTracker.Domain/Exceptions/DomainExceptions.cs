namespace MedTracker.Domain.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key)
        : base($"Entity \"{entityName}\" with key ({key}) was not found.") { }
}

public class DuplicateException : Exception
{
    public DuplicateException(string message) : base(message) { }
}

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Authentication required.")
        : base(message) { }
}

public class ForbiddenException : Exception
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message) { }
}

public class DomainValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public DomainValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors.AsReadOnly();
    }

    public DomainValidationException(string field, string error)
        : base(error)
    {
        Errors = new Dictionary<string, string[]> { { field, new[] { error } } }.AsReadOnly();
    }
}

public class ExcelImportException : Exception
{
    public ExcelImportException(string message) : base(message) { }
    public ExcelImportException(string message, Exception inner) : base(message, inner) { }
}