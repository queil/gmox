module Queil.Gmox.App

open Giraffe
open Grpc.AspNetCore.Server
open Queil.Gmox.Infra.Json
open Queil.Gmox.Infra.Saturn
open Queil.Gmox.Core
open Queil.Gmox.Core.Types
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Saturn
open System
open System.IO
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open System.Runtime.Serialization
open System.Threading.Tasks

type StubPreloader = IHostedService

let router =
  router {

      get "/" (fun next ctx ->
        task {
          return! ctx.WriteJsonChunkedAsync(ctx.GetService<StubStore>().list())
        })

      post "/test" (fun next ctx -> 
        task {
          let! test = ctx.BindJsonAsync<TestData>()
          return! ctx.WriteJsonChunkedAsync(ctx.GetService<StubStore>().findBestMatchFor test)
        })

      post "/clear" (fun next ctx ->
        task {
          ctx.GetService<StubStore>().clear()
          return! Successful.NO_CONTENT next ctx
        })

      post "/add" (fun next ctx ->
        task {
          let! stub = ctx.BindJsonAsync<Stub>()
          stub |> ctx.GetService<StubStore>().addOrReplace
          return! Successful.NO_CONTENT next ctx
        })
    }

type AppSettings = {
  Services: Type list
  StubPreloadDir: string option
}

let app (config: AppSettings) =

  application {
    listen_local 4770 (fun opts -> opts.Protocols <- HttpProtocols.Http2)
    listen_local 4771 (fun opts -> opts.Protocols <- HttpProtocols.Http1)
    memory_cache
    use_gzip
    use_dynamic_grpc [
      yield! config.Services
    ]
    use_router router
    service_config (fun svcs ->
      let options = SystemTextJson.Serializer.DefaultOptions
      options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
      options.Converters.Add(JsonFSharpConverter(unionTagNamingPolicy=JsonNamingPolicy.CamelCase, unionEncoding= JsonUnionEncoding.FSharpLuLike))
      options.Converters.Add(ProtoMessageConverterFactory())
      svcs.AddSingleton<Json.ISerializer>(SystemTextJson.Serializer(options)) |> ignore
      svcs.AddSingleton<Serialize>(Func<IServiceProvider,Serialize>(fun f -> f.GetRequiredService<Json.ISerializer>().SerializeToString)) |> ignore
      svcs.AddSingleton<GetGrpcMethod>(Func<IServiceProvider, GetGrpcMethod>(fun f ->
          let src = f.GetRequiredService<EndpointDataSource>()
          fun s ->
          let method =
            src.Endpoints
            |> Seq.map (fun x -> x.Metadata.GetMetadata<GrpcMethodMetadata>())
            |> Seq.filter (not << isNull)
            |> Seq.find(fun x -> x.Method.FullName.[1..] = s.Method)
          method.ServiceType.GetMethod(s.Method.Split("/")[1], BindingFlags.Public ||| BindingFlags.Instance)
      )) |> ignore
      svcs.AddSingleton<StubStore>() |> ignore
      svcs.AddSingleton<StubPreloader>(fun f ->
        {
          new StubPreloader with
            override _.StartAsync(_: Threading.CancellationToken): Task = 
              task {
                match config.StubPreloadDir with
                | None -> ()
                | Some stubDir -> 
                  let serializer = f.GetRequiredService<Json.ISerializer>()
                  let store = f.GetRequiredService<StubStore>()
                  do! (
                    Directory.EnumerateFiles(stubDir, "*.json")
                    |> Seq.map(fun path ->
                        task {
                          use stream = File.OpenRead(path)
                          let! stubs = serializer.DeserializeAsync<Stub []>(stream)
                          for s in stubs do
                            store.addOrReplace s
                        }) 
                    |> Seq.map (fun x -> x :> Task)
                    |> Seq.toArray
                    |> Task.WhenAll
                )
              }
            override _.StopAsync(_: Threading.CancellationToken): Task = Task.CompletedTask
        })
    )
  }
