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

        [TestMethod]
        [DataRow("\u0085", "NEL — a line terminator for .NET Regex multiline and JS log viewers")]
        [DataRow("\u009B", "CSI — single-char ANSI escape introducer; blocking ESC alone is not enough")]
        [DataRow("\u0080", "PAD — C1 control")]
        [DataRow("\u2028", "LINE SEPARATOR")]
        [DataRow("\u2029", "PARAGRAPH SEPARATOR")]
        [DataRow("\u202E", "RLO — visually reverses the rest of the audit line")]
        public void NonAsciiControlAndSeparators_AreStripped(string dangerous, string why)
        {
            var result = LogSanitizer.Sanitize("a" + dangerous + "b");

            Assert.AreEqual("a_b", result, why);
            Assert.IsFalse(result!.Contains(dangerous, StringComparison.Ordinal),
                $"{why}: character survived sanitisation");
        }

        [TestMethod]
        public void RealNames_WithNonAsciiLetters_SurviveUnchanged()
        {
            // The stripping must key off Unicode category, not "non-ASCII".
            Assert.AreEqual("Müller", LogSanitizer.Sanitize("Müller"));
            Assert.AreEqual("Łukasz", LogSanitizer.Sanitize("Łukasz"));
            Assert.AreEqual("José", LogSanitizer.Sanitize("José"));
        }

        [TestMethod]
        public void Truncation_DoesNotSplitSurrogatePair()
        {
            // 511 filler + a 2-char emoji: a naive 512 slice leaves a lone high surrogate,
            // which serialises downstream as U+FFFD.
            var input = new string('x', 511) + "\U0001F600";

            var result = LogSanitizer.Sanitize(input)!;

            Assert.IsFalse(char.IsHighSurrogate(result[^1]), "truncation left a dangling surrogate");
            Assert.AreEqual(511, result.Length);
        }

    }
}
