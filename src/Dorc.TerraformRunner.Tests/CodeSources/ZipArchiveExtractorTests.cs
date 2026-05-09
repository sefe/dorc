using System.IO.Compression;
using Dorc.TerraformRunner.CodeSources;

namespace Dorc.TerraformRunner.Tests.CodeSources
{
    [TestClass]
    public class ZipArchiveExtractorTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = CombineUnder(Path.GetTempPath(), "zaet-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* best-effort */ }
        }

        private static string CombineUnder(string root, string relative)
        {
            if (Path.IsPathRooted(relative))
            {
                throw new ArgumentException("relative path must not be rooted", nameof(relative));
            }
            return Path.Join(root, relative);
        }

        private string TargetDir() => CombineUnder(_tempRoot, "out");
        private string ArchivePath() => CombineUnder(_tempRoot, "archive.zip");

        private string BuildArchive(Action<ZipArchive> populate)
        {
            var path = ArchivePath();
            using (var fs = File.Create(path))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                populate(archive);
            }
            return path;
        }

        private static void WriteEntry(ZipArchive archive, string fullName, string content)
        {
            var entry = archive.CreateEntry(fullName);
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            w.Write(content);
        }

        [TestMethod]
        public void Extract_NormalArchive_FilesAreCreated()
        {
            BuildArchive(a =>
            {
                WriteEntry(a, "file.txt", "hello");
                WriteEntry(a, "subdir/inner.txt", "world");
            });

            var extractor = new ZipArchiveExtractor(ZipArchiveExtractionOptions.Default);
            extractor.Extract(ArchivePath(), TargetDir());

            Assert.IsTrue(File.Exists(CombineUnder(TargetDir(), "file.txt")));
            Assert.IsTrue(File.Exists(CombineUnder(TargetDir(), Path.Join("subdir", "inner.txt"))));
            Assert.AreEqual("hello", File.ReadAllText(CombineUnder(TargetDir(), "file.txt")));
        }

        [TestMethod]
        public void Extract_PathTraversalEntry_ThrowsParentSegment()
        {
            BuildArchive(a => WriteEntry(a, "../escape.txt", "no"));

            var extractor = new ZipArchiveExtractor(ZipArchiveExtractionOptions.Default);

            var ex = Assert.ThrowsExactly<UnsafeArchiveException>(
                () => extractor.Extract(ArchivePath(), TargetDir()));
            Assert.AreEqual(UnsafeArchiveReason.ParentSegment, ex.Reason);
        }

        [TestMethod]
        public void Extract_AbsolutePathEntry_ThrowsAbsolutePath()
        {
            BuildArchive(a => WriteEntry(a, "/etc/foo", "no"));

            var extractor = new ZipArchiveExtractor(ZipArchiveExtractionOptions.Default);

            var ex = Assert.ThrowsExactly<UnsafeArchiveException>(
                () => extractor.Extract(ArchivePath(), TargetDir()));
            Assert.AreEqual(UnsafeArchiveReason.AbsolutePath, ex.Reason);
        }

        [TestMethod]
        public void Extract_EntryCountExceeded_Throws()
        {
            BuildArchive(a =>
            {
                WriteEntry(a, "a.txt", "1");
                WriteEntry(a, "b.txt", "2");
                WriteEntry(a, "c.txt", "3");
            });

            var options = new ZipArchiveExtractionOptions { MaxEntryCount = 2 };
            var extractor = new ZipArchiveExtractor(options);

            var ex = Assert.ThrowsExactly<UnsafeArchiveException>(
                () => extractor.Extract(ArchivePath(), TargetDir()));
            Assert.AreEqual(UnsafeArchiveReason.EntryCountExceeded, ex.Reason);
        }

        [TestMethod]
        public void Extract_PerEntrySizeExceeded_Throws()
        {
            BuildArchive(a => WriteEntry(a, "big.txt", new string('x', 100)));

            var options = new ZipArchiveExtractionOptions { MaxBytesPerEntry = 10 };
            var extractor = new ZipArchiveExtractor(options);

            var ex = Assert.ThrowsExactly<UnsafeArchiveException>(
                () => extractor.Extract(ArchivePath(), TargetDir()));
            Assert.AreEqual(UnsafeArchiveReason.EntrySizeExceeded, ex.Reason);
        }

        [TestMethod]
        public void Extract_TotalSizeExceeded_Throws()
        {
            BuildArchive(a =>
            {
                WriteEntry(a, "a.txt", new string('x', 60));
                WriteEntry(a, "b.txt", new string('y', 60));
            });

            var options = new ZipArchiveExtractionOptions
            {
                MaxBytesPerEntry = 100,
                MaxBytesTotal = 100
            };
            var extractor = new ZipArchiveExtractor(options);

            var ex = Assert.ThrowsExactly<UnsafeArchiveException>(
                () => extractor.Extract(ArchivePath(), TargetDir()));
            Assert.AreEqual(UnsafeArchiveReason.TotalSizeExceeded, ex.Reason);
        }

        [TestMethod]
        public void Extract_NormalisesAndContains_NoEscape()
        {
            // entry like "subdir/../escape.txt": explicit '..' segment is rejected
            BuildArchive(a => WriteEntry(a, "subdir/../escape.txt", "no"));

            var extractor = new ZipArchiveExtractor(ZipArchiveExtractionOptions.Default);

            var ex = Assert.ThrowsExactly<UnsafeArchiveException>(
                () => extractor.Extract(ArchivePath(), TargetDir()));
            Assert.AreEqual(UnsafeArchiveReason.ParentSegment, ex.Reason);
        }

        [TestMethod]
        public void Constructor_NullOptions_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(
                () => new ZipArchiveExtractor(null!));
        }

        [TestMethod]
        public void Extract_DirectoryEntry_CreatesDirectory()
        {
            BuildArchive(a => a.CreateEntry("emptydir/"));

            var extractor = new ZipArchiveExtractor(ZipArchiveExtractionOptions.Default);
            extractor.Extract(ArchivePath(), TargetDir());

            Assert.IsTrue(Directory.Exists(CombineUnder(TargetDir(), "emptydir")));
        }
    }
}
