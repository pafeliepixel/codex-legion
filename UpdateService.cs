using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexNativeLite
{
    public sealed class UpdateCheckResult
    {
        public bool IsConfigured { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public Version CurrentVersion { get; set; }
        public Version LatestVersion { get; set; }
        public string Tag { get; set; }
        public string Notes { get; set; }
        public string ReleaseUrl { get; set; }
        public string InstallerUrl { get; set; }
        public string InstallerDigest { get; set; }
        public string DigestUrl { get; set; }
        public string Repository { get; set; }
        public string Error { get; set; }
    }

    public sealed class UpdateService
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public bool HasReleaseChannel
        {
            get
            {
                try
                {
                    Dictionary<string, object> channel = ReadChannel();
                    string repository = ReadString(channel, "repository");
                    return !String.IsNullOrWhiteSpace(repository) && repository.IndexOf('/') > 0;
                }
                catch { return false; }
            }
        }

        public Task<UpdateCheckResult> CheckAsync()
        {
            return Task.Factory.StartNew((Func<UpdateCheckResult>)Check, TaskCreationOptions.LongRunning);
        }

        public Task<string> DownloadInstallerAsync(UpdateCheckResult update)
        {
            return Task.Factory.StartNew((Func<string>)(() => DownloadInstaller(update)), TaskCreationOptions.LongRunning);
        }

        private UpdateCheckResult Check()
        {
            Version current = Assembly.GetExecutingAssembly().GetName().Version;
            var result = new UpdateCheckResult { CurrentVersion = current };
            try
            {
                Dictionary<string, object> channel = ReadChannel();
                string repository = ReadString(channel, "repository");
                string assetName = ReadString(channel, "installerAsset") ?? "CodexLegionSetup.exe";
                result.Repository = repository;
                if (String.IsNullOrWhiteSpace(repository) || repository.IndexOf('/') <= 0)
                {
                    result.IsConfigured = false;
                    return result;
                }
                result.IsConfigured = true;
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                string api = "https://api.github.com/repos/" + repository.Trim('/') + "/releases/latest";
                string raw;
                using (var web = CreateClient()) raw = web.DownloadString(api);
                var release = _json.Deserialize<Dictionary<string, object>>(raw);
                result.Tag = ReadString(release, "tag_name");
                result.Notes = ReadString(release, "body");
                result.ReleaseUrl = ReadString(release, "html_url");
                result.LatestVersion = ParseVersion(result.Tag);
                foreach (object rawAsset in Objects(release, "assets"))
                {
                    var asset = rawAsset as Dictionary<string, object>;
                    if (asset == null) continue;
                    string name = ReadString(asset, "name");
                    if (String.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
                    {
                        result.InstallerUrl = ReadString(asset, "browser_download_url");
                        result.InstallerDigest = ReadString(asset, "digest");
                    }
                    else if (String.Equals(name, assetName + ".sha256", StringComparison.OrdinalIgnoreCase))
                        result.DigestUrl = ReadString(asset, "browser_download_url");
                }
                if (result.LatestVersion == null) result.Error = "The latest release has no readable version tag.";
                else if (String.IsNullOrWhiteSpace(result.InstallerUrl)) result.Error = "The latest release has no " + assetName + " asset.";
                else result.IsUpdateAvailable = result.LatestVersion > current;
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                result.Error = response != null && response.StatusCode == HttpStatusCode.NotFound
                    ? "No published release was found yet."
                    : ex.Message;
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }

        private string DownloadInstaller(UpdateCheckResult update)
        {
            if (update == null || String.IsNullOrWhiteSpace(update.InstallerUrl))
                throw new ArgumentException("No installer is available for this update.");
            string tag = String.IsNullOrWhiteSpace(update.Tag) ? "latest" : SafeName(update.Tag);
            string directory = Path.Combine(Path.GetTempPath(), "CodexLegionUpdates", tag);
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "CodexLegionSetup.exe");
            string temporary = target + ".download";
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            using (var web = CreateClient()) web.DownloadFile(update.InstallerUrl, temporary);
            string digest = update.InstallerDigest;
            if (String.IsNullOrWhiteSpace(digest) && !String.IsNullOrWhiteSpace(update.DigestUrl))
            {
                using (var web = CreateClient())
                {
                    string sidecar = web.DownloadString(update.DigestUrl).Trim();
                    if (!String.IsNullOrWhiteSpace(sidecar)) digest = sidecar.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                }
            }
            VerifyDigest(temporary, digest);
            if (File.Exists(target)) File.Delete(target);
            File.Move(temporary, target);
            return target;
        }

        private Dictionary<string, object> ReadChannel()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update-channel.json");
            if (!File.Exists(path)) return new Dictionary<string, object>();
            return _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        }

        private static WebClient CreateClient()
        {
            var client = new WebClient { Encoding = Encoding.UTF8 };
            client.Headers[HttpRequestHeader.UserAgent] = "Codex-Legion-Updater";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            client.Headers["X-GitHub-Api-Version"] = "2026-03-10";
            return client;
        }

        private static void VerifyDigest(string path, string digest)
        {
            if (String.IsNullOrWhiteSpace(digest))
                throw new InvalidDataException("GitHub did not provide a SHA-256 digest for this installer.");
            string expected = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7) : digest;
            string actual;
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(path); } catch { }
                throw new InvalidDataException("The downloaded installer did not match its GitHub SHA-256 digest.");
            }
        }

        private static Version ParseVersion(string tag)
        {
            if (String.IsNullOrWhiteSpace(tag)) return null;
            string value = tag.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            int dash = value.IndexOf('-');
            if (dash >= 0) value = value.Substring(0, dash);
            Version parsed;
            return Version.TryParse(value, out parsed) ? parsed : null;
        }

        private static string SafeName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '-');
            return value;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object raw;
            return source != null && source.TryGetValue(key, out raw) && raw != null ? Convert.ToString(raw) : null;
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
    }
}
