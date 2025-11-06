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

        public EnvironmentVariablesManager()
        {
            string settingsFolder = SuperPuTTY.Settings.SettingsFolder;
            if (string.IsNullOrEmpty(settingsFolder))
            {
                settingsFolder = Path.GetDirectoryName(Properties.Settings.Default.SettingsFilePath ?? Path.Combine(Application.UserAppDataPath, "SuperXPuTTY.settings"));
            }
            Directory.CreateDirectory(settingsFolder);
            _filePath = Path.Combine(settingsFolder, "EnvironmentVariables.json");
            EnsureFileExists();
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