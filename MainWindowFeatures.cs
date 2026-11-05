using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Ellipse = System.Windows.Shapes.Ellipse;
using Polygon = System.Windows.Shapes.Polygon;

namespace CodexNativeLite
{
    public sealed partial class MainWindow
    {
        private readonly RuntimeSettings _runtimeSettings = new RuntimeSettings();
        private readonly AppSettingsStore _settingsStore = new AppSettingsStore();
        private CapabilityCatalog _capabilities = new CapabilityCatalog();
        private readonly List<UserAttachment> _attachments = new List<UserAttachment>();
        private readonly GitService _git = new GitService();
        private readonly UpdateService _updates = new UpdateService();
        private readonly SemaphoreSlim _serverRequestGate = new SemaphoreSlim(1, 1);
        private Button _modelButton;
        private Button _permissionButton;
        private Button _attachmentButton;
        private Button _threadActionsButton;
        private TextBlock _attachmentBadge;
        private TextBox _historySearch;
        private List<ThreadSummary> _historyCache = new List<ThreadSummary>();
        private string _lastCompletedTurnId;
        private TextBlock _liveActivityText;
        private string _liveActivityKind;
        private Popup _activePopup;

        private void AppendActivity(string kind, string delta)
        {
            if (String.IsNullOrWhiteSpace(delta)) return;
            string clean = delta.Replace("\r", "");
            if (_liveActivityText == null || !String.Equals(_liveActivityKind, kind, StringComparison.Ordinal))
            {
                AddActivity(kind, clean);
                return;
            }
            string next = _liveActivityText.Text + clean;
            if (next.Length > 6000) next = "…\n" + next.Substring(next.Length - 5998);
            _liveActivityText.Text = next;
            ScrollToBottom();
        }

        private void AddDiffCard(string diff)
        {
            if (String.IsNullOrWhiteSpace(diff)) { AddActivity("Diff", "Workspace changes updated"); return; }
            if (diff.Length > 20000) diff = diff.Substring(0, 20000) + "\n… diff trimmed in the timeline";
            var rows = new StackPanel();
            foreach (string line in diff.Replace("\r", "").Split('\n'))
            {
                Brush foreground = MutedBrush;
                Brush background = Brushes.Transparent;
                if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
                {
                    foreground = Brush("#B9F6D1"); background = Brush("#192B24");
                }
                else if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
                {
                    foreground = Brush("#FFB7B0"); background = Brush("#302022");
                }
                else if (line.StartsWith("@@", StringComparison.Ordinal)) foreground = AccentTextBrush;
                rows.Children.Add(new TextBlock { Text = line, Foreground = foreground, Background = background, FontFamily = new FontFamily("Consolas"), FontSize = 11 });
            }
            var scroller = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 360 };
            var card = new Border
            {
                Background = PanelBrush, BorderBrush = BorderLineBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(11), Margin = new Thickness(0, 0, 50, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch, Child = scroller
            };
            _messages.Children.Add(card);
            AnimateIn(card, 6);
            ScrollToBottom();
        }

        private void FilterHistory(object sender, TextChangedEventArgs e)
        {
            RenderHistory();
        }

        private void RenderHistory()
        {
            if (_historyPanel == null) return;
            _historyPanel.Children.Clear();
            string query = _historySearch == null ? "" : (_historySearch.Text ?? "").Trim();
            int shown = 0;
            foreach (ThreadSummary summary in _historyCache)
            {
                string haystack = (summary.Title ?? "") + " " + (summary.Preview ?? "") + " " + (summary.Cwd ?? "");
                if (!String.IsNullOrWhiteSpace(query) && haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string title = summary.Title ?? "Conversation";
                if (title.Length > 38) title = title.Substring(0, 35) + "…";
                string date = summary.UpdatedAt == DateTime.MinValue ? "" : "\n" + summary.UpdatedAt.ToString("MMM d, HH:mm");
                var button = MakeButton(title + date, false);
                button.Tag = summary;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.FontWeight = FontWeights.Normal;
                button.FontSize = 11;
                button.Margin = new Thickness(0, 0, 0, 6);
                button.ToolTip = summary.Preview;
                button.Click += async delegate(object source, RoutedEventArgs args)
                {
                    var selected = ((Button)source).Tag as ThreadSummary;
                    if (selected != null) await OpenThreadAsync(selected);
                };
                _historyPanel.Children.Add(button);
                shown++;
            }
            if (shown == 0) ShowHistoryEmpty(String.IsNullOrWhiteSpace(query) ? "No chats yet" : "No matches");
        }

        private async Task LoadCapabilitiesAsync()
        {
            if (_client == null || !_client.IsRunning || !_authenticated) return;
            try
            {
                _capabilities = await _client.LoadCapabilitiesAsync();
                ModelOption selected = null;
                if (!String.IsNullOrWhiteSpace(_runtimeSettings.Model))
                {
                    foreach (ModelOption model in _capabilities.Models)
                        if (String.Equals(model.Id, _runtimeSettings.Model, StringComparison.OrdinalIgnoreCase)) { selected = model; break; }
                }
                foreach (ModelOption model in _capabilities.Models)
                {
                    if (selected == null && model.IsDefault) { selected = model; break; }
                }
                if (selected == null && _capabilities.Models.Count > 0) selected = _capabilities.Models[0];
                if (selected != null)
                {
                    _runtimeSettings.Model = selected.Id;
                    bool effortAvailable = false;
                    foreach (ReasoningOption effort in selected.ReasoningEfforts)
                        if (String.Equals(effort.Id, _runtimeSettings.ReasoningEffort, StringComparison.OrdinalIgnoreCase)) effortAvailable = true;
                    if (!effortAvailable) _runtimeSettings.ReasoningEffort = selected.DefaultReasoningEffort;
                    NormalizeParallelForModel(selected);
                }
                if (!_capabilities.Allows(Convert.ToString(_runtimeSettings.ApprovalPolicy), _runtimeSettings.SandboxMode))
                {
                    if (_capabilities.Allows("on-request", "read-only")) _runtimeSettings.PermissionMode = "read-only";
                    else if (_capabilities.Allows("never", "danger-full-access")) _runtimeSettings.PermissionMode = "full";
                }
                UpdateRuntimeButtons();
                _settingsStore.Save(_runtimeSettings);
                SetStatus(_client.UsesBundledRuntime ? "Ready · private runtime" : "Ready · dev runtime", false);
            }
            catch (Exception ex)
            {
                UpdateRuntimeButtons();
                AddActivity("Settings", "Using server defaults · " + ex.Message);
            }
        }

        private async void RefreshCapabilitiesAfterAccount()
        {
            await LoadCapabilitiesAsync();
        }

        private async Task HandleServerRequestAsync(Dictionary<string, object> message)
        {
            await _serverRequestGate.WaitAsync();
            try
            {
                int id;
                if (!CodexAppServerClient.TryGetInt(message, "id", out id)) return;
                string method = CodexAppServerClient.GetString(message, "method");
                var parameters = CodexAppServerClient.GetDictionary(message, "params");

                if (method == "item/tool/call")
                {
                    await HandleBrowserToolRequestAsync(id, parameters);
                }
                else if (method == "item/commandExecution/requestApproval" || method == "item/fileChange/requestApproval")
                {
                    string reason = CodexAppServerClient.GetString(parameters, "reason");
                    string command = CodexAppServerClient.GetString(parameters, "command");
                    string detail = !String.IsNullOrWhiteSpace(command) ? command : "Codex wants to change files in this project.";
                    if (!String.IsNullOrWhiteSpace(reason)) detail = reason + "\n\n" + detail;
                    var choices = new List<ChoiceItem>
                    {
                        new ChoiceItem("accept", "Allow once", "Continue with this action."),
                        new ChoiceItem("acceptForSession", "Allow for this session", "Remember similar approval prompts until the app closes."),
                        new ChoiceItem("decline", "Don’t allow", "Keep the turn going without this action."),
                        new ChoiceItem("cancel", "Stop the turn", "Deny this action and stop current work.")
                    };
                    string decision = await ShowChoiceAsync("Codex needs your okay", detail, choices);
                    _client.RespondToServerRequest(id, new { decision = decision ?? "decline" });
                }
                else if (method == "item/permissions/requestApproval")
                {
                    string reason = CodexAppServerClient.GetString(parameters, "reason") ?? "Codex is asking for extra file or network access.";
                    var permissions = CodexAppServerClient.GetDictionary(parameters, "permissions");
                    string details = reason + "\n\n" + DescribePermissions(permissions);
                    string decision = await ShowChoiceAsync("Extra access", details, new List<ChoiceItem>
                    {
                        new ChoiceItem("turn", "Allow this turn", "Grant only for the current turn."),
                        new ChoiceItem("session", "Allow this session", "Keep the grant until this app session ends."),
                        new ChoiceItem("deny", "Don’t allow", "Grant nothing.")
                    });
                    if (decision == "turn" || decision == "session")
                        _client.RespondToServerRequest(id, new Dictionary<string, object> { { "permissions", permissions }, { "scope", decision } });
                    else
                        _client.RespondToServerRequest(id, new { permissions = new Dictionary<string, object>() });
                }
                else if (method == "item/tool/requestUserInput")
                {
                    var answers = new Dictionary<string, object>();
                    foreach (object rawQuestion in Objects(parameters, "questions"))
                    {
                        var question = rawQuestion as Dictionary<string, object>;
                        if (question == null) continue;
                        string questionId = CodexAppServerClient.GetString(question, "id");
                        string prompt = CodexAppServerClient.GetString(question, "question") ?? "Codex has a question";
                        string header = CodexAppServerClient.GetString(question, "header") ?? "Quick question";
                        string answer = null;
                        var options = new List<ChoiceItem>();
                        foreach (object rawOption in Objects(question, "options"))
                        {
                            var option = rawOption as Dictionary<string, object>;
                            if (option == null) continue;
                            string label = CodexAppServerClient.GetString(option, "label");
                            options.Add(new ChoiceItem(label, label, CodexAppServerClient.GetString(option, "description")));
                        }
                        if (options.Count > 0) answer = await ShowChoiceAsync(header, prompt, options);
                        else answer = await ShowTextPromptAsync(header, prompt, "Your answer", GetBool(question, "isSecret"));
                        answers[questionId] = new { answers = answer == null ? new string[0] : new[] { answer } };
                    }
                    _client.RespondToServerRequest(id, new { answers = answers });
                }
                else if (method == "mcpServer/elicitation/request")
                {
                    await HandleMcpElicitationAsync(id, parameters);
                }
                else if (method == "attestation/generate")
                {
                    _client.RespondToServerRequestError(id, -32601, "This client does not provide device attestation.");
                }
                else
                {
                    _client.RespondToServerRequestError(id, -32601, "Unsupported client request: " + method);
                    AddSystemCard("Unsupported interaction", method);
                }
            }
            catch (Exception ex)
            {
                AddSystemCard("Approval error", ex.Message);
            }
            finally
            {
                _serverRequestGate.Release();
            }
        }

        private async Task HandleMcpElicitationAsync(int id, Dictionary<string, object> parameters)
        {
            string mode = CodexAppServerClient.GetString(parameters, "mode");
            string message = CodexAppServerClient.GetString(parameters, "message") ?? "An app needs more information.";
            string server = CodexAppServerClient.GetString(parameters, "serverName") ?? "App";
            if (mode == "url")
            {
                string url = CodexAppServerClient.GetString(parameters, "url");
                string action = await ShowChoiceAsync(server, message, new List<ChoiceItem>
                {
                    new ChoiceItem("open", "Open link", url),
                    new ChoiceItem("decline", "Not now", "Decline this request.")
                });
                if (action == "open" && !String.IsNullOrWhiteSpace(url))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    _client.RespondToServerRequest(id, new { action = "accept", content = (object)null });
                }
                else _client.RespondToServerRequest(id, new { action = "decline", content = (object)null });
                return;
            }

            var content = new Dictionary<string, object>();
            var schema = CodexAppServerClient.GetDictionary(parameters, "requestedSchema");
            var properties = CodexAppServerClient.GetDictionary(schema, "properties");
            foreach (KeyValuePair<string, object> pair in properties)
            {
                var field = pair.Value as Dictionary<string, object>;
                if (field == null) continue;
                string label = CodexAppServerClient.GetString(field, "title") ?? Friendly(pair.Key);
                string description = CodexAppServerClient.GetString(field, "description") ?? message;
                string type = CodexAppServerClient.GetString(field, "type") ?? "string";
                string value = null;
                var enumChoices = new List<ChoiceItem>();
                foreach (object raw in Objects(field, "enum"))
                {
                    string option = Convert.ToString(raw);
                    enumChoices.Add(new ChoiceItem(option, option, null));
                }
                if (type == "boolean")
                    value = await ShowChoiceAsync(label, description, new List<ChoiceItem> { new ChoiceItem("true", "Yes", null), new ChoiceItem("false", "No", null) });
                else if (enumChoices.Count > 0) value = await ShowChoiceAsync(label, description, enumChoices);
                else value = await ShowTextPromptAsync(label, description, pair.Key, false);
                if (value == null)
                {
                    _client.RespondToServerRequest(id, new { action = "cancel", content = (object)null });
                    return;
                }
                if (type == "boolean") content[pair.Key] = String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                else if (type == "integer") { int parsed; content[pair.Key] = Int32.TryParse(value, out parsed) ? (object)parsed : value; }
                else content[pair.Key] = value;
            }
            bool accept = await ShowPromptAsync(server, message, "Send", "Cancel", false);
            _client.RespondToServerRequest(id, accept ? (object)new { action = "accept", content = content } : new { action = "cancel", content = (object)null });
        }

        private static string DescribePermissions(Dictionary<string, object> permissions)
        {
            var builder = new StringBuilder();
            var network = CodexAppServerClient.GetDictionary(permissions, "network");
            if (GetBool(network, "enabled")) builder.AppendLine("• Internet access");
            var fileSystem = CodexAppServerClient.GetDictionary(permissions, "fileSystem");
            foreach (object raw in Objects(fileSystem, "read")) builder.AppendLine("• Read " + Convert.ToString(raw));
            foreach (object raw in Objects(fileSystem, "write")) builder.AppendLine("• Write " + Convert.ToString(raw));
            foreach (object raw in Objects(fileSystem, "entries"))
            {
                var entry = raw as Dictionary<string, object>;
                if (entry != null) builder.AppendLine("• " + Friendly(CodexAppServerClient.GetString(entry, "access") ?? "access") + " requested path");
            }
            return builder.Length == 0 ? "Extra access requested" : builder.ToString().TrimEnd();
        }

        private static IEnumerable<object> Objects(Dictionary<string, object> source, string key)
        {
            object raw;
            if (source == null || !source.TryGetValue(key, out raw) || raw == null) yield break;
            var array = raw as object[];
            if (array != null) { foreach (object item in array) yield return item; yield break; }
            var list = raw as ArrayList;
            if (list != null) foreach (object item in list) yield return item;
        }

        private static bool GetBool(Dictionary<string, object> source, string key)
        {
            object raw;
            if (source == null || !source.TryGetValue(key, out raw) || raw == null) return false;
            try { return Convert.ToBoolean(raw); } catch { return false; }
        }

        private ModelOption SelectedModel()
        {
            foreach (ModelOption model in _capabilities.Models)
                if (String.Equals(model.Id, _runtimeSettings.Model, StringComparison.OrdinalIgnoreCase)) return model;
            return null;
        }

        private void UpdateRuntimeButtons()
        {
            ModelOption model = SelectedModel();
            if (_modelButton != null)
            {
                LocalModelChoice local = _runtimeSettings.UsesLocalModel ? LocalModelCatalog.Find(_runtimeSettings.LocalModelId) : null;
                string modelName = local != null ? "Local · " + local.Name : (model == null ? "Server default" : model.ToString());
                string effortName = String.IsNullOrWhiteSpace(_runtimeSettings.ReasoningEffort) ? "Default" : Friendly(_runtimeSettings.ReasoningEffort);
                _modelButton.Content = modelName + "  " + effortName + " ⌄";
                _modelButton.IsEnabled = _authenticated;
            }
            if (_permissionButton != null)
            {
                _permissionButton.Content = PermissionGlyph() + "  " + _runtimeSettings.PermissionLabel;
                _permissionButton.IsEnabled = _authenticated;
            }
            UpdateParallelButton();
            UpdateAttachmentBadge();
        }

        private string PermissionGlyph()
        {
            if (_runtimeSettings.PermissionMode == "full") return "!";
            if (_runtimeSettings.PermissionMode == "auto-review") return "◇";
            if (_runtimeSettings.PermissionMode == "read-only") return "◌";
            return "◉";
        }

        private void OpenModelPopover(object sender, RoutedEventArgs e)
        {
            CloseActivePopup();
            var popup = CreatePopover(_modelButton, 344, true);
            _activePopup = popup;
            ShowModelSettings(popup);
            popup.IsOpen = true;
        }

        private void ShowModelSettings(Popup popup)
        {
            ModelOption model = SelectedModel();
            LocalModelChoice local = _runtimeSettings.UsesLocalModel ? LocalModelCatalog.Find(_runtimeSettings.LocalModelId) : null;
            if (local != null)
            {
                model = new ModelOption { Id = "strata", DisplayName = "Local · " + local.Name, DefaultReasoningEffort = "medium" };
                model.ReasoningEfforts.Add(new ReasoningOption { Id = "none", Description = "Fastest answers with no extra reasoning budget." });
                model.ReasoningEfforts.Add(new ReasoningOption { Id = "low", Description = "A little more time for straightforward work." });
                model.ReasoningEfforts.Add(new ReasoningOption { Id = "medium", Description = "A balanced local default." });
                model.ReasoningEfforts.Add(new ReasoningOption { Id = "high", Description = "More local reasoning for difficult work." });
            }
            var panel = new StackPanel();
            Slider effortSlider = null;
            bool updatingEffort = false;
            Action refreshEffortVisuals = null;

            var header = new Grid { Margin = new Thickness(3, 0, 3, 13) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = "ϟ", Foreground = AccentTextBrush, FontSize = 19, VerticalAlignment = VerticalAlignment.Center });
            var effortTitle = new TextBlock
            {
                Text = String.IsNullOrWhiteSpace(_runtimeSettings.ReasoningEffort) ? "Default" : Friendly(_runtimeSettings.ReasoningEffort),
                Foreground = AccentTextBrush, FontSize = 18, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(effortTitle, 1);
            header.Children.Add(effortTitle);
            var reset = MakeLinkButton("↻");
            reset.FontSize = 18;
            reset.ToolTip = "Reset thinking level";
            reset.Click += delegate
            {
                if (model != null) _runtimeSettings.ReasoningEffort = model.DefaultReasoningEffort;
                _settingsStore.Save(_runtimeSettings);
                UpdateRuntimeButtons();
                if (refreshEffortVisuals != null) refreshEffortVisuals();
            };
            Grid.SetColumn(reset, 2);
            header.Children.Add(reset);
            panel.Children.Add(header);

            var modelRow = MakeButton((model == null ? "Server default" : model.ToString()) + "   ›", false);
            modelRow.HorizontalContentAlignment = HorizontalAlignment.Center;
            modelRow.FontSize = 13;
            modelRow.Margin = new Thickness(0, 0, 0, 12);
            modelRow.Click += delegate { ShowModelList(popup); };
            panel.Children.Add(modelRow);

            if (model != null && model.ReasoningEfforts.Count > 0)
            {
                int selectedIndex = 0;
                for (int index = 0; index < model.ReasoningEfforts.Count; index++)
                    if (String.Equals(model.ReasoningEfforts[index].Id, _runtimeSettings.ReasoningEffort, StringComparison.OrdinalIgnoreCase)) selectedIndex = index;
                effortSlider = new Slider
                {
                    Minimum = 0,
                    Maximum = model.ReasoningEfforts.Count - 1,
                    TickFrequency = 1,
                    IsSnapToTickEnabled = true,
                    SmallChange = 1,
                    LargeChange = 1,
                    Value = selectedIndex,
                    Height = 46,
                    Template = CreateEffortSliderTemplate()
                };
                var sliderLayer = new Grid();
                var dots = new UniformGrid
                {
                    Rows = 1,
                    Columns = model.ReasoningEfforts.Count,
                    Margin = new Thickness(22, 0, 22, 0),
                    IsHitTestVisible = false,
                    VerticalAlignment = VerticalAlignment.Center
                };
                foreach (ReasoningOption effort in model.ReasoningEfforts)
                    dots.Children.Add(new Grid
                    {
                        Children = { new Ellipse { Width = 5, Height = 5, Fill = Brush("#81798B"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }
                    });
                sliderLayer.Children.Add(effortSlider);
                sliderLayer.Children.Add(dots);
                effortSlider.ValueChanged += delegate
                {
                    if (updatingEffort) return;
                    int index = Math.Max(0, Math.Min(model.ReasoningEfforts.Count - 1, (int)Math.Round(effortSlider.Value)));
                    ReasoningOption effort = model.ReasoningEfforts[index];
                    _runtimeSettings.ReasoningEffort = effort.Id;
                    effortTitle.Text = Friendly(effort.Id);
                    effortSlider.ToolTip = Friendly(effort.Id) + (String.IsNullOrWhiteSpace(effort.Description) ? "" : "\n" + effort.Description);
                    _settingsStore.Save(_runtimeSettings);
                    UpdateRuntimeButtons();
                };
                refreshEffortVisuals = delegate
                {
                    int index = 0;
                    for (int position = 0; position < model.ReasoningEfforts.Count; position++)
                        if (String.Equals(model.ReasoningEfforts[position].Id, _runtimeSettings.ReasoningEffort, StringComparison.OrdinalIgnoreCase)) index = position;
                    updatingEffort = true;
                    effortSlider.Value = index;
                    updatingEffort = false;
                    effortTitle.Text = String.IsNullOrWhiteSpace(_runtimeSettings.ReasoningEffort) ? "Default" : Friendly(_runtimeSettings.ReasoningEffort);
                    ReasoningOption effort = model.ReasoningEfforts[index];
                    effortSlider.ToolTip = Friendly(effort.Id) + (String.IsNullOrWhiteSpace(effort.Description) ? "" : "\n" + effort.Description);
                };
                refreshEffortVisuals();
                panel.Children.Add(sliderLayer);
            }
            popup.Child = CreatePopoverCard(panel, 344, true);
        }

        private static ControlTemplate CreateEffortSliderTemplate()
        {
            const string xaml = @"<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""{x:Type Slider}"">
<Grid Height=""46"">
  <Border Background=""#2B2732"" BorderBrush=""#4A4651"" BorderThickness=""1"" CornerRadius=""23""/>
  <Track x:Name=""PART_Track"" Margin=""22,0"" VerticalAlignment=""Center"">
    <Track.DecreaseRepeatButton>
      <RepeatButton Command=""{x:Static Slider.DecreaseLarge}"" IsTabStop=""False""><RepeatButton.Template><ControlTemplate TargetType=""{x:Type RepeatButton}""><Border Height=""7"" CornerRadius=""4"" Background=""#9C83F7""/></ControlTemplate></RepeatButton.Template></RepeatButton>
    </Track.DecreaseRepeatButton>
    <Track.IncreaseRepeatButton>
      <RepeatButton Command=""{x:Static Slider.IncreaseLarge}"" IsTabStop=""False""><RepeatButton.Template><ControlTemplate TargetType=""{x:Type RepeatButton}""><Border Height=""7"" CornerRadius=""4"" Background=""#4A4651""/></ControlTemplate></RepeatButton.Template></RepeatButton>
    </Track.IncreaseRepeatButton>
    <Track.Thumb>
      <Thumb Width=""30"" Height=""30""><Thumb.Template><ControlTemplate TargetType=""{x:Type Thumb}""><Border CornerRadius=""15"" Background=""#FFFDFC"" BorderBrush=""#FFFFFF"" BorderThickness=""1""><Border.Effect><DropShadowEffect Color=""#000000"" BlurRadius=""12"" ShadowDepth=""2"" Opacity=""0.45""/></Border.Effect></Border></ControlTemplate></Thumb.Template></Thumb>
    </Track.Thumb>
  </Track>
</Grid>
</ControlTemplate>";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private void ShowModelList(Popup popup)
        {
            var panel = new StackPanel();
            var back = MakeLinkButton("‹  Models");
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.FontSize = 12;
            back.Margin = new Thickness(0, 0, 0, 10);
            back.Click += delegate { ShowModelSettings(popup); };
            panel.Children.Add(back);
            var list = new StackPanel();
            var localButton = MakeButton("◆  Local models on this PC", false);
            localButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            localButton.Margin = new Thickness(0, 0, 0, 9);
            localButton.Click += async delegate
            {
                popup.IsOpen = false;
                try { await ShowLocalModelsAsync(); }
                catch (Exception ex) { AddSystemCard("Local AI hiccup", ex.Message); SetStatus("Local AI unavailable", true); }
            };
            list.Children.Add(localButton);
            foreach (ModelOption model in _capabilities.Models)
            {
                var content = new Grid();
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = model.ToString(), Foreground = TextBrush, FontWeight = FontWeights.SemiBold });
                if (!String.IsNullOrWhiteSpace(model.Description))
                    text.Children.Add(new TextBlock { Text = model.Description, Foreground = MutedBrush, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 8, 0) });
                content.Children.Add(text);
                if (String.Equals(model.Id, _runtimeSettings.Model, StringComparison.OrdinalIgnoreCase))
                {
                    var check = new TextBlock { Text = "✓", Foreground = AccentTextBrush, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(check, 1);
                    content.Children.Add(check);
                }
                var button = MakeButton("", false);
                button.Content = content;
                button.Tag = model.Id;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Margin = new Thickness(0, 0, 0, 6);
                button.Click += delegate(object source, RoutedEventArgs args)
                {
                    bool leavingLocal = _runtimeSettings.UsesLocalModel;
                    if (leavingLocal) _localAi.Stop();
                    _runtimeSettings.ModelProvider = "openai";
                    _runtimeSettings.LocalBaseUrl = null;
                    _runtimeSettings.Model = Convert.ToString(((Button)source).Tag);
                    ModelOption selected = SelectedModel();
                    _runtimeSettings.ReasoningEffort = selected == null ? null : selected.DefaultReasoningEffort;
                    NormalizeParallelForModel(selected);
                    _settingsStore.Save(_runtimeSettings);
                    UpdateRuntimeButtons();
                    if (leavingLocal) PrepareNewThread();
                    ShowModelSettings(popup);
                };
                list.Children.Add(button);
            }
            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 390, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            popup.Child = CreatePopoverCard(panel, 344, true);
        }

        private void OpenPermissionPopover(object sender, RoutedEventArgs e)
        {
            CloseActivePopup();
            var popup = CreatePopover(_permissionButton, 430, false);
            _activePopup = popup;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "How should Codex act?", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 4, 13) });
            AddPermissionRow(panel, popup, "ask", "◉", "Ask for approval", "Ask before sensitive file or internet access.", false);
            AddPermissionRow(panel, popup, "auto-review", "◇", "Approve for me", "Codex reviews actions that may be unsafe.", false);
            AddPermissionRow(panel, popup, "read-only", "◌", "Read only", "Inspect the project without changing files.", false);
            AddPermissionRow(panel, popup, "full", "!", "Full access", "Unrestricted access to files and the internet.", true);
            popup.Child = CreatePopoverCard(panel, 430, false);
            popup.IsOpen = true;
        }

        private void AddPermissionRow(StackPanel panel, Popup popup, string mode, string glyph, string title, string description, bool danger)
        {
            string approval = mode == "full" ? "never" : "on-request";
            string sandbox = mode == "read-only" ? "read-only" : (mode == "full" ? "danger-full-access" : "workspace-write");
            if (!_capabilities.Allows(approval, sandbox)) return;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = glyph, Foreground = danger ? CoralBrush : MutedBrush, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            var copy = new StackPanel();
            copy.Children.Add(new TextBlock { Text = title, Foreground = danger ? CoralBrush : TextBrush, FontSize = 14, FontWeight = FontWeights.SemiBold });
            copy.Children.Add(new TextBlock { Text = description, Foreground = danger ? Brush("#FF9D82") : MutedBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 0) });
            Grid.SetColumn(copy, 1);
            row.Children.Add(copy);
            if (String.Equals(_runtimeSettings.PermissionMode, mode, StringComparison.Ordinal))
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
                _runtimeSettings.PermissionMode = Convert.ToString(((Button)source).Tag);
                _settingsStore.Save(_runtimeSettings);
                UpdateRuntimeButtons();
                popup.IsOpen = false;
            };
            panel.Children.Add(button);
        }

        private Popup CreatePopover(UIElement target, double width, bool alignRight)
        {
            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Custom,
                AllowsTransparency = true,
                StaysOpen = false,
                PopupAnimation = PopupAnimation.Fade,
                Width = width
            };
            popup.CustomPopupPlacementCallback = delegate(Size popupSize, Size targetSize, Point offset)
            {
                double x = alignRight ? targetSize.Width - popupSize.Width : 0;
                return new[] { new CustomPopupPlacement(new Point(x, -popupSize.Height - 5), PopupPrimaryAxis.None) };
            };
            popup.Closed += delegate { if (ReferenceEquals(_activePopup, popup)) _activePopup = null; };
            return popup;
        }

        private FrameworkElement CreatePopoverCard(UIElement content, double width, bool alignRight)
        {
            var root = new Grid { Width = width };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(9) });
            var card = new Border
            {
                Width = width,
                Background = Gradient("#2A2730", "#211E27", 90),
                BorderBrush = Brush("#4A4651"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(17),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 26, ShadowDepth = 8, Opacity = 0.48 },
                Child = content
            };
            root.Children.Add(card);
            var pointer = new Polygon
            {
                Width = 18,
                Height = 9,
                Stretch = Stretch.Fill,
                Points = new PointCollection { new Point(0, 0), new Point(18, 0), new Point(9, 9) },
                Fill = Brush("#211E27"),
                Stroke = Brush("#4A4651"),
                StrokeThickness = 1,
                HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = alignRight ? new Thickness(0, -1, 34, 0) : new Thickness(34, -1, 0, 0)
            };
            Grid.SetRow(pointer, 1);
            root.Children.Add(pointer);
            return root;
        }

        private void CloseActivePopup()
        {
            if (_activePopup != null) _activePopup.IsOpen = false;
            _activePopup = null;
        }

        private async void ChooseModelClick(object sender, RoutedEventArgs e)
        {
            var choices = new List<ChoiceItem>();
            foreach (ModelOption model in _capabilities.Models)
                choices.Add(new ChoiceItem(model.Id, model.ToString(), model.Description));
            string result = await ShowChoiceAsync("Pick a model", "Only models available to this account are shown.", choices);
            if (String.IsNullOrWhiteSpace(result)) return;
            bool leavingLocal = _runtimeSettings.UsesLocalModel;
            if (leavingLocal) _localAi.Stop();
            _runtimeSettings.ModelProvider = "openai";
            _runtimeSettings.LocalBaseUrl = null;
            _runtimeSettings.Model = result;
            ModelOption selected = SelectedModel();
            _runtimeSettings.ReasoningEffort = selected == null ? null : selected.DefaultReasoningEffort;
            NormalizeParallelForModel(selected);
            UpdateRuntimeButtons();
            _settingsStore.Save(_runtimeSettings);
            if (leavingLocal) PrepareNewThread();
        }

        private async void ChooseEffortClick(object sender, RoutedEventArgs e)
        {
            ModelOption model = SelectedModel();
            if (model == null) return;
            var choices = new List<ChoiceItem>();
            foreach (ReasoningOption effort in model.ReasoningEfforts)
                choices.Add(new ChoiceItem(effort.Id, Friendly(effort.Id), effort.Description));
            string result = await ShowChoiceAsync("Thinking level", "More thinking can take longer.", choices);
            if (String.IsNullOrWhiteSpace(result)) return;
            _runtimeSettings.ReasoningEffort = result;
            UpdateRuntimeButtons();
            _settingsStore.Save(_runtimeSettings);
        }

        private async void ChoosePermissionClick(object sender, RoutedEventArgs e)
        {
            var choices = new List<ChoiceItem>();
            if (_capabilities.Allows("on-request", "read-only"))
                choices.Add(new ChoiceItem("read-only", "Read only", "Inspect files without changing them."));
            if (_capabilities.Allows("on-request", "workspace-write"))
            {
                choices.Add(new ChoiceItem("ask", "Ask first", "Work in the project and ask before sensitive actions."));
                choices.Add(new ChoiceItem("auto-review", "Approve for me", "Let Codex review eligible approval requests."));
            }
            if (_capabilities.Allows("never", "danger-full-access"))
                choices.Add(new ChoiceItem("full", "Full access", "No sandbox and no approval prompts."));
            if (choices.Count == 0) choices.Add(new ChoiceItem("ask", "Server default", "Managed settings decide access."));
            string result = await ShowChoiceAsync("Project access", "This applies to new work and later turns.", choices);
            if (String.IsNullOrWhiteSpace(result)) return;
            _runtimeSettings.PermissionMode = result;
            UpdateRuntimeButtons();
            _settingsStore.Save(_runtimeSettings);
        }

        private async void AttachmentClick(object sender, RoutedEventArgs e)
        {
            var choices = new List<ChoiceItem>
            {
                new ChoiceItem("image", "Add image", "Attach a local PNG, JPEG, GIF, or WebP."),
                new ChoiceItem("skill", "Use a skill", "Add a local Codex skill to this message."),
                new ChoiceItem("clear", "Clear attachments", "Remove everything queued for this message.")
            };
            string result = await ShowChoiceAsync("Add to message", "Attachments stay local until you send.", choices);
            if (result == "image") AddImageAttachment();
            else if (result == "skill") await ChooseSkillAsync();
            else if (result == "clear") { _attachments.Clear(); UpdateAttachmentBadge(); }
        }

        private void AddImageAttachment()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose an image",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|All files|*.*",
                Multiselect = true
            };
            if (dialog.ShowDialog(this) != true) return;
            foreach (string path in dialog.FileNames)
            {
                _attachments.Add(new UserAttachment { Type = "localImage", Path = path, Name = Path.GetFileName(path) });
            }
            UpdateAttachmentBadge();
        }

        private async Task ChooseSkillAsync()
        {
            if (_client == null || !_client.IsRunning) return;
            try
            {
                SetStatus("Finding skills…", false);
                List<SkillOption> skills = await _client.ListSkillsAsync(_workingDirectory, false);
                var choices = new List<ChoiceItem>();
                foreach (SkillOption skill in skills)
                    if (skill.Enabled) choices.Add(new ChoiceItem(
                        skill.Path,
                        FriendlySkillName(skill.Name),
                        skill.Description,
                        skill.Name,
                        SkillIcon(skill.Name)));
                string path = await ShowChoiceAsync("Skills", choices.Count == 0 ? "No enabled skills found." : "Pick one for this message.", choices);
                if (String.IsNullOrWhiteSpace(path)) { SetStatus("Ready ✦", false); return; }
                SkillOption selected = skills.Find(delegate(SkillOption s) { return s.Path == path; });
                if (selected != null)
                {
                    _attachments.Add(new UserAttachment { Type = "skill", Path = selected.Path, Name = selected.Name });
                    if (!_composer.Text.StartsWith("$" + selected.Name, StringComparison.OrdinalIgnoreCase))
                        _composer.Text = "$" + selected.Name + " " + _composer.Text;
                    _composer.CaretIndex = _composer.Text.Length;
                    UpdateAttachmentBadge();
                }
                SetStatus("Ready ✦", false);
            }
            catch (Exception ex) { AddSystemCard("Skills unavailable", ex.Message); }
        }

        private void UpdateAttachmentBadge()
        {
            if (_attachmentBadge == null) return;
            _attachmentBadge.Text = _attachments.Count == 0 ? "" : _attachments.Count + " queued";
            _attachmentBadge.Visibility = _attachments.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void ThreadActionsClick(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var choices = new List<ChoiceItem>
            {
                new ChoiceItem("git", "Git snapshot", "Branch and changed files."),
                new ChoiceItem("worktree", "New worktree", "Create an isolated Git worktree."),
                new ChoiceItem("skills", "Skills", "Browse local skills."),
                new ChoiceItem("local-models", "Local models", "Match this PC with downloadable models and run one inside Legion.", null, "◆")
            };
            if (_localAi.IsRunning)
                choices.Add(new ChoiceItem("local-stop", "Stop local model", "Release its RAM and VRAM.", null, "■"));
            if (_updates.HasReleaseChannel)
                choices.Add(new ChoiceItem("updates", "Check for updates", "Install the newest GitHub release."));
            if (_managedBrowser.IsRunning)
                choices.Add(new ChoiceItem("browser-close", "Close managed browser", "End the isolated browser session and release its memory."));
            if (_managedBrowser.HasProfileData)
                choices.Add(new ChoiceItem("browser-forget", "Forget browser logins", "Clear cookies and site data saved only by Legion's browser."));
            if (_client != null && !String.IsNullOrWhiteSpace(_client.ThreadId))
            {
                choices.Insert(0, new ChoiceItem("rename", "Rename chat", "Give this conversation a name."));
                choices.Insert(1, new ChoiceItem("review", "Review changes", "Review uncommitted Git changes."));
                choices.Insert(2, new ChoiceItem("fork", "Fork chat", "Branch this conversation."));
                choices.Insert(3, new ChoiceItem("compact", "Compact context", "Free context while keeping the chat."));
                if (!String.IsNullOrWhiteSpace(_lastCompletedTurnId))
                    choices.Insert(4, new ChoiceItem("revert", "Undo last turn", "Remove the last turn from conversation history."));
                choices.Add(new ChoiceItem("archive", "Archive chat", "Move it out of the active list."));
                choices.Add(new ChoiceItem("delete", "Delete chat", "Permanently remove this conversation."));
            }
            string action = await ShowChoiceAsync("Chat tools", "A few useful controls, all in one place.", choices);
            if (String.IsNullOrWhiteSpace(action)) return;
            await RunThreadActionAsync(action);
        }

        private async Task RunThreadActionAsync(string action)
        {
            try
            {
                if (action == "rename")
                {
                    string name = await ShowTextPromptAsync("Rename chat", "Keep it short and useful.", "Chat name", false);
                    if (String.IsNullOrWhiteSpace(name)) return;
                    await _client.SetThreadNameAsync(name.Trim());
                    _thread.Text = name.Trim();
                    await RefreshHistoryAsync();
                }
                else if (action == "review")
                {
                    BeginAssistantBubble();
                    SetBusy(true);
                    await _client.StartReviewAsync();
                }
                else if (action == "fork")
                {
                    string id = await _client.ForkThreadAsync(_runtimeSettings, _workingDirectory);
                    _thread.Text = "Fork " + CompactId(id);
                    AddActivity("Forked", "You’re now in a new copy of this chat.");
                    await RefreshHistoryAsync();
                }
                else if (action == "compact")
                {
                    await _client.CompactThreadAsync();
                    AddActivity("Context", "Compaction started.");
                }
                else if (action == "revert")
                {
                    bool okay = await ShowPromptAsync("Undo the last turn?", "This changes chat history, not files on disk.", "Undo turn", "Keep it", true);
                    if (!okay) return;
                    await _client.RevertThreadAsync(_lastCompletedTurnId);
                    _lastCompletedTurnId = null;
                    await ReloadCurrentThreadAsync();
                }
                else if (action == "archive")
                {
                    bool okay = await ShowPromptAsync("Archive this chat?", "You can still find it through the server later.", "Archive", "Cancel", false);
                    if (!okay) return;
                    await _client.ArchiveThreadAsync();
                    _client.PrepareNewThread();
                    PrepareNewThread();
                    await RefreshHistoryAsync();
                }
                else if (action == "delete")
                {
                    bool okay = await ShowPromptAsync("Delete this chat?", "This cannot be undone.", "Delete", "Cancel", true);
                    if (!okay) return;
                    await _client.DeleteThreadAsync();
                    _client.PrepareNewThread();
                    PrepareNewThread();
                    await RefreshHistoryAsync();
                }
                else if (action == "git") await ShowGitSnapshotAsync();
                else if (action == "worktree") await CreateWorktreeAsync();
                else if (action == "skills") await BrowseSkillsAsync();
                else if (action == "local-models") await ShowLocalModelsAsync();
                else if (action == "local-stop")
                {
                    if (_localAiStartStop != null) _localAiStartStop.Cancel();
                    _localAi.Stop();
                    _runtimeSettings.LocalBaseUrl = null;
                    SetStatus("Local model stopped", false);
                    UpdateRuntimeButtons();
                }
                else if (action == "browser-close")
                {
                    await _managedBrowser.ExecuteAsync("close", new Dictionary<string, object>());
                    _browserApprovedForSession = false;
                    SetStatus("Ready ✦", false);
                }
                else if (action == "browser-forget")
                {
                    bool forget = await ShowPromptAsync(
                        "Forget managed browser logins?",
                        "This clears cookies and site data from Legion’s isolated browser profile. Your everyday browser is untouched.",
                        "Forget data",
                        "Keep it",
                        true);
                    if (!forget) return;
                    _managedBrowser.ClearProfileData();
                    _browserApprovedForSession = false;
                    SetStatus("Browser data cleared", false);
                }
                else if (action == "updates") await CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                SetBusy(false);
                AddSystemCard("That didn’t work", ex.Message);
            }
        }

        private async Task ReloadCurrentThreadAsync()
        {
            if (_client == null || String.IsNullOrWhiteSpace(_client.ThreadId)) return;
            List<ConversationMessage> history = await _client.LoadMessagesAsync(_client.ThreadId);
            _messages.Children.Clear();
            ResetParallelTurnUi();
            foreach (ConversationMessage message in history) AddBubble(message.IsUser ? "You" : "Codex", message.Text, message.IsUser);
        }

        private async Task ShowGitSnapshotAsync()
        {
            GitSnapshot snapshot = await _git.ReadAsync(_workingDirectory);
            string title = snapshot.IsRepository ? (snapshot.Branch + " · " + snapshot.ChangedFiles + " changed") : "No Git repository";
            await ShowPromptAsync(title, snapshot.StatusText, "Done", "Close", false);
        }

        private async Task CreateWorktreeAsync()
        {
            string branch = await ShowTextPromptAsync("New worktree", "This creates a branch in an app-managed folder.", "branch-name", false);
            if (String.IsNullOrWhiteSpace(branch)) return;
            bool okay = await ShowPromptAsync("Create worktree?", branch, "Create", "Cancel", false);
            if (!okay) return;
            SetStatus("Creating worktree…", false);
            string path = await _git.CreateManagedWorktreeAsync(_workingDirectory, branch.Trim());
            _workingDirectory = path;
            UpdateProjectDisplay();
            PrepareNewThread();
            SetStatus("Worktree ready", false);
        }

        private async Task BrowseSkillsAsync()
        {
            List<SkillOption> skills = await _client.ListSkillsAsync(_workingDirectory, true);
            var choices = new List<ChoiceItem>();
            foreach (SkillOption skill in skills)
                choices.Add(new ChoiceItem(
                    skill.Path,
                    (skill.Enabled ? "" : "Disabled · ") + FriendlySkillName(skill.Name),
                    skill.Description,
                    skill.Name,
                    SkillIcon(skill.Name)));
            await ShowChoiceAsync("Skills", choices.Count == 0 ? "No skills found." : skills.Count + " available", choices);
        }

        private async Task CheckForUpdatesAsync()
        {
            SetStatus("Checking GitHub…", false);
            UpdateCheckResult update = await _updates.CheckAsync();
            if (!update.IsConfigured)
            {
                SetStatus("Updates not connected yet", false);
                await ShowPromptAsync("Updates aren’t connected yet", "The GitHub release channel will be added when this build is published.", "Got it", "Close", false);
                return;
            }
            if (!String.IsNullOrWhiteSpace(update.Error))
            {
                SetStatus("Update check failed", true);
                AddSystemCard("Couldn’t check for updates", update.Error);
                return;
            }
            if (!update.IsUpdateAvailable)
            {
                SetStatus("You’re up to date", false);
                await ShowPromptAsync("You’re up to date", "Codex Legion " + update.CurrentVersion.ToString(3) + " is the latest version.", "Done", "Close", false);
                return;
            }

            string notes = String.IsNullOrWhiteSpace(update.Notes) ? "A new version is ready." : update.Notes.Trim();
            if (notes.Length > 900) notes = notes.Substring(0, 897) + "…";
            bool download = await ShowPromptAsync(
                "Codex Legion " + update.LatestVersion.ToString(3),
                notes,
                "Download update",
                "Not now",
                false);
            if (!download) { SetStatus("Ready ✦", false); return; }

            SetStatus("Downloading update…", false);
            string installer = await _updates.DownloadInstallerAsync(update);
            bool install = await ShowPromptAsync(
                "Update verified",
                "The installer matches the SHA-256 digest published by GitHub.",
                "Install now",
                "Later",
                false);
            if (!install) { SetStatus("Update downloaded", false); return; }
            Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
            AnimateClose();
        }

        private async Task<string> ShowChoiceAsync(string title, string message, IList<ChoiceItem> choices)
        {
            if (_root == null || _promptOpen) return null;
            _promptOpen = true;
            var completion = new TaskCompletionSource<string>();
            var overlay = CreateOverlay();
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = message, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 17) });
            var list = new StackPanel();
            foreach (ChoiceItem choice in choices)
            {
                var row = new StackPanel();
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
                if (!String.IsNullOrWhiteSpace(choice.Icon))
                {
                    titleRow.Children.Add(new Border
                    {
                        Width = 27,
                        Height = 27,
                        CornerRadius = new CornerRadius(9, 4, 9, 4),
                        Background = Brush("#302641"),
                        BorderBrush = Brush("#55416F"),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, 10, 0),
                        Child = new TextBlock
                        {
                            Text = choice.Icon,
                            Foreground = CoralBrush,
                            FontSize = choice.Icon.Length > 2 ? 8 : 13,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    });
                }
                titleRow.Children.Add(new TextBlock
                {
                    Text = choice.Label,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TextBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
                row.Children.Add(titleRow);
                if (!String.IsNullOrWhiteSpace(choice.Description)) row.Children.Add(new TextBlock
                {
                    Text = choice.Description,
                    Foreground = MutedBrush,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(String.IsNullOrWhiteSpace(choice.Icon) ? 0 : 37, 3, 0, 0)
                });
                var button = MakeButton("", false);
                button.Content = row;
                button.Tag = choice.Key;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 0, 0, 7);
                list.Children.Add(button);
            }
            var scroll = new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            content.Children.Add(scroll);
            var cancel = MakeButton("Close", false);
            cancel.HorizontalAlignment = HorizontalAlignment.Right;
            cancel.Margin = new Thickness(0, 10, 0, 0);
            content.Children.Add(cancel);
            var card = CreateOverlayCard(content, 520);
            overlay.Children.Add(card);
            _root.Children.Add(overlay);

            Action<string> dismiss = null;
            bool closing = false;
            dismiss = value =>
            {
                if (closing) return;
                closing = true;
                var fade = new DoubleAnimation(overlay.Opacity, 0, TimeSpan.FromMilliseconds(110));
                fade.Completed += delegate { _root.Children.Remove(overlay); _promptOpen = false; completion.TrySetResult(value); };
                overlay.BeginAnimation(OpacityProperty, fade);
            };
            foreach (Button button in list.Children) button.Click += delegate(object sender, RoutedEventArgs e) { dismiss(Convert.ToString(((Button)sender).Tag)); };
            cancel.Click += delegate { dismiss(null); };
            overlay.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; dismiss(null); } };
            overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            overlay.Focus();
            return await completion.Task;
        }

        private Task<string> ShowTextPromptAsync(string title, string message, string hint, bool secret)
        {
            if (_root == null || _promptOpen) return Task.FromResult<string>(null);
            _promptOpen = true;
            var completion = new TaskCompletionSource<string>();
            var overlay = CreateOverlay();
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = message, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 17) });
            Control input;
            if (secret) input = MakePasswordBox(hint);
            else input = new TextBox { Background = RaisedBrush, Foreground = TextBrush, BorderBrush = BorderLineBrush, BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), FontSize = 14, ToolTip = hint };
            content.Children.Add(input);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = MakeButton("Cancel", false);
            var okay = MakeButton("Done", true);
            okay.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(cancel);
            actions.Children.Add(okay);
            content.Children.Add(actions);
            overlay.Children.Add(CreateOverlayCard(content, 460));
            _root.Children.Add(overlay);
            Action<string> dismiss = value => { _root.Children.Remove(overlay); _promptOpen = false; completion.TrySetResult(value); };
            cancel.Click += delegate { dismiss(null); };
            okay.Click += delegate { dismiss(secret ? ((PasswordBox)input).Password : ((TextBox)input).Text); };
            overlay.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; dismiss(null); } else if (e.Key == Key.Enter) { e.Handled = true; dismiss(secret ? ((PasswordBox)input).Password : ((TextBox)input).Text); } };
            overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            input.Focus();
            return completion.Task;
        }

        private Grid CreateOverlay()
        {
            var overlay = new Grid { Background = Brush("#D10A0810"), Opacity = 0, Focusable = true };
            Grid.SetRowSpan(overlay, 2);
            Panel.SetZIndex(overlay, 1000);
            return overlay;
        }

        private Border CreateOverlayCard(UIElement content, double width)
        {
            return new Border
            {
                Width = width,
                MaxHeight = Math.Max(460, ActualHeight - 90),
                Background = Gradient("#211A2D", "#15111F", 25),
                BorderBrush = Brush("#4A3B62"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24, 8, 24, 8),
                Padding = new Thickness(28),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = ColorFrom("#7C5CC4"), BlurRadius = 34, ShadowDepth = 0, Opacity = 0.34 },
                Child = content
            };
        }

        private static string Friendly(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "Default";
            return Char.ToUpperInvariant(value[0]) + value.Substring(1).Replace('-', ' ');
        }

        private static string FriendlySkillName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "Unnamed Skill";
            string name = value.Trim().TrimStart('$');
            int namespaceSeparator = name.LastIndexOf(':');
            if (namespaceSeparator >= 0 && namespaceSeparator + 1 < name.Length)
                name = name.Substring(namespaceSeparator + 1);
            if (name.StartsWith("artifact-template-", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("artifact-template-".Length);

            string lower = name.ToLowerInvariant();
            if (lower == "openai-docs") return "OpenAI Docs";
            if (lower == "imagegen") return "Image Creator";
            if (lower == "pdf") return "PDF Tools";
            if (lower == "computer-use") return "Computer Control";
            if (lower == "excel-live-control") return "Excel Live Control";

            string[] words = name.Replace('_', '-').Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            var builder = new StringBuilder();
            foreach (string word in words)
            {
                if (builder.Length > 0) builder.Append(' ');
                string normalized = word.ToLowerInvariant();
                if (normalized == "api" || normalized == "mcp" || normalized == "pdf" || normalized == "ui")
                    builder.Append(normalized.ToUpperInvariant());
                else if (normalized.Length > 0)
                    builder.Append(Char.ToUpperInvariant(normalized[0])).Append(normalized.Substring(1));
            }
            return builder.Length == 0 ? "Unnamed Skill" : builder.ToString();
        }

        private static string SkillIcon(string value)
        {
            string name = (value ?? "").ToLowerInvariant();
            if (name.Contains("analytics") || name.Contains("dashboard") || name.Contains("spreadsheet") || name.Contains("excel")) return "▦";
            if (name.Contains("business-review") || name.Contains("presentation") || name.Contains("slides")) return "▣";
            if (name.Contains("report") || name.Contains("document") || name.Contains("docx")) return "▤";
            if (name.Contains("experiment") || name.Contains("analysis")) return "⚗";
            if (name.Contains("image") || name.Contains("sketch") || name.Contains("draw")) return "✦";
            if (name.Contains("pdf")) return "PDF";
            if (name.Contains("computer")) return "⌁";
            if (name.Contains("calendar")) return "◷";
            if (name.Contains("site") || name.Contains("web")) return "◇";
            if (name.Contains("skill") || name.Contains("template")) return "✦";
            if (name.Contains("pet")) return "◉";
            return "◆";
        }

        private sealed class ChoiceItem
        {
            public string Key { get; private set; }
            public string Label { get; private set; }
            public string Description { get; private set; }
            public string Extra { get; private set; }
            public string Icon { get; private set; }
            public ChoiceItem(string key, string label, string description) : this(key, label, description, null, null) { }
            public ChoiceItem(string key, string label, string description, string extra) : this(key, label, description, extra, null) { }
            public ChoiceItem(string key, string label, string description, string extra, string icon)
            {
                Key = key; Label = label; Description = description; Extra = extra; Icon = icon;
            }
        }
    }
}
