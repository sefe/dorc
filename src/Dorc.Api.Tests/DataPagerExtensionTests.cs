using Dorc.PersistentData.Extensions;

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

        private class TestModel
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public DateTime CreatedDate { get; set; } // this is needed to test unsupported by filter property type (DateTime)
        }

        [TestMethod]
        public void ContainsExpression_ShouldReturnValidExpressionForInt()
        {
            // Arrange
            var data = new List<TestModel>().AsQueryable();

            // Act
            var expression = data.ContainsExpression("Id", "1");

            // Assert
            Assert.IsNotNull(expression);
            var compiledExpression = expression.Compile();

            Assert.IsTrue(compiledExpression(new TestModel { Id = 1 }));
            Assert.IsFalse(compiledExpression(new TestModel { Id = 2 }));
        }

        [TestMethod]
        public void ContainsExpression_ShouldReturnValidExpressionForString()
        {
            // Arrange
            var data = new List<TestModel>().AsQueryable();

            // Act
            var expression = data.ContainsExpression("Name", "Test");

            // Assert
            Assert.IsNotNull(expression);
            var compiledExpression = expression.Compile();

            var model = new TestModel();
            Assert.IsTrue(compiledExpression(new TestModel { Name = "Test" }));
            Assert.IsFalse(compiledExpression(new TestModel { Name = "test" })); // case-sensitive check
            Assert.IsFalse(compiledExpression(new TestModel { Name = "noexists" }));
        }

        [TestMethod]
        public void ContainsExpression_ShouldHandleEmptyValuesForString()
        {
            // Arrange
            var data = new List<TestModel>().AsQueryable();
            string propertyName = "Name";
            string propertyValue = "Test";

            // Act
            var expression = data.ContainsExpression(propertyName, propertyValue);

            // Assert
            Assert.IsNotNull(expression);
            var compiledExpression = expression.Compile();
            Assert.IsFalse(compiledExpression(new TestModel { Name = "" }));
        }

        [TestMethod]
        public void ContainsExpression_ShouldReturnNullForUnsupportedPropertyType()
        {
            // Arrange
            var data = new List<TestModel>().AsQueryable();
            string propertyName = "CreatedDate"; // DateTime property (unsupported)
            string propertyValue = "2023-01-01";

            // Act
            var expression = data.ContainsExpression(propertyName, propertyValue);

            // Assert
            Assert.IsNull(expression);
        }

        [TestMethod]
        public void ContainsExpression_ShouldThrowForInvalidPropertyName()
        {
            // Arrange
            var data = new List<TestModel>().AsQueryable();

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                var expression = data.ContainsExpression("InvalidProperty", "Test");
            });
        }

        [TestMethod]
        public void StartsWithExpression_ShouldReturnValidExpressionForString()
        {
            var data = new List<TestModel>().AsQueryable();

            var expression = data.StartsWithExpression("Name", "Pro");

            Assert.IsNotNull(expression);
            var compiledExpression = expression.Compile();

            Assert.IsTrue(compiledExpression(new TestModel { Name = "ProjectAlpha" }));
            Assert.IsTrue(compiledExpression(new TestModel { Name = "Pro" }));
            Assert.IsFalse(compiledExpression(new TestModel { Name = "MyProject" })); // substring no longer matches
            Assert.IsFalse(compiledExpression(new TestModel { Name = "pro" })); // case-sensitive
        }

        [TestMethod]
        public void StartsWithExpression_NarrowerThanContains_OnPrefixOverlap()
        {
            // PROD vs PROD-NA: equality/prefix narrower than substring; documents the
            // intentional behaviour change for SC5.
            var data = new List<TestModel>().AsQueryable();

            var expression = data.StartsWithExpression("Name", "PROD-NA");
            var compiled = expression.Compile();

            // PROD-NA prefix returns rows starting with PROD-NA
            Assert.IsTrue(compiled(new TestModel { Name = "PROD-NA" }));
            Assert.IsTrue(compiled(new TestModel { Name = "PROD-NA-DR" }));
            // PROD does NOT start with PROD-NA -- previously substring would have not matched either,
            // but a partial-match scenario like "TIER1-PROD-NA" (substring would match, prefix would not)
            // is also excluded:
            Assert.IsFalse(compiled(new TestModel { Name = "PROD" }));
            Assert.IsFalse(compiled(new TestModel { Name = "TIER1-PROD-NA" }));
        }

        [TestMethod]
        public void StartsWithExpression_ShouldReturnNullForUnsupportedPropertyType()
        {
            var data = new List<TestModel>().AsQueryable();

            // int and DateTime properties are not supported by StartsWithExpression.
            var intExpression = data.StartsWithExpression("Id", "1");
            var dateExpression = data.StartsWithExpression("CreatedDate", "2026-01-01");

            Assert.IsNull(intExpression);
            Assert.IsNull(dateExpression);
        }

        [TestMethod]
        public void StartsWithExpression_ShouldThrowForInvalidPropertyName()
        {
            var data = new List<TestModel>().AsQueryable();

            Assert.Throws<ArgumentException>(() =>
            {
                var expression = data.StartsWithExpression("InvalidProperty", "Test");
            });
        }
    }
}
