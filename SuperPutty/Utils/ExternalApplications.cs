using System;
using log4net;
using SuperPutty.Data;
using System.Diagnostics;

namespace SuperPutty.Utils
{
    public static class ExternalApplications
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ExternalApplications));

        /// <summary>
        /// Open the filezilla program with the sesion data, for sftp connection. 
        /// </summary>
        /// <param name="session"></param>
        public static void openFileZilla(SessionData session)
        {
            if (!String.IsNullOrEmpty(session.Password) && !SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg)
                Log.Warn("SuperPuTTY is set to NOT allow the use of the -pw <password> argument, this can be overriden in Tools -> Options -> GUI");

            // open filezilla with the session info (https://wiki.filezilla-project.org/Command-line_arguments_%28Client%29)
            String pw = session.Password;
            String user = Uri.EscapeDataString(session.Username);
            String userPw =    !String.IsNullOrEmpty(user) ? (!String.IsNullOrEmpty(pw) && SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg ? user + ":" + pw      + "@" : user + "@") : "";
            String userPwLog = !String.IsNullOrEmpty(user) ? (!String.IsNullOrEmpty(pw) && SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg ? user + ":" + "XXXXX" + "@" : user + "@") : "";
            String rp = String.IsNullOrEmpty(session.RemotePath) ? "" : session.RemotePath;
            String lp = String.IsNullOrEmpty(session.LocalPath) ? "" : " --local=\"" + session.LocalPath + "\" ";
            String param = "sftp://" + userPw + session.Host + ":" + session.Port + rp + lp;
            
            Log.Debug("Send to FileZilla:" + SuperPuTTY.Settings.FileZillaExe + " params="+ "sftp://" + userPwLog + session.Host + ":" + session.Port + rp + lp);
            Process.Start(SuperPuTTY.Settings.FileZillaExe, param);
        }

        /// <summary>
        /// Open the WinSCP program with the sesion data, for sftp connection. 
        /// </summary>
        /// <param name="session"></param>
        public static void openWinSCP(SessionData session)
        {
            // fill in {key} placeholders, as for PuTTY (asking for any without a value), on a copy of the session
            if (!SessionVariables.EnsureDefined(session, SuperPuTTY.MainForm))
                return;
            session = SessionVariables.Resolve(session);
            if (!String.IsNullOrEmpty(session.Password) && !SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg)
                Log.Warn("SuperPuTTY is set to NOT allow the use of the -pw <password> argument, this can be overriden in Tools -> Options -> GUI");

            // open WinSCP with the session info (https://winscp.net/eng/docs/commandline)           
            String pw = Uri.EscapeDataString(session.Password ?? "");
            String user = Uri.EscapeDataString(UsernameFor(session) ?? "");
            String userPw =    !String.IsNullOrEmpty(user) ? (!String.IsNullOrEmpty(pw) && SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg ? user + ":" + pw      + "@" : user + "@") : "";
            String userPwLog = !String.IsNullOrEmpty(user) ? (!String.IsNullOrEmpty(pw) && SuperPuTTY.Settings.AllowPlainTextPuttyPasswordArg ? user + ":" + "XXXXX" + "@" : user + "@") : "";
            String rp = String.IsNullOrEmpty(session.RemotePath) ? "" : session.RemotePath;
            if (!rp.EndsWith("/"))
            {
                rp += "/";
            }
            String lp = String.IsNullOrEmpty(session.LocalPath) ? "" : " -rawsettings localDirectory=\"" + session.LocalPath + "\" ";
            int port = session.Port != 0 ? session.Port : SessionData.DefaultPort(session.Proto);
            // the key PuTTY would use; WinSCP doesn't read PuTTY's saved sessions, so it has to be told
            String key = PrivateKeyFor(session);
            String keyArg = key == null ? "" : " -privatekey=\"" + key + "\"";
            String param = "sftp://" + userPw + session.Host + ":" + port + rp + lp + keyArg;
            Log.Debug("Send to WinSCP:" + SuperPuTTY.Settings.WinSCPExe + " params="+ "sftp://" + userPwLog + session.Host + ":" + port + rp + lp + keyArg);
            Process.Start(SuperPuTTY.Settings.WinSCPExe, param);
        }

        /// <summary>
        /// The username PuTTY would log in with: the session's own, or else the "Auto-login username" (UserName) of the
        /// PuTTY (or KiTTY) saved session it loads, so a user kept only in the PuTTY profile reaches WinSCP too
        /// </summary>
        /// <param name="session">The session, placeholders already filled in</param>
        /// <returns>The username, or null if neither has one (WinSCP then asks)</returns>
        public static string UsernameFor(SessionData session)
        {
            if (!String.IsNullOrEmpty(session.Username))
                return session.Username;
            string user = PuttyDataHelper.GetProfileValue(session.PuttySession, "UserName");
            return String.IsNullOrWhiteSpace(user) ? null : user.Trim();
        }

        /// <summary>
        /// The private key PuTTY would log in with for a session: a -i in its extra arguments, otherwise the
        /// PublicKeyFile of the PuTTY (or KiTTY) saved session it loads, with environment variables expanded
        /// </summary>
        /// <param name="session">The session, placeholders already filled in</param>
        /// <returns>The key file's path, or null if it uses none (PuTTY then tries Pageant, as WinSCP does by itself)</returns>
        public static string PrivateKeyFor(SessionData session)
        {
            string key = CommandLineOptions.getcommand(session.ExtraArgs, "-i");
            if (String.IsNullOrEmpty(key))
            {
                key = PuttyDataHelper.GetProfileValue(session.PuttySession, "PublicKeyFile");
            }
            if (String.IsNullOrWhiteSpace(key))
                return null;
            key = Environment.ExpandEnvironmentVariables(key.Trim());
            if (!System.IO.File.Exists(key))
            {
                Log.WarnFormat("The session's key file {0} doesn't exist; WinSCP is started without it", key);
                return null;
            }
            return key;
        }

    }
}
