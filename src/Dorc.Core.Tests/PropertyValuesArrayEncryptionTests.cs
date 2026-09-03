using System.Text.Json;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources;

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
    }
}
