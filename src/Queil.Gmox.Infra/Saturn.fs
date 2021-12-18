namespace Queil.Gmox.Infra

open System
open Saturn
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Builder
open Grpc

module Saturn =

  type Application.ApplicationBuilder with

      [<CustomOperation("use_dynamic_grpc")>]
      member __.UseDynamicGrpc<'a, 'b when 'a : not struct>(state, types: Type list) =
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
