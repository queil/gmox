# Integration

To manually validate protos structure:

```bash
protoc protos/org/books/list/*.proto protos/org/books/shared/*.proto protos/shared/*.proto -I protos --plugin=protoc-gen-grpc=grpc_csharp_plugin --csharp_out=out --grpc_out=out
```