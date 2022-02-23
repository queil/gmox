namespace Queil.Gmox.Server

open System
open Google.Protobuf
open Microsoft.Extensions.Hosting
open Microsoft.AspNetCore.Http
open Queil.Gmox.Core.Types
open Saturn
open Giraffe

module Router =

    let router =
      router {

          get "/" (fun _ ctx ->
            task {
              return! ctx.WriteJsonChunkedAsync(ctx.GetService<StubStore>().list())
            })
          
          post "/test" (fun _ ctx ->
            task {
              let! test = ctx.BindJsonAsync<TestData>()
              return! ctx.WriteJsonChunkedAsync(ctx.GetService<StubStore>().findBestMatchFor test)
            })
          
          post "/clear" (fun next ctx ->
            task {
              ctx.GetService<StubStore>().clear()
              return! Successful.NO_CONTENT next ctx
            })
          
          post "/add" (fun next ctx ->
            task {
              ctx.Request.EnableBuffering();
              let! stubMethod = ctx.BindJsonAsync<StubMethod>()
              ctx.Request.Body.Position <- 0
              let responseType = stubMethod.Method |> ctx.GetService<ResolveResponseType>()
              ctx.Items.Add("response-descriptor", (Activator.CreateInstance(responseType) :?> IMessage).Descriptor)
              let! output = ctx.BindJsonAsync<Stub>()
              output |> ctx.GetService<StubStore>().addOrReplace
              return! Successful.NO_CONTENT next ctx
            })
                
          post "/quit" (fun next ctx ->
            task {
               ctx.GetService<IHostApplicationLifetime>().StopApplication()
               return! Successful.NO_CONTENT next ctx
            }
          )
        }
