using Dorc.Api.Windows.Interfaces;
using System.IO;

namespace Dorc.Api.Windows.Deployment
{
    public class FileOperations : IFileOperations
    {
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }
    }
}