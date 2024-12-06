﻿using Dorc.PersistentData.Model;
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

            paged.TotalItems = query.Count();
            paged.TotalPages = (int)Math.Ceiling(paged.TotalItems / (double)limit);

            return paged;
        }

        public static Expression<Func<T, bool>> ContainsExpression<T>(this IQueryable<T> source, string propertyName,
            string propertyValue)
        {
            var parameterExp = Expression.Parameter(typeof(T), "type");
            var propertyExp = Expression.Property(parameterExp, propertyName);
            if (propertyExp.Type == typeof(string))
            {
                var method = typeof(string).GetMethod("Contains", new[] { typeof(string) });
                var someValue = Expression.Constant(propertyValue, typeof(string));
                var containsMethodExp = Expression.Call(propertyExp, method, someValue);

                return Expression.Lambda<Func<T, bool>>(containsMethodExp, parameterExp);
            }

            if (propertyExp.Type == typeof(int))
            {
                if (!int.TryParse(propertyValue, out var intPropVal))
                    throw new Exception("Invalid value detected for column");

                var method = typeof(int).GetMethod("Equals", new[] { typeof(int) });
                var someValue = Expression.Constant(intPropVal, typeof(int));
                var containsMethodExp = Expression.Call(propertyExp, method, someValue);

                return Expression.Lambda<Func<T, bool>>(containsMethodExp, parameterExp);
            }

            return null;
        }
    }

}
