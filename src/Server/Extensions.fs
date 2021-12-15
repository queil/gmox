module Saturn

open Saturn
open Microsoft.AspNetCore.Builder

open Microsoft.Extensions.DependencyInjection
open System.Reflection
open System
open System.Threading.Tasks
open System.Collections.Concurrent

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
open Grpc.Core


module ConcurrentDict =
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
with
  member x.As(typ: Type) =
    let parseMethod =
      typeof<JsonParser>.GetMethod("Parse", [|typeof<string>|])
        .GetGenericMethodDefinition()
        .MakeGenericMethod([|typ|])
    parseMethod.Invoke(JsonParser.Default, [|JsonFormatter.Default.Format(x.Data)|])

type Mapping = (Input * Msg) list
type Methods = ConcurrentDictionary<string,Mapping>
type Services = ConcurrentDictionary<string,Methods>


type Store2(serializer: Json.ISerializer) =
  let stubs = Services()

  member x.findStub2 (z:obj) =
     Task.FromResult(Grpc.Health.V1.HealthCheckResponse())


 // TODO: use MethodBase.GetCurrentMethod in the dynamic impl and inject here
  member x.findStub3 (request:IMessage) (context:ServerCallContext) (mb:MethodBase) =
    let chunks = context.Method.Split('/', StringSplitOptions.RemoveEmptyEntries)
    let http = context.GetHttpContext()
    
    let (service, method) =
      match chunks with
      | [|s; m|] -> (s, m)
      | _ -> failwithf "Could not parse method %s" (context.Method)
    
    let returnType = (mb :?> MethodInfo).ReturnType
    let responseType = 
      if (typeof<Task<_>>).GetGenericTypeDefinition() = returnType.GetGenericTypeDefinition()
      then returnType.GenericTypeArguments.[0]
      else failwithf "Unexpected method return type. Expected: Task<_> but was %s" returnType.FullName
     

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
        | _ -> Activator.CreateInstance(responseType) :?> IMessage //we need to get in hold of the actual response type here
      | _ -> Activator.CreateInstance(responseType) :?> IMessage //we need to get in hold of the actual response type here
    Task.FromResult(msg)

  member x.addOrReplace (s:Stub) =
    
    let msg = {
      Data= JsonParser.Default.Parse<Grpc.Health.V1.HealthCheckResponse>(serializer.SerializeToString(s.Output.Data))
      Error = s.Output.Error
    }
    let setAdd = stubs |> ConcurrentDict.addOrReplace s.Service
    let setReplace = setAdd <| fun _ -> seq { s.Method, [s.Input, msg] } |> ConcurrentDict.make
    (setReplace <| fun _ methods ->
      let setAdd = methods |> ConcurrentDict.addOrReplace s.Method
      let setReplace = setAdd <| fun _ -> [s.Input, msg ]
      setReplace <| fun _ _ -> [ s.Input, msg ])
    |> ignore
  
  member x.list () = 
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
    
  // member x.findStub<'a, 'b when 'a :> IMessage and 'b :> IMessage and 'b: (new:unit -> 'b) > (serializer:Json.ISerializer) (msg: 'a) (service: string) (method:string) : 'b =

  //   let message = Google.Protobuf.JsonFormatter.Default.Format(msg)
  //   let map = serializer.Deserialize<Map<string, obj>>(message)

  //   match stubs.TryGetValue(service) with
  //   | true, methods ->
  //     match methods.TryGetValue(method) with
  //     | true, mappings ->
  //       mappings |> Seq.find(fun (i, o) ->
  //         match i with
  //         | Equals x -> true
  //         | Contains x -> true
  //         | Matches x -> true
  //       ) |> fun (i,o) -> Google.Protobuf.JsonParser.Default.Parse<'b>(serializer.SerializeToString(o.Data))
  //     | _ -> new 'b()
  //   | _ -> new 'b()

  member x.clear () = stubs.Clear()


module Json2 =

  type ProtoMessageConverter<'a when 'a :> IMessage>()  (*and 'a : (new: unit -> 'a)*) =
   inherit JsonConverter<'a>()
   override x.Read(reader: byref<Utf8JsonReader> , typ: Type, options:JsonSerializerOptions) : 'a =
     failwith "boom" //JsonParser.Default.Parse(reader.GetString())
   
   override x.Write(writer: Utf8JsonWriter, msg: 'a, options:JsonSerializerOptions) : unit =
     writer.WriteRawValue(JsonFormatter.Default.Format(msg))

  type ProtoMessageConverterFactory() =
    inherit JsonConverterFactory()
    override x.CanConvert(typ:Type) = typeof<IMessage>.IsAssignableFrom(typ)
    override x.CreateConverter(typeToConvert: Type, options: JsonSerializerOptions): JsonConverter = 
      let def = typeof<ProtoMessageConverter<WellKnownTypes.Empty>>.GetGenericTypeDefinition()
      let convType = def.MakeGenericType([|typeToConvert|])
      Activator.CreateInstance(convType) :?> JsonConverter

                
module GrpcNonGeneric =
  open Microsoft.AspNetCore.Routing
  let MapGrpcService (serviceType: Type) (builder: IEndpointRouteBuilder)  =
    //let r = Activator.CreateInstance(serviceType, Store2())
    
    let typ = typeof<GrpcEndpointRouteBuilderExtensions>
    typ.GetMethod("MapGrpcService", BindingFlags.Static ||| BindingFlags.Public)
       .GetGenericMethodDefinition()
       .MakeGenericMethod(serviceType)
       .Invoke(null, [|builder|]) |> ignore

type Saturn.Application.ApplicationBuilder with

    [<CustomOperation("use_grpc_2")>]
    ///Adds gRPC Code First endpoint. Passed parameter should be any constructor of the gRPC service implementation.
    member __.UseGrpc<'a, 'b when 'a : not struct>(state, serviceType: Type) =
        let configureServices (services: IServiceCollection) =
            services.AddGrpc() |> ignore
            services.AddGrpcReflection() |> ignore
            services.AddSingleton<Store2>() |> ignore
            services
            

        let configureApp (app: IApplicationBuilder) =
            app.UseRouting()

        let configureGrpcEndpoint (app: IApplicationBuilder) =
            app.UseEndpoints (fun endpoints -> 
              
              endpoints |> GrpcNonGeneric.MapGrpcService serviceType
              endpoints.MapGrpcReflectionService() |> ignore
            )
            

        { state with
            AppConfigs = configureApp::configureGrpcEndpoint::state.AppConfigs
            ServicesConfig = configureServices::state.ServicesConfig
        }