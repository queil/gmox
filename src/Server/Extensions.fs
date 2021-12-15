module Saturn

open Saturn
open Microsoft.AspNetCore.Builder

open Microsoft.Extensions.DependencyInjection
open System.Reflection
open System
open System.Threading.Tasks

type Store2 =
  val mutable store : string list
  new () = {store = ["test"]}
  // member x.findStub2 (svc:string) (method: string) (rs: Type) (rq: IMessage) =
  //   Activator.CreateInstance(rs) :?> IMessage
  member x.findStub2 (z:obj) =
     Task.FromResult(Grpc.Health.V1.HealthCheckResponse())

module GrpcNonGeneric =
  open Microsoft.AspNetCore.Routing
  let MapGrpcService (serviceType: Type) (builder: IEndpointRouteBuilder)  =
    let r = Activator.CreateInstance(serviceType, Store2())
    
    let typ = typeof<GrpcEndpointRouteBuilderExtensions>
    typ.GetMethod("MapGrpcService", BindingFlags.Static ||| BindingFlags.Public)
       .GetGenericMethodDefinition()
       .MakeGenericMethod(serviceType)
       .Invoke(null, [|builder|]) |> ignore

type Saturn.Application.ApplicationBuilder with

    [<CustomOperation("use_grpc_2")>]
    ///Adds gRPC Code First endpoint. Passed parameter should be any constructor of the gRPC service implementation.
    member __.UseGrpc<'a, 'b when 'a : not struct>(state, serviceType: Type) =
        let configureServices (services: IServiceCollection) =
            services.AddGrpc() |> ignore
            services.AddGrpcReflection() |> ignore
            services.AddSingleton<Store2>() |> ignore
            services
            

        let configureApp (app: IApplicationBuilder) =
            app.UseRouting()

        let configureGrpcEndpoint (app: IApplicationBuilder) =
            app.UseEndpoints (fun endpoints -> 
              
              endpoints |> GrpcNonGeneric.MapGrpcService serviceType
              endpoints.MapGrpcReflectionService() |> ignore
            )
            

        { state with
            AppConfigs = configureApp::configureGrpcEndpoint::state.AppConfigs
            ServicesConfig = configureServices::state.ServicesConfig
        }