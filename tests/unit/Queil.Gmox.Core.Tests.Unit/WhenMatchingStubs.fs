module Tests

open Expecto
open Queil.Gmox.Core.Types
open System.Text.Json.Nodes
open System.Reflection


[<Tests>]
let tests =
  
  let store () = StubStore((fun a -> ""), (fun x -> Unchecked.defaultof<MethodInfo>))

  testList "when matching stubs" [
    testCase "should match on exact" <| fun _ ->
      
      let testData = {
          Method = "test/method"
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
        Method = "test/method"
        Match = Exact (JsonNode.Parse("{}"))
        Return = Output(Data = JsonNode.Parse("{}"))
      }

      let result = store.findBestMatchFor testData
      
      $"Expected stub configuration for {testData.Method}" |> Expect.isSome result
  ]
