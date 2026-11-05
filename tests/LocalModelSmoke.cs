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
            var balanced = new LocalHardwareProfile { TotalMemoryBytes = 32L * 1024 * 1024 * 1024, FreeDiskBytes = 300L * 1024 * 1024 * 1024 };
            balanced.Gpus.Add(new LocalGpuInfo { Name = "NVIDIA RTX test", MemoryBytes = 8L * 1024 * 1024 * 1024 });
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
            EmbeddedStrataManager.EnableTls12();
            Assert((System.Net.ServicePointManager.SecurityProtocol & System.Net.SecurityProtocolType.Tls12) != 0, "TLS 1.2 was not enabled for GitHub downloads.");
            List<LocalModelChoice> balancedRanked = LocalModelCatalog.Rank(balanced);
            Assert(balancedRanked[0].Id == "qwen36-q4", "The 32 GB recommendation is not practical.");
            List<LocalModelChoice> roomyRanked = LocalModelCatalog.Rank(roomy);
            Assert(roomyRanked[0].Id == "qwen38-iq3s", "The 64 GB recommendation is not using the stronger fitting model.");
            RunProgressParserTest();
            RunComponentUpdateParserTest();
            RunStorageTest();
            if (args.Length == 2 && args[0] == "--control-archive") RunControlArchiveTest(args[1]);
            if (args.Length == 1 && args[0] == "--download-control") RunControlDownloadTest();
            Console.WriteLine("Local model catalog and hardware smoke test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void RunComponentUpdateParserTest()
    {
        string json = "{\"ok\":true,\"engine\":{\"status\":\"available\",\"installed\":{\"version\":\"0.1.56\"},\"offered\":{\"version\":\"0.1.57\"},\"download_bytes\":1048576},\"dsv41\":{\"status\":\"none-installed\"}}";
        LocalComponentUpdateInfo update = EmbeddedStrataManager.ParseComponentUpdateCheck(json);
        Assert(update.CheckSucceeded && update.UpdateAvailable, "A signed Strata engine update was not detected.");
        Assert(update.InstalledVersion == "0.1.56" && update.OfferedVersion == "0.1.57", "Strata update versions were not parsed.");
        Assert(update.DownloadBytes == 1048576, "Strata update size was not parsed.");
    }

    private static void RunProgressParserTest()
    {
        var buffer = new LocalProgressTextBuffer();
        char[] raw = "Qwen model: 1.00 / 4.00 GB (25%)\rQwen model: 2.00 / 4.00 GB (50%)\r".ToCharArray();
        IList<string> lines = buffer.Feed(raw, raw.Length);
        Assert(lines.Count == 2, "Carriage-return progress was not split into updates.");
        var parser = new LocalDownloadProgressParser();
        LocalDownloadProgress first;
        LocalDownloadProgress second;
        DateTime started = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert(parser.TryParse(lines[0], started, out first), "The first model progress line was not parsed.");
        Assert(parser.TryParse(lines[1], started.AddSeconds(10), out second), "The second model progress line was not parsed.");
        Assert(first.Percent == 25 && second.Percent == 50, "Model progress percentages are wrong.");
        Assert(second.BytesPerSecond > 90000000, "Model download speed was not calculated.");
        Assert(second.TimeRemaining.HasValue, "Model download ETA was not calculated.");
    }

    private static void RunStorageTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "clstore-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            string folder = Path.Combine(root, "models", "qwen36-UD-Q4_K_XL");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "model.gguf.part"), new byte[1024]);
            var storage = new LocalModelStorage();
            List<LocalModelInstall> found = storage.Discover(root, null);
            Assert(found.Count == 1 && found[0].Id == "qwen36-q4", "A known local model folder was not recognized.");
            Assert(found[0].IsPartial && found[0].Bytes == 1024, "A partial local model was not measured correctly.");
            Assert(storage.Remove(root, folder) == 1024, "Removing a partial model reported the wrong size.");
            Assert(!Directory.Exists(folder), "The selected model folder was not removed.");
            bool guarded = false;
            try { storage.Remove(root, root); }
            catch (InvalidOperationException) { guarded = true; }
            Assert(guarded, "The model deletion path guard did not reject the model root.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
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
                    Assert(output.IndexOf(EmbeddedStrataManager.UpstreamVersion, StringComparison.OrdinalIgnoreCase) >= 0, "Unexpected embedded Strata version: " + output);
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

    private static void RunControlDownloadTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "cldl-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            using (var manager = new EmbeddedStrataManager(root, Path.Combine(root, "models")))
            {
                manager.EnsureControlAsync(null).GetAwaiter().GetResult();
                Assert(manager.IsControlReady, "The real GitHub control download did not install cleanly.");
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
