using System.Collections.Generic;

namespace DropsCleaner.Models
{
    internal class DropProject
    {
        public string Name { get; init; }
        public string Path { get; init; }
        public List<BuildGroup> BuildGroups { get; init; }

        public long TotalSizeBytes { get; set; }
        public int TotalFileCount { get; set; }

        public long LooseFilesBytes { get; set; }
        public int LooseFileCount { get; set; }
    }
}