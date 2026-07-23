using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SidebarDiagnostics.Overlay
{
    // Reads RTSS (RivaTuner Statistics Server)'s public shared-memory stats API
    // ("RTSSSharedMemoryV2") instead of hooking DirectX/Vulkan/OpenGL ourselves -
    // real per-game FPS capture requires injecting into every graphics API's
    // present call, which is exactly what RTSS already does. If RTSS isn't
    // installed/running, every call here just reports "not detected" rather
    // than throwing, the same defensive pattern used by PawnIO/UpdateCheck.
    [SupportedOSPlatform("windows")]
    public static class RTSSReader
    {
        private const string MAPNAME = "RTSSSharedMemoryV2";

        [StructLayout(LayoutKind.Sequential)]
        private struct RTSS_SHARED_MEMORY_HEADER
        {
            public uint dwSignature;
            public uint dwVersion;
            public uint dwAppEntrySize;
            public uint dwAppArrOffset;
            public uint dwAppArrSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct RTSS_SHARED_MEMORY_APP_ENTRY
        {
            public uint dwProcessID;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)]
            public byte[] szName;

            public uint dwFlags;
            public uint dwTime0;
            public uint dwTime1;
            public uint dwFrames;
            public uint dwFrameTime;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static bool IsAvailable
        {
            get
            {
                try
                {
                    using (MemoryMappedFile.OpenExisting(MAPNAME, MemoryMappedFileRights.Read)) { }
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        // Returns null if RTSS isn't running, or if the foreground process
        // currently has no frame data (e.g. it just launched, or isn't a
        // graphics application RTSS is tracking).
        public static double? GetForegroundFps()
        {
            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint _processID);

                if (_processID == 0)
                {
                    return null;
                }

                using (MemoryMappedFile _map = MemoryMappedFile.OpenExisting(MAPNAME, MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    RTSS_SHARED_MEMORY_HEADER _header;
                    _view.Read(0, out _header);

                    int _entrySize = Marshal.SizeOf<RTSS_SHARED_MEMORY_APP_ENTRY>();

                    for (int i = 0; i < _header.dwAppArrSize; i++)
                    {
                        long _offset = _header.dwAppArrOffset + (long)i * _header.dwAppEntrySize;

                        uint _entryProcessID;
                        _view.Read(_offset, out _entryProcessID);

                        if (_entryProcessID != _processID)
                        {
                            continue;
                        }

                        uint _frameTime;
                        // dwFrameTime sits after dwProcessID (4) + szName (260) + dwFlags (4) + dwTime0 (4) + dwTime1 (4) + dwFrames (4)
                        _view.Read(_offset + 4 + 260 + 4 + 4 + 4 + 4, out _frameTime);

                        if (_frameTime == 0)
                        {
                            return null;
                        }

                        return 1_000_000.0 / _frameTime;
                    }
                }
            }
            catch (Exception)
            {
                // RTSS not running, shared memory layout mismatch, or the
                // foreground window couldn't be resolved - degrade silently
            }

            return null;
        }
    }
}
