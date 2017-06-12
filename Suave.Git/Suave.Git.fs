module Suave.Git

// * Imports

open System
open System.IO
open System.Text
open System.Diagnostics

open Suave
open Suave.Http
open Suave.Files
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Writers
open Suave.Web

// * Service

type private Service =
  | UploadPack
  | ReceivePack

  // ** Parse

  static member Parse = function
    | "upload-pack"
    | "git-upload-pack"  -> UploadPack
    | "receive-pack"
    | "git-receive-pack" -> ReceivePack
    | other -> failwithf "unrecognized service: %s" other

  // ** ToString

  override self.ToString() =
    match self with
    | UploadPack  -> "upload-pack"
    | ReceivePack -> "receive-pack"

// * (^^)

let rec private (^^) (lst: (string * string option) list) name =
  match lst with
  | [] -> None
  | (hdk, v) :: _ when hdk = name -> v
  | _ :: rest -> rest ^^ name

// * join

let private join sep (strings: seq<string>)=
  String.Join(sep, strings)

// * getAdvertisement

let private getAdvertisement path (srvc: Service) =
  use proc = new Process()
  proc.StartInfo.FileName <- "git"
  proc.StartInfo.Arguments <- (string srvc) + " --stateless-rpc --advertise-refs " + path
  proc.StartInfo.CreateNoWindow <- true
  proc.StartInfo.UseShellExecute <- false
  proc.StartInfo.RedirectStandardOutput <- true
  proc.StartInfo.RedirectStandardError <- true

  if proc.Start() then
    let lines = ResizeArray()
    while not proc.StandardOutput.EndOfStream do
      proc.StandardOutput.ReadLine()
      |> lines.Add
    proc.WaitForExit()
    lines.ToArray()
    |> join "\n"
  else
    proc.WaitForExit()
    proc.StandardError.ReadToEnd()
    |> failwithf "Error: %s"

// * postData

let private postData path srvc (data: byte array) =
  use proc = new Process()
  proc.StartInfo.FileName <- "git"
  proc.StartInfo.Arguments <- (string srvc) + " --stateless-rpc " + path
  proc.StartInfo.CreateNoWindow <- true
  proc.StartInfo.UseShellExecute <- false
  proc.StartInfo.RedirectStandardInput <- true
  proc.StartInfo.RedirectStandardOutput <- true
  proc.StartInfo.RedirectStandardError <- true

  if proc.Start() then
    // We want to write the bytes unparsed to the processes stdin, so we need to wire up a
    // BinaryWriter to the underlying Stream and write to that.
    use bw = new BinaryWriter(proc.StandardInput.BaseStream)

    bw.Write data
    bw.Flush()
    bw.Close()

    proc.WaitForExit()
    if proc.ExitCode = 0 then
      let mutable bytes = ResizeArray()
      use reader = new BinaryReader(proc.StandardOutput.BaseStream)
      let mutable run = true
      while run do
        try
          reader.ReadByte() |> bytes.Add
        with
          | :? EndOfStreamException -> run <- false
      bytes.ToArray()
    else
      let lines = ResizeArray()
      while not proc.StandardError.EndOfStream do
        proc.StandardError.ReadLine()
        |> lines.Add
      lines.ToArray()
      |> join "\n"
      |> failwithf "ERROR: %s"
  else
    proc.WaitForExit()
    proc.StandardError.ReadToEnd()
    |> failwithf "Error: %s"

// * makePacketHeader

let private makePacketHeader (cmd: string) =
  let hexchars = "0123456789abcdef"
  let length = cmd.Length + 4      // 4 hex digits

  let toHex (idx: int) = hexchars.Chars(idx &&& 15)

  let builder = StringBuilder()
  [| toHex (length >>> 12)
     toHex (length >>> 8)
     toHex (length >>> 4)
     toHex  length |]
  |> Array.iter (builder.Append >> ignore)
  string builder

// * makePacket

let private makePacket (cmd: Service) =
  let packet = String.Format("# service=git-{0}\n", string cmd)
  let header = makePacketHeader packet
  String.Format("{0}{1}0000", header, packet)

// * makeContentType

let private makeContentType (noun: string) (cmd: Service) =
  String.Format("application/x-git-{0}-{1}", string cmd, noun)

// * makeHttpHeaders

let private makeHttpHeaders (contentType: string) =
  setHeader "Cache-Control" "no-cache, no-store, max-age=0, must-revalidate"
  >=> setHeader "If-You-Need-Help" "k@ioct.it"
  >=> setHeader "Pragma" "no-cache"
  >=> setHeader "Expires" "Fri, 01 Jan 1980 00:00:00 GMT"
  >=> setHeader "Content-Type" contentType

// * parseService

let private parseService q = q ^^ "service" |> Option.map Service.Parse

// * getData

let private getData path (cmd: Service) =
  let result = getAdvertisement path cmd

  let headers =
    cmd
    |> makeContentType "advertisement"
    |> makeHttpHeaders

  let body = StringBuilder()

  makePacket cmd |> body.Append |> ignore
  result |> body.Append |> ignore

  headers >=> OK (string body)

// * handleGetRequest

let private handleGetRequest path (req: HttpRequest) =
  match req.query |> parseService with
  | Some cmd -> getData path cmd
  | None -> RequestErrors.FORBIDDEN "missing or malformed git service request"

// * handlePostRequest

let private handlePostRequest path (cmd: Service) (req: HttpRequest) =
  let result = postData path cmd req.rawForm
  let headers =
    cmd
    |> makeContentType "result"
    |> makeHttpHeaders
  headers >=> ok result

// * uploadPack

let private uploadPack path  =
  UploadPack
  |> handlePostRequest path
  |> request

// * receivePack

let private receivePack path  =
  ReceivePack
  |> handlePostRequest path
  |> request

// * get

let private get path = path |> handleGetRequest |> request

// * route

let private route (name: string option) path =
  match name with
  | Some name when name.StartsWith("/") -> String.Format("{0}{1}", name, path)
  | Some name -> String.Format("/{0}{1}", name, path)
  | None      -> path

// * gitServer

/// <summary> Generate a WebPart to serve git repositories.  </summary> <param name="basepath">
/// Optionally specify a namespace to serve the git repository under . For instance, given
/// <c>Some("myproject")</c> and served from localhost on port 5000, the resulting routes would be
/// <code>
///   GET  http//localhost:5000/myproject/info/refs
///   POST http//localhost:5000/myproject/git-upload-pack
///   POST http//localhost:5000/myproject/git-receive-pack
///</code>
///</param>
/// <param name="gitfolder">
///   Absolute path to the git repository, bare or non-bare, to be served.
/// </param>
/// <returns>
///  WebPart<HttpContext>
///</returns>
let gitServer (basepath: string option) (gitfolder: string) =
  choose [
      Filters.path (route basepath "/info/refs") >=> Filters.GET >=> get gitfolder
      Filters.POST >=>
        (choose [
          Filters.path (route basepath "/git-receive-pack") >=> receivePack gitfolder
          Filters.path (route basepath "/git-upload-pack" ) >=> uploadPack  gitfolder ])
    ]
