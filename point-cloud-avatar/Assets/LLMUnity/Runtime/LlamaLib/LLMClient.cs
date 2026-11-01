using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UndreamAI.LlamaLib
{
    /// <summary>
    /// LLM Client that supports both native library and pure C# remote modes.
    /// When UseNativeForRemote is false (default), remote connections bypass native library.
    /// </summary>
    public class LLMClient : LLMLocal
    {
        /// <summary>
        /// Set to true to use native library for remote connections (original behavior).
        /// Set to false (default) to use pure C# HTTP client for remote connections.
        /// </summary>
        public static bool UseNativeForRemote = false;

        // Pure C# remote client (used when UseNativeForRemote = false)
        private RemoteLLMClient _pureRemoteClient;
        private bool _isRemoteMode = false;

        public LLMClient(LLMProvider provider)
        {
            if (provider.disposed)
                throw new ObjectDisposedException(nameof(provider));

            llamaLib = provider.llamaLib;
            llm = CreateClient(provider);
            _isRemoteMode = false;
        }

        public LLMClient(string url, int port, string apiKey = "", int numRetries = 5)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException(nameof(url));

            _isRemoteMode = true;

            if (!UseNativeForRemote)
            {
                // Use pure C# implementation - no native library loaded
                _pureRemoteClient = new RemoteLLMClient(url, port, apiKey, numRetries);
                return;
            }

            // Original native implementation
            try
            {
                llamaLib = new LlamaLib(false);
                llm = CreateRemoteClient(url, port, apiKey, numRetries);
            }
            catch
            {
                llamaLib?.Dispose();
                throw;
            }
        }

        private IntPtr CreateClient(LLMProvider provider)
        {
            var llm = llamaLib.LLMClient_Construct(provider.llm);
            if (llm == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create LLMClient");
            return llm;
        }

        private IntPtr CreateRemoteClient(string url, int port, string apiKey = "", int numRetries = 5)
        {
            var llm = llamaLib.LLMClient_Construct_Remote(url ?? string.Empty, port, apiKey ?? string.Empty, numRetries);
            if (llm == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to create remote LLMClient for {url}:{port}");
            return llm;
        }

        public void SetSSL(string SSL_cert)
        {
            if (_pureRemoteClient != null)
            {
                // SSL not directly supported in pure C# mode - HttpClient handles it automatically
                return;
            }
            llamaLib.LLMClient_Set_SSL(llm, SSL_cert ?? string.Empty);
        }

        public bool IsServerAlive()
        {
            if (_pureRemoteClient != null)
            {
                return _pureRemoteClient.IsServerAlive();
            }
            return llamaLib.LLMClient_Is_Server_Alive(llm);
        }

        public async Task<bool> IsServerAliveAsync()
        {
            if (_pureRemoteClient != null)
            {
                return await _pureRemoteClient.IsServerAliveAsync();
            }
            return await Task.Run(() => llamaLib.LLMClient_Is_Server_Alive(llm));
        }

        // Override base class methods to use pure C# client when applicable

        public new List<int> Tokenize(string content)
        {
            if (_pureRemoteClient != null)
            {
                return _pureRemoteClient.Tokenize(content);
            }
            return base.Tokenize(content);
        }

        public new string Detokenize(List<int> tokens)
        {
            if (_pureRemoteClient != null)
            {
                return _pureRemoteClient.Detokenize(tokens);
            }
            return base.Detokenize(tokens);
        }

        public new List<float> Embeddings(string content)
        {
            if (_pureRemoteClient != null)
            {
                return _pureRemoteClient.Embeddings(content);
            }
            return base.Embeddings(content);
        }

        public new void SetCompletionParameters(JObject parameters)
        {
            if (_pureRemoteClient != null)
            {
                _pureRemoteClient.SetCompletionParameters(parameters);
                return;
            }
            base.SetCompletionParameters(parameters);
        }

        public new JObject GetCompletionParameters()
        {
            if (_pureRemoteClient != null)
            {
                return _pureRemoteClient.GetCompletionParameters();
            }
            return base.GetCompletionParameters();
        }

        public new void SetGrammar(string grammar)
        {
            if (_pureRemoteClient != null)
            {
                _pureRemoteClient.SetGrammar(grammar);
                return;
            }
            base.SetGrammar(grammar);
        }

        public new string GetGrammar()
        {
            if (_pureRemoteClient != null)
            {
                return _pureRemoteClient.GetGrammar();
            }
            return base.GetGrammar();
        }

        public new string Completion(string prompt, LlamaLib.CharArrayCallback callback = null, int idSlot = -1)
        {
            if (_pureRemoteClient != null)
            {
                return _pureRemoteClient.Completion(prompt, callback, idSlot);
            }
            return base.Completion(prompt, callback, idSlot);
        }

        public new async Task<string> CompletionAsync(string prompt, LlamaLib.CharArrayCallback callback = null, int idSlot = -1)
        {
            if (_pureRemoteClient != null)
            {
                return await _pureRemoteClient.CompletionAsync(prompt, callback, idSlot);
            }
            return await base.CompletionAsync(prompt, callback, idSlot);
        }

        public new void Cancel(int idSlot)
        {
            if (_pureRemoteClient != null)
            {
                _pureRemoteClient.Cancel(idSlot);
                return;
            }
            base.Cancel(idSlot);
        }

        public override void Dispose()
        {
            if (_pureRemoteClient != null)
            {
                _pureRemoteClient.Dispose();
                _pureRemoteClient = null;
            }
            base.Dispose();
        }

        /// <summary>
        /// Check if this client is using pure C# remote mode
        /// </summary>
        public bool IsPureCSharpRemote => _pureRemoteClient != null;

        /// <summary>
        /// Get the underlying pure C# remote client (for advanced usage)
        /// </summary>
        public RemoteLLMClient PureRemoteClient => _pureRemoteClient;
    }
}