module Queil.Gmox.CliArgs

open Argu
open System.IO

type CliArgs =
| [<AltCommandLine("-p")>]Proto of string list
| [<AltCommandLine("-r")>]Root of string
| [<AltCommandLine("-i")>]Imports of string list
| [<AltCommandLine("-s")>]Stub_Dir of string
| [<AltCommandLine("-w")>]Work_Dir of string
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

type Options = {
  Proto: string list
  ProtoRoot: string option
  ImportPaths: string list
  StubsDir: string option
  DebugMode: bool
}

  let parseOptions argv =
    let parser = ArgumentParser.Create<CliArgs>(programName = "gmox")
    let cmd = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
    let trimEndSlash (p:string) = Path.TrimEndingDirectorySeparator p

    let cwd =
      (cmd.TryGetResult(Work_Dir))
      |> Option.map(fun p -> if Path.IsPathRooted p then p else Path.Combine(Directory.GetCurrentDirectory(), p))
      |> Option.defaultValue (Directory.GetCurrentDirectory())
      |> trimEndSlash

    let fullPath (path:string) = Path.Combine(cwd, trimEndSlash path)

    {
      Proto = cmd.GetResult(Proto) |> List.map fullPath
      ProtoRoot = cmd.TryGetResult(Root) |> Option.map fullPath
      ImportPaths = cmd.TryGetResult(Imports) |> Option.defaultValue ["."] //|> List.map fullPath
      StubsDir = cmd.TryGetResult(Stub_Dir) |> Option.map fullPath
      DebugMode = cmd.Contains(Debug_Mode)
    }
