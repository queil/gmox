module Server

open Giraffe
open Saturn

open Microsoft.Extensions.DependencyInjection
open System.Collections.Concurrent
open System.Collections.Generic
open System.Text.Json.Serialization
open System.Runtime.Serialization
open Microsoft.AspNetCore.Server.Kestrel.Core
open System.Text.Json
open Google.Protobuf
open System
open Microsoft.Extensions.Options

module Route =
  let list = "/"
  let add = "/add"
  let find = "/find"
  let clear = "/clear"








module Emit =
  open System
  open System.Reflection
  open System.Reflection.Emit
  open Google.Protobuf
  open FSharp.Quotations
  open FSharp.Linq.RuntimeHelpers
  open Grpc.Health.V1
  open System.Threading.Tasks
  open System.Linq.Expressions
  let makeImpl (typ:Type) =

    if not <| typ.IsAbstract then
      failwithf "Expecting a service base type which should be abstract. Given type: '%s'" (typ.AssemblyQualifiedName)

    let uid () = Guid.NewGuid().ToString("N").Remove(10)
    let asmName = AssemblyName($"grpc-dynamic-{uid ()}")
    let asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run)
    let moduleBuilder = asmBuilder.DefineDynamicModule("grpc-impl")
    let typeBuilder = moduleBuilder.DefineType($"{typ.Name}-{uid ()}")
    typeBuilder.SetParent(typ)

    let storeFld = typeBuilder.DefineField("_store", typeof<Store2>, FieldAttributes.Public)

    let ctorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [|typeof<Store2>|])
    let ctorIl = ctorBuilder.GetILGenerator()
    ctorIl.Emit(OpCodes.Ldarg_0)
    ctorIl.Emit(OpCodes.Ldarg_1)
    ctorIl.Emit(OpCodes.Stfld, storeFld)
    ctorIl.Emit(OpCodes.Ret)

    for method in typ.GetMethods(BindingFlags.Public ||| BindingFlags.DeclaredOnly ||| BindingFlags.Instance) do
      let methodBuilder =
        typeBuilder.DefineMethod(
          $"{typ.Name}.{method.Name}",
          MethodAttributes.Public ||| MethodAttributes.Virtual,
          CallingConventions.HasThis,
          method.ReturnType,
          method.GetParameters() |> Array.map (fun p -> p.ParameterType))

      let mi = storeFld.FieldType.GetMethod("findStub3")

      let il = methodBuilder.GetILGenerator()
       
      il.Emit(OpCodes.Ldarg_0)         // push Grpc svc on to the stack (this)
      il.Emit(OpCodes.Ldfld, storeFld) // push Store instance reference on to the stack
      il.Emit(OpCodes.Ldarg_1)         // grpc method request reference
      il.Emit(OpCodes.Ldarg_2)         // grpc method server call context
      
      let getCurrentMethod = typeof<MethodBase>.GetMethod(nameof(MethodBase.GetCurrentMethod), BindingFlags.Public ||| BindingFlags.Static)
      il.Emit(OpCodes.Call, getCurrentMethod)

      il.Emit(OpCodes.Callvirt, mi)    // now call findStubs
      il.Emit(OpCodes.Ret)             // return the retrieved response


      typeBuilder.DefineMethodOverride(methodBuilder, method)
    let t = typeBuilder.CreateType()

    let r = t.GetMethods() |> Seq.find (fun m -> m.Name = "Check") |> (fun m -> System.Text.Encoding.ASCII.GetString(m.GetMethodBody().GetILAsByteArray())) 
    t


let stubControl =
  router {
      get Route.clear (fun next ctx ->
         task {
           ctx.GetService<Store2>().clear()
           return! Successful.NO_CONTENT next ctx
         })
      get Route.list (fun next ctx -> 
          task {
          return! ctx.WriteJsonChunkedAsync(ctx.GetService<Store2>().list())
        }
      )
      post Route.add (fun next ctx ->
         task {
          let! stub = ctx.BindJsonAsync<Stub>()
          stub |> ctx.GetService<Store2>().addOrReplace
          return! Successful.NO_CONTENT next ctx
         }
      )
    }


let app =
  application {
    listen_local 4770 (fun opts -> opts.Protocols <- HttpProtocols.Http2)
    listen_local 4771 (fun opts -> opts.Protocols <- HttpProtocols.Http1)
    memory_cache
    use_gzip
    use_grpc_2 (typeof<Grpc.Health.V1.Health.HealthBase> |> Emit.makeImpl)
    use_router stubControl
    service_config (fun svcs ->
       let options = SystemTextJson.Serializer.DefaultOptions
       options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
       options.Converters.Add(JsonFSharpConverter(unionTagNamingPolicy=JsonNamingPolicy.CamelCase, unionEncoding= JsonUnionEncoding.FSharpLuLike))
       //options.Converters.Add(Json2.StubConverter<Grpc.Health.V1.Health.HealthBase>())
       options.Converters.Add(Json2.ProtoMessageConverterFactory())
       
       svcs.AddSingleton<Json.ISerializer>(SystemTextJson.Serializer(options))
    )
  }

run app
