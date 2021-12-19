module Queil.Gmox.Program

open Queil.Gmox.CliArgs
open Queil.Gmox.App
open Saturn
open System.IO
open System.Reflection
open Fake.Core
open Grpc.Core

type ProjInfo = {
  Name: string
  Path: string
  CompileOutputPath: string
  Options: Options
}
with member x.OutputAssemblyFullPath () = Path.Combine(x.Path, x.CompileOutputPath, $"{x.Name}.dll")

let createTempProj (info: ProjInfo) =
  
  let csProjPath = Path.Combine(info.Path, $"{info.Name}.csproj")

  let importCompiles indent = 
    info.Options.ImportPaths
    |> Seq.map (fun p -> $"""{indent}<Protobuf Include="{p}" GrpcServices="None" ProtoRoot="{info.Options.ProtoRoot}" />""")
    |> String.concat System.Environment.NewLine

  let protos indent = 
    info.Options.Proto
    |> Seq.map (fun p -> $"""{indent}<Protobuf Include="{p}" GrpcServices="Server" ProtoRoot="{info.Options.ProtoRoot}" />""")
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
{"  " |> protos}
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

  printfn "%A" opts

  let projInfo = {
    Name = "grpc"
    Path = tempProjPath
    CompileOutputPath = "out"
    Options = opts
  }

  projInfo |> createTempProj
  |> Proc.run
  |> ignore

  let asm = Assembly.LoadFile(projInfo.OutputAssemblyFullPath ())

  run (app [
    yield! Queil.Gmox.Infra.Grpc.servicesFromAssembly asm
  ])

finally
  dir.Delete(true)
  ()