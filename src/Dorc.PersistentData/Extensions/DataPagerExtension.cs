using Dorc.PersistentData.Model;
using System.Linq.Expressions;

namespace Dorc.PersistentData.Extensions
{
    public static class DataPagerExtension
    {
        public static PagedModel<TModel> Paginate<TModel>(
            this IQueryable<TModel> query,
            int page,
            int limit)
            where TModel : class
        {

            var paged = new PagedModel<TModel>();

            page = page < 0 ? 1 : page;

            paged.CurrentPage = page;
            paged.PageSize = limit;

            var startRow = (page - 1) * limit;
            paged.Items = query.Skip(startRow).Take(limit).ToList();

            // as appeared query.Count() is too heavy operation for big tables with joins so
            // returning +1 item more than currently loaded items as TotalItems
            var smartTotalItems = (paged.Items.Count == limit) ? page * limit + 1 : startRow + paged.Items.Count;
            paged.TotalItems = smartTotalItems;
            paged.TotalPages = (int)Math.Ceiling(paged.TotalItems / (double)limit);

            return paged;
        }

        public static Expression<Func<T, bool>> ContainsExpression<T>(this IQueryable<T> source, string propertyName,
            string propertyValue)
        {
            var parameterExp = Expression.Parameter(typeof(T), "type");

            if (typeof(T).GetProperty(propertyName) == null)
            {
                throw new ArgumentException($"Filter by '{propertyName}' has failed: type '{typeof(T).Name}' does not have property '{propertyName}'"); // propertyName not exists in T
            }

            var propertyExp = Expression.Property(parameterExp, propertyName);
            
            if (propertyExp.Type == typeof(string))
            {
                var method = typeof(string).GetMethod("Contains", new[] { typeof(string) });

                var containsMethodExp = Expression.Call(propertyExp, method, Expression.Invoke(() => propertyValue));

                return Expression.Lambda<Func<T, bool>>(containsMethodExp, parameterExp);
            }

            if (propertyExp.Type == typeof(int))
            {
                if (!int.TryParse(propertyValue, out var intPropVal))
                    throw new ArgumentException($"Invalid value detected for column '{propertyName}', must be integer");

                var method = typeof(int).GetMethod("Equals", new[] { typeof(int) });

                var containsMethodExp = Expression.Call(propertyExp, method, Expression.Invoke(() => intPropVal));

                return Expression.Lambda<Func<T, bool>>(containsMethodExp, parameterExp);
            }

            return null;
        }

        public static Expression<Func<T, bool>> StartsWithExpression<T>(this IQueryable<T> source, string propertyName,
            string propertyValue)
        {
            var parameterExp = Expression.Parameter(typeof(T), "type");

            if (typeof(T).GetProperty(propertyName) == null)
            {
                throw new ArgumentException($"Filter by '{propertyName}' has failed: type '{typeof(T).Name}' does not have property '{propertyName}'");
            }

            var propertyExp = Expression.Property(parameterExp, propertyName);

            // SARGable prefix predicate. EF Core compiles string.StartsWith into LIKE '@p%',
            // which uses an index seek when a supporting non-clustered index exists on the
            // column. Only string columns make sense here -- non-string properties return
            // null (callers skip such filters).
            if (propertyExp.Type == typeof(string))
            {
                var method = typeof(string).GetMethod("StartsWith", new[] { typeof(string) });

                var startsWithMethodExp = Expression.Call(propertyExp, method, Expression.Invoke(() => propertyValue));

                return Expression.Lambda<Func<T, bool>>(startsWithMethodExp, parameterExp);
            }

            return null;
        }
    }

}
