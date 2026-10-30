using System;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;
using SuperPutty.Models;
using log4net;

namespace SuperPutty.Utils
{
    public class EnvironmentVariablesManager
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(EnvironmentVariablesManager));
        private readonly string _filePath;

        private const string FileName = "EnvironmentVariables.json";

        public EnvironmentVariablesManager()
        {
            // Values are per user, so keep them next to the user's SuperXPuTTY.settings rather than in
            // SettingsFolder, which may be a shared network drive holding everyone's Sessions.xml
            string userFolder = Path.GetDirectoryName(Properties.Settings.Default.SettingsFilePath ?? Path.Combine(Application.UserAppDataPath, "SuperXPuTTY.settings"));
            Directory.CreateDirectory(userFolder);
            _filePath = Path.Combine(userFolder, FileName);
            MigrateFromSettingsFolder();
            EnsureFileExists();
        }

        /// <summary>Copy the file from SettingsFolder, where earlier builds stored it, if this user has none yet</summary>
        private void MigrateFromSettingsFolder()
        {
            string settingsFolder = SuperPuTTY.Settings.SettingsFolder;
            if (string.IsNullOrEmpty(settingsFolder) || File.Exists(_filePath))
                return;

            string oldPath = Path.Combine(settingsFolder, FileName);
            try
            {
                if (File.Exists(oldPath) && !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(oldPath, _filePath);
                    Log.InfoFormat("Copied {0} from {1} to {2}", FileName, oldPath, _filePath);
                }
            }
            catch (Exception ex)
            {
                Log.WarnFormat("Could not copy {0} from {1}: {2}", FileName, oldPath, ex.Message);
            }
        }

        private void EnsureFileExists()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    File.WriteAllText(_filePath, JsonConvert.SerializeObject(new EnvironmentVariables(), Formatting.Indented));
                    Log.InfoFormat("Created empty EnvironmentVariables.json at {0}", _filePath);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Failed to create JSON file at {0}: {1}", _filePath, ex.Message);
                throw new IOException($"Failed to create JSON file at {_filePath}: {ex.Message}", ex);
            }
        }

        public EnvironmentVariables LoadVariables()
        {
            try
            {
                string json = File.ReadAllText(_filePath);
                var variables = JsonConvert.DeserializeObject<EnvironmentVariables>(json) ?? new EnvironmentVariables();
                foreach (EnvironmentVariable variable in variables.Variables)
                {
                    if (variable.Secret)
                    {
                        variable.Value = Unprotect(variable.Key, variable.ProtectedValue);
                    }
                }
                Log.InfoFormat("Loaded {0} environment variables from {1}", variables.Variables.Count, _filePath);
                return variables;
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Failed to load environment variables from {0}: {1}", _filePath, ex.Message);
                return new EnvironmentVariables();
            }
        }

        public void SaveVariables(EnvironmentVariables variables)
        {
            try
            {
                foreach (EnvironmentVariable variable in variables.Variables)
                {
                    variable.ProtectedValue = variable.Secret ? Protect(variable.Value) : null;
                }
                string json = JsonConvert.SerializeObject(variables, Formatting.Indented);
                int retries = 3;
                while (retries > 0)
                {
                    try
                    {
                        File.WriteAllText(_filePath, json);
                        Log.InfoFormat("Saved {0} environment variables to {1}", variables.Variables.Count, _filePath);
                        break;
                    }
                    catch (IOException ex)
                    {
                        Log.WarnFormat("Retry {0}: Failed to save to {1}, error: {2}", retries, _filePath, ex.Message);
                        retries--;
                        System.Threading.Thread.Sleep(1000);
                        if (retries == 0) throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Failed to save environment variables to {0}: {1}", _filePath, ex.Message);
                throw new IOException($"Failed to save environment variables to {_filePath}: {ex.Message}", ex);
            }
        }

        /// <summary>Extra input to DPAPI so other programs using DPAPI for this user can't decrypt these values by accident</summary>
        private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("SuperPuTTY.EnvironmentVariables");

        /// <summary>Encrypt a secret value with Windows DPAPI, so only the current Windows user on this computer can read it</summary>
        /// <param name="value">The plain value; may be empty</param>
        /// <returns>The encrypted value as base64</returns>
        private static string Protect(string value)
        {
            byte[] plain = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] encrypted = System.Security.Cryptography.ProtectedData.Protect(plain, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        /// <summary>
        /// Decrypt a secret value. It can't be decrypted if the file came from another Windows user or computer;
        /// the value is then empty, and the user has to enter it again.
        /// </summary>
        /// <param name="key">The variable's key, for the log</param>
        /// <param name="protectedValue">The encrypted value as base64</param>
        /// <returns>The plain value, or an empty string if it can't be decrypted</returns>
        private static string Unprotect(string key, string protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue))
                return string.Empty;
            try
            {
                byte[] plain = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                Log.WarnFormat("Could not decrypt the secret '{0}' (saved by another Windows user or computer?); it needs to be entered again: {1}", key, ex.Message);
                return string.Empty;
            }
        }
    }
}