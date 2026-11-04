using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CodexNativeLite;

internal static class BrowserToolSmoke
{
    private static int Main(string[] args)
    {
        try
        {
            string serialized = new JavaScriptSerializer().Serialize(BrowserToolContract.CreateSpecs());
            Assert(serialized.Contains("legion_browser"), "Browser namespace is missing.");
            Assert(serialized.Contains("snapshot") && serialized.Contains("click") && serialized.Contains("type"), "Core browser tools are missing.");
            Assert(BrowserToolContract.IsInteractive("click"), "Click should require interactive approval.");
            Assert(!BrowserToolContract.IsInteractive("snapshot"), "Snapshot should remain read-only.");
            Assert(ManagedBrowser.NormalizeUrl("example.com").StartsWith("https://example.com", StringComparison.Ordinal), "Friendly URL normalization failed.");
            ExpectFailure("file:///C:/Windows/win.ini");
            ExpectFailure("https://name:secret@example.com/");
            string browser = ManagedBrowser.FindInstalledBrowserPath();
            Assert(!String.IsNullOrWhiteSpace(browser) && File.Exists(browser), "Edge or Chrome was not discovered.");
            if (args.Length > 0 && String.Equals(args[0], "--integration", StringComparison.OrdinalIgnoreCase))
                RunIntegration();
            Console.WriteLine("Managed browser smoke test passed: " + Path.GetFileName(browser));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void RunIntegration()
    {
        string profile = Path.Combine(Path.GetTempPath(), "CodexLegionBrowserTest-" + Guid.NewGuid().ToString("N"));
        using (var page = new LocalPageServer())
        using (var browser = new ManagedBrowser(profile))
        {
            var arguments = new System.Collections.Generic.Dictionary<string, object> { { "url", page.Url } };
            BrowserToolResult result = browser.ExecuteAsync("open", arguments).GetAwaiter().GetResult();
            Assert(result.Text != null && result.Text.Contains("Legion Browser Test"), "The managed browser did not read the local test page.");
            Assert(result.ImageDataUrl != null && result.ImageDataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal), "The managed browser did not capture a screenshot.");
            result = browser.ExecuteAsync("type", new System.Collections.Generic.Dictionary<string, object>
            {
                { "target", "L1" }, { "text", "Legion" }
            }).GetAwaiter().GetResult();
            result = browser.ExecuteAsync("click", new System.Collections.Generic.Dictionary<string, object>
            {
                { "target", "L2" }
            }).GetAwaiter().GetResult();
            Assert(result.Text != null && result.Text.Contains("Hello Legion"), "Typing or clicking through CDP failed.");
            browser.ExecuteAsync("close", new System.Collections.Generic.Dictionary<string, object>()).GetAwaiter().GetResult();
            Assert(!browser.IsRunning, "The managed browser did not close cleanly.");
            browser.ClearProfileData();
        }
        Assert(!Directory.Exists(profile), "The integration browser profile was not removed.");
    }

    private static void ExpectFailure(string value)
    {
        bool failed = false;
        try { ManagedBrowser.NormalizeUrl(value); }
        catch (ArgumentException) { failed = true; }
        Assert(failed, "Unsafe URL was accepted: " + value);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class LocalPageServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Task _loop;
        public string Url { get; private set; }

        public LocalPageServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = "http://127.0.0.1:" + port + "/";
            _loop = Task.Run((Func<Task>)ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch { return; }
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    string line;
                    do { line = await reader.ReadLineAsync(); } while (!String.IsNullOrEmpty(line));
                    string html = "<!doctype html><title>Legion Browser Test</title><h1>Legion Browser Test</h1>"
                        + "<input placeholder='Type your name'><button onclick=\"document.getElementById('out').innerText='Hello '+document.querySelector('input').value\">Greet</button>"
                        + "<p id='out'>Waiting</p>";
                    byte[] body = Encoding.UTF8.GetBytes(html);
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length);
                    await stream.WriteAsync(body, 0, body.Length);
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            try { _loop.Wait(1000); } catch { }
            _stop.Dispose();
        }
    }
}
