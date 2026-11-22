namespace Reviews.Application.Common;

public sealed class NotFoundException(string entity, object key) : Exception($"{entity} '{key}' was not found");

/// <summary>Конфлікт версії (оптимістична конкурентність) або бізнес-конфлікт → 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
