namespace AutoFATE.CLib.Extensions;

public static class IPluginLogExtensions {
    extension(IPluginLog log) {
        public void DuoLogError(string message) {
            IChatGui.Get().EchoError(message);
            log.Error(message);
        }
    }
}
