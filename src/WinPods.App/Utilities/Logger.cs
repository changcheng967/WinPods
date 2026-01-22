using System.IO;

namespace WinPods.App.Utilities
{
    /// <summary>
    /// Simple file logger for debugging.
    /// </summary>
    public static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "WinPods.log");

        private static readonly object _lock = new object();

        static Logger()
        {
            // Clear log on startup
            try
            {
                File.WriteAllText(LogFilePath, $"=== WinPods Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
            }
            catch
            {
                // Ignore errors
            }
        }

        public static void Log(string message)
        {
            lock (_lock)
            {
                try
                {
                    string timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                    File.AppendAllText(LogFilePath, timestampedMessage + "\n");

                    // Also output to debug
                    System.Diagnostics.Debug.WriteLine(timestampedMessage);
                }
                catch
                {
                    // Ignore logging errors
                }
            }
        }

        public static void LogImportant(string message)
        {
            string importantMessage = $"*** {message} ***";
            Log(importantMessage);
        }
    }
}
