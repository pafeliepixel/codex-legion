using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexNativeLite
{
    public sealed class HistoryImportResult
    {
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
    }

    public sealed class ProfileSecurity
    {
        private const int Iterations = 100000;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexNativeLite.Auth.v1");
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public string RootDirectory { get; private set; }
        public string ProfileDirectory { get; private set; }
        private string PinPath { get { return Path.Combine(RootDirectory, "pin.json"); } }
        private string HistoryOfferPath { get { return Path.Combine(RootDirectory, "history-import-offered"); } }
        private string AuthPath { get { return Path.Combine(ProfileDirectory, "auth.json"); } }
        private string ProtectedAuthPath { get { return Path.Combine(RootDirectory, "auth.protected"); } }

        public ProfileSecurity()
            : this(null)
        {
        }

        public ProfileSecurity(string rootDirectory)
        {
            RootDirectory = String.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexNativeLite")
                : rootDirectory;
            ProfileDirectory = Path.Combine(RootDirectory, "profile");
            Directory.CreateDirectory(ProfileDirectory);
        }

        public bool HasPin { get { return File.Exists(PinPath); } }
        public bool HasRememberedCredentials { get { return File.Exists(AuthPath) || File.Exists(ProtectedAuthPath); } }
        public bool HasOfferedHistoryImport { get { return File.Exists(HistoryOfferPath); } }
        public bool HasOfficialHistory
        {
            get
            {
                return Directory.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex",
                    "sessions"));
            }
        }

        public bool RequiresPin
        {
            get
            {
                if (!HasPin || !HasRememberedCredentials) return false;
                PinRecord record = ReadRecord();
                DateTime next;
                return !DateTime.TryParse(record.NextChallengeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out next) ||
                    DateTime.UtcNow >= next.ToUniversalTime();
            }
        }

        public DateTime? NextChallengeLocal
        {
            get
            {
                if (!HasPin) return null;
                DateTime next;
                if (!DateTime.TryParse(ReadRecord().NextChallengeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out next))
                    return null;
                return next.ToLocalTime();
            }
        }

        public void SetPin(string pin)
        {
            ValidatePin(pin);
            byte[] salt = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(salt);
            byte[] hash = Derive(pin, salt);
            WriteRecord(new PinRecord
            {
                Salt = Convert.ToBase64String(salt),
                Hash = Convert.ToBase64String(hash),
                NextChallengeUtc = DateTime.UtcNow.AddDays(7).ToString("o")
            });
        }

        public bool VerifyPin(string pin)
        {
            if (!HasPin) return false;
            PinRecord record = ReadRecord();
            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(record.Salt);
                expected = Convert.FromBase64String(record.Hash);
            }
            catch { return false; }

            byte[] actual = Derive(pin ?? "", salt);
            bool equal = FixedTimeEquals(actual, expected);
            if (equal)
            {
                record.NextChallengeUtc = DateTime.UtcNow.AddDays(7).ToString("o");
                WriteRecord(record);
            }
            return equal;
        }

        public void UnsealCredentials()
        {
            if (File.Exists(AuthPath) || !File.Exists(ProtectedAuthPath)) return;
            byte[] protectedBytes = File.ReadAllBytes(ProtectedAuthPath);
            byte[] plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(AuthPath, plain);
            Array.Clear(plain, 0, plain.Length);
        }

        public void SealCredentials()
        {
            if (!File.Exists(AuthPath)) return;
            byte[] plain = File.ReadAllBytes(AuthPath);
            byte[] protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            string temporary = ProtectedAuthPath + ".tmp";
            File.WriteAllBytes(temporary, protectedBytes);
            if (File.Exists(ProtectedAuthPath)) File.Delete(ProtectedAuthPath);
            File.Move(temporary, ProtectedAuthPath);
            File.Delete(AuthPath);
            Array.Clear(plain, 0, plain.Length);
        }

        public void ClearCredentials()
        {
            if (File.Exists(AuthPath)) File.Delete(AuthPath);
            if (File.Exists(ProtectedAuthPath)) File.Delete(ProtectedAuthPath);
        }

        public void ResetPinAndCredentials()
        {
            ClearCredentials();
            if (File.Exists(PinPath)) File.Delete(PinPath);
        }

        public void MarkHistoryImportOffered()
        {
            Directory.CreateDirectory(RootDirectory);
            File.WriteAllText(HistoryOfferPath, DateTime.UtcNow.ToString("o"), Encoding.UTF8);
        }

        public HistoryImportResult ImportOfficialHistory()
        {
            var result = new HistoryImportResult();
            string source = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "sessions");
            string destination = Path.Combine(ProfileDirectory, "sessions");
            if (!Directory.Exists(source)) return result;

            foreach (string sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (sourceFile.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = sourceFile.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destinationFile = Path.Combine(destination, relative);
                try
                {
                    if (File.Exists(destinationFile))
                    {
                        result.Skipped++;
                        continue;
                    }
                    string parent = Path.GetDirectoryName(destinationFile);
                    if (!String.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                    File.Copy(sourceFile, destinationFile, false);
                    result.Imported++;
                }
                catch
                {
                    result.Failed++;
                }
            }
            return result;
        }

        public static void ValidatePin(string pin)
        {
            if (String.IsNullOrWhiteSpace(pin) || pin.Length < 4 || pin.Length > 12)
                throw new ArgumentException("PIN must contain 4 to 12 digits.");
            foreach (char value in pin)
                if (!Char.IsDigit(value)) throw new ArgumentException("PIN must contain digits only.");
        }

        private static byte[] Derive(string pin, byte[] salt)
        {
            using (var derive = new Rfc2898DeriveBytes(pin, salt, Iterations))
                return derive.GetBytes(32);
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        private PinRecord ReadRecord()
        {
            string text = File.ReadAllText(PinPath, Encoding.UTF8);
            return _json.Deserialize<PinRecord>(text) ?? new PinRecord();
        }

        private void WriteRecord(PinRecord record)
        {
            Directory.CreateDirectory(RootDirectory);
            File.WriteAllText(PinPath, _json.Serialize(record), Encoding.UTF8);
        }

        private sealed class PinRecord
        {
            public string Salt { get; set; }
            public string Hash { get; set; }
            public string NextChallengeUtc { get; set; }
        }
    }
}
