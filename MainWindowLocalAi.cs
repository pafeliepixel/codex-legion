using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexNativeLite
{
    public sealed partial class MainWindow
    {
        private readonly EmbeddedStrataManager _localAi = new EmbeddedStrataManager();
        private readonly LocalModelStorage _localModelStorage = new LocalModelStorage();
        private CancellationTokenSource _localAiStartStop;
        private Button _localUnloadButton;
        private Border _localProgressCard;
        private ProgressBar _localProgressBar;
        private TextBlock _localProgressTitle;
        private TextBlock _localProgressStage;
        private TextBlock _localProgressDetails;
        private bool _localAiBusy;

        private async Task ShowLocalModelsAsync()
        {
            await ShowLocalModelsAsync(false);
        }

        private async Task ShowLocalModelsAsync(bool showAll)
        {
            string savedDirectory = _runtimeSettings.LocalModelDirectory;
            if (!String.IsNullOrWhiteSpace(savedDirectory)) _localAi.ModelDirectory = savedDirectory;
            LocalHardwareProfile hardware = LocalHardwareDetector.Detect(_localAi.ModelDirectory);
            var choices = new List<ChoiceItem>();
            List<LocalModelChoice> ranked = LocalModelCatalog.Rank(hardware);
            int shown = 0;
            foreach (LocalModelChoice model in ranked)
            {
                if (!showAll && (!LocalModelCatalog.IsRecommended(model, hardware) || shown >= 3)) continue;
                string rating = model.Rating(hardware);
                string icon = rating == "Comfortable" ? "✓" : (rating == "Tight fit" ? "~" : (rating == "Not supported" ? "×" : "!"));
                string label = showAll ? model.Name : ((shown == 0 ? "Best match · " : "Good fit · ") + model.Name);
                choices.Add(new ChoiceItem(model.Id, label, model.Recommendation(hardware), null, showAll ? icon : (shown == 0 ? "✦" : icon)));
                shown++;
            }
            if (showAll)
                choices.Add(new ChoiceItem("matches", "Back to best matches", "Show the models Legion picked for this PC.", null, "‹"));
            else
                choices.Add(new ChoiceItem("all", "Browse every model", shown == 0 ? "No good match was found. See the full catalog anyway." : "Larger and unsupported models live here.", null, "…"));
            long storedBytes = _localModelStorage.TotalBytes(_localAi.ModelDirectory);
            choices.Add(new ChoiceItem("storage", "Manage local storage", storedBytes > 0 ? LocalModelStorage.FormatBytes(storedBytes) + " in models and partial downloads" : "No model files yet", null, "▦"));
            choices.Add(new ChoiceItem("folder", "Model folder", CompactPath(_localAi.ModelDirectory), null, "▤"));
            if (_runtimeSettings.UsesLocalModel)
                choices.Add(new ChoiceItem("cloud", "Back to OpenAI models", "Stop the local engine and begin a new cloud chat.", null, "☁"));

            string selected = await ShowChoiceAsync(showAll ? "All local models" : "Best local matches", HardwareSummary(hardware, shown), choices);
            if (selected == "folder")
            {
                ChooseLocalModelFolder();
                await ShowLocalModelsAsync(showAll);
                return;
            }
            if (selected == "all") { await ShowLocalModelsAsync(true); return; }
            if (selected == "matches") { await ShowLocalModelsAsync(false); return; }
            if (selected == "storage") { await ShowLocalStorageAsync(); return; }
            if (selected == "cloud")
            {
                await UnloadLocalModelAsync("Cloud models ready", false);
                return;
            }
            LocalModelChoice modelChoice = LocalModelCatalog.Find(selected);
            if (modelChoice == null) return;
            await BeginLocalModelAsync(modelChoice, hardware, false, showAll);
        }

        private async Task BeginLocalModelAsync(LocalModelChoice modelChoice, LocalHardwareProfile hardware, bool resuming, bool showAll)
        {
            string ratingText = modelChoice.Rating(hardware);
            if (ratingText == "Not supported" || ratingText == "Not recommended" || ratingText == "Needs more disk")
            {
                bool continueAnyway = await ShowPromptAsync(
                    ratingText,
                    modelChoice.Recommendation(hardware) + "\n\nLegion can still ask the embedded engine for its exact plan, but it may refuse this hardware.",
                    "Check anyway",
                    "Pick another",
                    false);
                if (!continueAnyway) { if (!resuming) await ShowLocalModelsAsync(showAll); return; }
            }

            if (!_localAi.IsControlReady)
            {
                bool bootstrap = await ShowPromptAsync(
                    "Add Legion’s local engine?",
                    "Legion will download a 73 MB pinned Strata control bundle with its private Python runtime. It stays inside Legion and its SHA-256 is checked before anything runs.",
                    "Add local engine",
                    "Not now",
                    false);
                if (!bootstrap) return;
                SetStatus("Adding local AI…", false);
                await _localAi.EnsureControlAsync(delegate(int percent, string text)
                {
                    if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                    try { Dispatcher.BeginInvoke((Action)(() => SetStatus(text + " · " + percent + "%", false))); }
                    catch (InvalidOperationException) { }
                });
            }

            SetStatus("Checking " + modelChoice.Name + "…", false);
            await _localAi.PlanAsync(modelChoice, LocalAiActivity);
            AddActivity("Local plan", "The embedded engine built a machine-specific plan. It will verify the engine package and model files again while downloading.");

            if (!resuming)
            {
                bool download = await ShowPromptAsync(
                    "Bring " + modelChoice.Name + " home?",
                    "About " + modelChoice.DownloadGb.ToString("0.#") + " GB of model files, plus the hardware engine, will be downloaded to:\n" + _localAi.ModelDirectory
                        + "\n\n" + modelChoice.LicenseName + " apply to the model files. Downloads resume if Legion closes.",
                    "Download & start",
                    "Not now",
                    false);
                if (!download) { SetStatus("Ready ✦", false); return; }
            }

            if (_localAiStartStop != null) _localAiStartStop.Cancel();
            _localAiStartStop = new CancellationTokenSource();
            _localAiBusy = true;
            ShowLocalProgress(modelChoice);
            UpdateLocalAiControls();
            SetStatus("Preparing local model…", false);
            try
            {
                await _localAi.StartAsync(modelChoice, LocalAiActivity, LocalAiProgress, _localAiStartStop.Token);
                _runtimeSettings.ModelProvider = "strata";
                _runtimeSettings.LocalModelId = modelChoice.Id;
                _runtimeSettings.LocalModelDirectory = _localAi.ModelDirectory;
                _runtimeSettings.LocalBaseUrl = _localAi.BaseUrl;
                _runtimeSettings.ParallelMode = "solo";
                if (_runtimeSettings.ReasoningEffort != "none" && _runtimeSettings.ReasoningEffort != "low"
                    && _runtimeSettings.ReasoningEffort != "medium" && _runtimeSettings.ReasoningEffort != "high")
                    _runtimeSettings.ReasoningEffort = "medium";
                _settingsStore.Save(_runtimeSettings);
                PrepareNewThread();
                UpdateRuntimeButtons();
                CompleteLocalProgress(modelChoice.Name + " is ready", true);
                UpdateLocalAiControls();
                SetStatus("Local · " + modelChoice.Name, false);
                AddSystemCard("Local model ready", modelChoice.Name + " is running inside Legion on " + _localAi.BaseUrl + ". Start a new chat whenever you’re ready.");
            }
            catch (OperationCanceledException)
            {
                _localAi.Stop();
                CompleteLocalProgress("Stopped for now · your download can resume later", false);
                SetStatus("Local model stopped", false);
            }
            catch
            {
                CompleteLocalProgress("Download stopped with an error · saved chunks are still here", false);
                throw;
            }
            finally
            {
                _localAiBusy = false;
                if (_localAiStartStop != null) { _localAiStartStop.Dispose(); _localAiStartStop = null; }
                UpdateLocalAiControls();
            }
        }

        private Button BuildLocalUnloadButton()
        {
            _localUnloadButton = MakeButton("■  Unload local model", false);
            _localUnloadButton.Margin = new Thickness(0, 0, 8, 0);
            _localUnloadButton.Visibility = Visibility.Collapsed;
            _localUnloadButton.ToolTip = "Release local-model RAM and VRAM without deleting its files";
            _localUnloadButton.Click += async delegate { await UnloadLocalModelAsync("Local model unloaded", true); };
            return _localUnloadButton;
        }

        private void UpdateLocalAiControls()
        {
            if (_localUnloadButton == null) return;
            bool visible = _localAi.IsRunning || _localAiBusy;
            _localUnloadButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _localUnloadButton.IsEnabled = visible;
            _localUnloadButton.Content = _localAiBusy && !_localAi.IsReady ? "■  Stop for now" : "■  Unload local model";
        }

        private async Task UnloadLocalModelAsync(string status, bool addCard)
        {
            if (_localAiStartStop != null) _localAiStartStop.Cancel();
            _localAiBusy = false;
            SetStatus("Unloading local model…", false);
            bool portClosed = await _localAi.UnloadAsync();
            _runtimeSettings.ModelProvider = "openai";
            _runtimeSettings.LocalBaseUrl = null;
            _settingsStore.Save(_runtimeSettings);
            if (_client != null && !String.IsNullOrWhiteSpace(_client.ThreadId) && String.IsNullOrWhiteSpace(_client.ActiveTurnId))
                PrepareNewThread();
            UpdateRuntimeButtons();
            UpdateLocalAiControls();
            SetStatus(portClosed ? status : "Local engine is still closing…", !portClosed);
            if (addCard) AddSystemCard(portClosed ? "Local model unloaded" : "Local model is still closing", portClosed
                ? "RAM, VRAM, and Legion’s local AI port are free. Your model files are still on disk."
                : "Legion stopped the launcher, but the local port has not cleared yet. Closing Legion will terminate the remaining process tree.");
        }

        private async Task ShowLocalStorageAsync()
        {
            List<LocalModelInstall> installs = _localModelStorage.Discover(_localAi.ModelDirectory, _localAi.ActiveModel == null ? null : _localAi.ActiveModel.Id);
            if (installs.Count == 0)
            {
                await ShowPromptAsync("Local storage is empty", "No downloaded or partial model files were found.", "Done", "Close", false);
                return;
            }
            var choices = new List<ChoiceItem>();
            for (int index = 0; index < installs.Count; index++)
            {
                LocalModelInstall install = installs[index];
                string state = install.IsPartial ? "Partial download" : (install.IsReady ? "Ready" : "Model files");
                if (install.IsActive) state = "Running";
                choices.Add(new ChoiceItem("model:" + index, install.Name, state + " · " + LocalModelStorage.FormatBytes(install.Bytes), null,
                    install.IsActive ? "●" : (install.IsPartial ? "◔" : "◆")));
            }
            string selected = await ShowChoiceAsync("Local storage", LocalModelStorage.FormatBytes(SumBytes(installs)) + " on disk", choices);
            if (String.IsNullOrWhiteSpace(selected) || !selected.StartsWith("model:", StringComparison.Ordinal)) return;
            int selectedIndex;
            if (!Int32.TryParse(selected.Substring(6), out selectedIndex) || selectedIndex < 0 || selectedIndex >= installs.Count) return;
            LocalModelInstall target = installs[selectedIndex];
            var actions = new List<ChoiceItem>();
            if (target.IsPartial && target.Model != null) actions.Add(new ChoiceItem("resume", "Resume download", "Continue using the chunks already on disk.", null, "↻"));
            if (target.IsActive) actions.Add(new ChoiceItem("unload", "Unload model", "Release RAM and VRAM but keep every file.", null, "■"));
            actions.Add(new ChoiceItem("remove", target.IsPartial ? "Discard download" : "Remove model", "Permanently recover about " + LocalModelStorage.FormatBytes(target.Bytes) + ".", null, "×"));
            string action = await ShowChoiceAsync(target.Name, CompactPath(target.FolderPath), actions);
            if (action == "resume" && target.Model != null)
            {
                await StartLocalModelChoiceAsync(target.Model);
                return;
            }
            if (action == "unload")
            {
                await UnloadLocalModelAsync("Local model unloaded", true);
                return;
            }
            if (action != "remove") return;
            bool remove = await ShowPromptAsync(
                target.IsPartial ? "Discard this download?" : "Remove this model?",
                target.Name + " · " + LocalModelStorage.FormatBytes(target.Bytes) + "\n\nThis permanently removes only this model’s folder.",
                target.IsPartial ? "Discard files" : "Remove model",
                "Keep it",
                true);
            if (!remove) return;
            if (target.IsActive) await UnloadLocalModelAsync("Local model unloaded", false);
            long freeBefore = AvailableFreeSpace(target.FolderPath);
            long removed = _localModelStorage.Remove(_localAi.ModelDirectory, target.FolderPath);
            long freeAfter = AvailableFreeSpace(_localAi.ModelDirectory);
            long recovered = freeAfter > freeBefore ? freeAfter - freeBefore : 0;
            string recovery = recovered > 0
                ? LocalModelStorage.FormatBytes(recovered) + " of disk space recovered"
                : LocalModelStorage.FormatBytes(removed) + " of model files removed";
            SetStatus(recovery, false);
            await ShowPromptAsync("Space recovered", recovery + ". The local engine and other models are untouched.", "Done", "Close", false);
        }

        private async Task StartLocalModelChoiceAsync(LocalModelChoice modelChoice)
        {
            LocalHardwareProfile hardware = LocalHardwareDetector.Detect(_localAi.ModelDirectory);
            await BeginLocalModelAsync(modelChoice, hardware, true, false);
        }

        private static long SumBytes(IList<LocalModelInstall> installs)
        {
            long total = 0;
            foreach (LocalModelInstall install in installs) total += install.Bytes;
            return total;
        }

        private void ShowLocalProgress(LocalModelChoice model)
        {
            var panel = new StackPanel();
            _localProgressTitle = new TextBlock { Text = model.Name, Foreground = TextBrush, FontSize = 14, FontWeight = FontWeights.SemiBold };
            _localProgressStage = new TextBlock { Text = "Preparing the local engine…", Foreground = AccentTextBrush, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
            _localProgressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Value = 0, Foreground = CoralBrush, Background = Brush("#302841"), Margin = new Thickness(0, 12, 0, 8), IsIndeterminate = true };
            _localProgressDetails = new TextBlock { Text = "Downloads resume if you stop now.", Foreground = MutedBrush, FontSize = 10, TextWrapping = TextWrapping.Wrap };
            var stop = MakeButton("Stop for now", false);
            stop.HorizontalAlignment = HorizontalAlignment.Left;
            stop.Margin = new Thickness(0, 12, 0, 0);
            stop.Click += delegate
            {
                if (_localAiStartStop != null) _localAiStartStop.Cancel();
                stop.IsEnabled = false;
                stop.Content = "Stopping…";
                SetStatus("Stopping local AI safely…", false);
            };
            panel.Children.Add(_localProgressTitle);
            panel.Children.Add(_localProgressStage);
            panel.Children.Add(_localProgressBar);
            panel.Children.Add(_localProgressDetails);
            panel.Children.Add(stop);
            _localProgressCard = new Border
            {
                Background = Gradient("#211A30", "#171321", 12),
                BorderBrush = Brush("#5D4B7D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14, 5, 14, 5),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 80, 12),
                MaxWidth = 620,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = panel
            };
            _messages.Children.Add(_localProgressCard);
            AnimateIn(_localProgressCard, 8);
            ScrollToBottom();
        }

        private void LocalAiProgress(LocalDownloadProgress progress)
        {
            if (progress == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            try { Dispatcher.BeginInvoke((Action)(() =>
            {
                if (_localProgressStage == null) return;
                _localProgressStage.Text = progress.Stage + (String.IsNullOrWhiteSpace(progress.Label) ? "" : " · " + progress.Label);
                if (_localProgressBar != null)
                {
                    _localProgressBar.IsIndeterminate = !progress.IsDeterminate;
                    if (progress.IsDeterminate) _localProgressBar.Value = progress.Percent;
                }
                if (_localProgressDetails != null)
                {
                    var details = new StringBuilder();
                    if (progress.IsDeterminate) details.Append(LocalModelStorage.FormatBytes(progress.DownloadedBytes)).Append(" / ").Append(LocalModelStorage.FormatBytes(progress.TotalBytes)).Append(" · ").Append(progress.Percent).Append("%");
                    if (progress.BytesPerSecond > 1) details.Append(" · ").Append(LocalModelStorage.FormatBytes((long)progress.BytesPerSecond)).Append("/s");
                    if (progress.TimeRemaining.HasValue) details.Append(" · about ").Append(FormatRemaining(progress.TimeRemaining.Value)).Append(" left");
                    if (details.Length == 0) details.Append(progress.Message);
                    _localProgressDetails.Text = details.ToString();
                }
                SetStatus(progress.Stage + (progress.IsDeterminate ? " · " + progress.Percent + "%" : ""), progress.IsStalled);
            })); }
            catch (InvalidOperationException) { }
        }

        private void CompleteLocalProgress(string text, bool ready)
        {
            if (_localProgressStage != null) { _localProgressStage.Text = text; _localProgressStage.Foreground = ready ? Brush("#B9F6D1") : MutedBrush; }
            if (_localProgressBar != null) { _localProgressBar.IsIndeterminate = false; if (ready) _localProgressBar.Value = 100; }
            if (_localProgressDetails != null) _localProgressDetails.Text = ready ? "Loaded and ready for a new chat." : "Select the model again whenever you want to continue.";
        }

        private static string FormatRemaining(TimeSpan value)
        {
            if (value.TotalHours >= 1) return Math.Ceiling(value.TotalHours) + " hr";
            if (value.TotalMinutes >= 1) return Math.Ceiling(value.TotalMinutes) + " min";
            return Math.Max(1, Math.Ceiling(value.TotalSeconds)) + " sec";
        }

        private static long AvailableFreeSpace(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetPathRoot(full);
                return String.IsNullOrWhiteSpace(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
            }
            catch { return 0; }
        }

        private async Task CheckLocalComponentsAfterAppUpdateAsync()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            if (String.Equals(_runtimeSettings.LastComponentAuditVersion, version, StringComparison.OrdinalIgnoreCase)) return;
            if (!_localAi.HasInstalledControl)
            {
                _runtimeSettings.LastComponentAuditVersion = version;
                _settingsStore.Save(_runtimeSettings);
                return;
            }
            try
            {
                if (!_localAi.IsControlReady)
                {
                    bool refreshed = await OfferPinnedControlUpdateAsync();
                    if (!refreshed)
                    {
                        _runtimeSettings.LastComponentAuditVersion = version;
                        _settingsStore.Save(_runtimeSettings);
                        return;
                    }
                }
                SetStatus("Checking local AI components…", false);
                LocalComponentUpdateInfo info = await _localAi.CheckComponentUpdatesAsync(null);
                _runtimeSettings.LastComponentAuditVersion = version;
                _settingsStore.Save(_runtimeSettings);
                if (info.UpdateAvailable) await OfferLocalComponentUpdateAsync(info);
                else SetStatus("Ready ✦", false);
            }
            catch (Exception ex)
            {
                AddActivity("Local AI update check", ex.Message);
                SetStatus("Ready ✦", false);
            }
        }

        private async Task<bool> OfferPinnedControlUpdateAsync()
        {
            bool update = await ShowPromptAsync(
                "Local AI helper update",
                "Strata control " + EmbeddedStrataManager.ControlVersion + " is ready. It is pinned by Legion and verified before it runs.",
                "Update helper",
                "Later",
                false);
            if (!update) return false;
            if (_localAi.IsRunning || _localAiBusy) await UnloadLocalModelAsync("Local model unloaded for update", false);
            SetStatus("Updating the local AI helper…", false);
            await _localAi.EnsureControlAsync(delegate(int percent, string text)
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                try { Dispatcher.BeginInvoke((Action)(() => SetStatus(text + " · " + percent + "%", false))); }
                catch (InvalidOperationException) { }
            });
            SetStatus("Local AI helper updated", false);
            return true;
        }

        private async Task<bool> OfferLocalComponentUpdateAsync(LocalComponentUpdateInfo info)
        {
            bool update = await ShowPromptAsync(
                "Local AI update ready",
                info.Summary + "\n\nThe signed Strata feed matches the engine to this PC. Model weights stay untouched.",
                "Update local AI",
                "Later",
                false);
            if (!update) return false;
            if (_localAi.IsRunning || _localAiBusy) await UnloadLocalModelAsync("Local model unloaded for update", false);
            SetStatus("Updating local AI components…", false);
            await _localAi.UpdateComponentsAsync(LocalAiActivity);
            SetStatus("Local AI components updated", false);
            await ShowPromptAsync("Local AI updated", "The engine was verified and switched safely. Your models were not changed.", "Done", "Close", false);
            return true;
        }

        private void ChooseLocalModelFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Choose where Legion keeps local models";
                dialog.ShowNewFolderButton = true;
                dialog.SelectedPath = Directory.Exists(_localAi.ModelDirectory) ? _localAi.ModelDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _localAi.ModelDirectory = dialog.SelectedPath;
                _runtimeSettings.LocalModelDirectory = dialog.SelectedPath;
                _settingsStore.Save(_runtimeSettings);
            }
        }

        private void LocalAiActivity(string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            try { Dispatcher.BeginInvoke((Action)(() =>
            {
                string clean = line.Trim();
                if (clean.StartsWith("{") && clean.Length > 180) clean = "Working through the verified model plan…";
                if (clean.Length > 240) clean = clean.Substring(0, 240) + "…";
                SetStatus("Local AI · " + clean, false);
                AppendActivity("Local AI", clean + "\n");
            })); }
            catch (InvalidOperationException) { }
        }

        private static string HardwareSummary(LocalHardwareProfile hardware, int matchCount)
        {
            var builder = new StringBuilder();
            builder.Append("Detected · ");
            if (hardware.TotalMemoryGb > 0) builder.Append(Math.Round(hardware.TotalMemoryGb)).Append(" GB RAM");
            else builder.Append("RAM unavailable");
            if (hardware.Gpus.Count > 0)
            {
                builder.Append(" · ");
                for (int index = 0; index < hardware.Gpus.Count; index++)
                {
                    if (index > 0) builder.Append(", ");
                    LocalGpuInfo gpu = hardware.Gpus[index];
                    builder.Append(gpu.Name);
                    if (gpu.MemoryBytes > 0) builder.Append(" ").Append(Math.Round(gpu.MemoryBytes / 1073741824d)).Append(" GB");
                }
            }
            else builder.Append(" · no supported GPU found");
            if (hardware.FreeDiskGb > 0) builder.Append(" · ").Append(Math.Round(hardware.FreeDiskGb)).Append(" GB free");
            builder.Append("\n");
            builder.Append(matchCount > 0 ? "Ranked for this PC. The embedded engine runs one final check before downloading." : "No suitable match found. You can still browse the full catalog.");
            return builder.ToString();
        }

        private static string CompactPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return "Choose where the big files live.";
            return path.Length <= 70 ? path : "…" + path.Substring(path.Length - 69);
        }
    }
}
