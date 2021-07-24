﻿// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

// This is a FAKE 5.0 script, run using
//    dotnet fake build

#r "paket: groupref fake //"

#if !FAKE
#load ".fake/build.fsx/intellisense.fsx"
#r "netstandard"
#endif

open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
open Fake.DotNet
open Fake.IO
open Fake.Tools
//open Fake.ReleaseNotesHelper
//open Fake.AssemblyInfoFile
open System
open System.IO

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "src/FSharp.Control.AsyncSeq"

// File system information
let solutionFile = "FSharp.Control.AsyncSeq.sln"

let summary = "Asynchronous sequences for F#"

let license = "Apache 2.0 License"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let projectRepo = "https://github.com/fsprojects/FSharp.Control.AsyncSeq"

let configuration = DotNet.BuildConfiguration.fromEnvironVarOrDefault "configuration" DotNet.BuildConfiguration.Release

// Folder to deposit deploy bin
let binDir = __SOURCE_DIRECTORY__ @@ "bin"

// Read additional information from the release notes document
let release = ReleaseNotes.load "RELEASE_NOTES.md"


let versionPropsTemplate = $"\
<Project>
  <PropertyGroup>
    <Version>%s{release.NugetVersion}</Version>
  </PropertyGroup>
</Project>"

// --------------------------------------------------------------------------------------
// Clean build results

Target.create "Clean" (fun _ ->
    DotNet.exec id "clean" "" |> ignore
    Shell.cleanDirs ["bin"; "temp"]
)

// Generate assembly info files with the right version & up-to-date information
Target.create "AssemblyInfo" (fun _ ->
    let info = [
        AssemblyInfo.Product project
        AssemblyInfo.Description summary
        AssemblyInfo.Version release.AssemblyVersion
        AssemblyInfo.FileVersion release.AssemblyVersion
        AssemblyInfo.InformationalVersion release.NugetVersion
        AssemblyInfo.Copyright license
    ]

    AssemblyInfoFile.createFSharp "src/Common/AssemblyInfo.fs" info
    AssemblyInfoFile.createCSharp "src/Common/AssemblyInfo.cs" info
    File.WriteAllText("version.props", sprintf """<Project>
      <PropertyGroup>
        <Version>%s</Version>
      </PropertyGroup>
    </Project>""" release.NugetVersion)
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target.create "Build" (fun _ ->
    solutionFile
    |> DotNet.build (fun opts -> { opts with Configuration = configuration } ))

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target.create "Test" (fun _ ->
    solutionFile
    |> DotNet.test (fun opts ->
        { opts with
            Blame = true
            NoBuild = true
            Configuration = configuration
            ResultsDirectory = Some "TestResults"
            Logger = Some "trx"
        })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "Pack" (fun _ ->

    File.WriteAllText("version.props",versionPropsTemplate)
    DotNet.pack (fun pack ->
        { pack with
            OutputPath = Some binDir
            Configuration = configuration
        }) solutionFile
)

// --------------------------------------------------------------------------------------
// Generate the documentation

Target.create "GenerateDocs" (fun _ ->
    Shell.cleanDir ".fsdocs"
    DotNet.exec id "fsdocs" "build --clean --properties Configuration=Release --eval" |> ignore
)

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" ignore

"Clean"
  ==> "Build"
  ==> "Test"
  ==> "Pack"
  ==> "All"

"Clean"
  ==> "Build"
  ==> "GenerateDocs"
  ==> "All"

Target.runOrDefault "All"
