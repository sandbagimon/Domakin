using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace domakin // <--- 已修改
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Configuration configuration;

        public ConfigWindow(Configuration config) : base("Domakin 翻译设置")
        {
            this.Size = new Vector2(400, 200);
            this.SizeCondition = ImGuiCond.FirstUseEver;
            this.configuration = config;
        }

        public void Dispose() { }

        public override void Draw()
        {
            ImGui.Text("Google Gemini API 设置");
            ImGui.Separator();
            ImGui.Spacing();
            
            var enabled = configuration.IsEnabled;
            if (ImGui.Checkbox("启用翻译", ref enabled))
            {
                configuration.IsEnabled = enabled;
                configuration.Save();
            }

            ImGui.Spacing();

            ImGui.Text("API Key (Google AI Studio):");
            var key = configuration.GeminiApiKey;
            
            if (ImGui.InputText("##apikey", ref key, 100, ImGuiInputTextFlags.Password)) 
            {
                configuration.GeminiApiKey = key;
                configuration.Save();
            }
            
            ImGui.Spacing();
            if (ImGui.Button("保存并关闭"))
            {
                this.IsOpen = false;
                configuration.Save();
            }
        }
    }
}
