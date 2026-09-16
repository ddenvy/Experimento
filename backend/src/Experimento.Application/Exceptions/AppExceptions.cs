namespace Experimento.Application.Exceptions;

/// <summary>Запрашиваемая сущность не найдена (HTTP 404).</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Конфликт состояния, например дубль по уникальному ключу (HTTP 409).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Доступ к ресурсу запрещён его владельцем/ролью (HTTP 403).</summary>
public sealed class ForbiddenException(string message = "Access to this resource is forbidden.") : Exception(message);

/// <summary>Некорректный запрос клиента (HTTP 400).</summary>
public sealed class BadRequestException(string message) : Exception(message);
