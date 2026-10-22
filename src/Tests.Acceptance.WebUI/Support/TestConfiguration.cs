using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace Tests.Acceptance.WebUI.Support
{
    /// <summary>
    /// TestConfiguration - Loads and manages test settings from appsettings.json
    /// Follows Coral.Testing pattern for centralized configuration management.
    /// 
    /// Environment-specific overrides are handled by:
    /// - appsettings.json (base/shared configuration)
    /// - appsettings.{Environment}.json (environment-specific overrides)
    /// - Environment variables (highest priority, for secrets/sensitive data)
    /// 
    /// Configuration is loaded once at startup and cached.
    /// </summary>
    public class TestConfiguration
    {
        private static IConfiguration? _config;
        private static readonly object _lockObject = new object();

        static TestConfiguration()
        {
            LoadConfiguration();
        }

        private static void LoadConfiguration()
        {
            if (_config != null)
                return;

            lock (_lockObject)
            {
                if (_config != null)
                    return;

                // Determine the environment
                var environment = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

                var configBuilder = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .Build();

                _config = configBuilder;
            }
        }

        /// <summary>
        /// Get the base URL for the specified environment, or the default environment if not specified.
        /// </summary>
        public static string GetBaseUrl(string? environmentName = null)
        {
            environmentName = environmentName ?? _config?["Environments:Default"];
            return _config?[$"Environments:{environmentName}:BaseUrl"] ?? "http://localhost:8888";
        }

        /// <summary>
        /// Name of the target environment (Environments:Default), e.g. "Development"
        /// </summary>
        public static string GetEnvironmentName()
        {
            return _config?["Environments:Default"] ?? "Development";
        }

        /// <summary>
        /// How the target DOrc API authenticates: "OAuth" (Entra ID - uses saved-session sign-in)
        /// or "WinAuth" (Windows/Negotiate - browser passes the logged-on user's credentials, no sign-in step)
        /// </summary>
        public static bool IsWinAuthMode()
        {
            return string.Equals(_config?["Authentication:Mode"], "WinAuth", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Playwright:SlowMo - milliseconds to pause between browser actions (0 = full speed)
        /// </summary>
        public static float GetSlowMo()
        {
            return float.TryParse(_config?["Playwright:SlowMo"], out var result) ? result : 0;
        }

        /// <summary>
        /// How long the interactive sign-in waits for the user to finish login + MFA
        /// </summary>
        public static int GetSignInTimeoutSeconds()
        {
            return int.TryParse(_config?["SessionState:SignInTimeoutSeconds"], out var result) ? result : 300;
        }

        /// <summary>
        /// Installed browser channel to use instead of bundled Chromium ("msedge", "chrome"); empty = bundled
        /// </summary>
        public static string GetBrowserChannel()
        {
            return _config?["Playwright:Channel"] ?? string.Empty;
        }

        /// <summary>
        /// Get the browser type (Chromium, Firefox, WebKit) to launch
        /// </summary>
        public static string GetBrowserType()
        {
            return _config?["Playwright:BrowserType"] ?? "Chromium";
        }

        /// <summary>
        /// Get whether to launch the browser in headless mode
        /// </summary>
        public static bool GetHeadless()
        {
            return bool.TryParse(_config?["Playwright:Headless"], out var result) ? result : true;
        }

        /// <summary>
        /// Get the default timeout (in ms) for Playwright operations
        /// </summary>
        public static int GetTimeout()
        {
            return int.TryParse(_config?["Playwright:Timeout"], out var result) ? result : 30000;
        }

        /// <summary>
        /// Get additional browser launch arguments (e.g., --disable-blink-features)
        /// </summary>
        public static IEnumerable<string> GetBrowserArgs()
        {
            var args = _config?.GetSection("Playwright:Args").Get<List<string>>();
            return args ?? new List<string>();
        }

        /// <summary>
        /// Get whether parallel execution is enabled
        /// </summary>
        public static bool IsParallelExecutionEnabled()
        {
            return bool.TryParse(_config?["ParallelExecution:Enabled"], out var result) ? result : false;
        }

        /// <summary>
        /// Get the max degree of parallelism (number of concurrent browser instances)
        /// </summary>
        public static int GetMaxParallelism()
        {
            return int.TryParse(_config?["ParallelExecution:MaxDegreeOfParallelism"], out var result) ? result : 1;
        }

        /// <summary>
        /// Get the session state cache directory
        /// </summary>
        public static string GetSessionStateDirectory()
        {
            var dir = _config?["SessionState:Directory"];
            if (string.IsNullOrEmpty(dir))
                dir = "SessionState";

            // Resolve to an absolute path if relative
            if (!Path.IsPathRooted(dir))
            {
                dir = Path.Combine(AppContext.BaseDirectory, dir);
            }

            return dir;
        }

        /// <summary>
        /// Get whether session state caching is enabled (to reuse bearer tokens / storageState)
        /// </summary>
        public static bool IsSessionStateCachingEnabled()
        {
            return bool.TryParse(_config?["SessionState:EnableCaching"], out var result) ? result : true;
        }

        /// <summary>
        /// Get the session state cache TTL in seconds (default 1 day = 86400)
        /// </summary>
        public static int GetSessionStateCacheTTL()
        {
            return int.TryParse(_config?["SessionState:CacheTTL"], out var result) ? result : 86400;
        }

        /// <summary>
        /// Get the vault token environment variable name (for secrets like credentials, TOTP keys)
        /// </summary>
        public static string GetVaultTokenEnvironmentVariable()
        {
            return _config?["TestAccounts:VaultTokenEnvironmentVariable"] ?? "TESTS_ACCEPTANCE_WEBUI_VAULT_TOKEN";
        }

        /// <summary>
        /// Get the default test account role (e.g., "QAUser", "Admin")
        /// </summary>
        public static string GetDefaultTestAccountRole()
        {
            return _config?["TestAccounts:DefaultRole"] ?? "QAUser";
        }

        /// <summary>
        /// Get a raw configuration value by key (dot-separated path, e.g., "Playwright:Headless")
        /// </summary>
        public static string? GetValue(string key)
        {
            return _config?[key];
        }

        /// <summary>
        /// Get a configuration section as a dictionary (e.g., "Environments")
        /// </summary>
        public static IConfigurationSection? GetSection(string sectionName)
        {
            return _config?.GetSection(sectionName);
        }
    }
}
