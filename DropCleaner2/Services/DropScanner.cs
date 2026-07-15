using DropsCleaner.Models;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DropsCleaner.Services
{
    internal partial class DropScanner
    {
        //^(?<buildgroup>.+?)(?:-(?<date>\d{4}-\d{2}-\d{2}))?(?:_(?<buildnr>\d+)|-(?<!\d{4}-\d{2}-)(?<buildnr>\d+))$
        //^(?<buildgroup>.+?)(?:-(?<date>\d{4}-\d{2}-\d{2}))?_(?<buildnr>\d+)$
        //^(?<buildgroup>.+?)(?:-(?<date>\d{4}-\d{2}-\d{2}))?...  
        //^(?<buildgroup>[a-zA-Z0-9.-]+?)[-_](?<date>\d{4}-?\d{2}-?\d{2})[._](?<buildNr>\d+)$
        [GeneratedRegex(@"^(?<buildgroup>[a-zA-Z0-9.-]+?)[-_](?<date>\d{4}-?\d{2}-?\d{2})[._](?<buildNr>\d+)$")]
        private static partial Regex BuildNameRegex();

        public async Task<List<DropProject>> ScanAsync(string rootPath, IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default)
        {
            var projects = new List<DropProject>();
            var projectDirectories = new DirectoryInfo(rootPath).GetDirectories();
            int total = projectDirectories.Length;
            int current = 0;

            foreach (var projectDirectory in projectDirectories)
            {
                if (BuildNameRegex().IsMatch(projectDirectory.Name))
                {
                    var missplacedBuild = ParseBuild(projectDirectory);
                    missplacedBuild.IsValid = false;
                    missplacedBuild.InvalidReason = "Build folder is located directly in the root directory instead of inside a project folder.";

                    var dummyGroup = new BuildGroup
                    {
                        Name = projectDirectory.Name,
                        Path = projectDirectory.FullName,
                        Builds = [missplacedBuild],
                        TotalSizeBytes = missplacedBuild.TotalSizeBytes,
                        TotalFileCount = missplacedBuild.FileCount
                    };

                    projects.Add(new DropProject
                    {
                        Name = $"[Fehlplatzierter Build] {projectDirectory.Name}",
                        Path = projectDirectory.FullName,
                        BuildGroups = [dummyGroup],
                        TotalSizeBytes = missplacedBuild.TotalSizeBytes,
                        TotalFileCount = missplacedBuild.FileCount
                    });
                }
                else
                {
                    projects.Add(await Task.Run(() => ParseProject(projectDirectory), cancellationToken));
                }

                progress?.Report((++current, total));
            }

            return projects;
        }


        private DropProject ParseProject(DirectoryInfo projectDirectory)
        {
            var allBuilds = new List<DropBuild>();
            long looseFilesBytes = 0;
            int looseFilesCount = 0;

            FindBuildsRecursive(projectDirectory, allBuilds, ref looseFilesBytes, ref looseFilesCount);

            var buildGroups = allBuilds
                .GroupBy(b =>
                {
                    var match = BuildNameRegex().Match(b.Name);
                    return match.Success ? match.Groups["buildgroup"].Value : b.Name;
                })
                .Select(group =>
                {
                    var builds = group.ToList();
                    string groupPath = Path.GetDirectoryName(builds[0].Path) ?? projectDirectory.FullName;

                    bool groupIsValid = builds.Any(b => b.IsValid);
                    string? groupInvalidReason = groupIsValid ? null : $"No build in this group matches the required naming schema '<Name>[-<YYYY-MM-DD>]_<BuildNumber>'. Group name: {group.Key}";

                    return new BuildGroup
                    {
                        Name = group.Key,
                        Path = groupPath,
                        Builds = builds,
                        TotalSizeBytes = builds.Sum(b => b.TotalSizeBytes),
                        TotalFileCount = builds.Sum(b => b.FileCount),
                        IsValid = groupIsValid,
                        InvalidReason = groupInvalidReason
                    };
                })
                .ToList();

            return new DropProject
            {
                Name = projectDirectory.Name,
                Path = projectDirectory.FullName,
                BuildGroups = buildGroups,
                TotalSizeBytes = buildGroups.Sum(g => g.TotalSizeBytes) + looseFilesBytes,
                TotalFileCount = buildGroups.Sum(g => g.TotalFileCount) + looseFilesCount,
                LooseFilesBytes = looseFilesBytes,
                LooseFileCount = looseFilesCount
            };
        }


        private static DropBuild ParseBuild(DirectoryInfo buildDirectory)
        {
            var match = BuildNameRegex().Match(buildDirectory.Name);

            DateOnly? date = null;
            int? buildNumber = null;
            bool isValid = false;
            string? invalidReason = null;

            if (!match.Success)
            {
                invalidReason = "Name does not match required format: <Name>-<YYYY-MM-DD>_<BuildNumber>";
            }
            else
            {
                string dateValue = match.Groups["date"].Value;
                string buildNrValue = match.Groups["buildNr"].Value;

                if (string.IsNullOrEmpty(dateValue))
                {
                    invalidReason = "Date is missing. Required format: <Name>-<YYYY-MM-DD>_<BuildNumber>";
                }
                else if (!DateOnly.TryParse(dateValue, out DateOnly parsedDate)) //robuster gegen fehler
                {
                    invalidReason = $"Invalid date : {dateValue}";
                }
                else if (!int.TryParse(buildNrValue, out int parsedBuildNumber))
                {
                    invalidReason = $"Invalid build number : {buildNrValue}";
                }
                else
                {
                    date = parsedDate;
                    buildNumber = parsedBuildNumber;
                    isValid = true;
                }
            }

            var buildFiles = buildDirectory.EnumerateFiles("*", SearchOption.AllDirectories).ToList();

            return new DropBuild
            {
                Name = buildDirectory.Name,
                Path = buildDirectory.FullName,
                Date = date,
                BuildNumber = buildNumber,
                IsValid = isValid,
                InvalidReason = invalidReason,
                Files = buildFiles.Select(f => f.FullName).ToList(),
                TotalSizeBytes = buildFiles.Sum(f => f.Length),
                FileCount = buildFiles.Count
            };
        }

        private void FindBuildsRecursive(DirectoryInfo currentDirectory, List<DropBuild> collectedBuilds, ref long looseBytes, ref int looseFileCount)
        {
            foreach (var file in currentDirectory.GetFiles())
            {
                looseBytes = looseBytes + file.Length;
                looseFileCount++;
            }

            foreach (var subDirectory in currentDirectory.GetDirectories())
            {
                if (BuildNameRegex().IsMatch(subDirectory.Name))
                {
                    collectedBuilds.Add(ParseBuild(subDirectory));
                }
                else
                {
                    FindBuildsRecursive(subDirectory, collectedBuilds, ref looseBytes, ref looseFileCount);
                }
            }
        }



        public static async Task SaveAsJsonAsync(List<DropProject> projects, string fullOutputPath, CancellationToken cancellationToken = default)
        {
            string? directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };

            await using var stream = System.IO.File.Create(fullOutputPath);
            await JsonSerializer.SerializeAsync(stream, projects, options, cancellationToken);
        }


    }
}