module Queil.Gmox.App

open Grpc.Core
open Giraffe
open Saturn
open Microsoft.Extensions.DependencyInjection
open System.Text.Json.Serialization
open System.Runtime.Serialization
open Microsoft.AspNetCore.Server.Kestrel.Core
open System.Text.Json
open Queil.Gmox.Types
open Queil.Gmox.Extensions.Saturn
open Queil.Gmox.Extensions.Json
open System
open System.Reflection

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

let services =
  query {
    for t in (typeof<Grpc.Health.V1.Health>).Assembly.DefinedTypes do
    where (query {
      for a in t.GetCustomAttributes() do
      exists (a.GetType() = typeof<BindServiceMethodAttribute>)
    } && t.IsAbstract)
    select (t.AsType())
  }

let app =
  application {
    listen_local 4770 (fun opts -> opts.Protocols <- HttpProtocols.Http2)
    listen_local 4771 (fun opts -> opts.Protocols <- HttpProtocols.Http1)
    memory_cache
    use_gzip
    use_dynamic_grpc_services [yield! services]
    use_router router
    service_config (fun svcs ->
      let options = SystemTextJson.Serializer.DefaultOptions
      options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
      options.Converters.Add(JsonFSharpConverter(unionTagNamingPolicy=JsonNamingPolicy.CamelCase, unionEncoding= JsonUnionEncoding.FSharpLuLike))
      options.Converters.Add(ProtoMessageConverterFactory())
      svcs.AddSingleton<Json.ISerializer>(SystemTextJson.Serializer(options)) |> ignore
      svcs.AddSingleton<Serialize>(Func<IServiceProvider,Serialize>(fun f -> f.GetRequiredService<Json.ISerializer>().SerializeToString))
    )
  }

run app
