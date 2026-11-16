using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Dorc.Api.Tests.Mocks
{
    public static class DbContextMock
    {
        public static DbSet<T> GetQueryableMockDbSet<T>(List<T> sourceList) where T : class
        {
            var queryable = sourceList.AsQueryable();
            var dbSet = Substitute.For<DbSet<T>, IQueryable<T>>();
            var provider = new CollateRewritingQueryProvider(queryable.Provider, dbSet, queryable);

            ((IQueryable<T>)dbSet).Provider.Returns(provider);
            ((IQueryable<T>)dbSet).Expression.Returns(queryable.Expression);
            ((IQueryable<T>)dbSet).ElementType.Returns(queryable.ElementType);
            // A fresh enumerator per call: query shapes that iterate a set more than
            // once (e.g. cross joins) would otherwise see an exhausted enumerator on
            // the second pass and silently drop rows.
            ((IQueryable<T>)dbSet).GetEnumerator().Returns(_ => queryable.GetEnumerator());
            dbSet.When(x => x.Add(Arg.Any<T>())).Do(call => sourceList.Add(call.Arg<T>()));
            dbSet.When(x => x.Remove(Arg.Any<T>())).Do(call => sourceList.Remove(call.Arg<T>()));

            return dbSet;
        }

        private sealed class CollateRewritingQueryProvider(IQueryProvider inner, object dbSet, IQueryable queryable) : IQueryProvider
        {
            private readonly CollateVisitor _visitor = new(dbSet, queryable);

            public IQueryable CreateQuery(Expression expression) =>
                inner.CreateQuery(_visitor.Visit(expression)!);

            public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
                new CollateRewritingQueryable<TElement>(inner.CreateQuery<TElement>(_visitor.Visit(expression)!), this);

            public object? Execute(Expression expression) =>
                inner.Execute(_visitor.Visit(expression)!);

            public TResult Execute<TResult>(Expression expression) =>
                inner.Execute<TResult>(_visitor.Visit(expression)!);
        }

        private sealed class CollateRewritingQueryable<T>(IQueryable<T> inner, IQueryProvider provider) : IOrderedQueryable<T>
        {
            public Type ElementType => inner.ElementType;
            public Expression Expression => inner.Expression;
            public IQueryProvider Provider => provider;
            public System.Collections.IEnumerator GetEnumerator() => inner.GetEnumerator();
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => inner.GetEnumerator();
        }

        private sealed class CollateVisitor(object dbSet, IQueryable queryable) : ExpressionVisitor
        {
            private static readonly System.Reflection.MethodInfo ToUpperInvariantMethod =
                typeof(string).GetMethod(nameof(string.ToUpperInvariant), Type.EmptyTypes)!;

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (ReferenceEquals(node.Value, dbSet))
                {
                    return Expression.Constant(queryable);
                }

                return base.VisitConstant(node);
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.DeclaringType == typeof(EF) &&
                    node.Method.Name == nameof(EF.Constant) &&
                    node.Arguments.Count == 1)
                {
                    return Visit(node.Arguments[0])!;
                }

                if (node.Method.Name == nameof(RelationalDbFunctionsExtensions.Collate) &&
                    node.Arguments.Count >= 2 &&
                    node.Arguments[1].Type == typeof(string))
                {
                    var operand = Visit(node.Arguments[1])!;
                    return Expression.Condition(
                        Expression.Equal(operand, Expression.Constant(null, typeof(string))),
                        Expression.Constant(null, typeof(string)),
                        Expression.Call(operand, ToUpperInvariantMethod));
                }

                return base.VisitMethodCall(node);
            }
        }
    }
}
