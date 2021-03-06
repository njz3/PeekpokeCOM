﻿using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PeekpokeCOM.Utils
{
    /// <summary>
    /// Scan running processes for known executables.
    /// Start/kill processes;.
    /// </summary>
    public class ProcessAnalyzer
    {

        /// <summary>
        /// Search matching process name and title 
        /// </summary>
        /// <param name="namesAndTitle"></param>
        /// <returns></returns>
        public static List<Process> ScanProcessesForKnownNamesAndTitle(List<Tuple<string, string>> namesAndTitle)
        {
            List<Process> processes = new List<Process>();
            foreach (var name in namesAndTitle) {
                var allProcesses = Process.GetProcessesByName(name.Item1);
                foreach (Process proc in allProcesses) {
                    bool add = false;
                    var windowsTitle = name.Item2;
                    if (windowsTitle != null && windowsTitle != "") {
                        if (proc.MainWindowTitle.Contains(windowsTitle)) {
                            add = true;
                        }
                    } else {
                        add = true;
                    }
                    if (add)
                        processes.Add(proc);
                }
            }
            return processes;
        }

        /// <summary>
        /// Start a new process
        /// </summary>
        /// <param name="command"></param>
        /// <param name="args"></param>
        public static Process StartProcess(string command = "command.exe", string args = "")
        {
            Process process = new Process();
            // Configure the process using the StartInfo properties.
            process.StartInfo.FileName = command;
            process.StartInfo.Arguments = args;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            process.StartInfo.UseShellExecute = true;
            process.Start();
            return process;
        }

        public static void KillAllProcesses(string name = "command.exe")
        {
            try {
                foreach (Process proc in Process.GetProcessesByName(name)) {
                    proc.Kill();
                }
            } catch (Exception ex) {
                Logger.Log("KillAllProcesses failed with " + ex.Message, LogLevels.IMPORTANT);
            }
        }

    }


    /// <summary>
    /// Process manipulation for read/write access.
    /// /!\ write access needs admin privileges.
    /// Once found, this allow to read memory of process at known addresses
    /// to get information about which game/option is activated
    /// </summary>
    public class ProcessManipulation
    {
        #region Dll import
        /// <summary>
        /// See https://www.pinvoke.net/default.aspx/kernel32.OpenProcess
        /// </summary>
        [Flags]
        enum ProcessAccessFlags : uint
        {
            All = 0x001F0FFF,
            Terminate = 0x00000001,
            CreateThread = 0x00000002,
            VirtualMemoryOperation = 0x00000008,
            VirtualMemoryRead = 0x00000010,
            VirtualMemoryWrite = 0x00000020,
            DuplicateHandle = 0x00000040,
            CreateProcess = 0x000000080,
            SetQuota = 0x00000100,
            SetInformation = 0x00000200,
            QueryInformation = 0x00000400,
            QueryLimitedInformation = 0x00001000,
            Synchronize = 0x00100000
        }

        [Flags]
        public enum ProcessAccess : uint
        {
            PROCESS_WM_READ = (uint)ProcessAccessFlags.VirtualMemoryRead+ProcessAccessFlags.VirtualMemoryOperation,
            PROCESS_VM_READWRITE = (uint)ProcessAccessFlags.VirtualMemoryRead+ProcessAccessFlags.VirtualMemoryWrite+ProcessAccessFlags.VirtualMemoryOperation,
            PROCESS_VM_ALL = (uint)ProcessAccessFlags.All,
        }

        /// <summary>
        /// Name of the Windows Multimedia library that manage tick period
        /// </summary>
        const string LIBKERNEL32 = "kernel32.dll";

        /// <summary>
        /// https://www.pinvoke.net/default.aspx/kernel32.OpenProcess
        /// </summary>
        /// <param name="dwDesiredAccess"></param>
        /// <param name="bInheritHandle"></param>
        /// <param name="dwProcessId"></param>
        /// <returns></returns>
        [DllImport(LIBKERNEL32)]
        protected static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        /// <summary>
        /// http://www.pinvoke.net/default.aspx/kernel32.CloseHandle
        /// </summary>
        /// <param name="hObject"></param>
        /// <returns></returns>
        [DllImport(LIBKERNEL32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        protected static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// https://www.pinvoke.net/default.aspx/kernel32.ReadProcessMemory
        /// </summary>
        /// <param name="hProcess"></param>
        /// <param name="lpBaseAddress"></param>
        /// <param name="lpBuffer"></param>
        /// <param name="dwSize"></param>
        /// <param name="lpNumberOfBytesRead"></param>
        /// <returns></returns>
        [DllImport(LIBKERNEL32, SetLastError = true)]
        protected static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out, MarshalAs(UnmanagedType.AsAny)] object lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);
        /// <summary>
        /// http://www.pinvoke.net/default.aspx/kernel32/WriteProcessMemory.html
        /// </summary>
        /// <param name="hProcess"></param>
        /// <param name="lpBaseAddress"></param>
        /// <param name="lpBuffer"></param>
        /// <param name="dwSize"></param>
        /// <param name="lpNumberOfBytesWritten"></param>
        /// <returns></returns>
        [DllImport(LIBKERNEL32, SetLastError = true)]
        protected static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [MarshalAs(UnmanagedType.AsAny)] object lpBuffer, int dwSize, out IntPtr lpNumberOfBytesWritten);

        protected Process Process = null;
        protected IntPtr ProcessHandle;


        const Int64 INVALID_HANDLE_VALUE = -1;
        [Flags]
        private enum SnapshotFlags : uint
        {
            HeapList = 0x00000001,
            Process = 0x00000002,
            Thread = 0x00000004,
            Module = 0x00000008,
            Module32 = 0x00000010,
            Inherit = 0x80000000,
            All = 0x0000001F,
            NoHeaps = 0x40000000
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateToolhelp32Snapshot(SnapshotFlags dwFlags, IntPtr th32ProcessID);
        [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
        public struct MODULEENTRY32
        {
            internal uint dwSize;
            internal uint th32ModuleID;
            internal uint th32ProcessID;
            internal uint GlblcntUsage;
            internal uint ProccntUsage;
            internal IntPtr modBaseAddr;
            internal uint modBaseSize;
            internal IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            internal string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            internal string szExePath;
        }

        [DllImport("kernel32.dll")]
        static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

        [DllImport("kernel32.dll")]
        static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);
        [DllImport("kernel32.dll")]
        internal static extern bool IsWow64Process(SafeProcessHandle processHandle, out bool isWow64Process);
        #endregion

        #region Public API

        public IntPtr BaseAddress { get; protected set; }

        /// <summary>
        /// See https://codingvision.net/security/c-read-write-another-process-memory
        /// </summary>
        public bool OpenProcess(ProcessAccess mode, Process process)
        {
            // Open first process with given access mode
            Process = process;
            ProcessHandle = OpenProcess((uint)mode, false, this.Process.Id);
            if (ProcessHandle == null)
                return false;
            var stt = GetModuleBaseAddress(out var baseAddress, "");
            BaseAddress = baseAddress;
            return stt;
        }

        public bool OpenProcess(ProcessAccess mode, string name = "", string title = null)
        {
            var nametitle = new Tuple<string, string>(name, title);
            var list = new List<Tuple<string, string>>();
            list.Add(nametitle);
            var found = ProcessAnalyzer.ScanProcessesForKnownNamesAndTitle(list);
            if (found.Count == 0)
                return false;
            return OpenProcess(mode, found[0]);
        }

        public bool OpenProcessForRead(string name = "", string title = null)
        {
            return OpenProcess(ProcessAccess.PROCESS_WM_READ, name, title);
        }


        public bool OpenProcessForReadWrite(string name = "", string title = null)
        {
            return OpenProcess(ProcessAccess.PROCESS_VM_READWRITE, name, title);
        }

        public void CloseProcess()
        {
            CloseHandle(this.ProcessHandle);
        }


        // Generic variants
        public bool Wpm<T>(IntPtr lpBaseAddress, T value) where T : struct
        {
            var buffer = new T[Marshal.SizeOf<T>()];
            buffer[0] = value;
            return WriteProcessMemory(ProcessHandle, lpBaseAddress, buffer, Marshal.SizeOf<T>(), out var bytesread);
        }
        public bool Rpm<T>(IntPtr lpBaseAddress, out T value) where T : struct
        {
            T[] buffer = new T[Marshal.SizeOf<T>()];
            var stt = ReadProcessMemory(ProcessHandle, lpBaseAddress, buffer, Marshal.SizeOf<T>(), out var bytesread);
            value = buffer[0]; // [0] would be faster, but First() is safer. E.g. of buffer[0] ?? default(T)
            return stt;
        }

        // Single type variants
        public bool Read(ulong address, byte[] buffer, int length)
        {
            var stt = ReadProcessMemory(ProcessHandle, (IntPtr)address, buffer, buffer.Length, out var bytesRead);
            Console.WriteLine(Encoding.Unicode.GetString(buffer) + " (" + ((ulong)bytesRead).ToString() + "bytes)");
            return stt;
        }
        public bool Write(ulong address, byte[] buffer, int length)
        {
            var stt = WriteProcessMemory(ProcessHandle, (IntPtr)address, buffer, buffer.Length, out var bytesWritten);
            Console.WriteLine(Encoding.Unicode.GetString(buffer) + " (" + ((ulong)bytesWritten).ToString() + "bytes)");
            return stt;
        }
        public bool ReadByte(ulong address, out byte value)
        {
            return Rpm<byte>((IntPtr)address, out value);
        }
        public bool WriteByte(ulong address, byte value)
        {
            return Wpm<byte>((IntPtr)address, value);
        }
        public bool ReadUInt32(ulong address, out UInt32 value)
        {
            return Rpm<UInt32>((IntPtr)address, out value);
        }
        public bool WriteUInt32(ulong address, UInt32 value)
        {
            return Wpm<UInt32>((IntPtr)address, value);
        }
        public bool ReadUInt64(ulong address, out UInt64 value)
        {
            return Rpm<UInt64>((IntPtr)address, out value);
        }
        public bool WriteUInt65(ulong address, UInt64 value)
        {
            return Wpm<UInt64>((IntPtr)address, value);
        }
        #endregion

        public bool GetModuleBaseAddress(out IntPtr baseAddress, string moduleName = "")
        {
            return GetModuleBaseAddress(this.Process, out baseAddress, moduleName);
        }

        public bool GetListOfModules(List<string> moduleNames)
        {
            moduleNames.Clear();
            IntPtr hSnap = CreateToolhelp32Snapshot(SnapshotFlags.Module | SnapshotFlags.Module32, (IntPtr)Process.Id);
            if (hSnap.ToInt64() != INVALID_HANDLE_VALUE) {
                MODULEENTRY32 modEntry = new MODULEENTRY32();
                modEntry.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32));
                if (Module32First(hSnap, ref modEntry)) {
                    moduleNames.Add(modEntry.szModule);
                    while (Module32Next(hSnap, ref modEntry)) {
                        moduleNames.Add(modEntry.szModule);
                    }
                }
            }
            CloseHandle(hSnap);
            return true;
        }
        static public bool GetModuleBaseAddress(Process process, out IntPtr modBaseAddr, string moduleName = "")
        {
            modBaseAddr = IntPtr.Zero;
            bool found = false;
            IntPtr hSnap = CreateToolhelp32Snapshot(SnapshotFlags.Module | SnapshotFlags.Module32, (IntPtr)process.Id);
            if (hSnap.ToInt64() != INVALID_HANDLE_VALUE) {
                MODULEENTRY32 modEntry = new MODULEENTRY32();
                modEntry.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32));
                if (Module32First(hSnap, ref modEntry)) {
                    modBaseAddr = modEntry.modBaseAddr;
                    if (moduleName=="") {
                        found = true;
                    } else {
                        do {
                            if (modEntry.szModule.Equals(moduleName)) {
                                modBaseAddr = modEntry.modBaseAddr;
                                found = true;
                                break;
                            }
                        } while (Module32Next(hSnap, ref modEntry));
                    }
                }
            }
            CloseHandle(hSnap);
            return found;
        }

        public bool FindDmaAddress(IntPtr baseAddress, List<int> listOfPointerOffsets, out IntPtr pointer)
        {
            pointer = baseAddress;
            // Get the id of the process
            var processId = this.Process.Id;
            // Determine if the process is running under Wow64 (x86)
            IsWow64Process(this.Process.SafeHandle, out var isWow64);
            foreach (var offset in listOfPointerOffsets) {
                // If the process is x86
                if (isWow64) {
                    // Read the next address (next multi level pointer) from the current address
                    if (!ReadUInt32((ulong)pointer, out var val))
                        return false;
                    pointer = (IntPtr)(val);
                }
                // If the process is x64
                else {
                    // Read the next address (next multi level pointer) from the current address
                    if (!ReadUInt64((ulong)pointer, out var val))
                        return false;
                    pointer = (IntPtr)(val);
                }

                // Add the next offset onto the address
                pointer += offset;
            }
            return true;
        }
    }



    public class OSUtilities
    {
        public static Version AssemblyVersion()
        {
            var assembly = typeof(PeekpokeCOM.Program).Assembly;
            var version = assembly.GetName().Version;
            return version;
        }

        public static string AssemblyCopyright()
        {
            AssemblyCopyrightAttribute copyright =
                Assembly.GetExecutingAssembly().
                GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false)[0]
                as AssemblyCopyrightAttribute;
            return copyright.Copyright;
        }

    }
}
