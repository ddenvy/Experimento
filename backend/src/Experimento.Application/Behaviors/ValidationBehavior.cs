using FluentValidation;
using MediatR;

namespace Experimento.Application.Behaviors;

/// <summary>
/// Выполняет все зарегистрированные FluentValidation-валидаторы запроса до вызова хэндлера.
/// </summary>
/// <remarks>
/// Без этого поведения валидаторы, объявленные в сборке, никогда не вызываются —
/// контроллеры MediatR не запускают их автоматически.
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (validators is not null)
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = validators
                .Select(v => v.Validate(context))
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count != 0)
                throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}
