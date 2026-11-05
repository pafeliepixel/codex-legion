using System;
using System.Collections.Generic;
using System.IO;

namespace CodexNativeLite
{
    public sealed class LocalModelInstall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string State { get; set; }
        public long Bytes { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; }
        public LocalModelChoice Model { get; set; }
        public bool IsPartial { get { return String.Equals(State, "partial", StringComparison.Ordinal); } }
        public bool IsReady { get { return String.Equals(State, "ready", StringComparison.Ordinal); } }
    }

    public sealed class LocalModelStorage
    {
        public List<LocalModelInstall> Discover(string modelDirectory, string activeModelId)
        {
            var result = new List<LocalModelInstall>();
            string root = ModelsRoot(modelDirectory);
            if (!Directory.Exists(root)) return result;
            foreach (string directory in Directory.GetDirectories(root))
            {
                if (IsReparsePoint(directory)) continue;
                LocalModelChoice model = MatchModel(Path.GetFileName(directory));
                bool partial = false;
                bool ready = false;
                DateTime updated = DateTime.MinValue;
                long bytes = 0;
                Scan(directory, ref bytes, ref partial, ref ready, ref updated);
                result.Add(new LocalModelInstall
                {
                    Id = model == null ? Path.GetFileName(directory) : model.Id,
                    Name = model == null ? FriendlyFolder(Path.GetFileName(directory)) : model.Name,
                    FolderPath = directory,
                    State = partial ? "partial" : (ready ? "ready" : "other"),
                    Bytes = bytes,
                    UpdatedAt = updated,
                    IsActive = model != null && String.Equals(model.Id, activeModelId, StringComparison.OrdinalIgnoreCase),
                    Model = model
                });
            }
            result.Sort(delegate(LocalModelInstall left, LocalModelInstall right) { return right.UpdatedAt.CompareTo(left.UpdatedAt); });
            return result;
        }

        public long TotalBytes(string modelDirectory)
        {
            long total = 0;
            foreach (LocalModelInstall install in Discover(modelDirectory, null)) total += install.Bytes;
            return total;
        }

        public long Remove(string modelDirectory, string folderPath)
        {
            string root = WithSeparator(ModelsRoot(modelDirectory));
            string target = Path.GetFullPath(folderPath ?? "");
            string parent = WithSeparator(Path.GetDirectoryName(target));
            if (!String.Equals(root, parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Legion only removes direct children of its model folder.");
            if (!Directory.Exists(target)) return 0;
            if (IsReparsePoint(target)) throw new InvalidOperationException("Legion will not remove a linked model folder.");
            long bytes = DirectoryBytes(target);
            Directory.Delete(target, true);
            return bytes;
        }

        public static string ModelsRoot(string modelDirectory)
        {
            if (String.IsNullOrWhiteSpace(modelDirectory)) throw new ArgumentException("A model directory is required.");
            return Path.GetFullPath(Path.Combine(modelDirectory, "models"));
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1073741824L) return (bytes / 1073741824d).ToString("0.0") + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576d).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0.0") + " KB";
            return bytes + " B";
        }

        private static LocalModelChoice MatchModel(string folder)
        {
            foreach (LocalModelChoice model in LocalModelCatalog.Create())
            {
                string expected = model.Family + "-" + model.Model;
                if (String.Equals(expected, folder, StringComparison.OrdinalIgnoreCase)) return model;
            }
            return null;
        }

        private static string FriendlyFolder(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "Other local model";
            return value.Replace('_', ' ').Replace('-', ' ');
        }

        private static long DirectoryBytes(string directory)
        {
            long bytes = 0;
            bool partial = false;
            bool ready = false;
            DateTime updated = DateTime.MinValue;
            Scan(directory, ref bytes, ref partial, ref ready, ref updated);
            return bytes;
        }

        private static void Scan(string directory, ref long bytes, ref bool partial, ref bool ready, ref DateTime updated)
        {
            try
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        bytes += info.Length;
                        if (info.LastWriteTimeUtc > updated) updated = info.LastWriteTimeUtc;
                        string name = info.Name;
                        if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                            || name.IndexOf(".part.", StringComparison.OrdinalIgnoreCase) >= 0) partial = true;
                        else if (name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                            || name.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)) ready = true;
                    }
                    catch { }
                }
                foreach (string child in Directory.GetDirectories(directory))
                {
                    if (!IsReparsePoint(child)) Scan(child, ref bytes, ref partial, ref ready, ref updated);
                }
            }
            catch { }
        }

        private static bool IsReparsePoint(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }
        }

        private static string WithSeparator(string path)
        {
            string full = Path.GetFullPath(path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full + Path.DirectorySeparatorChar;
        }
    }
}
