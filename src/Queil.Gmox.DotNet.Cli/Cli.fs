module Queil.Gmox.CliArgs

open Argu
open System.IO

type CliArgs =
| [<AltCommandLine("-p")>]Protos of string list
| [<AltCommandLine("-r")>]Root_Dir of string
| [<AltCommandLine("-i")>]Imports of string list
| [<AltCommandLine("-s")>]Stubs_Dir of string
| [<AltCommandLine("-w")>]Work_Dir of string
with
  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Protos _ -> "Proto paths to compile."
      | Root_Dir _ -> "Proto root directory (e.g. if operating in a monorepo)."
      | Imports _ -> "Additional protos import path(s)."
      | Stubs_Dir _ -> "Directory containing stubs definitions to pre-load."
      | Work_Dir _ -> "If specified overrides the current working directory."

type Options = {
  Proto: string list
  ProtoRoot: string
  ImportPaths: string list
  StubsDir: string option
  WorkDir: string
}

  let parseOptions argv =
    let parser = ArgumentParser.Create<CliArgs>(programName = "gmox")
    let cmd = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
    let cwd = cmd.TryGetResult(Work_Dir) |> Option.defaultValue (Directory.GetCurrentDirectory()) |> Path.TrimEndingDirectorySeparator
    let fullPath (path:string) = Path.Combine(cwd, path |> Path.TrimEndingDirectorySeparator)
    {
      Proto = cmd.GetResult(Protos) |> List.map fullPath
      ProtoRoot = cmd.GetResult(Root_Dir) |> fullPath
      ImportPaths = cmd.TryGetResult(Imports) |> Option.defaultValue [] |> List.map fullPath
      StubsDir = cmd.TryGetResult(Stubs_Dir) |> Option.map fullPath
      WorkDir = cwd
    }
