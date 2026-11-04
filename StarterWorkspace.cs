using System;
using System.IO;

namespace CodexNativeLite
{
    public static class StarterWorkspace
    {
        public static string Create()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Create(Path.Combine(documents, "Codex Legion", "Ideas"));
        }

        public static string Create(string rootDirectory)
        {
            if (String.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("A workspace root is required.");
            Directory.CreateDirectory(rootDirectory);

            string stem = "Idea " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            string path = Path.Combine(rootDirectory, stem);
            int suffix = 2;
            while (Directory.Exists(path))
            {
                path = Path.Combine(rootDirectory, stem + " " + suffix);
                suffix++;
            }
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
