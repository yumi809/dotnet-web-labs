using MediatR;

namespace Reviews.Application.Abstractions;

/// <summary>Команда без результату (Delete тощо).</summary>
public interface ICommand : IRequest;

/// <summary>Команда з результатом (Create повертає Id).</summary>
public interface ICommand<out TResult> : IRequest<TResult>;

/// <summary>Запит (читання) — лише повертає дані, не змінює стан.</summary>
public interface IQuery<out TResult> : IRequest<TResult>;
