using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NSubstitute;

namespace Experimento.Application.Tests;

/// <summary>
/// In-memory <see cref="DbSet{TEntity}"/> с асинхронным LINQ-провайдером для юнит-тестов
/// хендлеров: обычная NSubstitute-подделка DbSet не поддерживает AnyAsync/ToListAsync.
/// Реализация — канонический паттерн из документации EF Core по тестированию.
/// </summary>
internal static class TestDbSet
{
    public static DbSet<T> Create<T>(IEnumerable<T>? items = null) where T : class
    {
        var data = new List<T>(items ?? []);
        var queryable = data.AsQueryable();

        var set = Substitute.For<DbSet<T>, IAsyncEnumerable<T>, IQueryable<T>>();

        // Expression отдаём внутреннего списка — тогда Any/First выполняются над data.
        ((IQueryable<T>)set).Provider.Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        ((IQueryable<T>)set).Expression.Returns(queryable.Expression);
        ((IQueryable<T>)set).ElementType.Returns(queryable.ElementType);
        ((IQueryable<T>)set).GetEnumerator().Returns(_ => data.GetEnumerator());
        ((IAsyncEnumerable<T>)set)
            .GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(_ => new TestAsyncEnumerator<T>(data.GetEnumerator()));

        set.Add(Arg.Do<T>(data.Add));
        set.When(s => s.AddRange(Arg.Any<IEnumerable<T>>()))
            .Do(ci => data.AddRange(ci.Arg<IEnumerable<T>>()));
        return set;
    }

    private sealed class TestAsyncQueryProvider<TEntity>(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression) => inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = typeof(IQueryProvider)
                .GetMethods()
                .First(m => m.Name == nameof(IQueryProvider.Execute) && m.IsGenericMethod)
                .MakeGenericMethod(resultType)
                .Invoke(this, [expression]);

            return typeof(Task).IsAssignableFrom(typeof(TResult))
                ? (TResult)Task.FromResult((dynamic?)executionResult)
                : (TResult)executionResult!;
        }
    }

    private sealed class TestAsyncEnumerable<T>(Expression expression)
        : EnumerableQuery<T>(expression), IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable) : this(enumerable.AsQueryable().Expression) { }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

    private sealed class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;
        public ValueTask<bool> MoveNextAsync() => new(inner.MoveNext());
        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
