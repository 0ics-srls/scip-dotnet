using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ScipDotnet;

/// <summary>
/// Orchestrates Roslyn and MSBuild APIs to SCIP index a given project.
/// </summary>
public class ScipProjectIndexer
{
    public ScipProjectIndexer(ILogger<ScipProjectIndexer> logger) =>
        Logger = logger;

    private ILogger<ScipProjectIndexer> Logger { get; }

    private void Restore(IndexCommandOptions options, FileInfo project)
    {
        var isSolution = project.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                     || project.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
        var arguments = isSolution ? $"restore {project.FullName} /p:EnableWindowsTargeting=true" : "restore /p:EnableWindowsTargeting=true";
        if (options.NugetConfigPath != null)
        {
            arguments += $" --configfile \"{options.NugetConfigPath.FullName}\"";
        }
        var process = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                WorkingDirectory = options.WorkingDirectory.FullName,
                FileName = "dotnet",
                Arguments = arguments
            }
        };
        options.Logger.LogInformation("$ dotnet {Arguments}", arguments);
        process.Start();
        if (!process.WaitForExit(options.DotnetRestoreTimeout))
        {
            Logger.LogWarning("Dotnet restore did not finish in {Time} milliseconds, the results of the indexing might be incorrect.", options.DotnetRestoreTimeout);
        }
    }

    public async IAsyncEnumerable<Scip.Document> IndexDocuments(IHost host, IndexCommandOptions options)
    {
        var indexedProjects = new HashSet<ProjectId>();
        foreach (var project in options.ProjectsFile)
        {
            await foreach (var document in IndexProject(host, options, project, indexedProjects))
            {
                yield return document;
            }
        }
    }

    private async IAsyncEnumerable<Scip.Document> IndexProject(IHost host,
                                                               IndexCommandOptions options,
                                                               FileInfo rootProject,
                                                               HashSet<ProjectId> indexedProjects)
    {
        if (!options.SkipDotnetRestore)
        {
            Restore(options, rootProject);
        }

        List<Project> projects;
        var workspace = host.Services.GetRequiredService<MSBuildWorkspace>();

        if (string.Equals(rootProject.Extension, ".csproj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rootProject.Extension, ".vbproj", StringComparison.OrdinalIgnoreCase))
        {
            projects = new List<Project> { await workspace.OpenProjectAsync(rootProject.FullName) };
        }
        else if (string.Equals(rootProject.Extension, ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            // .slnx is XML — MSBuild SolutionFile doesn't support it yet.
            // Parse manually and open each project individually.
            projects = new List<Project>();
            var slnxDir = rootProject.DirectoryName!;
            var doc = System.Xml.Linq.XDocument.Load(rootProject.FullName);
            var projectPaths = doc.Descendants("Project")
                .Select(e => e.Attribute("Path")?.Value)
                .Where(p => p != null)
                .Select(p => Path.GetFullPath(Path.Combine(slnxDir, p!)));
            foreach (var projectPath in projectPaths)
            {
                if (!File.Exists(projectPath))
                {
                    Logger.LogWarning("Project not found: {ProjectPath}", projectPath);
                    continue;
                }

                // Check if already loaded as a dependency of a previous project
                var existing = workspace.CurrentSolution.Projects
                    .FirstOrDefault(p => string.Equals(p.FilePath, projectPath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    projects.Add(existing);
                    continue;
                }

                try
                {
                    projects.Add(await workspace.OpenProjectAsync(projectPath));
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Failed to open project {ProjectPath}: {Message}", projectPath, ex.Message);
                }
            }
        }
        else
        {
            projects = (await workspace.OpenSolutionAsync(rootProject.FullName)).Projects.ToList();
        }


        var totalProjects = projects.Count;
        options.Logger.LogInformation("Loaded {Count} projects from {Root}", totalProjects, rootProject.Name);
        var projectsPerProjFile = projects.GroupBy(x => x.FilePath);
        var framework = $"net{Environment.Version.Major}.0";
        var projectIndex = 0;
        foreach (var projectGroup in projectsPerProjFile)
        {

            // If the project was found by opening the solution, we need to find the project that matches the framework.
            // if we can' fall back to the first one. Without this, we will process the same document multiple times
            // once for each framework version being targeting and it leads to unpredictable results since the scip file
            // will contain the same document multiple times iwth different symbols.
            var project = projectGroup.FirstOrDefault(x => x.Name.Contains($"({framework})", StringComparison.OrdinalIgnoreCase)) ?? projectGroup.First();
            if (project.Language != "C#" && project.Language != "Visual Basic")
            {
                Logger.LogWarning(
                    "Skipping project {ProjectFilePath} because it has language {ProjectLanguage} and scip-dotnet currently only supports C# and Visual Basic.",
                    project.FilePath, project.Language);
                continue;
            }

            if (indexedProjects.Contains(project.Id))
            {
                continue;
            }

            indexedProjects.Add(project.Id);
            projectIndex++;

            var globals = new Dictionary<ISymbol, ScipSymbol>(SymbolEqualityComparer.Default);
            var docCount = project.Documents.Count();

            options.Logger.LogInformation("[{Index}/{Total}] Indexing {ProjectName} ({DocCount} documents)",
                projectIndex, totalProjects, project.Name, docCount);

            var indexed = 0;
            foreach (var document in project.Documents)
            {
                if (options.Matcher.Match(options.WorkingDirectory.FullName, document.FilePath).HasMatches)
                {
                    yield return await IndexDocument(document, options, globals, project.Language);
                    indexed++;
                }
            }

            options.Logger.LogInformation("[{Index}/{Total}] {ProjectName}: {Indexed} documents indexed",
                projectIndex, totalProjects, project.Name, indexed);
        }
    }

    private async Task<Scip.Document> IndexDocument(Document document,
                                                    IndexCommandOptions options,
                                                    Dictionary<ISymbol, ScipSymbol> globals,
                                                    string language)
    {
        Scip.Document doc = new()
        {
            Language = language,
            RelativePath = document.FilePath == null
                ? null
                : Path.GetRelativePath(options.WorkingDirectory.FullName, document.FilePath)
        };
        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
        {
            Logger.LogWarning(
                "Skipping document {DocumentFilePath} because document.GetSemanticModelAsync() returned null",
                document.FilePath);
        }
        else
        {
            var symbolFormatter = new ScipDocumentIndexer(doc, options, globals);
            var root = await document.GetSyntaxRootAsync();
            if (language == "C#")
            {
                var walker = new ScipCSharpSyntaxWalker(symbolFormatter, semanticModel);
                walker.Visit(root);
            }
            else if (language == "Visual Basic")
            {
                var walker = new ScipVisualBasicSyntaxWalker(symbolFormatter, semanticModel);
                walker.Visit(root);
            }
        }

        return doc;
    }
}