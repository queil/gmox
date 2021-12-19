namespace Queil.Gmox.Infra

open System
open System.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open Grpc.Core

module Grpc =

  type IEndpointRouteBuilder with
    member x.MapGrpcService (serviceType: Type) =
      let typ = typeof<GrpcEndpointRouteBuilderExtensions>
      typ.GetMethod("MapGrpcService", BindingFlags.Static ||| BindingFlags.Public)
        .GetGenericMethodDefinition()
        .MakeGenericMethod(serviceType)
        .Invoke(null, [| x |]) |> ignore

  let servicesFromAssembly (asm: Assembly) =
    query {
      for t in asm.DefinedTypes do
      where (query {
        for a in t.GetCustomAttributes() do
        exists (a.GetType() = typeof<BindServiceMethodAttribute>)
      } && t.IsAbstract)
      select (t.AsType())
    }

  let servicesFromAssemblyOf<'a> = typeof<'a>.Assembly |> servicesFromAssembly
