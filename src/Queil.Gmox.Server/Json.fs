namespace Queil.Gmox.Server

open System.Text.Json.Nodes
open System.Text.Json.Serialization
open System.Text.Json
open Google.Protobuf
open Google.Protobuf.Reflection
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

  type GetDescriptorByTypeName = string -> MessageDescriptor
  type AddOrReplaceStub = JsonNode -> unit

  type JsonSerializerOptions with
    member this.TryGetConverter<'a when 'a :> JsonConverter>() =
      this.Converters |> Seq.tryFind (fun c -> c.GetType() = typeof<'a>) |> Option.map (fun x -> x :?> 'a)

  type ResponseDescriptorHolder(descriptor:MessageDescriptor) =
    inherit JsonConverter<bool>()
      member val ResponseDescriptor = descriptor
      override _.CanConvert(_:System.Type) = false
      override this.Read(_, _, _) = failwith "This should never be called"
      override this.Write(_, _, _) = failwith "This should never be called"

  type ProtoMessageConverter() =
    inherit JsonConverter<IMessage>()
    override _.Read(reader: byref<Utf8JsonReader> , _, options:JsonSerializerOptions) : IMessage =

      let str = JsonDocument.ParseValue(&reader).RootElement.GetRawText()
      let descriptor =
        match options.TryGetConverter<ResponseDescriptorHolder>() with
        | None -> failwithf $"Expected %s{nameof(ResponseDescriptorHolder)}"
        | Some h -> h.ResponseDescriptor
      JsonParser.Default.Parse(str, descriptor)

    override _.Write(writer: Utf8JsonWriter, msg: IMessage, _:JsonSerializerOptions) : unit =
      writer.WriteRawValue(JsonFormatter.Default.Format(msg))

  type StubConverter(options: JsonSerializerOptions, fsOptions: JsonFSharpOptions) =
    inherit JsonRecordConverter<Stub>(options, fsOptions)
       
      override this.Read(reader: byref<Utf8JsonReader>, typeToConvert: System.Type, options: JsonSerializerOptions) =
        base.Read(&reader, typeToConvert, options)
        
      override this.Write(writer, value, options) =
        base.Write(writer, value, options)
