using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexNativeLite
{
    public sealed class LocalDownloadProgress
    {
        public string Stage { get; set; }
        public string Label { get; set; }
        public string Message { get; set; }
        public int Percent { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public TimeSpan? TimeRemaining { get; set; }
        public bool IsDeterminate { get { return TotalBytes > 0; } }
        public bool IsStalled { get; set; }
    }

    public sealed class LocalDownloadProgressParser
    {
        private static readonly Regex Progress = new Regex(@"^\s*(.+?):\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*(GB|MB)\s*\(([0-9]+(?:\.[0-9]+)?)%\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Ansi = new Regex("\\x1B\\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
        private readonly Dictionary<string, Sample> _samples = new Dictionary<string, Sample>(StringComparer.OrdinalIgnoreCase);

        public bool TryParse(string raw, DateTime now, out LocalDownloadProgress progress)
        {
            progress = null;
            string line = Clean(raw);
            if (String.IsNullOrWhiteSpace(line)) return false;
            Match match = Progress.Match(line);
            if (match.Success)
            {
                string unit = match.Groups[4].Value.ToUpperInvariant();
                double factor = unit == "GB" ? 1000000000d : 1000000d;
                long have = (long)(Double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * factor);
                long total = (long)(Double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) * factor);
                int percent = (int)Math.Max(0, Math.Min(100, Math.Round(Double.Parse(match.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture))));
                string label = match.Groups[1].Value.Trim();
                double speed = 0;
                Sample previous;
                if (_samples.TryGetValue(label, out previous))
                {
                    double seconds = (now - previous.At).TotalSeconds;
                    if (seconds > 0 && have >= previous.Bytes) speed = (have - previous.Bytes) / seconds;
                }
                _samples[label] = new Sample { Bytes = have, At = now };
                TimeSpan? remaining = speed > 1 && total > have ? TimeSpan.FromSeconds((total - have) / speed) : (TimeSpan?)null;
                progress = new LocalDownloadProgress
                {
                    Stage = "Downloading",
                    Label = label,
                    Message = line,
                    Percent = percent,
                    DownloadedBytes = have,
                    TotalBytes = total,
                    BytesPerSecond = speed,
                    TimeRemaining = remaining
                };
                return true;
            }
            string lower = line.ToLowerInvariant();
            if (lower.IndexOf("no data for", StringComparison.Ordinal) >= 0 && lower.IndexOf("stuck", StringComparison.Ordinal) >= 0)
            {
                progress = new LocalDownloadProgress { Stage = "Connection paused", Message = line, IsStalled = true };
                return true;
            }
            if (lower.IndexOf("checking", StringComparison.Ordinal) >= 0 || lower.IndexOf("sha-256", StringComparison.Ordinal) >= 0)
            {
                progress = new LocalDownloadProgress { Stage = "Verifying", Message = line };
                return true;
            }
            if (lower.IndexOf("preparing", StringComparison.Ordinal) >= 0 || lower.IndexOf("packing", StringComparison.Ordinal) >= 0)
            {
                progress = new LocalDownloadProgress { Stage = "Preparing", Message = line };
                return true;
            }
            return false;
        }

        private static string Clean(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : Ansi.Replace(value, "").Trim();
        }

        private sealed class Sample
        {
            public long Bytes;
            public DateTime At;
        }
    }

    public sealed class LocalProgressTextBuffer
    {
        private readonly StringBuilder _buffer = new StringBuilder();

        public IList<string> Feed(char[] chars, int count)
        {
            var lines = new List<string>();
            for (int index = 0; index < count; index++)
            {
                char value = chars[index];
                if (value == '\r' || value == '\n')
                {
                    if (_buffer.Length > 0)
                    {
                        lines.Add(_buffer.ToString());
                        _buffer.Clear();
                    }
                }
                else _buffer.Append(value);
            }
            return lines;
        }

        public string Flush()
        {
            if (_buffer.Length == 0) return null;
            string value = _buffer.ToString();
            _buffer.Clear();
            return value;
        }
    }
}
