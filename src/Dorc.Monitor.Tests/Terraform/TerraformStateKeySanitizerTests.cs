using Dorc.Monitor.Terraform;

namespace Dorc.Monitor.Tests.Terraform
{
    [TestClass]
    public class TerraformStateKeySanitizerTests
    {
        // The sanitizer is injective modulo case-folding: every character
        // outside [a-z0-9-] is hex-escaped as _XXXX, so two distinct names
        // can only collide if they differ solely by case.

        [TestMethod]
        public void Sanitize_SpaceVersusHyphen_ProduceDifferentKeys()
        {
            var withSpace = TerraformStateKeySanitizer.Sanitize("Prod EU");
            var withHyphen = TerraformStateKeySanitizer.Sanitize("Prod-EU");

            Assert.AreNotEqual(withHyphen, withSpace,
                "'Prod EU' and 'Prod-EU' are distinct names and must map to distinct state keys.");
            Assert.AreEqual("prod_0020eu", withSpace, "Space (U+0020) is hex-escaped as _0020.");
            Assert.AreEqual("prod-eu", withHyphen, "Hyphen is in the pass-through charset.");
        }

        [TestMethod]
        public void Sanitize_UnderscoreVersusHyphen_ProduceDifferentKeys()
        {
            var withUnderscore = TerraformStateKeySanitizer.Sanitize("Prod_EU");
            var withHyphen = TerraformStateKeySanitizer.Sanitize("Prod-EU");

            Assert.AreNotEqual(withHyphen, withUnderscore,
                "'Prod_EU' and 'Prod-EU' are distinct names and must map to distinct state keys.");
            Assert.AreEqual("prod_005feu", withUnderscore,
                "Underscore is the escape character itself and must be hex-escaped as _005f.");
        }

        [TestMethod]
        public void Sanitize_DiffersOnlyByCase_ProducesSameKey()
        {
            Assert.AreEqual(
                TerraformStateKeySanitizer.Sanitize("prod"),
                TerraformStateKeySanitizer.Sanitize("PROD"),
                "Names differing only by case share a state key (names are unique case-insensitively in DOrc).");
        }

        [TestMethod]
        public void Sanitize_PlainLowercaseNameWithDigitsAndHyphens_PassesThroughUnchanged()
        {
            Assert.AreEqual("prod-eu-1", TerraformStateKeySanitizer.Sanitize("prod-eu-1"));
        }

        [TestMethod]
        public void Sanitize_NullOrEmpty_ReturnsUnknown()
        {
            Assert.AreEqual("unknown", TerraformStateKeySanitizer.Sanitize(string.Empty));
            Assert.AreEqual("unknown", TerraformStateKeySanitizer.Sanitize(null!));
        }
    }
}
