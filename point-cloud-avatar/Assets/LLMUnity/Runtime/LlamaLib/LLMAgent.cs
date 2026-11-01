using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UndreamAI.LlamaLib
{
    // Data structure for chat messages
    public class ChatMessage
    {
        public string role { get; set; }
        public string content { get; set; }

        public ChatMessage(string _role, string _content)
        {
            role = _role;
            content = _content;
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["role"] = role,
                ["content"] = content
            };
        }

        public static ChatMessage FromJson(JObject json)
        {
            return new ChatMessage(
                json["role"]?.ToString() ?? string.Empty,
                json["content"]?.ToString() ?? string.Empty
            );
        }

        public override bool Equals(object obj)
        {
            if (obj is not ChatMessage other)
                return false;
            return role == other.role && content == other.content;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (role?.GetHashCode() ?? 0);
                hash = hash * 23 + (content?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{role}: {content}";
        }
    }

    /// <summary>
    /// LLMAgent class that supports both native library and pure C# remote modes.
    /// When the underlying LLMClient uses pure C# mode, this agent delegates to RemoteLLMAgent.
    /// </summary>
    public class LLMAgent : LLMLocal
    {
        private LLMLocal llmBase;

        // Pure C# remote agent (used when underlying client is in pure C# mode)
        private RemoteLLMAgent _pureRemoteAgent;

        public LLMAgent(LLMLocal _llm, string _systemPrompt = "")
        {
            if (_llm == null)
                throw new ArgumentNullException(nameof(_llm));

            // Check if the underlying client is using pure C# remote mode
            if (_llm is LLMClient client && client.IsPureCSharpRemote)
            {
                // Use pure C# implementation
                _pureRemoteAgent = new RemoteLLMAgent(client.PureRemoteClient, _systemPrompt);
                return;
            }

            // Original native implementation
            if (_llm.disposed)
                throw new ObjectDisposedException(nameof(_llm));

            llmBase = _llm;
            llamaLib = llmBase.llamaLib;

            llm = llamaLib.LLMAgent_Construct(llmBase.llm, _systemPrompt ?? string.Empty);
            if (llm == IntPtr.Zero) throw new InvalidOperationException("Failed to create LLMAgent");
        }

        /// <summary>
        /// Check if this agent is using pure C# remote mode
        /// </summary>
        public bool IsPureCSharpRemote => _pureRemoteAgent != null;

        // Properties
        public int SlotId
        {
            get
            {
                if (_pureRemoteAgent != null)
                    return _pureRemoteAgent.SlotId;
                CheckLlamaLib();
                return llamaLib.LLMAgent_Get_Slot(llm);
            }
            set
            {
                if (_pureRemoteAgent != null)
                {
                    _pureRemoteAgent.SlotId = value;
                    return;
                }
                CheckLlamaLib();
                llamaLib.LLMAgent_Set_Slot(llm, value);
            }
        }

        public string SystemPrompt
        {
            get
            {
                if (_pureRemoteAgent != null)
                    return _pureRemoteAgent.SystemPrompt;
                CheckLlamaLib();
                return Marshal.PtrToStringAnsi(llamaLib.LLMAgent_Get_System_Prompt(llm)) ?? "";
            }
            set
            {
                if (_pureRemoteAgent != null)
                {
                    _pureRemoteAgent.SystemPrompt = value ?? "";
                    return;
                }
                CheckLlamaLib();
                llamaLib.LLMAgent_Set_System_Prompt(llm, value ?? string.Empty);
            }
        }

        // History management
        public JArray History
        {
            get
            {
                if (_pureRemoteAgent != null)
                {
                    var historyArray = new JArray();
                    foreach (var msg in _pureRemoteAgent.GetHistory())
                    {
                        historyArray.Add(msg.ToJson());
                    }
                    return historyArray;
                }
                CheckLlamaLib();
                IntPtr result = llamaLib.LLMAgent_Get_History(llm);
                string historyStr = Marshal.PtrToStringAnsi(result) ?? "[]";
                try
                {
                    return JArray.Parse(historyStr);
                }
                catch
                {
                    return new JArray();
                }
            }
            set
            {
                if (_pureRemoteAgent != null)
                {
                    var messages = new List<ChatMessage>();
                    foreach (var item in value)
                    {
                        if (item is JObject messageObj)
                        {
                            messages.Add(ChatMessage.FromJson(messageObj));
                        }
                    }
                    _pureRemoteAgent.SetHistory(messages);
                    return;
                }
                CheckLlamaLib();
                string historyJson = value?.ToString() ?? "[]";
                llamaLib.LLMAgent_Set_History(llm, historyJson);
            }
        }

        public List<ChatMessage> GetHistory()
        {
            if (_pureRemoteAgent != null)
                return _pureRemoteAgent.GetHistory();

            var history = History;
            var messages = new List<ChatMessage>();

            try
            {
                foreach (var item in history)
                {
                    if (item is JObject messageObj)
                    {
                        messages.Add(ChatMessage.FromJson(messageObj));
                    }
                }
            }
            catch {}

            return messages;
        }

        public void SetHistory(List<ChatMessage> messages)
        {
            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.SetHistory(messages);
                return;
            }

            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            var historyArray = new JArray();
            foreach (var message in messages)
            {
                historyArray.Add(message.ToJson());
            }
            History = historyArray;
        }

        public void ClearHistory()
        {
            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.ClearHistory();
                return;
            }
            CheckLlamaLib();
            llamaLib.LLMAgent_Clear_History(llm);
        }

        public void AddUserMessage(string content)
        {
            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.AddUserMessage(content);
                return;
            }
            CheckLlamaLib();
            llamaLib.LLMAgent_Add_User_Message(llm, content ?? string.Empty);
        }

        public void AddAssistantMessage(string content)
        {
            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.AddAssistantMessage(content);
                return;
            }
            CheckLlamaLib();
            llamaLib.LLMAgent_Add_Assistant_Message(llm, content ?? string.Empty);
        }

        public void RemoveLastMessage()
        {
            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.RemoveLastMessage();
                return;
            }
            CheckLlamaLib();
            llamaLib.LLMAgent_Remove_Last_Message(llm);
        }

        public void SaveHistory(string filepath)
        {
            if (string.IsNullOrEmpty(filepath))
                throw new ArgumentNullException(nameof(filepath));

            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.SaveHistory(filepath);
                return;
            }
            CheckLlamaLib();
            llamaLib.LLMAgent_Save_History(llm, filepath ?? string.Empty);
        }

        public void LoadHistory(string filepath)
        {
            if (string.IsNullOrEmpty(filepath))
                throw new ArgumentNullException(nameof(filepath));

            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.LoadHistory(filepath);
                return;
            }
            CheckLlamaLib();
            llamaLib.LLMAgent_Load_History(llm, filepath ?? string.Empty);
        }

        public int GetHistorySize()
        {
            if (_pureRemoteAgent != null)
                return _pureRemoteAgent.GetHistorySize();
            CheckLlamaLib();
            return llamaLib.LLMAgent_Get_History_Size(llm);
        }

        // Chat functionality
        public string Chat(string userPrompt, bool addToHistory = true, LlamaLib.CharArrayCallback callback = null, bool returnResponseJson = false, bool debugPrompt = false)
        {
            if (_pureRemoteAgent != null)
                return _pureRemoteAgent.Chat(userPrompt, addToHistory, callback, returnResponseJson, debugPrompt);

            CheckLlamaLib();
            IntPtr result = llamaLib.LLMAgent_Chat(llm, userPrompt ?? string.Empty, addToHistory, callback, returnResponseJson, debugPrompt);
            return Marshal.PtrToStringAnsi(result) ?? string.Empty;
        }

        public async Task<string> ChatAsync(string userPrompt, bool addToHistory = true, LlamaLib.CharArrayCallback callback = null, bool returnResponseJson = false, bool debugPrompt = false)
        {
            if (_pureRemoteAgent != null)
                return await _pureRemoteAgent.ChatAsync(userPrompt, addToHistory, callback, returnResponseJson, debugPrompt);

            return await Task.Run(() => Chat(userPrompt, addToHistory, callback, returnResponseJson, debugPrompt));
        }

        // Completion parameters
        public new void SetCompletionParameters(JObject parameters)
        {
            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.SetCompletionParameters(parameters);
                return;
            }
            base.SetCompletionParameters(parameters);
        }

        public new JObject GetCompletionParameters()
        {
            if (_pureRemoteAgent != null)
                return _pureRemoteAgent.GetCompletionParameters();
            return base.GetCompletionParameters();
        }

        public new void SetGrammar(string grammar)
        {
            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.SetGrammar(grammar);
                return;
            }
            base.SetGrammar(grammar);
        }

        public new string GetGrammar()
        {
            if (_pureRemoteAgent != null)
                return _pureRemoteAgent.GetGrammar();
            return base.GetGrammar();
        }

        // Override completion methods to use agent-specific implementations
        public string Completion(string prompt, LlamaLib.CharArrayCallback callback = null)
        {
            if (_pureRemoteAgent != null)
            {
                StreamingCallback wrappedCallback = null;
                if (callback != null)
                {
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
                return _pureRemoteAgent.Completion(prompt, wrappedCallback, SlotId);
            }
            return Completion(prompt, callback, SlotId);
        }

        public async Task<string> CompletionAsync(string prompt, LlamaLib.CharArrayCallback callback = null)
        {
            if (_pureRemoteAgent != null)
            {
                StreamingCallback wrappedCallback = null;
                if (callback != null)
                {
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
                return await _pureRemoteAgent.CompletionAsync(prompt, wrappedCallback, SlotId);
            }
            return await Task.Run(() => Completion(prompt, callback));
        }

        public string SaveSlot(string filepath)
        {
            if (string.IsNullOrEmpty(filepath))
                throw new ArgumentNullException(nameof(filepath));

            if (_pureRemoteAgent != null)
            {
                // Slot saving not supported in pure C# remote mode
                // Save history instead
                SaveHistory(filepath + ".history.json");
                return "";
            }
            CheckLlamaLib();
            IntPtr result = llamaLib.LLM_Save_Slot(llm, SlotId, filepath ?? string.Empty);
            return Marshal.PtrToStringAnsi(result) ?? string.Empty;
        }

        public string LoadSlot(string filepath)
        {
            if (string.IsNullOrEmpty(filepath) || !File.Exists(filepath))
                throw new ArgumentNullException(nameof(filepath));

            if (_pureRemoteAgent != null)
            {
                // Slot loading not supported in pure C# remote mode
                // Try loading history instead
                var historyPath = filepath + ".history.json";
                if (File.Exists(historyPath))
                    LoadHistory(historyPath);
                return "";
            }
            CheckLlamaLib();
            IntPtr result = llamaLib.LLM_Load_Slot(llm, SlotId, filepath ?? string.Empty);
            return Marshal.PtrToStringAnsi(result) ?? string.Empty;
        }

        public void Cancel()
        {
            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.Cancel();
                return;
            }
            CheckLlamaLib();
            llamaLib.LLM_Cancel(llm, SlotId);
        }

        public override void Dispose()
        {
            if (_pureRemoteAgent != null)
            {
                _pureRemoteAgent.Dispose();
                _pureRemoteAgent = null;
            }
            base.Dispose();
        }

        // Override slot-based methods to hide them
        private new string SaveSlot(int id_slot, string filepath)
        {
            return SaveSlot(filepath);
        }

        private new string LoadSlot(int id_slot, string filepath)
        {
            return LoadSlot(filepath);
        }

        private new void Cancel(int id_slot)
        {
            Cancel();
        }
    }
}

