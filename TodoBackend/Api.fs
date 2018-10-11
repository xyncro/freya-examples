namespace TodoBackend

(* Overview

   This is an idiosyncratic Freya implementation of a small and light server
   to implement the TodoBackend example application. The code is relatively
   minimal, and only implements the minimum to meet the requirements of the
   application.

   Support for more HTTP features and more fully "correct" support for resource
   design would be simple to add to the exposed API.

   See [http://www.todobackend.com] for more. *)

(* Domain

   We'll build a simple domain model, and an in-memory data store using a
   simple F# MailboxProcessor to serialize access to the state. This will give
   a simple but well-typed API.

   We'll also use Chiron to make the types representing input and output easily
   serializable to/from JSON as required. *)

module Domain =

    open System
    open Aether
    open Aether.Operators
    open Chiron
    open Chiron.Operators

    (* Types

       Some simple types representing a Todo item, and the New and Patch types
       which will form the input surface for the model.

       Note that they include Chiron ToJson and FromJson members as appropriate
       to allow for simple serialization. *)

    type Todo =
        { Id: Guid
          Url: string
          Order: int option
          Title: string
          Completed: bool }

        static member ToJson (x: Todo) =
                Json.write "id" x.Id
             *> Json.write "url" x.Url
             *> Json.write "order" x.Order
             *> Json.write "title" x.Title
             *> Json.write "completed" x.Completed

        static member create =
            fun (newTodo: NewTodo) ->
                let id =
                    Guid.NewGuid ()

                { Id = id
                  Url = sprintf "http://localhost:5000/%A" id
                  Order = newTodo.Order
                  Title = newTodo.Title
                  Completed = false }

     and NewTodo =
        { Title: string
          Order: int option }

        static member FromJson (_: NewTodo) =
                fun t o ->
                    { Title = t
                      Order = o }
            <!> Json.read "title"
            <*> Json.tryRead "order"

     and PatchTodo =
        { Title: string option
          Order: int option
          Completed: bool option }

        static member FromJson (_: PatchTodo) =
                fun t o c ->
                    { Title = t
                      Order = o
                      Completed = c }
            <!> Json.tryRead "title"
            <*> Json.tryRead "order"
            <*> Json.tryRead "completed"

    (* State

       Types for storing and interacting with the state of our domain model,
       which will be encapsulated in a mailbox processor. *)

    type State =
        { Todos: Map<Guid,Todo> }

        static member todos_ =
            (fun x -> x.Todos), (fun t x -> { x with Todos = t })

        static member empty =
            { Todos = Map.empty}

    type Protocol =
        | Add of AsyncReplyChannel<Todo> * NewTodo
        | Clear of AsyncReplyChannel<unit>
        | Delete of AsyncReplyChannel<unit> * Guid
        | Get of AsyncReplyChannel<Todo option> * Guid
        | List of AsyncReplyChannel<Todo list>
        | Update of AsyncReplyChannel<Todo> * Guid * PatchTodo

    (* Optics

       Useful optics for interacting with the State at various levels, in this
       case for interacting with the complete set of Todos, and an individual
       Todo by ID. *)

    let todos_ =
            State.todos_

    let todo_ id =
            todos_
        >-> Map.value_ id

    (* Processor

       A simple processor loop with internal logic to handle responding to
       our communication protocol, and a mailbox processor to provide a global
       running instance of the processor loop. *)

    let processor (mailbox: MailboxProcessor<Protocol>) =

        (* Reply

           Simple returning reply function. *)

        let reply (channel: AsyncReplyChannel<_>) x =
            channel.Reply x
            x

        (* Operations

           Individual operations over state, returning the appropriate value
           asynchronously. *)

        let add channel newTodo =
            Todo.create newTodo
            |> reply channel
            |> fun x -> Optic.set (todo_ x.Id) (Some x)

        let clear channel =
            ()
            |> reply channel
            |> fun _ -> Optic.set todos_ Map.empty

        let delete channel id =
            ()
            |> reply channel
            |> fun _ -> Optic.set (todo_ id) None

        let get channel id state =
            Optic.get (todo_ id) state
            |> reply channel
            |> fun _ -> state

        let list channel state =
            Optic.get todos_ state
            |> Map.toList
            |> List.map snd
            |> reply channel
            |> fun _ -> state

        let update channel id patchTodo state =
            Optic.get (todo_ id) state
            |> Option.get
            |> fun x -> (function | Some t -> { x with Title = t } | _ -> x) patchTodo.Title
            |> fun x -> (function | Some o -> { x with Order = Some o } | _ -> x) patchTodo.Order
            |> fun x -> (function | Some c -> { x with Completed = c } | _ -> x) patchTodo.Completed
            |> reply channel
            |> fun x -> Optic.set (todo_ id) (Some x) state

        (* Loop

           Processing loop for receiving commands and dispatching asynchrnously
           based on the defined domain protocol. *)

        let rec loop (state: State) =
            async.Bind (mailbox.Receive (),
                function | Add (channel, newTodo) -> loop (add channel newTodo state)
                         | Clear (channel) -> loop (clear channel state)
                         | Delete (channel, id) -> loop (delete channel id state)
                         | Get (channel, id) -> loop (get channel id state)
                         | List (channel) -> loop (list channel state)
                         | Update (channel, id, patchTodo) -> loop (update channel id patchTodo state))

        loop State.empty

    let state =
        MailboxProcessor.Start (processor)

    (* API

       The usable API of our domain model, consisting of simple functions
       interacting with the global state. *)

    let add newTodo =
        state.PostAndAsyncReply (fun channel -> Add (channel, newTodo))

    let clear () =
        state.PostAndAsyncReply (fun channel -> Clear (channel))

    let delete id =
        state.PostAndAsyncReply (fun channel -> Delete (channel, id))

    let get id =
        state.PostAndAsyncReply (fun channel -> Get (channel, id))

    let list () =
        state.PostAndAsyncReply (fun channel -> List (channel))

    let update (id, patchTodo) =
        state.PostAndAsyncReply (fun channel -> Update (channel, id, patchTodo))

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

    (* Route

       Values associated with the route, in this case the ID of the todo in
       question. As we only ever call use this value when a route containing an
       ID has been matched, we take a shortcut and map to Option.get to avoid
       propagating the option value. *)

    let id =
        Freya.memo (Option.get <!> Freya.Optic.get (Route.atom_ "id" >?> guid_))

    (* Operations

       Mapping of contextual information (properties of the request, such as
       the ID and payload (where applicable) mapped to functions of the
       domain.

       All functions are wrapped in a memoization function, so that they may be
       used multiple times per request, but only evaluated once. *)

    let add =
        freya {
            let! p = payload()
            return! Freya.fromAsync(Domain.add p.Value)
        }
        |> Freya.memo

    let clear =
        Freya.memo (Freya.fromAsync (Domain.clear ()))

    let delete =
        freya {
            let! i = id
            return! Freya.fromAsync(Domain.delete i)
        }
        |> Freya.memo

    let get =
        freya {
            let! i = id
            return! Freya.fromAsync(Domain.get i)
        }
        |> Freya.memo

    let list =
        Freya.memo (Freya.fromAsync (Domain.list ()))

    let update =
        freya {
            let! i = id
            let! p = payload()
            return! Freya.fromAsync(Domain.update(i, p.Value))
        }
        |> Freya.memo

    (* Resources

       We use Freya HTTP machines to model our two resources, a collection of
       Todo items, and an individual Todo item. Each resource has appropriate
       properties defined, and each includes CORS support (using default CORS
       configuration).

       Additionally the individual Todo resource also includes PATCH support,
       and supports a PATCH action. *)

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

    let root =
        todoRouter
