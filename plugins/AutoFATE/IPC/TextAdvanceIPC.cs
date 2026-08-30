using Dalamud.Plugin.Ipc;

namespace AutoFATE.IPC;

public sealed class TextAdvanceIPC : IPluginService {
    public int InitOrder => 10;

    public string Name => "TextAdvance";
    public bool IsLoaded => Svc.Interface.IsPluginLoaded(Name);

    private readonly ICallGateSubscriber<string, ExternalTerritoryConfig, bool> _enableExternalControl;
    private readonly ICallGateSubscriber<string, bool> _disableExternalControl;
    private readonly ICallGateSubscriber<bool> _isInExternalControl;

    public TextAdvanceIPC() {
        _enableExternalControl = Svc.Interface.GetIpcSubscriber<string, ExternalTerritoryConfig, bool>("TextAdvance.EnableExternalControl");
        _disableExternalControl = Svc.Interface.GetIpcSubscriber<string, bool>("TextAdvance.DisableExternalControl");
        _isInExternalControl = Svc.Interface.GetIpcSubscriber<bool>("TextAdvance.IsInExternalControl");
    }

    public bool EnableExternalControl(string pluginName, ExternalTerritoryConfig config)
        => _enableExternalControl.HasFunction && _enableExternalControl.InvokeFunc(pluginName, config);

    public bool DisableExternalControl(string pluginName)
        => _disableExternalControl.HasFunction && _disableExternalControl.InvokeFunc(pluginName);

    public bool IsInExternalControl()
        => _isInExternalControl.HasFunction && _isInExternalControl.InvokeFunc();

    public sealed class ExternalTerritoryConfig {
        public bool? EnableQuestAccept;
        public bool? EnableQuestComplete;
        public bool? EnableRewardPick;
        public bool? EnableRequestHandin;
        public bool? EnableCutsceneEsc;
        public bool? EnableCutsceneSkipConfirm;
        public bool? EnableTalkSkip;
        public bool? EnableRequestFill;
        public bool? EnableAutoInteract;
    }
}
