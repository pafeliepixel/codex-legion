using System;
using System.Collections.Generic;

namespace CodexNativeLite
{
    public sealed class ReasoningOption
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public override string ToString() { return Id; }
    }

    public sealed class ModelOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string DefaultReasoningEffort { get; set; }
        public bool IsDefault { get; set; }
        public bool SupportsImages { get; set; }
        public List<ReasoningOption> ReasoningEfforts { get; private set; }

        public ModelOption() { ReasoningEfforts = new List<ReasoningOption>(); }
        public override string ToString() { return String.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName; }
    }

    public sealed class RuntimeSettings
    {
        public string Model { get; set; }
        public string ReasoningEffort { get; set; }
        public string PermissionMode { get; set; }
        public string ModelProvider { get; set; }
        public string LocalModelId { get; set; }
        public string LocalModelDirectory { get; set; }
        public string LocalBaseUrl { get; set; }

        public RuntimeSettings()
        {
            PermissionMode = "ask";
            ModelProvider = "openai";
        }

        public bool UsesLocalModel { get { return String.Equals(ModelProvider, "strata", StringComparison.OrdinalIgnoreCase); } }
        public string EffectiveModel { get { return UsesLocalModel ? "strata" : Model; } }

        public string PermissionLabel
        {
            get
            {
                if (PermissionMode == "read-only") return "Read only";
                if (PermissionMode == "auto-review") return "Approve for me";
                if (PermissionMode == "full") return "Full access";
                return "Ask first";
            }
        }

        public object ApprovalPolicy
        {
            get { return PermissionMode == "full" ? (object)"never" : "on-request"; }
        }

        public string ApprovalsReviewer
        {
            get { return PermissionMode == "auto-review" ? "auto_review" : "user"; }
        }

        public object SandboxPolicy
        {
            get
            {
                if (PermissionMode == "read-only") return new Dictionary<string, object> { { "type", "readOnly" } };
                if (PermissionMode == "full") return new Dictionary<string, object> { { "type", "dangerFullAccess" } };
                return new Dictionary<string, object> { { "type", "workspaceWrite" } };
            }
        }

        public string SandboxMode
        {
            get
            {
                if (PermissionMode == "read-only") return "read-only";
                if (PermissionMode == "full") return "danger-full-access";
                return "workspace-write";
            }
        }
    }

    public sealed class CapabilityCatalog
    {
        public List<ModelOption> Models { get; private set; }
        public List<string> AllowedApprovalPolicies { get; private set; }
        public List<string> AllowedSandboxModes { get; private set; }

        public CapabilityCatalog()
        {
            Models = new List<ModelOption>();
            AllowedApprovalPolicies = new List<string>();
            AllowedSandboxModes = new List<string>();
        }

        public bool Allows(string approvalPolicy, string sandboxMode)
        {
            bool approvalAllowed = AllowedApprovalPolicies.Count == 0 || AllowedApprovalPolicies.Contains(approvalPolicy);
            bool sandboxAllowed = AllowedSandboxModes.Count == 0 || AllowedSandboxModes.Contains(sandboxMode);
            return approvalAllowed && sandboxAllowed;
        }
    }

    public sealed class UserAttachment
    {
        public string Type { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
    }

    public sealed class SkillOption
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Description { get; set; }
        public bool Enabled { get; set; }
        public string Scope { get; set; }
        public override string ToString() { return Name; }
    }

    public sealed class AppOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Accessible { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class GitSnapshot
    {
        public bool IsRepository { get; set; }
        public string Branch { get; set; }
        public string StatusText { get; set; }
        public int ChangedFiles { get; set; }
    }
}
