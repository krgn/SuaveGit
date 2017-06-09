// include Fake libs
#r "./packages/FAKE/tools/FakeLib.dll"

open Fake
open System

// Directories
let buildDir  = "./build/"
let deployDir = "./deploy/"


// Filesets
let appReferences  =
    !! "/**/*.csproj"
    ++ "/**/*.fsproj"

// version info
let version = "0.1"  // or retrieve from CI server

let maybeFail = function
  | 0    -> ()
  | code -> failwithf "Command failed with exit code %d" code

// Targets
Target "Clean" (fun _ ->
    CleanDirs [buildDir; deployDir]
)

Target "Build" (fun _ ->
    // compile all projects below src/app/
    MSBuildDebug buildDir "Build" appReferences
    |> Log "AppBuild-Output: "
)

Target "Test" (fun _ ->
    ExecProcess (fun info ->
                    info.FileName <- "Suave.Git.Tests.exe"
                    info.UseShellExecute <- false
                    info.WorkingDirectory <- buildDir)
                TimeSpan.MaxValue
    |> maybeFail
)

Target "Deploy" (fun _ ->
    !! (buildDir + "/**/*.*")
    -- "*.zip"
    |> Zip buildDir (deployDir + "ApplicationName." + version + ".zip")
)

"Build"
  ==> "Test"

// Build order
"Clean"
  ==> "Build"
  ==> "Test"
  ==> "Deploy"

// start build
RunTargetOrDefault "Build"
