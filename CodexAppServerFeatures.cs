using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CodexNativeLite
{
    public sealed partial class CodexAppServerClient
    {
        public async Task<CapabilityCatalog> LoadCapabilitiesAsync()
        {
            var catalog = new CapabilityCatalog();
            var modelsResponse = await RequestAsync("model/list", new
            {
                limit = 100,
                includeHidden = false
            }).ConfigureAwait(false);
            var modelsResult = GetDictionary(modelsResponse, "result");
            foreach (object raw in GetObjectList(modelsResult, "data"))
            {
                var model = raw as Dictionary<string, object>;
                if (model == null) continue;
                var option = new ModelOption
                {
                    Id = GetString(model, "model") ?? GetString(model, "id"),
                    DisplayName = GetString(model, "displayName"),
                    Description = GetString(model, "description"),
                    DefaultReasoningEffort = GetString(model, "defaultReasoningEffort"),
                    IsDefault = GetBool(model, "isDefault"),
                    SupportsImages = ContainsString(model, "inputModalities", "image"),
                    MultiAgentVersion = GetString(model, "multiAgentVersion")
                };
                foreach (object rawEffort in GetObjectList(model, "supportedReasoningEfforts"))
                {
                    var effort = rawEffort as Dictionary<string, object>;
                    if (effort == null) continue;
                    option.ReasoningEfforts.Add(new ReasoningOption
                    {
                        Id = GetString(effort, "reasoningEffort"),
                        Description = GetString(effort, "description")
                    });
                }
                if (!String.IsNullOrWhiteSpace(option.Id)) catalog.Models.Add(option);
            }

            try
            {
                var requirementsResponse = await RequestAsync("configRequirements/read", new { }).ConfigureAwait(false);
                var requirementsResult = GetDictionary(requirementsResponse, "result");
                var requirements = GetDictionary(requirementsResult, "requirements");
                AddStrings(requirements, "allowedApprovalPolicies", catalog.AllowedApprovalPolicies);
                AddStrings(requirements, "allowedSandboxModes", catalog.AllowedSandboxModes);
            }
            catch
            {
                // Older app-server builds may not expose managed requirements.
            }
            return catalog;
        }

        public async Task<List<SkillOption>> ListSkillsAsync(string workingDirectory, bool forceReload)
        {
            var response = await RequestAsync("skills/list", new
            {
                cwds = String.IsNullOrWhiteSpace(workingDirectory) ? new string[0] : new[] { workingDirectory },
                forceReload = forceReload
            }).ConfigureAwait(false);
            var result = GetDictionary(response, "result");
            var skills = new List<SkillOption>();
            foreach (object rawEntry in GetObjectList(result, "data"))
            {
                var entry = rawEntry as Dictionary<string, object>;
                if (entry == null) continue;
                foreach (object rawSkill in GetObjectList(entry, "skills"))
                {
                    var skill = rawSkill as Dictionary<string, object>;
                    if (skill == null) continue;
                    skills.Add(new SkillOption
                    {
                        Name = GetString(skill, "name"),
                        Path = GetString(skill, "path"),
                        Description = GetString(skill, "description") ?? GetString(skill, "shortDescription"),
                        Enabled = !skill.ContainsKey("enabled") || GetBool(skill, "enabled"),
                        Scope = GetString(skill, "scope")
                    });
                }
            }
            return skills;
        }

        public async Task<List<AppOption>> ListAppsAsync()
        {
            var response = await RequestAsync("app/list", new
            {
                limit = 100,
                forceRefetch = false,
                threadId = ThreadId
            }).ConfigureAwait(false);
            var result = GetDictionary(response, "result");
            var apps = new List<AppOption>();
            foreach (object raw in GetObjectList(result, "data"))
            {
                var app = raw as Dictionary<string, object>;
                if (app == null) continue;
                apps.Add(new AppOption
                {
                    Id = GetString(app, "id"),
                    Name = GetString(app, "name"),
                    Description = GetString(app, "description"),
                    Accessible = GetBool(app, "isAccessible"),
                    Enabled = !app.ContainsKey("isEnabled") || GetBool(app, "isEnabled")
                });
            }
            return apps;
        }

        public Task SteerTurnAsync(string text)
        {
            if (String.IsNullOrWhiteSpace(ThreadId) || String.IsNullOrWhiteSpace(ActiveTurnId))
                throw new InvalidOperationException("There is no active turn to steer.");
            return RequestAsync("turn/steer", new
            {
                threadId = ThreadId,
                expectedTurnId = ActiveTurnId,
                input = new object[] { new { type = "text", text = text } }
            });
        }

        public Task SetThreadNameAsync(string name)
        {
            EnsureThread();
            return RequestAsync("thread/name/set", new { threadId = ThreadId, name = name });
        }

        public Task ArchiveThreadAsync()
        {
            EnsureThread();
            return RequestAsync("thread/archive", new { threadId = ThreadId });
        }

        public Task DeleteThreadAsync()
        {
            EnsureThread();
            return RequestAsync("thread/delete", new { threadId = ThreadId });
        }

        public async Task<string> ForkThreadAsync(RuntimeSettings settings, string workingDirectory)
        {
            EnsureThread();
            var parameters = new Dictionary<string, object>();
            parameters["threadId"] = ThreadId;
            parameters["excludeTurns"] = true;
            parameters["ephemeral"] = false;
            if (!String.IsNullOrWhiteSpace(workingDirectory)) parameters["cwd"] = workingDirectory;
            if (settings != null)
            {
                parameters["model"] = settings.EffectiveModel;
                parameters["approvalPolicy"] = settings.ApprovalPolicy;
                parameters["approvalsReviewer"] = settings.ApprovalsReviewer;
                parameters["sandbox"] = settings.SandboxMode;
            }
            var response = await RequestAsync("thread/fork", parameters).ConfigureAwait(false);
            var result = GetDictionary(response, "result");
            var thread = GetDictionary(result, "thread");
            string id = GetString(thread, "id");
            if (String.IsNullOrWhiteSpace(id)) throw new InvalidDataException("app-server did not return the forked thread id.");
            ThreadId = id;
            ActiveTurnId = null;
            return id;
        }

        public Task CompactThreadAsync()
        {
            EnsureThread();
            return RequestAsync("thread/compact/start", new { threadId = ThreadId });
        }

        public Task RevertThreadAsync(string beforeTurnId)
        {
            EnsureThread();
            return RequestAsync("thread/revert", new { threadId = ThreadId, beforeTurnId = beforeTurnId });
        }

        public Task StartReviewAsync()
        {
            EnsureThread();
            return RequestAsync("review/start", new
            {
                threadId = ThreadId,
                target = new { type = "uncommittedChanges" },
                delivery = "inline"
            });
        }

        private void EnsureThread()
        {
            if (String.IsNullOrWhiteSpace(ThreadId)) throw new InvalidOperationException("Open or start a thread first.");
        }

        private static bool GetBool(Dictionary<string, object> source, string key)
        {
            object raw;
            if (source == null || !source.TryGetValue(key, out raw) || raw == null) return false;
            try { return Convert.ToBoolean(raw); }
            catch { return false; }
        }

        private static bool ContainsString(Dictionary<string, object> source, string key, string expected)
        {
            foreach (object raw in GetObjectList(source, key))
                if (String.Equals(Convert.ToString(raw), expected, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void AddStrings(Dictionary<string, object> source, string key, IList<string> target)
        {
            foreach (object raw in GetObjectList(source, key))
            {
                string value = raw as string;
                if (!String.IsNullOrWhiteSpace(value) && !target.Contains(value)) target.Add(value);
            }
        }
    }
}
