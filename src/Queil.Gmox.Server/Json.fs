namespace Queil.Gmox.Server

open System
open System.Collections.Generic
open System.Reflection
open System.Text.Json.Serialization
open System.Text.Json
open Google.Protobuf
open Queil.Gmox.Core.Types

module Json =

  let (|JArrayEnd|_|) (reader:byref<Utf8JsonReader>) =
    reader.Read() |> ignore
    match reader.TokenType with
    | JsonTokenType.EndArray -> Some()
    |_ -> None

  let (|Expect|) (tokenType: JsonTokenType) (reader:byref<Utf8JsonReader>)  =
    match reader.TokenType with
    | t when t = tokenType -> Expect
    | _ -> raise (JsonException())

  let (|Unless|) (tokenType: JsonTokenType) (reader:byref<Utf8JsonReader>)  =
    match reader.TokenType with
    | t when t = tokenType -> raise (JsonException())
    | _ -> Unless

  type JsonSerializerOptions with
    member this.TryGetConverter<'a when 'a :> JsonConverter>() =
      this.Converters |> Seq.tryFind (fun c -> c.GetType() = typeof<'a>) |> Option.map (fun x -> x :?> 'a)

  type ResponseTypeHolder() =
    inherit JsonConverter<bool>()
      member val ResponseType = typeof<unit> with get, set
      override _.CanConvert(_:Type) = false
      override this.Read(_, _, _) = failwith "This should never be called"
      override this.Write(_, _, _) = failwith "This should never be called"

  type ProtoMessageConverter<'a when 'a :> IMessage>() =
    inherit JsonConverter<'a>()
    member val Parsers = Dictionary<Type, MethodInfo>()
    override x.Read(reader: byref<Utf8JsonReader> , _, options:JsonSerializerOptions) : 'a =
      let holder =
        match options.TryGetConverter<ResponseTypeHolder>() with
        | None -> failwith "ResponseTypeHolder expected"
        | Some h -> h

      let parseProtoMessage =
        if not <| x.Parsers.ContainsKey holder.ResponseType then 
          x.Parsers.[holder.ResponseType] <-    
              typeof<JsonParser>
                .GetMethod("Parse", BindingFlags.Public ||| BindingFlags.Instance, [|typeof<string>|])
                .GetGenericMethodDefinition()
                .MakeGenericMethod(holder.ResponseType)
        fun (data:string) -> x.Parsers.[holder.ResponseType].Invoke(JsonParser.Default, [| data |]) :?> 'a
             
      let str = JsonDocument.ParseValue(&reader).RootElement.GetRawText()
      str |> parseProtoMessage

    override _.Write(writer: Utf8JsonWriter, msg: 'a, _:JsonSerializerOptions) : unit =
      writer.WriteRawValue(JsonFormatter.Default.Format(msg))

  type ProtoMessageConverterFactory() =
    inherit JsonConverterFactory()
    override _.CanConvert(typ:Type) = typeof<IMessage>.IsAssignableFrom(typ)
    override _.CreateConverter(typeToConvert: Type, options: JsonSerializerOptions): JsonConverter =
      let def = typeof<ProtoMessageConverter<WellKnownTypes.Empty>>.GetGenericTypeDefinition()
      let convType = def.MakeGenericType([|typeToConvert|])
      Activator.CreateInstance(convType) :?> JsonConverter

  type StubConverter(resolveResponseType: ResolveResponseType, options: JsonSerializerOptions, fsOptions: JsonFSharpOptions) =
    inherit JsonRecordConverter<Stub>(options, fsOptions)
      member x.GetMethod(reader: byref<Utf8JsonReader>) =
        let methodPropertyName = fsOptions.UnionTagNamingPolicy.ConvertName(nameof Unchecked.defaultof<Stub>.Method)
        let cp = reader
        match cp.TokenType with
        | JsonTokenType.StartObject ->
          let mutable methodName : string = ""
          while methodName = "" && cp.Read() do
            if cp.TokenType = JsonTokenType.EndObject then ()
            else
              if cp.TokenType = JsonTokenType.PropertyName && cp.GetString() = methodPropertyName
              then
                cp.Read() |> ignore
                methodName <- cp.GetString()
              else
                cp.Skip()
              cp.Read() |> ignore
          methodName
        | _ -> raise (JsonException())
       
      override this.Read(reader: byref<Utf8JsonReader>, typeToConvert: Type, options: JsonSerializerOptions) =
        let method = this.GetMethod(&reader)
        let responseType = method |> resolveResponseType
        let holder = options.TryGetConverter<ResponseTypeHolder>()
        holder |> Option.iter(fun x -> x.ResponseType <- responseType)
        base.Read(&reader, typeToConvert, options)
        
      override this.Write(writer, value, options) =
        base.Write(writer, value, options)

  type StubArrayConverter(converter: StubConverter) =
    inherit JsonConverter<Stub []>()
    static member private Arr (acc: Stub list, reader:byref<Utf8JsonReader>, options: JsonSerializerOptions, converter: StubConverter) =
      match &reader with
      | JArrayEnd _ -> acc
      | _ ->
        let elem = converter.Read(&reader, typeof<Stub>, options)
        StubArrayConverter.Arr((elem::acc), &reader, options, converter)
      
    override this.Read(reader, _, options) =
      match &reader with
      | Expect JsonTokenType.StartArray _ ->
        (StubArrayConverter.Arr([], &reader, options, converter) |> List.rev |> List.toArray)            
        
    override this.Write(writer, value, options) =
      writer.WriteStartArray()
      for v in value do
        converter.Write(writer, v, options)
      writer.WriteEndArray()

  type StubConverterFactory(converter:StubConverter) =
    inherit JsonConverterFactory()
    override this.CreateConverter(_, _) = converter
    override this.CanConvert(typeToConvert) = typeToConvert = typeof<Stub>
