using System.Linq.Expressions;
using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources
{
    public class PagingPersistentSourceBase
    {
        protected static Dictionary<string, EnvironmentPrivInfo> GetEnvironmentPrivInfos(
            string username,
            IDeploymentContext context,
            IEnumerable<string> environments
            )
        {
            var userSids = username.GetSidsForUser();
            var envGroups = (from ed in context.Environments
                join environment in context.Environments on ed.Name equals environment.Name
                join ac in context.AccessControls on environment.ObjectId equals ac.ObjectId
                    into accessControlEnvironments
                from allAccessControlEnvironments in accessControlEnvironments.DefaultIfEmpty()
                where environments.Contains(ed.Name)
                let isOwner = ed.Owner == username
                let isDelegate =
                    (from envDetail in context.Environments
                        join env in context.Environments on envDetail.Name equals env.Name
                        where env.Name == environment.Name &&
                              envDetail.Users.Select(u => u.LoginId).Contains(username)
                        select envDetail.Name).Any()
                let hasPermission = (from envDetail in context.Environments
                    join env in context.Environments on envDetail.Name equals env.Name
                    join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                    where env.Name == environment.Name && userSids.Contains(ac.Sid) &&
                          (ac.Allow & (int)AccessLevel.Write) != 0
                    select ed.Name).Any()
                select new EnvironmentPrivInfo
                {
                    Environment = ed,
                    IsOwner = isOwner,
                    IsDelegate = isDelegate,
                    HasPermission = hasPermission
                }).GroupBy(info => info.Environment.Name);


            // Need to collapse environments down to singular objects
            var envPrivilegeInfos = new Dictionary<string, EnvironmentPrivInfo>();
            foreach (var envGroup in envGroups)
            {
                var epi = envGroup.First();
                envPrivilegeInfos.Add(envGroup.Key, new EnvironmentPrivInfo
                {
                    Environment = epi.Environment,
                    IsOwner = epi.IsOwner,
                    IsDelegate = epi.IsDelegate,
                    HasPermission = envGroup.Any(i => i.HasPermission)
                });
            }

            return envPrivilegeInfos;
        }

        protected static Dictionary<string, EnvironmentPrivInfo> GetEnvironmentPrivInfos(
            string username,
            IDeploymentContext context
            )
        {
            var userSids = username.GetSidsForUser();

            var envGroups = (from ed in context.Environments
                join environment in context.Environments on ed.Name equals environment.Name
                join ac in context.AccessControls on environment.ObjectId equals ac.ObjectId
                    into accessControlEnvironments
                from allAccessControlEnvironments in accessControlEnvironments.DefaultIfEmpty()
                let isOwner = ed.Owner == username
                let isDelegate =
                    (from envDetail in context.Environments
                        join env in context.Environments on envDetail.Name equals env.Name
                        where env.Name == environment.Name &&
                              envDetail.Users.Select(u => u.LoginId).Contains(username)
                        select envDetail.Name).Any()
                let hasPermission = (from envDetail in context.Environments
                    join env in context.Environments on envDetail.Name equals env.Name
                    join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                    where env.Name == environment.Name && userSids.Contains(ac.Sid) &&
                          (ac.Allow & (int)AccessLevel.Write) != 0
                    select ed.Name).Any()
                select new EnvironmentPrivInfo
                {
                    Environment = ed,
                    IsOwner = isOwner,
                    IsDelegate = isDelegate,
                    HasPermission = hasPermission
                }).GroupBy(info => info.Environment.Name);


            // Need to collapse environments down to singular objects
            var envPrivilegeInfos = new Dictionary<string, EnvironmentPrivInfo>();
            foreach (var envGroup in envGroups)
            {
                var epi = envGroup.First();
                envPrivilegeInfos.Add(envGroup.Key, new EnvironmentPrivInfo
                {
                    Environment = epi.Environment,
                    IsOwner = epi.IsOwner,
                    IsDelegate = epi.IsDelegate,
                    HasPermission = envGroup.Any(i => i.HasPermission)
                });
            }

            return envPrivilegeInfos;
        }

        protected static Expression<Func<T, R>> GetExpressionForOrdering<T,R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<T, R>>(prop, param);
        }

        protected IQueryable<T> WhereAll<T>(
            IQueryable<T> source,
            params Expression<Func<T, bool>>[] predicates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            if (predicates.Length == 0) return source.Where(x => false); // no matches!
            if (predicates.Length == 1) return source.Where(predicates[0]); // simple

            Expression<Func<T, bool>> pred = null;
            for (var i = 0; i < predicates.Length; i++)
            {
                pred = pred == null
                    ? predicates[i]
                    : pred.And(predicates[i]);
            }
            return source.Where(pred);
        }

        protected static IOrderedQueryable<T> OrderScripts<T, R>(PagedDataOperators operators, int i, IOrderedQueryable<T> orderedQuery,
            IQueryable<T> scriptsQuery, Expression<Func<T, R>> expr)
        {
            if (i == 0)
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = scriptsQuery.OrderBy(expr);
                        break;

                    case "desc":
                        orderedQuery = scriptsQuery.OrderByDescending(expr);
                        break;
                }
            else
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = orderedQuery?.ThenBy(expr);
                        break;

                    case "desc":
                        orderedQuery = orderedQuery?.ThenByDescending(expr);
                        break;
                }

            return orderedQuery;
        }
    }
}
