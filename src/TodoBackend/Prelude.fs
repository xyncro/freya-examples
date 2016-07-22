[<AutoOpen>]
module TodoBackend.Prelude

open System
open System.IO
open System.Text
open Aether
open Chiron
open Freya.Core
open Freya.Core.Operators
open Freya.Machines.Http
open Freya.Optics.Http
open Freya.Types.Http

(* Optics

   Useful optics for working with generic forms of data. Here an epimorphism
   from string to Guid is provided to aid working with Guid formed identifiers
   in externally sent string data. *)

let guid_ : Epimorphism<string,Guid> =
    (fun x ->
        match Guid.TryParse x with
        | true, x -> Some x
        | _ -> None),
    (string)

(* Payload

   A helper function to read, parse and deserialize (where possible) the payload
   data of an HTTP request body to a statically resolved type. The function will
   therefore attempt to return the payload as whatever type is needed, returning
   None where this is not possible. *)

let inline payload () =
        function | x -> Json.tryParse ((new StreamReader (x: Stream)).ReadToEnd ())
     >> function | Choice1Of2 x -> Json.tryDeserialize x
                 | Choice2Of2 x -> Choice2Of2 x
     >> function | Choice1Of2 x -> Some x
                 | _ -> None
    <!> Freya.Optic.get Request.body_

(* Representation

   A helper function to return any object as a standard JSON encoded response,
   given the object implements the appropriate statically resolved members, as
   used in the Chiron JSON library. *)

let inline represent x =
    { Description =
        { Charset = Some Charset.Utf8
          Encodings = None
          MediaType = Some MediaType.Json
          Languages = None }
      Data = (Json.serialize >> Json.format >> Encoding.UTF8.GetBytes) x }