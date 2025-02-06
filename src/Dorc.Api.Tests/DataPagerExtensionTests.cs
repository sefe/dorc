using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class DataPagerExtensionTests
    {
        [TestMethod]
        public void Paginate_ShouldReturnCorrectNumbersIfTotalItemsLessThanPage()
        {
            // Arrange
            var data = Enumerable.Range(1, 9).Select(x => new TestModel { Id = x }).AsQueryable();
            int page = 1;
            int limit = 10;

            // Act
            var result = data.Paginate(page, limit);

            // Assert
            Assert.AreEqual(page, result.CurrentPage);
            Assert.AreEqual(limit, result.PageSize);
            Assert.AreEqual(9, result.Items.Count);
            Assert.AreEqual(9, result.TotalItems);
            Assert.AreEqual(1, result.TotalPages);
        }

        [TestMethod]
        public void Paginate_ShouldReturnCorrectNumbersForLastPage()
        {
            // Arrange
            var data = Enumerable.Range(1, 18).Select(x => new TestModel { Id = x }).AsQueryable();
            int page = 2;
            int limit = 10;

            // Act
            var result = data.Paginate(page, limit);

            // Assert
            Assert.AreEqual(page, result.CurrentPage);
            Assert.AreEqual(limit, result.PageSize);
            Assert.AreEqual(8, result.Items.Count);
            Assert.AreEqual(18, result.TotalItems);
            Assert.AreEqual(2, result.TotalPages);
        }

        [TestMethod]
        public void Paginate_ShouldReturnSmartTotalNumbersForMiddlePage()
        {
            // Arrange
            var data = Enumerable.Range(1, 28).Select(x => new TestModel { Id = x }).AsQueryable();
            int page = 2;
            int limit = 10;

            // Act
            var result = data.Paginate(page, limit);

            // Assert
            Assert.AreEqual(page, result.CurrentPage);
            Assert.AreEqual(limit, result.PageSize);
            Assert.AreEqual(limit, result.Items.Count);
            Assert.AreEqual(page * limit + 1, result.TotalItems);
            Assert.AreEqual(3, result.TotalPages);
        }

        [TestMethod]
        public void ContainsExpression_ShouldReturnValidExpressionForInt()
        {
            // Arrange
            var data = new List<TestModel>().AsQueryable();
            string propertyName = "Id";
            string propertyValue = "1";

            // Act
            var expression = data.ContainsExpression(propertyName, propertyValue);

            // Assert
            Assert.IsNotNull(expression);
            var compiledExpression = expression.Compile();
            Assert.IsTrue(compiledExpression(new TestModel { Id = 1 }));
            Assert.IsFalse(compiledExpression(new TestModel { Id = 2 }));
        }

        private class TestModel
        {
            public string Name { get; set; }
            public int Id { get; set; }
        }
    }
}
