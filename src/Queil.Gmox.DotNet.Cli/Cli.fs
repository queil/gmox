module Queil.Gmox.CliArgs

open Argu
open System.IO

type CliArgs =
| Proto of string list
| ProtoRoot of string
| Import_Path of string list
| Stubs_Dir of string
with
  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Proto _ -> "gRPC service proto path."
      | ProtoRoot _ -> "Root directory if operating in a monorepo."
      | Import_Path _ -> "Additional protos import path(s)."
      | Stubs_Dir _ -> "Directory containing stubs definitions to pre-load."

type Options = {
  Proto: string list
  ProtoRoot: string
  ImportPaths: string list
  StubsDir: string
}

  let parseOptions argv =
    let parser = ArgumentParser.Create<CliArgs>(programName = "gmox")
    let cmd = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
    {
      Proto = cmd.GetResult(Proto) |> Seq.map Path.TrimEndingDirectorySeparator |> Seq.toList
      ProtoRoot = cmd.GetResult(ProtoRoot) |> Path.TrimEndingDirectorySeparator
      ImportPaths = cmd.GetResult(Import_Path) |> Seq.map Path.TrimEndingDirectorySeparator |> Seq.toList
      StubsDir = cmd.GetResult(Stubs_Dir) |> Path.TrimEndingDirectorySeparator
    }