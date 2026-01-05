using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace domakin // <--- 已修改
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;
        public string GeminiApiKey { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        
        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface!.SavePluginConfig(this);
        }
    }
}
