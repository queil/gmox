module Queil.Gmox.CliArgs

open Argu
open System.IO

type CliArgs =
| [<AltCommandLine("-p")>]Proto_Paths of string list
| [<AltCommandLine("-r")>]Proto_Root_Dir of string
| [<AltCommandLine("-i")>]Import_Paths of string list
| [<AltCommandLine("-s")>]Stubs_Dir of string
| [<AltCommandLine("-w")>]Work_Dir of string
with
  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Proto_Paths _ -> "Proto paths to compile"
      | Proto_Root_Dir _ -> "Proto Root directory if operating in a monorepo."
      | Import_Paths _ -> "Additional protos import path(s)."
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
      Proto = cmd.GetResult(Proto_Paths) |> Seq.map fullPath |> Seq.toList
      ProtoRoot = cmd.GetResult(Proto_Root_Dir) |> fullPath
      ImportPaths = cmd.GetResult(Import_Paths) |> Seq.map fullPath |> Seq.toList
      StubsDir = cmd.TryGetResult(Stubs_Dir) |> Option.map(fullPath)
      WorkDir = cwd
    }
