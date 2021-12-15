namespace Queil.Gmox


module Types =
  open System.Collections.Concurrent
  open Grpc.Core
  open System.Reflection
  open System
  open Google.Protobuf
  open System.Threading.Tasks
  open Microsoft.AspNetCore.Routing
  open Grpc.AspNetCore.Server

  module internal ConcurrentDict =
    open System.Collections.Generic
    
    let addOrReplace  (key: 'a) (d:ConcurrentDictionary<'a,'b>) (addValue:'a -> 'b) (updateValue:'a -> 'b -> 'b) =
      d.AddOrUpdate(key, addValue, updateValue) |> ignore
      d
    
    let make (items: seq<'a * 'b>) = items |> Seq.map KeyValuePair |> ConcurrentDictionary<'a,'b>

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
    Data: obj
    Error: string
  }

  type Msg = {
    Data: IMessage
    Error: string
  }

  type Mapping = (Input * Msg) list
  type Methods = ConcurrentDictionary<string,Mapping>
  type Services = ConcurrentDictionary<string,Methods>
  type Serialize = obj -> string

  type StubStore(serialize: Serialize, endpoints: EndpointDataSource) =
    let stubs = Services()

    let responseType (m:MethodInfo) =
      let returnType = m.ReturnType
      if (typeof<Task<_>>).GetGenericTypeDefinition() = returnType.GetGenericTypeDefinition()
      then returnType.GenericTypeArguments.[0]
      else failwithf "Unexpected method return type. Expected: Task<_> but was %s" returnType.FullName

    let getGrpcMethod (s:Stub) =
      let grpcMethod = 
        endpoints.Endpoints
        |> Seq.map (fun x -> x.Metadata.GetMetadata<GrpcMethodMetadata>())
        |> Seq.filter (not << isNull)
        |> Seq.find(fun x -> x.Method.FullName = $"/{s.Service}/{s.Method}")
      grpcMethod.ServiceType.GetMethod(s.Method, BindingFlags.Public ||| BindingFlags.Instance)


    member _.resolveResponse (request:IMessage) (context:ServerCallContext) (mb:MethodBase) =
      let chunks = context.Method.Split('/', StringSplitOptions.RemoveEmptyEntries)
      
      let (service, method) =
        match chunks with
        | [| s; m |] -> (s, m)
        | _ -> failwithf "Could not parse method %s" (context.Method)
      
      let default' () = Activator.CreateInstance(responseType (mb :?> MethodInfo)) :?> IMessage

      let msg =
        match stubs.TryGetValue(service) with
        | true, methods ->
          match methods.TryGetValue(method) with
          | true, mappings ->
            mappings |> Seq.find(fun (i, o) ->
              match i with
              | Equals x -> true
              | Contains x -> true
              | Matches x -> true
            ) |> fun (i,o) -> o.Data
          | _ -> default' ()
        | _ -> default' ()
      Task.FromResult(msg)

    member _.addOrReplace (s:Stub) =
      
      let rsType = s |> (getGrpcMethod >> responseType)

      let parser = typeof<JsonParser>.GetMethod("Parse", BindingFlags.Public ||| BindingFlags.Instance, [|typeof<string>|])
                    .GetGenericMethodDefinition()
                    .MakeGenericMethod(rsType)
      
      let msg = {
        // this is awful - to be fixed (i.e. serialize to deserialize)
        Data = parser.Invoke(JsonParser.Default, [|serialize s.Output.Data|]) :?> IMessage
        Error = s.Output.Error
      }
      let setAdd = stubs |> ConcurrentDict.addOrReplace s.Service
      let setReplace = setAdd <| fun _ -> seq { s.Method, [s.Input, msg] } |> ConcurrentDict.make
      (setReplace <| fun _ methods ->
        let setAdd = methods |> ConcurrentDict.addOrReplace s.Method
        let setReplace = setAdd <| fun _ -> [s.Input, msg ]
        setReplace <| fun _ _ -> [ s.Input, msg ])
      |> ignore
    
    member _.list () = 
      query {
        for KeyValue(svc, methods) in stubs do
        for KeyValue(method, mappings) in methods do
        for (a, b) in mappings do
        select {
          Service = svc
          Method = method
          Input = a
          Output = {
            Data = b.Data
            Error = b.Error
          }
        }
      }
      
    member _.clear () = stubs.Clear()
    
    member x.GetFindStubMethodName () = nameof(x.resolveResponse)
    static member FindStubMethodName = StubStore((fun _ -> ""), null).GetFindStubMethodName ()
