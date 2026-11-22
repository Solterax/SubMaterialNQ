using Dalamud.Configuration;

namespace SubMaterialNQ;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public HashSet<uint> Whitelist { get; set; } = [];
}
