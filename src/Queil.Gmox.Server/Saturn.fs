namespace Queil.Gmox.Server

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open System.Threading.Tasks
open Giraffe
open Google.Protobuf.Reflection
open Grpc.AspNetCore.Server
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
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

      [<CustomOperation "listen_any">]
      member __.ListenAny (state:ApplicationState, portNumber, listenOptions : ListenOptions -> unit) =
        let config (webHostBuilder:IWebHostBuilder) =
            webHostBuilder
               .ConfigureKestrel(fun options -> options.ListenAnyIP(portNumber, Action<ListenOptions> listenOptions))

        {state with WebHostConfigs = config::state.WebHostConfigs}

      [<CustomOperation("use_gmox")>]
      member x.UseGmox(state, config: AppSettings) =

          let state = x.MemoryCache(state)
          let state = x.UseGZip(state)
          let state = x.Router(state, Queil.Gmox.Server.Router.router)
          let state = x.UseDynamicGrpc(state, config.Services)
          let state = x.ErrorHandler(state, fun exn logger ->
              logger.Log(LogLevel.Error, exn, "Error occurred")
              clearResponse >=> setStatusCode 500 >=> json {| error = exn.Message |}
            )

          let configureSerializer (services: IServiceCollection) =
            let getOptions (f:IServiceProvider) =
              let options = SystemTextJson.Serializer.DefaultOptions
              options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
              let fsharpOptions = JsonFSharpOptions(unionTagNamingPolicy=JsonNamingPolicy.CamelCase, unionEncoding= JsonUnionEncoding.FSharpLuLike)
              options.Converters.Add(JsonFSharpConverter(fsharpOptions))
              options.Converters.Add(ProtoMessageConverter())
              options.Converters.Add(StubConverter(options, fsharpOptions))
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
                let reflectionSvc = "grpc.reflection.v1alpha.ServerReflection/ServerReflectionInfo"
                fun methodToFind ->
                  let allMethods =
                    src.Endpoints
                    |> Seq.map (fun x -> x.Metadata.GetMetadata<GrpcMethodMetadata>())
                    |> Seq.filter (not << isNull)
                    |> Seq.filter(fun m -> m.Method.FullName <> reflectionSvc)
                  try
                    let method =
                      allMethods
                      |> Seq.find(fun x -> x.Method.FullName.[1..].Equals(methodToFind, StringComparison.OrdinalIgnoreCase))
                    method.ServiceType.GetMethod(methodToFind.Split("/")[1], BindingFlags.Public ||| BindingFlags.Instance)
                  with
                  | :? KeyNotFoundException as kx ->
                      let availableMethods = allMethods |> Seq.map (fun m -> m.Method.FullName.[1..]) |> String.concat ", "
                      raise (KeyNotFoundException(
                        $"Method %s{methodToFind} is not available for stubbing. Available methods: %s{ availableMethods }", kx))
            )

            services.AddSingletonFunc<GetDescriptorByTypeName>(fun f ->
              let descriptors = ConcurrentDictionary<string, MessageDescriptor>()
              fun svcMethodName ->
                descriptors.GetOrAdd(svcMethodName,
                   fun m ->
                     let typ = m |> f.GetService<ResolveResponseType>()
                     (Activator.CreateInstance(typ) :?> Google.Protobuf.IMessage).Descriptor
                  )
            )

            services.AddSingleton<StubStore>() |> ignore
            services.AddSingletonFunc<AddOrReplaceStub>(fun f->
              fun node ->
                 let options = f.GetRequiredService<JsonSerializerOptions>()
                 let method = JsonSerializer.Deserialize<StubMethod>(node, options)
                 let getDescriptor = f.GetRequiredService<GetDescriptorByTypeName>()
                 let descriptor = getDescriptor method.Method
                 let opts = JsonSerializerOptions(options)
                 opts.Converters.Add(ResponseDescriptorHolder(descriptor))
                 let stub = JsonSerializer.Deserialize<Stub>(node, opts)
                 f.GetRequiredService<StubStore>().addOrReplace stub
            )

            services.AddSingletonFunc<StubPreloader>(
              fun f -> fun () ->
                match config.StubPreloadDir with
                | None -> ()
                | Some stubDir ->
                  Directory.EnumerateFiles(stubDir, "*.json")
                  |> Seq.iter(fun path ->
                     let doc = JsonNode.Parse(File.ReadAllBytes(path))
                     for node in doc.AsArray() do
                       node |> f.GetRequiredService<AddOrReplaceStub>()
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
