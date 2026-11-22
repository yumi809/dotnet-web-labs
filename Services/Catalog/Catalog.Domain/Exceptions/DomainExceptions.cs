namespace Catalog.Domain.Exceptions;

/// <summary>Порушення доменного інваріанту (некоректна ціна, недостатній запас тощо) → 409.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Сутність не знайдено → 404.</summary>
public sealed class NotFoundException(string entity, object key) : Exception($"{entity} with key '{key}' was not found");

/// <summary>Конфлікт з поточним станом БД (унікальність, конкурентний запис) → 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
