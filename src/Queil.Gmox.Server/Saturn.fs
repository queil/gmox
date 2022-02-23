namespace Queil.Gmox.Server

open System
open System.IO
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks
open Giraffe
open Grpc.AspNetCore.Server
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.Hosting
open Queil.Gmox.Core.Types
open Saturn
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Builder
open Grpc
open Queil.Gmox.Server.Json

module Saturn =
  
  type IServiceCollection with
    member x.AddSingletonFunc<'a when 'a : not struct>(factory: IServiceProvider -> 'a) =
        x.AddSingleton<'a>(Func<IServiceProvider, 'a>(factory)) |> ignore
 
  type AppSettings = {
    Services: Type list
    StubPreloadDir: string option
  }
  
  type Application.ApplicationBuilder with

      [<CustomOperation("use_dynamic_grpc")>]
      member _.UseDynamicGrpc<'a, 'b when 'a : not struct>(state, types: Type list) =
          let configureServices (services: IServiceCollection) =
              services.AddGrpc() |> ignore
              services.AddGrpcReflection() |> ignore
              services

          let configureApp (app: IApplicationBuilder) =
              app.UseRouting()

          let configureGrpcEndpoint (app: IApplicationBuilder) =
              app.UseEndpoints (fun endpoints ->
                for t in types do
                  endpoints.MapGrpcService t
                endpoints.MapGrpcReflectionService() |> ignore
              )

          { state with
              AppConfigs = configureApp::configureGrpcEndpoint::state.AppConfigs
              ServicesConfig = configureServices::state.ServicesConfig
          }

      [<CustomOperation("use_gmox")>]
      member x.UseGmox(state, config: AppSettings) =
          
          let state = x.ListenLocal(state, 4770, fun opts -> opts.Protocols <- HttpProtocols.Http2)
          let state = x.ListenLocal(state, 4771, fun opts -> opts.Protocols <- HttpProtocols.Http1)
          let state = x.MemoryCache(state)
          let state = x.UseGZip(state)
          let state = x.Router(state, Queil.Gmox.Server.Router.router)
          let state = x.UseDynamicGrpc(state, config.Services)
          
          let configureSerializer (services: IServiceCollection) =
            let getOptions (f:IServiceProvider) =
              let options = SystemTextJson.Serializer.DefaultOptions
              options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
              let fsharpOptions = JsonFSharpOptions(unionTagNamingPolicy=JsonNamingPolicy.CamelCase, unionEncoding= JsonUnionEncoding.FSharpLuLike)
              options.Converters.Add(JsonFSharpConverter(fsharpOptions))
              let stubConverter = StubConverter(options, fsharpOptions)
              options.Converters.Add(StubArrayConverter(stubConverter))
              options.Converters.Add(ProtoMessageConverter(f.GetRequiredService<IHttpContextAccessor>()))
              options.Converters.Add(stubConverter)
              options
            services.AddSingletonFunc<JsonSerializerOptions>(getOptions)
            services
          
          let configureServices (services: IServiceCollection) =

            services.AddHttpContextAccessor() |> ignore
            services.AddSingletonFunc<Json.ISerializer>(fun f -> SystemTextJson.Serializer(f.GetRequiredService<JsonSerializerOptions>()))
            services.AddSingletonFunc<ResolveResponseType>(fun f ->
              fun methodName ->
                let resolveGrpcMethod = f.GetRequiredService<GetGrpcMethod>()
                let methodInfo = methodName |> resolveGrpcMethod
                let returnType = methodInfo.ReturnType
                if typedefof<Task<_>> = returnType.GetGenericTypeDefinition()
                then returnType.GenericTypeArguments.[0]
                else failwithf $"Unexpected method return type. Expected: Task<_> but was %s{returnType.FullName}"
            )
            services.AddSingletonFunc<GetGrpcMethod>(fun f ->
                let src = f.GetRequiredService<EndpointDataSource>()
                fun methodName ->
                let method =
                  src.Endpoints
                  |> Seq.map (fun x -> x.Metadata.GetMetadata<GrpcMethodMetadata>())
                  |> Seq.filter (not << isNull)
                  |> Seq.find(fun x -> x.Method.FullName.[1..] = methodName)

                method.ServiceType.GetMethod(methodName.Split("/")[1], BindingFlags.Public ||| BindingFlags.Instance)
            )
            services.AddSingleton<StubStore>() |> ignore
            services.AddSingletonFunc<StubPreloader>(
              fun f -> fun () ->
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
            services
            
          let configureApp (app: IApplicationBuilder) =
              let lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>()
              let preloader = app.ApplicationServices.GetRequiredService<StubPreloader>()
              lifetime.ApplicationStarted.Register preloader |> ignore
              app
          
          {
            state with
              ServicesConfig = configureSerializer::configureServices::state.ServicesConfig
              AppConfigs = configureApp::state.AppConfigs
          }
