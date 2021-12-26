module Queil.Gmox.Program

open Fake.Core
open Froto.Parser
open Froto.Parser.Ast
open Queil.Gmox.Core
open Queil.Gmox.CliArgs
open Queil.Gmox.Server
open Saturn
open System.IO
open System.Diagnostics
open System.Reflection

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
  let cmdOpts = System.Environment.GetCommandLineArgs().[1..] |> parseOptions

  match cmdOpts with
  | Version ->
    let ver () = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion
    printfn "%s" (ver ())
  | Serve opts ->

    let asmLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
    let googleIncludesPath = Path.Combine(asmLocation, "include") |> Path.GetFullPath
    let opts =
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

    let parse proto =
      try
        Parse.loadFromFile proto opts.ImportPaths
      with
      | :? FileNotFoundException as fx ->
        printfn "File '%s' was not found. You may need to set --root" fx.Message
        reraise()

    let allFiles =
      opts.Proto
      |> Seq.collect (parse)
      |> Seq.filter (fun (_, tree) -> tree |> List.contains(TPackage "google.protobuf") |> not)
      |> Seq.map fst
      |> Seq.distinct
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

    if not <| opts.ValidateOnly then
      let app = 
        application {
          use_gmox {
            Services = Grpc.servicesFromAssembly asm |> Seq.map Emit.makeImpl |> Seq.toList
            StubPreloadDir = opts.StubsDir
          }
        }
      run app
  with
    | :? Argu.ArguParseException as p ->
      printfn "%s" p.Message
      System.Environment.ExitCode <- 1
    | e -> 
      printfn "%s" (e.ToString())
      System.Environment.ExitCode <- 1
