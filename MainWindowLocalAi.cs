using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CodexNativeLite
{
    public sealed partial class MainWindow
    {
        private readonly EmbeddedStrataManager _localAi = new EmbeddedStrataManager();
        private CancellationTokenSource _localAiStartStop;

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
            if (selected == "cloud")
            {
                _localAi.Stop();
                _runtimeSettings.ModelProvider = "openai";
                _runtimeSettings.LocalBaseUrl = null;
                _settingsStore.Save(_runtimeSettings);
                PrepareNewThread();
                UpdateRuntimeButtons();
                SetStatus("Cloud models ready", false);
                return;
            }
            LocalModelChoice modelChoice = LocalModelCatalog.Find(selected);
            if (modelChoice == null) return;

            string ratingText = modelChoice.Rating(hardware);
            if (ratingText == "Not supported" || ratingText == "Not recommended" || ratingText == "Needs more disk")
            {
                bool continueAnyway = await ShowPromptAsync(
                    ratingText,
                    modelChoice.Recommendation(hardware) + "\n\nLegion can still ask the embedded engine for its exact plan, but it may refuse this hardware.",
                    "Check anyway",
                    "Pick another",
                    false);
                if (!continueAnyway) { await ShowLocalModelsAsync(showAll); return; }
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

            bool download = await ShowPromptAsync(
                "Bring " + modelChoice.Name + " home?",
                "About " + modelChoice.DownloadGb.ToString("0.#") + " GB of model files, plus the hardware engine, will be downloaded to:\n" + _localAi.ModelDirectory
                    + "\n\n" + modelChoice.LicenseName + " apply to the model files. Downloads resume if Legion closes.",
                "Download & start",
                "Not now",
                false);
            if (!download) { SetStatus("Ready ✦", false); return; }

            if (_localAiStartStop != null) _localAiStartStop.Cancel();
            _localAiStartStop = new CancellationTokenSource();
            SetStatus("Preparing local model…", false);
            try
            {
                await _localAi.StartAsync(modelChoice, LocalAiActivity, _localAiStartStop.Token);
                _runtimeSettings.ModelProvider = "strata";
                _runtimeSettings.LocalModelId = modelChoice.Id;
                _runtimeSettings.LocalModelDirectory = _localAi.ModelDirectory;
                _runtimeSettings.LocalBaseUrl = _localAi.BaseUrl;
                if (_runtimeSettings.ReasoningEffort != "none" && _runtimeSettings.ReasoningEffort != "low"
                    && _runtimeSettings.ReasoningEffort != "medium" && _runtimeSettings.ReasoningEffort != "high")
                    _runtimeSettings.ReasoningEffort = "medium";
                _settingsStore.Save(_runtimeSettings);
                PrepareNewThread();
                UpdateRuntimeButtons();
                SetStatus("Local · " + modelChoice.Name, false);
                AddSystemCard("Local model ready", modelChoice.Name + " is running inside Legion on " + _localAi.BaseUrl + ". Start a new chat whenever you’re ready.");
            }
            catch (OperationCanceledException)
            {
                _localAi.Stop();
                SetStatus("Local model stopped", false);
            }
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
