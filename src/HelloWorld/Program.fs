module Hello =

    open Freya.Core
    open Freya.Machines.Http
    open Freya.Routers.Uri.Template

    let name =
        freya {
            let! name = Freya.Optic.get (Route.atom_ "name")

            match name with
            | Some name -> return name
            | _ -> return "World" }

    let hello =
        freya {
            let! name = name

            return Represent.text (sprintf "Hello %s!" name) }

    let machine =
        freyaMachine {
            handleOk hello }

    let router =
        freyaRouter {
            resource "/hello{/name}" machine }

module Server =

    open Freya.Core

    (* Katana

       Katana (Owin Self Hosting) expects us to expose a type with a specific
       method. Freya lets us do see easily, the OwinAppFunc module providing
       functions to turn any Freya<'a> function in to a suitable value for
       OWIN compatible hosts such as Katana. *)

    type HelloWorld () =
        member __.Configuration () =
            OwinAppFunc.ofFreya (Hello.router)

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

    let _ = WebApp.Start<Server.HelloWorld> ("http://localhost:7000")
    let _ = Console.ReadLine ()

    0