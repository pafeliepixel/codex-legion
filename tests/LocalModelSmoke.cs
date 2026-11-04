using System;
using System.Collections.Generic;
using System.IO;
using CodexNativeLite;

internal static class LocalModelSmoke
{
    private static int Main(string[] args)
    {
        try
        {
            List<LocalModelChoice> models = LocalModelCatalog.Create();
            Assert(models.Count >= 8, "The local model catalog is incomplete.");
            Assert(LocalModelCatalog.Find("qwen36-q4") != null, "The balanced Qwen model is missing.");
            Assert(LocalModelCatalog.Find("deepseek-200").DownloadGb > 250, "The DeepSeek disk estimate is unsafe.");
            var small = new LocalHardwareProfile { TotalMemoryBytes = 16L * 1024 * 1024 * 1024, FreeDiskBytes = 300L * 1024 * 1024 * 1024 };
            small.Gpus.Add(new LocalGpuInfo { Name = "NVIDIA RTX test", MemoryBytes = 8L * 1024 * 1024 * 1024 });
            Assert(LocalModelCatalog.Find("qwen38-iq3s").Rating(small) == "Not recommended", "A 16 GB PC received an unsafe recommendation.");
            var roomy = new LocalHardwareProfile { TotalMemoryBytes = 64L * 1024 * 1024 * 1024, FreeDiskBytes = 400L * 1024 * 1024 * 1024 };
            roomy.Gpus.Add(new LocalGpuInfo { Name = "NVIDIA RTX test", MemoryBytes = 16L * 1024 * 1024 * 1024 });
            Assert(LocalModelCatalog.Find("qwen38-iq2").Rating(roomy) == "Comfortable", "A suitable 64 GB PC was not recognized.");
            LocalHardwareProfile detected = LocalHardwareDetector.Detect(Path.Combine(Path.GetTempPath(), "CodexLegionModels"));
            Assert(detected.TotalMemoryBytes > 0, "Windows RAM detection failed.");
            using (var manager = new EmbeddedStrataManager())
            {
                Assert(manager.BaseUrl == "http://127.0.0.1:18347/v1", "The Legion-local endpoint changed unexpectedly.");
                Assert(EmbeddedStrataManager.ControlSha256.Length == 64, "The pinned control hash is malformed.");
            }
            if (args.Length == 2 && args[0] == "--control-archive") RunControlArchiveTest(args[1]);
            Console.WriteLine("Local model catalog and hardware smoke test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void RunControlArchiveTest(string archive)
    {
        // Keep this close to the installed LocalAppData path length. The embedded
        // Python bundle contains deep package paths and .NET Framework zip APIs
        // still observe the classic Windows path limit on some machines.
        string root = Path.Combine(Path.GetTempPath(), "clai-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            using (var manager = new EmbeddedStrataManager(root, Path.Combine(root, "models")))
            {
                manager.InstallControlArchiveForTest(archive);
                Assert(manager.IsControlReady, "The pinned Strata control archive did not install cleanly.");
                string python = Path.Combine(manager.ControlDirectory, "python", "python.exe");
                string script = Path.Combine(manager.ControlDirectory, "strata.py");
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "\"" + script + "\" --version",
                    WorkingDirectory = manager.ControlDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(start))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    Assert(process.ExitCode == 0, "The embedded Strata entry point failed: " + error);
                    Assert(output.IndexOf("0.2.10", StringComparison.OrdinalIgnoreCase) >= 0, "Unexpected embedded Strata version: " + output);
                }
                foreach (LocalModelChoice model in LocalModelCatalog.Create())
                {
                    string plan = manager.PlanAsync(model, null).GetAwaiter().GetResult();
                    Assert(!String.IsNullOrWhiteSpace(plan), "The embedded engine returned an empty plan for " + model.Id + ".");
                }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
