using System.ComponentModel.DataAnnotations;
using Dorc.ApiModel;

namespace Dorc.Api.Tests
{
    /// <summary>
    /// The tag boundary contract. Tags are stored as rows, so the primary limit is
    /// per tag rather than on a joined total: a caller may send far more tags than
    /// the old delimited column could hold, provided each fits
    /// deploy.DatabaseTag.Tag / deploy.ServerTag.Tag, and no tag may carry the
    /// delimiter the deployment-variable payload still uses to join them.
    ///
    /// One ceiling survives while the deprecated delimited columns are still
    /// dual-written: the joined form has to fit them. That is enforced here so it
    /// surfaces as a validation message against the field rather than as a
    /// truncation error out of SaveChanges, and both the check and this test go
    /// when the columns do.
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
        public void ManyTags_AreNotCappedByThePerTagLimit()
        {
            // 200 tags is far more than the old delimited column's 4000 characters
            // would have allowed at the per-tag width, and every one of them is
            // accepted: the ceiling is per tag, not on how many there are.
            var tags = Enumerable.Range(0, 200).Select(i => $"tag-{i:D4}-abcdefghij").ToArray();
            Assert.IsTrue(string.Join(";", tags).Length <= TagLimits.MaxTagStringLength);

            var model = new DatabaseApiModel { Name = "d", Tags = tags };

            Assert.AreEqual(0, Validate(model).Count);
        }

        [TestMethod]
        public void TagsExceedingTheDeprecatedColumnOnceJoined_AreRejectedNotTruncated()
        {
            // Each tag is individually valid, but SyncTagLinks still writes the joined
            // form to the deprecated NVARCHAR(4000) column. Without this check the
            // request reaches SaveChanges and comes back as SQL 8152 "String or binary
            // data would be truncated" — a 500 with nothing pointing at the field.
            var tags = Enumerable.Range(0, 500).Select(i => $"tag-{i:D4}-abcdefghij").ToArray();
            Assert.IsTrue(tags.All(t => t.Length <= TagLimits.MaxTagLength));
            Assert.IsTrue(string.Join(";", tags).Length > TagLimits.MaxTagStringLength);

            var model = new DatabaseApiModel { Name = "d", Tags = tags };

            var results = Validate(model);
            Assert.AreEqual(1, results.Count);
            StringAssert.Contains(results[0].ErrorMessage, TagLimits.MaxTagStringLength.ToString());
        }
    }
}
