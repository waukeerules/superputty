using System;
using System.Linq;
using SuperPutty.Data;
using log4net;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace SuperPutty.Utils
{
    public class PuttyStartInfo
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PuttyStartInfo));

        private static readonly Regex regExEnvVars = new Regex(@"(%\w+%)");

        public static String GetExecutable(SessionData session)
        {
            switch (session.Proto)
            {
                case ConnectionProtocol.Mintty:
                    return TryParseEnvVars(SuperPuTTY.Settings.MinttyExe);

                case ConnectionProtocol.VNC:
                    return TryParseEnvVars(SuperPuTTY.Settings.VNCExe);

                case ConnectionProtocol.RDP:
                    return TryParseEnvVars(SuperPuTTY.Settings.RDPExe);

                case ConnectionProtocol.WSL:
                    return Environment.ExpandEnvironmentVariables("%systemroot%\\system32\\wsl.exe");

                case ConnectionProtocol.WINCMD:
                    return Environment.ExpandEnvironmentVariables("%systemroot%\\system32\\cmd.exe");
                
                case ConnectionProtocol.PS:
                    return Environment.ExpandEnvironmentVariables("%systemroot%\\system32\\windowspowershell\\v1.0\\powershell.exe");

                default:
                    return TryParseEnvVars(SuperPuTTY.Settings.PuttyExe);
            }
        }

        public PuttyStartInfo(SessionData session)
        {
            string argsToLog = null;

            // replace {key} placeholders on a copy so the stored session keeps them
            session = SessionVariables.Resolve(session);

            this.Executable = GetExecutable(session);

            if (session.Proto == ConnectionProtocol.Cygterm)
            {
                CygtermStartInfo cyg = new CygtermStartInfo(session);
                this.Args = cyg.Args;
                this.WorkingDir = cyg.StartingDir;
            }
            else if (session.Proto == ConnectionProtocol.Mintty)
            {
                MinttyStartInfo mintty = new MinttyStartInfo(session);
                this.Args = mintty.Args;
                this.WorkingDir = mintty.StartingDir;
            }
            else if (session.Proto == ConnectionProtocol.VNC)
            {
                VNCStartInfo vnc = new VNCStartInfo(session);
                this.Args = vnc.Args;
                this.WorkingDir = vnc.StartingDir;
            }
            else if (session.Proto == ConnectionProtocol.RDP)
            {
                RDPStartInfo rdp = new RDPStartInfo(session, this.Executable);
                this.Args = rdp.Args;
                this.WorkingDir = rdp.StartingDir;
                // the remote desktop is sized to the tab once its size is known (see ApplicationPanel.StartApplication)
                this.ArgsForSize = size => { string args = rdp.GetArgs(size); return args.Contains('%') ? TryParseEnvVars(args) : args; };
            }
            else if (session.Proto == ConnectionProtocol.WINCMD)
            {
                WCMDStartInfo wcmd = new WCMDStartInfo(session);
                this.Args = wcmd.Args;
                this.WorkingDir = wcmd.StartingDir;
            }
            else if (session.Proto == ConnectionProtocol.PS)
            {
                PSStartInfo ps = new PSStartInfo(session);
                this.Args = ps.Args;
                this.WorkingDir = ps.StartingDir;
            }
            else if (session.Proto == ConnectionProtocol.WSL)
            {
                WSLStartInfo ps = new WSLStartInfo(session);
                this.Args = ps.Args;
                this.WorkingDir = ps.StartingDir;
            }
            else
            {
                this.Args = MakeArgs(session, true);
                argsToLog = MakeArgs(session, false);
            }

            // Windows 11 hands console programs to the default terminal (usually Windows Terminal), whose window
            // belongs to another process and can't be embedded. Under conhost.exe they keep a classic console
            // window; ApplicationPanel.WaitForTargetProcess then follows conhost to the shell to find it.
            if (UsesConsoleHost(session.Proto))
            {
                this.Args = ("\"" + this.Executable + "\" " + this.Args).TrimEnd();
                this.Executable = ConsoleHostPath;
            }

            // attempt to parse env vars
            this.Args = this.Args.Contains('%') ? TryParseEnvVars(this.Args) : this.Args;

            Log.InfoFormat("Putty Args: '{0}'", argsToLog ?? this.Args);
        }

        /// <summary>Full path of conhost.exe, the classic Windows console host</summary>
        public static readonly string ConsoleHostPath = Environment.ExpandEnvironmentVariables(@"%systemroot%\system32\conhost.exe");

        /// <summary>First Windows build (Windows 11) that can hand console programs to another terminal such as Windows Terminal</summary>
        private const int FirstBuildWithTerminalDelegation = 22000;

        /// <summary>The Windows build number, read once from the registry (Environment.OSVersion reports 6.2 to apps without a manifest)</summary>
        private static readonly Lazy<int> WindowsBuild = new Lazy<int>(() =>
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build;
                    return int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out build) ? build : 0;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the Windows build number", ex);
                return 0;
            }
        });

        /// <summary>
        /// Whether sessions of this protocol are started under conhost.exe so their window can be embedded:
        /// the console protocols (cmd, PowerShell, WSL) on Windows 11 and later, where the default terminal
        /// may otherwise take the window
        /// </summary>
        /// <param name="proto">The session's protocol</param>
        /// <returns>true to start the session as "conhost.exe &lt;program&gt; &lt;args&gt;"</returns>
        public static bool UsesConsoleHost(ConnectionProtocol proto)
        {
            return (proto == ConnectionProtocol.WINCMD || proto == ConnectionProtocol.PS || proto == ConnectionProtocol.WSL)
                && WindowsBuild.Value >= FirstBuildWithTerminalDelegation
                && File.Exists(ConsoleHostPath);
        }

        static string MakeArgs(SessionData session, bool includePassword)
        {
            if (!String.IsNullOrEmpty(session.Password) && includePassword && !SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg)
                Log.Warn("SuperPuTTY is set to NOT allow the use of the -pw <password> argument, this can be overriden in Tools -> Options -> GUI");

            string args = "-" + session.Proto.ToString().ToLower() + " ";
            args += !String.IsNullOrEmpty(session.Password) && session.Password.Length > 0 && SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg 
                ? "-pw " + (includePassword ? session.Password : "XXXXX") + " " 
                : "";
            args += "-P " + session.Port + " ";
            args += !String.IsNullOrEmpty(session.PuttySession) ? "-load \"" + session.PuttySession + "\" " : "";

            args += !String.IsNullOrEmpty(SuperPuTTY.Settings.PuttyDefaultParameters) ? SuperPuTTY.Settings.PuttyDefaultParameters + " " : "";

            //If extra args contains the password, delete it (it's in session.password)
            string extraArgs = CommandLineOptions.replacePassword(session.ExtraArgs,"");            
            args += !String.IsNullOrEmpty(extraArgs) ? extraArgs + " " : "";
            args += !String.IsNullOrEmpty(session.Username) && session.Username.Length > 0 ? " -l " + session.Username + " " : "";
            args += session.Host;

            return args;
        }

        static string TryParseEnvVars(string args)
        {
            string result = args;
            try
            {
                result = regExEnvVars.Replace(
                    args,
                    m =>
                    {
                        string name = m.Value.Trim('%');
                        return Environment.GetEnvironmentVariable(name) ?? m.Value;
                    });
            }
            catch(Exception ex)
            {
                Log.Warn("Could not parse env vars in args", ex);
            }
            return result;
        }

        /// <summary>
        /// Start of standalone putty process
        /// </summary>
        public void StartStandalone()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = this.Executable,
                Arguments = this.Args
            };
            if (this.WorkingDir != null && Directory.Exists(this.WorkingDir))
            {
                startInfo.WorkingDirectory = this.WorkingDir;
            }
            Process.Start(startInfo);
        }

        public SessionData Session { get; private set; }

        public string Args { get; private set; }

        /// <summary>
        /// For protocols whose arguments depend on the window size (RDP), builds the arguments for a given
        /// size; null for the others, which always use <see cref="Args"/>
        /// </summary>
        public Func<System.Drawing.Size, string> ArgsForSize { get; private set; }
        public string WorkingDir { get; private set; }
        public string Executable { get; private set; }

    }
}
