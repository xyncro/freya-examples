module TodoBackend.Program

open System
open Freya.Core

(* Katana

   Katana (Owin Self Hosting) expects us to expose a type with a specific
   method. Freya lets us do see easily, the OwinAppFunc module providing
   functions to turn any Freya<'a> function in to a suitable value for
   OWIN compatible hosts such as Katana. *)

type TodoBackend () =
    member __.Configuration () =
        OwinAppFunc.ofFreya (Api.todoRoutes)

(* Main
   A very simple program, simply a console app, with a blocking read from
   the console to keep our server from shutting down immediately. Though
   we are self hosting here as a console application, the same application
   should be easily transferrable to any OWIN compatible server, including
   IIS. *)

open Microsoft.Owin.Hosting

[<EntryPoint>]
let main _ = 
    let _ = WebApp.Start<TodoBackend> ("http://localhost:7000")
    let _ = Console.ReadLine ()
    0