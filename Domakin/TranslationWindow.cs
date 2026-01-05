using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace domakin
{
    public class TranslationWindow : Window, IDisposable
    {
        private readonly GeminiClient _geminiClient;
        private readonly Configuration _config;

        // 输入框的变量
        private string _inputText = "";
        // 输出结果的变量
        private string _outputText = "";
        
        private bool _isTranslating = false;
        private CancellationTokenSource? _cts;

        public TranslationWindow(GeminiClient client, Configuration config) : base("Domakin 中日翻译机")
        {
            this.Size = new Vector2(400, 300);
            this.SizeCondition = ImGuiCond.FirstUseEver;
            _geminiClient = client;
            _config = config;
        }

        public void Dispose()
        {
            _cts?.Cancel();
        }

        public override void Draw()
        {
            ImGui.Text("输入简体中文:");
            
            // 多行输入框
            // ##Input 隐藏标签，只显示框
            ImGui.InputTextMultiline("##Input", ref _inputText, 1000, new Vector2(-1, 80));

            ImGui.Spacing();

            // 翻译按钮
            if (ImGui.Button("翻译成日文 (Translate)"))
            {
                if (!string.IsNullOrWhiteSpace(_inputText) && !_isTranslating)
                {
                    DoTranslate();
                }
            }

            // 加个加载中的旋转圈圈
            if (_isTranslating)
            {
                ImGui.SameLine();
                ImGui.Text(" 正在思考...");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("日文结果:");
            
            // 结果显示框 (只读)
            ImGui.InputTextMultiline("##Output", ref _outputText, 2000, new Vector2(-1, 80), ImGuiInputTextFlags.ReadOnly);

            ImGui.Spacing();

            // 复制按钮
            if (ImGui.Button("复制结果 (Copy)"))
            {
                if (!string.IsNullOrEmpty(_outputText))
                {
                    ImGui.SetClipboardText(_outputText);
                }
            }
            
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "复制后直接在游戏聊天框 Ctrl+V");
        }

        private void DoTranslate()
        {
            _isTranslating = true;
            _outputText = "翻译中...";
            
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    string result = await _geminiClient.TranslateToJapaneseAsync(
                        _config.GeminiApiKey, 
                        _inputText, 
                        _cts.Token
                    );

                    // 只有当没有被取消时才更新UI
                    if (!_cts.Token.IsCancellationRequested)
                    {
                        _outputText = result ?? "翻译失败";
                    }
                }
                finally
                {
                    _isTranslating = false;
                }
            });
        }
    }
}
