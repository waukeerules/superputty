using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DarkModeForms;
using log4net;
using SuperPutty.Data;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Replaces {key} placeholders in session fields with the current user's values
    /// from Tools -> Environment Variables, so one Sessions.xml can be shared by many users
    /// </summary>
    public static class SessionVariables
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionVariables));

        private static readonly Regex regExPlaceholder = new Regex(@"\{(\w+)\}");

        /// <summary>Load the current user's variables, with case-insensitive keys</summary>
        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in new EnvironmentVariablesManager().LoadVariables().Variables)
            {
                if (!string.IsNullOrWhiteSpace(variable.Key))
                {
                    variables[variable.Key.Trim()] = variable.Value ?? string.Empty;
                }
            }
            return variables;
        }

        /// <summary>Replace each {key} with its value in a single pass; undefined keys are left as they are</summary>
        public static string Substitute(string input, IDictionary<string, string> variables)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return regExPlaceholder.Replace(input, m => variables.TryGetValue(m.Groups[1].Value, out string value) ? value : m.Value);
        }

        /// <summary>Return the placeholders in <paramref name="input"/> that have no value, e.g. "{domain}"</summary>
        public static List<string> FindMissing(string input, IDictionary<string, string> variables)
        {
            if (string.IsNullOrEmpty(input))
                return new List<string>();

            return regExPlaceholder.Matches(input).Cast<Match>()
                .Where(m => !variables.ContainsKey(m.Groups[1].Value))
                .Select(m => m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Return the undefined placeholders in the host, username and extra arguments.
        /// The password is not checked since a real password may contain braces.
        /// </summary>
        public static List<string> FindMissing(SessionData session, IDictionary<string, string> variables)
        {
            return FindMissing(session.Host, variables)
                .Concat(FindMissing(session.Username, variables))
                .Concat(FindMissing(session.ExtraArgs, variables))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Return a copy of the session with its placeholders replaced. The original keeps its
        /// placeholders, so the current user's values are never saved back to Sessions.xml.
        /// </summary>
        public static SessionData Resolve(SessionData session)
        {
            Dictionary<string, string> variables = Load();
            SessionData resolved = (SessionData)session.Clone();
            resolved.Host = Substitute(session.Host, variables);
            resolved.Username = Substitute(session.Username, variables);
            resolved.Password = Substitute(session.Password, variables);
            resolved.ExtraArgs = Substitute(session.ExtraArgs, variables);
            return resolved;
        }

        /// <summary>
        /// Check that every placeholder in the session has a value. If not, tell the user which are
        /// missing and offer to open the Environment Variables dialog to define them.
        /// </summary>
        /// <returns>true if the session can be launched</returns>
        public static bool EnsureDefined(SessionData session, IWin32Window owner = null)
        {
            while (true)
            {
                List<string> missing = FindMissing(session, Load());
                if (missing.Count == 0)
                    return true;

                Log.WarnFormat("Session {0} has undefined placeholders: {1}", session.SessionId, string.Join(", ", missing));
                DialogResult result = Messenger.MessageBox(
                    $"Session \"{session.SessionName}\" uses placeholders that have no value yet:\n\n{string.Join(", ", missing)}\n\n" +
                    "Define them in Tools -> Environment Variables. Open it now?",
                    "Undefined placeholders", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                    return false;

                using (EnvironmentVariablesForm form = new EnvironmentVariablesForm())
                {
                    if (form.ShowDialog(owner) != DialogResult.OK)
                        return false;
                }
            }
        }
    }
}
