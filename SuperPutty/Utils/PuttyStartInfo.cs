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

        public static string GetExecutable(SessionData session)
        {
            try
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
            catch (Exception ex)
            {
                Log.ErrorFormat("Error getting executable for Proto {0}: {1}", session.Proto, ex.Message);
                return null;
            }
        }

        public PuttyStartInfo(SessionData session)
        {
            try
            {
                if (session == null)
                {
                    Log.Error("Session is null in PuttyStartInfo constructor");
                    throw new ArgumentNullException(nameof(session));
                }

                string argsToLog = null;
                this.Executable = GetExecutable(session);
                this.Session = session;

                if (this.Executable == null)
                {
                    Log.ErrorFormat("No executable found for session {0}, Proto {1}", session.SessionId, session.Proto);
                    throw new InvalidOperationException($"No executable found for protocol {session.Proto}");
                }

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

                Log.InfoFormat("PuttyStartInfo created: Executable={0}, Args={1}, WorkingDir={2}",
                    this.Executable, argsToLog ?? this.Args, this.WorkingDir ?? "null");
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Error creating PuttyStartInfo for session {0}: {1}\nStackTrace: {2}",
                    session?.SessionId, ex.Message, ex.StackTrace);
                throw;
            }
        }

        static string MakeArgs(SessionData session, bool includePassword)
        {
            if (!string.IsNullOrEmpty(session.Password) && includePassword && !SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg)
                Log.Warn("SuperPuTTY is set to NOT allow the use of the -pw <password> argument, this can be overridden in Tools -> Options -> GUI");

            string args = "-" + session.Proto.ToString().ToLower() + " ";
            args += !string.IsNullOrEmpty(session.Password) && session.Password.Length > 0 && SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg
                ? "-pw " + (includePassword ? session.Password : "XXXXX") + " "
                : "";
            args += "-P " + session.Port + " ";
            args += !string.IsNullOrEmpty(session.PuttySession) ? "-load \"" + session.PuttySession + "\" " : "";
            args += !string.IsNullOrEmpty(SuperPuTTY.Settings.PuttyDefaultParameters) ? SuperPuTTY.Settings.PuttyDefaultParameters + " " : "";
            string extraArgs = CommandLineOptions.replacePassword(session.ExtraArgs, "");
            args += !string.IsNullOrEmpty(extraArgs) ? extraArgs + " " : "";
            args += !string.IsNullOrEmpty(session.Username) ? "-l " + session.Username + " " : "";
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
            catch (Exception ex)
            {
                Log.Warn("Could not parse env vars in args", ex);
            }
            return result;
        }

        public void StartStandalone()
        {
            try
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
                Log.InfoFormat("Starting standalone process: FileName={0}, Arguments={1}, WorkingDir={2}",
                    startInfo.FileName, startInfo.Arguments, startInfo.WorkingDirectory ?? "null");
                Process.Start(startInfo);
                Log.InfoFormat("Successfully started process for session {0}", Session?.SessionId);
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Error starting standalone process for session {0}: {1}\nStackTrace: {2}",
                    Session?.SessionId, ex.Message, ex.StackTrace);
                throw;
            }
        }

        public SessionData Session { get; private set; }
        public string Args { get; private set; }
        public string WorkingDir { get; private set; }
        public string Executable { get; private set; }
    }
}