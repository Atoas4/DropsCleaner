using DropsCleaner.Models;
using DropsCleaner.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

//\\fileserver\Entwicklung\Drops
namespace DropsCleaner.Commands
{
    internal class AnalyzeCommand : AsyncCommand
    {
        private readonly DropScanner _scanner;

        public AnalyzeCommand(DropScanner scanner)
        {
            _scanner = scanner;
        }

        protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            string inputPath = AnsiConsole.Ask<string>("Enter [green]Folderpath[/]:");

            if (!Directory.Exists(inputPath))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] The specified path does not exist.");
                return 1;
            }

            List<DropProject> projects = [];

            await AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async context =>
                {
                    var task = context.AddTask("[green]Scanning projects...[/]");
                    var progress = new Progress<(int current, int total)>(report =>
                    {
                        task.MaxValue = report.total;
                        task.Value = report.current;
                    });

                    projects = await _scanner.ScanAsync(inputPath, progress, cancellationToken);
                });

            RenderTree(inputPath, projects);
            RenderInvalidBuilds(projects);
            RenderTop5ProjectsBySize(projects);   
            RenderTop5BySize(projects);       
            RenderTop5ByFileCount(projects);


            if (AnsiConsole.Confirm("Do you want to delete Entries?"))
            {
                PromptAndCleanup(projects);
            }

            if (AnsiConsole.Confirm("Save results as JSON?"))
            {
                Console.WriteLine("Enter folder path for save location: ");
                string locationSave = Console.ReadLine();

                string jsonPath = Path.Combine(locationSave, "drops-analysis.json");
                await DropScanner.SaveAsJsonAsync(projects, jsonPath, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Saved:[/] {jsonPath}");
            }

            return 0;
        }

        private static void RenderTree(string rootPath, List<DropProject> projects)
        {
            int totalBuilds = projects.SelectMany(p => p.BuildGroups).Sum(g => g.Builds.Count);
            int totalProjects = projects.Count;
            long totalSize = projects.Sum(p => p.TotalSizeBytes);   
            int totalFiles = projects.Sum(p => p.TotalFileCount);   

            var tree = new Tree($"[yellow]{Markup.Escape(rootPath)}[/] [grey]({totalProjects} projects, {totalBuilds} builds, {FormatSize(totalSize)}, {totalFiles} files)[/]");

            foreach (var project in projects)
            {
                int projectBuildCount = project.BuildGroups.Sum(g => g.Builds.Count);
                long projectSize = project.TotalSizeBytes;          
                int projectFileCount = project.TotalFileCount;      
                int projectInvalidCount = project.BuildGroups.Sum(g => g.Builds.Count(b => !b.IsValid));

                string projectInvalidHint;
                if (projectInvalidCount > 0)
                {
                    projectInvalidHint = $"[red]{projectInvalidCount} invalid[/]";
                }
                else
                {
                    projectInvalidHint = "";
                }

                string looseHint;
                if (project.LooseFileCount > 0)
                {
                    looseHint = ($"[grey](+{FormatSize(project.LooseFilesBytes)} loose[/]");
                }
                else
                {
                    looseHint = "";
                }

                var projectNode = tree.AddNode($"[cyan]{Markup.Escape(project.Name)}[/] [grey]{project.BuildGroups.Count} groups, {projectBuildCount} builds, {FormatSize(projectSize)}, {projectFileCount} files[/]{projectInvalidHint}{looseHint}");

                foreach (var group in project.BuildGroups)
                {
                    bool allInvalid = group.Builds.Count > 0 && group.Builds.All(b => !b.IsValid);
                    int invalidCount = group.Builds.Count(b => !b.IsValid);

                    DropBuild? latest = group.Builds
                        .Where(b => b.IsValid)
                        .OrderByDescending(b => b.Date)
                        .FirstOrDefault();

                    string latestHint;
                    if (latest != null)
                    {
                        latestHint = $"[grey]latest: {latest.Date}[/]";
                    }
                    else
                    {
                        latestHint = "";
                    }
                    
                    if (allInvalid)
                    {
                        projectNode.AddNode($"[red]{Markup.Escape(group.Name)}[/] [grey]{group.Builds.Count} builds, {FormatSize(group.TotalSizeBytes)}, {group.TotalFileCount} files[/] [red](all invalid)[/]");
                        continue;
                    }

                    string invalidHint;
                    if (invalidCount > 0)
                    {
                        invalidHint = ($"[red]{invalidCount} invalid[/]");
                    }
                    else
                    {
                        invalidHint = ""; 
                    }

                    projectNode.AddNode($"[blue]{Markup.Escape(group.Name)}[/] [grey]{group.Builds.Count} builds, {FormatSize(group.TotalSizeBytes)}, {group.TotalFileCount} files[/]{latestHint}{invalidHint}");
                }
            }

            AnsiConsole.Write(tree);
        }

        private static void RenderInvalidBuilds(List<DropProject> projects)
        {
            var invalidGroups = projects
                .SelectMany(p => p.BuildGroups
                .Where(g => !g.IsValid)
                .Select(g => new { Project = p.Name, Group = g }))
                .ToList();

            var invalidBuilds = projects
                .SelectMany(p => p.BuildGroups
                .Where(g => g.IsValid)
                .SelectMany(g => g.Builds
                .Where(b => !b.IsValid)                
                .Select(b => new { Project = p.Name, Group = g.Name, Build = b })))
                .ToList();

            if (invalidGroups.Count == 0 && invalidBuilds.Count == 0)
            {
                return;
            }

            AnsiConsole.WriteLine();
            var table = new Table().Title("[red]Invalid Builds[/]").BorderColor(Color.Red);
            table.AddColumn("Project");
            table.AddColumn("Build Group");
            table.AddColumn("Build Name");
            table.AddColumn("Reason");

            foreach (var entry in invalidGroups)
            {
                table.AddRow(
                    Markup.Escape(entry.Project),
                    $"[red]{Markup.Escape(entry.Group.Name)}[/]",
                    $"[grey]({entry.Group.Builds.Count} builds)[/]",
                    $"[grey]{Markup.Escape(entry.Group.InvalidReason ?? string.Empty)}[/]");
            }

            foreach (var entry in invalidBuilds)
            {
                table.AddRow(
                    Markup.Escape(entry.Project),
                    Markup.Escape(entry.Group),
                    $"[red]{Markup.Escape(entry.Build.Name)}[/]",
                    $"[grey]{Markup.Escape(entry.Build.InvalidReason ?? string.Empty)}[/]");
            }

            AnsiConsole.Write(table);
        }

        private static void RenderTop5BySize(List<DropProject> projects)
        {
            var top5 = projects
                .SelectMany(p => p.BuildGroups
                .SelectMany(g => g.Builds
                .Select(b => new { Project = p.Name, Build = b })))
                .OrderByDescending(x => x.Build.TotalSizeBytes)
                .Take(5)
                .ToList();


            AnsiConsole.WriteLine();
            var table = new Table().Title("[yellow]Top 5 Builds by Size[/]");
            table.AddColumn("Project");
            table.AddColumn("Build");
            table.AddColumn(new TableColumn("Size").RightAligned());

            foreach (var entry in top5)
            {
                table.AddRow(
                    Markup.Escape(entry.Project),
                    Markup.Escape(entry.Build.Name),
                    FormatSize(entry.Build.TotalSizeBytes));
            }

            AnsiConsole.Write(table);
        }

        private static void RenderTop5ByFileCount(List<DropProject> projects)
        {
            var top5 = projects
                .SelectMany(p => p.BuildGroups
                .SelectMany(g => g.Builds
                .Select(b => new { Project = p.Name, Build = b })))
                .OrderByDescending(x => x.Build.FileCount)
                .Take(5)
                .ToList();

            AnsiConsole.WriteLine();
            var table = new Table().Title("[yellow]Top 5 Builds by File Count[/]");
            table.AddColumn("Project");
            table.AddColumn("Build");
            table.AddColumn(new TableColumn("Files").RightAligned());

            foreach (var entry in top5)
            {
                table.AddRow(
                    Markup.Escape(entry.Project),
                    Markup.Escape(entry.Build.Name),
                    entry.Build.FileCount.ToString());
            }
            AnsiConsole.Write(table);
        }

        private static void RenderTop5ProjectsBySize(List<DropProject> projects)
        {
            var top5 = projects
                .OrderByDescending(p => p.TotalSizeBytes)
                .Take(5)
                .ToList();

            AnsiConsole.WriteLine();
            var table = new Table().Title("[yellow]Top 5 Projects by Size[/]");
            table.AddColumn("Project");
            table.AddColumn("Path");
            table.AddColumn(new TableColumn("Size").RightAligned());
            table.AddColumn(new TableColumn("Files").RightAligned());
            table.AddColumn(new TableColumn("Build Groups").RightAligned());

            foreach (var project in top5)
            {
                table.AddRow(
                    Markup.Escape(project.Name),
                    Markup.Escape(project.Path),
                    FormatSize(project.TotalSizeBytes),
                    project.TotalFileCount.ToString(),
                    project.BuildGroups.Count.ToString());
            }

            AnsiConsole.Write(table);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };

            int order = (int)Math.Log(bytes, 1024);
            double value = bytes / Math.Pow(1024, order);
            return $"{value:F0} {suffixes[order]}".Replace(".0", "");
        }

        //////



        public static void PromptAndCleanup(List<DropProject> projects)
        {
            /*var allItems = new List<DropBuild>();

            foreach (var project in projects)
            {
                foreach (var group in project.BuildGroups) 
                {
                    foreach (var build in group.Builds)
                    {
                        allItems.Add(build);
                    }
                }
            }*/



            var invalidItems = new List<DropBuild>();

            foreach (var project in projects)
            {
                foreach (var group in project.BuildGroups)
                {
                    foreach (var build in group.Builds)
                    {
                        if (!build.IsValid)
                        {
                            invalidItems.Add(build);
                        }
                    }
                }
            }

            if (!invalidItems.Any())
            {
                AnsiConsole.MarkupLine("[green]No invalid folders or files found to delete![/]");
                return;
            }

            var prompt = new MultiSelectionPrompt<DropBuild>()
                .Title("Select the folders/files to be deleted (space to select, Enter to confirm):")
                .NotRequired()
                .PageSize(10)
                .MoreChoicesText("[grey](Move with arrows)[/]")
                .InstructionsText("[grey](Press Space to chose and Enter to delete)[/]")
                .UseConverter(build =>
                {
                    string sizeStr = build.TotalSizeBytes > 1024 * 1024 ? $"{build.TotalSizeBytes / (1024 * 1024):N2} MB" : $"{build.TotalSizeBytes / 1024:N2} KB";

                    return $"{build.Name} ({sizeStr}) - [grey]{build.Path}[/]";
                });

            foreach (var item in invalidItems)
            {
                prompt.AddChoice(item);
            }

            /*foreach (var item in allItems)
            {
                prompt.AddChoice(item);
            }*/

            var selectedItems = AnsiConsole.Prompt(prompt); 

            if (!selectedItems.Any())
            {
                AnsiConsole.MarkupLine("[yellow]Deletion process cancelled. No entries selected[/]");
                return;
            }

            if (!AnsiConsole.Confirm($"Are you sure that you want to delete {selectedItems.Count} Entries?"))
            {
                AnsiConsole.MarkupLine("[yellow]Deletion prozess aborted.[/]");
                return;
            }

            //delete
            AnsiConsole.Status()
            .Start("Delete selected data", context =>
            {
                foreach (var item in selectedItems)
                {
                    try
                    {
                        if (File.Exists(item.Path))
                        {
                            File.Delete(item.Path);
                            AnsiConsole.MarkupLine($"[green]File successfully deleted:[/] {item.Name}");
                        }
                        else if (Directory.Exists(item.Path))
                        {
                            Directory.Delete(item.Path, true);
                            AnsiConsole.MarkupLine($"[green]Folder successfully deleted:[/] {item.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error deleting {item.Name}:[/] {ex.Message}");
                    }
                }
            });

            AnsiConsole.MarkupLine("[bold green]Cleanup finished![/]");
        }

        //projekt, buildgruppe, build
        
    }
}
