using System.ComponentModel.DataAnnotations;
using Dorc.ApiModel;

namespace Dorc.Api.Tests
{
    /// <summary>
    /// SC-1 boundary contract (docs/tag-capacity-expansion, IS S-003): the DTO
    /// validation attributes accept exactly N and reject N+1 with a readable message.
    /// [ApiController] on RefDataServersController / RefDataDatabasesController turns
    /// these validation failures into automatic 400 responses at the API boundary.
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
        public void ServerTags_AtLimit_Valid()
        {
            var model = new ServerApiModel { Name = "s", ApplicationTags = new string('a', TagLimits.MaxTagStringLength) };

            Assert.AreEqual(0, Validate(model).Count);
        }

        [TestMethod]
        public void ServerTags_OverLimit_InvalidWithReadableMessage()
        {
            var model = new ServerApiModel { Name = "s", ApplicationTags = new string('a', TagLimits.MaxTagStringLength + 1) };

            var results = Validate(model);
            Assert.AreEqual(1, results.Count);
            StringAssert.Contains(results[0].ErrorMessage, "4000");
            CollectionAssert.Contains(results[0].MemberNames.ToList(), nameof(ServerApiModel.ApplicationTags));
        }

        [TestMethod]
        public void DatabaseTags_AtLimit_Valid()
        {
            var model = new DatabaseApiModel { Name = "d", ArrayName = new string('a', TagLimits.MaxTagStringLength) };

            Assert.AreEqual(0, Validate(model).Count);
        }

        [TestMethod]
        public void DatabaseTags_OverLimit_InvalidWithReadableMessage()
        {
            var model = new DatabaseApiModel { Name = "d", ArrayName = new string('a', TagLimits.MaxTagStringLength + 1) };

            var results = Validate(model);
            Assert.AreEqual(1, results.Count);
            StringAssert.Contains(results[0].ErrorMessage, "4000");
            CollectionAssert.Contains(results[0].MemberNames.ToList(), nameof(DatabaseApiModel.ArrayName));
        }

        [TestMethod]
        public void MappingRoundTrip_BeyondOldCeiling_Unmodified()
        {
            // SC-3's mocked half: a value past the old effective 1000-char server
            // ceiling survives DTO assignment/copy untouched.
            var tags = string.Join(";", Enumerable.Range(0, 150).Select(i => $"tag-{i:D4}-abcdefghij"));
            Assert.IsTrue(tags.Length > 1000 && tags.Length <= TagLimits.MaxTagStringLength);

            var model = new ServerApiModel { Name = "s", ApplicationTags = tags };
            var copy = new ServerApiModel { Name = model.Name, ApplicationTags = model.ApplicationTags };

            Assert.AreEqual(tags, copy.ApplicationTags);
            Assert.AreEqual(0, Validate(copy).Count);
        }
    }
}
