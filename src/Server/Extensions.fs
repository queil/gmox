module Saturn

open Saturn
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open ProtoBuf.Grpc.Server
open ProtoBuf.Grpc.Configuration
open System.Reflection
open Grpc.Reflection
open System

module Binder2 =
  type ServiceBinderFrom(services:IServiceCollection) =
    inherit ServiceBinder()
    override _.GetMetadata(method: MethodInfo, contractType: Type , serviceType: Type) =
      let resolvedServiceType =
        match serviceType with
        | t when t.IsInterface -> 
            query {
              for s in services do
              where (s.ServiceType = serviceType)
              select s.ImplementationType
            } |> Seq.tryExactlyOne
              |> Option.defaultValue t
        | t -> t
      base.GetMetadata(method, contractType, resolvedServiceType);


type Saturn.Application.ApplicationBuilder with

    [<CustomOperation("use_grpc_2")>]
    ///Adds gRPC Code First endpoint. Passed parameter should be any constructor of the gRPC service implementation.
    member __.UseGrpc<'a, 'b when 'a : not struct>(state, cons: 'b -> 'a) =
        let configureServices (services: IServiceCollection) =
            //services.AddCodeFirstGrpc()
            services.AddGrpc() |> ignore
            services.AddGrpcReflection() |> ignore
            services
            //services.AddSingleton(BinderConfiguration.Create([|ProtoBufMarshallerFactory.Default |], Binder2.ServiceBinderFrom(services))) |> ignore
            //services.AddCodeFirstGrpcReflection()
            

        let configureApp (app: IApplicationBuilder) =
            app.UseRouting()

        let configureGrpcEndpoint (app: IApplicationBuilder) =
            app.UseEndpoints (fun endpoints -> 
              
              endpoints.MapGrpcService<'a>() |> ignore
              endpoints.MapGrpcReflectionService() |> ignore
              //endpoints.MapCodeFirstGrpcReflectionService() |> ignore
              
            )
            

        { state with
            AppConfigs = configureApp::configureGrpcEndpoint::state.AppConfigs
            ServicesConfig = configureServices::state.ServicesConfig
        }