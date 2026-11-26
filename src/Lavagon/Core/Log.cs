using System;
using System.IO;

namespace Lavagon.Core
{
    public static class Log
    {
        private static readonly object Sync = new object();
        private static string FilePath => Path.Combine(AppPaths.Logs ?? Path.GetTempPath(), "lavagon.log");

        public static void Info(string message) => Write("INFO ", message);

        public static void Error(Exception ex)
        {
            if (ex != null) Write("ERROR", ex.ToString());
        }

        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}\r\n");
                }
            }
            catch
            {
                // Logging must never break the app.
            }
        }
    }

    /// <summary>Error with a message meant for the user (shown as-is in a dialog).</summary>
    public class LavagonException : Exception
    {
        public LavagonException(string message) : base(message) { }
    }
}
