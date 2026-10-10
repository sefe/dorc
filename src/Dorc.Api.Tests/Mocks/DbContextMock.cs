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

            ((IQueryable<T>)dbSet).Provider.Returns(queryable.Provider);
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
    }
}
