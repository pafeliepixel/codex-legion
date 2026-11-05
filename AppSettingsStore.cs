using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace CodexNativeLite
{
    public sealed class AppSettingsStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public AppSettingsStore()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexNativeLite");
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "ui-settings.json");
        }

        public void Load(RuntimeSettings target)
        {
            if (target == null || !File.Exists(_path)) return;
            try
            {
                var values = _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(_path));
                target.Model = Read(values, "model");
                target.ReasoningEffort = Read(values, "reasoningEffort");
                string provider = Read(values, "modelProvider");
                target.ModelProvider = provider == "strata" ? "strata" : "openai";
                target.LocalModelId = Read(values, "localModelId");
                target.LocalModelDirectory = Read(values, "localModelDirectory");
                string parallel = Read(values, "parallelMode");
                if (parallel == "solo" || parallel == "asked" || parallel == "smart") target.ParallelMode = parallel;
                target.LastComponentAuditVersion = Read(values, "lastComponentAuditVersion");
                string permission = Read(values, "permissionMode");
                if (permission == "read-only" || permission == "ask" || permission == "auto-review" || permission == "full")
                    target.PermissionMode = permission;
            }
            catch
            {
                // A broken preference file should never block the app from opening.
            }
        }

        public void Save(RuntimeSettings settings)
        {
            if (settings == null) return;
            var values = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "reasoningEffort", settings.ReasoningEffort },
                { "permissionMode", settings.PermissionMode },
                { "modelProvider", settings.ModelProvider },
                { "localModelId", settings.LocalModelId },
                { "localModelDirectory", settings.LocalModelDirectory },
                { "parallelMode", settings.ParallelMode },
                { "lastComponentAuditVersion", settings.LastComponentAuditVersion }
            };
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, _json.Serialize(values));
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }

        private static string Read(Dictionary<string, object> values, string key)
        {
            object raw;
            return values != null && values.TryGetValue(key, out raw) && raw != null ? Convert.ToString(raw) : null;
        }
    }
}
