using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexNativeLite
{
    public sealed class VoiceCredentialStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexLegion.Voice.v1");
        private readonly string _path;

        public VoiceCredentialStore()
            : this(null)
        {
        }

        public VoiceCredentialStore(string rootDirectory)
        {
            string directory = String.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexNativeLite")
                : rootDirectory;
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "voice-key.protected");
        }

        public bool HasKey { get { return File.Exists(_path); } }

        public void Save(string apiKey)
        {
            if (String.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Enter an API key.");
            byte[] plain = Encoding.UTF8.GetBytes(apiKey.Trim());
            try
            {
                byte[] protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                string temporary = _path + ".tmp";
                File.WriteAllBytes(temporary, protectedBytes);
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public string Load()
        {
            if (!File.Exists(_path)) return null;
            byte[] protectedBytes = File.ReadAllBytes(_path);
            byte[] plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        public void Clear()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
