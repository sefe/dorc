using Dorc.Api.Interfaces;
using System.IO;

namespace Dorc.Api.Services
{
    public class FileSystemHelper : IFileSystemHelper
    {
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }
    }
}