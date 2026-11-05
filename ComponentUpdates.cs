using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexNativeLite
{
    public sealed class ComponentStatus
    {
        public string Name { get; set; }
        public string CurrentVersion { get; set; }
        public string ExpectedVersion { get; set; }
        public string State { get; set; }
        public string Note { get; set; }
    }

    public sealed class ComponentAuditResult
    {
        public List<ComponentStatus> Components { get; private set; }
        public ComponentAuditResult() { Components = new List<ComponentStatus>(); }

        public string FriendlySummary()
        {
            var text = new StringBuilder();
            foreach (ComponentStatus component in Components)
            {
                if (text.Length > 0) text.AppendLine();
                string icon = component.State == "current" ? "✓" : (component.State == "not-installed" ? "○" : "↻");
                text.Append(icon).Append("  ").Append(component.Name).Append(" · ").Append(component.Note);
            }
            return text.ToString();
        }
    }

    public sealed class LocalComponentUpdateInfo
    {
        public bool CheckSucceeded { get; set; }
        public bool UpdateAvailable { get; set; }
        public string InstalledVersion { get; set; }
        public string OfferedVersion { get; set; }
        public long DownloadBytes { get; set; }
        public string Summary { get; set; }
        public string Raw { get; set; }
    }

    public static class ComponentAuditor
    {
        public static ComponentAuditResult Audit(string appDirectory, EmbeddedStrataManager localAi, LocalModelStorage storage, string modelDirectory)
        {
            var result = new ComponentAuditResult();
            string expectedRuntime = null;
            try
            {
                string channelPath = Path.Combine(appDirectory, "update-channel.json");
                if (File.Exists(channelPath))
                {
                    var json = new JavaScriptSerializer();
                    var channel = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(channelPath));
                    object raw;
                    if (channel.TryGetValue("runtimeVersion", out raw) && raw != null) expectedRuntime = Convert.ToString(raw);
                }
            }
            catch { }
            string installedRuntime = null;
            try
            {
                string marker = Path.Combine(appDirectory, "runtime", ".legion-runtime-version");
                if (File.Exists(marker)) installedRuntime = File.ReadAllText(marker).Trim();
            }
            catch { }
            bool runtimeCurrent = !String.IsNullOrWhiteSpace(expectedRuntime) && String.Equals(expectedRuntime, installedRuntime, StringComparison.OrdinalIgnoreCase);
            result.Components.Add(new ComponentStatus
            {
                Name = "Codex runtime", CurrentVersion = installedRuntime, ExpectedVersion = expectedRuntime,
                State = runtimeCurrent ? "current" : "repair",
                Note = runtimeCurrent ? "ready" : (String.IsNullOrWhiteSpace(installedRuntime) ? "added by the installer" : "refresh with the next installer")
            });
            result.Components.Add(new ComponentStatus
            {
                Name = "Strata control", CurrentVersion = localAi.InstalledControlVersion, ExpectedVersion = EmbeddedStrataManager.ControlVersion,
                State = localAi.IsControlReady ? "current" : (localAi.HasInstalledControl ? "update" : "not-installed"),
                Note = localAi.IsControlReady ? localAi.InstalledControlVersion + " · ready"
                    : (localAi.HasInstalledControl ? localAi.InstalledControlVersion + " → " + EmbeddedStrataManager.ControlVersion : "not installed")
            });
            result.Components.Add(new ComponentStatus
            {
                Name = "Local AI engine", CurrentVersion = localAi.InstalledEngineVersion,
                State = String.IsNullOrWhiteSpace(localAi.InstalledEngineVersion) ? "not-installed" : "check",
                Note = String.IsNullOrWhiteSpace(localAi.InstalledEngineVersion) ? "not installed" : localAi.InstalledEngineVersion + " · checking signed Strata feed"
            });
            List<LocalModelInstall> models = storage.Discover(modelDirectory, localAi.ActiveModel == null ? null : localAi.ActiveModel.Id);
            int ready = 0, partial = 0;
            long bytes = 0;
            foreach (LocalModelInstall model in models)
            {
                if (model.IsReady) ready++;
                if (model.IsPartial) partial++;
                bytes += model.Bytes;
            }
            result.Components.Add(new ComponentStatus
            {
                Name = "Local models", State = partial > 0 ? "attention" : "current",
                Note = ready + " ready" + (partial > 0 ? " · " + partial + " partial" : "") + " · " + LocalModelStorage.FormatBytes(bytes)
            });
            string browser = FindBrowser();
            string browserVersion = null;
            try { if (!String.IsNullOrWhiteSpace(browser)) browserVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(browser).FileVersion; } catch { }
            result.Components.Add(new ComponentStatus
            {
                Name = "Managed browser", CurrentVersion = browserVersion,
                State = String.IsNullOrWhiteSpace(browser) ? "attention" : "current",
                Note = String.IsNullOrWhiteSpace(browser) ? "Edge or Chrome was not found" : Path.GetFileNameWithoutExtension(browser) + (String.IsNullOrWhiteSpace(browserVersion) ? " · ready" : " " + browserVersion)
            });
            return result;
        }

        private static string FindBrowser()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
            };
            foreach (string candidate in candidates) if (File.Exists(candidate)) return candidate;
            return null;
        }
    }
}
