using System.IO;
using System.Text;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Turns the bytes that were verified into the text that is executed.
    ///
    /// This exists because verification and execution must operate on a single read. Reading the
    /// file twice — once as bytes to hash and once as text to run — leaves a window between the
    /// two in which the file can be substituted, which is exactly the window the verification is
    /// there to close.
    ///
    /// The decoding reproduces what <c>File.ReadAllText</c> did before: UTF-8 by default, with
    /// the byte-order mark of another encoding honoured if one is present. Reproducing it
    /// exactly matters — a script that decoded one way yesterday and another way today would
    /// change behaviour for reasons that have nothing to do with this step.
    /// </summary>
    public static class ScriptText
    {
        public static string Decode(byte[] content)
        {
            using (var stream = new MemoryStream(content))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
