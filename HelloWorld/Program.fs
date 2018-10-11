module Program

open Freya.Core
open Suave

(* Main

   A very simple program, simply a console app, with a blocking read from
   the console to keep our server from shutting down immediately. Though
   we are self hosting here as a console application, the same application
   should be easily transferrable to any OWIN compatible server, including
   IIS. *)

[<EntryPoint>]
let main argv =
    startWebServer
        defaultConfig
        (Owin.OwinApp.ofAppFunc "/" (OwinAppFunc.ofFreya Api.root))

    0
