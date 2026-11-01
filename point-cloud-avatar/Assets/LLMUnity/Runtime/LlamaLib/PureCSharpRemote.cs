/// @file
/// @brief Pure C# implementation for remote LLM communication without native library dependency.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;

namespace UndreamAI.LlamaLib
{
    /// <summary>
    /// Delegate for streaming callback, compatible with LlamaLib.CharArrayCallback
    /// </summary>
    public delegate void StreamingCallback(string content);

    /// <summary>
    /// Base class for pure C# remote LLM operations.
    /// Handles HTTP communication with llama.cpp server without native library.
    /// </summary>
    public abstract class RemoteLLMBase : IDisposable
    {
        protected readonly HttpClient _httpClient;
        protected readonly string _baseUrl;
        protected readonly string _apiKey;
        protected readonly int _numRetries;
        protected JObject _completionParameters = new JObject();
        protected string _grammar = "";
        protected bool _disposed = false;
        protected CancellationTokenSource _cancellationTokenSource;

        protected RemoteLLMBase(string host, int port, string apiKey = "", int numRetries = 5)
        {
            if (string.IsNullOrEmpty(host))
                throw new ArgumentNullException(nameof(host));

            // Normalize host URL
            string protocol = host.StartsWith("https://") ? "" : (host.StartsWith("http://") ? "" : "http://");
            _baseUrl = $"{protocol}{host}:{port}";
            _apiKey = apiKey ?? "";
            _numRetries = numRetries;

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(10); // Long timeout for LLM responses

            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            }
        }

        /// <summary>
        /// Check if the remote server is alive
        /// </summary>
        public virtual bool IsServerAlive()
        {
            try
            {
                return IsServerAliveAsync().Result;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if the remote server is alive asynchronously
        /// </summary>
        public virtual async Task<bool> IsServerAliveAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Set completion parameters (temperature, top_k, etc.)
        /// </summary>
        public virtual void SetCompletionParameters(JObject parameters)
        {
            _completionParameters = parameters ?? new JObject();
        }

        /// <summary>
        /// Get current completion parameters
        /// </summary>
        public virtual JObject GetCompletionParameters()
        {
            return _completionParameters;
        }

        /// <summary>
        /// Set grammar for structured output
        /// </summary>
        public virtual void SetGrammar(string grammar)
        {
            _grammar = grammar ?? "";
        }

        /// <summary>
        /// Get current grammar
        /// </summary>
        public virtual string GetGrammar()
        {
            return _grammar;
        }

        /// <summary>
        /// Tokenize text into token IDs
        /// </summary>
        public virtual List<int> Tokenize(string content)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentNullException(nameof(content));

            var requestBody = new JObject { ["content"] = content };
            var response = PostJsonAsync($"{_baseUrl}/tokenize", requestBody).Result;

            try
            {
                var result = JObject.Parse(response);
                return result["tokens"]?.ToObject<List<int>>() ?? new List<int>();
            }
            catch
            {
                return new List<int>();
            }
        }

        /// <summary>
        /// Detokenize token IDs back to text
        /// </summary>
        public virtual string Detokenize(List<int> tokens)
        {
            if (tokens == null)
                throw new ArgumentNullException(nameof(tokens));

            var requestBody = new JObject { ["tokens"] = JArray.FromObject(tokens) };
            var response = PostJsonAsync($"{_baseUrl}/detokenize", requestBody).Result;

            try
            {
                var result = JObject.Parse(response);
                return result["content"]?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Get embeddings for text
        /// </summary>
        public virtual List<float> Embeddings(string content)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentNullException(nameof(content));

            var requestBody = new JObject { ["content"] = content };
            var response = PostJsonAsync($"{_baseUrl}/embedding", requestBody).Result;

            try
            {
                var result = JObject.Parse(response);
                return result["embedding"]?.ToObject<List<float>>() ?? new List<float>();
            }
            catch
            {
                return new List<float>();
            }
        }

        /// <summary>
        /// Run completion with optional streaming callback
        /// </summary>
        public virtual string Completion(string prompt, StreamingCallback callback = null, int idSlot = -1)
        {
            return CompletionAsync(prompt, callback, idSlot).Result;
        }

        /// <summary>
        /// Run completion asynchronously with optional streaming callback
        /// </summary>
        public virtual async Task<string> CompletionAsync(string prompt, StreamingCallback callback = null, int idSlot = -1)
        {
            if (string.IsNullOrEmpty(prompt))
                throw new ArgumentNullException(nameof(prompt));

            _cancellationTokenSource = new CancellationTokenSource();

            var requestBody = new JObject
            {
                ["prompt"] = prompt,
                ["stream"] = callback != null
            };

            // Add completion parameters
            foreach (var param in _completionParameters)
            {
                requestBody[param.Key] = param.Value;
            }

            // Add grammar if set
            if (!string.IsNullOrEmpty(_grammar))
            {
                requestBody["grammar"] = _grammar;
            }

            // Add slot if specified
            if (idSlot >= 0)
            {
                requestBody["id_slot"] = idSlot;
            }

            if (callback != null)
            {
                // Streaming mode
                return await StreamCompletionAsync(requestBody, callback, _cancellationTokenSource.Token);
            }
            else
            {
                // Non-streaming mode
                var response = await PostJsonAsync($"{_baseUrl}/completion", requestBody);
                try
                {
                    var result = JObject.Parse(response);
                    return result["content"]?.ToString() ?? "";
                }
                catch
                {
                    return "";
                }
            }
        }

        /// <summary>
        /// Stream completion response with SSE
        /// </summary>
        protected virtual async Task<string> StreamCompletionAsync(JObject requestBody, StreamingCallback callback, CancellationToken cancellationToken)
        {
            var content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/completion")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var fullContent = new StringBuilder();

            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (line.StartsWith("data: "))
                    {
                        var jsonData = line.Substring(6);
                        if (jsonData == "[DONE]")
                            break;

                        try
                        {
                            var data = JObject.Parse(jsonData);
                            var tokenContent = data["content"]?.ToString() ?? "";

                            if (!string.IsNullOrEmpty(tokenContent))
                            {
                                fullContent.Append(tokenContent);
                                callback?.Invoke(tokenContent);
                            }

                            // Check for stop condition
                            if (data["stop"]?.ToObject<bool>() == true)
                                break;
                        }
                        catch
                        {
                            // Skip malformed JSON lines
                        }
                    }
                }
            }

            return fullContent.ToString();
        }

        /// <summary>
        /// Cancel ongoing request
        /// </summary>
        public virtual void Cancel(int idSlot = -1)
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Post JSON to endpoint and return response
        /// </summary>
        protected virtual async Task<string> PostJsonAsync(string url, JObject body)
        {
            int retries = 0;
            Exception lastException = null;

            while (retries <= _numRetries)
            {
                try
                {
                    var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    retries++;
                    if (retries <= _numRetries)
                    {
                        await Task.Delay(100 * retries); // Exponential backoff
                    }
                }
            }

            throw lastException ?? new Exception("Request failed after retries");
        }

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Pure C# implementation of LLMClient for remote server communication.
    /// Drop-in replacement that doesn't require native library.
    /// </summary>
    public class RemoteLLMClient : RemoteLLMBase
    {
        public RemoteLLMClient(string host, int port, string apiKey = "", int numRetries = 5)
            : base(host, port, apiKey, numRetries)
        {
        }

        /// <summary>
        /// Wrapper for LlamaLib.CharArrayCallback compatibility
        /// </summary>
        public string Completion(string prompt, LlamaLib.CharArrayCallback callback = null, int idSlot = -1)
        {
            StreamingCallback wrappedCallback = null;
            if (callback != null)
            {
                // Handle delegate signature difference on IL2CPP platforms
#if ENABLE_IL2CPP
                wrappedCallback = (content) => 
                {
                    IntPtr ptr = Marshal.StringToHGlobalAnsi(content);
                    try
                    {
                        callback(ptr);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                };
#else
                wrappedCallback = (content) => callback(content);
#endif
            }
            return Completion(prompt, wrappedCallback, idSlot);
        }

        /// <summary>
        /// Async wrapper for LlamaLib.CharArrayCallback compatibility
        /// </summary>
        public async Task<string> CompletionAsync(string prompt, LlamaLib.CharArrayCallback callback = null, int idSlot = -1)
        {
            StreamingCallback wrappedCallback = null;
            if (callback != null)
            {
                // Handle delegate signature difference on IL2CPP platforms
#if ENABLE_IL2CPP
                wrappedCallback = (content) => 
                {
                    IntPtr ptr = Marshal.StringToHGlobalAnsi(content);
                    try
                    {
                        callback(ptr);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                };
#else
                wrappedCallback = (content) => callback(content);
#endif
            }
            return await CompletionAsync(prompt, wrappedCallback, idSlot);
        }
    }

    /// <summary>
    /// Pure C# implementation of LLMAgent for remote server communication.
    /// Manages chat history locally without native library.
    /// </summary>
    public class RemoteLLMAgent : RemoteLLMBase
    {
        private List<ChatMessage> _history = new List<ChatMessage>();
        private string _systemPrompt = "";
        private int _slotId = -1;

        public RemoteLLMAgent(RemoteLLMClient client, string systemPrompt = "")
            : base(ExtractHost(client), ExtractPort(client), "", 5)
        {
            _systemPrompt = systemPrompt ?? "";
        }

        public RemoteLLMAgent(string host, int port, string apiKey = "", string systemPrompt = "", int numRetries = 5)
            : base(host, port, apiKey, numRetries)
        {
            _systemPrompt = systemPrompt ?? "";
        }

        private static string ExtractHost(RemoteLLMClient client)
        {
            // Extract host from client's base URL
            var field = typeof(RemoteLLMBase).GetField("_baseUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var baseUrl = field?.GetValue(client) as string ?? "localhost";
            var uri = new Uri(baseUrl);
            return uri.Host;
        }

        private static int ExtractPort(RemoteLLMClient client)
        {
            var field = typeof(RemoteLLMBase).GetField("_baseUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var baseUrl = field?.GetValue(client) as string ?? "http://localhost:13333";
            var uri = new Uri(baseUrl);
            return uri.Port;
        }

        /// <summary>
        /// Slot ID for this agent
        /// </summary>
        public int SlotId
        {
            get => _slotId;
            set => _slotId = value;
        }

        /// <summary>
        /// System prompt for this agent
        /// </summary>
        public string SystemPrompt
        {
            get => _systemPrompt;
            set => _systemPrompt = value ?? "";
        }

        /// <summary>
        /// Get chat history
        /// </summary>
        public List<ChatMessage> GetHistory()
        {
            return new List<ChatMessage>(_history);
        }

        /// <summary>
        /// Set chat history
        /// </summary>
        public void SetHistory(List<ChatMessage> messages)
        {
            _history = messages != null ? new List<ChatMessage>(messages) : new List<ChatMessage>();
        }

        /// <summary>
        /// Clear chat history
        /// </summary>
        public void ClearHistory()
        {
            _history.Clear();
        }

        /// <summary>
        /// Add user message to history
        /// </summary>
        public void AddUserMessage(string content)
        {
            _history.Add(new ChatMessage("user", content ?? ""));
        }

        /// <summary>
        /// Add assistant message to history
        /// </summary>
        public void AddAssistantMessage(string content)
        {
            _history.Add(new ChatMessage("assistant", content ?? ""));
        }

        /// <summary>
        /// Remove last message from history
        /// </summary>
        public void RemoveLastMessage()
        {
            if (_history.Count > 0)
            {
                _history.RemoveAt(_history.Count - 1);
            }
        }

        /// <summary>
        /// Get history size
        /// </summary>
        public int GetHistorySize()
        {
            return _history.Count;
        }

        /// <summary>
        /// Save history to file
        /// </summary>
        public void SaveHistory(string filepath)
        {
            if (string.IsNullOrEmpty(filepath))
                throw new ArgumentNullException(nameof(filepath));

            var historyArray = new JArray();
            foreach (var message in _history)
            {
                historyArray.Add(message.ToJson());
            }

            File.WriteAllText(filepath, historyArray.ToString());
        }

        /// <summary>
        /// Load history from file
        /// </summary>
        public void LoadHistory(string filepath)
        {
            if (string.IsNullOrEmpty(filepath))
                throw new ArgumentNullException(nameof(filepath));

            if (!File.Exists(filepath))
                throw new FileNotFoundException($"History file not found: {filepath}");

            var json = File.ReadAllText(filepath);
            var historyArray = JArray.Parse(json);

            _history.Clear();
            foreach (var item in historyArray)
            {
                if (item is JObject messageObj)
                {
                    _history.Add(ChatMessage.FromJson(messageObj));
                }
            }
        }

        /// <summary>
        /// Build the full prompt with system prompt and chat history
        /// </summary>
        protected virtual string BuildPrompt(string userMessage, bool addToHistory)
        {
            var messages = new JArray();

            // Add system prompt if set
            if (!string.IsNullOrEmpty(_systemPrompt))
            {
                messages.Add(new JObject { ["role"] = "system", ["content"] = _systemPrompt });
            }

            // Add history
            foreach (var msg in _history)
            {
                messages.Add(msg.ToJson());
            }

            // Add current user message
            if (!string.IsNullOrEmpty(userMessage))
            {
                messages.Add(new JObject { ["role"] = "user", ["content"] = userMessage });
            }

            return messages.ToString();
        }

        /// <summary>
        /// Chat with the LLM
        /// </summary>
        public string Chat(string userPrompt, bool addToHistory = true, LlamaLib.CharArrayCallback callback = null, bool returnResponseJson = false, bool debugPrompt = false)
        {
            return ChatAsync(userPrompt, addToHistory, callback, returnResponseJson, debugPrompt).Result;
        }

        /// <summary>
        /// Chat with the LLM asynchronously
        /// </summary>
        public async Task<string> ChatAsync(string userPrompt, bool addToHistory = true, LlamaLib.CharArrayCallback callback = null, bool returnResponseJson = false, bool debugPrompt = false)
        {
            _cancellationTokenSource = new CancellationTokenSource();

            // Build request for chat completion
            var messages = new JArray();

            // Add system prompt
            if (!string.IsNullOrEmpty(_systemPrompt))
            {
                messages.Add(new JObject { ["role"] = "system", ["content"] = _systemPrompt });
            }

            // Add history
            foreach (var msg in _history)
            {
                messages.Add(msg.ToJson());
            }

            // Add current user message
            if (!string.IsNullOrEmpty(userPrompt))
            {
                messages.Add(new JObject { ["role"] = "user", ["content"] = userPrompt });

                if (addToHistory)
                {
                    _history.Add(new ChatMessage("user", userPrompt));
                }
            }

            var requestBody = new JObject
            {
                ["messages"] = messages,
                ["stream"] = callback != null
            };

            // Add completion parameters
            foreach (var param in _completionParameters)
            {
                requestBody[param.Key] = param.Value;
            }

            // Add grammar if set
            if (!string.IsNullOrEmpty(_grammar))
            {
                requestBody["grammar"] = _grammar;
            }

            // Add slot if specified
            if (_slotId >= 0)
            {
                requestBody["id_slot"] = _slotId;
            }

            string response;
            if (callback != null)
            {
                // Streaming mode
                StreamingCallback wrappedCallback;
#if ENABLE_IL2CPP
                wrappedCallback = (content) => 
                {
                    IntPtr ptr = Marshal.StringToHGlobalAnsi(content);
                    try
                    {
                        callback(ptr);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                };
#else
                wrappedCallback = (content) => callback(content);
#endif
                response = await StreamChatAsync(requestBody, wrappedCallback, _cancellationTokenSource.Token);
            }
            else
            {
                // Non-streaming mode - try /v1/chat/completions first, fallback to /completion
                try
                {
                    var httpResponse = await PostJsonAsync($"{_baseUrl}/v1/chat/completions", requestBody);
                    var result = JObject.Parse(httpResponse);
                    response = result["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
                }
                catch
                {
                    // Fallback to /completion endpoint with prompt
                    var promptBody = new JObject
                    {
                        ["prompt"] = messages.ToString(),
                        ["stream"] = false
                    };
                    foreach (var param in _completionParameters)
                    {
                        promptBody[param.Key] = param.Value;
                    }
                    if (!string.IsNullOrEmpty(_grammar))
                    {
                        promptBody["grammar"] = _grammar;
                    }

                    var httpResponse = await PostJsonAsync($"{_baseUrl}/completion", promptBody);
                    var result = JObject.Parse(httpResponse);
                    response = result["content"]?.ToString() ?? "";
                }
            }

            // Add assistant response to history
            if (addToHistory && !string.IsNullOrEmpty(response))
            {
                _history.Add(new ChatMessage("assistant", response));
            }

            return response;
        }

        /// <summary>
        /// Stream chat completion response
        /// </summary>
        protected virtual async Task<string> StreamChatAsync(JObject requestBody, StreamingCallback callback, CancellationToken cancellationToken)
        {
            var content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json");

            // Try /v1/chat/completions first
            string endpoint = $"{_baseUrl}/v1/chat/completions";

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                // Fallback to regular completion endpoint
                return await StreamCompletionAsync(requestBody, callback, cancellationToken);
            }

            var fullContent = new StringBuilder();

            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (line.StartsWith("data: "))
                    {
                        var jsonData = line.Substring(6);
                        if (jsonData == "[DONE]")
                            break;

                        try
                        {
                            var data = JObject.Parse(jsonData);
                            var delta = data["choices"]?[0]?["delta"]?["content"]?.ToString() ?? "";

                            if (!string.IsNullOrEmpty(delta))
                            {
                                fullContent.Append(delta);
                                callback?.Invoke(delta);
                            }

                            // Check for finish reason
                            var finishReason = data["choices"]?[0]?["finish_reason"]?.ToString();
                            if (!string.IsNullOrEmpty(finishReason) && finishReason != "null")
                                break;
                        }
                        catch
                        {
                            // Skip malformed JSON lines
                        }
                    }
                }
            }

            return fullContent.ToString();
        }
    }
}
