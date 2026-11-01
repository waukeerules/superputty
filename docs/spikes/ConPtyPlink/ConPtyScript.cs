// Spike for docs/MonacoTerminal.md, not part of SuperPutty.sln.
// Build: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:ConPtyScript.exe ConPtyScript.cs
// Run:   ConPtyScript.exe "<command line>" <script file> <raw output file> [COLSxROWS]
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Drives a console program (plink) inside a Windows pseudo console from a small script, to check against a real
/// device what ConPtyPlink.cs could only show offline: prompts, paging, resizing and what the raw stream looks like.
/// Script lines: "wait TEXT" (up to 15 s for TEXT to appear after the last wait), "send TEXT" (\r \n \t \e \xNN escapes),
/// "sendsecret FILE" (sends a file's first line plus \r, never printed), "sleep MS", "resize COLS ROWS", "end".
/// Everything received is written raw to the output file and echoed with escapes made visible.
/// </summary>
public static class ConPtyScript
{
    /// <summary>Output received so far, for "wait"</summary>
    private static readonly StringBuilder received = new StringBuilder();

    /// <summary>Where in <see cref="received"/> the next "wait" starts looking</summary>
    private static int waitFrom;

    /// <summary>Run the command line in a pseudo console (80x24 unless given) and play the script against it</summary>
    /// <param name="args">Command line, script file, raw output file, optional start size such as 132x40</param>
    public static void Main(string[] args)
    {
        SafeFileHandle inputRead, inputWrite, outputRead, outputWrite;
        if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0) || !CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0))
            throw new Win32Exception();
        IntPtr console;
        string[] startSize = (args.Length > 3 ? args[3] : "80x24").Split('x');
        int hr = CreatePseudoConsole(new COORD { X = short.Parse(startSize[0]), Y = short.Parse(startSize[1]) }, inputRead, outputWrite, 0, out console);
        if (hr != 0)
            Marshal.ThrowExceptionForHR(hr);
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
            || !UpdateProcThreadAttribute(startup.lpAttributeList, 0, (IntPtr)0x00020016, console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception();
        PROCESS_INFORMATION process;
        if (!CreateProcess(null, args[0], IntPtr.Zero, IntPtr.Zero, false, 0x00080000, IntPtr.Zero, null, ref startup, out process))
            throw new Win32Exception();

        FileStream raw = new FileStream(args[2], FileMode.Create, FileAccess.Write);
        Thread reader = new Thread(() =>
        {
            using (FileStream output = new FileStream(outputRead, FileAccess.Read))
            {
                byte[] buffer = new byte[4096];
                int read;
                while ((read = output.Read(buffer, 0, buffer.Length)) > 0)
                {
                    raw.Write(buffer, 0, read);
                    raw.Flush();
                    string text = Encoding.UTF8.GetString(buffer, 0, read);
                    lock (received) { received.Append(text); }
                    Console.Write(Visible(text));
                }
            }
        }) { IsBackground = true };
        reader.Start();

        FileStream input = new FileStream(inputWrite, FileAccess.Write);
        foreach (string line in File.ReadAllLines(args[1]))
        {
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int space = line.IndexOf(' ');
            string verb = space < 0 ? line : line.Substring(0, space);
            string rest = space < 0 ? "" : line.Substring(space + 1);
            Console.Write("\n<<" + (verb == "sendsecret" ? "sendsecret ***" : line) + ">>\n");
            if (verb == "wait") Wait(rest);
            else if (verb == "send") Send(input, Unescape(rest));
            else if (verb == "sendsecret") Send(input, File.ReadAllLines(rest)[0] + "\r");
            else if (verb == "sleep") Thread.Sleep(int.Parse(rest));
            else if (verb == "resize") { string[] p = rest.Split(' '); ResizePseudoConsole(console, new COORD { X = short.Parse(p[0]), Y = short.Parse(p[1]) }); }
            else if (verb == "end") break;
        }
        WaitForSingleObject(process.hProcess, 3000);
        ClosePseudoConsole(console);
        reader.Join(2000);
        raw.Dispose();
        Console.WriteLine("\n[done]");
    }

    /// <summary>Wait up to 15 s for text to appear in the output after the previous wait</summary>
    /// <param name="text">The text to wait for</param>
    private static void Wait(string text)
    {
        DateTime until = DateTime.Now.AddSeconds(15);
        while (DateTime.Now < until)
        {
            lock (received)
            {
                int at = received.ToString().IndexOf(text, waitFrom, StringComparison.Ordinal);
                if (at >= 0) { waitFrom = at + text.Length; return; }
            }
            Thread.Sleep(50);
        }
        Console.Write("\n[timeout waiting for '" + text + "']\n");
    }

    /// <summary>Write text to the pseudo console's input, as if typed</summary>
    /// <param name="input">The input pipe</param>
    /// <param name="text">The text</param>
    private static void Send(FileStream input, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        input.Write(bytes, 0, bytes.Length);
        input.Flush();
    }

    /// <summary>Turn \r \n \t \e and \xNN in a script line into the characters</summary>
    /// <param name="text">Script text</param>
    /// <returns>The text to send</returns>
    private static string Unescape(string text)
    {
        StringBuilder result = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length) { result.Append(text[i]); continue; }
            char c = text[++i];
            if (c == 'r') result.Append('\r');
            else if (c == 'n') result.Append('\n');
            else if (c == 't') result.Append('\t');
            else if (c == 'e') result.Append('\x1b');
            else if (c == 'x') { result.Append((char)Convert.ToInt32(text.Substring(i + 1, 2), 16)); i += 2; }
            else result.Append(c);
        }
        return result.ToString();
    }

    /// <summary>Make ESC and other control characters readable</summary>
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
