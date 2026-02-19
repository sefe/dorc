using Dorc.Api.Interfaces;
using System.IO;

namespace Dorc.Api.Deployment
{
    public class FileOperations : IFileOperations
    {
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }
    }
}