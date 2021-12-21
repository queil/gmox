module Queil.Gmox.Program

open Queil.Gmox.App
open Queil.Gmox.CliArgs
open Queil.Gmox.Core
open Queil.Gmox.Infra
open System.IO
open System.Reflection
open Fake.Core

type ProjInfo = {
  Name: string
  Path: string
  CompileOutputPath: string
  AllProtoFiles: string list
  Options: Options
}
with member x.OutputAssemblyFullPath () = Path.Combine(x.Path, x.CompileOutputPath, $"{x.Name}.dll")

let createTempProj (info: ProjInfo) =

  let csProjPath = Path.Combine(info.Path, $"{info.Name}.csproj")
  let protosRoot = 
    match info.Options.ProtoRoot with
    | Some path -> $"ProtoRoot=\"{path}\""
    | None -> ""

  let importCompiles indent =
    info.AllProtoFiles
    |> Seq.map (fun p -> $"""{indent}<Protobuf Include="{p}" GrpcServices="Server" {protosRoot} />""")
    |> String.concat System.Environment.NewLine

  let template = $"""<Project Sdk="Microsoft.NET.Sdk">

<PropertyGroup>
  <TargetFramework>netstandard2.1</TargetFramework>
  <OutputPath>{info.CompileOutputPath}</OutputPath>
  <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Grpc.Core.Api" Version="[2.42.0, 3.0.0)" />
  <PackageReference Include="Grpc.Tools" Version="[2.42.0, 3.0.0)" />
  <PackageReference Include="Google.Protobuf" Version="[3.19.1, 4.0.0)" />
</ItemGroup>

<ItemGroup>
{"  " |> importCompiles}
</ItemGroup>

</Project>"""

  File.WriteAllText(Path.Combine(info.Path, "Program.cs"), "")
  File.WriteAllText(csProjPath, template)

  RawCommand("dotnet", Arguments.OfArgs ["build"])
  |> CreateProcess.fromCommand
  |> CreateProcess.withWorkingDirectory info.Path
  |> CreateProcess.ensureExitCode


let tempProjPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
let dir = Directory.CreateDirectory(tempProjPath)

try
  let opts =
    System.Environment.GetCommandLineArgs().[1..]
    |> parseOptions
    |> fun opts ->
      let asmLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
      let googleIncludesPath = Path.Combine(asmLocation, "include")
      {
        opts with
          ImportPaths = [
            yield! opts.ImportPaths
            yield googleIncludesPath
            yield! opts.Proto |> List.map (Path.GetDirectoryName)
            match opts.ProtoRoot with
            | Some path -> yield path
            | None -> ()
          ]
      }

  let allFiles = 
    opts.Proto
    |> Seq.collect (fun proto -> Froto.Parser.Parse.loadFromFile proto opts.ImportPaths)
    |> Seq.map fst
    |> Seq.distinct
    |> Seq.filter (fun p -> not <| p.Contains("/include/google/protobuf/"))
    |> Seq.toList

  let projInfo = {
    Name = "proto-gen"
    Path = tempProjPath
    CompileOutputPath = "out"
    AllProtoFiles = allFiles
    Options = opts
  }

  if opts.DebugMode then printfn "%A" projInfo

  projInfo |> createTempProj
  |> Proc.run
  |> ignore

  let asm = Assembly.LoadFile(projInfo.OutputAssemblyFullPath ())

  runGmox (app {
    Services = Grpc.servicesFromAssembly asm |> Seq.map Emit.makeImpl |> Seq.toList
    StubPreloadDir = opts.StubsDir
  })

  if not <| opts.DebugMode then dir.Delete(true)
with
  | :? Argu.ArguParseException as p ->
    printfn "%s" p.Message
  | e -> printfn "%s" (e.ToString())
