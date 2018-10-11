module Api

open Freya.Core
open Freya.Machines.Http
open Freya.Types.Http
open Freya.Routers.Uri.Template

(* Code

   A very simple Freya example! Two simple functions to see whether the client
   has supplied a name, and if so to use it in place of "World" in the classic
   greeting, a machine to deal with all of the complexities of HTTP, and a
   router to make sure suitable requests end up in the right place. *)

let name_ = Route.atom_ "name"

let name =
    freya {
        let! nameO = Freya.Optic.get name_

        match nameO with
        | Some name -> return name
        | None -> return "World" }

let sayHello =
    freya {
        let! name = name

        return Represent.text (sprintf "Hello, %s!" name) }

let helloMachine =
    freyaMachine {
        methods [GET; HEAD; OPTIONS]
        handleOk sayHello }

let root =
    freyaRouter {
        resource "/hello{/name}" helloMachine }
