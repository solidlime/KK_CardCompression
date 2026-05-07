using BepInEx.Logging;

namespace Extension
{
    public class Logger
    {
        public static ManualLogSource logger { get => _logger; set => _logger = value; }
        private static ManualLogSource _logger;

        public static void LogDebug(string data) => _logger?.LogDebug(data);
        public static void LogWarning(string data) => _logger?.LogWarning(data);
        public static void LogError(string data) => _logger?.LogError(data);
    }
}
