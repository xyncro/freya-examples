module TodoBackend.Program

open KestrelInterop

// Run http://todobackend.com/specs/index.html?http://localhost:5000/ to test the implementation.

[<EntryPoint>]
let main argv =
    let configureApp =
        ApplicationBuilder.useFreya Api.root

    WebHost.create ()
    |> WebHost.bindTo [|"http://localhost:5000"|]
    |> WebHost.configure configureApp
    |> WebHost.buildAndRun

    0
