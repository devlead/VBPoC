Imports System
Imports System.Linq
Imports CakeBridge
Imports Cake.Core
Imports Cake.Core.Diagnostics
Imports Cake.Core.IO
Imports Cake.Common
Imports Cake.Common.IO
Imports Cake.Common.Diagnostics
Imports Cake.Common.Tools.DotNetCore
Imports Cake.Common.Tools.DotNetCore.Build
Imports Cake.Common.Tools.DotNetCore.Pack
Imports Cake.Common.Tools.DotNetCore.Restore
Imports Cake.Common.Tools.DotNetCore.Test

Module Program
    Sub Main()
        '//////////////////////////////////////////////////////////////////////
        '// ARGUMENTS
        '//////////////////////////////////////////////////////////////////////
        Dim target          = Context.Argument("target", "Default"),
            configuration   = Context.Argument("configuration", "Release")

        '//////////////////////////////////////////////////////////////////////
        '// GLOBALS
        '//////////////////////////////////////////////////////////////////////
        Dim nugetRoot       As DirectoryPath    = Nothing,
            solution        As FilePath         = Nothing,
            solutionDir     As DirectoryPath    = Nothing,
            semVersion      As String           = Nothing,
            assemblyVersion As String           = Nothing,
            fileVersion     As String           = Nothing

        '//////////////////////////////////////////////////////////////////////
        '// SETUP / TEARDOWN
        '//////////////////////////////////////////////////////////////////////
        Setup(
            Sub(ctx As ICakeContext)
                ctx.Information("Setting up...")

                solution = ctx.GetFiles("./src/*.sln").Select(Function(file as FilePath) ctx.MakeAbsolute(file)).FirstOrDefault()

                If solution Is Nothing Then
                    Throw New Exception("Failed to find solution")
                End If

                solutionDir = solution.GetDirectory()
                nugetRoot = ctx.MakeAbsolute(ctx.Directory("./nuget"))
                
                Dim releaseNotes    = ctx.ParseReleaseNotes("./ReleaseNotes.md")
                assemblyVersion     = releaseNotes.Version.ToString()
                fileVersion         = assemblyVersion
                semVersion          = $"{assemblyVersion}-alpha"

                ctx.Information("Executing build {0}...", semVersion)
            End Sub
        )

        Teardown(
            Sub(ctx As ITeardownContext) ctx.Information("Tearing down...")
        )

        '//////////////////////////////////////////////////////////////////////
        '// TASKS
        '//////////////////////////////////////////////////////////////////////
        Dim cleanTask = Task("Clean").Does(
            Sub()
                Context.CleanDirectories($"{solutionDir.FullPath}/**/bin/{configuration}")
                Context.CleanDirectories($"{solutionDir.FullPath}/**/obj/{configuration}")
                Context.CleanDirectory(nugetRoot)
            End Sub
            )

        Dim restoreTask = Task("Restore").Does(
            Sub() Context.DotNetCoreRestore(solution.FullPath,
                                  New DotNetCoreRestoreSettings With {
                                  .Sources = {"https://api.nuget.org/v3/index.json"}
                                  })
            ).IsDependentOn(cleanTask)

        Dim buildTask = Task("Build").Does(
            Sub() Context.DotNetCoreBuild(solution.FullPath,
                                  New DotNetCoreBuildSettings With {
                                  .Configuration = configuration,
                                  .ArgumentCustomization = Function(args) args.Append(
                                                                                "/p:Version={0}", semVersion
                                                                            ).Append(
                                                                                "/p:AssemblyVersion={0}", assemblyVersion
                                                                            ).Append(
                                                                                "/p:FileVersion={0}", fileVersion
                                                                            )
                                  })
            ).IsDependentOn(restoreTask)

        Dim testTask = Task("Test").Does(
            Sub()
                For Each project In Context.GetFiles("./src/**/*.Tests.vbproj")
                    Context.DotNetCoreTest(project.FullPath,
                                    New DotNetCoreTestSettings With { 
                                        .Configuration = configuration,
                                        .NoBuild = True 
                                        })
                Next
            End Sub
            ).IsDependentOn(buildTask)

        Dim packTask = Task("Pack").Does(
            Sub()
                For Each project In (Context.GetFiles("./src/**/*.vbproj") - Context.GetFiles("./src/**/*.Tests.vbproj"))
                    Context.DotNetCorePack(project.FullPath,
                                    New DotNetCorePackSettings With { 
                                        .Configuration = configuration,
                                        .OutputDirectory = nugetRoot,
                                        .NoBuild = True,
                                        .ArgumentCustomization = Function(args) args.Append(
                                                                                        "/p:Version={0}", semVersion
                                                                                    ).Append(
                                                                                        "/p:AssemblyVersion={0}", assemblyVersion
                                                                                    ).Append(
                                                                                        "/p:FileVersion={0}", fileVersion
                                                                                    )
                                        })
                Next
            End Sub
            ).IsDependentOn(testTask)

        Task("Default").IsDependentOn(packTask)

        '//////////////////////////////////////////////////////////////////////
        '// EXECUTION
        '//////////////////////////////////////////////////////////////////////
        RunTarget(target)
    End Sub
End Module