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
    }
}