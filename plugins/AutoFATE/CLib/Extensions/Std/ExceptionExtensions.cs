using System.Text;

namespace AutoFATE.CLib.Extensions;

public static class ExceptionExtensions {
    extension(Exception ex) {
        public void Log(string message) => IPluginLog.Get().Error(ex, message);
        public void Log() => IPluginLog.Get().Error(ex, GetFullMessage(ex));

        public void LogVerbose(string message) => IPluginLog.Get().Verbose(ex, message);
        public void LogVerbose() => IPluginLog.Get().Verbose(ex, GetFullMessage(ex));

        public void LogDebug(string message) => IPluginLog.Get().Debug(ex, message);
        public void LogDebug() => IPluginLog.Get().Debug(ex, GetFullMessage(ex));

        public void LogInfo(string message) => IPluginLog.Get().Info(ex, message);
        public void LogInfo() => IPluginLog.Get().Info(ex, GetFullMessage(ex));

        public void LogWarning(string message) => IPluginLog.Get().Warning(ex, message);
        public void LogWarning() => IPluginLog.Get().Warning(ex, GetFullMessage(ex));

        public void LogFatal(string message) => IPluginLog.Get().Fatal(ex, message);
        public void LogFatal() => IPluginLog.Get().Fatal(ex, GetFullMessage(ex));

        public void DuoLog() => DuoLog(ex, GetFullMessage(ex));
        public void DuoLog(string message) {
            IChatGui.Get().PrintError(message);
            ex.Log(message);
        }

        private string GetFullMessage() {
            var sb = new StringBuilder($"{ex.Message}\n{ex.StackTrace}");
            var inner = ex.InnerException;
            while (inner != null) {
                sb.Append($"\nAn inner exception was thrown: {inner.Message}\n{inner.StackTrace}");
                inner = inner.InnerException;
            }
            return sb.ToString();
        }
    }
}
