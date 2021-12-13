module Server

open Giraffe
open Saturn

open Microsoft.Extensions.DependencyInjection
open System.Collections.Concurrent
open System.Collections.Generic
open System.Text.Json.Serialization

type Stub = {
  Service: string
  Method: string
  Input: Input
  Output: Output
}
and Input = {
  Equals: Map<string, obj> option
  Contains: Map<string, obj> option
  Matches: Map<string, obj> option
}
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

let services = Services()

let webApp =
  router {
    get Route.clear (fun next ctx -> 
      task {
        services.Clear()
        return! Successful.ACCEPTED "OK" next ctx
    })
    get Route.list (fun next ctx ->
      task {
        return! ctx.WriteJsonChunkedAsync services
    })
    post Route.add (fun next ctx -> 
      task {
        let! stub = ctx.BindJsonAsync<Stub>()
        
        let add =
          services |> ConcurrentDict.addOrReplace stub.Service
        
        let replace =
          add <| fun _ -> seq { stub.Method, [stub.Input, stub.Output] } |> ConcurrentDict.make

        let z =
            replace <| fun _ methods ->
            let add = methods |> ConcurrentDict.addOrReplace stub.Method
            let replace = add <| fun _ -> [stub.Input, stub.Output ]
            replace <| fun _ _ -> [ stub.Input, stub.Output ]
        z |> ignore
        return! Successful.OK "OK" next ctx
      }
    )
  }

let app =
  application {
    url "http://0.0.0.0:4771"
    use_router webApp
    memory_cache
    use_static "public"
    use_gzip
    service_config (fun svcs -> 
       let serializationOptions = SystemTextJson.Serializer.DefaultOptions
    
       serializationOptions.Converters.Add(JsonFSharpConverter(JsonUnionEncoding.FSharpLuLike))

       svcs.AddSingleton<Json.ISerializer>(SystemTextJson.Serializer(serializationOptions))
    )
  }

run app
