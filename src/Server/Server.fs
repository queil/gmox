module Server

open Giraffe
open Saturn

open Grpc.HealthCheck
//open Grpc.Reflection;
open Microsoft.Extensions.DependencyInjection
open System.Collections.Concurrent
open System.Collections.Generic
open System.Text.Json.Serialization
open System.Runtime.Serialization
open Microsoft.AspNetCore.Server.Kestrel.Core
open Grpc.Health.V1
open Grpc.Core
open System.Text.Json

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
    let add = stubs |> ConcurrentDict.addOrReplace s.Service
    let replace = add <| fun _ -> seq { s.Method, [s.Input, s.Output] } |> ConcurrentDict.make
    (replace <| fun _ methods ->
      let add = methods |> ConcurrentDict.addOrReplace s.Method
      let replace = add <| fun _ -> [s.Input, s.Output ]
      replace <| fun _ _ -> [ s.Input, s.Output ])
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

let stubControl =
  router {
      get Route.clear (fun next ctx -> 
        Store.clear()
        Successful.OK "" next ctx
      )
      get Route.list (json (Store.list ()))
      post Route.add (bindJson<Stub> (fun s -> 
        s |> Store.addOrReplace
        Successful.OK ""
      ))
    }

type MyHealth(serializer: Json.ISerializer) =
  inherit Grpc.HealthCheck.HealthServiceImpl()
  override x.Check(request: HealthCheckRequest , context: ServerCallContext) =
    task {
      //TODO: dynamic auto-impl
      let rs = Store.findStub<HealthCheckRequest,HealthCheckResponse> serializer request "HealthServiceImpl" "Check"
      return rs
    }

let app =
  application {
    listen_local 4770 (fun opts -> opts.Protocols <- HttpProtocols.Http2)
    listen_local 4771 (fun opts -> opts.Protocols <- HttpProtocols.Http1)
    memory_cache
    use_gzip
    
    
    use_grpc_2 MyHealth //TODO: Refleciton doesn't seem to work
    use_router stubControl
    service_config (fun svcs -> 
       let options = SystemTextJson.Serializer.DefaultOptions
       options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
       options.Converters.Add(JsonFSharpConverter(unionTagNamingPolicy=JsonNamingPolicy.CamelCase, unionEncoding= JsonUnionEncoding.FSharpLuLike))
       svcs.AddSingleton<Json.ISerializer>(SystemTextJson.Serializer(options))
    )
  }

run app
