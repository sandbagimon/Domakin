using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace domakin
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Domakin Translator";
        
        // 指令定义
        private const string CommandName = "/gtrans";      // 打开设置
        private const string InputCommandName = "/gj";     // 打开中翻日窗口

        [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
        [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
        [PluginService] private static IChatGui ChatGui { get; set; } = null!;

        public Configuration Configuration { get; init; }
        public WindowSystem WindowSystem = new("DomakinTranslator");
        
        // 窗口实例
        private ConfigWindow ConfigWindow { get; init; }
        private TranslationWindow TranslationWindow { get; init; } // 新增：翻译窗口
        
        // 核心客户端
        private GeminiClient GeminiClient { get; init; }

        // === 宏处理与线程安全变量 ===
        
        // 1. 全局取消令牌（用于插件卸载/关闭时停止所有网络请求）
        private readonly CancellationTokenSource _shutdownCts = new();
        
        // 2. 防抖计时器令牌（用于宏的缓冲等待）
        private CancellationTokenSource? _debounceCts;
        
        // 3. 消息缓冲区 (发送者, 内容)
        private readonly List<(string Sender, string Message)> _messageBuffer = new();
        
        // 4. 线程锁
        private readonly object _bufferLock = new();

        // 5. 翻译缓存 (避免重复翻译消耗 Token)
        private ConcurrentDictionary<string, string> _translationCache = new();

        public Plugin()
        {
            // 1. 初始化配置
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            // 2. 初始化 API 客户端
            GeminiClient = new GeminiClient();

            // 3. 初始化窗口
            ConfigWindow = new ConfigWindow(Configuration);
            TranslationWindow = new TranslationWindow(GeminiClient, Configuration); // 初始化新窗口

            // 4. 添加窗口到系统
            WindowSystem.AddWindow(ConfigWindow);
            WindowSystem.AddWindow(TranslationWindow);

            // 5. 注册指令
            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "打开翻译设置面板"
            });

            CommandManager.AddHandler(InputCommandName, new CommandInfo(OnInputCommand)
            {
                HelpMessage = "打开[简体中文 -> 日文]翻译窗口"
            });

            // 6. 绑定事件
            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            ChatGui.ChatMessage += OnChatMessage;
        }

        public void Dispose()
        {
            // 1. 停止所有异步任务
            _shutdownCts.Cancel();
            _shutdownCts.Dispose();
            _debounceCts?.Cancel();

            // 2. 移除窗口和指令
            WindowSystem.RemoveAllWindows();
            ConfigWindow.Dispose();
            TranslationWindow.Dispose(); // 别忘了清理新窗口
            
            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(InputCommandName);

            // 3. 解绑事件
            ChatGui.ChatMessage -= OnChatMessage;
            
            // 4. 清理缓存
            _translationCache.Clear();
        }

        // 处理 /gtrans 指令
        private void OnCommand(string command, string args) => ConfigWindow.IsOpen = true;

        // 处理 /gj 指令
        private void OnInputCommand(string command, string args)
        {
            // 简单的切换显示/隐藏
            TranslationWindow.IsOpen = !TranslationWindow.IsOpen;
        }

        private void DrawUI() => WindowSystem.Draw();
        private void DrawConfigUI() => ConfigWindow.IsOpen = true;

        private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            if (!Configuration.IsEnabled) return;
            
            // 频道过滤
            if (type != XivChatType.Party && 
                type != XivChatType.CrossParty && 
                type != XivChatType.Alliance) 
            {
                return;
            }

            string senderName = sender.TextValue;
            string originalText = message.TextValue;

            // 过滤非日语 (只翻译日语 -> 中文)
            if (!ContainsJapanese(originalText)) return;

            // === 宏处理逻辑：缓冲池 ===
            lock (_bufferLock)
            {
                _messageBuffer.Add((senderName, originalText));
                
                // 重置倒计时
                _debounceCts?.Cancel();
                _debounceCts = new CancellationTokenSource();
            }

            // 启动防抖任务 (等待 300ms 后执行翻译)
            _ = RunDebouncedTranslation(_debounceCts.Token);
        }

        private async Task RunDebouncedTranslation(CancellationToken debounceToken)
        {
            try
            {
                // 等待 300ms，让宏发完
                await Task.Delay(300, debounceToken);

                // 取出缓冲区的消息
                List<(string Sender, string Message)> batchToProcess;
                lock (_bufferLock)
                {
                    if (_messageBuffer.Count == 0) return;
                    batchToProcess = new List<(string Sender, string Message)>(_messageBuffer);
                    _messageBuffer.Clear();
                }

                await ProcessBatch(batchToProcess);
            }
            catch (TaskCanceledException)
            {
                // 新消息来了，倒计时被重置，正常忽略
            }
            catch (Exception ex)
            {
                // 仅仅打印错误日志，不要弹窗打扰玩家
                Console.WriteLine($"[Domakin] Buffering Error: {ex.Message}");
            }
        }

        private async Task ProcessBatch(List<(string Sender, string Message)> batch)
        {
            // 拼接宏文本
            var sb = new StringBuilder();
            foreach (var item in batch)
            {
                sb.AppendLine(item.Message);
            }
            string fullText = sb.ToString().Trim();
            string senderName = batch.First().Sender;

            // 查缓存
            if (_translationCache.TryGetValue(fullText, out string cachedTranslation))
            {
                PrintTranslation(senderName, cachedTranslation, true);
                return;
            }

            // 调用 API
            // 注意：这里目标语言写"繁体中文"，配合 GeminiClient 的 Prompt 解决国际服缺字问题
            string result = await GeminiClient.TranslateAsync(
                Configuration.GeminiApiKey, 
                fullText, 
                "繁体中文", 
                _shutdownCts.Token
            );

            if (string.IsNullOrEmpty(result)) return;

            // 存缓存
            _translationCache.TryAdd(fullText, result);

            // 打印
            PrintTranslation(senderName, result, false);
        }

        private void PrintTranslation(string sender, string text, bool isCached)
        {
            var builder = new SeStringBuilder();
            
            // 头部 [Domakin] 绿色
            builder.AddUiForeground(43); 
            builder.AddText("[Domakin] ");
            builder.AddUiForegroundOff();
            
            builder.AddText($"{sender}:\n"); // 强制换行，适应宏显示

            // 翻译内容 淡黄色
            builder.AddUiForeground(50); 
            builder.AddText(text);
            builder.AddUiForegroundOff();

            ChatGui.Print(new XivChatEntry
            {
                Message = builder.BuiltString,
                Type = XivChatType.Echo
            });
        }

        private bool ContainsJapanese(string text)
        {
            foreach (char c in text)
            {
                if ((c >= 0x3040 && c <= 0x309F) || // 平假名
                    (c >= 0x30A0 && c <= 0x30FF) || // 片假名
                    (c >= 0x4E00 && c <= 0x9FBF))   // 汉字
                {
                    return true;
                }
            }
            return false;
        }
    }
}
