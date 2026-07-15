using System;
using System.Collections.Generic;
using System.IO;

namespace DropsCleaner.Models
{
    internal class DropBuild
    {
        public string Name { get; init; }
        public string Path { get; init; }
        public DateOnly? Date { get; init; }
        public int? BuildNumber { get; init; }
        public bool IsValid { get; set; }
        public string? InvalidReason { get; set; }
        public List<string> Files { get; init; }
        public long TotalSizeBytes { get; init; }
        public int FileCount { get; init; }
    }
}