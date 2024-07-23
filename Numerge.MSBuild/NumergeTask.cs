using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.NET.StringTools;

namespace Numerge.MSBuild;

public class NumergeTask : Task
{
    public string? NumergeConfigFile { get; set; }

    public bool NumergeClearIntermediatePackages { get; set; }

    [Required] public string PackageVersion { get; set; } = null!;
    [Required] public string Configuration { get; set; } = null!;
    [Required] public string ProjectDirectory { get; set; } = null!;

    public override bool Execute()
    {
        var numergeConfigFile = NumergeConfigFile ?? "numerge.config.json";
        var projectDirectory = ProjectDirectory!;
        if (!Path.IsPathRooted(numergeConfigFile))
        {
            Log.LogMessage(MessageImportance.Low,
                "Numerge config path ({0}) isn't rooted, evaluating as relative to current project file ({1})",
                numergeConfigFile, projectDirectory);
            numergeConfigFile = Path.Combine(projectDirectory, numergeConfigFile);
        }

        Log.LogMessage(MessageImportance.Low, "Target numerge config file: {0}", numergeConfigFile);
        if (!File.Exists(numergeConfigFile))
        {
            Log.LogError("Numerge config file doesn't exists at {0}", numergeConfigFile);
            return false;
        }

        var config = MergeConfiguration.LoadFile(numergeConfigFile);

        var solutionDirectory = FindSolutionDirectory(projectDirectory);
        Log.LogMessage(MessageImportance.Low, "Finding packages in {0}", solutionDirectory);

        var tempPath = Path.GetTempPath() + Guid.NewGuid();
        Directory.CreateDirectory(tempPath);

        try
        {
            MovePackagesToTempDirectory(solutionDirectory, "nupkg", Configuration, tempPath, PackageVersion,
                NumergeClearIntermediatePackages);
            MovePackagesToTempDirectory(solutionDirectory, "snupkg", Configuration, tempPath, PackageVersion,
                NumergeClearIntermediatePackages);

            return NugetPackageMerger.Merge(tempPath, Path.Combine(projectDirectory, "bin", Configuration), config,
                new NumergeMSBuildLogger(Log));
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    private void MovePackagesToTempDirectory(string solutionDirectory, string extension, string configuration,
        string destination,
        string version, bool move)
    {
        var files = Directory.GetFiles(solutionDirectory, "*." + extension, SearchOption.AllDirectories)
            .Where(s => s.Contains(configuration))
            .Where(s => s.EndsWith($"{version}.{extension}"));

        foreach (var file in files)
        {
            if (move)
            {
                File.Move(file, Path.Combine(destination, Path.GetFileName(file)));
            }
            else
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }
    }

    private string FindSolutionDirectory(string projectDirectory)
    {
        while (!string.IsNullOrEmpty(projectDirectory))
        {
            var solutionFiles = Directory.GetFiles(projectDirectory, "*.sln");
            if (solutionFiles.Length > 0)
            {
                return projectDirectory;
            }

            projectDirectory = Path.GetDirectoryName(projectDirectory)!;
        }

        return Path.GetDirectoryName(projectDirectory)!;
    }
}