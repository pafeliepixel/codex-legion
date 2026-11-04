using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexLegionInstaller
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && String.Equals(args[0], "/cleanup", StringComparison.OrdinalIgnoreCase))
            {
                Cleanup(args);
                return;
            }
            if (args.Length > 0 && String.Equals(args[0], "/verify", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using (var archive = new PayloadArchive(Application.ExecutablePath)) archive.Verify();
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && String.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase))
            {
                Uninstall();
                return;
            }
            Application.Run(new InstallerForm());
        }

        private static void Uninstall()
        {
            string installDirectory = ReadInstallDirectory();
            if (String.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
            {
                MessageBox.Show("Codex Legion is not installed for this Windows account.", "Codex Legion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("Remove Codex Legion and its shortcuts?\n\nYour separate login profile and chats will be kept.", "Uninstall Codex Legion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                ShortcutManager.RemoveAll();
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLegion", false);
                string helper = Path.Combine(Path.GetTempPath(), "CodexLegionCleanup-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Application.ExecutablePath, helper, true);
                var start = new ProcessStartInfo
                {
                    FileName = helper,
                    Arguments = "/cleanup " + Quote(installDirectory) + " " + Process.GetCurrentProcess().Id,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(start);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Codex Legion could not be removed.\n\n" + ex.Message, "Uninstall failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void Cleanup(string[] args)
        {
            if (args.Length < 3) return;
            string directory = args[1];
            int pid;
            if (Int32.TryParse(args[2], out pid))
            {
                try { Process.GetProcessById(pid).WaitForExit(12000); } catch { }
            }
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                    break;
                }
                catch { Thread.Sleep(300); }
            }
            NativeMethods.MoveFileEx(Application.ExecutablePath, null, 4);
        }

        private static string ReadInstallDirectory()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLegion"))
                return key == null ? null : Convert.ToString(key.GetValue("InstallLocation"));
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
    }

    internal sealed class InstallerForm : Form
    {
        private static readonly Color BackgroundColor = Color.FromArgb(18, 16, 24);
        private static readonly Color CardColor = Color.FromArgb(32, 29, 39);
        private static readonly Color TextColor = Color.FromArgb(248, 245, 255);
        private static readonly Color MutedColor = Color.FromArgb(168, 160, 181);
        private static readonly Color AccentColor = Color.FromArgb(156, 131, 247);
        private readonly TextBox _location;
        private readonly CheckBox _desktop;
        private readonly CheckBox _startMenu;
        private readonly Button _install;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private bool _complete;

        public InstallerForm()
        {
            Text = "Install Codex Legion";
            ClientSize = new Size(620, 465);
            MinimumSize = new Size(620, 465);
            MaximumSize = new Size(760, 560);
            BackColor = BackgroundColor;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 10F);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var mark = new Label { Text = "//", ForeColor = BackgroundColor, BackColor = AccentColor, Font = new Font("Consolas", 14F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Location = new Point(34, 31), Size = new Size(46, 46) };
            Controls.Add(mark);
            Controls.Add(new Label { Text = "Codex Legion", ForeColor = TextColor, Font = new Font("Segoe UI", 24F, FontStyle.Bold), AutoSize = true, Location = new Point(96, 29) });
            Controls.Add(new Label { Text = "A private, native Codex client for Windows", ForeColor = MutedColor, Font = new Font("Segoe UI", 10F), AutoSize = true, Location = new Point(100, 70) });

            var card = new Panel { BackColor = CardColor, Location = new Point(30, 112), Size = new Size(560, 255), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            Controls.Add(card);
            card.Controls.Add(new Label { Text = "Install for this Windows account", ForeColor = TextColor, Font = new Font("Segoe UI", 13F, FontStyle.Bold), AutoSize = true, Location = new Point(22, 20) });
            card.Controls.Add(new Label { Text = "Installs the native app and fetches its verified runtime when needed.", ForeColor = MutedColor, AutoSize = true, Location = new Point(22, 52) });
            card.Controls.Add(new Label { Text = "Install location", ForeColor = MutedColor, AutoSize = true, Location = new Point(22, 91) });
            _location = new TextBox { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Codex Legion"), BackColor = Color.FromArgb(42, 39, 49), ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle, Location = new Point(25, 116), Size = new Size(420, 30) };
            card.Controls.Add(_location);
            var browse = MakeButton("Browse", false);
            browse.Location = new Point(453, 114);
            browse.Size = new Size(82, 32);
            browse.Click += BrowseClick;
            card.Controls.Add(browse);
            _startMenu = new CheckBox { Text = "Start menu shortcut", Checked = true, ForeColor = TextColor, AutoSize = true, Location = new Point(25, 168), FlatStyle = FlatStyle.Flat };
            _desktop = new CheckBox { Text = "Desktop shortcut", Checked = false, ForeColor = TextColor, AutoSize = true, Location = new Point(250, 168), FlatStyle = FlatStyle.Flat };
            card.Controls.Add(_startMenu);
            card.Controls.Add(_desktop);
            card.Controls.Add(new Label { Text = "Your login, PIN and chats stay in the existing private profile during updates.", ForeColor = MutedColor, AutoSize = true, Location = new Point(25, 211) });

            _status = new Label { Text = "Ready to install", ForeColor = MutedColor, AutoSize = true, Location = new Point(32, 386) };
            Controls.Add(_status);
            _progress = new ProgressBar { Location = new Point(32, 414), Size = new Size(380, 10), Style = ProgressBarStyle.Continuous, Visible = false };
            Controls.Add(_progress);
            _install = MakeButton("Install", true);
            _install.Location = new Point(455, 394);
            _install.Size = new Size(135, 42);
            _install.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _install.Click += InstallClick;
            Controls.Add(_install);
        }

        private static Button MakeButton(string text, bool accent)
        {
            return new Button
            {
                Text = text,
                BackColor = accent ? AccentColor : Color.FromArgb(48, 44, 57),
                ForeColor = accent ? BackgroundColor : TextColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
        }

        private void BrowseClick(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose where Codex Legion will be installed", SelectedPath = _location.Text, ShowNewFolderButton = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) _location.Text = Path.Combine(dialog.SelectedPath, "Codex Legion");
        }

        private async void InstallClick(object sender, EventArgs e)
        {
            if (_complete)
            {
                Process.Start(new ProcessStartInfo(Path.Combine(_location.Text, "CodexLegion.exe")) { UseShellExecute = true });
                Close();
                return;
            }
            if (Process.GetProcessesByName("CodexLegion").Length > 0)
            {
                MessageBox.Show(this, "Close Codex Legion before installing the update.", "Codex Legion is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string target = _location.Text.Trim();
            if (String.IsNullOrWhiteSpace(target)) return;
            _install.Enabled = false;
            _location.Enabled = false;
            _desktop.Enabled = false;
            _startMenu.Enabled = false;
            _progress.Visible = true;
            _progress.Value = 0;
            _status.Text = "Installing…";
            try
            {
                await Task.Factory.StartNew(() => Install(target), TaskCreationOptions.LongRunning);
                _complete = true;
                _progress.Value = 100;
                _status.Text = "Codex Legion is ready";
                _install.Text = "Launch";
                _install.Enabled = true;
            }
            catch (Exception ex)
            {
                _status.Text = "Installation failed";
                _install.Enabled = true;
                _location.Enabled = true;
                MessageBox.Show(this, ex.Message, "Couldn’t install Codex Legion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Install(string target)
        {
            Directory.CreateDirectory(target);
            using (var archive = new PayloadArchive(Application.ExecutablePath))
            {
                archive.Extract(target, delegate(int value, string file)
                {
                    BeginInvoke((Action)(() => { _progress.Value = Math.Max(0, Math.Min(100, value)); _status.Text = "Installing " + file; }));
                });
                string uninstaller = Path.Combine(target, "Uninstall Codex Legion.exe");
                if (!File.Exists(uninstaller)) archive.CopyStub(uninstaller);
            }
            EnsureRuntime(target);
            string executable = Path.Combine(target, "CodexLegion.exe");
            if (!File.Exists(executable)) throw new InvalidDataException("The application executable was missing from the installer payload.");
            ShortcutManager.Create(executable, target, _startMenu.Checked, _desktop.Checked);
            WriteUninstallRegistration(target, executable);
        }

        private void EnsureRuntime(string target)
        {
            string executable = Path.Combine(target, "runtime", "codex.exe");
            string channelPath = Path.Combine(target, "update-channel.json");
            if (!File.Exists(channelPath))
            {
                if (File.Exists(executable)) return;
                throw new InvalidDataException("This setup has no runtime download channel. Use the offline installer instead.");
            }
            var json = new JavaScriptSerializer();
            var channel = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(channelPath));
            string repository = ReadString(channel, "repository");
            string releaseTag = ReadString(channel, "releaseTag");
            string runtimeReleaseTag = ReadString(channel, "runtimeReleaseTag") ?? releaseTag;
            string asset = ReadString(channel, "runtimeAsset") ?? "CodexLegionRuntime-win-x64.zip";
            string expectedVersion = ReadString(channel, "runtimeVersion");
            string versionFile = Path.Combine(target, "runtime", ".legion-runtime-version");
            string installedVersion = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;
            if (File.Exists(executable) && !String.IsNullOrWhiteSpace(expectedVersion) && String.Equals(expectedVersion, installedVersion, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(executable) && String.IsNullOrWhiteSpace(expectedVersion)) return;
            string bundledArchive = Path.Combine(target, asset);
            if (File.Exists(bundledArchive))
            {
                BeginInvoke((Action)(() => { _status.Text = "Adding the Codex runtime…"; _progress.Value = 82; }));
                ExtractRuntimeArchive(bundledArchive, Path.Combine(target, "runtime"));
                File.Delete(bundledArchive);
                if (!File.Exists(executable)) throw new InvalidDataException("The bundled runtime did not contain codex.exe.");
                if (!String.IsNullOrWhiteSpace(expectedVersion)) File.WriteAllText(versionFile, expectedVersion, Encoding.ASCII);
                return;
            }
            if (String.IsNullOrWhiteSpace(repository) || String.IsNullOrWhiteSpace(runtimeReleaseTag))
                throw new InvalidDataException("This web installer is not connected to a GitHub release yet. Use CodexLegionSetup-offline.exe instead.");

            BeginInvoke((Action)(() => { _status.Text = "Downloading the Codex runtime…"; _progress.Style = ProgressBarStyle.Marquee; }));
            string baseUrl = "https://github.com/" + repository.Trim('/') + "/releases/download/" + Uri.EscapeDataString(runtimeReleaseTag) + "/";
            string temporaryRoot = Path.Combine(Path.GetTempPath(), "CodexLegionInstaller", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            string archivePath = Path.Combine(temporaryRoot, asset);
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                string expected;
                using (var web = CreateWebClient()) expected = web.DownloadString(baseUrl + Uri.EscapeDataString(asset + ".sha256")).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                using (var web = CreateWebClient()) web.DownloadFile(baseUrl + Uri.EscapeDataString(asset), archivePath);
                VerifyFile(archivePath, expected);
                BeginInvoke((Action)(() => { _status.Text = "Adding the Codex runtime…"; _progress.Style = ProgressBarStyle.Continuous; _progress.Value = 82; }));
                ExtractRuntimeArchive(archivePath, Path.Combine(target, "runtime"));
                if (!File.Exists(executable)) throw new InvalidDataException("The downloaded runtime did not contain codex.exe.");
                if (!String.IsNullOrWhiteSpace(expectedVersion)) File.WriteAllText(versionFile, expectedVersion, Encoding.ASCII);
            }
            finally
            {
                try { if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true); } catch { }
            }
        }

        private static WebClient CreateWebClient()
        {
            var web = new WebClient();
            web.Headers[HttpRequestHeader.UserAgent] = "Codex-Legion-Installer";
            return web;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }

        private static void VerifyFile(string path, string expected)
        {
            if (String.IsNullOrWhiteSpace(expected)) throw new InvalidDataException("The runtime checksum was empty.");
            string actual;
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
            if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The downloaded runtime failed its SHA-256 check.");
        }

        private static void ExtractRuntimeArchive(string archivePath, string runtimeDirectory)
        {
            Directory.CreateDirectory(runtimeDirectory);
            string root = Path.GetFullPath(runtimeDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = System.IO.Compression.ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (String.IsNullOrEmpty(entry.Name)) continue;
                    string destination = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe path in runtime package.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, true);
                }
            }
        }

        private static void WriteUninstallRegistration(string target, string executable)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLegion"))
            {
                key.SetValue("DisplayName", "Codex Legion");
                key.SetValue("DisplayVersion", "0.5.0");
                key.SetValue("Publisher", "Codex Legion community project");
                key.SetValue("InstallLocation", target);
                key.SetValue("DisplayIcon", executable);
                key.SetValue("UninstallString", "\"" + Path.Combine(target, "Uninstall Codex Legion.exe") + "\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
    }

    internal sealed class PayloadArchive : IDisposable
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CLGNPKGFORMAT001");
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private readonly long _offset;

        public PayloadArchive(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (_stream.Length < 24) throw new InvalidDataException("This setup file has no payload.");
            _reader = new BinaryReader(_stream, Encoding.UTF8);
            _offset = LocatePayloadOffset();
            if (_offset <= 0) throw new InvalidDataException("This setup payload is damaged.");
        }

        private long LocatePayloadOffset()
        {
            int scanLength = (int)Math.Min(_stream.Length, 4L * 1024L * 1024L);
            long scanStart = _stream.Length - scanLength;
            _stream.Position = scanStart;
            byte[] tail = _reader.ReadBytes(scanLength);
            for (int index = tail.Length - Magic.Length; index >= 8; index--)
            {
                bool match = true;
                for (int m = 0; m < Magic.Length; m++) if (tail[index + m] != Magic[m]) { match = false; break; }
                if (!match) continue;
                long candidate = BitConverter.ToInt64(tail, index - 8);
                long footerPosition = scanStart + index - 8;
                if (candidate > 0 && candidate < footerPosition) return candidate;
            }
            return -1;
        }

        public void Extract(string target, Action<int, string> progress)
        {
            string root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            _stream.Position = _offset;
            int version = _reader.ReadInt32();
            int count = _reader.ReadInt32();
            if (version != 1 || count < 1 || count > 1000) throw new InvalidDataException("Unsupported setup payload format.");
            byte[] buffer = new byte[1024 * 1024];
            for (int index = 0; index < count; index++)
            {
                string relative = _reader.ReadString();
                long length = _reader.ReadInt64();
                byte[] expected = _reader.ReadBytes(32);
                string destination = Path.GetFullPath(Path.Combine(root, relative));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe path in installer payload.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                string temporary = destination + ".installing";
                using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sha = SHA256.Create())
                {
                    long remaining = length;
                    while (remaining > 0)
                    {
                        int wanted = (int)Math.Min(buffer.Length, remaining);
                        int read = _stream.Read(buffer, 0, wanted);
                        if (read <= 0) throw new EndOfStreamException("The setup payload ended unexpectedly.");
                        output.Write(buffer, 0, read);
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        remaining -= read;
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    if (!Equal(expected, sha.Hash)) throw new InvalidDataException("Integrity check failed for " + relative + ".");
                }
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporary, destination);
                if (progress != null) progress((index + 1) * 100 / count, Path.GetFileName(relative));
            }
        }

        public void Verify()
        {
            _stream.Position = _offset;
            int version = _reader.ReadInt32();
            int count = _reader.ReadInt32();
            if (version != 1 || count < 1 || count > 1000) throw new InvalidDataException("Unsupported setup payload format.");
            byte[] buffer = new byte[1024 * 1024];
            for (int index = 0; index < count; index++)
            {
                string relative = _reader.ReadString();
                long remaining = _reader.ReadInt64();
                byte[] expected = _reader.ReadBytes(32);
                using (var sha = SHA256.Create())
                {
                    while (remaining > 0)
                    {
                        int read = _stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (read <= 0) throw new EndOfStreamException("The setup payload ended unexpectedly.");
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        remaining -= read;
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    if (!Equal(expected, sha.Hash)) throw new InvalidDataException("Integrity check failed for " + relative + ".");
                }
            }
        }

        public void CopyStub(string destination)
        {
            _stream.Position = 0;
            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[1024 * 1024];
                long remaining = _offset;
                while (remaining > 0)
                {
                    int read = _stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read <= 0) throw new EndOfStreamException();
                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        public void Dispose() { _reader.Dispose(); _stream.Dispose(); }
    }

    internal static class ShortcutManager
    {
        private const string ShortcutName = "Codex Legion.lnk";

        public static void Create(string executable, string workingDirectory, bool startMenu, bool desktop)
        {
            RemoveAll();
            if (startMenu)
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                CreateShortcut(Path.Combine(folder, ShortcutName), executable, workingDirectory);
            }
            if (desktop) CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName), executable, workingDirectory);
        }

        public static void RemoveAll()
        {
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", ShortcutName));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName));
        }

        private static void CreateShortcut(string path, string executable, string workingDirectory)
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(executable);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription("Codex Legion");
            link.SetIconLocation(executable, 0);
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true);
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr data, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);
    }
}
