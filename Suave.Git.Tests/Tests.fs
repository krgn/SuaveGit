module Tests

open Expecto
open Suave
open Suave.Git
open Suave.Http
open Suave.Files
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Writers
open Suave.Web

open System
open System.IO
open System.Net
open System.Threading
open System.Diagnostics

[<Literal>]
let PORT = 5000us

type IRepo =
  inherit IDisposable
  abstract Path: string
  abstract Url: string
  abstract Commit: string -> unit
  abstract Commits: string array
  abstract StageAll: unit -> unit
  abstract AddRemote: name:string -> url:string -> unit
  abstract Push: remote:string -> branch:string -> unit
  abstract Pull: remote:string -> branch:string -> unit
  abstract WriteFile: filename:string -> contents:string -> unit

let join (sep:string) (lines: string[]) =
  String.Join(sep, lines)

let runGit subcmd options workDir =
  use proc = new Process()
  proc.StartInfo.FileName <- "git"
  proc.StartInfo.Arguments <- subcmd + " " + options
  proc.StartInfo.WorkingDirectory <- workDir
  proc.StartInfo.CreateNoWindow <- true
  proc.StartInfo.UseShellExecute <- false
  proc.StartInfo.RedirectStandardOutput <- true
  proc.StartInfo.RedirectStandardError <- true

  if proc.Start() then
    let output = ResizeArray()

    while not proc.StandardOutput.EndOfStream do
      proc.StandardOutput.ReadLine()
      |> output.Add

    while not proc.StandardError.EndOfStream do
      proc.StandardError.ReadLine()
      |> output.Add

    proc.WaitForExit()

    if proc.ExitCode = 0 then
      output.ToArray()
    else
      output.ToArray()
      |> join "\n"
      |> failwithf "ERROR: %s"
  else failwith "Could not start git"

let deleteRepo basedir name =
  try
    Directory.Delete(Path.Combine(basedir,name), true)
  with
    | :? DirectoryNotFoundException ->
      printfn "%s/%s was not found" basedir name

let setConfig basedir name =
  let options = "--local receive.denyCurrentBranch updateInstead"
  Path.Combine(basedir, name)
  |> runGit "config" options
  |> join "\n"
  |> Console.WriteLine

let openRepo basedir name =
  let path = Path.Combine(basedir,name)
  setConfig basedir name
  { new IRepo with
      member self.Path
        with get () = path

      member self.Url
        with get () = sprintf "http://localhost:%d/%s" PORT name

      member self.Commit(msg: string) =
        Path.Combine(basedir, name)
        |> runGit "commit" (sprintf " -am %A " msg)
        |> join "\n"
        |> Console.WriteLine

      member self.StageAll() =
        Path.Combine(basedir, name)
        |> runGit "add" "."
        |> join "\n"
        |> Console.WriteLine

      member self.Commits
        with get () =
          Path.Combine(basedir,name)
          |> runGit "log" "--oneline"

      member self.AddRemote (remote: string) (url: string) =
        let options = sprintf "add %s %s" remote url
        Path.Combine(basedir,name)
        |> runGit "remote" options
        |> join "\n"
        |> Console.WriteLine

      member self.Push (remote: string) (branch: string) =
        Path.Combine(basedir,name)
        |> runGit "push" (remote + " " + branch)
        |> join "\n"
        |> Console.WriteLine

      member self.Pull (remote: string) (branch: string) =
        Path.Combine(basedir,name)
        |> runGit "pull" (remote + " " + branch)
        |> join "\n"
        |> Console.WriteLine

      member self.WriteFile (fn: string) (content: string) =
        File.WriteAllText(Path.Combine(basedir,name,fn), content)

      member self.Dispose() =
        deleteRepo basedir name }

let createRepo basedir name =
  runGit "init" name basedir |> ignore
  openRepo basedir name

let cloneRepo basedir name url =
  runGit "clone" (sprintf "%s %s" url name) basedir |> ignore
  openRepo basedir name

let createServer basedir name =
  let cts = new CancellationTokenSource()

  let config =
    { defaultConfig with
        cancellationToken = cts.Token
        bindings = [ HttpBinding.create HTTP IPAddress.Loopback PORT ] }

  let routes = gitServer (Some name) (Path.Combine(basedir,name))

  startWebServerAsync config routes
  |> (fun (_, server) -> Async.Start(server, cts.Token))

  // on very slow machines (AppVeyor) it sometimes takes
  // around 100ms, so we wait a little to make sure the server is up
  Thread.Sleep(150)

  { new IDisposable with
      member self.Dispose() =
        try
          cts.Cancel()
          cts.Dispose()
        with | _ -> () }

let cloneWorks =
  testCase "git clone works" <| fun _ ->
    let basedir = Path.GetTempPath()
    let name = Path.GetRandomFileName()

    use original = createRepo basedir name
    use server = createServer basedir name

    Thread.Sleep(1000)

    original.WriteFile (Path.GetRandomFileName()) "hello"
    original.StageAll()
    original.Commit "added hello"

    original.WriteFile (Path.GetRandomFileName()) "bye"
    original.StageAll()
    original.Commit "added bye"

    let target = Path.GetRandomFileName()
    use cloned = cloneRepo basedir target original.Url

    let commits1 = original.Commits
    let commits2 = cloned.Commits

    Expect.equal commits1 commits2 "Commits should be equal"

let pushWorks =
  testCase "git push works" <| fun _ ->
    let basedir = Path.GetTempPath()
    let name = Path.GetRandomFileName()

    use remote = createRepo basedir name
    use server = createServer basedir name

    use local = createRepo basedir (Path.GetRandomFileName())

    local.WriteFile (Path.GetRandomFileName()) "hello"
    local.StageAll()
    local.Commit "added hello"

    local.WriteFile (Path.GetRandomFileName()) "bye"
    local.StageAll()
    local.Commit "added bye"

    local.AddRemote "origin" remote.Url
    local.Push "origin" "master"

    let commits1 = remote.Commits
    let commits2 = local.Commits

    Expect.equal commits1 commits2 "Commits should be equal"

let pullWorks =
  testCase "git pull works" <| fun _ ->
    let basedir = Path.GetTempPath()
    let name = Path.GetRandomFileName()

    use remote = createRepo basedir name
    use server = createServer basedir name

    use local = createRepo basedir (Path.GetRandomFileName())

    remote.WriteFile (Path.GetRandomFileName()) "hello"
    remote.StageAll()
    remote.Commit "added hello"

    remote.WriteFile (Path.GetRandomFileName()) "bye"
    remote.StageAll()
    remote.Commit "added bye"

    local.AddRemote "origin" remote.Url
    local.Pull "origin" "master"

    let commits1 = remote.Commits
    let commits2 = local.Commits

    Expect.equal commits1 commits2 "Commits should be equal"

[<Tests>]
let tests =
  testList "samples" [
    pushWorks
    cloneWorks
    pullWorks
  ] |> testSequenced
