using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using log4net;
using Microsoft.Win32.SafeHandles;

namespace SuperPutty.Utils
{
    /// <summary>
    /// A console program (plink) running in a Windows pseudo console (ConPTY, Windows 10 1809 and later), so its output
    /// arrives as a VT byte stream and typing goes back the same way, with no console window. Because the program sees a
    /// real console, plink's password, passphrase and host-key prompts work, and <see cref="Resize"/> makes it tell the
    /// server the new window size. Used by Gui.TerminalControl; proven against a Cisco switch by docs/spikes/ConPtyPlink.
    /// </summary>
    public sealed class ConPty : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ConPty));

        /// <summary>The pseudo console handle; zero once closed</summary>
        private IntPtr console;

        /// <summary>Where typing is written</summary>
        private FileStream input;

        /// <summary>Where the program's output is read; read on <see cref="reader"/></summary>
        private FileStream output;

        /// <summary>The program's process and thread handles</summary>
        private PROCESS_INFORMATION process;

        /// <summary>Reads <see cref="output"/> and raises <see cref="Output"/></summary>
        private Thread reader;

        /// <summary>The thread pool's wait for the process to end; unregistered when disposed</summary>
        private RegisteredWaitHandle exitWait;

        /// <summary>Set once <see cref="Exited"/> has been raised, so it's raised once</summary>
        private int exitRaised;

        /// <summary>Raised on a background thread with each piece of output, in order</summary>
        public event Action<byte[]> Output;

        /// <summary>Raised once on a background thread when the program has ended, with its exit code</summary>
        public event Action<int> Exited;

        /// <summary>
        /// Start a program in a new pseudo console
        /// </summary>
        /// <param name="commandLine">The full command line, executable quoted when it has spaces</param>
        /// <param name="columns">The console's width in characters</param>
        /// <param name="rows">The console's height in lines</param>
        /// <exception cref="Win32Exception">The pseudo console or the process couldn't be created</exception>
        public void Start(string commandLine, int columns, int rows)
        {
            SafeFileHandle inputRead, inputWrite, outputRead, outputWrite;
            if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0))
                throw new Win32Exception();
            if (!CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0))
                throw new Win32Exception();

            int hr = CreatePseudoConsole(Size(columns, rows), inputRead, outputWrite, 0, out this.console);
            // the pseudo console keeps its own copies; ours must close, or reading never sees the end of the output
            inputRead.Dispose();
            outputWrite.Dispose();
            if (hr != 0)
                throw new Win32Exception(hr);

            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            STARTUPINFOEX startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
            // empty std handles: otherwise a program started by a process whose own std handles are redirected inherits
            // those, and its session output bypasses the pseudo console (seen in the spike)
            startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
            startup.lpAttributeList = Marshal.AllocHGlobal(size);
            try
            {
                if (!InitializeProcThreadAttributeList(startup.lpAttributeList, 1, 0, ref size)
                    || !UpdateProcThreadAttribute(startup.lpAttributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                        this.console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception();
                if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT,
                        IntPtr.Zero, null, ref startup, out this.process))
                    throw new Win32Exception();
            }
            finally
            {
                DeleteProcThreadAttributeList(startup.lpAttributeList);
                Marshal.FreeHGlobal(startup.lpAttributeList);
            }

            this.input = new FileStream(inputWrite, FileAccess.Write);
            this.output = new FileStream(outputRead, FileAccess.Read);
            this.reader = new Thread(this.ReadOutput) { IsBackground = true, Name = "ConPty output" };
            this.reader.Start();
            this.exitWait = ThreadPool.RegisterWaitForSingleObject(new ProcessWaitHandle(this.process.hProcess),
                (state, timedOut) => this.OnProcessExited(), null, -1, true);
        }

        /// <summary>The program's process id, for the log; 0 before it starts</summary>
        public int ProcessId
        {
            get { return this.process.dwProcessId; }
        }

        /// <summary>Read output until the pseudo console closes</summary>
        private void ReadOutput()
        {
            byte[] buffer = new byte[16384];
            try
            {
                int read;
                while ((read = this.output.Read(buffer, 0, buffer.Length)) > 0)
                {
                    byte[] chunk = new byte[read];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                    this.Output?.Invoke(chunk);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                // closed while reading: the session is over
            }
        }

        /// <summary>
        /// The program ended. Its last output may still be in the pseudo console, so it's closed (which flushes it to
        /// the reader) before Exited is raised.
        /// </summary>
        private void OnProcessExited()
        {
            if (Interlocked.Exchange(ref this.exitRaised, 1) != 0)
                return;
            int code;
            if (!GetExitCodeProcess(this.process.hProcess, out code))
                code = -1;
            this.ClosePseudoConsole();
            this.reader?.Join(2000);
            Log.InfoFormat("Terminal process {0} ended with exit code {1}", this.process.dwProcessId, code);
            this.Exited?.Invoke(code);
        }

        /// <summary>Type into the program, as if at its console. Safe to call from any thread.</summary>
        /// <param name="bytes">UTF-8 text and VT key sequences</param>
        public void Write(byte[] bytes)
        {
            FileStream stream = this.input;
            if (stream == null || bytes == null || bytes.Length == 0)
                return;
            try
            {
                lock (stream)
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                Log.Debug("Typing after the terminal process ended", ex);
            }
        }

        /// <summary>Only one console can be attached to this process at a time</summary>
        private static readonly object ConsoleLock = new object();

        /// <summary>
        /// Make the program's console pass arrow keys and Ctrl+C through as characters. plink reads its console with
        /// only "processed input" set, so the pseudo console turns ESC [ A into an Up-arrow key event that has no
        /// character, and plink drops it; and Ctrl+C is a console signal rather than 0x03 for the device. Turning on
        /// virtual terminal input and off processed input fixes both. plink sets its console mode when the session
        /// starts, after any login prompts, so this is checked before typing an ESC or Ctrl+C rather than once. It
        /// attaches this process to the program's console for a moment, which is why it's serialised.
        /// </summary>
        public void EnsureVirtualTerminalInput()
        {
            int pid = this.process.dwProcessId;
            if (pid == 0 || this.console == IntPtr.Zero)
                return;
            lock (ConsoleLock)
            {
                if (!AttachConsole(pid))
                {
                    Log.DebugFormat("Could not attach to the console of process {0}: error {1}", pid, Marshal.GetLastWin32Error());
                    return;
                }
                try
                {
                    // while attached, a Ctrl+C on that console would otherwise end SuperPuTTY too
                    SetConsoleCtrlHandler(IntPtr.Zero, true);
                    IntPtr input = CreateFile("CONIN$", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (input == INVALID_HANDLE_VALUE)
                        return;
                    try
                    {
                        uint mode;
                        if (GetConsoleMode(input, out mode))
                        {
                            uint wanted = (mode | ENABLE_VIRTUAL_TERMINAL_INPUT) & ~ENABLE_PROCESSED_INPUT;
                            if (wanted != mode && !SetConsoleMode(input, wanted))
                            {
                                Log.DebugFormat("Could not set the console mode of process {0}: error {1}", pid, Marshal.GetLastWin32Error());
                            }
                        }
                    }
                    finally
                    {
                        CloseHandle(input);
                    }
                }
                finally
                {
                    FreeConsole();
                }
            }
        }

        /// <summary>Change the console's size; plink passes it on to the server</summary>
        /// <param name="columns">Width in characters</param>
        /// <param name="rows">Height in lines</param>
        public void Resize(int columns, int rows)
        {
            if (this.console != IntPtr.Zero)
            {
                ResizePseudoConsole(this.console, Size(columns, rows));
            }
        }

        /// <summary>A COORD for the pseudo console, kept within what it accepts</summary>
        /// <param name="columns">Width</param>
        /// <param name="rows">Height</param>
        /// <returns>The size</returns>
        private static COORD Size(int columns, int rows)
        {
            return new COORD { X = (short)Math.Max(2, Math.Min(columns, 32000)), Y = (short)Math.Max(1, Math.Min(rows, 32000)) };
        }

        /// <summary>Close the pseudo console, which ends the program's console and the output stream</summary>
        private void ClosePseudoConsole()
        {
            IntPtr handle = Interlocked.Exchange(ref this.console, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                ClosePseudoConsole(handle);
            }
        }

        /// <summary>End the program if it's still running and release everything; <see cref="Exited"/> isn't raised</summary>
        public void Dispose()
        {
            // stop watching first, so ending the program here doesn't report it as the session ending
            Interlocked.Exchange(ref this.exitRaised, 1);
            this.exitWait?.Unregister(null);
            if (this.process.hProcess != IntPtr.Zero && WaitForSingleObject(this.process.hProcess, 0) != 0)
            {
                TerminateProcess(this.process.hProcess, 1);
            }
            this.ClosePseudoConsole();
            this.input?.Dispose();
            this.output?.Dispose();
            if (this.process.hThread != IntPtr.Zero)
            {
                CloseHandle(this.process.hThread);
                this.process.hThread = IntPtr.Zero;
            }
            if (this.process.hProcess != IntPtr.Zero)
            {
                CloseHandle(this.process.hProcess);
                this.process.hProcess = IntPtr.Zero;
            }
        }

        /// <summary>Lets the thread pool wait on the process handle without owning it</summary>
        private sealed class ProcessWaitHandle : WaitHandle
        {
            /// <summary>Wrap a process handle; it isn't closed when this is</summary>
            /// <param name="handle">The process handle</param>
            public ProcessWaitHandle(IntPtr handle)
            {
                this.SafeWaitHandle = new SafeWaitHandle(handle, false);
            }
        }

        private const uint ENABLE_PROCESSED_INPUT = 0x0001;
        private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x1;
        private const uint FILE_SHARE_WRITE = 0x2;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const int STARTF_USESTDHANDLES = 0x00000100;
        private const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD { public short X; public short Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);
        [DllImport("kernel32.dll")]
        private static extern int CreatePseudoConsole(COORD size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);
        [DllImport("kernel32.dll")]
        private static extern int ResizePseudoConsole(IntPtr console, COORD size);
        [DllImport("kernel32.dll")]
        private static extern void ClosePseudoConsole(IntPtr console);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);
        [DllImport("kernel32.dll")]
        private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(string application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            bool inherit, int flags, IntPtr environment, string directory, ref STARTUPINFOEX startup, out PROCESS_INFORMATION process);
        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out int exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr process, int exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr handle, uint mode);
    }
}
