//--------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r "paket:
nuget BlackFox.Fake.BuildTask
nuget Fake.Core.Target
nuget Fake.Core.Process
nuget Fake.Core.ReleaseNotes
nuget Fake.IO.FileSystem
nuget Fake.DotNet.Cli
nuget Fake.DotNet.MSBuild
nuget Fake.DotNet.AssemblyInfoFile
nuget Fake.DotNet.Paket
nuget Fake.DotNet.FSFormatting
nuget Fake.DotNet.Fsi
nuget Fake.DotNet.NuGet
nuget Fake.Api.Github
nuget Fake.DotNet.Testing.Expecto //"

#load ".fake/build.fsx/intellisense.fsx"

open BlackFox.Fake
open System.IO
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing
open Fake.IO.Globbing.Operators
open Fake.DotNet.Testing
open Fake.Tools
open Fake.Api
open Fake.Tools.Git

Target.initEnvironment ()

let release = Fake.Core.ReleaseNotes.load ("RELEASE_NOTES.md")

//Nuget package info
let authors = "Kevin Schneider"
let title = "Dash.NET.Template"
let owners = "Kevin Schneider, Plotly"
let description = "Template to get you started with Dash.NET"
let projectUrl = "https://github.com/plotly/Dash.NET.Template"
let tags = "dotnet-new templates F# FSharp C# CSharp plotly dash data-viz"
let releaseNotes = (release.Notes |> String.concat "\r\n")
let projectRepo = "https://github.com/plotly/Dash.NET.Template"

let stableVersion = SemVer.parse release.NugetVersion

let stableVersionTag = (sprintf "%i.%i.%i" stableVersion.Major stableVersion.Minor stableVersion.Patch )

let mutable prereleaseSuffix = ""

let mutable prereleaseTag = ""

let mutable isPrerelease = false

let pkgDir = "pkg"

[<AutoOpen>]
module MessagePrompts =

    let prompt (msg:string) =
        System.Console.Write(msg)
        System.Console.ReadLine().Trim()
        |> function | "" -> None | s -> Some s
        |> Option.map (fun s -> s.Replace ("\"","\\\""))

    let rec promptYesNo msg =
        match prompt (sprintf "%s [Yn]: " msg) with
        | Some "Y" | Some "y" -> true
        | Some "N" | Some "n" -> false
        | _ -> System.Console.WriteLine("Sorry, invalid answer"); promptYesNo msg

    let releaseMsg = """This will stage all uncommitted changes, push them to the origin and bump the release version to the latest number in the RELEASE_NOTES.md file. 
        Do you want to continue?"""

    let releaseDocsMsg = """This will push the docs to gh-pages. Remember building the docs prior to this. Do you want to continue?"""


let runDotNet cmd workingDir =
    let result =
        Fake.DotNet.DotNet.exec (Fake.DotNet.DotNet.Options.withWorkingDirectory workingDir) cmd ""
    if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir

let setPrereleaseTag = BuildTask.create "SetPrereleaseTag" [] {
    printfn "Please enter pre-release package suffix"
    let suffix = System.Console.ReadLine()
    prereleaseSuffix <- suffix
    prereleaseTag <- (sprintf "%s-%s" release.NugetVersion suffix)
    isPrerelease <- true
}

let clean = BuildTask.create "clean" [] {
    !! "templates/**/bin"
    ++ "templates/**/obj"
    ++ "tests"
    ++ "pkg"
    |> Shell.cleanDirs 
}

let pack = BuildTask.create "Pack" [clean] {
    "Dash.NET.Template.fsproj"
    |> Fake.DotNet.DotNet.pack (fun p ->
        let msBuildParams =
            {p.MSBuildParams with 
                Properties = ([
                    "Version",(sprintf "%i.%i.%i" stableVersion.Major stableVersion.Minor stableVersion.Patch )
                    "Authors",      authors
                    "Title",        title
                    "Owners",       owners
                    "Description",  description
                    "PackageProjectUrl",   projectUrl
                    "PackageTags",         tags
                    "PackageReleaseNotes", releaseNotes
                    "RepositoryUrl",projectRepo
                    "RepositoryType","git"
                ] @ p.MSBuildParams.Properties)
            }
        {
            p with 
                MSBuildParams = msBuildParams
                OutputPath = Some pkgDir
        }
    )
}


let packPrerelease = BuildTask.create "PackPrerelease" [clean] {
    "Dash.NET.Template.fsproj"
    |> Fake.DotNet.DotNet.pack (fun p ->
        let msBuildParams =
            {p.MSBuildParams with 
                Properties = ([
                    "Version",(sprintf "%i.%i.%i" stableVersion.Major stableVersion.Minor stableVersion.Patch )
                    "Authors",      authors
                    "Title",        title
                    "Owners",       owners
                    "Description",  description
                    "PackageProjectUrl",   projectUrl
                    "PackageTags",         tags
                    "PackageReleaseNotes", releaseNotes
                    "RepositoryUrl",projectRepo
                    "RepositoryType","git"
                ] @ p.MSBuildParams.Properties)
            }
        {
            p with 
                VersionSuffix = Some prereleaseSuffix
                MSBuildParams = msBuildParams
                OutputPath = Some pkgDir
        }
    )
}


let createTag = BuildTask.create "CreateTag" [clean; pack] {
    if promptYesNo (sprintf "tagging branch with %s OK?" stableVersionTag ) then
        Git.Branches.tag "" stableVersionTag
        Git.Branches.pushTag "" projectRepo stableVersionTag
    else
        failwith "aborted"
}

let createPrereleaseTag = BuildTask.create "CreatePrereleaseTag" [setPrereleaseTag; clean; packPrerelease] {
    if promptYesNo (sprintf "tagging branch with %s OK?" prereleaseTag ) then 
        Git.Branches.tag "" prereleaseTag
        Git.Branches.pushTag "" projectRepo prereleaseTag
    else
        failwith "aborted"
}

let publishNuget = BuildTask.create "PublishNuget" [clean; pack] {
    let targets = (!! (sprintf "%s/*.*pkg" pkgDir ))
    for target in targets do printfn "%A" target
    let msg = sprintf "release package with version %s?" stableVersionTag
    if promptYesNo msg then
        let source = "https://api.nuget.org/v3/index.json"
        let apikey =  Environment.environVar "NUGET_KEY"
        for artifact in targets do
            let result = Fake.DotNet.DotNet.exec id "nuget" (sprintf "push -s %s -k %s %s --skip-duplicate" source apikey artifact)
            if not result.OK then failwith "failed to push packages"
    else failwith "aborted"
}

let publishNugetPrerelease = BuildTask.create "PublishNugetPrerelease" [clean; packPrerelease] {
    let targets = (!! (sprintf "%s/*.*pkg" pkgDir ))
    for target in targets do printfn "%A" target
    let msg = sprintf "release package with version %s?" prereleaseTag
    if promptYesNo msg then
        let source = "https://api.nuget.org/v3/index.json"
        let apikey =  Environment.environVar "NUGET_KEY"
        for artifact in targets do
            let result = Fake.DotNet.DotNet.exec id "nuget" (sprintf "push -s %s -k %s %s --skip-duplicate" source apikey artifact)
            if not result.OK then failwith "failed to push packages"
    else failwith "aborted"
}

let uninstallTemplate =  BuildTask.create "uninstallTemplate" [] {
    runDotNet "new -u Dash.NET.Template" __SOURCE_DIRECTORY__
}

let installTemplate =  BuildTask.create "installTemplate" [pack] {
    runDotNet 
        (sprintf "new -i %s" (pkgDir @@ (sprintf "Dash.NET.Template.%i.%i.%i.nupkg" stableVersion.Major stableVersion.Minor stableVersion.Patch)))
        __SOURCE_DIRECTORY__
}

let testTemplate =  BuildTask.create "testTemplate" [installTemplate] {

    Directory.create (__SOURCE_DIRECTORY__ @@ "tests/dash")

    runDotNet "new dash" (__SOURCE_DIRECTORY__ @@ "tests/dash")
    //runDotNet "fake build" (__SOURCE_DIRECTORY__ @@ "tests/slim")



}

let _test = BuildTask.createEmpty "Test" [installTemplate; testTemplate; uninstallTemplate]

let _release = BuildTask.createEmpty "Release" [clean; pack; publishNuget]

let _releasePreview = BuildTask.createEmpty "ReleasePreview" [clean; setPrereleaseTag; packPrerelease; publishNugetPrerelease]

let bootstrapBuildDependencies = BuildTask.createEmpty "bootstrapBuildDependencies" []

BuildTask.runOrDefaultWithArguments pack