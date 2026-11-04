using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CodexNativeLite
{
    public sealed partial class MainWindow
    {
        private readonly VoiceCredentialStore _voiceCredentials = new VoiceCredentialStore();
        private RealtimeVoiceSession _voiceSession;
        private Button _voiceButton;
        private TextBlock _voiceUserText;
        private TextBlock _voiceAssistantText;
        private bool _voiceStarting;

        private Button BuildVoiceButton()
        {
            _voiceButton = MakeButton("\uE720", false);
            _voiceButton.FontFamily = new FontFamily("Segoe Fluent Icons");
            _voiceButton.FontSize = 16;
            _voiceButton.Width = 42;
            _voiceButton.Height = 42;
            _voiceButton.Padding = new Thickness(0);
            _voiceButton.Margin = new Thickness(8, 0, 0, 0);
            _voiceButton.ToolTip = "Start voice";
            _voiceButton.Template = CreateButtonTemplate(new CornerRadius(21), HoverBrush, Brush("#302841"));
            _voiceButton.Click += VoiceButtonClick;
            return _voiceButton;
        }

        private async void VoiceButtonClick(object sender, RoutedEventArgs e)
        {
            if (_voiceStarting) return;
            if (_voiceSession != null && _voiceSession.IsActive)
            {
                await StopVoiceAsync();
                return;
            }

            string key = null;
            bool newKey = false;
            try
            {
                if (_voiceCredentials.HasKey) key = _voiceCredentials.Load();
            }
            catch
            {
                _voiceCredentials.Clear();
            }

            if (String.IsNullOrWhiteSpace(key))
            {
                key = await ShowVoiceSetupAsync();
                if (String.IsNullOrWhiteSpace(key)) return;
                newKey = true;
            }

            _voiceStarting = true;
            UpdateVoiceButton("Connecting…", true);
            try
            {
                _voiceSession = new RealtimeVoiceSession();
                _voiceSession.StatusChanged += VoiceStatusChanged;
                _voiceSession.UserTranscript += VoiceUserTranscript;
                _voiceSession.AssistantTranscript += VoiceAssistantTranscript;
                string projectName = String.IsNullOrWhiteSpace(_workingDirectory) ? null : new DirectoryInfo(_workingDirectory).Name;
                await _voiceSession.StartAsync(key, projectName);
                _voiceSession.Failed += VoiceFailed;
                if (newKey) _voiceCredentials.Save(key);
                AddVoiceCard();
                UpdateVoiceButton("Listening", true);
            }
            catch (Exception ex)
            {
                if (newKey || LooksLikeCredentialError(ex.Message)) _voiceCredentials.Clear();
                if (_voiceSession != null) _voiceSession.Dispose();
                _voiceSession = null;
                UpdateVoiceButton("Start voice", false);
                AddSystemCard("Voice couldn’t start", FriendlyVoiceError(ex.Message));
            }
            finally
            {
                _voiceStarting = false;
                key = null;
            }
        }

        private async Task StopVoiceAsync()
        {
            RealtimeVoiceSession session = _voiceSession;
            _voiceSession = null;
            if (session != null)
            {
                try { await session.StopAsync(); }
                catch { }
                session.Dispose();
            }
            UpdateVoiceButton("Start voice", false);
            SetStatus("Voice ended", false);
        }

        private Task<string> ShowVoiceSetupAsync()
        {
            if (_root == null || _promptOpen) return Task.FromResult<string>(null);
            _promptOpen = true;
            var completion = new TaskCompletionSource<string>();
            var overlay = CreateOverlay();
            var content = new StackPanel();

            var mark = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(23),
                Background = AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = "\uE720",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Foreground = WindowBrush,
                    FontSize = 19,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            content.Children.Add(mark);
            content.Children.Add(new TextBlock
            {
                Text = "Give Legion a voice",
                FontSize = 23,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 15, 0, 0)
            });
            content.Children.Add(new TextBlock
            {
                Text = "Add an OpenAI API key once, then tap the mic whenever you feel chatty.",
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 8, 0, 15)
            });

            var getKey = MakeButton("Get an API key  ↗", false);
            getKey.HorizontalAlignment = HorizontalAlignment.Left;
            getKey.Click += delegate
            {
                Process.Start(new ProcessStartInfo("https://platform.openai.com/api-keys") { UseShellExecute = true });
            };
            content.Children.Add(getKey);

            content.Children.Add(new TextBlock
            {
                Text = "API key",
                Foreground = AccentTextBrush,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 14, 0, 5)
            });
            var keyInput = MakePasswordBox("Paste your API key");
            content.Children.Add(keyInput);
            content.Children.Add(new TextBlock
            {
                Text = "Saved for this Windows user with DPAPI · Voice uses API credits",
                Foreground = Brush("#80778F"),
                FontSize = 10,
                Margin = new Thickness(2, 8, 0, 0)
            });

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            var cancel = MakeButton("Maybe later", false);
            var connect = MakeButton("Start talking", true);
            connect.Margin = new Thickness(9, 0, 0, 0);
            actions.Children.Add(cancel);
            actions.Children.Add(connect);
            content.Children.Add(actions);
            overlay.Children.Add(CreateOverlayCard(content, 500));
            _root.Children.Add(overlay);

            bool closing = false;
            Action<string> dismiss = value =>
            {
                if (closing) return;
                closing = true;
                var fade = new DoubleAnimation(overlay.Opacity, 0, TimeSpan.FromMilliseconds(110));
                fade.Completed += delegate
                {
                    _root.Children.Remove(overlay);
                    _promptOpen = false;
                    completion.TrySetResult(value);
                };
                overlay.BeginAnimation(OpacityProperty, fade);
            };
            cancel.Click += delegate { dismiss(null); };
            connect.Click += delegate
            {
                string value = keyInput.Password.Trim();
                if (String.IsNullOrWhiteSpace(value))
                {
                    keyInput.BorderBrush = CoralBrush;
                    keyInput.ToolTip = "Paste your API key first";
                    keyInput.Focus();
                    return;
                }
                dismiss(value);
            };
            overlay.PreviewKeyDown += delegate(object source, KeyEventArgs args)
            {
                if (args.Key == Key.Escape) { args.Handled = true; dismiss(null); }
                else if (args.Key == Key.Enter && !String.IsNullOrWhiteSpace(keyInput.Password))
                {
                    args.Handled = true;
                    dismiss(keyInput.Password.Trim());
                }
            };
            overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            keyInput.Focus();
            return completion.Task;
        }

        private void AddVoiceCard()
        {
            _voiceUserText = new TextBlock { Text = "Go ahead—I’m listening.", Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            _voiceAssistantText = new TextBlock { Foreground = TextBrush, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21, Margin = new Thickness(0, 10, 0, 0) };
            var content = new StackPanel();
            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            heading.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = CoralBrush, Margin = new Thickness(0, 5, 9, 0), VerticalAlignment = VerticalAlignment.Top });
            heading.Children.Add(new TextBlock { Text = "VOICE IS LIVE", Foreground = AccentTextBrush, FontSize = 10, FontWeight = FontWeights.Bold });
            content.Children.Add(heading);
            content.Children.Add(_voiceUserText);
            content.Children.Add(_voiceAssistantText);
            var card = new Border
            {
                Background = Gradient("#251B33", "#181321", 15),
                BorderBrush = Brush("#614D7D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18, 6, 18, 6),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 5, 50, 15),
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = content
            };
            _messages.Children.Add(card);
            AnimateIn(card, 8);
            ScrollToBottom();
        }

        private void VoiceStatusChanged(object sender, VoiceTextEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)(() => UpdateVoiceButton(e.Text, true)));
        }

        private void VoiceUserTranscript(object sender, VoiceTextEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (_voiceUserText != null)
                {
                    if (_voiceUserText.Text == "Go ahead—I’m listening.") _voiceUserText.Text = "You · ";
                    _voiceUserText.Text += e.Text;
                }
                ScrollToBottom();
            }));
        }

        private void VoiceAssistantTranscript(object sender, VoiceTextEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (_voiceAssistantText != null)
                {
                    if (String.IsNullOrEmpty(_voiceAssistantText.Text)) _voiceAssistantText.Text = "Legion · ";
                    _voiceAssistantText.Text += e.Text;
                }
                ScrollToBottom();
            }));
        }

        private void VoiceFailed(object sender, VoiceTextEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)(async () =>
            {
                AddSystemCard("Voice hiccup", FriendlyVoiceError(e.Text));
                await StopVoiceAsync();
            }));
        }

        private void UpdateVoiceButton(string status, bool active)
        {
            if (_voiceButton == null) return;
            _voiceButton.ToolTip = active ? status + " · click to end" : status;
            _voiceButton.Foreground = active ? WindowBrush : TextBrush;
            _voiceButton.Background = active ? AccentBrush : RaisedBrush;
            _voiceButton.BorderBrush = active ? AccentTextBrush : BorderLineBrush;
            _voiceButton.Content = active ? "\uE71A" : "\uE720";
            if (active)
            {
                _voiceButton.BeginAnimation(OpacityProperty, new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(720))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }
            else
            {
                _voiceButton.BeginAnimation(OpacityProperty, null);
                _voiceButton.Opacity = 1;
            }
            SetStatus(active ? "Voice · " + status : "Ready ✦", false);
        }

        private static bool LooksLikeCredentialError(string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return false;
            string value = message.ToLowerInvariant();
            return value.Contains("api key") || value.Contains("401") || value.Contains("unauthorized");
        }

        private static string FriendlyVoiceError(string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return "Try the mic again in a moment.";
            if (LooksLikeCredentialError(message)) return "That API key didn’t work. Tap the mic to add another one.";
            return message;
        }
    }
}
