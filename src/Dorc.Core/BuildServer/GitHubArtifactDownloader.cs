using System.IO.Compression;
using System.Net.Http.Headers;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.BuildServer
{
    /// <summary>Downloads GitHub Actions artifacts; folder via <c>AppSettings:GitHubArtifactDownloadFolder</c>, defaults to %ProgramData%\dorc\artifacts.</summary>
    public class GitHubArtifactDownloader : IGitHubArtifactDownloader
    {
        // 2 GiB cap on downloaded zip — GitHub itself caps artifacts at 10 GB, but 2 GiB
        // covers any sensible deploy payload while preventing disk exhaustion from a hostile
        // or misconfigured workflow.
        private const long MaxArtifactBytes = 2L * 1024 * 1024 * 1024;

        private readonly ILogger<GitHubArtifactDownloader> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IGitHubHostValidator _hostValidator;
        private readonly ISourceHostAllowList _sourceHosts;
        private readonly string _gitHubToken;
        private readonly string _downloadFolder;

        public GitHubArtifactDownloader(
            ILogger<GitHubArtifactDownloader> logger,
            IHttpClientFactory httpClientFactory,
            IGitHubHostValidator hostValidator,
            IConfiguration configuration,
            ISourceHostAllowList sourceHosts)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _hostValidator = hostValidator;
            _sourceHosts = sourceHosts ?? throw new ArgumentNullException(nameof(sourceHosts));
            var appSettings = configuration.GetSection("AppSettings");
            _gitHubToken = appSettings["GitHubToken"] ?? string.Empty;
            _downloadFolder = appSettings["GitHubArtifactDownloadFolder"] ?? string.Empty;

            if (string.IsNullOrEmpty(_gitHubToken))
            {
                _logger.LogWarning("GitHubToken is not configured in AppSettings. GitHub artifact downloads " +
                    "will fail with 401 Unauthorized — the /actions/artifacts/{id}/zip endpoint requires a token " +
                    "with 'actions:read' on the target repository.");
            }

            if (!string.IsNullOrEmpty(_downloadFolder))
            {
                _logger.LogInformation("GitHub artifacts will be downloaded under: {Folder}", _downloadFolder);
            }
            else
            {
                _logger.LogInformation("GitHubArtifactDownloadFolder is not configured in AppSettings. " +
                    "Downloads will use the system-wide fallback %ProgramData%\\dorc\\artifacts on the " +
                    "Monitor host. Set this to a UNC path if deploy scripts on remote targets need to " +
                    "read the artifact directly.");
            }
        }

        public bool IsGitHubArtifactUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            // GitHub Actions artifact download URLs contain /actions/artifacts/
            // e.g. https://api.github.com/repos/{owner}/{repo}/actions/artifacts/{id}/zip
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && url.Contains("/actions/artifacts/", StringComparison.OrdinalIgnoreCase);
        }

        public string DownloadAndExtract(string artifactUrl)
        {
            // SSRF guard: the bearer token gets attached below, so we must
            // validate the host against the GitHub allow-list before
            // dispatching the request. Otherwise a crafted DropLocation could
            // exfiltrate the token to an attacker-controlled host.
            if (!Uri.TryCreate(artifactUrl, UriKind.Absolute, out var parsedUrl) ||
                parsedUrl.Scheme != Uri.UriSchemeHttps ||
                !parsedUrl.IsDefaultPort)
            {
                throw new ArgumentException(
                    $"GitHub artifact URL must be an absolute https:// URL on default port 443: '{artifactUrl}'",
                    nameof(artifactUrl));
            }
            _hostValidator.ValidateHost(parsedUrl.Host);
            RequirePermittedGitHubArtefactHost(artifactUrl);
            var configuredApiBase = _hostValidator.GetApiBase(artifactUrl);
            if (Uri.TryCreate(configuredApiBase, UriKind.Absolute, out var originUri)
                && !ApiModel.SourceHost.HasSameOrigin(artifactUrl, originUri.AbsoluteUri))
            {
                throw new InvalidOperationException(
                    $"Refusing to present GitHub token to '{artifactUrl}': origin '{ApiModel.SourceHost.OriginOf(artifactUrl) ?? "<none>"}' does not match configured GitHub API origin '{ApiModel.SourceHost.OriginOf(originUri.AbsoluteUri) ?? "<none>"}'.");
            }

            _logger.LogInformation("Downloading GitHub Actions artifact from: {Url}", artifactUrl);

            using var client = _httpClientFactory.CreateClient("GitHubActions");
            client.DefaultRequestHeaders.Authorization = null;
            var authorization = !string.IsNullOrEmpty(_gitHubToken)
                ? new AuthenticationHeaderValue("Bearer", _gitHubToken)
                : null;

            var isDefaultArtifactsRoot = string.IsNullOrEmpty(_downloadFolder);
            var baseDir = !isDefaultArtifactsRoot
                ? _downloadFolder
                : Path.Join(DorcProgramData.Root, "artifacts");
            var requestFolderName = Guid.NewGuid().ToString("N");
            var tempDir = Path.Join(baseDir, requestFolderName);

            PinnedLocalArtifactStage? pinnedStage = null;
            if (OperatingSystem.IsWindows() && !ApiModel.SourceHost.IsRemoteOrUnc(baseDir))
            {
                pinnedStage = PinnedLocalArtifactStage.Create(baseDir, tempDir, isDefaultArtifactsRoot);
            }
            else
            {
                CreateRemoteOrUnixArtifactDirectory(baseDir, tempDir);
            }

            try
            {
                var zipPath = Path.Join(pinnedStage?.TempDirectory ?? tempDir, "artifact.zip");
                var extractDir = pinnedStage?.ExtractDirectory ?? Path.Join(tempDir, "drop");

                using (var response = Security.OriginBoundHttpSender.GetAsync(client, parsedUrl, authorization).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    using var fileStream = pinnedStage != null
                        ? pinnedStage.CreateNewVerifiedZipStream(zipPath)
                        : new FileStream(zipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    CopyWithLimit(stream, fileStream, MaxArtifactBytes);
                    fileStream.Flush(flushToDisk: true);

                    _logger.LogInformation("Downloaded artifact zip ({Size} bytes) to: {Path}",
                        fileStream.Length, zipPath);

                    // Extract directly from the open, unshared zip handle while baseDir, tempDir,
                    // and extractDir remain pinned without FileShare.Delete so no directory or zip
                    // path can be replaced with a junction/symlink mid-flight.
                    fileStream.Position = 0;
                    if (pinnedStage == null)
                    {
                        Directory.CreateDirectory(extractDir);
                    }

                    using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: true);
                    archive.ExtractToDirectory(extractDir);
                }

                _logger.LogInformation("Extracted artifact to: {Path}", extractDir);

                File.Delete(zipPath);

                return tempDir;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download/extract GitHub artifact from: {Url}", artifactUrl);
                pinnedStage?.Dispose();
                pinnedStage = null;
                Cleanup(tempDir);
                throw;
            }
            finally
            {
                pinnedStage?.Dispose();
            }
        }

        private static void CreateRemoteOrUnixArtifactDirectory(string baseDir, string tempDir)
        {
            if (Directory.Exists(baseDir) && (new DirectoryInfo(baseDir).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Refusing to stage GitHub artifact under reparse point '{baseDir}'.");
            }

            if (File.Exists(tempDir) || Directory.Exists(tempDir))
            {
                throw new IOException($"Refusing to stage GitHub artifact into pre-existing path '{tempDir}'.");
            }

            // Remote/UNC staging folders rely on share and NTFS permissions configured for the
            // Monitor + Runner hosts; PendingRequestProcessor.RestrictExtractedArtifactDrop skips
            // UNC paths, so applying a local-host-only protected DACL here would lock out distinct
            // non-admin Runner service accounts.
            Directory.CreateDirectory(tempDir);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private sealed class PinnedLocalArtifactStage : IDisposable
        {
            private const uint GenericRead = 0x80000000u;
            private const uint GenericWrite = 0x40000000u;
            private const uint DeleteRight = 0x00010000u;
            private const uint FileListDirectory = 0x00000001u;
            private const uint FileReadAttributes = 0x00000080u;
            private const uint ReadControl = 0x00020000u;
            private const uint WriteDac = 0x00040000u;

            private const uint FileShareNone = 0u;
            private const uint FileShareRead = 0x00000001u;
            private const uint FileShareWrite = 0x00000002u;

            private const uint CreateNewDisposition = 1u;
            private const uint OpenExistingDisposition = 3u;

            private const uint FileAttributeNormal = 0x00000080u;
            private const uint FileFlagBackupSemantics = 0x02000000u;
            private const uint FileFlagOpenReparsePoint = 0x00200000u;

            private const uint SeFileObject = 1u;
            private const uint OwnerSecurityInformation = 0x00000001u;
            private const uint DaclSecurityInformation = 0x00000004u;
            private const int ErrorInsufficientBuffer = 122;

            private const System.Security.AccessControl.FileSystemRights UnsafeParentAllowRights =
                System.Security.AccessControl.FileSystemRights.CreateDirectories
                | System.Security.AccessControl.FileSystemRights.CreateFiles
                | System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles
                | System.Security.AccessControl.FileSystemRights.Delete
                | System.Security.AccessControl.FileSystemRights.ChangePermissions
                | System.Security.AccessControl.FileSystemRights.TakeOwnership
                | System.Security.AccessControl.FileSystemRights.WriteAttributes
                | System.Security.AccessControl.FileSystemRights.WriteExtendedAttributes;

            private const System.Security.AccessControl.FileSystemRights UnsafeAncestorAllowRights =
                System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles
                | System.Security.AccessControl.FileSystemRights.Delete
                | System.Security.AccessControl.FileSystemRights.ChangePermissions
                | System.Security.AccessControl.FileSystemRights.TakeOwnership;

            private const int GenericAllMask = 0x10000000;
            private const int GenericWriteOrAllMask = unchecked((int)0x50000000);
            private const string TrustedInstallerSidValue = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct SecurityAttributes
            {
                public uint nLength;
                public IntPtr lpSecurityDescriptor;
                public int bInheritHandle;
            }

            [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
            private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
                string lpFileName,
                uint dwDesiredAccess,
                uint dwShareMode,
                IntPtr lpSecurityAttributes,
                uint dwCreationDisposition,
                uint dwFlagsAndAttributes,
                IntPtr hTemplateFile);

            [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool CreateDirectoryW(
                string lpPathName,
                ref SecurityAttributes lpSecurityAttributes);

            [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
            private static extern uint GetFinalPathNameByHandleW(
                Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
                [System.Runtime.InteropServices.Out] char[] lpszFilePath,
                uint cchFilePath,
                uint dwFlags);

            [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "GetSecurityInfo", ExactSpelling = true, SetLastError = false)]
            private static extern uint GetSecurityInfo(
                Microsoft.Win32.SafeHandles.SafeFileHandle handle,
                uint objectType,
                uint securityInfo,
                out IntPtr ppsidOwner,
                IntPtr ppsidGroup,
                IntPtr ppDacl,
                IntPtr ppSacl,
                out IntPtr ppSecurityDescriptor);

            [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "LocalFree", ExactSpelling = true, SetLastError = true)]
            private static extern IntPtr LocalFree(IntPtr hMem);

            [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "GetKernelObjectSecurity", ExactSpelling = true, SetLastError = true)]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool GetKernelObjectSecurity(
                Microsoft.Win32.SafeHandles.SafeFileHandle handle,
                uint requestedInformation,
                [System.Runtime.InteropServices.Out] byte[]? pSecurityDescriptor,
                uint nLength,
                out uint lpnLengthNeeded);

            [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "SetKernelObjectSecurity", ExactSpelling = true, SetLastError = true)]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool SetKernelObjectSecurity(
                Microsoft.Win32.SafeHandles.SafeFileHandle handle,
                uint securityInformation,
                [System.Runtime.InteropServices.In] byte[] pSecurityDescriptor);

            private readonly List<Microsoft.Win32.SafeHandles.SafeFileHandle> _ancestorHandles;
            private readonly Microsoft.Win32.SafeHandles.SafeFileHandle _baseHandle;
            private readonly Microsoft.Win32.SafeHandles.SafeFileHandle _tempHandle;
            private readonly Microsoft.Win32.SafeHandles.SafeFileHandle _extractHandle;

            public string TempDirectory { get; }
            public string ExtractDirectory { get; }

            private PinnedLocalArtifactStage(
                List<Microsoft.Win32.SafeHandles.SafeFileHandle> ancestorHandles,
                Microsoft.Win32.SafeHandles.SafeFileHandle baseHandle,
                Microsoft.Win32.SafeHandles.SafeFileHandle tempHandle,
                Microsoft.Win32.SafeHandles.SafeFileHandle extractHandle,
                string tempDirectory,
                string extractDirectory)
            {
                _ancestorHandles = ancestorHandles;
                _baseHandle = baseHandle;
                _tempHandle = tempHandle;
                _extractHandle = extractHandle;
                TempDirectory = tempDirectory;
                ExtractDirectory = extractDirectory;
            }

            public static PinnedLocalArtifactStage Create(string baseDir, string tempDir, bool isDefaultArtifactsRoot)
            {
                if (File.Exists(baseDir))
                {
                    throw new IOException($"Refusing to stage GitHub artifact under file '{baseDir}'.");
                }

                if (Directory.Exists(baseDir) && (new DirectoryInfo(baseDir).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Refusing to stage GitHub artifact under reparse point '{baseDir}'.");
                }

                if (File.Exists(tempDir) || Directory.Exists(tempDir))
                {
                    throw new IOException($"Refusing to stage GitHub artifact into pre-existing path '{tempDir}'.");
                }

                var security = BuildRestrictedArtifactDirectorySecurity(out var privilegedOwners);
                var trustedAncestorPrincipals = new HashSet<System.Security.Principal.SecurityIdentifier>(privilegedOwners)
                {
                    new System.Security.Principal.SecurityIdentifier(TrustedInstallerSidValue)
                };

                var fullBaseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir));
                var ancestors = new List<string>();
                for (var current = Path.GetDirectoryName(fullBaseDir);
                     !string.IsNullOrEmpty(current);
                     current = Path.GetDirectoryName(current))
                {
                    ancestors.Add(current);
                }
                ancestors.Reverse();

                var ancestorHandles = new List<Microsoft.Win32.SafeHandles.SafeFileHandle>(ancestors.Count);
                Microsoft.Win32.SafeHandles.SafeFileHandle? baseHandle = null;
                Microsoft.Win32.SafeHandles.SafeFileHandle? tempHandle = null;
                Microsoft.Win32.SafeHandles.SafeFileHandle? extractHandle = null;
                try
                {
                    string? previousCanonical = null;
                    for (var i = 0; i < ancestors.Count; i++)
                    {
                        var ancestor = ancestors[i];
                        if (File.Exists(ancestor))
                        {
                            throw new IOException(
                                $"Refusing to stage GitHub artifact under '{baseDir}' because ancestor '{ancestor}' is a file.");
                        }

                        Microsoft.Win32.SafeHandles.SafeFileHandle ancestorHandle;
                        string actualAncestorCanonical;
                        if (!Directory.Exists(ancestor))
                        {
                            var ancestorLeaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(ancestor));
                            if (string.IsNullOrEmpty(ancestorLeaf) || previousCanonical == null)
                            {
                                throw new IOException($"Invalid missing ancestor directory '{ancestor}' for '{baseDir}'.");
                            }

                            actualAncestorCanonical = Path.Join(previousCanonical, ancestorLeaf);
                            ancestorHandle = CreateAndPinChildDirectory(actualAncestorCanonical, security, privilegedOwners);
                        }
                        else
                        {
                            ancestorHandle = OpenPinnedDirectory(ancestor, includeWriteDac: false);
                            try
                            {
                                actualAncestorCanonical = GetCanonicalPath(ancestorHandle, ancestor);
                                if (previousCanonical != null
                                    && !string.Equals(
                                        Path.GetDirectoryName(actualAncestorCanonical),
                                        previousCanonical,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new IOException(
                                        $"Ancestor directory '{ancestor}' resolved to '{actualAncestorCanonical}' outside expected parent '{previousCanonical}'.");
                                }

                                VerifyPrivilegedOwner(ancestorHandle, ancestor, trustedAncestorPrincipals);
                                VerifyTrustedAncestorDacl(ancestorHandle, ancestor, baseDir, trustedAncestorPrincipals);
                            }
                            catch
                            {
                                ancestorHandle.Dispose();
                                throw;
                            }
                        }

                        ancestorHandles.Add(ancestorHandle);
                        previousCanonical = actualAncestorCanonical;
                    }

                    if (!Directory.Exists(fullBaseDir))
                    {
                        var baseLeaf = Path.GetFileName(fullBaseDir);
                        if (string.IsNullOrEmpty(baseLeaf) || previousCanonical == null)
                        {
                            throw new IOException($"Invalid artifact staging root '{baseDir}'.");
                        }

                        var expectedBaseCanonical = Path.Join(previousCanonical, baseLeaf);
                        baseHandle = CreateAndPinChildDirectory(expectedBaseCanonical, security, privilegedOwners);
                    }
                    else
                    {
                        baseHandle = OpenPinnedDirectory(fullBaseDir, includeWriteDac: isDefaultArtifactsRoot);
                        VerifyPrivilegedOwner(baseHandle, baseDir, privilegedOwners);
                        VerifyOrHardenParentDacl(baseHandle, baseDir, privilegedOwners, security, isDefaultArtifactsRoot);
                    }

                    var canonicalBase = GetCanonicalPath(baseHandle, baseDir);
                    if (previousCanonical != null
                        && !string.Equals(
                            Path.GetDirectoryName(canonicalBase),
                            previousCanonical,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException(
                            $"Artifact staging directory '{baseDir}' resolved to '{canonicalBase}' outside expected parent '{previousCanonical}'.");
                    }

                    var requestLeaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(tempDir));
                    if (string.IsNullOrEmpty(requestLeaf))
                    {
                        throw new IOException($"Invalid artifact request staging directory '{tempDir}'.");
                    }

                    var canonicalTemp = Path.Join(canonicalBase, requestLeaf);
                    tempHandle = CreateAndPinChildDirectory(canonicalTemp, security, privilegedOwners);

                    var canonicalExtract = Path.Join(canonicalTemp, "drop");
                    extractHandle = CreateAndPinChildDirectory(canonicalExtract, security, privilegedOwners);

                    return new PinnedLocalArtifactStage(
                        ancestorHandles,
                        baseHandle,
                        tempHandle,
                        extractHandle,
                        canonicalTemp,
                        canonicalExtract);
                }
                catch
                {
                    extractHandle?.Dispose();
                    tempHandle?.Dispose();
                    baseHandle?.Dispose();
                    for (var i = ancestorHandles.Count - 1; i >= 0; i--)
                    {
                        ancestorHandles[i].Dispose();
                    }
                    if (Directory.Exists(tempDir))
                    {
                        try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort rollback */ }
                    }
                    throw;
                }
            }

            public FileStream CreateNewVerifiedZipStream(string zipPath)
            {
                var handle = CreateFileW(
                    zipPath,
                    GenericRead | GenericWrite | FileReadAttributes | DeleteRight,
                    FileShareNone,
                    IntPtr.Zero,
                    CreateNewDisposition,
                    FileAttributeNormal | FileFlagOpenReparsePoint,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                    handle.Dispose();
                    throw new IOException(
                        $"Cannot create GitHub artifact zip '{zipPath}' (Win32 error {error}).",
                        new System.ComponentModel.Win32Exception(error));
                }

                try
                {
                    var attributes = File.GetAttributes(handle);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    {
                        throw new IOException($"Refusing to write GitHub artifact zip through unexpected file attributes on '{zipPath}'.");
                    }

                    var actualCanonical = GetCanonicalPath(handle, zipPath);
                    var expectedCanonical = Path.Join(TempDirectory, "artifact.zip");
                    if (!string.Equals(actualCanonical, expectedCanonical, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException(
                            $"GitHub artifact zip '{zipPath}' resolved to '{actualCanonical}' instead of '{expectedCanonical}'.");
                    }

                    return new FileStream(handle, FileAccess.ReadWrite, bufferSize: 81920, isAsync: false);
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                _extractHandle.Dispose();
                _tempHandle.Dispose();
                _baseHandle.Dispose();
                for (var i = _ancestorHandles.Count - 1; i >= 0; i--)
                {
                    _ancestorHandles[i].Dispose();
                }
            }

            private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenPinnedDirectory(
                string path,
                bool includeWriteDac = true)
            {
                // Omit FILE_SHARE_DELETE and request FILE_LIST_DIRECTORY so NTFS enforces the
                // share mask on the directory FCB, blocking rename/replacement of the directory
                // and its ancestors while the handle remains open.
                var desiredAccess = FileListDirectory | FileReadAttributes | ReadControl
                    | (includeWriteDac ? WriteDac : 0u);
                var handle = CreateFileW(
                    path,
                    desiredAccess,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExistingDisposition,
                    FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                    handle.Dispose();
                    throw new IOException(
                        $"Failed to open artifact directory '{path}' (Win32 error {error}).",
                        new System.ComponentModel.Win32Exception(error));
                }

                try
                {
                    var attributes = File.GetAttributes(handle);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException($"Refusing to stage GitHub artifact under reparse point '{path}'.");
                    }

                    if ((attributes & FileAttributes.Directory) == 0)
                    {
                        throw new IOException($"Expected artifact staging directory at '{path}', but found a file.");
                    }

                    return handle;
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }

            private static Microsoft.Win32.SafeHandles.SafeFileHandle CreateAndPinChildDirectory(
                string expectedCanonicalChild,
                System.Security.AccessControl.DirectorySecurity security,
                HashSet<System.Security.Principal.SecurityIdentifier> privilegedOwners)
            {
                var descriptorBytes = security.GetSecurityDescriptorBinaryForm();
                var pinnedDescriptor = System.Runtime.InteropServices.GCHandle.Alloc(
                    descriptorBytes,
                    System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    var attributes = new SecurityAttributes
                    {
                        nLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SecurityAttributes>(),
                        lpSecurityDescriptor = pinnedDescriptor.AddrOfPinnedObject(),
                        bInheritHandle = 0
                    };

                    if (!CreateDirectoryW(expectedCanonicalChild, ref attributes))
                    {
                        var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                        throw new IOException(
                            $"Cannot create restricted artifact staging directory '{expectedCanonicalChild}' (Win32 error {error}).",
                            new System.ComponentModel.Win32Exception(error));
                    }
                }
                finally
                {
                    pinnedDescriptor.Free();
                }

                var handle = OpenPinnedDirectory(expectedCanonicalChild);
                try
                {
                    VerifyPrivilegedOwner(handle, expectedCanonicalChild, privilegedOwners);
                    var actualCanonical = GetCanonicalPath(handle, expectedCanonicalChild);
                    if (!string.Equals(actualCanonical, expectedCanonicalChild, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException(
                            $"Artifact staging directory '{expectedCanonicalChild}' resolved to '{actualCanonical}'.");
                    }

                    ApplyProtectedDacl(handle, security, expectedCanonicalChild);
                    return handle;
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }

            private static void VerifyPrivilegedOwner(
                Microsoft.Win32.SafeHandles.SafeFileHandle handle,
                string path,
                HashSet<System.Security.Principal.SecurityIdentifier> privilegedOwners)
            {
                var result = GetSecurityInfo(
                    handle,
                    SeFileObject,
                    OwnerSecurityInformation,
                    out var ownerSidPtr,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out var securityDescriptorPtr);
                if (result != 0)
                {
                    throw new IOException(
                        $"Failed to inspect NTFS owner of '{path}' (Win32 error {result}).",
                        new System.ComponentModel.Win32Exception((int)result));
                }

                try
                {
                    var ownerSid = ownerSidPtr != IntPtr.Zero
                        ? new System.Security.Principal.SecurityIdentifier(ownerSidPtr)
                        : null;
                    if (ownerSid == null || !privilegedOwners.Contains(ownerSid))
                    {
                        throw new IOException(
                            $"Refusing to stage GitHub artifact under '{path}' because its NTFS owner '{ownerSid?.Value ?? "<unknown>"}' is not a privileged account.");
                    }
                }
                finally
                {
                    if (securityDescriptorPtr != IntPtr.Zero)
                    {
                        LocalFree(securityDescriptorPtr);
                    }
                }
            }

            private static void VerifyOrHardenParentDacl(
                Microsoft.Win32.SafeHandles.SafeFileHandle baseHandle,
                string baseDir,
                HashSet<System.Security.Principal.SecurityIdentifier> privilegedOwners,
                System.Security.AccessControl.DirectorySecurity restrictedSecurity,
                bool isDefaultArtifactsRoot)
            {
                var currentSecurity = GetDirectorySecurity(baseHandle, baseDir);
                var ownerRightsSid = new System.Security.Principal.SecurityIdentifier("S-1-3-4");
                var rules = currentSecurity
                    .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(System.Security.Principal.SecurityIdentifier))
                    .Cast<System.Security.AccessControl.FileSystemAccessRule>();

                foreach (var rule in rules)
                {
                    if (rule.AccessControlType != System.Security.AccessControl.AccessControlType.Allow)
                    {
                        continue;
                    }

                    if ((rule.PropagationFlags & System.Security.AccessControl.PropagationFlags.InheritOnly) != 0)
                    {
                        continue;
                    }

                    var sid = (System.Security.Principal.SecurityIdentifier)rule.IdentityReference;
                    if (privilegedOwners.Contains(sid) || sid == ownerRightsSid)
                    {
                        continue;
                    }

                    var hasUnsafeRights = (rule.FileSystemRights & UnsafeParentAllowRights) != 0
                        || ((int)rule.FileSystemRights & GenericWriteOrAllMask) != 0;
                    if (!hasUnsafeRights)
                    {
                        continue;
                    }

                    if (isDefaultArtifactsRoot)
                    {
                        ApplyProtectedDacl(baseHandle, restrictedSecurity, baseDir);
                        return;
                    }

                    throw new IOException(
                        $"Refusing to stage GitHub artifact under '{baseDir}' because its DACL grants write, child-creation, or child-deletion rights to non-privileged principal '{sid.Value}'.");
                }

                if (isDefaultArtifactsRoot && !currentSecurity.AreAccessRulesProtected)
                {
                    ApplyProtectedDacl(baseHandle, restrictedSecurity, baseDir);
                }
            }

            private static void VerifyTrustedAncestorDacl(
                Microsoft.Win32.SafeHandles.SafeFileHandle ancestorHandle,
                string ancestorPath,
                string baseDir,
                HashSet<System.Security.Principal.SecurityIdentifier> trustedAncestorPrincipals)
            {
                var currentSecurity = GetDirectorySecurity(ancestorHandle, ancestorPath);
                var ownerRightsSid = new System.Security.Principal.SecurityIdentifier("S-1-3-4");
                var rules = currentSecurity
                    .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(System.Security.Principal.SecurityIdentifier))
                    .Cast<System.Security.AccessControl.FileSystemAccessRule>();

                foreach (var rule in rules)
                {
                    if (rule.AccessControlType != System.Security.AccessControl.AccessControlType.Allow)
                    {
                        continue;
                    }

                    if ((rule.PropagationFlags & System.Security.AccessControl.PropagationFlags.InheritOnly) != 0)
                    {
                        continue;
                    }

                    var sid = (System.Security.Principal.SecurityIdentifier)rule.IdentityReference;
                    if (trustedAncestorPrincipals.Contains(sid) || sid == ownerRightsSid)
                    {
                        continue;
                    }

                    var hasUnsafeRights = (rule.FileSystemRights & UnsafeAncestorAllowRights) != 0
                        || ((int)rule.FileSystemRights & GenericAllMask) != 0;
                    if (hasUnsafeRights)
                    {
                        throw new IOException(
                            $"Refusing to stage GitHub artifact under '{baseDir}' because ancestor directory '{ancestorPath}' grants replacement or permission-change rights to non-privileged principal '{sid.Value}'.");
                    }
                }
            }

            private static System.Security.AccessControl.DirectorySecurity GetDirectorySecurity(
                Microsoft.Win32.SafeHandles.SafeFileHandle handle,
                string path)
            {
                if (!GetKernelObjectSecurity(handle, DaclSecurityInformation, null, 0u, out var lengthNeeded))
                {
                    var probeError = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                    if (probeError != ErrorInsufficientBuffer || lengthNeeded == 0)
                    {
                        throw new IOException(
                            $"Failed to query DACL size for '{path}' (Win32 error {probeError}).",
                            new System.ComponentModel.Win32Exception(probeError));
                    }
                }

                var rawDescriptor = new byte[lengthNeeded];
                if (!GetKernelObjectSecurity(handle, DaclSecurityInformation, rawDescriptor, lengthNeeded, out _))
                {
                    var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                    throw new IOException(
                        $"Failed to read DACL for '{path}' (Win32 error {error}).",
                        new System.ComponentModel.Win32Exception(error));
                }

                var security = new System.Security.AccessControl.DirectorySecurity();
                security.SetSecurityDescriptorBinaryForm(
                    rawDescriptor,
                    System.Security.AccessControl.AccessControlSections.Access);
                return security;
            }

            private static void ApplyProtectedDacl(
                Microsoft.Win32.SafeHandles.SafeFileHandle handle,
                System.Security.AccessControl.DirectorySecurity security,
                string path)
            {
                var descriptorBytes = security.GetSecurityDescriptorBinaryForm();
                if (descriptorBytes.Length < 4)
                {
                    throw new IOException($"Security descriptor for '{path}' was truncated.");
                }

                descriptorBytes[3] |= 0x10; // SE_DACL_PROTECTED
                if (!SetKernelObjectSecurity(handle, DaclSecurityInformation, descriptorBytes))
                {
                    var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                    throw new IOException(
                        $"Failed to apply restricted DACL to '{path}' (Win32 error {error}).",
                        new System.ComponentModel.Win32Exception(error));
                }
            }

            private static string GetCanonicalPath(
                Microsoft.Win32.SafeHandles.SafeFileHandle handle,
                string originalPath)
            {
                var buffer = new char[512];
                var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0u);
                if (length >= buffer.Length)
                {
                    buffer = new char[length + 1];
                    length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0u);
                }

                if (length == 0 || length >= buffer.Length)
                {
                    var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                    throw new IOException(
                        $"Failed to resolve canonical path for '{originalPath}' (Win32 error {error}).",
                        new System.ComponentModel.Win32Exception(error));
                }

                var resolved = new string(buffer, 0, (int)length);
                if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                {
                    resolved = @"\\" + resolved[8..];
                }
                else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal))
                {
                    resolved = resolved[4..];
                }

                return Path.TrimEndingDirectorySeparator(resolved);
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static System.Security.AccessControl.DirectorySecurity BuildRestrictedArtifactDirectorySecurity(
            out HashSet<System.Security.Principal.SecurityIdentifier> privilegedOwners)
        {
            var security = new System.Security.AccessControl.DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            privilegedOwners = new HashSet<System.Security.Principal.SecurityIdentifier>();

            const System.Security.AccessControl.InheritanceFlags inherit =
                System.Security.AccessControl.InheritanceFlags.ContainerInherit
                | System.Security.AccessControl.InheritanceFlags.ObjectInherit;

            using var currentIdentity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (currentIdentity.User != null)
            {
                privilegedOwners.Add(currentIdentity.User);
                security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    currentIdentity.User,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    inherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow));
            }

            var localSystemSid = new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.LocalSystemSid,
                null);
            privilegedOwners.Add(localSystemSid);
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                localSystemSid,
                System.Security.AccessControl.FileSystemRights.FullControl,
                inherit,
                System.Security.AccessControl.PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));

            var builtinAdminsSid = new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid,
                null);
            privilegedOwners.Add(builtinAdminsSid);
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                builtinAdminsSid,
                System.Security.AccessControl.FileSystemRights.FullControl,
                inherit,
                System.Security.AccessControl.PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));

            return security;
        }

        public void Cleanup(string extractedPath)
        {
            try
            {
                if (string.IsNullOrEmpty(extractedPath) || !Directory.Exists(extractedPath))
                    return;

                // Defence-in-depth against path traversal: refuse to recursive-delete
                // anything that doesn't canonically resolve under the configured
                // artifact base folder. Current call sites only pass paths returned
                // by DownloadAndExtract, but the interface is public and a future
                // caller could plausibly forward untrusted input.
                if (!IsUnderArtifactBase(extractedPath))
                {
                    _logger.LogWarning(
                        "Refusing to delete path outside artifact base folder: {Path}",
                        extractedPath);
                    return;
                }

                Directory.Delete(extractedPath, recursive: true);
                _logger.LogInformation("Cleaned up artifact directory: {Path}", extractedPath);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "Failed to clean up artifact directory: {Path}", extractedPath);
            }
        }

        private static void CopyWithLimit(Stream source, Stream destination, long maxBytes)
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new InvalidOperationException(
                        $"GitHub artifact download exceeded the {maxBytes:N0} byte limit; aborting to prevent disk exhaustion.");
                }
                destination.Write(buffer, 0, read);
            }
        }

        private void RequirePermittedGitHubArtefactHost(string url)
        {
            if (_sourceHosts.IsArtefactSourceUnconfigured)
            {
                throw new InvalidOperationException(
                    $"Refusing to present GitHub credentials because no artefact host allow-list is configured ('{SourceHostAllowList.ArtefactHostsSetting}').");
            }

            var decision = _sourceHosts.CheckArtefactSource(url);
            if (decision == null || !decision.Allowed)
            {
                throw new InvalidOperationException(
                    $"Refusing to present GitHub credentials to '{url}', because {decision?.Reason ?? "host is not permitted"}");
            }
        }

        private bool IsUnderArtifactBase(string candidatePath)
        {
            var baseDir = !string.IsNullOrEmpty(_downloadFolder)
                ? _downloadFolder
                : Path.Join(DorcProgramData.Root, "artifacts");

            var canonicalBase = Path.GetFullPath(baseDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var canonicalCandidate = Path.GetFullPath(candidatePath);

            return canonicalCandidate.StartsWith(canonicalBase, StringComparison.OrdinalIgnoreCase);
        }
    }
}
