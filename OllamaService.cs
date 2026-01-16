using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace LinuxMcpServer
{
    public class OllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelName;
        private readonly ILogger<OllamaService> _logger;

        public OllamaService(string baseUrl, string modelName, ILogger<OllamaService> logger)
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
            _modelName = modelName;
            _logger = logger;
        }

        public async Task<string> TranslateToCommandAsync(string userPrompt)
        {
            try
            {
                var requestBody = new
                {
                    model = _modelName,
                    prompt = $"You are a Linux command expert. Translate the following natural language request into a single executable Linux shell command. Do not explain. Do not use markdown backticks. Just return the command.\n\nRequest: {userPrompt}\nCommand:",
                    stream = false
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/api/generate", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Ollama API Error: {response.StatusCode}");
                    return userPrompt; // Fallback to original input
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("response", out var responseElement))
                {
                     return responseElement.GetString()?.Trim() ?? userPrompt;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to call Ollama: {ex.Message}");
            }

            return userPrompt;
        }
    }
}
