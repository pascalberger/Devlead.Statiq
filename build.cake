// Install .NET Core Global tools.
#tool "dotnet:https://api.nuget.org/v3/index.json?package=GitVersion.Tool&version=5.6.6"

#load "build/records.cake"
#load "build/helpers.cake"

/*****************************
 * Setup
 *****************************/
Setup(
    static context => {

        var assertedVersions = context.GitVersion(new GitVersionSettings
            {
                OutputType = GitVersionOutput.Json
            });

        var gh = context.GitHubActions();
        var version = assertedVersions.LegacySemVerPadded;
        var branchName = assertedVersions.BranchName;
        var isMainBranch = StringComparer.OrdinalIgnoreCase.Equals("main", branchName);
        var configuration = context.Argument("configuration", "Release");

        context.Information("Building version {0} (Branch: {1}, IsMain: {2})",
            version,
            branchName,
            isMainBranch);

        var artifactsPath = context
                            .MakeAbsolute(context.Directory("./artifacts"));

        return new BuildData(
            version,
            isMainBranch,
            "src",
            configuration,
            new DotNetCoreMSBuildSettings()
                .SetConfiguration(configuration)
                .SetVersion(version)
                .WithProperty("Copyright", $"Mattias Karlsson © {DateTime.UtcNow.Year}")
                .WithProperty("Authors", "devlead")
                .WithProperty("Company", "devlead")
                .WithProperty("PackageLicenseExpression", "MIT")
                .WithProperty("PackageTags", "Statiq;Extensions;StaticContent;StaticSite;Blog;BlogEngine")
                .WithProperty("PackageDescription", "Provides helpers and extensions for the static site generator Statiq, i.e. themes from http uri and TabGroup shortcode.")
                .WithProperty("PackageIconUrl", "https://cdn.jsdelivr.net/gh/devlead/Devlead.Console.Template/src/devlead.png")
                .WithProperty("PackageIcon", "devlead.png")
                .WithProperty("PackageProjectUrl", "https://www.devlead.se")
                .WithProperty("RepositoryUrl", "https://github.com/devlead/Devlead.Statiq.git")
                .WithProperty("RepositoryType", "git")
                .WithProperty("ContinuousIntegrationBuild", gh.IsRunningOnGitHubActions ? "true" : "false")
                .WithProperty("EmbedUntrackedSources", "true"),
            artifactsPath,
            artifactsPath.Combine(version),
            "Devlead.Statiq.TestWeb",
            new [] {
                "net5.0",
                "netcoreapp3.1"
            }
            );
    }
);

/*****************************
 * Tasks
 *****************************/
Task("Clean")
    .Does<BuildData>(
        static (context, data) => context.CleanDirectories(data.DirectoryPathsToClean)
    )
.Then("Restore")
    .Does<BuildData>(
        static (context, data) => context.DotNetCoreRestore(
            data.ProjectRoot.FullPath,
            new DotNetCoreRestoreSettings {
                MSBuildSettings = data.MSBuildSettings
            }
        )
    )
.Then("DPI")
    .Does<BuildData>(
        static (context, data) => context.DotNetCoreTool(
                "tool",
                new DotNetCoreToolSettings {
                    ArgumentCustomization = args => args
                                                        .Append("run")
                                                        .Append("dpi")
                                                        .Append("nuget")
                                                        .Append("--silent")
                                                        .AppendSwitchQuoted("--output", "table")
                                                        .Append(
                                                            (
                                                                !string.IsNullOrWhiteSpace(context.EnvironmentVariable("NuGetReportSettings_SharedKey"))
                                                                &&
                                                                !string.IsNullOrWhiteSpace(context.EnvironmentVariable("NuGetReportSettings_WorkspaceId"))
                                                            )
                                                                ? "report"
                                                                : "analyze"
                                                            )
                                                        .AppendSwitchQuoted("--buildversion", data.Version)
                }
            )
    )
.Then("Build")
    .Does<BuildData>(
        static (context, data) => context.DotNetCoreBuild(
            data.ProjectRoot.FullPath,
            new DotNetCoreBuildSettings {
                NoRestore = true,
                MSBuildSettings = data.MSBuildSettings
            }
        )
    )
.Then("Test")
    .DoesForEach<BuildData, string>(
        static (data, context) => data.TestTargetFrameworks,
        static (data, item, context) => {
            context.Information("Testing target framework {0}", item);
            context.DotNetCoreRun(
                data.TestProjectPath.FullPath,
                "pipelines -l Warning",
                new DotNetCoreRunSettings
                {
                    Configuration = data.Configuration,
                    Framework = item,
                    NoRestore = true,
                    NoBuild = true
                }
            );
        }
    )
    .Default()
.Then("Pack")
    .Does<BuildData>(
        static (context, data) => context.DotNetCorePack(
            data.ProjectRoot.FullPath,
            new DotNetCorePackSettings {
                NoBuild = true,
                NoRestore = true,
                OutputDirectory = data.NuGetOutputPath,
                MSBuildSettings = data.MSBuildSettings
            }
        )
    )
.Then("Push-GitHub-Packages")
    .WithCriteria<BuildData>( (context, data) => data.ShouldPushGitHubPackages())
    .DoesForEach<BuildData, FilePath>(
        static (data, context)
            => context.GetFiles(data.NuGetOutputPath.FullPath + "/*.nupkg"),
        static (data, item, context)
            => context.DotNetCoreNuGetPush(
                item.FullPath,
            new DotNetCoreNuGetPushSettings
            {
                Source = data.GitHubNuGetSource,
                ApiKey = data.GitHubNuGetApiKey
            }
        )
    )
.Then("Push-NuGet-Packages")
    .WithCriteria<BuildData>( (context, data) => data.ShouldPushNuGetPackages())
    .DoesForEach<BuildData, FilePath>(
        static (data, context)
            => context.GetFiles(data.NuGetOutputPath.FullPath + "/*.nupkg"),
        static (data, item, context)
            => context.DotNetCoreNuGetPush(
                item.FullPath,
                new DotNetCoreNuGetPushSettings
                {
                    Source = data.NuGetSource,
                    ApiKey = data.NuGetApiKey
                }
        )
    )
.Then("GitHub-Actions")
.Run();