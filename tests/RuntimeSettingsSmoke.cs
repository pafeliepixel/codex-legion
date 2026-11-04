using System;
using CodexNativeLite;

internal static class RuntimeSettingsSmoke
{
    private static int Main()
    {
        var settings = new RuntimeSettings();
        Assert(settings.PermissionMode == "ask", "Ask-first must be the default.");
        Assert(Convert.ToString(settings.ApprovalPolicy) == "on-request", "Ask-first approval policy mismatch.");
        Assert(settings.SandboxMode == "workspace-write", "Ask-first sandbox mismatch.");

        settings.PermissionMode = "read-only";
        Assert(settings.SandboxMode == "read-only", "Read-only sandbox mismatch.");
        Assert(settings.ApprovalsReviewer == "user", "Read-only reviewer mismatch.");

        settings.PermissionMode = "auto-review";
        Assert(settings.ApprovalsReviewer == "auto_review", "Auto-review mode mismatch.");
        Assert(settings.SandboxMode == "workspace-write", "Auto-review sandbox mismatch.");

        settings.PermissionMode = "full";
        Assert(Convert.ToString(settings.ApprovalPolicy) == "never", "Full-access approval policy mismatch.");
        Assert(settings.SandboxMode == "danger-full-access", "Full-access sandbox mismatch.");

        var catalog = new CapabilityCatalog();
        catalog.AllowedApprovalPolicies.Add("on-request");
        catalog.AllowedSandboxModes.Add("read-only");
        Assert(catalog.Allows("on-request", "read-only"), "Allowed managed combination was rejected.");
        Assert(!catalog.Allows("never", "danger-full-access"), "Disallowed managed combination was accepted.");

        Console.WriteLine("Runtime settings smoke tests passed.");
        return 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
