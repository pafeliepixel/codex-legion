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
        Assert(settings.ParallelMode == "asked", "Backup-on-request must be the default.");
        Assert(settings.ParallelLabel == "Backup on request", "Parallel label mismatch.");
        Assert(settings.EffectiveReasoningEffort == null, "Asked mode should preserve the selected reasoning effort.");

        settings.Model = "gpt-test";
        settings.ReasoningEffort = "high";
        settings.ParallelMode = "smart";
        Assert(settings.EffectiveReasoningEffort == "ultra", "Smart split must use Ultra reasoning.");
        Assert(settings.ParallelInstructions.IndexOf("independent workstreams", StringComparison.Ordinal) >= 0, "Smart split instructions are missing.");
        var collaboration = settings.CollaborationMode as System.Collections.Generic.Dictionary<string, object>;
        Assert(collaboration != null && Convert.ToString(collaboration["mode"]) == "default", "Collaboration mode mismatch.");
        var collaborationSettings = collaboration["settings"] as System.Collections.Generic.Dictionary<string, object>;
        Assert(collaborationSettings != null && Convert.ToString(collaborationSettings["reasoning_effort"]) == "ultra", "Collaboration effort mismatch.");
        Assert(Convert.ToString(collaborationSettings["developer_instructions"]).Length > 10, "Collaboration instructions are missing.");

        var parallelModel = new ModelOption { MultiAgentVersion = "v2" };
        parallelModel.ReasoningEfforts.Add(new ReasoningOption { Id = "ultra" });
        Assert(parallelModel.SupportsParallelWork, "v2 model should support parallel work.");
        Assert(parallelModel.SupportsUltra, "Ultra support was not detected.");
        Assert(!new ModelOption { MultiAgentVersion = "disabled" }.SupportsParallelWork, "Disabled model should stay in one lane.");

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
