using Dorc.Api.Windows.Interfaces;
using System.IO;

namespace Dorc.Api.Windows.Windows.Services
{
    public class FileSystemHelper : IFileSystemHelper
    {
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }
    }
}