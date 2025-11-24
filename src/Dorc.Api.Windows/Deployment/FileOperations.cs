using Dorc.Api.Windows.Interfaces;
using System.IO;

namespace Dorc.Api.Windows.Windows.Services
{
    public class FileOperations : IFileOperations
    {
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }
    }
}