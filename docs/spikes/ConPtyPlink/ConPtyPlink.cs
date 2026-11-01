// Spike for docs/MonacoTerminal.md, not part of SuperPutty.sln.
// Build: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:ConPtyPlink.exe ConPtyPlink.cs
// Run:   ConPtyPlink.exe "\"C:\Program Files\PuTTY\plink.exe\" -load MyProfile"
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Proves that a .NET Framework 4.8 process can run plink (or any console program) inside a Windows pseudo console
/// (ConPTY, Windows 10 1809+) and get its output as a VT byte stream, which is what xterm.js would be fed. Because
/// plink sees a real console, its password, passphrase and host key prompts work and appear in the stream, and
/// ResizePseudoConsole makes it send the server a window-size change. Prints the stream with escapes made visible.
/// </summary>
public static class ConPtyPlink
{
    /// <summary>Start the command line in an 80x24 pseudo console, echo its output for a few seconds, then resize once</summary>
    /// <param name="args">args[0]: the command line to run</param>
    public static void Main(string[] args)
    {
        string commandLine = args.Length > 0 ? args[0] : "cmd.exe /c echo hello from conpty & ver";

        SafeFileHandle inputRead, inputWrite, outputRead, outputWrite;
        if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0) || !CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0))
            throw new Win32Exception();

        IntPtr console;
        int hr = CreatePseudoConsole(new COORD { X = 80, Y = 24 }, inputRead, outputWrite, 0, out console);
        if (hr != 0)
            Marshal.ThrowExceptionForHR(hr);
        // the pseudo console holds its own copies; ours must close or reads never see end of stream
        inputRead.Dispose();
        outputWrite.Dispose();

        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        STARTUPINFOEX startup = new STARTUPINFOEX();
        startup.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
        // empty std handles: otherwise a child of a process whose stdout is redirected inherits those handles and its session
        // output bypasses the pseudo console
        startup.StartupInfo.dwFlags = 0x00000100; // STARTF_USESTDHANDLES
        startup.lpAttributeList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(startup.lpAttributeList, 1, 0, ref size)
            || !UpdateProcThreadAttribute(startup.lpAttributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception();

        PROCESS_INFORMATION process;
        if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero, null, ref startup, out process))
            throw new Win32Exception();

        Thread reader = new Thread(() =>
        {
            using (FileStream output = new FileStream(outputRead, FileAccess.Read))
            {
                byte[] buffer = new byte[4096];
                int read;
                while ((read = output.Read(buffer, 0, buffer.Length)) > 0)
                {
                    Console.Write(Visible(Encoding.UTF8.GetString(buffer, 0, read)));
                }
            }
        }) { IsBackground = true };
        reader.Start();

        WaitForSingleObject(process.hProcess, 3000);
        ResizePseudoConsole(console, new COORD { X = 132, Y = 40 });
        WaitForSingleObject(process.hProcess, 5000);
        ClosePseudoConsole(console);
        reader.Join(2000);
        inputWrite.Dispose();
        Console.WriteLine();
        Console.WriteLine("[done]");
    }

    /// <summary>Make ESC and other control characters readable in the spike's output</summary>
    /// <param name="text">Raw VT text</param>
    /// <returns>The text with ESC shown as \e and CR/LF kept</returns>
    private static string Visible(string text)
    {
        StringBuilder result = new StringBuilder();
        foreach (char c in text)
        {
            if (c == '\x1b') result.Append("\\e");
            else if (c == '\n' || c == '\r' || !char.IsControl(c)) result.Append(c);
            else result.AppendFormat("\\x{0:x2}", (int)c);
        }
        return result.ToString();
    }

    private const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [StructLayout(LayoutKind.Sequential)] private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)] private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
    [StructLayout(LayoutKind.Sequential)] private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(COORD size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")] private static extern int ResizePseudoConsole(IntPtr console, COORD size);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, int flags, IntPtr environment, string directory, ref STARTUPINFOEX startup, out PROCESS_INFORMATION process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
}
