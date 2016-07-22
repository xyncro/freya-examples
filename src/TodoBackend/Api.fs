module TodoBackend.Api

open Aether.Operators
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

(* Route *)

let id =
    Freya.memo (Option.get <!> Freya.Optic.get (Route.atom_ "id" >?> guid_))

(* Operations *)

let add =
    Freya.memo (payload () >>= fun x -> Freya.fromAsync (x.Value, addTodo))

let clear =
    Freya.memo (Freya.fromAsync ((), clearTodos))

let delete =
    Freya.memo (id >>= fun x -> Freya.fromAsync (x, deleteTodo))

let get =
    Freya.memo (id >>= fun x -> Freya.fromAsync (x, getTodo))

let list =
    Freya.memo (Freya.fromAsync ((), listTodos))

let update =
    Freya.memo (id >>= fun x -> payload () >>= fun y -> Freya.fromAsync ((x, y.Value), updateTodo))

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
   We have our two resources, but they need to have appropriate requests
   routed to them. We route them using the freyaRouter expression, using the
   shorthand "resource" syntax defined in Freya.Machine.Router (simply shorthand
   for "route All". *)

let todoRoutes =
    freyaRouter {
        resource "/" todos
        resource "/{id}" todo }

(* API
   Finally we expose our actual API. In more complex applications than this
   we would expect to see multiple components of the application pipelined
   to form a more complex whole, but in this case we only have our single router. *)

let api =
    todoRoutes