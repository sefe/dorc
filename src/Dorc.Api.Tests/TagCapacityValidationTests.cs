using System.ComponentModel.DataAnnotations;
using Dorc.ApiModel;

namespace Dorc.Api.Tests
{
    /// <summary>
    /// The tag boundary contract. Tags are stored as rows, so the limit is per tag
    /// rather than on a joined total: a caller may send any number of tags provided
    /// each fits deploy.DatabaseTag.Tag / deploy.ServerTag.Tag, and no tag may carry
    /// the delimiter the deployment-variable payload still uses to join them.
    /// </summary>
    [TestClass]
    public class TagCapacityValidationTests
    {
        private static List<ValidationResult> Validate(object model)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
            return results;
        }

        [TestMethod]
        public void ServerTag_AtLimit_Valid()
        {
            var model = new ServerApiModel { Name = "s", Tags = new[] { new string('a', TagLimits.MaxTagLength) } };

            Assert.AreEqual(0, Validate(model).Count);
        }

        [TestMethod]
        public void ServerTag_OverLimit_InvalidWithReadableMessage()
        {
            var model = new ServerApiModel { Name = "s", Tags = new[] { new string('a', TagLimits.MaxTagLength + 1) } };

            var results = Validate(model);
            Assert.AreEqual(1, results.Count);
            StringAssert.Contains(results[0].ErrorMessage, TagLimits.MaxTagLength.ToString());
            CollectionAssert.Contains(results[0].MemberNames.ToList(), nameof(ServerApiModel.Tags));
        }

        [TestMethod]
        public void DatabaseTag_OverLimit_Invalid()
        {
            var model = new DatabaseApiModel { Name = "d", Tags = new[] { new string('a', TagLimits.MaxTagLength + 1) } };

            Assert.AreEqual(1, Validate(model).Count);
        }

        [TestMethod]
        public void TagContainingTheDelimiter_IsRejected()
        {
            // It would round-trip as two tags through the still-delimited
            // deployment-variable payload.
            var model = new DatabaseApiModel { Name = "d", Tags = new[] { "Endur;Reporting" } };

            var results = Validate(model);
            Assert.AreEqual(1, results.Count);
            StringAssert.Contains(results[0].ErrorMessage, ";");
        }

        [TestMethod]
        public void ManyTags_AreNotCappedByAJoinedTotal()
        {
            // The old delimited column capped the JOINED string at 4000 characters.
            // Rows have no such ceiling: 500 tags is fine as long as each one fits.
            var tags = Enumerable.Range(0, 500).Select(i => $"tag-{i:D4}-abcdefghij").ToArray();
            Assert.IsTrue(string.Join(";", tags).Length > TagLimits.MaxTagStringLength);

            var model = new DatabaseApiModel { Name = "d", Tags = tags };

            Assert.AreEqual(0, Validate(model).Count);
        }
    }
}
