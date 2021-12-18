namespace Queil.Gmox.Infra

open System
open System.Text.Json.Serialization
open System.Text.Json
open Google.Protobuf

module Json =
  type ProtoMessageConverter<'a when 'a :> IMessage>() =
    inherit JsonConverter<'a>()
    override _.Read(reader: byref<Utf8JsonReader> , typ: Type, options:JsonSerializerOptions) : 'a =
      failwith "not implemented"

    override _.Write(writer: Utf8JsonWriter, msg: 'a, options:JsonSerializerOptions) : unit =
      writer.WriteRawValue(JsonFormatter.Default.Format(msg))

  type ProtoMessageConverterFactory() =
    inherit JsonConverterFactory()
    override _.CanConvert(typ:Type) = typeof<IMessage>.IsAssignableFrom(typ)
    override _.CreateConverter(typeToConvert: Type, options: JsonSerializerOptions): JsonConverter =
      let def = typeof<ProtoMessageConverter<WellKnownTypes.Empty>>.GetGenericTypeDefinition()
      let convType = def.MakeGenericType([|typeToConvert|])
      Activator.CreateInstance(convType) :?> JsonConverter
