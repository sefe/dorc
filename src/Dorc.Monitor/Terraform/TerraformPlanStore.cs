using Dorc.Core;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;

namespace Dorc.Monitor.Terraform
{
    /// <summary>
    /// The local home of one dispatch's Terraform plan and sidecar artefacts, for as long as
    /// they are needed and no longer.
    ///
    /// Plan dispatches stage the binary plan, rendered plain-text plan, planned-source archive,
    /// and provider lockfile here; apply dispatches download the approved binary plan and source
    /// archive here and receive the Runner's applied-resource inventory and deployed-source
    /// archive sidecars. All of these previously landed in a single shared
    /// <c>%ProgramData%\dorc\terraform-plans</c> directory created with inherited permissions —
    /// readable by any authenticated user on the deployment host — and were either left behind
    /// forever or deleted before failure recovery could inspect them.
    ///
    /// Restricting a shared directory is not possible without knowing who to admit, which is why
    /// this waited for execution identity to become environment-dependent: each dispatch now gets
    /// a fresh unique <c>{resultId}-{nonce}</c> directory under the Monitor-only root, created
    /// initially for Monitor, SYSTEM, and Administrators only, and opened to that dispatch's
    /// resolved execution identity (<c>Modify | Synchronize</c>) only for the span of the Runner
    /// process. All post-runner Monitor reads, DACL updates, and cancel-signal writes use
    /// handle-pinned <c>FILE_FLAG_OPEN_REPARSE_POINT</c>, <c>GetFinalPathNameByHandleW</c>, and
    /// <c>GetFileInformationByHandle</c> single-link (<c>nNumberOfLinks == 1</c>) checks, staging
    /// verified artefacts into a Monitor-only directory before blob upload or provenance capture.
    /// </summary>
    internal sealed class TerraformPlanStore : IDisposable
    {
        internal const string DirectoryName = "terraform-plans";

        /// <summary>
        /// How long plan artefacts left behind by an earlier deployment may survive before a
        /// later one removes them.
        ///
        /// Confirmed plan-only completion (<c>WaitingConfirmation</c> after all uploads/receipts
        /// persist) and successful applies expire their per-dispatch directory immediately.
        /// Failed or cancelled plan/apply dispatches and post-apply registration/provenance/archive
        /// failures skip immediate expiry, revoking Runner access while retaining the confined
        /// Monitor-only directory for operator inspection, retry, or recovery up to this 7-day
        /// orphan-sweep window.
        /// </summary>
        internal static readonly TimeSpan OrphanedPlanRetention = TimeSpan.FromDays(7);

        private static readonly ConcurrentDictionary<string, byte> ActiveDirectories = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private readonly ILogger _logger;
        private readonly string _root;
        private readonly string _canonicalRoot;
        private readonly FileIdentity _rootIdentity;
        private readonly string _canonicalDirectory;
        private readonly FileIdentity _directoryIdentity;
        private readonly string _dispatchDirectoryName;
        private readonly List<string> _leasedPaths = new();
        private SafeFileHandle? _rootPinHandle;
        private SafeFileHandle? _directoryPinHandle;
        private SafeFileHandle? _stagingPinHandle;
        private string? _stagingDirectory;
        private string? _canonicalStagingDirectory;
        private FileIdentity _stagingDirectoryIdentity;
        private int _disposed;

        private TerraformPlanStore(
            ILogger logger,
            string root,
            string canonicalRoot,
            FileIdentity rootIdentity,
            SafeFileHandle? rootPinHandle,
            string directory,
            string canonicalDirectory,
            FileIdentity directoryIdentity,
            SafeFileHandle? directoryPinHandle,
            string dispatchDirectoryName)
        {
            _logger = logger;
            _root = root;
            _canonicalRoot = canonicalRoot;
            _rootIdentity = rootIdentity;
            _rootPinHandle = rootPinHandle;
            Directory = directory;
            _canonicalDirectory = canonicalDirectory;
            _directoryIdentity = directoryIdentity;
            _directoryPinHandle = directoryPinHandle;
            _dispatchDirectoryName = dispatchDirectoryName;
        }

        /// <summary>The per-dispatch directory the plan artefacts live in.</summary>
        public string Directory { get; }

        /// <summary>
        /// Reserves a fresh Monitor-only per-dispatch plan directory
        /// (<c>&lt;root&gt;\{deploymentResultId}-{nonce}</c>) before any runner identity is admitted.
        /// </summary>
        public static TerraformPlanStore Reserve(
            ILogger logger,
            string root,
            int deploymentResultId)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            ArgumentOutOfRangeException.ThrowIfNegative(deploymentResultId);

            var dispatchDirectoryName =
                $"{deploymentResultId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
            return ReserveWithDirectoryName(logger, root, dispatchDirectoryName);
        }

        /// <summary>
        /// Reserves a fresh per-dispatch plan directory and admits <paramref name="deploymentIdentity"/>.
        /// </summary>
        public static TerraformPlanStore Reserve(
            ILogger logger,
            string root,
            int deploymentResultId,
            ScriptGroupReaderIdentity deploymentIdentity)
        {
            ArgumentNullException.ThrowIfNull(deploymentIdentity);
            return Reserve(logger, root, deploymentResultId, deploymentIdentity.QualifiedAccountName);
        }

        /// <summary>
        /// Reserves a fresh per-dispatch plan directory and admits <paramref name="qualifiedAccountName"/>.
        /// </summary>
        public static TerraformPlanStore Reserve(
            ILogger logger,
            string root,
            int deploymentResultId,
            string qualifiedAccountName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedAccountName);

            var store = Reserve(logger, root, deploymentResultId);
            try
            {
                store.AdmitRunner(qualifiedAccountName);
                return store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        internal static TerraformPlanStore ReserveWithDirectoryName(
            ILogger logger,
            string root,
            string dispatchDirectoryName,
            string? programDataRoot = null)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            ValidateBareName(dispatchDirectoryName, nameof(dispatchDirectoryName));

            var canonicalRoot = EnsureRoot(
                root,
                out var rootIdentity,
                out var rootPinHandle,
                programDataRoot);

            var leaseAcquired = false;
            var normalizedDirectory = string.Empty;
            string? canonicalDirectory = null;
            SafeFileHandle? directoryPinHandle = null;
            TerraformPlanStore? store = null;

            try
            {
                var directory = Path.Join(root, dispatchDirectoryName);
                normalizedDirectory = NormalizePath(directory);

                if (File.Exists(directory)
                    || System.IO.Directory.Exists(directory)
                    || IsExistingReparsePoint(directory))
                {
                    throw new IOException(
                        $"Refusing to reserve Terraform plan directory '{directory}' because the path already exists.");
                }

                if (!ActiveDirectories.TryAdd(normalizedDirectory, 0))
                {
                    throw new IOException(
                        $"Refusing to reserve Terraform plan directory '{directory}' because it is already active.");
                }

                leaseAcquired = true;

                canonicalDirectory = CreateMonitorOnlyDirectory(
                    directory,
                    canonicalRoot,
                    rootIdentity,
                    dispatchDirectoryName,
                    out var directoryIdentity,
                    out directoryPinHandle);
                ActiveDirectories[canonicalDirectory] = 0;

                store = new TerraformPlanStore(
                    logger,
                    root,
                    canonicalRoot,
                    rootIdentity,
                    rootPinHandle,
                    directory,
                    canonicalDirectory,
                    directoryIdentity,
                    directoryPinHandle,
                    dispatchDirectoryName);
                rootPinHandle = null;
                directoryPinHandle = null;

                store.TrackLease(normalizedDirectory);
                store.TrackLease(canonicalDirectory);
                store.RemoveOrphanedPlans(root);

                return store;
            }
            catch
            {
                if (store is not null)
                {
                    store.Dispose();
                }
                else
                {
                    directoryPinHandle?.Dispose();
                    rootPinHandle?.Dispose();
                    if (leaseAcquired)
                    {
                        ActiveDirectories.TryRemove(normalizedDirectory, out _);
                    }

                    if (canonicalDirectory is not null)
                    {
                        ActiveDirectories.TryRemove(canonicalDirectory, out _);
                    }
                }

                throw;
            }
        }

        /// <summary>
        /// Grants the resolved execution identity <c>Modify | Synchronize</c> access on this
        /// dispatch's directory and any Monitor-staged input files inside it immediately before
        /// starting <c>Dorc.TerraformRunner.exe</c>.
        /// </summary>
        public void AdmitRunner(string qualifiedAccountName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedAccountName);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (IsExistingReparsePoint(Directory))
            {
                throw new IOException($"Refusing to use reparse point '{Directory}' as a Terraform plan directory.");
            }

            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            ApplyRunnerAccessWindows(qualifiedAccountName);
        }

        /// <summary>
        /// Restores this dispatch's plan directory and regular artefacts to Monitor, SYSTEM, and
        /// Administrators only once the Runner process has exited.
        /// </summary>
        public void RevokeRunner()
        {
            if (!OperatingSystem.IsWindows() || !System.IO.Directory.Exists(Directory))
            {
                return;
            }

            try
            {
                RevokeRunnerAccessWindows();
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Failed to revoke runner access on Terraform plan directory '{Directory}'.",
                    Directory);
            }
        }

        /// <summary>
        /// Where a named plan artefact belongs. Plan file names are derived from the deployment
        /// result identifier and are the blob names too, so they are not attacker-chosen — but a
        /// name that escaped this directory would escape its access control list as well, so the
        /// join refuses anything that is not a bare file name inside <see cref="Directory"/>.
        /// </summary>
        public string PathOf(string planFileName)
        {
            ValidateBareName(planFileName, nameof(planFileName));

            var canonicalDirectory = NormalizePath(Directory);
            var candidateFullPath = Path.GetFullPath(Path.Join(canonicalDirectory, planFileName));
            if (!string.Equals(
                    Path.GetDirectoryName(candidateFullPath),
                    canonicalDirectory,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"A Terraform plan artefact must resolve inside '{canonicalDirectory}'.",
                    nameof(planFileName));
            }

            return Path.Join(Directory, planFileName);
        }

        /// <summary>
        /// Opens a Runner-written artefact inside this store without following reparse points and
        /// verifies that its final canonical path resolves directly inside <see cref="Directory"/>.
        /// </summary>
        public FileStream OpenVerifiedRead(string planFileName)
        {
            if (!TryOpenVerifiedRead(planFileName, out var stream))
            {
                var fullPath = PathOf(planFileName);
                throw new FileNotFoundException(
                    $"Terraform plan artefact '{fullPath}' does not exist.",
                    fullPath);
            }

            return stream;
        }

        /// <summary>
        /// Opens an optional Runner-written artefact without following reparse points, returning
        /// <see langword="false"/> only when the file is genuinely absent while still throwing
        /// <see cref="IOException"/> if a reparse point, directory, or path escape is present.
        /// </summary>
        public bool TryOpenVerifiedRead(string planFileName, [NotNullWhen(true)] out FileStream? stream)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var fullPath = PathOf(planFileName);
            var expectedCanonicalPath = Path.Join(_canonicalDirectory, planFileName);

            if (OperatingSystem.IsWindows())
            {
                using var directoryHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                    Directory,
                    ReparseFreeHandles.FileReadAttributes);
                VerifyPinnedDispatchDirectoryWindows(directoryHandle, "read artefact from");
                stream = ReparseFreeHandles.OpenVerifiedRegularFileForRead(
                    fullPath,
                    expectedCanonicalPath,
                    _canonicalDirectory,
                    _directoryIdentity,
                    out var notFound);
                return !notFound && stream is not null;
            }

            var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Directory));
            var verifiedFullPath = Path.GetFullPath(fullPath);
            if (!verifiedFullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path", nameof(planFileName));
            }

            if (IsExistingReparsePoint(Directory))
            {
                throw new IOException($"Refusing to read from reparse point directory '{Directory}'.");
            }

            if (IsExistingReparsePoint(verifiedFullPath))
            {
                throw new IOException($"Refusing to read Terraform artefact '{verifiedFullPath}' because it is a reparse point.");
            }

            if (System.IO.Directory.Exists(verifiedFullPath))
            {
                throw new IOException($"Refusing to read Terraform artefact '{verifiedFullPath}' because it is a directory.");
            }

            if (!File.Exists(verifiedFullPath))
            {
                stream = null;
                return false;
            }

            stream = File.OpenRead(verifiedFullPath);
            return true;
        }

        /// <summary>
        /// Reads <paramref name="planFileName"/> once through a handle-verified stream and stages
        /// an immutable copy inside a Monitor-only staging directory so downstream blob uploads
        /// and provenance hashing cannot race with a runner principal.
        /// </summary>
        public string StageVerifiedArtefact(string planFileName)
        {
            using var source = OpenVerifiedRead(planFileName);
            return CopyVerifiedStreamToStaging(source, planFileName);
        }

        /// <summary>
        /// Stages an optional Runner-written artefact into a Monitor-only directory when present,
        /// returning <see langword="false"/> when absent and throwing <see cref="IOException"/>
        /// if a reparse point, directory, or path escape is detected.
        /// </summary>
        public bool TryStageVerifiedArtefact(string planFileName, [NotNullWhen(true)] out string? stagedPath)
        {
            if (!TryOpenVerifiedRead(planFileName, out var source))
            {
                stagedPath = null;
                return false;
            }

            using (source)
            {
                stagedPath = CopyVerifiedStreamToStaging(source, planFileName);
                return true;
            }
        }

        /// <summary>
        /// Writes a zero-byte <c>.cancel</c> sentinel inside <see cref="Directory"/> without
        /// following junctions or symbolic links.
        /// </summary>
        public void WriteCancelSignal(string cancelFileName)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var fullPath = PathOf(cancelFileName);
            var expectedCanonicalPath = Path.Join(_canonicalDirectory, cancelFileName);

            if (OperatingSystem.IsWindows())
            {
                using var directoryHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                    Directory,
                    ReparseFreeHandles.FileReadAttributes);
                VerifyPinnedDispatchDirectoryWindows(directoryHandle, "write cancel signal into");
                ReparseFreeHandles.WriteVerifiedEmptyFile(fullPath, expectedCanonicalPath);
                return;
            }

            var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Directory));
            var verifiedFullPath = Path.GetFullPath(fullPath);
            if (!verifiedFullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path", nameof(cancelFileName));
            }

            if (IsExistingReparsePoint(Directory) || IsExistingReparsePoint(verifiedFullPath))
            {
                throw new IOException($"Refusing to write cancel signal '{verifiedFullPath}' through a reparse point.");
            }

            if (System.IO.Directory.Exists(verifiedFullPath))
            {
                throw new IOException($"Refusing to write cancel signal '{verifiedFullPath}' because it is a directory.");
            }

            File.WriteAllBytes(verifiedFullPath, Array.Empty<byte>());
        }

        /// <summary>
        /// Removes this deployment's local plan and sidecar artefacts without traversing reparse
        /// points.
        ///
        /// Called immediately when plan creation reaches <c>WaitingConfirmation</c> (after all
        /// blob uploads and receipt writes succeed) and when an apply completes without
        /// registration, provenance, or source-archive failure. Deliberately skipped when a
        /// plan or apply runner invocation fails or is cancelled, or when post-apply recovery
        /// persistence fails: Runner access has already been revoked, and the confined
        /// Monitor-only directory is retained for operator recovery until
        /// <see cref="OrphanedPlanRetention"/> sweeps it.
        /// </summary>
        public void Expire()
        {
            try
            {
                if (IsExistingReparsePoint(Directory))
                {
                    _logger.LogWarning(
                        "Refusing to expire Terraform plan directory '{Directory}' because it is a reparse point.",
                        Directory);
                    return;
                }

                if (!System.IO.Directory.Exists(Directory))
                {
                    ReleaseDirectoryPinHandle();
                    return;
                }

                if (DeleteDirectorySafely(Directory, _directoryIdentity, ReleaseDirectoryPinHandle))
                {
                    _logger.LogDebug("Local Terraform plan artefacts in '{Directory}' have been removed.", Directory);
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                // Called from the deployment path. Failing to delete is worth knowing about but
                // must not replace the deployment's own outcome.
                _logger.LogError(
                    ex,
                    "Failed to remove local Terraform plan artefacts in '{Directory}'.",
                    Directory);
            }
        }

        /// <summary>
        /// Releases this store's active-sweep lease and deletes any temporary Monitor-only staging
        /// directory, while leaving <see cref="Directory"/> itself intact if <see cref="Expire"/>
        /// was not called so failed dispatches keep their 7-day recovery copy.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_stagingDirectory is not null)
            {
                try
                {
                    if (System.IO.Directory.Exists(_stagingDirectory))
                    {
                        DeleteDirectorySafely(
                            _stagingDirectory,
                            _stagingDirectoryIdentity,
                            ReleaseStagingPinHandle);
                    }
                }
                catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to remove temporary Monitor staging directory '{StagingDirectory}'.",
                        _stagingDirectory);
                }
                finally
                {
                    ReleaseStagingPinHandle();
                }
            }
            else
            {
                ReleaseStagingPinHandle();
            }

            ReleaseDirectoryPinHandle();
            ReleaseRootPinHandle();

            foreach (var leasedPath in _leasedPaths)
            {
                ActiveDirectories.TryRemove(leasedPath, out _);
            }
        }

        internal void ReleasePinHandlesForTesting()
        {
            ReleaseDirectoryPinHandle();
            ReleaseStagingPinHandle();
            ReleaseRootPinHandle();
        }

        private void ReleaseDirectoryPinHandle() =>
            Interlocked.Exchange(ref _directoryPinHandle, null)?.Dispose();

        private void ReleaseStagingPinHandle() =>
            Interlocked.Exchange(ref _stagingPinHandle, null)?.Dispose();

        private void ReleaseRootPinHandle() =>
            Interlocked.Exchange(ref _rootPinHandle, null)?.Dispose();

        private void TrackLease(string path)
        {
            if (!_leasedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _leasedPaths.Add(path);
            }
        }

        private string CopyVerifiedStreamToStaging(FileStream source, string planFileName)
        {
            ValidateBareName(planFileName, nameof(planFileName));
            EnsureStagingDirectory();
            var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_stagingDirectory!));
            var stagedPath = Path.Join(baseFull, planFileName);
            var fullPath = Path.GetFullPath(stagedPath);
            if (!fullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path", nameof(planFileName));
            }

            var expectedCanonicalStagedPath = Path.Join(_canonicalStagingDirectory!, planFileName);

            if (OperatingSystem.IsWindows())
            {
                using var stagingHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                    _stagingDirectory!,
                    ReparseFreeHandles.FileReadAttributes);
                var actualStagingCanonical = ReparseFreeHandles.GetCanonicalPath(stagingHandle, _stagingDirectory!);
                if (!string.Equals(actualStagingCanonical, _canonicalStagingDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(
                        $"Monitor staging directory '{_stagingDirectory}' resolved to '{actualStagingCanonical}' instead of '{_canonicalStagingDirectory}'.");
                }

                ReparseFreeHandles.VerifyFileIdentity(stagingHandle, _stagingDirectoryIdentity, _stagingDirectory!);
                using var destination = ReparseFreeHandles.OpenVerifiedRegularFileForWrite(
                    fullPath,
                    expectedCanonicalStagedPath,
                    overwriteExisting: true);
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
                return fullPath;
            }

            using (var destination = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }

            return fullPath;
        }

        private void EnsureStagingDirectory()
        {
            if (_stagingDirectory is not null)
            {
                return;
            }

            var stagingName = $"_staging-{_dispatchDirectoryName}";
            var candidatePath = Path.Join(_root, stagingName);
            var normalizedStaging = NormalizePath(candidatePath);

            if (!ActiveDirectories.TryAdd(normalizedStaging, 0))
            {
                throw new IOException($"Monitor staging directory '{candidatePath}' is already active.");
            }

            TrackLease(normalizedStaging);

            var canonicalStaging = CreateMonitorOnlyDirectory(
                candidatePath,
                _canonicalRoot,
                _rootIdentity,
                stagingName,
                out var stagingIdentity,
                out var stagingPinHandle);
            ActiveDirectories[canonicalStaging] = 0;
            TrackLease(canonicalStaging);

            _stagingDirectory = candidatePath;
            _canonicalStagingDirectory = canonicalStaging;
            _stagingDirectoryIdentity = stagingIdentity;
            _stagingPinHandle = stagingPinHandle;
        }

        private static string EnsureRoot(
            string root,
            out FileIdentity rootIdentity,
            out SafeFileHandle? rootPinHandle,
            string? programDataRoot = null)
        {
            if (IsExistingReparsePoint(root))
            {
                throw new IOException($"Refusing to use reparse point '{root}' as the Terraform plan root.");
            }

            if (!OperatingSystem.IsWindows())
            {
                System.IO.Directory.CreateDirectory(root);
                rootIdentity = default;
                rootPinHandle = null;
                return NormalizePath(root);
            }

            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var parentOfRoot = Path.GetDirectoryName(normalizedRoot);
            var normalizedProgramDataRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(programDataRoot ?? DorcProgramData.Root));

            if (parentOfRoot is not null
                && string.Equals(parentOfRoot, normalizedProgramDataRoot, StringComparison.OrdinalIgnoreCase))
            {
                RestrictedDirectory.EnsureProgramDataRootAndPin(
                    normalizedProgramDataRoot,
                    out var programDataIdentity,
                    out var programDataPinHandle);
                using (programDataPinHandle)
                {
                    var canonicalUnderProgramData = RestrictedDirectory.EnsureAndPin(
                        root,
                        programDataIdentity,
                        out rootIdentity,
                        out var pinnedRootUnderProgramData);
                    rootPinHandle = pinnedRootUnderProgramData;
                    return canonicalUnderProgramData;
                }
            }

            // Root admits ONLY Monitor + SYSTEM + Administrators. Admitting any deployment
            // identity to the root would let it enumerate other deployments' directories.
            var canonicalRoot = RestrictedDirectory.EnsureAndPin(
                root,
                expectedParentIdentity: null,
                out rootIdentity,
                out var pinnedRoot);
            rootPinHandle = pinnedRoot;
            return canonicalRoot;
        }

        private static string CreateMonitorOnlyDirectory(
            string directory,
            string canonicalRoot,
            FileIdentity rootIdentity,
            string expectedDirectoryName,
            out FileIdentity directoryIdentity,
            out SafeFileHandle? directoryPinHandle)
        {
            if (!OperatingSystem.IsWindows())
            {
                if (File.Exists(directory)
                    || System.IO.Directory.Exists(directory)
                    || IsExistingReparsePoint(directory))
                {
                    throw new IOException($"Refusing to reserve existing path '{directory}'.");
                }

                System.IO.Directory.CreateDirectory(directory);
                directoryIdentity = default;
                directoryPinHandle = null;
                return NormalizePath(directory);
            }

            var canonicalDirectory = RestrictedDirectory.CreateNew(
                directory,
                rootIdentity,
                out directoryIdentity,
                out var pinnedDirectory);
            var expectedCanonical = Path.Join(canonicalRoot, expectedDirectoryName);
            if (!string.Equals(canonicalDirectory, expectedCanonical, StringComparison.OrdinalIgnoreCase))
            {
                pinnedDirectory.Dispose();
                throw new IOException(
                    $"Reserved Terraform directory '{directory}' resolved to '{canonicalDirectory}' instead of '{expectedCanonical}'.");
            }

            directoryPinHandle = pinnedDirectory;
            return canonicalDirectory;
        }

        [SupportedOSPlatform("windows")]
        private void VerifyPinnedDispatchDirectoryWindows(
            Microsoft.Win32.SafeHandles.SafeFileHandle directoryHandle,
            string operation)
        {
            var actualCanonical = ReparseFreeHandles.GetCanonicalPath(directoryHandle, Directory);
            if (!string.Equals(actualCanonical, _canonicalDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Refusing to {operation} '{Directory}' because it resolved to '{actualCanonical}' instead of '{_canonicalDirectory}'.");
            }

            ReparseFreeHandles.VerifyFileIdentity(directoryHandle, _directoryIdentity, Directory);
        }

        [SupportedOSPlatform("windows")]
        private void ApplyRunnerAccessWindows(string qualifiedAccountName)
        {
            // Failure to resolve the identity is logged, not thrown. The confinement is the
            // protected access control list, which is applied either way; what an unresolvable
            // identity costs is the deployment's ability to write its plan, and that surfaces as
            // an access denial from Terraform moments later.
            var resolution = DeploymentPrincipal.Resolve(qualifiedAccountName);
            if (!resolution.IsResolved)
            {
                _logger.LogError(
                    resolution.Cause,
                    "{Refusal} The Terraform plan directory '{Directory}' has been kept without it, so"
                    + " the Runner will be unable to write its plan unless it runs as an account the"
                    + " directory already admits.",
                    resolution.Refusal,
                    Directory);
            }

            IdentityReference[] additional = resolution.SecurityIdentifier is null
                ? []
                : [resolution.SecurityIdentifier];

            var directorySecurity = RestrictedDirectory.BuildSecurity(additional);
            var fileSecurity = RestrictedDirectory.BuildFileSecurity(additional);

            using var directoryHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(Directory);
            VerifyPinnedDispatchDirectoryWindows(directoryHandle, "admit runner to");
            ReparseFreeHandles.ApplyProtectedDacl(directoryHandle, directorySecurity, Directory);

            // Propagate the same grant onto any inputs Monitor staged before admitting the runner
            // (specifically .tfplan and .source.zip on ApplyPlan) using verified file handles.
            foreach (var entry in new DirectoryInfo(_canonicalDirectory).EnumerateFileSystemInfos())
            {
                var expectedChildCanonical = Path.Join(_canonicalDirectory, entry.Name);
                using var fileHandle = ReparseFreeHandles.OpenVerifiedRegularFileForDacl(
                    entry.FullName,
                    expectedChildCanonical);
                ReparseFreeHandles.ApplyProtectedDacl(fileHandle, fileSecurity, entry.FullName);
            }
        }

        [SupportedOSPlatform("windows")]
        internal void RevokeRunnerAccessWindows()
        {
            // Post-run lockdown applies protected, non-inheriting directory DACLs handle-by-handle
            // so directory ACL updates never auto-propagate onto unverified hard-linked children
            // before each child handle's link count is checked.
            var monitorOnlyDirectorySecurity = RestrictedDirectory.BuildSecurity([], inheritToChildren: false);
            var monitorOnlyFileSecurity = RestrictedDirectory.BuildFileSecurity([]);

            using var directoryHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(Directory);
            VerifyPinnedDispatchDirectoryWindows(directoryHandle, "revoke runner access on");
            ReparseFreeHandles.ApplyProtectedDacl(
                directoryHandle,
                monitorOnlyDirectorySecurity,
                Directory);

            IOException? hardLinkViolation = null;
            RevokeRunnerOnChildrenWindows(
                _canonicalDirectory,
                monitorOnlyDirectorySecurity,
                monitorOnlyFileSecurity,
                ref hardLinkViolation);
            if (hardLinkViolation is not null)
            {
                throw hardLinkViolation;
            }
        }

        [SupportedOSPlatform("windows")]
        private void RevokeRunnerOnChildrenWindows(
            string parentCanonicalDirectory,
            System.Security.AccessControl.DirectorySecurity monitorOnlyDirectorySecurity,
            System.Security.AccessControl.FileSecurity monitorOnlyFileSecurity,
            ref IOException? hardLinkViolation)
        {
            foreach (var entry in new DirectoryInfo(parentCanonicalDirectory).EnumerateFileSystemInfos())
            {
                if (IsReparsePoint(entry))
                {
                    continue;
                }

                var expectedChildCanonical = Path.Join(parentCanonicalDirectory, entry.Name);
                using var childHandle = ReparseFreeHandles.OpenRawHandle(
                    expectedChildCanonical,
                    ReparseFreeHandles.FileReadAttributes
                    | ReparseFreeHandles.ReadControl
                    | ReparseFreeHandles.WriteDac,
                    ReparseFreeHandles.FileShareRead
                    | ReparseFreeHandles.FileShareWrite
                    | ReparseFreeHandles.FileShareDelete,
                    ReparseFreeHandles.OpenExisting,
                    out var openError);
                if (childHandle.IsInvalid)
                {
                    if (openError is ReparseFreeHandles.ErrorFileNotFound or ReparseFreeHandles.ErrorPathNotFound)
                    {
                        continue;
                    }

                    throw ReparseFreeHandles.CreateWin32IoException(
                        $"Failed to open '{expectedChildCanonical}' while revoking runner access",
                        openError);
                }

                var childAttributes = File.GetAttributes(childHandle);
                if ((childAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var actualChildCanonical = ReparseFreeHandles.GetCanonicalPath(childHandle, expectedChildCanonical);
                if (!string.Equals(actualChildCanonical, expectedChildCanonical, StringComparison.OrdinalIgnoreCase)
                    || !IsStrictlyBeneath(actualChildCanonical, _canonicalDirectory))
                {
                    throw new IOException(
                        $"Refusing to revoke runner access on '{expectedChildCanonical}' because its canonical path '{actualChildCanonical}' escaped '{parentCanonicalDirectory}'.");
                }

                if ((childAttributes & FileAttributes.Directory) != 0)
                {
                    ReparseFreeHandles.ApplyProtectedDacl(
                        childHandle,
                        monitorOnlyDirectorySecurity,
                        actualChildCanonical);
                    RevokeRunnerOnChildrenWindows(
                        actualChildCanonical,
                        monitorOnlyDirectorySecurity,
                        monitorOnlyFileSecurity,
                        ref hardLinkViolation);
                }
                else
                {
                    var linkCount = ReparseFreeHandles.GetLinkCount(childHandle, actualChildCanonical);
                    if (linkCount != 1)
                    {
                        hardLinkViolation ??= new IOException(
                            $"Refusing to rewrite DACL on '{actualChildCanonical}' because it has {linkCount} NTFS links (expected 1).");
                        continue;
                    }

                    ReparseFreeHandles.ApplyProtectedDacl(
                        childHandle,
                        monitorOnlyFileSecurity,
                        actualChildCanonical);
                }
            }
        }

        /// <summary>
        /// Removes plan artefacts that outlived the retention window without following symlinks
        /// or NTFS junctions, while skipping any directory with an active in-flight lease.
        /// </summary>
        private void RemoveOrphanedPlans(string root)
        {
            try
            {
                if (IsExistingReparsePoint(root))
                {
                    _logger.LogWarning(
                        "Refusing to sweep Terraform plan root '{Root}' because it is a reparse point.",
                        root);
                    return;
                }

                var cutoff = DateTime.UtcNow - OrphanedPlanRetention;
                var removed = 0;

                foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
                {
                    try
                    {
                        if (IsActiveDirectory(entry))
                        {
                            continue;
                        }

                        if (IsReparsePoint(entry))
                        {
                            _logger.LogWarning(
                                "Refusing to traverse or delete reparse point '{ReparsePath}' during Terraform plan sweep.",
                                entry.FullName);
                            continue;
                        }

                        if (entry is DirectoryInfo subDirectory)
                        {
                            // The write time of a directory does not move when a file inside it is
                            // written, so the newest artefact decides whether the directory is
                            // spent - not the moment it was created.
                            if (LastWriteWithin(subDirectory, cutoff))
                            {
                                continue;
                            }

                            if (DeleteDirectorySafely(subDirectory.FullName))
                            {
                                removed++;
                            }
                        }
                        else
                        {
                            if (entry.LastWriteTimeUtc >= cutoff)
                            {
                                ConfineRetainedRootFile(entry);
                                continue;
                            }

                            DeleteRegularRootFile(entry);
                            removed++;
                        }
                    }
                    catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to remove orphaned Terraform plan artefacts at '{EntryPath}'.",
                            entry.FullName);
                    }
                }

                if (removed > 0)
                {
                    _logger.LogInformation(
                        "Removed {Count} orphaned Terraform plan artefact(s) from '{Root}'.",
                        removed,
                        root);
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                // Housekeeping. Never let it stop a deployment.
                _logger.LogWarning(
                    ex,
                    "Failed to enumerate Terraform plan artefacts in '{Root}'.",
                    root);
            }
        }

        private static bool IsActiveDirectory(FileSystemInfo entry)
        {
            if (ActiveDirectories.ContainsKey(NormalizePath(entry.FullName)))
            {
                return true;
            }

            if (OperatingSystem.IsWindows() && entry is DirectoryInfo && !IsReparsePoint(entry))
            {
                var handle = ReparseFreeHandles.OpenRawHandle(
                    entry.FullName,
                    ReparseFreeHandles.FileReadAttributes,
                    ReparseFreeHandles.FileShareRead | ReparseFreeHandles.FileShareWrite | ReparseFreeHandles.FileShareDelete,
                    ReparseFreeHandles.OpenExisting,
                    out _);
                using (handle)
                {
                    if (!handle.IsInvalid)
                    {
                        var canonical = ReparseFreeHandles.GetCanonicalPath(handle, entry.FullName);
                        if (ActiveDirectories.ContainsKey(canonical))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void ConfineRetainedRootFile(FileSystemInfo entry)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
            var fullPath = Path.GetFullPath(entry.FullName);
            if (!fullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path", nameof(entry));
            }

            ConfineRetainedRootFileWindows(fullPath, entry.Name);
        }

        [SupportedOSPlatform("windows")]
        private void ConfineRetainedRootFileWindows(string fullPath, string entryName)
        {
            using var handle = ReparseFreeHandles.OpenRawHandle(
                fullPath,
                ReparseFreeHandles.FileReadAttributes
                | ReparseFreeHandles.ReadControl
                | ReparseFreeHandles.WriteDac,
                ReparseFreeHandles.FileShareRead
                | ReparseFreeHandles.FileShareWrite
                | ReparseFreeHandles.FileShareDelete,
                ReparseFreeHandles.OpenExisting,
                out var error);
            if (handle.IsInvalid)
            {
                if (error is ReparseFreeHandles.ErrorFileNotFound or ReparseFreeHandles.ErrorPathNotFound)
                {
                    return;
                }

                throw ReparseFreeHandles.CreateWin32IoException(
                    $"Failed to open '{fullPath}' to confine retained Terraform plan artefact",
                    error);
            }

            var attributes = File.GetAttributes(handle);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                _logger.LogWarning(
                    "Refusing to rewrite DACL on reparse point '{ReparsePath}' during Terraform plan sweep.",
                    fullPath);
                return;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                return;
            }

            var canonicalFile = ReparseFreeHandles.GetCanonicalPath(handle, fullPath);
            var expectedCanonical = Path.Join(_canonicalRoot, entryName);
            if (!string.Equals(canonicalFile, expectedCanonical, StringComparison.OrdinalIgnoreCase)
                || !IsDirectCanonicalChild(canonicalFile, _canonicalRoot))
            {
                _logger.LogWarning(
                    "Refusing to rewrite DACL on '{FilePath}' because its canonical path '{CanonicalPath}' escaped '{CanonicalRoot}'.",
                    fullPath,
                    canonicalFile,
                    _canonicalRoot);
                return;
            }

            var linkCount = ReparseFreeHandles.GetLinkCount(handle, canonicalFile);
            if (linkCount != 1)
            {
                _logger.LogWarning(
                    "Refusing to rewrite DACL on '{FilePath}' during Terraform plan sweep because it has {LinkCount} NTFS links (expected 1).",
                    canonicalFile,
                    linkCount);
                return;
            }

            ReparseFreeHandles.ApplyProtectedDacl(
                handle,
                RestrictedDirectory.BuildFileSecurity([]),
                canonicalFile);
        }

        private void DeleteRegularRootFile(FileSystemInfo entry)
        {
            var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
            var fullPath = Path.GetFullPath(entry.FullName);
            if (!fullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path", nameof(entry));
            }

            if (OperatingSystem.IsWindows())
            {
                var handle = ReparseFreeHandles.OpenRawHandle(
                    fullPath,
                    ReparseFreeHandles.Delete
                    | ReparseFreeHandles.FileReadAttributes
                    | ReparseFreeHandles.FileWriteAttributes,
                    ReparseFreeHandles.FileShareRead
                    | ReparseFreeHandles.FileShareWrite
                    | ReparseFreeHandles.FileShareDelete,
                    ReparseFreeHandles.OpenExisting,
                    out var error);
                if (handle.IsInvalid && error == ReparseFreeHandles.ErrorAccessDenied)
                {
                    handle.Dispose();
                    handle = ReparseFreeHandles.OpenRawHandle(
                        fullPath,
                        ReparseFreeHandles.Delete | ReparseFreeHandles.FileReadAttributes,
                        ReparseFreeHandles.FileShareRead
                        | ReparseFreeHandles.FileShareWrite
                        | ReparseFreeHandles.FileShareDelete,
                        ReparseFreeHandles.OpenExisting,
                        out error);
                }

                using (handle)
                {
                    if (handle.IsInvalid)
                    {
                        if (error is ReparseFreeHandles.ErrorFileNotFound or ReparseFreeHandles.ErrorPathNotFound)
                        {
                            return;
                        }

                        throw ReparseFreeHandles.CreateWin32IoException($"Failed to open '{fullPath}' for deletion", error);
                    }

                    var attributes = File.GetAttributes(handle);
                    if ((attributes & FileAttributes.ReparsePoint) != 0
                        || (attributes & FileAttributes.Directory) != 0)
                    {
                        return;
                    }

                    var canonicalFile = ReparseFreeHandles.GetCanonicalPath(handle, fullPath);
                    if (!IsDirectCanonicalChild(canonicalFile, _canonicalRoot))
                    {
                        return;
                    }

                    ReparseFreeHandles.DeleteByHandle(handle, canonicalFile);
                }

                return;
            }

            ClearReadOnlyAttribute(entry);
            File.Delete(fullPath);
        }

        private bool DeleteDirectorySafely(
            string directoryPath,
            FileIdentity? expectedDirectoryIdentity = null,
            Action? releasePinBeforeFinalDelete = null)
        {
            if (OperatingSystem.IsWindows())
            {
                return DeleteDirectorySafelyWindows(
                    directoryPath,
                    expectedDirectoryIdentity,
                    releasePinBeforeFinalDelete);
            }

            releasePinBeforeFinalDelete?.Invoke();
            return DeleteDirectorySafelyNonWindows(new DirectoryInfo(directoryPath));
        }

        [SupportedOSPlatform("windows")]
        private bool DeleteDirectorySafelyWindows(
            string directoryPath,
            FileIdentity? expectedDirectoryIdentity = null,
            Action? releasePinBeforeFinalDelete = null)
        {
            string canonicalDir;
            FileIdentity directoryIdentity;
            var dirHandle = ReparseFreeHandles.OpenRawHandle(
                directoryPath,
                ReparseFreeHandles.FileListDirectory
                | ReparseFreeHandles.FileReadAttributes
                | ReparseFreeHandles.FileWriteAttributes
                | ReparseFreeHandles.ReadControl
                | ReparseFreeHandles.WriteDac,
                ReparseFreeHandles.FileShareRead,
                ReparseFreeHandles.OpenExisting,
                out var openError);
            using (dirHandle)
            {
                if (dirHandle.IsInvalid)
                {
                    if (openError is ReparseFreeHandles.ErrorFileNotFound or ReparseFreeHandles.ErrorPathNotFound)
                    {
                        return true;
                    }

                    throw ReparseFreeHandles.CreateWin32IoException(
                        $"Failed to open directory '{directoryPath}' for deletion",
                        openError);
                }

                var dirAttributes = File.GetAttributes(dirHandle);
                if ((dirAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    _logger.LogWarning(
                        "Refusing to traverse or delete reparse point '{ReparsePath}' under Terraform plan storage.",
                        directoryPath);
                    return false;
                }

                if ((dirAttributes & FileAttributes.Directory) == 0)
                {
                    throw new IOException($"Expected a directory at '{directoryPath}'.");
                }

                directoryIdentity = ReparseFreeHandles.GetFileIdentity(dirHandle, directoryPath);
                if (expectedDirectoryIdentity.HasValue && directoryIdentity != expectedDirectoryIdentity.Value)
                {
                    _logger.LogWarning(
                        "Refusing to delete directory '{DirectoryPath}' because its NTFS file ID changed from {ExpectedId} to {ActualId}.",
                        directoryPath,
                        expectedDirectoryIdentity.Value,
                        directoryIdentity);
                    return false;
                }

                canonicalDir = ReparseFreeHandles.GetCanonicalPath(dirHandle, directoryPath);
                if (!IsStrictlyBeneath(canonicalDir, _canonicalRoot))
                {
                    _logger.LogWarning(
                        "Refusing to delete directory '{CanonicalPath}' because it escaped Terraform plan root '{CanonicalRoot}'.",
                        canonicalDir,
                        _canonicalRoot);
                    return false;
                }

                // Strip non-privileged ACEs on the directory handle BEFORE deleting children while
                // keeping dirHandle open without FileShareWrite across enumeration and child unlinking.
                // Use non-inheriting protected ACEs so directory DACL updates never propagate onto
                // unverified hard-linked children.
                ReparseFreeHandles.ApplyProtectedDacl(
                    dirHandle,
                    RestrictedDirectory.BuildSecurity([], inheritToChildren: false),
                    canonicalDir);
                ReparseFreeHandles.ClearReadOnlyByHandle(dirHandle);

                foreach (var entry in new DirectoryInfo(canonicalDir).EnumerateFileSystemInfos())
                {
                    if ((File.GetAttributes(dirHandle) & FileAttributes.ReparsePoint) != 0)
                    {
                        _logger.LogWarning(
                            "Refusing to continue deleting '{CanonicalPath}' because it became a reparse point during enumeration.",
                            canonicalDir);
                        return false;
                    }

                    var childPath = Path.Join(canonicalDir, entry.Name);
                    var childHandle = OpenChildHandleForCleanupWindows(childPath, canonicalDir, out var childError);

                    string? subDirectoryCanonical = null;
                    FileIdentity subDirectoryIdentity = default;
                    using (childHandle)
                    {
                        if (childHandle.IsInvalid)
                        {
                            if (childError is ReparseFreeHandles.ErrorFileNotFound or ReparseFreeHandles.ErrorPathNotFound)
                            {
                                continue;
                            }

                            throw ReparseFreeHandles.CreateWin32IoException(
                                $"Failed to inspect '{childPath}' during Terraform plan cleanup",
                                childError);
                        }

                        if ((File.GetAttributes(dirHandle) & FileAttributes.ReparsePoint) != 0)
                        {
                            _logger.LogWarning(
                                "Refusing to unlink '{ChildPath}' because parent directory '{CanonicalPath}' became a reparse point.",
                                childPath,
                                canonicalDir);
                            return false;
                        }

                        var childCanonical = ReparseFreeHandles.GetCanonicalPath(childHandle, childPath);
                        if (!IsDirectCanonicalChild(childCanonical, canonicalDir)
                            || !IsStrictlyBeneath(childCanonical, _canonicalRoot))
                        {
                            _logger.LogWarning(
                                "Refusing to delete '{ChildPath}' because its canonical path '{CanonicalChild}' escaped '{CanonicalParent}'.",
                                childPath,
                                childCanonical,
                                canonicalDir);
                            return false;
                        }

                        var childAttributes = File.GetAttributes(childHandle);
                        var isReparse = (childAttributes & FileAttributes.ReparsePoint) != 0;
                        var isDirectory = (childAttributes & FileAttributes.Directory) != 0;

                        if (isReparse)
                        {
                            // Unlink the planted junction or symbolic link directly via the open
                            // FILE_FLAG_OPEN_REPARSE_POINT handle after confirming its own canonical
                            // link location is a direct child of canonicalDir beneath _canonicalRoot.
                            ReparseFreeHandles.DeleteByHandle(childHandle, childCanonical);
                        }
                        else if (isDirectory)
                        {
                            subDirectoryCanonical = childCanonical;
                            subDirectoryIdentity = ReparseFreeHandles.GetFileIdentity(childHandle, childCanonical);
                        }
                        else
                        {
                            ReparseFreeHandles.DeleteByHandle(childHandle, childCanonical);
                        }
                    }

                    if (subDirectoryCanonical is not null
                        && !DeleteDirectorySafelyWindows(subDirectoryCanonical, subDirectoryIdentity))
                    {
                        return false;
                    }
                }
            }

            releasePinBeforeFinalDelete?.Invoke();

            using (var finalHandle = OpenDirectoryHandleForDeletionWindows(canonicalDir, out var finalError))
            {
                if (finalHandle.IsInvalid)
                {
                    if (finalError is ReparseFreeHandles.ErrorFileNotFound or ReparseFreeHandles.ErrorPathNotFound)
                    {
                        return true;
                    }

                    throw ReparseFreeHandles.CreateWin32IoException(
                        $"Failed to re-verify '{canonicalDir}' before removing directory",
                        finalError);
                }

                var finalAttributes = File.GetAttributes(finalHandle);
                if ((finalAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    _logger.LogWarning(
                        "Refusing to delete Terraform plan directory '{ReparsePath}' because it became a reparse point.",
                        canonicalDir);
                    return false;
                }

                if ((finalAttributes & FileAttributes.Directory) == 0)
                {
                    throw new IOException($"Expected a directory at '{canonicalDir}'.");
                }

                var finalIdentity = ReparseFreeHandles.GetFileIdentity(finalHandle, canonicalDir);
                if (finalIdentity != directoryIdentity)
                {
                    _logger.LogWarning(
                        "Refusing to delete Terraform plan directory '{CanonicalDir}' because its NTFS file ID changed from {ExpectedId} to {ActualId}.",
                        canonicalDir,
                        directoryIdentity,
                        finalIdentity);
                    return false;
                }

                var finalCanonical = ReparseFreeHandles.GetCanonicalPath(finalHandle, canonicalDir);
                if (!string.Equals(finalCanonical, canonicalDir, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Refusing to delete Terraform plan directory '{CanonicalDir}' because it resolved to '{FinalCanonical}'.",
                        canonicalDir,
                        finalCanonical);
                    return false;
                }

                ReparseFreeHandles.DeleteByHandle(finalHandle, canonicalDir);
            }

            return true;
        }

        [SupportedOSPlatform("windows")]
        private Microsoft.Win32.SafeHandles.SafeFileHandle OpenChildHandleForCleanupWindows(
            string childPath,
            string parentCanonicalDir,
            out int error)
        {
            const uint shareAll = ReparseFreeHandles.FileShareRead
                | ReparseFreeHandles.FileShareWrite
                | ReparseFreeHandles.FileShareDelete;

            var handle = ReparseFreeHandles.OpenRawHandle(
                childPath,
                ReparseFreeHandles.Delete
                | ReparseFreeHandles.FileReadAttributes
                | ReparseFreeHandles.FileWriteAttributes,
                shareAll,
                ReparseFreeHandles.OpenExisting,
                out error);
            if (!handle.IsInvalid || error != ReparseFreeHandles.ErrorAccessDenied)
            {
                return handle;
            }

            handle.Dispose();
            handle = ReparseFreeHandles.OpenRawHandle(
                childPath,
                ReparseFreeHandles.Delete | ReparseFreeHandles.FileReadAttributes,
                shareAll,
                ReparseFreeHandles.OpenExisting,
                out error);
            if (!handle.IsInvalid || error != ReparseFreeHandles.ErrorAccessDenied)
            {
                return handle;
            }

            // Non-empty subdirectories do not require DELETE until their recursive call empties them;
            // open for attribute/canonical inspection if DELETE was denied (e.g. ReadOnly directory).
            handle.Dispose();
            using var inspectHandle = ReparseFreeHandles.OpenRawHandle(
                childPath,
                ReparseFreeHandles.FileReadAttributes
                | ReparseFreeHandles.ReadControl
                | ReparseFreeHandles.WriteDac,
                shareAll,
                ReparseFreeHandles.OpenExisting,
                out var inspectError);
            if (!inspectHandle.IsInvalid)
            {
                var attrs = File.GetAttributes(inspectHandle);
                if ((attrs & FileAttributes.ReparsePoint) == 0)
                {
                    var canonical = ReparseFreeHandles.GetCanonicalPath(inspectHandle, childPath);
                    if (IsDirectCanonicalChild(canonical, parentCanonicalDir)
                        && IsStrictlyBeneath(canonical, _canonicalRoot))
                    {
                        var childIdentity = ReparseFreeHandles.GetFileIdentity(inspectHandle, canonical);
                        if ((attrs & FileAttributes.Directory) != 0)
                        {
                            ReparseFreeHandles.ApplyProtectedDacl(
                                inspectHandle,
                                RestrictedDirectory.BuildSecurity([], inheritToChildren: false),
                                canonical);
                            var dirInspectHandle = ReparseFreeHandles.OpenRawHandle(
                                canonical,
                                ReparseFreeHandles.FileReadAttributes,
                                shareAll,
                                ReparseFreeHandles.OpenExisting,
                                out error);
                            if (!dirInspectHandle.IsInvalid)
                            {
                                ReparseFreeHandles.VerifyFileIdentity(dirInspectHandle, childIdentity, canonical);
                            }

                            return dirInspectHandle;
                        }

                        if (ReparseFreeHandles.GetLinkCount(inspectHandle, canonical) == 1)
                        {
                            ReparseFreeHandles.ApplyProtectedDacl(
                                inspectHandle,
                                RestrictedDirectory.BuildFileSecurity([]),
                                canonical);
                            var reopenedFile = ReparseFreeHandles.OpenRawHandle(
                                canonical,
                                ReparseFreeHandles.Delete
                                | ReparseFreeHandles.FileReadAttributes
                                | ReparseFreeHandles.FileWriteAttributes,
                                shareAll,
                                ReparseFreeHandles.OpenExisting,
                                out error);
                            if (!reopenedFile.IsInvalid)
                            {
                                ReparseFreeHandles.VerifyFileIdentity(reopenedFile, childIdentity, canonical);
                            }

                            return reopenedFile;
                        }
                    }
                }
            }

            return ReparseFreeHandles.OpenRawHandle(
                childPath,
                ReparseFreeHandles.Delete | ReparseFreeHandles.FileReadAttributes,
                shareAll,
                ReparseFreeHandles.OpenExisting,
                out error);
        }

        [SupportedOSPlatform("windows")]
        private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenDirectoryHandleForDeletionWindows(
            string canonicalDir,
            out int error)
        {
            const uint shareAll = ReparseFreeHandles.FileShareRead
                | ReparseFreeHandles.FileShareWrite
                | ReparseFreeHandles.FileShareDelete;

            var handle = ReparseFreeHandles.OpenRawHandle(
                canonicalDir,
                ReparseFreeHandles.Delete
                | ReparseFreeHandles.FileReadAttributes
                | ReparseFreeHandles.FileWriteAttributes,
                shareAll,
                ReparseFreeHandles.OpenExisting,
                out error);
            if (handle.IsInvalid && error == ReparseFreeHandles.ErrorAccessDenied)
            {
                handle.Dispose();
                handle = ReparseFreeHandles.OpenRawHandle(
                    canonicalDir,
                    ReparseFreeHandles.Delete | ReparseFreeHandles.FileReadAttributes,
                    shareAll,
                    ReparseFreeHandles.OpenExisting,
                    out error);
            }

            return handle;
        }

        private bool DeleteDirectorySafelyNonWindows(DirectoryInfo directory)
        {
            var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
            var directoryFullPath = Path.GetFullPath(directory.FullName);
            if (!directoryFullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid file path", nameof(directory));
            }

            if (IsReparsePoint(directory))
            {
                _logger.LogWarning(
                    "Refusing to traverse or delete reparse point '{ReparsePath}' under Terraform plan storage.",
                    directoryFullPath);
                return false;
            }

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                var entryFullPath = Path.GetFullPath(entry.FullName);
                if (!entryFullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Invalid file path", nameof(directory));
                }

                if (IsReparsePoint(entry))
                {
                    if (entry is DirectoryInfo)
                    {
                        System.IO.Directory.Delete(entryFullPath, recursive: false);
                    }
                    else
                    {
                        File.Delete(entryFullPath);
                    }

                    continue;
                }

                if (entry is DirectoryInfo subDirectory)
                {
                    if (!DeleteDirectorySafelyNonWindows(subDirectory))
                    {
                        return false;
                    }
                }
                else
                {
                    ClearReadOnlyAttribute(entry);
                    File.Delete(entryFullPath);
                }
            }

            directory.Refresh();
            if (IsReparsePoint(directory))
            {
                _logger.LogWarning(
                    "Refusing to delete Terraform plan directory '{ReparsePath}' because it became a reparse point.",
                    directoryFullPath);
                return false;
            }

            ClearReadOnlyAttribute(directory);
            System.IO.Directory.Delete(directoryFullPath, recursive: false);
            return true;
        }

        private static void ValidateBareName(string name, string paramName)
        {
            if (string.IsNullOrWhiteSpace(name)
                || name.Contains(':', StringComparison.Ordinal)
                || name.Contains("..", StringComparison.Ordinal)
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || name != Path.GetFileName(name))
            {
                throw new ArgumentException(
                    "A Terraform plan path segment must be a bare single name.",
                    paramName);
            }
        }

        private static string NormalizePath(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        private static bool IsStrictlyBeneath(string candidateCanonicalPath, string canonicalRoot)
        {
            var prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
                ? canonicalRoot
                : canonicalRoot + Path.DirectorySeparatorChar;
            return candidateCanonicalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && candidateCanonicalPath.Length > prefix.Length;
        }

        private static bool IsDirectCanonicalChild(string childCanonicalPath, string parentCanonicalPath) =>
            string.Equals(
                Path.GetDirectoryName(childCanonicalPath),
                parentCanonicalPath,
                StringComparison.OrdinalIgnoreCase);

        private static void ClearReadOnlyAttribute(FileSystemInfo entry)
        {
            if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        private static bool IsReparsePoint(FileSystemInfo entry) =>
            (entry.Attributes & FileAttributes.ReparsePoint) != 0;

        private static bool IsExistingReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }

        private static bool IsExpectedFileSystemFailure(Exception exception)
        {
            return exception is IOException
                or UnauthorizedAccessException
                or SecurityException;
        }

        private static bool LastWriteWithin(DirectoryInfo directory, DateTime cutoff)
        {
            if (IsReparsePoint(directory))
            {
                return false;
            }

            if (directory.LastWriteTimeUtc >= cutoff)
            {
                return true;
            }

            return directory
                .EnumerateFileSystemInfos()
                .Where(entry => !IsReparsePoint(entry))
                .Any(entry => entry is DirectoryInfo subDirectory
                    ? LastWriteWithin(subDirectory, cutoff)
                    : entry.LastWriteTimeUtc >= cutoff);
        }
    }
}
