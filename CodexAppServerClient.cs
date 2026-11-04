using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexNativeLite
{
    public sealed class ServerMessageEventArgs : EventArgs
    {
        public Dictionary<string, object> Message { get; private set; }
        public ServerMessageEventArgs(Dictionary<string, object> message) { Message = message; }
    }

    public sealed class ServerLogEventArgs : EventArgs
    {
        public string Text { get; private set; }
        public ServerLogEventArgs(string text) { Text = text; }
    }

    public sealed class AccountInfo
    {
        public string Type { get; set; }
        public string Email { get; set; }
        public string PlanType { get; set; }
    }

    public sealed class LoginInfo
    {
        public string LoginId { get; set; }
        public string AuthorizationUrl { get; set; }
    }

    public sealed class ThreadSummary
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Preview { get; set; }
        public string Cwd { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ConversationMessage
    {
        public bool IsUser { get; set; }
        public string Text { get; set; }
    }

    public sealed partial class CodexAppServerClient : IDisposable
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> _pending =
            new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly object _writeLock = new object();
        private Process _process;
        private StreamWriter _input;
        private CancellationTokenSource _stop;
        private Task _outputTask;
        private Task _errorTask;
        private int _nextId;
        private string _lastLogLine;

        public event EventHandler<ServerMessageEventArgs> NotificationReceived;
        public event EventHandler<ServerMessageEventArgs> ServerRequestReceived;
        public event EventHandler<ServerLogEventArgs> LogReceived;
        public event EventHandler Disconnected;

        public string ThreadId { get; private set; }
        public string ActiveTurnId { get; private set; }
        public string ProfileDirectory { get; private set; }
        public string RuntimePath { get; private set; }
        public bool UsesBundledRuntime
        {
            get
            {
                if (String.IsNullOrWhiteSpace(RuntimePath)) return false;
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
                return RuntimePath.StartsWith(local, StringComparison.OrdinalIgnoreCase);
            }
        }
        public bool IsRunning { get { return _process != null && !_process.HasExited; } }
        public long WorkingSetBytes
        {
            get
            {
                try { return IsRunning ? _process.WorkingSet64 : 0L; }
                catch { return 0L; }
            }
        }

        public async Task StartAsync(string workingDirectory)
        {
            if (IsRunning) return;

            _stop = new CancellationTokenSource();
            ProfileDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexNativeLite",
                "profile");
            Directory.CreateDirectory(ProfileDirectory);
            string processDirectory = !String.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
                ? workingDirectory
                : ProfileDirectory;
            RuntimePath = ResolveCodexExecutable();
            var start = new ProcessStartInfo
            {
                FileName = RuntimePath,
                Arguments = "app-server --listen stdio:// -c cli_auth_credentials_store=file",
                WorkingDirectory = processDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.EnvironmentVariables["CODEX_HOME"] = ProfileDirectory;
            start.EnvironmentVariables.Remove("OPENAI_API_KEY");
            start.EnvironmentVariables.Remove("CODEX_ACCESS_TOKEN");

            _process = new Process { StartInfo = start, EnableRaisingEvents = true };
            _process.Exited += delegate { OnDisconnected(); };
            if (!_process.Start()) throw new InvalidOperationException("Could not start codex app-server.");
            _input = _process.StandardInput;
            _input.AutoFlush = true;

            _outputTask = Task.Factory.StartNew((Action)ReadLoop, TaskCreationOptions.LongRunning);
            _errorTask = Task.Factory.StartNew((Action)ReadErrorLoop, TaskCreationOptions.LongRunning);

            await RequestAsync("initialize", new
            {
                clientInfo = new
                {
                    name = "codex_legion",
                    title = "Codex Legion",
                    version = "0.2.0"
                },
                capabilities = new
                {
                    experimentalApi = true,
                    mcpServerOpenaiFormElicitation = true
                }
            }).ConfigureAwait(false);
            SendNotification("initialized", new { });
        }

        public async Task<AccountInfo> ReadAccountAsync()
        {
            var response = await RequestAsync("account/read", new { refreshToken = false }).ConfigureAwait(false);
            var result = GetDictionary(response, "result");
            object rawAccount;
            if (!result.TryGetValue("account", out rawAccount) || rawAccount == null) return null;
            var account = rawAccount as Dictionary<string, object>;
            if (account == null) return null;
            return new AccountInfo
            {
                Type = GetString(account, "type"),
                Email = GetString(account, "email"),
                PlanType = GetString(account, "planType")
            };
        }

        public async Task<LoginInfo> StartChatGptLoginAsync()
        {
            var response = await RequestAsync("account/login/start", new
            {
                type = "chatgpt",
                useHostedLoginSuccessPage = true,
                appBrand = "codex"
            }).ConfigureAwait(false);
            var result = GetDictionary(response, "result");
            string url = GetString(result, "authUrl");
            if (String.IsNullOrWhiteSpace(url))
                throw new InvalidDataException("app-server did not return a sign-in URL.");
            return new LoginInfo
            {
                LoginId = GetString(result, "loginId"),
                AuthorizationUrl = url
            };
        }

        public Task LogoutAsync()
        {
            PrepareNewThread();
            return RequestAsync("account/logout", new { });
        }

        public Task<string> StartThreadAsync(string workingDirectory)
        {
            return StartThreadAsync(workingDirectory, null);
        }

        public async Task<string> StartThreadAsync(string workingDirectory, RuntimeSettings settings)
        {
            var parameters = new Dictionary<string, object>();
            parameters["cwd"] = workingDirectory;
            parameters["serviceName"] = "codex_legion";
            parameters["threadSource"] = "codex_legion";
            if (settings != null)
            {
                if (!String.IsNullOrWhiteSpace(settings.Model)) parameters["model"] = settings.Model;
                parameters["approvalPolicy"] = settings.ApprovalPolicy;
                parameters["approvalsReviewer"] = settings.ApprovalsReviewer;
                parameters["sandbox"] = settings.SandboxMode;
            }
            var response = await RequestAsync("thread/start", parameters).ConfigureAwait(false);

            var result = GetDictionary(response, "result");
            var thread = GetDictionary(result, "thread");
            ThreadId = GetString(thread, "id");
            if (String.IsNullOrWhiteSpace(ThreadId))
                throw new InvalidDataException("app-server did not return a thread id.");
            return ThreadId;
        }

        public async Task<List<ThreadSummary>> ListThreadsAsync()
        {
            var summaries = new List<ThreadSummary>();
            string cursor = null;
            do
            {
                var parameters = new Dictionary<string, object>
                {
                    { "limit", 50 }, { "sortKey", "updated_at" }, { "sortDirection", "desc" }, { "archived", false }
                };
                if (!String.IsNullOrWhiteSpace(cursor)) parameters["cursor"] = cursor;
                var response = await RequestAsync("thread/list", parameters).ConfigureAwait(false);
                var result = GetDictionary(response, "result");
                foreach (object raw in GetObjectList(result, "data"))
                {
                    var thread = raw as Dictionary<string, object>;
                    if (thread == null) continue;
                    string id = GetString(thread, "id");
                    if (String.IsNullOrWhiteSpace(id)) continue;
                    string title = GetString(thread, "name");
                    string preview = GetString(thread, "preview");
                    DateTime updated = FromUnixTime(thread, "updatedAt");
                    if (String.IsNullOrWhiteSpace(title)) title = !String.IsNullOrWhiteSpace(preview) ? preview : "Conversation";
                    summaries.Add(new ThreadSummary
                    {
                        Id = id, Title = title, Preview = preview,
                        Cwd = GetString(thread, "cwd"), UpdatedAt = updated
                    });
                }
                cursor = GetString(result, "nextCursor");
            } while (!String.IsNullOrWhiteSpace(cursor) && summaries.Count < 250);
            return summaries;
        }

        public async Task ResumeThreadAsync(string threadId)
        {
            await RequestAsync("thread/resume", new
            {
                threadId = threadId,
                excludeTurns = true
            }).ConfigureAwait(false);
            ThreadId = threadId;
            ActiveTurnId = null;
        }

        public async Task<List<ConversationMessage>> LoadMessagesAsync(string threadId)
        {
            var messages = new List<ConversationMessage>();
            string cursor = null;
            do
            {
                var parameters = new Dictionary<string, object>
                {
                    { "threadId", threadId }, { "sortDirection", "asc" }, { "limit", 200 }
                };
                if (!String.IsNullOrWhiteSpace(cursor)) parameters["cursor"] = cursor;
                var response = await RequestAsync("thread/items/list", parameters).ConfigureAwait(false);
                var result = GetDictionary(response, "result");
                foreach (object rawEntry in GetObjectList(result, "data"))
                {
                    var entry = rawEntry as Dictionary<string, object>;
                    var item = GetDictionary(entry, "item");
                    string type = GetString(item, "type");
                    if (type == "agentMessage")
                    {
                        string text = GetString(item, "text");
                        if (!String.IsNullOrWhiteSpace(text)) messages.Add(new ConversationMessage { IsUser = false, Text = text });
                    }
                    else if (type == "userMessage")
                    {
                        var builder = new StringBuilder();
                        foreach (object rawContent in GetObjectList(item, "content"))
                        {
                            var content = rawContent as Dictionary<string, object>;
                            if (content == null || GetString(content, "type") != "text") continue;
                            string text = GetString(content, "text");
                            if (String.IsNullOrWhiteSpace(text)) continue;
                            if (builder.Length > 0) builder.AppendLine();
                            builder.Append(text);
                        }
                        if (builder.Length > 0) messages.Add(new ConversationMessage { IsUser = true, Text = builder.ToString() });
                    }
                }
                cursor = GetString(result, "nextCursor");
            } while (!String.IsNullOrWhiteSpace(cursor) && messages.Count < 2000);
            return messages;
        }

        public void PrepareNewThread()
        {
            if (!String.IsNullOrWhiteSpace(ActiveTurnId))
                throw new InvalidOperationException("Cannot replace a thread while a turn is active.");
            ThreadId = null;
            ActiveTurnId = null;
        }

        public Task StartTurnAsync(string text)
        {
            return StartTurnAsync(text, null, null, null);
        }

        public Task StartTurnAsync(string text, RuntimeSettings settings, string workingDirectory, IList<UserAttachment> attachments)
        {
            if (String.IsNullOrWhiteSpace(ThreadId))
                throw new InvalidOperationException("Start a thread before sending a message.");

            var input = new List<object>();
            input.Add(new Dictionary<string, object> { { "type", "text" }, { "text", text } });
            if (attachments != null)
            {
                foreach (UserAttachment attachment in attachments)
                {
                    if (attachment == null || String.IsNullOrWhiteSpace(attachment.Path)) continue;
                    if (attachment.Type == "skill")
                    {
                        input.Add(new Dictionary<string, object>
                        {
                            { "type", "skill" }, { "name", attachment.Name }, { "path", attachment.Path }
                        });
                    }
                    else
                    {
                        input.Add(new Dictionary<string, object> { { "type", "localImage" }, { "path", attachment.Path } });
                    }
                }
            }

            var parameters = new Dictionary<string, object>();
            parameters["threadId"] = ThreadId;
            parameters["input"] = input.ToArray();
            if (!String.IsNullOrWhiteSpace(workingDirectory)) parameters["cwd"] = workingDirectory;
            if (settings != null)
            {
                if (!String.IsNullOrWhiteSpace(settings.Model)) parameters["model"] = settings.Model;
                if (!String.IsNullOrWhiteSpace(settings.ReasoningEffort)) parameters["effort"] = settings.ReasoningEffort;
                parameters["approvalPolicy"] = settings.ApprovalPolicy;
                parameters["approvalsReviewer"] = settings.ApprovalsReviewer;
                parameters["sandboxPolicy"] = settings.SandboxPolicy;
                parameters["summary"] = "auto";
            }
            return RequestAsync("turn/start", parameters);
        }

        public async Task InterruptAsync()
        {
            if (String.IsNullOrWhiteSpace(ThreadId) || String.IsNullOrWhiteSpace(ActiveTurnId)) return;
            await RequestAsync("turn/interrupt", new
            {
                threadId = ThreadId,
                turnId = ActiveTurnId
            }).ConfigureAwait(false);
        }

        public void RespondToServerRequest(int requestId, object result)
        {
            Write(new { id = requestId, result = result });
        }

        public void RespondToServerRequestError(int requestId, int code, string message)
        {
            Write(new { id = requestId, error = new { code = code, message = message } });
        }

        private Task<Dictionary<string, object>> RequestAsync(string method, object parameters)
        {
            int id = Interlocked.Increment(ref _nextId);
            var completion = new TaskCompletionSource<Dictionary<string, object>>();
            if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate request id.");
            Write(new { method = method, id = id, @params = parameters });
            return completion.Task;
        }

        private void SendNotification(string method, object parameters)
        {
            Write(new { method = method, @params = parameters });
        }

        private void Write(object message)
        {
            string line = _json.Serialize(message);
            lock (_writeLock)
            {
                if (_input == null) throw new InvalidOperationException("app-server is not connected.");
                _input.WriteLine(line);
            }
        }

        private void ReadLoop()
        {
            try
            {
                string line;
                while (!_stop.IsCancellationRequested && (line = _process.StandardOutput.ReadLine()) != null)
                {
                    if (String.IsNullOrWhiteSpace(line)) continue;
                    Dictionary<string, object> message;
                    try { message = _json.Deserialize<Dictionary<string, object>>(line); }
                    catch (Exception ex)
                    {
                        OnLog("Could not parse app-server message: " + ex.Message);
                        continue;
                    }
                    DispatchMessage(message);
                }
            }
            catch (Exception ex)
            {
                if (!_stop.IsCancellationRequested) OnLog("app-server output ended: " + ex.Message);
            }
        }

        private void ReadErrorLoop()
        {
            try
            {
                string line;
                while (!_stop.IsCancellationRequested && (line = _process.StandardError.ReadLine()) != null)
                {
                    if (!String.IsNullOrWhiteSpace(line)) OnLog(line);
                }
            }
            catch (Exception ex)
            {
                if (!_stop.IsCancellationRequested) OnLog("app-server log ended: " + ex.Message);
            }
        }

        private void DispatchMessage(Dictionary<string, object> message)
        {
            int id;
            bool hasId = TryGetInt(message, "id", out id);
            string method = GetString(message, "method");

            if (hasId && String.IsNullOrWhiteSpace(method))
            {
                TaskCompletionSource<Dictionary<string, object>> completion;
                if (_pending.TryRemove(id, out completion))
                {
                    object error;
                    if (message.TryGetValue("error", out error) && error != null)
                        completion.TrySetException(new InvalidOperationException(DescribeError(error)));
                    else
                        completion.TrySetResult(message);
                }
                return;
            }

            if (!String.IsNullOrWhiteSpace(method))
            {
                var parameters = GetDictionary(message, "params");
                if (method == "turn/started")
                {
                    var turn = GetDictionary(parameters, "turn");
                    ActiveTurnId = GetString(turn, "id");
                }
                else if (method == "turn/completed")
                {
                    ActiveTurnId = null;
                }

                if (hasId)
                {
                    var handler = ServerRequestReceived;
                    if (handler != null) handler(this, new ServerMessageEventArgs(message));
                }
                else
                {
                    var handler = NotificationReceived;
                    if (handler != null) handler(this, new ServerMessageEventArgs(message));
                }
            }
        }

        private static string DescribeError(object error)
        {
            var dictionary = error as Dictionary<string, object>;
            if (dictionary == null) return Convert.ToString(error);
            object message;
            return dictionary.TryGetValue("message", out message) ? Convert.ToString(message) : "Unknown app-server error.";
        }

        public static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            if (source == null) return new Dictionary<string, object>();
            object value;
            if (!source.TryGetValue(key, out value) || value == null) return new Dictionary<string, object>();
            return value as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        public static string GetString(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            return source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }

        public static bool TryGetInt(Dictionary<string, object> source, string key, out int value)
        {
            value = 0;
            if (source == null) return false;
            object raw;
            if (!source.TryGetValue(key, out raw) || raw == null) return false;
            try { value = Convert.ToInt32(raw); return true; }
            catch { return false; }
        }

        private static IEnumerable<object> GetObjectList(Dictionary<string, object> source, string key)
        {
            if (source == null) yield break;
            object raw;
            if (!source.TryGetValue(key, out raw) || raw == null) yield break;
            var array = raw as object[];
            if (array != null)
            {
                foreach (object item in array) yield return item;
                yield break;
            }
            var list = raw as ArrayList;
            if (list != null)
                foreach (object item in list) yield return item;
        }

        private static DateTime FromUnixTime(Dictionary<string, object> source, string key)
        {
            object raw;
            if (source == null || !source.TryGetValue(key, out raw) || raw == null) return DateTime.MinValue;
            try
            {
                double seconds = Convert.ToDouble(raw);
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
            }
            catch { return DateTime.MinValue; }
        }

        private void OnLog(string text)
        {
            _lastLogLine = text;
            var handler = LogReceived;
            if (handler != null) handler(this, new ServerLogEventArgs(text));
        }

        private void OnDisconnected()
        {
            foreach (var pair in _pending)
            {
                TaskCompletionSource<Dictionary<string, object>> ignored;
                if (_pending.TryRemove(pair.Key, out ignored))
                {
                    string suffix = String.IsNullOrWhiteSpace(_lastLogLine) ? "" : " Last log: " + _lastLogLine;
                    ignored.TrySetException(new EndOfStreamException("Codex app-server disconnected." + suffix));
                }
            }
            var handler = Disconnected;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            try { if (_stop != null) _stop.Cancel(); } catch { }
            try { if (_input != null) _input.Close(); } catch { }
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    if (!_process.WaitForExit(700)) _process.Kill();
                }
            }
            catch { }
            if (_process != null) _process.Dispose();
            if (_stop != null) _stop.Dispose();
        }

        private static string ResolveCodexExecutable()
        {
            string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "codex.exe");
            if (File.Exists(bundled)) return bundled;

            string configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            if (!String.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string folder in path.Split(Path.PathSeparator))
                {
                    if (String.IsNullOrWhiteSpace(folder)) continue;
                    string candidate = Path.Combine(folder.Trim(), "codex.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }

            try
            {
                string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string binRoot = Path.Combine(localData, "OpenAI", "Codex", "bin");
                if (Directory.Exists(binRoot))
                {
                    string best = null;
                    DateTime bestTime = DateTime.MinValue;
                    foreach (string candidate in Directory.GetFiles(binRoot, "codex.exe", SearchOption.AllDirectories))
                    {
                        DateTime time = File.GetLastWriteTimeUtc(candidate);
                        if (best == null || time > bestTime)
                        {
                            best = candidate;
                            bestTime = time;
                        }
                    }
                    if (best != null) return best;
                }
            }
            catch { }

            return "codex";
        }
    }
}
