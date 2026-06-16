namespace Dorc.Core.Tests
{
    [TestClass]
    public class LogSanitizerTests
    {
        [TestMethod]
        public void Null_RoundTrips()
        {
            Assert.IsNull(LogSanitizer.Sanitize(null));
        }

        [TestMethod]
        public void Empty_RoundTrips()
        {
            Assert.AreEqual(string.Empty, LogSanitizer.Sanitize(string.Empty));
        }

        [TestMethod]
        public void PrintableAscii_SurvivesUnchanged()
        {
            const string s = "Hello, World! 2026-05-28 abc XYZ";
            Assert.AreEqual(s, LogSanitizer.Sanitize(s));
        }

        [TestMethod]
        public void NonAscii_SurvivesUnchanged()
        {
            const string s = "naïve résumé — café";
            Assert.AreEqual(s, LogSanitizer.Sanitize(s));
        }

        [TestMethod]
        public void NewlineAndCarriageReturn_AreStripped()
        {
            var result = LogSanitizer.Sanitize("legit\r\nFAKE 401 Unauthorized");
            Assert.IsFalse(result!.Contains('\r'));
            Assert.IsFalse(result.Contains('\n'));
            Assert.AreEqual("legit__FAKE 401 Unauthorized", result);
        }

        [TestMethod]
        public void Tab_IsStripped()
        {
            Assert.AreEqual("a_b", LogSanitizer.Sanitize("a\tb"));
        }

        [TestMethod]
        public void Null_byte_IsStripped()
        {
            Assert.AreEqual("a_b", LogSanitizer.Sanitize("a\0b"));
        }

        [TestMethod]
        public void EscapeSequence_IsStripped()
        {
            // Common terminal escape: ESC + [ + 31m starts red text. The ESC (0x1B) must go.
            var result = LogSanitizer.Sanitize("user\x1B[31mEVIL\x1B[0m");
            Assert.IsFalse(result!.Contains('\x1B'));
            StringAssert.Contains(result, "EVIL"); // content stays; the control char is what we strip
        }

        [TestMethod]
        public void DelChar_IsStripped()
        {
            Assert.AreEqual("a_b", LogSanitizer.Sanitize("ab"));
        }

        [TestMethod]
        public void LongInput_IsTruncatedTo512()
        {
            var longInput = new string('x', 1000);
            var result = LogSanitizer.Sanitize(longInput);
            Assert.AreEqual(512, result!.Length);
        }

        [TestMethod]
        public void Multiline_CannotForgeLogLine()
        {
            // Realistic attacker payload: appended fake log entry after a newline.
            var attack = "alice\n2026-05-28 12:00 INFO [DOrc] admin-action by bob";
            var result = LogSanitizer.Sanitize(attack);
            Assert.IsFalse(result!.Contains('\n'), "newline survived the sanitiser");
            // The substitution char prevents a parser from misinterpreting as a new entry.
            StringAssert.StartsWith(result, "alice_");
        }
    }
}
