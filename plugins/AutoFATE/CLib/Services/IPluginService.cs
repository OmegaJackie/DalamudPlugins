namespace AutoFATE.CLib.Services;

/// <summary>
/// Marker for plugin singletons auto-registered in <see cref="CLibMain.Init"/>.
/// Disposed in <see cref="CLibMain.Dispose"/> / <see cref="CLibMain.DisposeAsync"/>.
/// Types that also implement <c>IPluginConfiguration</c> are loaded from the plugin config file when present.
/// </summary>
public interface IPluginService {
    /// <remarks>Initialises in ascending order.</remarks>
    int InitOrder => 0;
}
