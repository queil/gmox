module Tests

open System.Text.Json
open Expecto
open Org.Books.Shared
open Queil.Gmox.Core.Types
open System.Text.Json.Nodes
open Org.Books.List

[<Tests>]
let tests =

  let store () = StubStore(fun s -> typeof<ListResponse>)
  
  let json (value:'a) = JsonSerializer.SerializeToNode(value)
  
  let givenStubs (stubs: Stub list) =
    let store = store ()
    for s in stubs do
      store.addOrReplace s
    store 
  
  let response author =
     let r = ListResponse()
     let b = Book()
     b.Author <- author
     r.Books.Add(b)
     r
  
  let exactResponse = response "Exact"
  let partialResponse = response "Partial"
  let regexResponse = response "Regex"
  
  let testRequest =
    json {|
      this = {|
          is = [1; 2; 3]
      |};
      someString = "value"
    |}
  
  let exactMatch =
    json {|
      this = {|
          is = [1; 2; 3]
      |};
      someString = "value"
    |} |> Exact 
  
  let partialMatch =
    json {|
      this = {|
          is = [3]
      |}
    |} |> Partial
  
  let regexMatch =
    json {|
      someString = "va.*"
    |} |> Regex 
  
  let shouldBeCorrectMatch (expectedRs: ListResponse) (result: Stub option) =
      match result with
      | None -> "Expected stub configuration for `org.books.list.ListService/ListBooks`" |> Expect.isSome result
      | Some s ->
        match s.Return with
        | Data x ->
          let response = x :?> ListResponse
          "Expected stub configured with the exact match" |> Expect.equal (response.Books[0].Author) expectedRs.Books[0].Author
        | x -> failtest $"{x} should be Data case"
  
  testList "when matching stubs" [
    testCase "should match on exact" <| fun _ ->
      
      let store =
        givenStubs [
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = exactMatch
            Return = Data (ListResponse())
          }
        ]
      
      let result =  
        store.findBestMatchFor {
          Method = "org.books.list.ListService/ListBooks"
          Data = testRequest
        } 
      "Expected stub configuration for `org.books.list.ListService/ListBooks`" |> Expect.isSome result
      
    testCase "should match on partial" <| fun _ ->
      
      let store =
        givenStubs [
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = partialMatch
            Return = Data (ListResponse())
          }
        ]
      
      let result =  
        store.findBestMatchFor {
          Method = "org.books.list.ListService/ListBooks"
          Data = testRequest
        } 
      "Expected stub configuration for `org.books.list.ListService/ListBooks`" |> Expect.isSome result
      
    testCase "should match on regex" <| fun _ ->
      
      let store =
        givenStubs [
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = regexMatch
            Return = Data (ListResponse())
          }
        ]
      
      let result =  
        store.findBestMatchFor {
          Method = "org.books.list.ListService/ListBooks"
          Data = testRequest
        } 
      "Expected stub configuration for `org.books.list.ListService/ListBooks`" |> Expect.isSome result
      
    testCase "should match find most specific match" <| fun _ ->
      let store =
        givenStubs [
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = regexMatch
            Return = Data regexResponse
          };
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = partialMatch
            Return = Data partialResponse
          };
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = exactMatch
            Return = Data exactResponse
          }
        ]
      let result =  
        store.findBestMatchFor {
          Method = "org.books.list.ListService/ListBooks"
          Data = testRequest
        }
      shouldBeCorrectMatch exactResponse result
      
    testCase "should match find most specific match 2" <| fun _ ->
      let store =
        givenStubs [
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = regexMatch
            Return = Data regexResponse
          };
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = partialMatch
            Return = Data partialResponse
          };
        ]
      let result =  
        store.findBestMatchFor {
          Method = "org.books.list.ListService/ListBooks"
          Data = testRequest
        }
      shouldBeCorrectMatch partialResponse result
      
    testCase "should match find most specific match 3" <| fun _ ->
      let store =
        givenStubs [
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = regexMatch
            Return = Data regexResponse
          };
          {
            Method = "org.books.list.ListService/ListBooks"
            Match = exactMatch
            Return = Data exactResponse
          }
        ]
      let result =  
        store.findBestMatchFor {
          Method = "org.books.list.ListService/ListBooks"
          Data = testRequest
        }
      shouldBeCorrectMatch exactResponse result
  ]

