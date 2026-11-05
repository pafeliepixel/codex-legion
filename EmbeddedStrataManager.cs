using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexNativeLite
{
    public sealed class EmbeddedStrataManager : IDisposable
    {
        public const string UpstreamVersion = "0.2.10";
        public const string ControlVersion = "0.1.56";
        public const string ControlSha256 = "ef74f429c60341048a68249d41699142a1ec3716cee322621875e823f4ead873";
        public const long ControlDownloadBytes = 73292001;
        public const int ServerPort = 18347;
        private const string ControlUrl = "https://github.com/Niko1221/Strata/releases/download/v0.2.10/strata-control-0.1.56-windows-x86_64.zip";

        private readonly string _root;
        private readonly string _components;
        private readonly string _control;
        private Process _process;
        private IntPtr _job;
        private readonly object _logLock = new object();
        private readonly Queue<string> _recentLog = new Queue<string>();
        private bool _disposed;

        public EmbeddedStrataManager() : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLegion", "LocalAI"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Codex Legion", "Models"))
        {
        }

        internal EmbeddedStrataManager(string root, string modelDirectory)
        {
            _root = Path.GetFullPath(root);
            _components = Path.Combine(_root, "components");
            _control = Path.Combine(_components, "control", ControlVersion);
            ModelDirectory = Path.GetFullPath(modelDirectory);
        }

        public string ModelDirectory { get; set; }
        public string BaseUrl { get { return "http://127.0.0.1:" + ServerPort + "/v1"; } }
        public bool IsControlReady
        {
            get
            {
                return File.Exists(Path.Combine(_control, "strata.py"))
                    && File.Exists(Path.Combine(_control, "python", "python.exe"))
                    && File.Exists(Path.Combine(_control, "BUNDLE.json"));
            }
        }
        public bool IsRunning { get { try { return _process != null && !_process.HasExited; } catch { return false; } } }
        public bool IsReady { get; private set; }
        public LocalModelChoice ActiveModel { get; private set; }
        internal string ControlDirectory { get { return _control; } }

        internal void InstallControlArchiveForTest(string archive)
        {
            if (!String.Equals(HashFile(archive), ControlSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The test control archive does not match the pinned SHA-256.");
            InstallControlArchive(archive);
        }

        internal static void EnableTls12()
        {
            // .NET Framework applications can otherwise inherit the older TLS
            // default, which GitHub correctly refuses for release downloads.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public async Task EnsureControlAsync(Action<int, string> progress)
        {
            ThrowIfDisposed();
            if (IsControlReady) return;
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "downloads"));
            string archive = Path.Combine(_root, "downloads", "strata-control-" + ControlVersion + ".zip");
            if (!File.Exists(archive) || !String.Equals(HashFile(archive), ControlSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(archive)) File.Delete(archive);
                EnableTls12();
                try
                {
                    using (var client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = "Codex-Legion/0.6.1";
                        client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                        {
                            if (progress != null) progress(e.ProgressPercentage, "Downloading the local AI control bundle");
                        };
                        await client.DownloadFileTaskAsync(new Uri(ControlUrl), archive).ConfigureAwait(false);
                    }
                }
                catch (WebException ex)
                {
                    try { if (File.Exists(archive)) File.Delete(archive); } catch { }
                    throw new InvalidOperationException("Couldn’t download the local engine from GitHub. Check your connection and try again.", ex);
                }
            }
            string digest = HashFile(archive);
            if (!String.Equals(digest, ControlSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(archive); } catch { }
                throw new InvalidDataException("The local AI control download did not match its pinned SHA-256.");
            }
            if (progress != null) progress(100, "Checking and unpacking the control bundle");
            InstallControlArchive(archive);
            if (!IsControlReady) throw new InvalidDataException("The embedded local AI control bundle is incomplete.");
        }

        public async Task<string> PlanAsync(LocalModelChoice model, Action<string> activity)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (!IsControlReady) throw new InvalidOperationException("The local AI control bundle is not ready.");
            Directory.CreateDirectory(ModelDirectory);
            ProcessResult result = await RunOneShotAsync(BuildLaunchArguments(model, true), activity).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new InvalidOperationException(UsefulError(result));
            return String.IsNullOrWhiteSpace(result.Output) ? "The model plan is ready." : result.Output.Trim();
        }

        public async Task StartAsync(LocalModelChoice model, Action<string> activity, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (model == null) throw new ArgumentNullException("model");
            if (!IsControlReady) throw new InvalidOperationException("The local AI control bundle is not ready.");
            Stop();
            Directory.CreateDirectory(ModelDirectory);
            Directory.CreateDirectory(Path.Combine(_root, "install"));
            var start = CreateStartInfo(BuildLaunchArguments(model, false));
            _process = new Process { StartInfo = start, EnableRaisingEvents = true };
            _process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { OnLine(e.Data, activity); };
            _process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { OnLine(e.Data, activity); };
            if (!_process.Start()) throw new InvalidOperationException("Could not start Legion's local AI engine.");
            AttachJob(_process);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            ActiveModel = model;
            IsReady = false;

            DateTime deadline = DateTime.UtcNow.AddHours(24);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsRunning) throw new InvalidOperationException("The local model stopped before it was ready. " + RecentError());
                if (await HealthReadyAsync().ConfigureAwait(false))
                {
                    IsReady = true;
                    if (activity != null) activity(model.Name + " is ready on this PC.");
                    return;
                }
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException("The local model did not become ready.");
        }

        public void Stop()
        {
            IsReady = false;
            ActiveModel = null;
            Process process = _process;
            _process = null;
            try
            {
                if (process != null && !process.HasExited)
                {
                    try { process.CloseMainWindow(); } catch { }
                    if (!process.WaitForExit(1800)) process.Kill();
                }
            }
            catch { }
            finally { if (process != null) process.Dispose(); }
            if (_job != IntPtr.Zero)
            {
                CloseHandle(_job);
                _job = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        private string BuildLaunchArguments(LocalModelChoice model, bool plan)
        {
            var arguments = new List<string>
            {
                Path.Combine(_control, "strata.py"), "launch",
                "--family", model.Family,
                "--model", model.Model,
                "--data-dir", ModelDirectory,
                "--context", "32768",
                "--access", "local",
                "--port", ServerPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--no-tui", "--output", "json"
            };
            if (plan) arguments.Add("--plan");
            else arguments.Add("--yes");
            return JoinArguments(arguments);
        }

        private ProcessStartInfo CreateStartInfo(string arguments)
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(_control, "python", "python.exe"),
                Arguments = arguments,
                WorkingDirectory = _control,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            string profiles = Path.Combine(_root, "profiles");
            start.EnvironmentVariables["PYTHONUTF8"] = "1";
            start.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
            start.EnvironmentVariables.Remove("PYTHONHOME");
            start.EnvironmentVariables.Remove("PYTHONPATH");
            start.EnvironmentVariables["STRATA_COMPONENTS_DIR"] = _components;
            start.EnvironmentVariables["STRATA_PROFILE_DIR"] = profiles;
            start.EnvironmentVariables["STRATA_LAUNCH_PRESET_DIR"] = Path.Combine(_root, "presets");
            start.EnvironmentVariables["STRATA_RUNTIME_DIR"] = Path.Combine(_root, "runtime");
            start.EnvironmentVariables["STRATA_STATE_DIR"] = Path.Combine(_root, "state");
            start.EnvironmentVariables["STRATA_DATA_DIR"] = ModelDirectory;
            start.EnvironmentVariables["STRATA_INSTALL_DIR"] = Path.Combine(_root, "install");
            start.EnvironmentVariables["STRATA_SETTINGS_FILE"] = Path.Combine(_root, "settings.json");
            start.EnvironmentVariables["STRATA_CONNECTION_DIR"] = Path.Combine(_root, "connections");
            start.EnvironmentVariables["STRATA_HOME"] = _root;
            start.EnvironmentVariables["STRATA_DOCUMENTS_DIR"] = Path.Combine(_root, "documents");
            start.EnvironmentVariables["STRATA_EXIT_WITH_PARENT"] = "1";
            start.EnvironmentVariables["STRATA_APP_VERSION"] = UpstreamVersion;
            return start;
        }

        private async Task<ProcessResult> RunOneShotAsync(string arguments, Action<string> activity)
        {
            var start = CreateStartInfo(arguments);
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Could not start the embedded local AI control process.");
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                await Task.Run((Action)process.WaitForExit).ConfigureAwait(false);
                string stdout = await output.ConfigureAwait(false);
                string stderr = await error.ConfigureAwait(false);
                if (activity != null && !String.IsNullOrWhiteSpace(stderr)) activity(LastUsefulLine(stderr));
                return new ProcessResult { ExitCode = process.ExitCode, Output = stdout, Error = stderr };
            }
        }

        private async Task<bool> HealthReadyAsync()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + ServerPort + "/health");
                request.Method = "GET";
                request.Timeout = 1200;
                request.ReadWriteTimeout = 1200;
                request.Proxy = null;
                using (WebResponse response = await request.GetResponseAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string json = await reader.ReadToEndAsync().ConfigureAwait(false);
                    return json.IndexOf("\"loaded\": true", StringComparison.OrdinalIgnoreCase) >= 0
                        || json.IndexOf("\"loaded\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        private void InstallControlArchive(string archive)
        {
            string parent = Path.GetDirectoryName(_control);
            Directory.CreateDirectory(parent);
            // Keep staging transactional without spending 40+ characters of the
            // classic Windows path budget used by the bundled Python packages.
            string staging = Path.Combine(parent, ".s-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(staging);
            try
            {
                string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                using (ZipArchive zip = ZipFile.OpenRead(archive))
                {
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        string destination = Path.GetFullPath(Path.Combine(staging, relative));
                        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("The local AI control archive contains an unsafe path.");
                        if (String.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination, true);
                    }
                }
                if (!File.Exists(Path.Combine(staging, "strata.py")) || !File.Exists(Path.Combine(staging, "python", "python.exe")))
                    throw new InvalidDataException("The local AI control archive is missing its entry point.");
                if (Directory.Exists(_control)) Directory.Delete(_control, true);
                Directory.Move(staging, _control);
                staging = null;
            }
            finally
            {
                if (!String.IsNullOrWhiteSpace(staging) && Directory.Exists(staging))
                    try { Directory.Delete(staging, true); } catch { }
            }
        }

        private void OnLine(string line, Action<string> activity)
        {
            if (String.IsNullOrWhiteSpace(line)) return;
            string clean = line.Trim();
            lock (_logLock)
            {
                _recentLog.Enqueue(clean);
                while (_recentLog.Count > 30) _recentLog.Dequeue();
            }
            if (activity != null) activity(clean.Length > 220 ? clean.Substring(0, 220) + "…" : clean);
        }

        private string RecentError()
        {
            lock (_logLock)
            {
                if (_recentLog.Count == 0) return "No engine details were returned.";
                string[] lines = _recentLog.ToArray();
                return lines[lines.Length - 1];
            }
        }

        private static string UsefulError(ProcessResult result)
        {
            string error = LastUsefulLine(result.Error);
            if (String.IsNullOrWhiteSpace(error)) error = LastUsefulLine(result.Output);
            return String.IsNullOrWhiteSpace(error) ? "The local model plan could not be created." : error;
        }

        private static string LastUsefulLine(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string[] lines = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? value.Trim() : lines[lines.Length - 1].Trim();
        }

        private static string HashFile(string path)
        {
            using (var hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] digest = hash.ComputeHash(stream);
                var builder = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        private static string JoinArguments(IEnumerable<string> arguments)
        {
            var builder = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(Quote(argument));
            }
            return builder.ToString();
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\"' }) < 0) return value;
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException("EmbeddedStrataManager"); }

        private sealed class ProcessResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public long Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);

        private void AttachJob(Process process)
        {
            try
            {
                IntPtr job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return;
                var limits = new ExtendedLimitInformation();
                limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
                int size = Marshal.SizeOf(typeof(ExtendedLimitInformation));
                IntPtr pointer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(limits, pointer, false);
                    if (!SetInformationJobObject(job, 9, pointer, (uint)size) || !AssignProcessToJobObject(job, process.Handle))
                    { CloseHandle(job); return; }
                    _job = job;
                }
                finally { Marshal.FreeHGlobal(pointer); }
            }
            catch { }
        }
    }
}
