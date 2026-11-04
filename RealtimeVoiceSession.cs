using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexNativeLite
{
    public sealed class VoiceTextEventArgs : EventArgs
    {
        public string Text { get; private set; }
        public VoiceTextEventArgs(string text) { Text = text; }
    }

    public sealed class RealtimeVoiceSession : IDisposable
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private ClientWebSocket _socket;
        private CancellationTokenSource _stop;
        private WinMmMicrophone _microphone;
        private WinMmSpeaker _speaker;
        private Task _receiveTask;
        private TaskCompletionSource<bool> _ready;
        private bool _disposed;

        public bool IsActive { get { return _socket != null && _socket.State == WebSocketState.Open; } }
        public event EventHandler<VoiceTextEventArgs> StatusChanged;
        public event EventHandler<VoiceTextEventArgs> UserTranscript;
        public event EventHandler<VoiceTextEventArgs> AssistantTranscript;
        public event EventHandler<VoiceTextEventArgs> Failed;

        public async Task StartAsync(string apiKey, string projectName)
        {
            if (_disposed) throw new ObjectDisposedException("RealtimeVoiceSession");
            if (IsActive) return;
            if (String.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("An OpenAI API key is required.");

            _stop = new CancellationTokenSource();
            _ready = new TaskCompletionSource<bool>();
            _socket = new ClientWebSocket();
            _socket.Options.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
            _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            RaiseStatus("Connecting…");

            try
            {
                await _socket.ConnectAsync(
                    new Uri("wss://api.openai.com/v1/realtime?model=gpt-realtime-2.1"),
                    _stop.Token).ConfigureAwait(false);
                _receiveTask = ReceiveLoopAsync(_stop.Token);
                await WaitWithTimeout(_ready.Task, TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                string context = String.IsNullOrWhiteSpace(projectName)
                    ? "No project folder is selected."
                    : "The current project folder is named " + projectName + ".";
                var update = new Dictionary<string, object>
                {
                    { "type", "session.update" },
                    { "session", new Dictionary<string, object>
                        {
                            { "type", "realtime" },
                            { "model", "gpt-realtime-2.1" },
                            { "output_modalities", new object[] { "audio" } },
                            { "instructions", "You are Codex Legion, a warm and concise coding voice companion. " + context + " Keep spoken answers clear and fairly short. Be honest that this voice session cannot inspect or edit local files directly." },
                            { "audio", new Dictionary<string, object>
                                {
                                    { "input", new Dictionary<string, object>
                                        {
                                            { "format", new Dictionary<string, object> { { "type", "audio/pcm" }, { "rate", 24000 } } },
                                            { "noise_reduction", new Dictionary<string, object> { { "type", "near_field" } } },
                                            { "transcription", new Dictionary<string, object> { { "model", "gpt-4o-mini-transcribe" } } },
                                            { "turn_detection", new Dictionary<string, object> { { "type", "semantic_vad" }, { "interrupt_response", true } } }
                                        }
                                    },
                                    { "output", new Dictionary<string, object>
                                        {
                                            { "format", new Dictionary<string, object> { { "type", "audio/pcm" }, { "rate", 24000 } } },
                                            { "voice", "marin" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };
                await SendAsync(update, _stop.Token).ConfigureAwait(false);

                _speaker = new WinMmSpeaker();
                _microphone = new WinMmMicrophone();
                _microphone.DataAvailable += MicrophoneDataAvailable;
                _microphone.Start();
                RaiseStatus("Listening");
            }
            catch
            {
                try { StopAsync().Wait(2000); } catch { }
                throw;
            }
        }

        public async Task StopAsync()
        {
            WinMmMicrophone microphone = _microphone;
            _microphone = null;
            if (microphone != null)
            {
                microphone.DataAvailable -= MicrophoneDataAvailable;
                microphone.Dispose();
            }

            WinMmSpeaker speaker = _speaker;
            _speaker = null;
            if (speaker != null) speaker.Dispose();

            ClientWebSocket socket = _socket;
            _socket = null;
            if (_stop != null) _stop.Cancel();
            if (socket != null)
            {
                try
                {
                    if (socket.State == WebSocketState.Open)
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Voice session ended", CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
                socket.Dispose();
            }
            if (_stop != null) { _stop.Dispose(); _stop = null; }
            RaiseStatus("Voice off");
        }

        private async void MicrophoneDataAvailable(object sender, AudioDataEventArgs e)
        {
            try
            {
                if (!IsActive) return;
                await SendAsync(new Dictionary<string, object>
                {
                    { "type", "input_audio_buffer.append" },
                    { "audio", Convert.ToBase64String(e.Data) }
                }, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { RaiseFailed(ex.Message); }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[16384];
            try
            {
                while (!cancellationToken.IsCancellationRequested && _socket != null && _socket.State == WebSocketState.Open)
                {
                    using (var stream = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close) return;
                            stream.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        if (result.MessageType != WebSocketMessageType.Text) continue;
                        string text = Encoding.UTF8.GetString(stream.ToArray());
                        HandleEvent(_json.Deserialize<Dictionary<string, object>>(text));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested) RaiseFailed(ex.Message);
            }
        }

        private void HandleEvent(Dictionary<string, object> message)
        {
            string type = ReadString(message, "type");
            if (type == "session.created")
            {
                _ready.TrySetResult(true);
                return;
            }
            if (type == "input_audio_buffer.speech_started")
            {
                if (_speaker != null) _speaker.Clear();
                RaiseStatus("Listening");
                return;
            }
            if (type == "input_audio_buffer.speech_stopped")
            {
                RaiseStatus("Thinking…");
                return;
            }
            if (type == "response.output_audio.delta")
            {
                string delta = ReadString(message, "delta");
                if (!String.IsNullOrWhiteSpace(delta) && _speaker != null)
                {
                    try { _speaker.Play(Convert.FromBase64String(delta)); }
                    catch (FormatException) { }
                }
                RaiseStatus("Speaking");
                return;
            }
            if (type == "response.output_audio_transcript.delta")
            {
                Raise(AssistantTranscript, ReadString(message, "delta"));
                return;
            }
            if (type == "conversation.item.input_audio_transcription.delta")
            {
                Raise(UserTranscript, ReadString(message, "delta"));
                return;
            }
            if (type == "response.done")
            {
                RaiseStatus("Listening");
                return;
            }
            if (type == "error")
            {
                Dictionary<string, object> error = ReadDictionary(message, "error");
                string detail = ReadString(error, "message");
                if (String.IsNullOrWhiteSpace(detail)) detail = "The voice service returned an error.";
                _ready.TrySetException(new InvalidOperationException(detail));
                RaiseFailed(detail);
            }
        }

        private async Task SendAsync(object value, CancellationToken cancellationToken)
        {
            string json = _json.Serialize(value);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ClientWebSocket socket = _socket;
                if (socket == null || socket.State != WebSocketState.Open) return;
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally { _sendGate.Release(); }
        }

        private static async Task WaitWithTimeout(Task task, TimeSpan timeout)
        {
            Task winner = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (!Object.ReferenceEquals(winner, task)) throw new TimeoutException("Voice took too long to connect.");
            await task.ConfigureAwait(false);
        }

        private void RaiseStatus(string text) { Raise(StatusChanged, text); }
        private void RaiseFailed(string text) { Raise(Failed, text); }
        private void Raise(EventHandler<VoiceTextEventArgs> handler, string text)
        {
            if (handler != null && !String.IsNullOrWhiteSpace(text)) handler(this, new VoiceTextEventArgs(text));
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value)) return new Dictionary<string, object>();
            return value as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { StopAsync().Wait(2000); } catch { }
            _sendGate.Dispose();
        }
    }
}
