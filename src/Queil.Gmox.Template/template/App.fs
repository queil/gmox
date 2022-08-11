module Queil.Gmox.App

open Queil.Gmox.Core
open Queil.Gmox.Server
open Queil.Gmox.Server.Saturn
open Saturn

let app =
  application {
    use_gmox { 
      Services = Grpc.servicesFromAssemblyOf<Grpc.Health.V1.Health> |> Seq.map Emit.makeImpl |> Seq.toList
      StubPreloadDir = Some "stubs"
    }
    configure_gmox
  }

run app
