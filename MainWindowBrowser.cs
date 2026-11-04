using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodexNativeLite
{
    public sealed partial class MainWindow
    {
        private readonly ManagedBrowser _managedBrowser = new ManagedBrowser();
        private bool _browserApprovedForSession;

        private async Task HandleBrowserToolRequestAsync(int requestId, Dictionary<string, object> parameters)
        {
            string tool = CodexAppServerClient.GetString(parameters, "tool");
            string toolNamespace = CodexAppServerClient.GetString(parameters, "namespace");
            var arguments = CodexAppServerClient.GetDictionary(parameters, "arguments");
            if (!String.Equals(toolNamespace, BrowserToolContract.Namespace, StringComparison.OrdinalIgnoreCase))
            {
                _client.RespondToServerRequest(requestId, BrowserResponse(false, "Unknown client tool namespace.", null));
                return;
            }

            if (BrowserToolContract.IsInteractive(tool))
            {
                if (String.Equals(_runtimeSettings.PermissionMode, "read-only", StringComparison.Ordinal))
                {
                    _client.RespondToServerRequest(requestId, BrowserResponse(false, "Browser clicks and typing are unavailable in Read only mode.", null));
                    return;
                }
                if (!String.Equals(_runtimeSettings.PermissionMode, "full", StringComparison.Ordinal) && !_browserApprovedForSession)
                {
                    string target = CodexAppServerClient.GetString(arguments, "target") ?? "the selected page element";
                    string detail = String.Equals(tool, "type", StringComparison.OrdinalIgnoreCase)
                        ? "Legion wants to type into " + target + ". The text stays hidden in this approval prompt."
                        : "Legion wants to click " + target + " in its managed browser.";
                    string decision = await ShowChoiceAsync("Browser action", detail, new List<ChoiceItem>
                    {
                        new ChoiceItem("once", "Allow once", "Run only this browser action."),
                        new ChoiceItem("session", "Allow this browser session", "Allow later clicks and typing until Legion closes."),
                        new ChoiceItem("deny", "Don’t allow", "Keep the page unchanged.")
                    });
                    if (decision == "session") _browserApprovedForSession = true;
                    else if (decision != "once")
                    {
                        _client.RespondToServerRequest(requestId, BrowserResponse(false, "The user declined this browser action.", null));
                        return;
                    }
                }
            }

            try
            {
                AddActivity("Browser", FriendlyBrowserAction(tool));
                BrowserToolResult result = await _managedBrowser.ExecuteAsync(tool, arguments);
                _client.RespondToServerRequest(requestId, BrowserResponse(true, result.Text, result.ImageDataUrl));
                SetStatus(_managedBrowser.IsRunning ? "Browser · " + _managedBrowser.BrowserName : "Ready ✦", false);
            }
            catch (Exception ex)
            {
                _client.RespondToServerRequest(requestId, BrowserResponse(false, ex.Message, null));
                AddSystemCard("Browser hiccup", ex.Message);
                SetStatus("Browser action failed", true);
            }
        }

        private static object BrowserResponse(bool success, string text, string imageDataUrl)
        {
            var content = new List<object>();
            if (!String.IsNullOrWhiteSpace(text)) content.Add(new Dictionary<string, object>
            {
                { "type", "inputText" }, { "text", text }
            });
            if (!String.IsNullOrWhiteSpace(imageDataUrl)) content.Add(new Dictionary<string, object>
            {
                { "type", "inputImage" }, { "imageUrl", imageDataUrl }
            });
            return new { contentItems = content.ToArray(), success = success };
        }

        private static string FriendlyBrowserAction(string tool)
        {
            if (String.Equals(tool, "open", StringComparison.OrdinalIgnoreCase)) return "Opening a managed page";
            if (String.Equals(tool, "snapshot", StringComparison.OrdinalIgnoreCase)) return "Reading the current page";
            if (String.Equals(tool, "click", StringComparison.OrdinalIgnoreCase)) return "Clicking in the managed browser";
            if (String.Equals(tool, "type", StringComparison.OrdinalIgnoreCase)) return "Typing in the managed browser";
            if (String.Equals(tool, "scroll", StringComparison.OrdinalIgnoreCase)) return "Scrolling the current page";
            if (String.Equals(tool, "back", StringComparison.OrdinalIgnoreCase)) return "Going back one page";
            if (String.Equals(tool, "close", StringComparison.OrdinalIgnoreCase)) return "Closing the managed browser";
            return "Working in the managed browser";
        }
    }
}
