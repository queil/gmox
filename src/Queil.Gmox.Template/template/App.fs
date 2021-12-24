module Queil.Gmox.App

let app =
  application {
    use_gmox { 
      Services = Infra.Grpc.servicesFromAssemblyOf<Grpc.Health.V1.Health> |> Seq.map Emit.makeImpl |> Seq.toList
      StubPreloadDir = Some "stubs"
    }
  }

run app
