
(* API

   We'll expose our domain model to the world using Freya to map the internal
   model to a simple resource based API. We'll use Freya HTTP machines to
   structure our resources in a clean way. *)

module Api =

    open System
    open System.IO
    open System.Text
    open Aether
    open Aether.Operators
    open Chiron
    open Freya.Core
    open Freya.Core.Operators
    open Freya.Machines.Http
    open Freya.Machines.Http.Cors
    open Freya.Machines.Http.Patch
    open Freya.Optics.Http
    open Freya.Routers.Uri.Template
    open Freya.Types.Http
    open Freya.Types.Http.Patch

    open TodoBackend.Domain

    (* Optics

       Useful optics for working with generic forms of data. Here an epimorphism
       from string to Guid is provided to aid working with Guid formed identifiers
       in routes. *)

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

    (* Route *)

    let id =
        Freya.memo (Option.get <!> Freya.Optic.get (Route.atom_ "id" >?> guid_))

    (* Operations *)

    let add =
        Freya.memo (payload () >>= fun p -> Freya.fromAsync (p.Value, addTodo))

    let clear =
        Freya.memo (Freya.fromAsync ((), clearTodos))

    let delete =
        Freya.memo (id >>= fun i -> Freya.fromAsync (i, deleteTodo))

    let get =
        Freya.memo (id >>= fun i -> Freya.fromAsync (i, getTodo))

    let list =
        Freya.memo (Freya.fromAsync ((), listTodos))

    let update =
        Freya.memo (id >>= fun i -> payload () >>= fun p -> Freya.fromAsync ((i, p.Value), updateTodo))

    (* Machine
       We define the functions that we'll use for decisions and resources
       within our freyaMachine expressions here. We can use the results of
       operations like "add" multiple times without worrying as we memoized
       that function.
       We also define a resource (common) of common properties of a resource,
       this saves us repeating configuration multiple times (once per resource).
       Finally we define our two resources, the first for the collection of Todos,
       the second for an individual Todo. *)

    let todos =
        freyaMachine {
            methods [ DELETE; GET; OPTIONS; POST ]
            created true
            doDelete (ignore <!> clear)
            doPost (ignore <!> add)
            handleCreated (represent <!> add)
            handleOk (represent <!> list)

            cors }

    let todo =
        freyaMachine {
            methods [ DELETE; GET; OPTIONS; PATCH ]
            doDelete (ignore <!> delete)
            handleOk (represent <!> get)

            cors

            patch
            patchDoPatch (ignore <!> update) }

    (* Router

       To route requests to our two machine resources, we'll use the Freya
       URI Template router, routing all requests which match appropriate paths
       to the correct resource. *)

    let todoRouter =
        freyaRouter {
            resource "/" todos
            resource "/{id}" todo }

    (* API

       Finally we expose our actual API. In more complex applications than this
       we would expect to see multiple components of the application pipelined
       to form a more complex whole, but in this case we only have our single router. *)

    let api =
        todoRouter

(* Server

   The basic types we need to hook up the API built with Freya to a common web
   server, in this case Katana. *)

module Server =

    open Freya.Core

    (* Katana

       Katana (Owin Self Hosting) expects us to expose a type with a specific
       method. Freya lets us do see easily, the OwinAppFunc module providing
       functions to turn any Freya<'a> function in to a suitable value for
       OWIN compatible hosts such as Katana. *)

    type TodoBackend () =
        member __.Configuration () =
            OwinAppFunc.ofFreya (Api.api)

(* Main

   A very simple program, simply a console app, with a blocking read from
   the console to keep our server from shutting down immediately. Though
   we are self hosting here as a console application, the same application
   should be easily transferrable to any OWIN compatible server, including
   IIS. *)

open System
open Microsoft.Owin.Hosting

[<EntryPoint>]
let main _ =

    let _ = WebApp.Start<Server.TodoBackend> ("http://localhost:7000")
    let _ = Console.ReadLine ()

    0