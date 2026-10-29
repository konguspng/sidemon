using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace SidebarDiagnostics
{
    /// <summary>
    /// Boot-time hardening. When SideMon is launched right after Windows boots,
    /// antivirus scans, disk spin-up or a half-finished shell can make DLLs in the
    /// install folder briefly unreadable. WPF loads some of them lazily (Accessibility
    /// when the first popup opens, vcruntime140_cor3 when the process unloads), so a
    /// transient failure used to surface as a fatal crash. This loads and pins them
    /// up front, with retries, while the app is still starting.
    /// </summary>
    internal static class Resilience
    {
        private static readonly string[] ManagedAssemblies =
        {
            "Accessibility",
            "UIAutomationProvider",
            "UIAutomationTypes",
            "UIAutomationClient",
            "UIAutomationClientSideProviders",
        };

        private static readonly string[] NativeLibraries =
        {
            "vcruntime140_cor3.dll",
            "wpfgfx_cor3.dll",
            "PresentationNative_cor3.dll",
            "D3DCompiler_47_cor3.dll",
            "PenImc_cor3.dll",
        };

        // handles are kept for the life of the process so the loader never needs the files again
        private static readonly List<IntPtr> _pinned = new List<IntPtr>();
        private static readonly List<Assembly> _pinnedAssemblies = new List<Assembly>();

        /// <summary>Right after a boot, wait (bounded) for the shell and the install folder to be usable.</summary>
        public static void WaitForBootSettle()
        {
            try
            {
                // only relevant in the first minutes after Windows started
                if (Environment.TickCount64 > 4 * 60 * 1000)
                {
                    return;
                }

                string dir = AppContext.BaseDirectory;
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);

                while (DateTime.UtcNow < deadline)
                {
                    bool shell = Process.GetProcessesByName("explorer").Length > 0;
                    bool files = File.Exists(Path.Combine(dir, "vcruntime140_cor3.dll")) && File.Exists(Path.Combine(dir, "Accessibility.dll"));

                    if (shell && files)
                    {
                        break;
                    }

                    Thread.Sleep(500);
                }

                // short grace period for startup-time antivirus scans to finish with our files
                Thread.Sleep(2000);
            }
            catch { }
        }

        /// <summary>Load and pin the lazily-loaded runtime pieces, retrying while the disk settles.</summary>
        public static void PreloadRuntime()
        {
            string dir = AppContext.BaseDirectory;

            foreach (string name in NativeLibraries)
            {
                string path = Path.Combine(dir, name);

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        if (NativeLibrary.TryLoad(path, out IntPtr handle))
                        {
                            _pinned.Add(handle);
                            break;
                        }
                    }
                    catch { }

                    Thread.Sleep(500);
                }
            }

            foreach (string name in ManagedAssemblies)
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        _pinnedAssemblies.Add(Assembly.Load(name));
                        break;
                    }
                    catch (Exception ex) when (ex is FileNotFoundException || ex is FileLoadException || ex is IOException)
                    {
                        Thread.Sleep(500);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>True for failures to load a framework DLL, which must never take the whole app down.</summary>
        public static bool IsAssemblyLoadFailure(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is FileNotFoundException f && f.FileName != null &&
                    (f.FileName.StartsWith("Accessibility", StringComparison.OrdinalIgnoreCase) ||
                     f.FileName.StartsWith("UIAutomation", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// End the process without running the C++/CLI module uninitializers, which is
        /// what raised "Unable to load DLL vcruntime140_cor3.dll" on the way out.
        /// </summary>
        public static void FastExit(int code)
        {
            try
            {
                TerminateProcess(Process.GetCurrentProcess().Handle, (uint)code);
            }
            catch { }

            Environment.Exit(code);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    }
}
