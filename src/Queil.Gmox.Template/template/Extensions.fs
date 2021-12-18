namespace Queil.Gmox

module Extensions =
  open System

  module Json =

    open System.Text.Json.Serialization
    open System.Text.Json
    open Google.Protobuf

    type ProtoMessageConverter<'a when 'a :> IMessage>() =
      inherit JsonConverter<'a>()
      override _.Read(reader: byref<Utf8JsonReader> , typ: Type, options:JsonSerializerOptions) : 'a =
        failwith "not implemented"

      override _.Write(writer: Utf8JsonWriter, msg: 'a, options:JsonSerializerOptions) : unit =
        writer.WriteRawValue(JsonFormatter.Default.Format(msg))

    type ProtoMessageConverterFactory() =
      inherit JsonConverterFactory()
      override _.CanConvert(typ:Type) = typeof<IMessage>.IsAssignableFrom(typ)
      override _.CreateConverter(typeToConvert: Type, options: JsonSerializerOptions): JsonConverter =
        let def = typeof<ProtoMessageConverter<WellKnownTypes.Empty>>.GetGenericTypeDefinition()
        let convType = def.MakeGenericType([|typeToConvert|])
        Activator.CreateInstance(convType) :?> JsonConverter

  module Grpc =
    open System.Reflection
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Routing
    open Grpc.Core

    let MapGrpcService (serviceType: Type) (builder: IEndpointRouteBuilder)  =

      let typ = typeof<GrpcEndpointRouteBuilderExtensions>
      typ.GetMethod("MapGrpcService", BindingFlags.Static ||| BindingFlags.Public)
        .GetGenericMethodDefinition()
        .MakeGenericMethod(serviceType)
        .Invoke(null, [|builder|]) |> ignore

    let servicesFromAssemblyOf<'a> =
      query {
        for t in (typeof<'a>).Assembly.DefinedTypes do
        where (query {
          for a in t.GetCustomAttributes() do
          exists (a.GetType() = typeof<BindServiceMethodAttribute>)
        } && t.IsAbstract)
        select (t.AsType())
      }

  module Saturn =

    open Saturn
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.AspNetCore.Builder
    open Types

    type Application.ApplicationBuilder with

        [<CustomOperation("use_dynamic_grpc")>]
        member __.UseDynamicGrpcServices<'a, 'b when 'a : not struct>(state, types: Type list) =
            let configureServices (services: IServiceCollection) =
                services.AddGrpc() |> ignore
                services.AddGrpcReflection() |> ignore
                services.AddSingleton<StubStore>() |> ignore
                services

            let configureApp (app: IApplicationBuilder) =
                app.UseRouting()

            let configureGrpcEndpoint (app: IApplicationBuilder) =
                app.UseEndpoints (fun endpoints ->
                  for t in types do
                    endpoints |> Grpc.MapGrpcService (t |> Emit.makeImpl)
                  endpoints.MapGrpcReflectionService() |> ignore
                )

            { state with
                AppConfigs = configureApp::configureGrpcEndpoint::state.AppConfigs
                ServicesConfig = configureServices::state.ServicesConfig
            }
