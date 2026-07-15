using System.Collections.Generic;

namespace DropsCleaner.Models
{
    internal class BuildGroup
    {
        public string Name { get; init; } 

        public string Path { get; init; } 

        public List<DropBuild> Builds { get; init; }

        public long TotalSizeBytes { get; init; }

        public int TotalFileCount { get; init; }

        public bool IsValid { get; init; }

        public string? InvalidReason { get; init; }
    }
}