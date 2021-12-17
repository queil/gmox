# Gmox

.NET-friendly gRPC mocks server inspired by [gripmock](https://github.com/tokopedia/gripmock) powered by [Saturn](https://saturnframework.org/) and F#.

Gmox exposes two endpoints:

* gRPC (port `4770`) serving endpoint
* HTTP (port `4771`) control endpoint (for dynamic stubbing)
  * `GET` `/` - lists configured stubs
  * `POST` `/add` - adds stub configuration
  * `POST` `/test` - test if the provided data matches any stub
  * `POST` `/clear` - deletes all the stub configurations

## Stubbing configuration

Stub configuration consists of a fully-qualified method name (like `grpc.health.v1.Health/Check`), a rule that matches incoming requests data, and a corresponding response data attached to it.

### Example

Given the server receives a call to the `grpc.health.v1.Health/Check` method and the message is `{}` (empty) then it returns `{"status": "SERVING"}`.

```json
{
  "method": "grpc.health.v1.Health/Check",
  "expect": {
    "exact": {}
  }, 
  "output": {
    "data": {
      "status": "SERVING"
    }
  }
}
```

Stubs can be configured using the following rules:

* `exact` - matches if the request is exactly the same as the stub input
* `partial` - matches if the stub input data is a sub-tree of the request data
* `regexp` - like `partial` but values in requests can be matched with a regular expression

Whenever a gRPC call is received the request data is evaluated against the available stub configurations.
Multiple stubs can be configured for a gRPC method and they're evaluated from the most (`exact`) to the least (`regexp`) specific.
The first match wins. 

## Usage/deployment modes

:information_source: All of the below modes support both static and dynamic stubbing.

### Dotnet CLI tool

Gmox as a dotnet CLI tool is useful in development scenarios when you want to quickly create a mock
server from protos.

### Dotnet Template

Gmox as a dotnet template is useful when you want to create a mock server for a particular service, equip it with a curated set of default rules, and/or it runs in an environment where you have no access to protos.

The template makes following assumptions out-of-the-box:

* references the NuGet package provided as a parameter
* auto-registers all the services from the assembly marked with a type (fully-qualified) passed as a parameter

Installation: `dotnet new --install Queil.Gmox.Template`

Usage: `dotnet new gmox -h`

### Docker image

Both the CLI tool and a server generated from the template may be packaged as Docker image and used in scenarios where having .NET SDK is not desirable.

## TODO

* [ ] Unit tests!
* [ ] Docs!
  * [ ] Add examples for API endpoints
* [x] Support dynamic stubbing:
  * [x] `exact`
  * [ ] `partial`
  * [ ] `regexp`
* [ ] Handle errors:
  * [ ] `/add` - if input JSON is not a valid request for the specified service method
* [ ] Support the following source of service schemas for static stubbing:
  1. [x] NuGet package (compile-time - as a dotnet project template, useful when we have no access to protos) 
  2. [ ] Protos (runtime - as a dotnet tool, useful on local dev when we do have protos and iterate quickly)
  3. [ ] NuGet (runtime - as a dotnet tool, this might be not needed)
* [ ] Support recording received calls and expose as via the control API
* [ ] Support requesting JSON-formatted request/responses so they can make creating stubs easier


## Development

### Testing `Queil.Gmox.Template`

```bash
cd ./src/Queil.Gmox.Template
dotnet new --uninstall $(pwd) && dotnet new --install $(pwd)
# then in another dir
dotnet new gmox -nu Your.NuGet.Package -as Your.NuGet.Package.Assembly.Type
```