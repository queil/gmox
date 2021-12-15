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

type Stub = {
  Service: string
  Method: string
  Input: Input
  Output: Output
}
and Input = 
  | Equals of Map<string, obj>
  | Contains of Map<string, obj>
  | Matches of Map<string, obj>
and Output = {
  Data: Map<string, obj>
  Error: string
}

module Route =
  let list = "/"
  let add = "/add"
  let find = "/find"
  let clear = "/clear"


type Mapping = (Input * Output) list
type Methods = ConcurrentDictionary<string,Mapping>
type Services = ConcurrentDictionary<string,Methods>

module ConcurrentDict =
  let addOrReplace  (key: 'a) (d:ConcurrentDictionary<'a,'b>) (addValue:'a -> 'b) (updateValue:'a -> 'b -> 'b) =
    d.AddOrUpdate(key, addValue, updateValue) |> ignore
    d
  
  let make (items: seq<'a * 'b>) = items |> Seq.map KeyValuePair |> ConcurrentDictionary<'a,'b>



module Store =



  let private stubs = Services()

  let addOrReplace (s:Stub) =
    //TODO: stub Output.Data should be validated on store
    let setAdd = stubs |> ConcurrentDict.addOrReplace s.Service
    let setReplace = setAdd <| fun _ -> seq { s.Method, [s.Input, s.Output] } |> ConcurrentDict.make
    (setReplace <| fun _ methods ->
      let setAdd = methods |> ConcurrentDict.addOrReplace s.Method
      let setReplace = setAdd <| fun _ -> [s.Input, s.Output ]
      setReplace <| fun _ _ -> [ s.Input, s.Output ])
    |> ignore
  
  let list () = 
    query {
      for KeyValue(svc, methods) in stubs do
      for KeyValue(method, mappings) in methods do
      for (a, b) in mappings do
      select {
        Service = svc
        Method = method
        Input = a
        Output = b
      }
    }

  let findStub2 (svc:string) (method: string) (rs: Type) (rq: IMessage) =
    Activator.CreateInstance(rs) :?> IMessage

  let findStub<'a, 'b when 'a :> Google.Protobuf.IMessage and 'b :> Google.Protobuf.IMessage and 'b: (new:unit -> 'b) > (serializer:Json.ISerializer) (msg: 'a) (service: string) (method:string) : 'b =

    let message = Google.Protobuf.JsonFormatter.Default.Format(msg)
    let map = serializer.Deserialize<Map<string, obj>>(message)

    match stubs.TryGetValue(service) with
    | true, methods ->
      match methods.TryGetValue(method) with
      | true, mappings ->
        mappings |> Seq.find(fun (i, o) ->
          match i with
          | Equals x -> true
          | Contains x -> true
          | Matches x -> true
        ) |> fun (i,o) -> Google.Protobuf.JsonParser.Default.Parse<'b>(serializer.SerializeToString(o.Data))
      | _ -> new 'b()
    | _ -> new 'b()

  let clear () = stubs.Clear()

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
  let makeImpl (func: unit -> unit) (typ:Type) =

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

      let mi = storeFld.FieldType.GetMethod("findStub2")

      let il = methodBuilder.GetILGenerator()
       
      il.Emit(OpCodes.Ldarg_0)
      il.Emit(OpCodes.Ldfld, storeFld)
      il.Emit(OpCodes.Newobj, typeof<obj>.GetConstructor(Type.EmptyTypes))
      il.Emit(OpCodes.Callvirt, mi)
      il.Emit(OpCodes.Ret)


      typeBuilder.DefineMethodOverride(methodBuilder, method)
    let t = typeBuilder.CreateType()

    let r = t.GetMethods() |> Seq.find (fun m -> m.Name = "Check") |> (fun m -> System.Text.Encoding.ASCII.GetString(m.GetMethodBody().GetILAsByteArray())) 
    t


let stubControl =
  router {
      get Route.clear (fun next ctx -> 
        Store.clear()
        Successful.NO_CONTENT next ctx
      )
      get Route.list (json (Store.list ()))
      post Route.add (bindJson<Stub> (fun s -> 
        s |> Store.addOrReplace
        Successful.NO_CONTENT
      ))
    }


let callMe () =
  printfn "%s" "I was called"
  ()

let app =
  application {
    listen_local 4770 (fun opts -> opts.Protocols <- HttpProtocols.Http2)
    listen_local 4771 (fun opts -> opts.Protocols <- HttpProtocols.Http1)
    memory_cache
    use_gzip
    use_grpc_2 (typeof<Grpc.Health.V1.Health.HealthBase> |> Emit.makeImpl callMe )
    use_router stubControl
    service_config (fun svcs -> 
       let options = SystemTextJson.Serializer.DefaultOptions
       options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
       options.Converters.Add(JsonFSharpConverter(unionTagNamingPolicy=JsonNamingPolicy.CamelCase, unionEncoding= JsonUnionEncoding.FSharpLuLike))
       svcs.AddSingleton<Json.ISerializer>(SystemTextJson.Serializer(options))
    )
  }

run app
