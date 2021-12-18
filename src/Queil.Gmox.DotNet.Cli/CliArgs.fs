namespace Queil.Gmox.DotNet.Cli

open Argu

type CliArgs =
 | Proto of string
with
  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Proto _ -> "gRPC service proto path"
