using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CodexNativeLite
{
    public sealed class GitService
    {
        public async Task<GitSnapshot> ReadAsync(string workingDirectory)
        {
            if (String.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
                return new GitSnapshot { IsRepository = false, StatusText = "No project selected" };

            ProcessResult root = await RunAsync(workingDirectory, "rev-parse --show-toplevel").ConfigureAwait(false);
            if (root.ExitCode != 0)
                return new GitSnapshot { IsRepository = false, StatusText = "This folder is not a Git repository" };

            ProcessResult branch = await RunAsync(workingDirectory, "branch --show-current").ConfigureAwait(false);
            ProcessResult status = await RunAsync(workingDirectory, "status --short --branch").ConfigureAwait(false);
            string text = String.IsNullOrWhiteSpace(status.Output) ? "Working tree clean" : status.Output.Trim();
            int changed = 0;
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (!line.StartsWith("##", StringComparison.Ordinal)) changed++;

            return new GitSnapshot
            {
                IsRepository = true,
                Branch = String.IsNullOrWhiteSpace(branch.Output) ? "detached" : branch.Output.Trim(),
                StatusText = text,
                ChangedFiles = changed
            };
        }

        public async Task<string> CreateManagedWorktreeAsync(string workingDirectory, string branchName)
        {
            if (String.IsNullOrWhiteSpace(branchName)) throw new ArgumentException("Enter a branch name.");
            foreach (char invalid in Path.GetInvalidFileNameChars())
                if (branchName.IndexOf(invalid) >= 0) throw new ArgumentException("That branch name contains an invalid character.");

            ProcessResult root = await RunAsync(workingDirectory, "rev-parse --show-toplevel").ConfigureAwait(false);
            if (root.ExitCode != 0) throw new InvalidOperationException("Choose a Git repository first.");
            string repository = root.Output.Trim();
            string safeName = branchName.Replace('/', '-').Replace('\\', '-').Trim();
            string baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexNativeLite", "worktrees");
            Directory.CreateDirectory(baseDirectory);
            string target = Path.Combine(baseDirectory, safeName);
            if (Directory.Exists(target)) throw new InvalidOperationException("A managed worktree with that name already exists.");

            ProcessResult create = await RunAsync(repository,
                "worktree add -b " + Quote(branchName) + " " + Quote(target)).ConfigureAwait(false);
            if (create.ExitCode != 0)
                throw new InvalidOperationException(String.IsNullOrWhiteSpace(create.Output) ? "Git could not create the worktree." : create.Output.Trim());
            return target;
        }

        private static Task<ProcessResult> RunAsync(string workingDirectory, string arguments)
        {
            return Task.Run(() =>
            {
                var start = new ProcessStartInfo
                {
                    FileName = "git.exe",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var process = Process.Start(start))
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return new ProcessResult
                    {
                        ExitCode = process.ExitCode,
                        Output = (stdout + (String.IsNullOrWhiteSpace(stderr) ? "" : "\n" + stderr)).Trim()
                    };
                }
            });
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class ProcessResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; }
        }
    }
}
