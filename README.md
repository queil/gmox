# Gmox

.NET-friendly gRPC mocks server inspired by [gripmock](https://github.com/tokopedia/gripmock) powered by [Saturn](https://saturnframework.org/) and F#.

Gmox exposes two endpoints:

* gRPC (port `4770`) serving endpoint
* HTTP (port `4771`) control endpoint (for dynamic stubbing)
  * `GET` `/` - lists configured stubs
  * `POST` `/add` - adds stub configuration
  * `POST` `/test` - test if the provided data matches any stub
  * `POST` `/clear` - deletes all the stub configurations

## Stubbing configuration rules

Requests are matched to the stubs in the following order (if multiple stubs for a method are configured):

* `exact` - matches if the request is exactly the same as the stub input
* `partial` - matches if the stub input data is a sub-tree of the request data
* `regexp` - like `partial` but values in requests can be matched with a regular expression

## TODO

* [ ] Unit tests!
* [x] Support dynamic stubbing:
  * [x] `exact`
  * [ ] `partial`
  * [ ] `regexp`
* [ ] Handle errors:
  * [ ] `/add` - if input JSON is not a valid request for the specified service method
* [ ] Support the following source of service schemas for static stubbing:
  1. [ ] NuGet package (compile-time - as a dotnet project template)
  2. [ ] Protos (runtime)
  3. [ ] NuGet (runtime)
* [ ] Support recording received calls and expose as via the control API
* [ ] Add examples for API endpoints
