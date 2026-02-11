using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorldLabs.API
{
    /// <summary>
    /// Utility class to load environment variables from a .env file.
    /// </summary>
    public static class EnvLoader
    {
        private static Dictionary<string, string> _envVariables;
        private static bool _isLoaded = false;

        /// <summary>
        /// Loads environment variables from a .env file in the project root.
        /// </summary>
        /// <param name="filePath">Optional custom path to the .env file. Defaults to project root.</param>
        public static void Load(string filePath = null)
        {
            _envVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(filePath))
            {
                // Look for .env in project root (parent of Assets folder)
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                filePath = Path.Combine(projectRoot, ".env");
            }

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[EnvLoader] .env file not found at: {filePath}");
                _isLoaded = true;
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(filePath);
                foreach (string line in lines)
                {
                    // Skip empty lines and comments
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                    {
                        continue;
                    }

                    // Parse KEY=VALUE format
                    int equalsIndex = trimmedLine.IndexOf('=');
                    if (equalsIndex > 0)
                    {
                        string key = trimmedLine.Substring(0, equalsIndex).Trim();
                        string value = trimmedLine.Substring(equalsIndex + 1).Trim();

                        // Remove surrounding quotes if present
                        if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                            (value.StartsWith("'") && value.EndsWith("'")))
                        {
                            value = value.Substring(1, value.Length - 2);
                        }

                        _envVariables[key] = value;
                    }
                }

                _isLoaded = true;
                Debug.Log($"[EnvLoader] Loaded {_envVariables.Count} environment variables from .env");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnvLoader] Failed to load .env file: {ex.Message}");
                _isLoaded = true;
            }
        }

        /// <summary>
        /// Gets an environment variable value.
        /// First checks the loaded .env file, then falls back to system environment variables.
        /// </summary>
        /// <param name="key">The environment variable key.</param>
        /// <param name="defaultValue">Default value if not found.</param>
        /// <returns>The environment variable value or default.</returns>
        public static string Get(string key, string defaultValue = null)
        {
            if (!_isLoaded)
            {
                Load();
            }

            // First check .env variables
            if (_envVariables != null && _envVariables.TryGetValue(key, out string value))
            {
                return value;
            }

            // Fall back to system environment variables
            string envValue = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(envValue))
            {
                return envValue;
            }

            return defaultValue;
        }

        /// <summary>
        /// Checks if an environment variable exists.
        /// </summary>
        /// <param name="key">The environment variable key.</param>
        /// <returns>True if the variable exists.</returns>
        public static bool HasKey(string key)
        {
            if (!_isLoaded)
            {
                Load();
            }

            if (_envVariables != null && _envVariables.ContainsKey(key))
            {
                return true;
            }

            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key));
        }

        /// <summary>
        /// Reloads the .env file.
        /// </summary>
        public static void Reload()
        {
            _isLoaded = false;
            _envVariables = null;
            Load();
        }
    }
}
