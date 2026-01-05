using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading; // 引入线程支持
using System.Threading.Tasks;

namespace domakin
{
    public class GeminiClient
    {
        private readonly HttpClient _httpClient;
        // 使用 -latest 以确保总是能找到模型
        private const string ApiUrlBase = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent";

        public GeminiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15); // 设置超时
        }

        // 增加 CancellationToken 参数
        public async Task<string> TranslateAsync(string apiKey, string text, string targetLang, CancellationToken token)
        {
            if (string.IsNullOrEmpty(apiKey)) return "Key未设置";

            var cleanKey = apiKey.Trim();
            var url = $"{ApiUrlBase}?key={cleanKey}";
            // 在 TranslateAsync 方法中，修改 systemPrompt
            var systemPrompt = $"你是一个FF14辅助翻译。请将日文聊天翻译成【繁体中文 (Traditional Chinese)】。\n" +
                               "重要规则：\n" +
                               "1. 这是一个国际服客户端，字库不支持简体字。请务必使用繁体字或日语通用汉字。\n" + // <--- 关键指令
                               "2. 如果是多行文本（宏/Macro），请保留原有的换行和大致的ASCII符号位置，只翻译其中的日语文本。\n" +
                               "3. 使用FF14国服通用术语（但用繁体表述，如：坦克、機制、散開）。\n" +
                               "4. 不要废话，直接输出结果。";
            
            var requestBody = new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[] { new { parts = new[] { new { text = text } } } }
            };

            var jsonContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            try
            {
                // 将 token 传递给 PostAsync，如果插件关闭，这里会直接抛出取消异常并停止
                var response = await _httpClient.PostAsync(url, jsonContent, token);

                if (!response.IsSuccessStatusCode)
                {
                    // 简单返回错误码，不把整个HTML吐出来
                    return $"Err: {response.StatusCode}";
                }

                var responseString = await response.Content.ReadAsStringAsync(token);
                var json = JObject.Parse(responseString);
                var translatedText = json["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                return translatedText?.Trim() ?? "...";
            }
            catch (OperationCanceledException)
            {
                return null; // 任务被取消，什么都不做
            }
            catch (Exception ex)
            {
                return $"Ex: {ex.Message}";
            }
        }
        public async Task<string> TranslateToJapaneseAsync(string apiKey, string cnText, CancellationToken token)
        {
            if (string.IsNullOrEmpty(apiKey)) return "Key未设置";

            var cleanKey = apiKey.Trim();
            var url = $"{ApiUrlBase}?key={cleanKey}";

            // 专门针对 中 -> 日 的提示词
            var systemPrompt = "你是一个日语母语的FF14玩家。请将输入的中文聊天内容翻译成自然的日文。\n" +
                               "规则：\n" +
                               "1. 语气：使用标准的礼貌语 (Desu/Masu) 或稍微轻松的游戏用语，不要太生硬。\n" +
                               "2. 术语转换：将中文黑话转换为日服习惯用语 (例如: 副本->ID, 机制->ギミック, 坦克->タンク, 奶妈->ヒーラー, 宏->マクロ)。\n" +
                               "3. 保持简洁，直接输出日文翻译结果，不要包含解释。";

            var requestBody = new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[] { new { parts = new[] { new { text = cnText } } } }
            };

            var jsonContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, jsonContent, token);

                if (!response.IsSuccessStatusCode) return $"Err: {response.StatusCode}";

                var responseString = await response.Content.ReadAsStringAsync(token);
                var json = JObject.Parse(responseString);
                var translatedText = json["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                return translatedText?.Trim() ?? "...";
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                return $"Ex: {ex.Message}";
            }
        }
    }
}
