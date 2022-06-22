module Queil.Gmox.App

open Microsoft.AspNetCore.Server.Kestrel.Core
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
    
    listen_any 4770 (fun opts -> opts.Protocols <- HttpProtocols.Http2)
    listen_any 4771 (fun opts -> opts.Protocols <- HttpProtocols.Http1)
  }

run app
