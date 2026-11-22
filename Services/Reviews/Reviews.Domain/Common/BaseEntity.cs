using MongoDB.Bson;

namespace Reviews.Domain.Common;

/// <summary>Базова сутність: Id (ObjectId), аудит і Version для оптимістичної конкурентності.</summary>
public abstract class BaseEntity
{
    public ObjectId Id { get; protected set; } = ObjectId.GenerateNewId();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    public int Version { get; protected set; } = 1;

    protected void Touch() => UpdatedAt = DateTime.UtcNow;

    /// <summary>Збільшується репозиторієм при кожному успішному оновленні.</summary>
    public void IncrementVersion() => Version++;
}

/// <summary>Базовий Value Object: порівняння за значеннями компонентів.</summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) =>
        other is not null && other.GetType() == GetType() && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override bool Equals(object? obj) => obj is ValueObject vo && Equals(vo);

    public override int GetHashCode() =>
        GetEqualityComponents().Aggregate(17, (hash, c) => HashCode.Combine(hash, c?.GetHashCode() ?? 0));

    public static bool operator ==(ValueObject? a, ValueObject? b) => a?.Equals(b) ?? b is null;
    public static bool operator !=(ValueObject? a, ValueObject? b) => !(a == b);
}

public class DomainException(string message) : Exception(message);
