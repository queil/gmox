namespace Queil.Gmox

module Types =

  open Grpc.Core
  open System.Reflection
  open System
  open Google.Protobuf
  open System.Threading.Tasks
  open Microsoft.AspNetCore.Routing
  open Grpc.AspNetCore.Server
  open System.Collections.Generic
  open System.Text.Json.JsonDiffPatch
  open System.Text.Json.Nodes

  [<CustomEquality>][<NoComparison>]
  type Stub = {
    Method: string
    Expect: Expect
    Output: Output
  }
  with
    interface IEquatable<Stub> with
      member this.Equals other =
        this.Method = other.Method &&
        JsonDiffPatcher.DeepEquals(this.Expect.Matcher, other.Expect.Matcher) 
    override this.Equals other =
      match other with
      | :? Stub as s -> (this :> IEquatable<_>).Equals s
      | _ -> false
    override this.GetHashCode () =
      this.Method.GetHashCode()

  and Expect =
   | Exact of JsonNode
   | Partial of JsonNode
   | Matches of JsonNode
   member x.Matcher : JsonNode =
     match x with
     | Exact m -> m
     | Partial m -> m
     | Matches m -> m

  and Output() =
    member val Data = obj() with get, set
    member val Error = "" with get, set
    member val internal Msg = (WellKnownTypes.Empty() :> IMessage) with get, set

  type Stubs = HashSet<Stub>
  
  type TestData = {
    Method: string
    Data: JsonNode
  }


  type Serialize = obj -> string
  
  

  let (|JArr|JObj|JVal|) (n:JsonNode) =
    match n with
    | :? JsonArray as x -> JArr(x)
    | :? JsonValue as x -> JVal(x)
    | :? JsonObject as x-> JObj(x)
    | _ -> failwithf "JsonNode '%s' is not supported" (n.ToJsonString())
  
  type Mode = Exact | Partial | Matches 




  type StubStore(serialize: Serialize, endpoints: EndpointDataSource) =
    let stubs = Stubs()

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
        |> Seq.find(fun x -> x.Method.FullName.[1..] = s.Method)
      grpcMethod.ServiceType.GetMethod(s.Method.Split("/")[1], BindingFlags.Public ||| BindingFlags.Instance)

    member _.resolveResponse (request:IMessage) (context:ServerCallContext) (mb:MethodBase) =

      let default' () = Activator.CreateInstance(responseType (mb :?> MethodInfo)) :?> IMessage
      
      let method = context.Method.[1..]
      Task.FromResult(default' ())

    member _.addOrReplace (s:Stub) =

      let rsType = s |> (getGrpcMethod >> responseType)

      let parser = typeof<JsonParser>.GetMethod("Parse", BindingFlags.Public ||| BindingFlags.Instance, [|typeof<string>|])
                    .GetGenericMethodDefinition()
                    .MakeGenericMethod(rsType)

      s.Output.Msg <- parser.Invoke(JsonParser.Default, [|serialize s.Output.Data|]) :?> IMessage
      
      stubs.Add s |> ignore

    member _.list () = stubs |> List.ofSeq
    member _.clear () = stubs.Clear()
    member _.test (test:TestData) =
      
      let isMatch (expected:Expect) (actual:JsonNode) : bool =
        let mode, exp =
          match expected with
          | Expect.Exact n -> Exact, n
          | Expect.Partial n -> Partial, n
          | Expect.Matches n -> Matches, n

        let rec next (xp:JsonNode) (ac:JsonNode) =
          match mode, xp, ac with
          | Exact, JArr a, JArr b when a.Count = b.Count ->
            query {
              for va in a do
              join vb in b
                on (va = vb)
              all (next va vb)
            }
          | Exact, JObj a, JObj b when a.Count = b.Count ->
            query {
              for KeyValue(ka, va) in a do
              join bkv in b
                on (ka = bkv.Key)
              all (next va bkv.Value)
            }
          | Exact, JVal a, JVal b when a.ToJsonString() = b.ToJsonString() -> true
          | Partial, JArr a, JArr b when a.Count <= b.Count ->
            query {
              for vb in b do
              leftOuterJoin va in a
                on (vb = va) into result
              for x in result do
              all (next x vb)
            }
          | Partial, JObj a, JObj b when a.Count <= b.Count ->
            // query {
            //   for KeyValue(kb, vb) in b do
            //   leftOuterJoin akv in a
            //     on (kb = akv.Key) into result
            //   for x in result do
            //   all (next x.Value vb)
            // }
            query {
              for KeyValue(ka, va) in a do
              join bkv in b
                on (ka = bkv.Key)
              all (next va bkv.Value)
            }
          | Partial, JVal a, JVal b when a.ToJsonString() = b.ToJsonString() -> true
          | _ -> false
        next exp actual
      stubs |> Seq.find (fun x -> x.Method = test.Method && isMatch x.Expect test.Data)
