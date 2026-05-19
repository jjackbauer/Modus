namespace Modus.Core.Plugins;

public interface IPluginLifecycle
{
    void Load(PluginLoadContext context);
    void Start(PluginStartContext context);
    void Stop(PluginStopContext context);
    void Unload(PluginUnloadContext context);
}