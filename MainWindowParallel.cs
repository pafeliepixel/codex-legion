using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CodexNativeLite
{
    public sealed partial class MainWindow
    {
        private Button _parallelButton;
        private Border _parallelCard;
        private StackPanel _parallelAgentRows;
        private TextBlock _parallelStatus;
        private readonly Dictionary<string, TextBlock> _parallelAgents = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);

        private bool CurrentModelSupportsParallel()
        {
            if (_runtimeSettings.UsesLocalModel) return false;
            ModelOption model = SelectedModel();
            return model != null && model.SupportsParallelWork;
        }

        private void NormalizeParallelForModel(ModelOption model)
        {
            if (_runtimeSettings.UsesLocalModel || model == null || !model.SupportsParallelWork)
                _runtimeSettings.ParallelMode = "solo";
            else if (_runtimeSettings.ParallelMode == "smart" && !model.SupportsUltra)
                _runtimeSettings.ParallelMode = "asked";
        }

        private void UpdateParallelButton()
        {
            if (_parallelButton == null) return;
            bool supported = CurrentModelSupportsParallel();
            string glyph = _runtimeSettings.ParallelMode == "smart" ? "✦" : (_runtimeSettings.ParallelMode == "asked" ? "◇" : "│");
            _parallelButton.Content = glyph + "  " + _runtimeSettings.ParallelLabel;
            _parallelButton.IsEnabled = _authenticated && supported && !_busy;
            _parallelButton.ToolTip = supported
                ? "Choose when Legion may split work across a small crew"
                : (_runtimeSettings.UsesLocalModel ? "Local models work in one lane for now" : "This model does not support parallel work");
        }

        private void OpenParallelPopover(object sender, RoutedEventArgs e)
        {
            if (_busy || !CurrentModelSupportsParallel()) return;
            CloseActivePopup();
            var popup = CreatePopover(_parallelButton, 410, false);
            _activePopup = popup;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "How big should the crew be?", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 4, 4) });
            panel.Children.Add(new TextBlock
            {
                Text = "Parallel work is best when tasks can move independently.",
                Foreground = MutedBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 0, 4, 13)
            });
            AddParallelRow(panel, popup, "solo", "│", "One lane", "Keep the whole job with one agent.");
            AddParallelRow(panel, popup, "asked", "◇", "Backup on request", "Bring in teammates only when you ask.");
            ModelOption model = SelectedModel();
            if (model != null && model.SupportsUltra)
                AddParallelRow(panel, popup, "smart", "✦", "Smart split", "Let Legion divide independent work when it will help.");
            popup.Child = CreatePopoverCard(panel, 410, false);
            popup.IsOpen = true;
        }

        private void AddParallelRow(StackPanel panel, Popup popup, string mode, string glyph, string title, string description)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = glyph, Foreground = mode == "smart" ? CoralBrush : AccentTextBrush, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            var copy = new StackPanel();
            copy.Children.Add(new TextBlock { Text = title, Foreground = TextBrush, FontSize = 14, FontWeight = FontWeights.SemiBold });
            copy.Children.Add(new TextBlock { Text = description, Foreground = MutedBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 0) });
            Grid.SetColumn(copy, 1);
            row.Children.Add(copy);
            if (String.Equals(_runtimeSettings.ParallelMode, mode, StringComparison.Ordinal))
            {
                var check = new TextBlock { Text = "✓", Foreground = AccentTextBrush, FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(check, 2);
                row.Children.Add(check);
            }
            var button = MakeButton("", false);
            button.Content = row;
            button.Tag = mode;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(12, 10, 12, 10);
            button.Margin = new Thickness(0, 0, 0, 5);
            button.Click += delegate(object source, RoutedEventArgs args)
            {
                string selected = Convert.ToString(((Button)source).Tag);
                if (selected == "smart") _runtimeSettings.ReasoningEffort = "ultra";
                _runtimeSettings.ParallelMode = selected;
                _settingsStore.Save(_runtimeSettings);
                popup.IsOpen = false;
                if (_client != null && !String.IsNullOrWhiteSpace(_client.ThreadId) && String.IsNullOrWhiteSpace(_client.ActiveTurnId))
                {
                    _client.PrepareNewThread();
                    PrepareNewThread();
                }
                UpdateRuntimeButtons();
                SetStatus(_runtimeSettings.ParallelLabel + " ready", false);
            };
            panel.Children.Add(button);
        }

        private void ResetParallelTurnUi()
        {
            _parallelCard = null;
            _parallelAgentRows = null;
            _parallelStatus = null;
            _parallelAgents.Clear();
        }

        private void EnsureParallelCard()
        {
            if (_parallelCard != null) return;
            var panel = new StackPanel();
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.Children.Add(new TextBlock { Text = "✦", Foreground = CoralBrush, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            var title = new StackPanel { Margin = new Thickness(9, 0, 0, 0) };
            title.Children.Add(new TextBlock { Text = "Legion split the work", Foreground = TextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold });
            _parallelStatus = new TextBlock { Text = "Calling in a focused crew…", Foreground = MutedBrush, FontSize = 10, Margin = new Thickness(0, 2, 0, 0) };
            title.Children.Add(_parallelStatus);
            Grid.SetColumn(title, 1);
            heading.Children.Add(title);
            panel.Children.Add(heading);
            _parallelAgentRows = new StackPanel { Margin = new Thickness(27, 10, 0, 0) };
            panel.Children.Add(_parallelAgentRows);
            _parallelCard = new Border
            {
                Background = Gradient("#211A30", "#171321", 12),
                BorderBrush = Brush("#5D4B7D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12, 5, 12, 5),
                Padding = new Thickness(13, 11, 14, 11),
                Margin = new Thickness(0, 0, 80, 10),
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 350,
                Child = panel
            };
            _messages.Children.Add(_parallelCard);
            AnimateIn(_parallelCard, 7);
            ScrollToBottom();
        }

        private void HandleParallelItem(Dictionary<string, object> item, bool completed)
        {
            string type = CodexAppServerClient.GetString(item, "type");
            if (type == "collabAgentToolCall")
            {
                EnsureParallelCard();
                string tool = CodexAppServerClient.GetString(item, "tool");
                string status = CodexAppServerClient.GetString(item, "status");
                if (tool == "spawnAgent") _parallelStatus.Text = completed || status == "completed" ? "The crew is moving." : "Splitting off a focused task…";
                else if (tool == "wait") _parallelStatus.Text = "Waiting for the crew to report back…";
                else if (tool == "sendMessage" || tool == "sendInput" || tool == "followupTask") _parallelStatus.Text = "Passing notes between teammates…";
                else if (tool == "interruptAgent" || tool == "closeAgent") _parallelStatus.Text = "Bringing a teammate back in…";
                return;
            }
            if (type != "subAgentActivity") return;
            EnsureParallelCard();
            string key = CodexAppServerClient.GetString(item, "agentThreadId") ?? CodexAppServerClient.GetString(item, "agentPath") ?? Guid.NewGuid().ToString("N");
            string path = CodexAppServerClient.GetString(item, "agentPath");
            string kind = CodexAppServerClient.GetString(item, "kind") ?? (completed ? "completed" : "started");
            TextBlock row;
            if (!_parallelAgents.TryGetValue(key, out row))
            {
                row = new TextBlock { Foreground = AccentTextBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) };
                _parallelAgents[key] = row;
                _parallelAgentRows.Children.Add(row);
            }
            string state = kind == "completed" ? "done" : (kind == "interrupted" ? "stopped" : (kind == "interacted" ? "checked in" : "joined"));
            string model = CodexAppServerClient.GetString(item, "model");
            string effort = CodexAppServerClient.GetString(item, "reasoningEffort");
            string details = "";
            if (!String.IsNullOrWhiteSpace(model)) details = " · " + model;
            if (!String.IsNullOrWhiteSpace(effort)) details += " " + Friendly(effort);
            row.Text = (kind == "completed" ? "✓  " : (kind == "interrupted" ? "×  " : "●  ")) + FriendlyAgentName(path) + " · " + state + details;
            row.Foreground = kind == "completed" ? Brush("#B9F6D1") : (kind == "interrupted" ? CoralBrush : AccentTextBrush);
            _parallelStatus.Text = kind == "completed" ? "Pulling the findings together…" : "The crew is working in parallel.";
            ScrollToBottom();
        }

        private void CompleteParallelTurnUi()
        {
            if (_parallelStatus != null) _parallelStatus.Text = "Crew wrapped up. One answer coming through.";
        }

        private static string FriendlyAgentName(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return "Teammate";
            string[] parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            string name = parts.Length == 0 ? path : parts[parts.Length - 1];
            name = name.Replace('_', ' ').Replace('-', ' ').Trim();
            return String.IsNullOrWhiteSpace(name) ? "Teammate" : Char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
    }
}
