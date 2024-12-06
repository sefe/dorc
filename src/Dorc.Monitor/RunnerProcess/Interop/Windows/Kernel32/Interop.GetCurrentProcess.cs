﻿using System.Runtime.InteropServices;

namespace Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

internal partial class Interop
{
    internal partial class Kernel32
    {
        [DllImport(Windows.Interop.Libraries.Kernel32, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetCurrentProcess();
    }
}