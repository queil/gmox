module Tests

open Expecto
open Queil.Gmox.Core.Types
open System.Text.Json.Nodes
open System.Reflection
open Org.Books
open Org.Books.List


[<Tests>]
let tests =

  let store () = StubStore( (fun _ -> typeof<List.ListService.ListServiceBase>.GetMethod("ListBooks") ))

  testList "when matching stubs" [
    testCase "should match on exact" <| fun _ ->
      
      let testData = {
          Method = "org.books.list.ListService/ListBooks"
          Data = JsonNode.Parse(
            """{
                 "this": {
                   "is": [
                     1, 2, 3
                   ],
                   "string": "value"
                 }
              }""")
        }

      let store = store ()
      store.addOrReplace {
        Method = "org.books.list.ListService/ListBooks"
        Match = Exact (JsonNode.Parse("{}"))
        Return = { Data = ListResponse() }
      }

//could we deserialize Output data as json node and use Json property instead serialize

      let result = store.findBestMatchFor testData
      
      $"Expected stub configuration for {testData.Method}" |> Expect.isSome result
  ]
