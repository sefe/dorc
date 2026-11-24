using System.Data;
using System.Text.Json;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class PropertyValuesArrayEncryptionTests
    {
        // Reversible stand-in for the real encryptor so we can assert round-trip behaviour.
        private sealed class ReversibleEncryptor : IPropertyEncryptor
        {
            public string EncryptValue(string value) => "enc:" + value;
            public string? DecryptValue(string value) =>
                value != null && value.StartsWith("enc:") ? value.Substring(4) : value;
        }

        [TestMethod]
        public void CachedProperties_CaseInsensitiveLookup_MatchesDifferentCasing()
        {
            using var table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Secure", typeof(bool));
            table.Columns.Add("IsArray", typeof(bool));
            table.Columns.Add("Value", typeof(string));
            table.Rows.Add("SecureProp", true, false, "enc:secret-val");

            using var dataSet = new DataSet();
            dataSet.Tables.Add(table);

            var context = Substitute.For<IDeploymentContext>();
            context.GetGlobalProperties(null).Returns(dataSet);
            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            contextFactory.GetContext().Returns(context);

            var source = new PropertyValuesPersistentSource(
                contextFactory,
                new ReversibleEncryptor(),
                Substitute.For<IClaimsPrincipalReader>());

            source.LoadAllPropertiesIntoCache();

            Assert.IsTrue(source.IsCachedPropertySecure("SECUREPROP"));
            Assert.IsTrue(source.IsCachedPropertySecure("secureprop"));
            Assert.IsNotNull(source.GetCachedPropertyValue("SECUREPROP"));
            Assert.AreEqual("secret-val", source.GetCachedPropertyValue("secureprop").Value);
        }

        [TestMethod]
        public void EncryptArrayValue_EncryptsEachElement_NotTheWholeBlob()
        {
            var enc = new ReversibleEncryptor();
            var input = JsonSerializer.Serialize(new[] { "alpha", "beta", "gamma" });

            var result = PropertyValuesPersistentSource.EncryptArrayValue(input, enc);

            var items = JsonSerializer.Deserialize<string[]>(result)!;
            CollectionAssert.AreEqual(new[] { "enc:alpha", "enc:beta", "enc:gamma" }, items);
        }

        [TestMethod]
        public void EncryptThenDecryptArray_RoundTripsEachElement()
        {
            var enc = new ReversibleEncryptor();
            var original = new[] { "s3cr3t-1", "p@ss:word", "tok/en" };
            var input = JsonSerializer.Serialize(original);

            var encrypted = PropertyValuesPersistentSource.EncryptArrayValue(input, enc);
            var decrypted = PropertyValuesPersistentSource.DecryptArrayValue(encrypted, enc);

            var items = JsonSerializer.Deserialize<string[]>(decrypted)!;
            CollectionAssert.AreEqual(original, items);
        }

        [TestMethod]
        public void EncryptArrayValue_DistinctElementsProduceDistinctCiphertexts()
        {
            var enc = new ReversibleEncryptor();
            var input = JsonSerializer.Serialize(new[] { "one", "two" });

            var result = PropertyValuesPersistentSource.EncryptArrayValue(input, enc);

            var items = JsonSerializer.Deserialize<string[]>(result)!;
            Assert.AreNotEqual(items[0], items[1],
                "Each element must be encrypted individually, not the serialized array.");
        }

        [TestMethod]
        public void EncryptAndDecryptArrayValue_HandlesScalarInputAndLegacySingleBlobFallback()
        {
            var enc = new ReversibleEncryptor();

            var wrappedAndEncrypted = PropertyValuesPersistentSource.EncryptArrayValue("scalar-secret", enc);
            var items = JsonSerializer.Deserialize<string[]>(wrappedAndEncrypted)!;
            CollectionAssert.AreEqual(new[] { "enc:scalar-secret" }, items);

            var legacyDecrypted = PropertyValuesPersistentSource.DecryptArrayValue("enc:legacy-blob", enc);
            Assert.AreEqual("legacy-blob", legacyDecrypted);
        }
    }
}
