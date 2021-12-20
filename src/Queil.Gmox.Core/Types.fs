namespace Queil.Gmox.Core

open Google.Protobuf
open Grpc.Core
open System
open System.Collections.Generic
open System.Reflection
open System.Text.Json.JsonDiffPatch
open System.Text.Json.Nodes
open System.Threading.Tasks

module Types =

  [<CustomEquality>][<NoComparison>]
  type Stub = {
    Method: string
    Match: Rule
    Return: Output
  }
  with
    interface IEquatable<Stub> with
      member this.Equals other =
        this.Method = other.Method &&
        JsonDiffPatcher.DeepEquals(this.Match.Matcher, other.Match.Matcher) 
    override this.Equals other =
      match other with
      | :? Stub as s -> (this :> IEquatable<_>).Equals s
      | _ -> false
    override this.GetHashCode () =
      this.Method.GetHashCode()

  and Rule =
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
  type GetGrpcMethod = Stub -> MethodInfo
  
  let (|JArr|JObj|JVal|) (n:JsonNode) =
    match n with
    | :? JsonArray as x -> JArr(x)
    | :? JsonValue as x -> JVal(x)
    | :? JsonObject as x-> JObj(x)
    | _ -> failwithf "JsonNode '%s' is not supported" (n.ToJsonString())
  
  type Mode = Exact | Partial | Matches 

  type StubStore(serialize: Serialize, getGrpcMethod: GetGrpcMethod) =
    let stubs = Stubs()

    let getResponseType (m:MethodInfo) =
      let returnType = m.ReturnType
      if (typeof<Task<_>>).GetGenericTypeDefinition() = returnType.GetGenericTypeDefinition()
      then returnType.GenericTypeArguments.[0]
      else failwithf "Unexpected method return type. Expected: Task<_> but was %s" returnType.FullName
    
    let parserFor (typ:Type) =
      let parser =
        typeof<JsonParser>
          .GetMethod("Parse", BindingFlags.Public ||| BindingFlags.Instance, [|typeof<string>|])
          .GetGenericMethodDefinition()
          .MakeGenericMethod(typ)
      
      fun (data:string) -> parser.Invoke(JsonParser.Default, [|data|]) :?> IMessage

    member x.resolveResponse (request:IMessage) (context:ServerCallContext) (mb:MethodBase) =
      let method = context.Method.[1..]
      let maybeStub = x.findBestMatchFor {Method = method; Data = JsonNode.Parse(JsonFormatter.Default.Format(request)) }
      
      let response =
        match maybeStub with
        | None -> 
          let default' () = Activator.CreateInstance(getResponseType (mb :?> MethodInfo)) :?> IMessage
          default' ()
        | Some s -> s.Return.Msg

      Task.FromResult(response)

    member _.addOrReplace (s:Stub) =
      let responseType = s |> (getGrpcMethod >> getResponseType)
      let parse = parserFor responseType
      s.Return.Msg <- s.Return.Data |> serialize |> parse
      stubs.Add s |> ignore

    member _.list () = stubs |> List.ofSeq
    member _.clear () = stubs.Clear()
    member _.findBestMatchFor (test:TestData) : Stub option =
      
      let isMatch (expected:Rule) (actual:JsonNode) : bool =
        let mode, exp =
          match expected with
          | Rule.Exact n -> Exact, n
          | Rule.Partial n -> Partial, n
          | Rule.Matches n -> Matches, n

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
              for va in a do
              join vb in b
                on (va = vb)
              all (next va vb)
            }
          | Partial, JObj a, JObj b when a.Count <= b.Count ->
            query {
              for KeyValue(ka, va) in a do
              join bkv in b
                on (ka = bkv.Key)
              all (next va bkv.Value)
            }
          | Partial, JVal a, JVal b when a.ToJsonString() = b.ToJsonString() -> true
          | _ -> false
        next exp actual
      stubs |> Seq.tryFind (fun x -> x.Method = test.Method && isMatch x.Match test.Data)
