# Integration

To manually validate protos structure:

```bash
protoc protos/org/books/**/*.proto  protos/shared/*.proto -I protos --plugin=protoc-gen-grpc=../../grpc_csharp_plugin --csharp_out=Org.Books --grpc_out=Org.Books
```