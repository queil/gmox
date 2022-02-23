namespace Queil.Gmox.Server

open System.Text.Json.Serialization
open System.Text.Json
open Google.Protobuf
open Google.Protobuf.Reflection
open Microsoft.AspNetCore.Http
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

  type ProtoMessageConverter(ctxAccessor: IHttpContextAccessor) =
    inherit JsonConverter<IMessage>()
    override _.Read(reader: byref<Utf8JsonReader> , _, _:JsonSerializerOptions) : IMessage =

      let str = JsonDocument.ParseValue(&reader).RootElement.GetRawText()
      let descriptor = ctxAccessor.HttpContext.Items["response-descriptor"] :?> MessageDescriptor
      JsonParser.Default.Parse(str, descriptor)

    override _.Write(writer: Utf8JsonWriter, msg: IMessage, _:JsonSerializerOptions) : unit =
      writer.WriteRawValue(JsonFormatter.Default.Format(msg))

  type StubConverter(options: JsonSerializerOptions, fsOptions: JsonFSharpOptions) =
    inherit JsonRecordConverter<Stub>(options, fsOptions)
       
      override this.Read(reader: byref<Utf8JsonReader>, typeToConvert: System.Type, options: JsonSerializerOptions) =
        base.Read(&reader, typeToConvert, options)
        
      override this.Write(writer, value, options) =
        base.Write(writer, value, options)

  type StubArrayConverter(converter: StubConverter) =
    inherit JsonConverter<Stub []>()
    static let rec arr (acc: Stub list, reader:byref<Utf8JsonReader>, options: JsonSerializerOptions, converter: StubConverter) =
      match &reader with
      | JArrayEnd _ -> acc
      | _ ->
        let elem = converter.Read(&reader, typeof<Stub>, options)
        arr((elem::acc), &reader, options, converter)
      
    override this.Read(reader, _, options) =
      match &reader with
      | Expect JsonTokenType.StartArray _ ->
        (arr([], &reader, options, converter) |> List.rev |> List.toArray)            
        
    override this.Write(writer, value, options) =
      writer.WriteStartArray()
      for v in value do
        converter.Write(writer, v, options)
      writer.WriteEndArray()
