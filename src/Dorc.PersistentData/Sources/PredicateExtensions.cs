using System.Linq.Expressions;

namespace Dorc.PersistentData.Sources
{
    public static class PredicateExtensions
    {
        ///  <summary>
        /// Begin an expression chain
        ///  </summary>
        ///  <typeparam name="T"></typeparam>
        ///  <returns>A lambda expression stub</returns>
        public static Expression<Func<T, bool>> Begin<T>(bool value = false)
        {
            if (value)
                return parameter => true;  //value cannot be used in place of true/false

            return parameter => false;
        }

        public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> left,
           Expression<Func<T, bool>> right)
        {
            return left.CombineLambdas(right, ExpressionType.AndAlso);
        }

        public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        {
            return left.CombineLambdas(right, ExpressionType.OrElse);
        }

        #region private

        private static Expression<Func<T, bool>> CombineLambdas<T>(this Expression<Func<T, bool>> left,
           Expression<Func<T, bool>> right, ExpressionType expressionType)
        {
            //Remove expressions created with Begin<T>()
            if (IsExpressionBodyConstant(left))
                return right;

            var p = left.Parameters[0];

            var visitor = new SubstituteParameterVisitor
            {
                Sub =
                {
                    [right.Parameters[0]] = p
                }
            };

            Expression body = Expression.MakeBinary(expressionType, left.Body, visitor.Visit(right.Body));
            return Expression.Lambda<Func<T, bool>>(body, p);
        }

        private static bool IsExpressionBodyConstant<T>(Expression<Func<T, bool>> left)
        {
            return left.Body.NodeType == ExpressionType.Constant;
        }

        internal class SubstituteParameterVisitor : ExpressionVisitor
        {
            public Dictionary<Expression, Expression> Sub = new Dictionary<Expression, Expression>();

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (Sub.TryGetValue(node, out var newValue))
                {
                    return newValue;
                }
                return node;
            }
        }

        #endregion
    }
}
