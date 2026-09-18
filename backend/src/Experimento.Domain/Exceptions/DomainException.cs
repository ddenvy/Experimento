namespace Experimento.Domain.Exceptions;

/// <summary>
/// Нарушение доменного инварианта (например, EnsureValid сущности).
/// Семантически это некорректный запрос клиента (HTTP 400), а не серверная ошибка.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
