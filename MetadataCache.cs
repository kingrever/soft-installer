using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SoftwareInstaller
{
    internal class MetadataCache
    {
        internal class Entry
        {
            public string FilePath { get; set; } = string.Empty;
            public DateTime LastWriteTimeUtc { get; set; }
            public string DisplayName { get; set; } = string.Empty;
            public string? Version { get; set; }
            public long SizeBytes { get; set; }
        }

        private readonly string _path;
        private readonly Dictionary<string, Entry> _entries = new();

        public MetadataCache(string path)
        {
            _path = path;
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var list = JsonSerializer.Deserialize<List<Entry>>(json);
                    if (list != null)
                    {
                        foreach (var e in list)
                            _entries[e.FilePath] = e;
                    }
                }
            }
            catch { }
        }

        public bool TryGet(string filePath, DateTime lastWriteTimeUtc, long sizeBytes, out Entry entry)
        {
            if (_entries.TryGetValue(filePath, out entry))
            {
                if (entry.LastWriteTimeUtc == lastWriteTimeUtc && entry.SizeBytes == sizeBytes)
                    return true;
            }
            entry = default!;
            return false;
        }

        public void Update(string filePath, DateTime lastWriteTimeUtc, long sizeBytes, string displayName, string? version)
        {
            _entries[filePath] = new Entry
            {
                FilePath = filePath,
                LastWriteTimeUtc = lastWriteTimeUtc,
                DisplayName = displayName,
                Version = version,
                SizeBytes = sizeBytes
            };
        }

        public void Save()
        {
            try
            {
                var list = _entries.Values.ToList();
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_path, json);
            }
            catch { }
        }
    }
}
