module Queil.Gmox.CliArgs

open Argu
open System.IO

type CliArgs =
| [<CliPrefix(CliPrefix.None)>]Serve of ParseResults<Serve>
| [<CliPrefix(CliPrefix.None)>][<SubCommand>]Version
with
  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Serve _ -> "Serve command"
      | Version _ -> "Displays version"

and Serve =
| [<AltCommandLine("-p")>]Proto of string list
| [<AltCommandLine("-r")>]Root of string
| [<AltCommandLine("-i")>]Imports of string list
| [<AltCommandLine("-s")>]Stub_Dir of string
| [<AltCommandLine("-w")>]Work_Dir of string
| [<AltCommandLine("-v")>]Validate_Only
| [<AltCommandLine("-d")>]Debug_Mode
with
  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Proto _ -> "Proto file path(s) to compile."
      | Root _ -> "Proto root directory (e.g. if operating in a monorepo)."
      | Imports _ -> "Additional protos import path(s)."
      | Stub_Dir _ -> "Directory containing stubs definitions to pre-load."
      | Work_Dir _ -> "Overrides the current working directory."
      | Debug_Mode _ -> "Debug mode."
      | Validate_Only _ -> "Makes sure protos compile successfully and terminates."

type Cmd = | Version | Serve of Options
and Options = {
  Proto: string list
  ProtoRoot: string option
  ImportPaths: string list
  StubsDir: string option
  DebugMode: bool
  ValidateOnly: bool
}

  let parseOptions argv =
    let parser = ArgumentParser.Create<CliArgs>(programName = "gmox")
    let cmd = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
    match cmd.GetSubCommand() with
    | CliArgs.Version -> Version
    | CliArgs.Serve serveCmd ->

      let cwd =
        (serveCmd.TryGetResult(Work_Dir))
        |> Option.map(fun p -> if Path.IsPathRooted p then p else Path.Combine(Directory.GetCurrentDirectory(), p))
        |> Option.defaultValue (Directory.GetCurrentDirectory())
        |> Path.GetFullPath

      let fullPath (path:string) = Path.Combine(cwd, path) |> Path.GetFullPath

      {
        Proto = serveCmd.GetResult(Proto) |> List.map fullPath
        ProtoRoot = serveCmd.TryGetResult(Root) |> Option.map fullPath
        ImportPaths = serveCmd.TryGetResult(Imports) |> Option.defaultValue ["."] |> List.map fullPath
        StubsDir = serveCmd.TryGetResult(Stub_Dir) |> Option.map fullPath
        DebugMode = serveCmd.Contains(Debug_Mode)
        ValidateOnly = serveCmd.Contains(Validate_Only)
      } |> Serve

