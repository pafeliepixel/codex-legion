using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexNativeLite
{
    public sealed partial class MainWindow : Window
    {
        private static readonly Brush WindowBrush = Brush("#0C0A12");
        private static readonly Brush PanelBrush = Brush("#13101C");
        private static readonly Brush RaisedBrush = Brush("#1B1727");
        private static readonly Brush HoverBrush = Brush("#282139");
        private static readonly Brush BorderLineBrush = Brush("#352D49");
        private static readonly Brush TextBrush = Brush("#F8F5FF");
        private static readonly Brush MutedBrush = Brush("#A49BB7");
        private static readonly Brush AccentTextBrush = Brush("#C7B6FF");
        private static readonly Brush CoralBrush = Brush("#FF8B6B");
        private static readonly Brush AccentBrush = Gradient("#9C83F7", "#FF8B6B", 18);
        private static readonly Brush UserBrush = Gradient("#302342", "#251E39", 20);

        private CodexAppServerClient _client;
        private Grid _root;
        private StackPanel _messages;
        private ScrollViewer _scroll;
        private TextBox _composer;
        private Button _sendButton;
        private Button _stopButton;
        private Button _accountButton;
        private Button _importHistoryButton;
        private Button _maximizeButton;
        private TextBlock _status;
        private TextBlock _project;
        private TextBlock _memory;
        private TextBlock _thread;
        private StackPanel _historyPanel;
        private TextBlock _streamingText;
        private Border _streamingBubble;
        private string _workingDirectory;
        private bool _busy;
        private bool _authenticated;
        private bool _loginPending;
        private bool _promptOpen;
        private readonly ProfileSecurity _profileSecurity;
        private readonly DispatcherTimer _metricsTimer = new DispatcherTimer();

        public MainWindow()
        {
            _profileSecurity = new ProfileSecurity();
            _settingsStore.Load(_runtimeSettings);
            Title = "Codex Legion";
            Width = 1120;
            Height = 760;
            MinWidth = 820;
            MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = WindowBrush;
            Foreground = TextBrush;
            FontFamily = new FontFamily("Segoe UI Variable Text");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(10),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            _workingDirectory = null;
            Content = BuildLayout();
            _composer.IsEnabled = false;
            _sendButton.IsEnabled = false;
            _importHistoryButton.IsEnabled = false;

            Loaded += OnLoaded;
            Closed += OnClosed;
            _metricsTimer.Interval = TimeSpan.FromSeconds(2);
            _metricsTimer.Tick += delegate { UpdateMemory(); };
        }

        private UIElement BuildLayout()
        {
            _root = new Grid { Background = WindowBrush };
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            _root.Children.Add(titleBar);

            var shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(shell, 1);
            _root.Children.Add(shell);

            var sidebar = BuildSidebar();
            Grid.SetColumn(sidebar, 0);
            shell.Children.Add(sidebar);

            var workspace = new Grid { Background = WindowBrush };
            workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });
            workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(workspace, 1);
            shell.Children.Add(workspace);

            var top = BuildTopBar();
            Grid.SetRow(top, 0);
            workspace.Children.Add(top);

            _messages = new StackPanel { Margin = new Thickness(26, 22, 26, 18) };
            _scroll = new ScrollViewer
            {
                Content = _messages,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(_scroll, 1);
            workspace.Children.Add(_scroll);

            var composer = BuildComposer();
            Grid.SetRow(composer, 2);
            workspace.Children.Add(composer);
            return _root;
        }

        private Border BuildTitleBar()
        {
            var grid = new Grid { Background = PanelBrush };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            brand.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(8, 4, 8, 4),
                Background = AccentBrush,
                Child = new TextBlock
                {
                    Text = "//",
                    Foreground = WindowBrush,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            brand.Children.Add(new TextBlock
            {
                Text = "Codex Legion",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });
            grid.Children.Add(brand);

            var mode = new TextBlock
            {
                Text = "SMALL APP  /  BIG IDEAS",
                Foreground = Brush("#716987"),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(mode, 1);
            grid.Children.Add(mode);

            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            var minimize = MakeWindowButton("\uE921", false);
            minimize.Click += delegate { AnimateMinimize(); };
            controls.Children.Add(minimize);

            _maximizeButton = MakeWindowButton("\uE922", false);
            _maximizeButton.Click += delegate { ToggleMaximize(); };
            controls.Children.Add(_maximizeButton);

            var close = MakeWindowButton("\uE8BB", true);
            close.Click += delegate { AnimateClose(); };
            controls.Children.Add(close);
            Grid.SetColumn(controls, 2);
            grid.Children.Add(controls);

            grid.MouseLeftButtonDown += TitleBarMouseLeftButtonDown;
            StateChanged += delegate
            {
                if (_maximizeButton != null)
                    _maximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
                if (WindowState != WindowState.Minimized) AnimateShellIn();
            };

            var frame = new Grid();
            frame.Children.Add(grid);
            frame.Children.Add(new Border
            {
                Height = 2,
                Background = AccentBrush,
                Opacity = 0.72,
                VerticalAlignment = VerticalAlignment.Top
            });

            return new Border
            {
                Background = PanelBrush,
                BorderBrush = BorderLineBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = frame
            };
        }

        private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is Button) return;
                source = VisualTreeHelper.GetParent(source);
            }
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void AnimateMinimize()
        {
            if (_root == null) { WindowState = WindowState.Minimized; return; }
            var fade = new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(90))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += delegate
            {
                _root.Opacity = 1;
                _root.BeginAnimation(OpacityProperty, null);
                WindowState = WindowState.Minimized;
            };
            _root.BeginAnimation(OpacityProperty, fade);
        }

        private void AnimateClose()
        {
            if (_root == null) { Close(); return; }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += delegate { Close(); };
            _root.BeginAnimation(OpacityProperty, fade);
        }

        private void AnimateShellIn()
        {
            if (_root == null) return;
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private Border BuildSidebar()
        {
            var panel = new Grid { Margin = new Thickness(14, 16, 14, 14) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var newChat = MakeButton("＋  New thread", true);
            newChat.Margin = new Thickness(0, 0, 0, 9);
            newChat.Click += delegate { PrepareNewThread(); };
            Grid.SetRow(newChat, 0);
            panel.Children.Add(newChat);

            var choose = MakeButton("◇  Choose folder", false);
            choose.Click += ChooseFolder;
            Grid.SetRow(choose, 1);
            panel.Children.Add(choose);

            var projectPanel = new StackPanel { Margin = new Thickness(2, 22, 2, 0) };
            projectPanel.Children.Add(Label("PROJECT", 0));
            _project = new TextBlock
            {
                Text = "Fresh start\nType below or choose a folder",
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                LineHeight = 18,
                MaxHeight = 58,
                Margin = new Thickness(0),
                ToolTip = _workingDirectory
            };
            projectPanel.Children.Add(new Border
            {
                Background = Brush("#191522"),
                BorderBrush = Brush("#302942"),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(10, 4, 10, 4),
                Padding = new Thickness(11, 9, 10, 9),
                Margin = new Thickness(-2, 8, -2, 0),
                Child = _project
            });
            Grid.SetRow(projectPanel, 2);
            panel.Children.Add(projectPanel);

            var historyHeader = new Grid { Margin = new Thickness(2, 23, 2, 8) };
            historyHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            historyHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            historyHeader.Children.Add(Label("HISTORY", 0));
            _importHistoryButton = MakeLinkButton("Import");
            _importHistoryButton.ToolTip = "Import conversation sessions from the official Codex app";
            _importHistoryButton.Click += ImportHistoryClick;
            Grid.SetColumn(_importHistoryButton, 1);
            historyHeader.Children.Add(_importHistoryButton);
            Grid.SetRow(historyHeader, 3);
            panel.Children.Add(historyHeader);

            _historyPanel = new StackPanel();
            ShowHistoryEmpty("Sign in to view history");
            _historySearch = new TextBox
            {
                Background = Brush("#191522"),
                Foreground = TextBrush,
                BorderBrush = Brush("#302942"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 9, 7),
                FontSize = 11,
                ToolTip = "Search chats",
                Margin = new Thickness(0, 0, 0, 8)
            };
            _historySearch.TextChanged += FilterHistory;
            var historyScroll = new ScrollViewer
            {
                Content = _historyPanel,
                Background = Brushes.Transparent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var historyArea = new Grid();
            historyArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            historyArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            historyArea.Children.Add(_historySearch);
            Grid.SetRow(historyScroll, 1);
            historyArea.Children.Add(historyScroll);
            Grid.SetRow(historyArea, 4);
            panel.Children.Add(historyArea);

            var sessionPanel = new StackPanel { Margin = new Thickness(2, 14, 2, 0) };
            sessionPanel.Children.Add(Label("SESSION", 0));
            _thread = new TextBlock
            {
                Text = "Not connected",
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0)
            };
            sessionPanel.Children.Add(_thread);
            Grid.SetRow(sessionPanel, 6);
            panel.Children.Add(sessionPanel);

            var footer = new StackPanel { Margin = new Thickness(2, 22, 2, 0) };
            _memory = new TextBlock { Text = "Measuring…", Foreground = MutedBrush, FontSize = 11 };
            footer.Children.Add(_memory);
            Grid.SetRow(footer, 7);
            panel.Children.Add(footer);
            return new Border
            {
                Background = PanelBrush,
                BorderBrush = BorderLineBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = panel
            };
        }

        private Border BuildTopBar()
        {
            var grid = new Grid { Margin = new Thickness(26, 0, 20, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = CoralBrush,
                Margin = new Thickness(0, 5, 9, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            titleRow.Children.Add(new TextBlock { Text = "Workspace", FontSize = 15, FontWeight = FontWeights.SemiBold });
            title.Children.Add(titleRow);
            _status = new TextBlock { Text = "Starting…", Foreground = MutedBrush, FontSize = 11, Margin = new Thickness(16, 3, 0, 0) };
            title.Children.Add(_status);
            grid.Children.Add(title);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _stopButton = MakeButton("Stop", false);
            _stopButton.Visibility = Visibility.Collapsed;
            _stopButton.VerticalAlignment = VerticalAlignment.Center;
            _stopButton.Click += async delegate { if (_client != null) await _client.InterruptAsync(); };
            actions.Children.Add(_stopButton);

            _threadActionsButton = MakeButton("Tools  ···", false);
            _threadActionsButton.Margin = new Thickness(8, 0, 0, 0);
            _threadActionsButton.VerticalAlignment = VerticalAlignment.Center;
            _threadActionsButton.Click += ThreadActionsClick;
            actions.Children.Add(_threadActionsButton);

            _accountButton = MakeButton("Sign in", false);
            _accountButton.Margin = new Thickness(8, 0, 0, 0);
            _accountButton.Click += AccountButtonClick;
            actions.Children.Add(_accountButton);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            return new Border
            {
                Background = WindowBrush,
                BorderBrush = BorderLineBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };
        }

        private Border BuildComposer()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _composer = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 46,
                MaxHeight = 150,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent,
                Foreground = TextBrush,
                CaretBrush = Brush("#FFB4A8"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 12, 14, 8),
                FontSize = 14,
                ToolTip = "Ctrl+Enter to send"
            };
            _composer.PreviewKeyDown += ComposerKeyDown;
            grid.Children.Add(_composer);

            var controls = new Grid { Margin = new Thickness(10, 2, 8, 8) };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _attachmentButton = MakeButton("＋", false);
            _attachmentButton.Width = 36;
            _attachmentButton.Height = 36;
            _attachmentButton.Padding = new Thickness(0);
            _attachmentButton.FontSize = 20;
            _attachmentButton.FontWeight = FontWeights.Light;
            _attachmentButton.Template = CreateButtonTemplate(new CornerRadius(18), HoverBrush, Brush("#302841"));
            _attachmentButton.Click += AttachmentClick;
            chips.Children.Add(_attachmentButton);
            _permissionButton = MakeButton("Ask first", false);
            _permissionButton.FontSize = 11;
            _permissionButton.Margin = new Thickness(7, 0, 0, 0);
            _permissionButton.Padding = new Thickness(12, 8, 12, 8);
            _permissionButton.Click += OpenPermissionPopover;
            chips.Children.Add(_permissionButton);
            _attachmentBadge = new TextBlock { Foreground = CoralBrush, FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            chips.Children.Add(_attachmentBadge);
            controls.Children.Add(chips);

            var rightControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _modelButton = MakeButton("Model  ·  Thinking ⌄", false);
            _modelButton.FontSize = 11;
            _modelButton.Padding = new Thickness(13, 8, 13, 8);
            _modelButton.Click += OpenModelPopover;
            rightControls.Children.Add(_modelButton);
            rightControls.Children.Add(BuildVoiceButton());
            _sendButton = MakeButton("↑", true);
            _sendButton.Width = 42;
            _sendButton.Height = 42;
            _sendButton.Padding = new Thickness(0);
            _sendButton.FontSize = 20;
            _sendButton.Margin = new Thickness(8, 0, 0, 0);
            _sendButton.Template = CreateButtonTemplate(new CornerRadius(21), Gradient("#B19AFD", "#FF9B7F", 18), Gradient("#856AD8", "#E47659", 18));
            _sendButton.VerticalAlignment = VerticalAlignment.Center;
            _sendButton.Click += async delegate { await SendAsync(); };
            rightControls.Children.Add(_sendButton);
            Grid.SetColumn(rightControls, 1);
            controls.Children.Add(rightControls);
            Grid.SetRow(controls, 1);
            grid.Children.Add(controls);

            var card = new Border
            {
                Background = Gradient("#1E192B", "#15121F", 0),
                BorderBrush = Brush("#413653"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24),
                Child = grid
            };
            return new Border { Padding = new Thickness(24, 10, 24, 22), Child = card };
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _metricsTimer.Start();
            if (_profileSecurity.RequiresPin)
            {
                ShowPinGate();
                return;
            }
            try { _profileSecurity.UnsealCredentials(); }
            catch (Exception ex)
            {
                AddSystemCard("Could not unlock saved login", ex.Message);
                SetStatus("Login vault error", true);
                return;
            }
            await ConnectAsync();
            _composer.Focus();
        }

        private async Task ConnectAsync()
        {
            try
            {
                SetStatus("Connecting to local Codex…", false);
                _client = new CodexAppServerClient();
                _client.NotificationReceived += ClientNotification;
                _client.ServerRequestReceived += ClientServerRequest;
                _client.LogReceived += ClientLog;
                _client.Disconnected += delegate { Dispatcher.BeginInvoke((Action)(() => SetStatus("Disconnected", true))); };
                await _client.StartAsync(_workingDirectory);
                AccountInfo account = await _client.ReadAccountAsync();
                ApplyAccount(account);
            }
            catch (Exception ex)
            {
                SetStatus("Could not connect", true);
                AddSystemCard("Connection failed", ex.Message + "\n\nMake sure the Codex CLI is installed and signed in.");
            }
        }

        private void PrepareNewThread()
        {
            if (_busy) return;
            if (!_authenticated)
            {
                ShowLoginView();
                return;
            }
            if (!_profileSecurity.HasPin)
            {
                ShowPinSetup();
                return;
            }
            if (String.IsNullOrWhiteSpace(_workingDirectory) || !Directory.Exists(_workingDirectory))
            {
                _thread.Text = "New chat";
                _thread.ToolTip = null;
                _composer.IsEnabled = true;
                _sendButton.IsEnabled = true;
                ShowProjectPickerView();
                SetStatus("Ready ✦", false);
                _composer.Focus();
                return;
            }
            try
            {
                if (_client == null || !_client.IsRunning)
                {
                    return;
                }
                _client.PrepareNewThread();
                _thread.Text = "New thread";
                _thread.ToolTip = null;
                _messages.Children.Clear();
                AddWelcome();
                _composer.IsEnabled = true;
                _sendButton.IsEnabled = true;
                SetStatus("Ready ✦", false);
            }
            catch (Exception ex)
            {
                SetStatus("Thread error", true);
                AddSystemCard("Could not start a thread", ex.Message);
            }
        }

        private async Task SendAsync()
        {
            string text = _composer.Text.Trim();
            if (!_authenticated || String.IsNullOrWhiteSpace(text) || _client == null || !_client.IsRunning) return;
            if (_busy && !String.IsNullOrWhiteSpace(_client.ActiveTurnId))
            {
                try
                {
                    _composer.Clear();
                    AddBubble("You · steering", text, true);
                    await _client.SteerTurnAsync(text);
                    SetStatus("Updated the plan…", false);
                }
                catch (Exception ex) { AddSystemCard("Could not steer", ex.Message); }
                return;
            }

            SetBusy(true);
            try
            {
                if (String.IsNullOrWhiteSpace(_workingDirectory) || !Directory.Exists(_workingDirectory))
                {
                    SetStatus("Making a workspace…", false);
                    _workingDirectory = StarterWorkspace.Create();
                    UpdateProjectDisplay();
                    _messages.Children.Clear();
                    AddWelcome();
                }
                if (String.IsNullOrWhiteSpace(_client.ThreadId))
                {
                    SetStatus("Starting…", false);
                    string id = await _client.StartThreadAsync(_workingDirectory, _runtimeSettings);
                    _thread.Text = "Thread " + CompactId(id);
                    _thread.ToolTip = id;
                }
                _composer.Clear();
                AddBubble("You", text, true);
                BeginAssistantBubble();
                var sentAttachments = new List<UserAttachment>(_attachments);
                _attachments.Clear();
                UpdateAttachmentBadge();
                await _client.StartTurnAsync(text, _runtimeSettings, _workingDirectory, sentAttachments);
            }
            catch (Exception ex)
            {
                AppendAssistant("Could not start the turn: " + ex.Message);
                SetBusy(false);
                SetStatus("Turn error", true);
            }
        }

        private void ClientNotification(object sender, ServerMessageEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)(() => HandleNotification(e.Message)));
        }

        private void HandleNotification(Dictionary<string, object> message)
        {
            string method = CodexAppServerClient.GetString(message, "method");
            var parameters = CodexAppServerClient.GetDictionary(message, "params");

            if (method == "item/agentMessage/delta")
            {
                AppendAssistant(CodexAppServerClient.GetString(parameters, "delta"));
            }
            else if (method == "item/started")
            {
                var item = CodexAppServerClient.GetDictionary(parameters, "item");
                string type = CodexAppServerClient.GetString(item, "type");
                if (type == "commandExecution")
                    AddActivity("Running", CodexAppServerClient.GetString(item, "command"));
                else if (type == "fileChange")
                    AddActivity("Editing", "Applying workspace changes");
                else if (type == "webSearch")
                    AddActivity("Searching", CodexAppServerClient.GetString(item, "query"));
                else if (type == "mcpToolCall")
                    AddActivity("App", CodexAppServerClient.GetString(item, "tool") ?? "Running an app tool");
                else if (type == "dynamicToolCall")
                    AddActivity("Browser", CodexAppServerClient.GetString(item, "tool") ?? "Using the managed browser");
                else if (type == "reasoning")
                    AddActivity("Thinking", "Working through the problem");
            }
            else if (method == "item/commandExecution/outputDelta")
            {
                string delta = CodexAppServerClient.GetString(parameters, "delta");
                if (!String.IsNullOrWhiteSpace(delta)) AppendActivity("Output", delta);
            }
            else if (method == "item/reasoning/summaryTextDelta" || method == "item/reasoning/textDelta" || method == "item/plan/delta")
            {
                string delta = CodexAppServerClient.GetString(parameters, "delta");
                if (!String.IsNullOrWhiteSpace(delta)) AppendActivity(method.IndexOf("plan", StringComparison.Ordinal) >= 0 ? "Plan" : "Thinking", delta);
            }
            else if (method == "turn/diff/updated")
            {
                string diff = CodexAppServerClient.GetString(parameters, "diff");
                AddDiffCard(diff);
            }
            else if (method == "turn/plan/updated")
            {
                AddActivity("Plan", "Plan updated");
            }
            else if (method == "thread/tokenUsage/updated")
            {
                var usage = CodexAppServerClient.GetDictionary(parameters, "tokenUsage");
                string total = CodexAppServerClient.GetString(usage, "totalTokens");
                if (!String.IsNullOrWhiteSpace(total)) _memory.ToolTip = "Thread tokens: " + total;
            }
            else if (method == "thread/compacted")
            {
                AddActivity("Context", "Conversation context compacted");
            }
            else if (method == "turn/started")
            {
                SetStatus("On it…", false);
            }
            else if (method == "turn/completed")
            {
                var turn = CodexAppServerClient.GetDictionary(parameters, "turn");
                string state = CodexAppServerClient.GetString(turn, "status") ?? "completed";
                _lastCompletedTurnId = CodexAppServerClient.GetString(turn, "id");
                SetBusy(false);
                SetStatus(state == "completed" ? "Ready ✦" : "Turn " + state, state == "failed");
                _streamingText = null;
                _streamingBubble = null;
                RefreshHistoryAfterTurn();
            }
            else if (method == "error")
            {
                var error = CodexAppServerClient.GetDictionary(parameters, "error");
                AddSystemCard("Codex error", CodexAppServerClient.GetString(error, "message") ?? "Unknown error");
            }
            else if (method == "account/login/completed" || method == "account/updated")
            {
                RefreshAccountAfterNotification();
            }
        }

        private async void RefreshAccountAfterNotification()
        {
            try
            {
                AccountInfo account = await _client.ReadAccountAsync();
                ApplyAccount(account);
            }
            catch (Exception ex)
            {
                SetStatus("Sign-in check failed", true);
                AddSystemCard("Authentication error", ex.Message);
            }
        }

        private async void AccountButtonClick(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsRunning || _busy) return;
            if (!_authenticated)
            {
                await StartLoginAsync();
                return;
            }

            bool answer = await ShowPromptAsync("Sign out?", "Your chats stay right here.", "Sign out", "Stay", false);
            if (!answer) return;

            try
            {
                SetStatus("Signing out…", false);
                await _client.LogoutAsync();
                _profileSecurity.ClearCredentials();
                ApplyAccount(null);
            }
            catch (Exception ex)
            {
                SetStatus("Sign-out failed", true);
                AddSystemCard("Could not sign out", ex.Message);
            }
        }

        private async Task StartLoginAsync()
        {
            if (_client == null || !_client.IsRunning || _loginPending) return;
            try
            {
                _loginPending = true;
                SetStatus("Starting secure sign-in…", false);
                LoginInfo login = await _client.StartChatGptLoginAsync();
                Process.Start(new ProcessStartInfo(login.AuthorizationUrl) { UseShellExecute = true });
                SetStatus("Finish sign-in in your browser", false);
                _accountButton.Content = "Waiting for sign-in…";
                _accountButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                _loginPending = false;
                _accountButton.IsEnabled = true;
                SetStatus("Sign-in failed", true);
                AddSystemCard("Could not start sign-in", ex.Message);
            }
        }

        private void ApplyAccount(AccountInfo account)
        {
            bool wasAuthenticated = _authenticated;
            _authenticated = account != null;
            _loginPending = false;
            _accountButton.IsEnabled = true;
            if (_authenticated)
            {
                string identity = !String.IsNullOrWhiteSpace(account.Email) ? account.Email :
                    (account.Type == "apiKey" ? "API key" : "ChatGPT");
                _accountButton.Content = "Account";
                _accountButton.ToolTip = identity;
                if (!_profileSecurity.HasPin)
                {
                    _composer.IsEnabled = false;
                    _sendButton.IsEnabled = false;
                    ShowPinSetup();
                    SetStatus("Set a weekly PIN", false);
                }
                else if (!wasAuthenticated)
                {
                    PrepareNewThread();
                    RefreshHistoryAfterAccount();
                    _importHistoryButton.IsEnabled = true;
                    OfferHistoryImportAfterSetup();
                    RefreshCapabilitiesAfterAccount();
                }
                else RefreshCapabilitiesAfterAccount();
            }
            else
            {
                _accountButton.Content = "Sign in";
                _accountButton.ToolTip = null;
                _thread.Text = "Signed out";
                _thread.ToolTip = "Credentials are isolated from the official Codex app.";
                _composer.IsEnabled = false;
                _sendButton.IsEnabled = false;
                _importHistoryButton.IsEnabled = false;
                ShowHistoryEmpty("Sign in to view history");
                ShowLoginView();
                SetStatus("Sign in to continue", false);
            }
        }

        private void ShowLoginView()
        {
            _messages.Children.Clear();
            var stage = new Grid
            {
                MaxWidth = 690,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 42, 0, 24)
            };
            stage.Children.Add(new Ellipse
            {
                Width = 330,
                Height = 330,
                Fill = new RadialGradientBrush(ColorFrom("#5C46B5"), Colors.Transparent),
                Opacity = 0.28,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -80, -100, 0)
            });
            stage.Children.Add(new Ellipse
            {
                Width = 180,
                Height = 180,
                Fill = new RadialGradientBrush(ColorFrom("#D65D48"), Colors.Transparent),
                Opacity = 0.2,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(-70, 0, 0, -55)
            });

            var panel = new StackPanel();
            panel.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brush("#261E37"),
                BorderBrush = Brush("#4B3C69"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10, 4, 10, 4),
                Padding = new Thickness(10, 5, 10, 5),
                Child = new TextBlock
                {
                    Text = "HEY THERE",
                    Foreground = AccentTextBrush,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                }
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Let’s make something.",
                FontSize = 32,
                LineHeight = 38,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 20, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Sign in to get started.",
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 21,
                MaxWidth = 470,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 15, 0, 24)
            });
            var signIn = MakeButton("Sign in with ChatGPT", true);
            signIn.HorizontalAlignment = HorizontalAlignment.Left;
            signIn.MinWidth = 210;
            signIn.Click += async delegate { await StartLoginAsync(); };
            panel.Children.Add(signIn);
            panel.Children.Add(new TextBlock
            {
                Text = "↗  Opens in your browser",
                Foreground = MutedBrush,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0)
            });
            stage.Children.Add(new Border
            {
                Background = Gradient("#1C1728", "#12101A", 25),
                BorderBrush = Brush("#3D3354"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(28, 9, 28, 9),
                Padding = new Thickness(44, 38, 44, 38),
                Margin = new Thickness(28),
                Child = panel
            });
            _messages.Children.Add(stage);
            AnimateIn(stage, 14);
            ScrollToBottom();
        }

        private void ShowProjectPickerView()
        {
            _messages.Children.Clear();
            var panel = new StackPanel
            {
                MaxWidth = 540,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0)
            };
            panel.Children.Add(new TextBlock
            {
                Text = "◇",
                Foreground = CoralBrush,
                FontSize = 26,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Start with an idea.",
                FontSize = 29,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 13, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Type below and Legion will make a workspace, or bring a folder you already have.",
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 21,
                Margin = new Thickness(0, 13, 0, 24)
            });
            var choose = MakeButton("Use an existing folder  ↗", true);
            choose.HorizontalAlignment = HorizontalAlignment.Left;
            choose.MinWidth = 230;
            choose.Click += ChooseFolder;
            panel.Children.Add(choose);
            var pickerCard = new Border
            {
                MaxWidth = 630,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 58, 0, 20),
                Background = Gradient("#1D1729", "#12101A", 24),
                BorderBrush = Brush("#403456"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(26, 8, 26, 8),
                Padding = new Thickness(42, 34, 42, 36),
                Child = panel
            };
            _messages.Children.Add(pickerCard);
            AnimateIn(pickerCard, 14);
            ScrollToBottom();
        }

        private void ShowPinGate()
        {
            _authenticated = false;
            _accountButton.Content = "Locked";
            _accountButton.IsEnabled = false;
            _composer.IsEnabled = false;
            _sendButton.IsEnabled = false;
            _thread.Text = "PIN required";
            SetStatus("Enter your weekly PIN", false);
            _messages.Children.Clear();

            var panel = new StackPanel { MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 70, 0, 20) };
            panel.Children.Add(new TextBlock
            {
                Text = "Unlock",
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Enter your PIN. You’re good for another week.",
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 12, 0, 20)
            });
            var pin = MakePasswordBox("4–12 digit PIN");
            panel.Children.Add(pin);
            var unlock = MakeButton("Unlock", true);
            unlock.Margin = new Thickness(0, 12, 0, 0);
            unlock.Click += async delegate
            {
                try
                {
                    if (!_profileSecurity.VerifyPin(pin.Password))
                    {
                        SetStatus("Incorrect PIN", true);
                        pin.Clear();
                        pin.Focus();
                        return;
                    }
                    _profileSecurity.UnsealCredentials();
                    SetStatus("PIN accepted", false);
                    await ConnectAsync();
                }
                catch (Exception ex)
                {
                    SetStatus("Could not unlock", true);
                    AddSystemCard("PIN unlock failed", ex.Message);
                }
            };
            panel.Children.Add(unlock);
            var reset = MakeButton("Forget saved login", false);
            reset.Margin = new Thickness(0, 10, 0, 0);
            reset.Click += async delegate
            {
                bool answer = await ShowPromptAsync("Forget this login?", "Your chats won’t be removed.", "Forget login", "Cancel", true);
                if (!answer) return;
                _profileSecurity.ResetPinAndCredentials();
                await ConnectAsync();
            };
            panel.Children.Add(reset);
            _messages.Children.Add(panel);
            AnimateIn(panel, 12);
            pin.Focus();
        }

        private void ShowPinSetup()
        {
            _messages.Children.Clear();
            var panel = new StackPanel { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 55, 0, 20) };
            panel.Children.Add(new TextBlock
            {
                Text = "Pick a PIN",
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Use 4–12 digits. We’ll ask again next week.",
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 12, 0, 20)
            });
            var pin = MakePasswordBox("Choose 4–12 digits");
            var confirm = MakePasswordBox("Confirm PIN");
            confirm.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(pin);
            panel.Children.Add(confirm);
            var save = MakeButton("Save PIN", true);
            save.Margin = new Thickness(0, 14, 0, 0);
            save.Click += delegate
            {
                try
                {
                    ProfileSecurity.ValidatePin(pin.Password);
                    if (pin.Password != confirm.Password) throw new ArgumentException("PIN entries do not match.");
                    _profileSecurity.SetPin(pin.Password);
                    pin.Clear();
                    confirm.Clear();
                    PrepareNewThread();
                    RefreshHistoryAfterAccount();
                    _importHistoryButton.IsEnabled = true;
                    SetStatus("Ready ✦", false);
                    OfferHistoryImportAfterSetup();
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, true);
                    pin.Clear();
                    confirm.Clear();
                    pin.Focus();
                }
            };
            panel.Children.Add(save);
            _messages.Children.Add(panel);
            AnimateIn(panel, 12);
            pin.Focus();
        }

        private async void RefreshHistoryAfterAccount()
        {
            await RefreshHistoryAsync();
        }

        private async void RefreshHistoryAfterTurn()
        {
            await RefreshHistoryAsync();
        }

        private async Task RefreshHistoryAsync()
        {
            if (!_authenticated || !_profileSecurity.HasPin || _client == null || !_client.IsRunning) return;
            try
            {
                List<ThreadSummary> threads = await _client.ListThreadsAsync();
                _historyCache = threads;
                RenderHistory();
            }
            catch (Exception ex)
            {
                _historyPanel.Children.Clear();
                _historyPanel.Children.Add(new TextBlock { Text = "History unavailable", Foreground = Brush("#FF8E82"), FontSize = 11 });
                Debug.WriteLine("history: " + ex.Message);
            }
        }

        private async Task OpenThreadAsync(ThreadSummary summary)
        {
            if (_busy || summary == null) return;
            SetBusy(true);
            SetStatus("Opening chat…", false);
            try
            {
                await _client.ResumeThreadAsync(summary.Id);
                List<ConversationMessage> history = await _client.LoadMessagesAsync(summary.Id);
                if (!String.IsNullOrWhiteSpace(summary.Cwd) && Directory.Exists(summary.Cwd))
                {
                    _workingDirectory = summary.Cwd;
                    UpdateProjectDisplay();
                }
                _messages.Children.Clear();
                foreach (ConversationMessage message in history)
                    AddBubble(message.IsUser ? "You" : "Codex", message.Text, message.IsUser);
                if (history.Count == 0) AddSystemCard("Empty conversation", "This thread does not contain saved messages yet.");
                _thread.Text = summary.Title;
                _thread.ToolTip = summary.Id;
                SetStatus("Ready ✦", false);
            }
            catch (Exception ex)
            {
                SetStatus("Could not load conversation", true);
                AddSystemCard("History error", ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void OfferHistoryImportAfterSetup()
        {
            if (_profileSecurity.HasOfferedHistoryImport || !_profileSecurity.HasOfficialHistory) return;
            _profileSecurity.MarkHistoryImportOffered();
            bool answer = await ShowPromptAsync(
                "Bring your old chats?",
                "Only conversations are copied. Everything else stays separate.",
                "Import chats",
                "Not now",
                false);
            if (answer) await ImportHistoryAsync();
        }

        private async void ImportHistoryClick(object sender, RoutedEventArgs e)
        {
            if (_busy || !_authenticated || !_profileSecurity.HasPin) return;
            bool answer = await ShowPromptAsync(
                "Import Codex chats?",
                "Existing chats are skipped. Logins and settings stay separate.",
                "Import chats",
                "Cancel",
                false);
            if (!answer) return;
            await ImportHistoryAsync();
        }

        private async Task ImportHistoryAsync()
        {
            _importHistoryButton.IsEnabled = false;
            SetStatus("Bringing chats over…", false);
            try
            {
                HistoryImportResult result = await Task.Run((Func<HistoryImportResult>)_profileSecurity.ImportOfficialHistory);
                await RefreshHistoryAsync();
                SetStatus(String.Format("{0} chats added · {1} skipped · {2} failed", result.Imported, result.Skipped, result.Failed), result.Failed > 0);
            }
            catch (Exception ex)
            {
                SetStatus("History import failed", true);
                AddSystemCard("Could not import history", ex.Message);
            }
            finally
            {
                _importHistoryButton.IsEnabled = _authenticated && _profileSecurity.HasPin;
            }
        }

        private Task<bool> ShowPromptAsync(string title, string message, string confirmText, string cancelText, bool danger)
        {
            if (_root == null || _promptOpen) return Task.FromResult(false);
            _promptOpen = true;
            var completion = new TaskCompletionSource<bool>();

            var overlay = new Grid
            {
                Background = Brush("#D10A0810"),
                Opacity = 0,
                Focusable = true
            };
            Grid.SetRowSpan(overlay, 2);
            Panel.SetZIndex(overlay, 1000);

            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold
            });
            var messageBlock = new TextBlock
            {
                Text = message,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 20
            };
            var messageScroll = new ScrollViewer
            {
                Content = messageBlock,
                MaxHeight = 220,
                Margin = new Thickness(0, 12, 0, 22),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            content.Children.Add(messageScroll);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancel = MakeButton(cancelText, false);
            cancel.MinWidth = 92;
            actions.Children.Add(cancel);
            var confirm = MakeButton(confirmText, true);
            confirm.MinWidth = 112;
            confirm.Margin = new Thickness(10, 0, 0, 0);
            if (danger)
            {
                confirm.Background = Gradient("#FF7A6D", "#E84158", 10);
                confirm.BorderBrush = CoralBrush;
            }
            actions.Children.Add(confirm);
            content.Children.Add(actions);

            var scale = new ScaleTransform(0.96, 0.96);
            var card = new Border
            {
                Width = 460,
                MaxWidth = 520,
                Background = Gradient("#211A2D", "#15111F", 25),
                BorderBrush = Brush("#4A3B62"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24, 8, 24, 8),
                Padding = new Thickness(30, 27, 30, 25),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new DropShadowEffect
                {
                    Color = ColorFrom("#7C5CC4"),
                    BlurRadius = 34,
                    ShadowDepth = 0,
                    Opacity = 0.34
                },
                Child = content
            };
            overlay.Children.Add(card);
            _root.Children.Add(overlay);

            bool closing = false;
            Action<bool> dismiss = null;
            dismiss = result =>
            {
                if (closing) return;
                closing = true;
                var fade = new DoubleAnimation(overlay.Opacity, 0, TimeSpan.FromMilliseconds(120));
                fade.Completed += delegate
                {
                    _root.Children.Remove(overlay);
                    _promptOpen = false;
                    completion.TrySetResult(result);
                };
                overlay.BeginAnimation(OpacityProperty, fade);
                AnimateScale(scale, 0.96, 120);
            };

            cancel.Click += delegate { dismiss(false); };
            confirm.Click += delegate { dismiss(true); };
            overlay.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == overlay) dismiss(false);
            };
            overlay.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; dismiss(false); }
                else if (e.Key == Key.Enter) { e.Handled = true; dismiss(true); }
            };

            overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            AnimateScale(scale, 1, 180);
            overlay.Focus();
            return completion.Task;
        }

        private void ClientServerRequest(object sender, ServerMessageEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)(() => HandleServerRequest(e.Message)));
        }

        private async void HandleServerRequest(Dictionary<string, object> message)
        {
            await HandleServerRequestAsync(message);
        }

        private void ClientLog(object sender, ServerLogEventArgs e)
        {
            Debug.WriteLine("app-server: " + e.Text);
        }

        private void ChooseFolder(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "Choose the project Codex can work in";
                dialog.SelectedPath = !String.IsNullOrWhiteSpace(_workingDirectory) && Directory.Exists(_workingDirectory)
                    ? _workingDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                _workingDirectory = dialog.SelectedPath;
                UpdateProjectDisplay();
                PrepareNewThread();
            }
        }

        private void UpdateProjectDisplay()
        {
            bool selected = !String.IsNullOrWhiteSpace(_workingDirectory) && Directory.Exists(_workingDirectory);
            _project.Text = selected ? ShortPath(_workingDirectory) : "Fresh start\nType below or choose a folder";
            _project.Foreground = selected ? TextBrush : MutedBrush;
            _project.ToolTip = selected ? _workingDirectory : null;
        }

        private async void ComposerKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                await SendAsync();
            }
        }

        private void AddWelcome()
        {
            var panel = new StackPanel { Margin = new Thickness(8, 32, 0, 28), HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(new TextBlock
            {
                Text = "WORKING IN  " + new DirectoryInfo(_workingDirectory).Name.ToUpperInvariant(),
                Foreground = CoralBrush,
                FontSize = 9,
                FontWeight = FontWeights.Bold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "What are we making?",
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Drop an idea below.",
                Foreground = MutedBrush,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            });
            _messages.Children.Add(panel);
            AnimateIn(panel, 10);
        }

        private void AddBubble(string author, string text, bool user)
        {
            var block = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 21,
                MaxWidth = 720
            };
            var card = new Border
            {
                Background = user ? UserBrush : RaisedBrush,
                BorderBrush = user ? Brush("#6B5794") : BorderLineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 12, 15, 12),
                Child = block,
                MaxWidth = 760,
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(user ? 80 : 0, 5, user ? 0 : 80, 14)
            };
            var wrapper = new StackPanel { HorizontalAlignment = card.HorizontalAlignment };
            wrapper.Children.Add(new TextBlock
            {
                Text = author,
                Foreground = user ? AccentTextBrush : MutedBrush,
                FontSize = 11,
                Margin = new Thickness(4, 0, 4, 5),
                HorizontalAlignment = card.HorizontalAlignment
            });
            wrapper.Children.Add(card);
            _messages.Children.Add(wrapper);
            AnimateIn(wrapper, 8);
            ScrollToBottom();
        }

        private void BeginAssistantBubble()
        {
            _streamingText = new TextBlock
            {
                Text = "",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 21,
                MaxWidth = 720
            };
            _streamingBubble = new Border
            {
                Background = RaisedBrush,
                BorderBrush = BorderLineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 12, 15, 12),
                Child = _streamingText,
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 5, 80, 14)
            };
            var wrapper = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            wrapper.Children.Add(new TextBlock { Text = "Codex", Foreground = MutedBrush, FontSize = 11, Margin = new Thickness(4, 0, 4, 5) });
            wrapper.Children.Add(_streamingBubble);
            _messages.Children.Add(wrapper);
            AnimateIn(wrapper, 8);
            ScrollToBottom();
        }

        private void AppendAssistant(string delta)
        {
            if (String.IsNullOrEmpty(delta)) return;
            if (_streamingText == null) BeginAssistantBubble();
            _streamingText.Text += delta;
            ScrollToBottom();
        }

        private void AddActivity(string action, string detail)
        {
            if (String.IsNullOrWhiteSpace(detail)) detail = "Working";
            if (detail.Length > 170) detail = detail.Substring(0, 167) + "…";
            var text = new TextBlock
            {
                Text = action + "  ·  " + detail.Replace("\r", " ").Replace("\n", " "),
                Foreground = MutedBrush,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            var activityCard = new Border
            {
                Background = PanelBrush,
                BorderBrush = BorderLineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 80, 10),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = text
            };
            _messages.Children.Add(activityCard);
            _liveActivityText = text;
            _liveActivityKind = action;
            AnimateIn(activityCard, 6);
            ScrollToBottom();
        }

        private void AddSystemCard(string title, string detail)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Brush("#FFB4A8") });
            panel.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush, Margin = new Thickness(0, 5, 0, 0) });
            var systemCard = new Border
            {
                Background = Brush("#2A1D1D"),
                BorderBrush = Brush("#5C3430"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13),
                Margin = new Thickness(0, 5, 60, 12),
                Child = panel
            };
            _messages.Children.Add(systemCard);
            AnimateIn(systemCard, 8);
            ScrollToBottom();
        }

        private static void AnimateIn(FrameworkElement element, double distance)
        {
            element.Opacity = 0;
            var slide = new TranslateTransform(0, distance);
            element.RenderTransform = slide;
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            bool canCompose = _authenticated && _profileSecurity.HasPin;
            _sendButton.IsEnabled = canCompose;
            _sendButton.Content = busy ? "↗" : "↑";
            _stopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _composer.IsEnabled = canCompose;
            if (!busy && canCompose) _composer.Focus();
        }

        private void SetStatus(string text, bool error)
        {
            _status.Text = text;
            _status.Foreground = error ? CoralBrush : MutedBrush;
        }

        private void ScrollToBottom()
        {
            Dispatcher.BeginInvoke((Action)(() => _scroll.ScrollToEnd()), DispatcherPriority.Background);
        }

        private void UpdateMemory()
        {
            long own = 0;
            try { own = Process.GetCurrentProcess().WorkingSet64; } catch { }
            long child = _client != null ? _client.WorkingSetBytes : 0;
            _memory.Text = String.Format("UI {0:0} MB · Codex {1:0} MB", own / 1048576.0, child / 1048576.0);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _metricsTimer.Stop();
            if (_voiceSession != null) _voiceSession.Dispose();
            _managedBrowser.Dispose();
            try { _settingsStore.Save(_runtimeSettings); } catch { }
            if (_client != null) _client.Dispose();
            try
            {
                if (_profileSecurity.HasPin) _profileSecurity.SealCredentials();
                else _profileSecurity.ClearCredentials();
            }
            catch { }
        }

        private static Button MakeButton(string text, bool accent)
        {
            var button = new Button
            {
                Content = text,
                Background = accent ? AccentBrush : RaisedBrush,
                Foreground = accent ? WindowBrush : TextBrush,
                BorderBrush = accent ? AccentTextBrush : BorderLineBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(13, 9, 13, 9),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };
            button.Template = CreateButtonTemplate(
                new CornerRadius(10, 4, 10, 4),
                accent ? (Brush)Gradient("#B19AFD", "#FF9B7F", 18) : HoverBrush,
                accent ? (Brush)Gradient("#856AD8", "#E47659", 18) : Brush("#302841"));
            AttachButtonMotion(button, 1.018);
            return button;
        }

        private static Button MakeLinkButton(string text)
        {
            var button = new Button
            {
                Content = text,
                Background = Brushes.Transparent,
                Foreground = AccentTextBrush,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 2, 6, 3),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };
            button.Template = CreateButtonTemplate(new CornerRadius(6, 2, 6, 2), Brush("#282038"), Brush("#352A49"));
            AttachButtonMotion(button, 1.04);
            return button;
        }

        private static Button MakeWindowButton(string glyph, bool close)
        {
            var button = new Button
            {
                Content = glyph,
                Width = 46,
                Height = 43,
                Background = Brushes.Transparent,
                Foreground = MutedBrush,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 10,
                Cursor = Cursors.Arrow,
                FocusVisualStyle = null
            };
            button.Template = CreateWindowButtonTemplate(close);
            AttachButtonMotion(button, 1.06);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            return button;
        }

        private static void AttachButtonMotion(Button button, double hoverScale)
        {
            var scale = new ScaleTransform(1, 1);
            button.RenderTransform = scale;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            button.MouseEnter += delegate
            {
                if (button.IsEnabled) AnimateScale(scale, hoverScale, 125);
            };
            button.MouseLeave += delegate { AnimateScale(scale, 1, 145); };
            button.PreviewMouseLeftButtonDown += delegate
            {
                if (button.IsEnabled) AnimateScale(scale, 0.965, 65);
            };
            button.PreviewMouseLeftButtonUp += delegate
            {
                if (button.IsEnabled) AnimateScale(scale, button.IsMouseOver ? hoverScale : 1, 105);
            };
        }

        private static void AnimateScale(ScaleTransform scale, double value, int milliseconds)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        }

        private static ControlTemplate CreateWindowButtonTemplate(bool close)
        {
            var border = new FrameworkElementFactory(typeof(Border)) { Name = "Chrome" };
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                close ? (Brush)Brush("#EE3F55") : Gradient("#3E315C", "#61456C", 0), "Chrome"));
            hover.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            hover.Setters.Add(new Setter(UIElement.EffectProperty, new DropShadowEffect
            {
                Color = close ? ColorFrom("#FF445B") : ColorFrom("#AE86FF"),
                BlurRadius = close ? 24 : 20,
                ShadowDepth = 0,
                Opacity = close ? 0.95 : 0.8
            }, "Chrome"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty,
                close ? (Brush)Brush("#C52E43") : Brush("#49365F"), "Chrome"));
            template.Triggers.Add(pressed);
            return template;
        }

        private static ControlTemplate CreateButtonTemplate(CornerRadius radius, Brush hover, Brush pressed)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, radius);
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            presenter.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, hover));
            template.Triggers.Add(hoverTrigger);
            var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, pressed));
            template.Triggers.Add(pressedTrigger);
            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.38));
            template.Triggers.Add(disabledTrigger);
            return template;
        }

        private void ShowHistoryEmpty(string text)
        {
            if (_historyPanel == null) return;
            _historyPanel.Children.Clear();
            _historyPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = MutedBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(3, 3, 3, 8)
            });
        }

        private static PasswordBox MakePasswordBox(string hint)
        {
            return new PasswordBox
            {
                Background = RaisedBrush,
                Foreground = TextBrush,
                BorderBrush = BorderLineBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                FontSize = 15,
                MaxLength = 12,
                ToolTip = hint
            };
        }

        private static TextBlock Label(string text, double top)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = MutedBrush,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(2, top, 0, 0)
            };
        }

        private static string CompactId(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) return "unknown";
            return id.Length <= 15 ? id : id.Substring(0, 8) + "…" + id.Substring(id.Length - 5);
        }

        private static string ShortPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return "No project";
            var info = new DirectoryInfo(path);
            return info.Name + "\n" + (info.Parent != null ? info.Parent.FullName : info.FullName);
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush(ColorFrom(hex));
        }

        private static Color ColorFrom(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static LinearGradientBrush Gradient(string from, string to, double angle)
        {
            return new LinearGradientBrush(ColorFrom(from), ColorFrom(to), angle);
        }
    }
}
