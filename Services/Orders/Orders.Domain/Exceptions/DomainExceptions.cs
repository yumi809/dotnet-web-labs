namespace Orders.Domain.Exceptions;

/// <summary>Базовий доменний виняток сервісу Замовлень.</summary>
public abstract class DomainException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Сутність не знайдено → HTTP 404.</summary>
public sealed class NotFoundException(string entity, object key)
    : DomainException($"{entity} with key '{key}' was not found")
{
    public string Entity { get; } = entity;
    public object Key { get; } = key;
}

/// <summary>Порушено бізнес-інваріант (недопустимий перехід статусу, дубль, конкурентний запис) → HTTP 409.</summary>
public sealed class BusinessConflictException(string message, Exception? inner = null) : DomainException(message, inner);

/// <summary>Порушено бізнес-правило валідації, що потребує стану БД → HTTP 400.</summary>
public sealed class DomainValidationException : DomainException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public DomainValidationException(string field, string error)
        : base(error) => Errors = new Dictionary<string, string[]> { [field] = [error] };

    public DomainValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred") => Errors = errors;
}
