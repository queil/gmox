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
open Microsoft.AspNetCore.Builder

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

type StubPreloader = unit -> unit

type AppSettings = {
  Services: Type list
  StubPreloadDir: string option
}

let runGmox (app: IHostBuilder) =
  let built = app.Build()
  let lifetime = built.Services.GetRequiredService<IHostApplicationLifetime>()
  let preloader = built.Services.GetRequiredService<StubPreloader>()
  lifetime.ApplicationStarted.Register preloader |> ignore
  built.Run()

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
      svcs.AddSingleton<StubPreloader>(
        Func<IServiceProvider, StubPreloader>(fun f -> fun () ->
          match config.StubPreloadDir with
          | None -> ()
          | Some stubDir -> 
            let serializer = f.GetRequiredService<Json.ISerializer>()
            let store = f.GetRequiredService<StubStore>()
            Directory.EnumerateFiles(stubDir, "*.json")
            |> Seq.iter(fun path ->
               let stubs = serializer.Deserialize<Stub []>(File.ReadAllBytes(path))
               for s in stubs do
                 store.addOrReplace s
            ) 
        )
      )
    )
  }
#if (!standalone)

runGmox (app {
  Services = Infra.Grpc.servicesFromAssemblyOf<Grpc.Health.V1.Health> |> Seq.map Emit.makeImpl |> Seq.toList
  StubPreloadDir = Some "stubs"
})

#endif