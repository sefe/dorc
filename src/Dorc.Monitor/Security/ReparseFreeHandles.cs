using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.Monitor.Security
{
    /// <summary>
    /// Immutable kernel volume + file identifier pair (<c>FILE_ID_INFO</c> /
    /// <c>BY_HANDLE_FILE_INFORMATION</c>) used to pin directory handles across operations.
    /// </summary>
    internal readonly struct FileIdentity : IEquatable<FileIdentity>
    {
        public readonly ulong VolumeSerialNumber;
        public readonly ulong FileIdLow;
        public readonly ulong FileIdHigh;

        public FileIdentity(ulong volumeSerialNumber, ulong fileIdLow, ulong fileIdHigh)
        {
            VolumeSerialNumber = volumeSerialNumber;
            FileIdLow = fileIdLow;
            FileIdHigh = fileIdHigh;
        }

        public bool Equals(FileIdentity other) =>
            VolumeSerialNumber == other.VolumeSerialNumber
            && FileIdLow == other.FileIdLow
            && FileIdHigh == other.FileIdHigh;

        public override bool Equals(object? obj) => obj is FileIdentity other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(VolumeSerialNumber, FileIdLow, FileIdHigh);

        public static bool operator ==(FileIdentity left, FileIdentity right) => left.Equals(right);

        public static bool operator !=(FileIdentity left, FileIdentity right) => !left.Equals(right);

        public override string ToString() =>
            $"0x{VolumeSerialNumber:X16}:0x{FileIdHigh:X16}{FileIdLow:X16}";
    }

    /// <summary>
    /// Windows handle-pinned file and directory operations that refuse to traverse reparse points
    /// and verify canonical kernel paths before reading, writing, or mutating access control lists.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class ReparseFreeHandles
    {
        internal const uint GenericRead = 0x80000000u;
        internal const uint GenericWrite = 0x40000000u;
        internal const uint Delete = 0x00010000u;
        internal const uint FileListDirectory = 0x00000001u;
        internal const uint FileReadAttributes = 0x00000080u;
        internal const uint FileWriteAttributes = 0x00000100u;
        internal const uint ReadControl = 0x00020000u;
        internal const uint WriteDac = 0x00040000u;

        internal const uint FileShareRead = 0x00000001u;
        internal const uint FileShareWrite = 0x00000002u;
        internal const uint FileShareDelete = 0x00000004u;

        internal const uint CreateAlways = 2u;
        internal const uint OpenExisting = 3u;
        internal const uint OpenAlways = 4u;

        private const uint FileFlagBackupSemantics = 0x02000000u;
        private const uint FileFlagOpenReparsePoint = 0x00200000u;
        private const uint VolumeNameDos = 0x0u;

        private const uint SeFileObject = 1u;
        private const uint OwnerSecurityInformation = 0x00000001u;
        private const uint DaclSecurityInformation = 0x00000004u;
        private const uint ProtectedDaclSecurityInformation = 0x80000000u;

        internal const int ErrorInvalidFunction = 1;
        internal const int ErrorFileNotFound = 2;
        internal const int ErrorPathNotFound = 3;
        internal const int ErrorAccessDenied = 5;
        internal const int ErrorNotSupported = 50;
        internal const int ErrorInvalidParameter = 87;
        private const int ErrorInsufficientBuffer = 122;
        internal const int ErrorAlreadyExists = 183;

        private const int FileBasicInfoClass = 0;
        private const int FileDispositionInfoClass = 4;
        private const int FileIdInfoClass = 18;
        private const int FileDispositionInfoExClass = 21;

        private const uint FileDispositionDelete = 0x00000001u;
        private const uint FileDispositionPosixSemantics = 0x00000002u;
        private const uint FileDispositionIgnoreReadOnlyAttribute = 0x00000010u;

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public uint nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileBasicInfo
        {
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public long ChangeTime;
            public uint FileAttributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileDispositionInfo
        {
            public byte DeleteFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileDispositionInfoEx
        {
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FiletimeStruct
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public FiletimeStruct CreationTime;
            public FiletimeStruct LastAccessTime;
            public FiletimeStruct LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectoryW(
            string lpPathName,
            ref SecurityAttributes lpSecurityAttributes);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle hFile,
            [Out] char[] lpszFilePath,
            uint cchFilePath,
            uint dwFlags);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile,
            out ByHandleFileInformation lpFileInformation);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle hFile,
            int fileInformationClass,
            out FileBasicInfo lpFileInformation,
            uint dwBufferSize);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle hFile,
            int fileInformationClass,
            out FileIdentity lpFileInformation,
            uint dwBufferSize);

        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle hFile,
            int fileInformationClass,
            ref FileBasicInfo lpFileInformation,
            uint dwBufferSize);

        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle hFile,
            int fileInformationClass,
            ref FileDispositionInfo lpFileInformation,
            uint dwBufferSize);

        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle hFile,
            int fileInformationClass,
            ref FileDispositionInfoEx lpFileInformation,
            uint dwBufferSize);

        [DllImport("kernel32.dll", EntryPoint = "LocalFree", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        [DllImport("advapi32.dll", EntryPoint = "GetSecurityInfo", ExactSpelling = true, SetLastError = false)]
        private static extern uint GetSecurityInfo(
            SafeFileHandle handle,
            uint objectType,
            uint securityInfo,
            out IntPtr ppsidOwner,
            IntPtr ppsidGroup,
            IntPtr ppDacl,
            IntPtr ppSacl,
            out IntPtr ppSecurityDescriptor);

        [DllImport("advapi32.dll", EntryPoint = "GetSecurityDescriptorDacl", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSecurityDescriptorDacl(
            IntPtr pSecurityDescriptor,
            [MarshalAs(UnmanagedType.Bool)] out bool lpbDaclPresent,
            out IntPtr pDacl,
            [MarshalAs(UnmanagedType.Bool)] out bool lpbDaclDefaulted);

        [DllImport("advapi32.dll", EntryPoint = "SetSecurityInfo", ExactSpelling = true, SetLastError = false)]
        private static extern uint SetSecurityInfo(
            SafeFileHandle handle,
            uint objectType,
            uint securityInfo,
            IntPtr psidOwner,
            IntPtr psidGroup,
            IntPtr pDacl,
            IntPtr pSacl);

        [DllImport("advapi32.dll", EntryPoint = "GetKernelObjectSecurity", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetKernelObjectSecurity(
            SafeFileHandle handle,
            uint requestedInformation,
            [Out] byte[]? pSecurityDescriptor,
            uint nLength,
            out uint lpnLengthNeeded);

        [DllImport("advapi32.dll", EntryPoint = "SetKernelObjectSecurity", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetKernelObjectSecurity(
            SafeFileHandle handle,
            uint securityInformation,
            [In] byte[] pSecurityDescriptor);

        public static string CreateNewDirectoryWithSecurity(string path, DirectorySecurity security) =>
            CreateNewDirectoryWithSecurity(path, security, out _);

        public static string CreateNewDirectoryWithSecurity(
            string path,
            DirectorySecurity security,
            out FileIdentity directoryIdentity)
        {
            var canonicalPath = CreateNewDirectoryWithSecurity(
                path,
                security,
                RestrictedDirectory.PrivilegedIdentities().ToArray(),
                out directoryIdentity,
                out var pinnedDirectoryHandle);
            pinnedDirectoryHandle.Dispose();
            return canonicalPath;
        }

        public static string CreateNewDirectoryWithSecurity(
            string path,
            DirectorySecurity security,
            out FileIdentity directoryIdentity,
            out SafeFileHandle pinnedDirectoryHandle) =>
            CreateNewDirectoryWithSecurity(
                path,
                security,
                RestrictedDirectory.PrivilegedIdentities().ToArray(),
                out directoryIdentity,
                out pinnedDirectoryHandle);

        public static string CreateNewDirectoryWithSecurity(
            string path,
            DirectorySecurity security,
            IReadOnlyCollection<SecurityIdentifier> privilegedOwners,
            out FileIdentity directoryIdentity,
            out SafeFileHandle pinnedDirectoryHandle)
        {
            var descriptorBytes = security.GetSecurityDescriptorBinaryForm();
            var pinnedDescriptor = GCHandle.Alloc(descriptorBytes, GCHandleType.Pinned);
            try
            {
                var attributes = new SecurityAttributes
                {
                    nLength = (uint)Marshal.SizeOf<SecurityAttributes>(),
                    lpSecurityDescriptor = pinnedDescriptor.AddrOfPinnedObject(),
                    bInheritHandle = 0
                };

                if (!CreateDirectoryW(path, ref attributes))
                {
                    var error = Marshal.GetLastPInvokeError();
                    throw new IOException(
                        $"Cannot create new restricted directory '{path}' (Win32 error {error}).",
                        new Win32Exception(error));
                }
            }
            finally
            {
                pinnedDescriptor.Free();
            }

            pinnedDirectoryHandle = OpenPinnedDirectoryHandle(
                path,
                FileListDirectory | FileReadAttributes | ReadControl | WriteDac);
            try
            {
                var currentOwner = GetOwnerSid(pinnedDirectoryHandle, path);
                if (!privilegedOwners.Contains(currentOwner))
                {
                    throw new IOException(
                        $"Refusing to use directory '{path}' because its NTFS owner '{currentOwner}' is not a privileged account.");
                }

                ApplyProtectedDacl(pinnedDirectoryHandle, security, path);
                directoryIdentity = GetFileIdentity(pinnedDirectoryHandle, path);
                return GetCanonicalPath(pinnedDirectoryHandle, path);
            }
            catch
            {
                pinnedDirectoryHandle.Dispose();
                throw;
            }
        }

        public static SafeFileHandle OpenRawHandle(
            string path,
            uint desiredAccess,
            uint shareMode,
            uint creationDisposition,
            out int win32Error)
        {
            var handle = CreateFileW(
                path,
                desiredAccess,
                shareMode,
                IntPtr.Zero,
                creationDisposition,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);
            win32Error = handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
            return handle;
        }

        public static SafeFileHandle OpenVerifiedDirectoryHandle(
            string path,
            uint desiredAccess = FileReadAttributes | ReadControl | WriteDac) =>
            OpenVerifiedDirectoryHandleWithShareMode(
                path,
                desiredAccess,
                FileShareRead | FileShareWrite | FileShareDelete);

        /// <summary>
        /// Opens a verified non-reparse directory handle with <c>FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES</c>
        /// and <c>FILE_SHARE_READ | FILE_SHARE_WRITE</c> (deliberately omitting <c>FILE_SHARE_DELETE</c>).
        /// Including <c>FILE_LIST_DIRECTORY</c> (<c>FILE_READ_DATA</c>) causes <c>IoSetShareAccess</c>
        /// to register the share mask on the NTFS FCB, so the kernel rejects any concurrent rename
        /// (<c>FileRenameInformation</c>) or <c>DELETE</c> open on the directory or its ancestors
        /// with <c>STATUS_SHARING_VIOLATION</c> while the handle remains open.
        /// </summary>
        public static SafeFileHandle OpenPinnedDirectoryHandle(
            string path,
            uint desiredAccess = FileListDirectory | FileReadAttributes) =>
            OpenVerifiedDirectoryHandleWithShareMode(
                path,
                desiredAccess,
                FileShareRead | FileShareWrite);

        private static SafeFileHandle OpenVerifiedDirectoryHandleWithShareMode(
            string path,
            uint desiredAccess,
            uint shareMode)
        {
            var handle = OpenRawHandle(
                path,
                desiredAccess,
                shareMode,
                OpenExisting,
                out var error);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw CreateWin32IoException($"Failed to open directory '{path}'", error);
            }

            try
            {
                var attributes = File.GetAttributes(handle);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Refusing to operate on reparse point '{path}'.");
                }

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    throw new IOException($"Expected a directory at '{path}', but found a file.");
                }

                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public static SafeFileHandle OpenVerifiedRegularFileForDacl(string filePath, string expectedCanonicalFilePath)
        {
            var handle = OpenRawHandle(
                filePath,
                FileReadAttributes | ReadControl | WriteDac,
                FileShareRead | FileShareWrite | FileShareDelete,
                OpenExisting,
                out var error);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw CreateWin32IoException($"Failed to open file '{filePath}' for ACL update", error);
            }

            try
            {
                VerifyRegularFileHandle(handle, filePath, expectedCanonicalFilePath);
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public static FileStream? OpenVerifiedRegularFileForRead(
            string filePath,
            string expectedCanonicalFilePath,
            string expectedCanonicalDirectory,
            FileIdentity expectedDirectoryIdentity,
            out bool notFound)
        {
            var handle = OpenRawHandle(
                filePath,
                GenericRead | FileReadAttributes,
                FileShareRead,
                OpenExisting,
                out var error);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                if (error == ErrorFileNotFound)
                {
                    // Confirm the containing directory is still canonical, non-reparse, and
                    // matches its pinned NTFS file ID before reporting a missing optional file.
                    var parentDirectory = Path.GetDirectoryName(filePath)
                        ?? throw new IOException($"Invalid artefact path '{filePath}'.");
                    using var dirHandle = OpenVerifiedDirectoryHandle(parentDirectory, FileReadAttributes);
                    VerifyFileIdentity(dirHandle, expectedDirectoryIdentity, parentDirectory);
                    var canonicalParent = GetCanonicalPath(dirHandle, parentDirectory);
                    if (!string.Equals(canonicalParent, expectedCanonicalDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException(
                            $"Refusing to inspect '{filePath}' because parent directory resolved to '{canonicalParent}' instead of '{expectedCanonicalDirectory}'.");
                    }

                    notFound = true;
                    return null;
                }

                throw CreateWin32IoException($"Failed to open Terraform artefact '{filePath}'", error);
            }

            try
            {
                VerifyRegularFileHandle(handle, filePath, expectedCanonicalFilePath);
                notFound = false;
                return new FileStream(handle, FileAccess.Read, bufferSize: 81920, isAsync: false);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public static FileStream OpenVerifiedRegularFileForWrite(
            string filePath,
            string expectedCanonicalFilePath,
            bool overwriteExisting)
        {
            // Never pass CreateAlways to CreateFileW: CREATE_ALWAYS truncates an existing file
            // inside the kernel BEFORE handle verification can inspect reparse attributes or
            // NTFS hard-link counts. Open via OpenAlways first, verify the handle, and truncate
            // only after single-link regular-file verification succeeds.
            var handle = OpenRawHandle(
                filePath,
                GenericWrite | FileReadAttributes,
                FileShareRead,
                OpenAlways,
                out var error);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw CreateWin32IoException($"Failed to open '{filePath}' for verified write", error);
            }

            try
            {
                VerifyRegularFileHandle(handle, filePath, expectedCanonicalFilePath);
                if (overwriteExisting)
                {
                    RandomAccess.SetLength(handle, 0);
                }

                return new FileStream(handle, FileAccess.Write, bufferSize: 81920, isAsync: false);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public static void WriteVerifiedEmptyFile(string filePath, string expectedCanonicalFilePath)
        {
            // OpenAlways + FileFlagOpenReparsePoint opens an existing link or creates a new file
            // without truncating any reparse target before handle verification runs.
            using var stream = OpenVerifiedRegularFileForWrite(
                filePath,
                expectedCanonicalFilePath,
                overwriteExisting: false);
            stream.SetLength(0);
            stream.Flush(flushToDisk: true);
        }

        public static string GetCanonicalPath(SafeFileHandle handle, string originalPath)
        {
            var buffer = new char[512];
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, VolumeNameDos);
            if (length == 0)
            {
                throw CreateWin32IoException(
                    $"Failed to resolve canonical path for '{originalPath}'",
                    Marshal.GetLastPInvokeError());
            }

            if (length >= buffer.Length)
            {
                buffer = new char[length + 1];
                length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, VolumeNameDos);
                if (length == 0 || length >= buffer.Length)
                {
                    throw CreateWin32IoException(
                        $"Failed to resolve canonical path for '{originalPath}'",
                        Marshal.GetLastPInvokeError());
                }
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

        public static void ApplyProtectedDacl(SafeFileHandle handle, ObjectSecurity security, string path)
        {
            var descriptorBytes = security.GetSecurityDescriptorBinaryForm();
            if (descriptorBytes.Length < 4)
            {
                throw new IOException($"Security descriptor for '{path}' was truncated.");
            }

            var pinnedDescriptor = GCHandle.Alloc(descriptorBytes, GCHandleType.Pinned);
            try
            {
                if (!GetSecurityDescriptorDacl(
                        pinnedDescriptor.AddrOfPinnedObject(),
                        out var daclPresent,
                        out _,
                        out _)
                    || !daclPresent)
                {
                    throw new IOException($"Security descriptor for '{path}' did not contain a DACL.");
                }
            }
            finally
            {
                pinnedDescriptor.Free();
            }

            // Enforce SE_DACL_PROTECTED (0x1000 in the little-endian Control word at bytes 2..3)
            // and apply directly to the kernel object via SetKernelObjectSecurity (NtSetSecurityObject)
            // rather than advapi32!SetSecurityInfo. SetSecurityInfo walks directory children in
            // user mode and auto-propagates inheritable ACEs onto unverified hard-linked children
            // before per-handle link-count verification can run; SetKernelObjectSecurity updates
            // ONLY this handle's security descriptor.
            descriptorBytes[3] |= 0x10;
            if (!SetKernelObjectSecurity(handle, DaclSecurityInformation, descriptorBytes))
            {
                throw CreateWin32IoException(
                    $"Failed to apply restricted DACL to '{path}'",
                    Marshal.GetLastPInvokeError());
            }
        }

        public static FileIdentity GetFileIdentity(SafeFileHandle handle, string path)
        {
            var size = (uint)Marshal.SizeOf<FileIdentity>();
            if (GetFileInformationByHandleEx(handle, FileIdInfoClass, out FileIdentity identity, size))
            {
                return identity;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error is ErrorInvalidParameter or ErrorNotSupported or ErrorInvalidFunction
                && GetFileInformationByHandle(handle, out var byHandleInfo))
            {
                var fileIndex = ((ulong)byHandleInfo.FileIndexHigh << 32) | byHandleInfo.FileIndexLow;
                return new FileIdentity(byHandleInfo.VolumeSerialNumber, fileIndex, 0UL);
            }

            throw CreateWin32IoException($"Failed to read NTFS file identifier for '{path}'", error);
        }

        public static void VerifyFileIdentity(SafeFileHandle handle, FileIdentity expectedIdentity, string path)
        {
            var actualIdentity = GetFileIdentity(handle, path);
            if (actualIdentity != expectedIdentity)
            {
                throw new IOException(
                    $"Refusing to operate on '{path}' because its NTFS file ID changed from {expectedIdentity} to {actualIdentity}.");
            }
        }

        public static DirectorySecurity GetDirectorySecurity(SafeFileHandle handle, string path)
        {
            if (!GetKernelObjectSecurity(
                    handle,
                    DaclSecurityInformation,
                    null,
                    0u,
                    out var lengthNeeded))
            {
                var probeError = Marshal.GetLastPInvokeError();
                if (probeError != ErrorInsufficientBuffer || lengthNeeded == 0)
                {
                    throw CreateWin32IoException($"Failed to query DACL size for '{path}'", probeError);
                }
            }

            var rawDescriptor = new byte[lengthNeeded];
            if (!GetKernelObjectSecurity(
                    handle,
                    DaclSecurityInformation,
                    rawDescriptor,
                    lengthNeeded,
                    out _))
            {
                throw CreateWin32IoException(
                    $"Failed to read DACL for '{path}'",
                    Marshal.GetLastPInvokeError());
            }

            var security = new DirectorySecurity();
            security.SetSecurityDescriptorBinaryForm(rawDescriptor, AccessControlSections.Access);
            return security;
        }

        public static SecurityIdentifier GetOwnerSid(SafeFileHandle handle, string path)
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
                throw CreateWin32IoException($"Failed to inspect owner of '{path}'", (int)result);
            }

            try
            {
                if (ownerSidPtr == IntPtr.Zero)
                {
                    throw new IOException($"Security descriptor for '{path}' did not contain an owner SID.");
                }

                return new SecurityIdentifier(ownerSidPtr);
            }
            finally
            {
                if (securityDescriptorPtr != IntPtr.Zero)
                {
                    LocalFree(securityDescriptorPtr);
                }
            }
        }

        public static void ClearReadOnlyByHandle(SafeFileHandle handle)
        {
            var size = (uint)Marshal.SizeOf<FileBasicInfo>();
            if (!GetFileInformationByHandleEx(handle, FileBasicInfoClass, out FileBasicInfo info, size))
            {
                return;
            }

            if ((info.FileAttributes & (uint)FileAttributes.ReadOnly) == 0)
            {
                return;
            }

            const uint structuralFlags = (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint);
            var mutableAttributes = info.FileAttributes & ~structuralFlags & ~(uint)FileAttributes.ReadOnly;
            var update = new FileBasicInfo
            {
                FileAttributes = mutableAttributes == 0 ? (uint)FileAttributes.Normal : mutableAttributes
            };

            SetFileInformationByHandle(handle, FileBasicInfoClass, ref update, size);
        }

        public static void DeleteByHandle(SafeFileHandle handle, string path)
        {
            // Never mutate attributes on a multiply-linked file handle: clearing ReadOnly on an
            // NTFS hard link modifies the shared inode of the external target file, whereas
            // unlinking the directory entry only removes the link inside the confined directory.
            if (!GetFileInformationByHandle(handle, out var byHandleInfo)
                || (byHandleInfo.FileAttributes & (uint)FileAttributes.Directory) != 0
                || byHandleInfo.NumberOfLinks <= 1)
            {
                ClearReadOnlyByHandle(handle);
            }

            var infoExSize = (uint)Marshal.SizeOf<FileDispositionInfoEx>();
            var infoEx = new FileDispositionInfoEx
            {
                Flags = FileDispositionDelete
                    | FileDispositionPosixSemantics
                    | FileDispositionIgnoreReadOnlyAttribute
            };

            if (SetFileInformationByHandle(handle, FileDispositionInfoExClass, ref infoEx, infoExSize))
            {
                return;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error is ErrorInvalidParameter or ErrorNotSupported or ErrorInvalidFunction)
            {
                infoEx.Flags = FileDispositionDelete | FileDispositionPosixSemantics;
                if (SetFileInformationByHandle(handle, FileDispositionInfoExClass, ref infoEx, infoExSize))
                {
                    return;
                }

                error = Marshal.GetLastPInvokeError();
                if (error is ErrorInvalidParameter or ErrorNotSupported or ErrorInvalidFunction)
                {
                    var info = new FileDispositionInfo { DeleteFile = 1 };
                    var infoSize = (uint)Marshal.SizeOf<FileDispositionInfo>();
                    if (SetFileInformationByHandle(handle, FileDispositionInfoClass, ref info, infoSize))
                    {
                        return;
                    }

                    error = Marshal.GetLastPInvokeError();
                }
            }

            throw CreateWin32IoException($"Failed to mark '{path}' for handle-based deletion", error);
        }

        public static uint GetLinkCount(SafeFileHandle handle, string path)
        {
            if (!GetFileInformationByHandle(handle, out var info))
            {
                throw CreateWin32IoException(
                    $"Failed to query NTFS link count for '{path}'",
                    Marshal.GetLastPInvokeError());
            }

            return info.NumberOfLinks;
        }

        public static void VerifySingleLinkRegularFile(SafeFileHandle handle, string path)
        {
            var linkCount = GetLinkCount(handle, path);
            if (linkCount != 1)
            {
                throw new IOException(
                    $"Refusing to access '{path}' because it has {linkCount} NTFS links (expected 1).");
            }
        }

        public static IOException CreateWin32IoException(string message, int win32Error) =>
            new($"{message} (Win32 error {win32Error}).", new Win32Exception(win32Error));

        private static void VerifyRegularFileHandle(
            SafeFileHandle handle,
            string originalPath,
            string expectedCanonicalFilePath)
        {
            var attributes = File.GetAttributes(handle);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Refusing to access '{originalPath}' because it is a reparse point.");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new IOException($"Refusing to access '{originalPath}' as a file because it is a directory.");
            }

            VerifySingleLinkRegularFile(handle, originalPath);

            var canonicalPath = GetCanonicalPath(handle, originalPath);
            if (!string.Equals(canonicalPath, expectedCanonicalFilePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Refusing to access '{originalPath}' because its canonical path '{canonicalPath}' escaped '{expectedCanonicalFilePath}'.");
            }
        }
    }
}
