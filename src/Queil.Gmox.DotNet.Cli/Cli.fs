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
| [<AltCommandLine("-p")>][<Unique>] Proto of string list
| [<AltCommandLine("-r")>][<Unique>] Root of string
| [<AltCommandLine("-i")>][<Unique>] Imports of string list
| [<AltCommandLine("-s")>][<Unique>] Stub_Dir of string
| [<AltCommandLine("-w")>][<Unique>] Work_Dir of string
| [<AltCommandLine("-v")>][<Unique>] Validate_Only
| [<AltCommandLine("-d")>][<Unique>] Debug_Mode
| [<AltCommandLine("-sp")>][<Unique>] Serve_Port of int
| [<AltCommandLine("-cp")>][<Unique>] Control_Port of int
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
      | Serve_Port _ -> "gRPC port the mocked service(s) are served at. Default: 4770"
      | Control_Port _ -> "HTTP port the control API is served at: Default: 4771"

type Cmd = | Version | Serve of Options
and Options = {
  Proto: string list
  ProtoRoot: string option
  ImportPaths: string list
  StubsDir: string option
  DebugMode: bool
  ValidateOnly: bool
  ServePort: int
  ControlPort: int
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
        ServePort = serveCmd.TryGetResult(Serve_Port) |> Option.defaultValue 4770
        ControlPort = serveCmd.TryGetResult(Control_Port) |> Option.defaultValue 4771
      } |> Serve
