module Dash.NET.POC.App

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe


open Dash.NET
open Plotly.NET


module Helpers = 

    ///returns a choropleth plot that has the input country highlighted.
    let createWorldHighlightFigure countryName =
        Chart.ChoroplethMap(locations=[countryName],z=[100],Locationmode = StyleParam.LocationFormat.CountryNames)
        |> Chart.withMapStyle(
            ShowLakes=true,
            ShowOcean=true,
            OceanColor="lightblue",
            ShowRivers=true
        )
        |> Chart.withSize (1000.,1000.)
        |> Chart.withLayout (Layout.init(Paper_bgcolor="rgba(0,0,0,0)",Plot_bgcolor="rgba(0,0,0,0)"))
        |> GenericChart.toFigure

//----------------------------------------------------------------------------------------------------
//============================================== LAYOUT ==============================================
//----------------------------------------------------------------------------------------------------

//The layout describes the components that Dash will render for you. 
open Dash.NET.Html // this namespace contains the standard html copmponents, such as div, h1, etc.
open Dash.NET.DCC  // this namespace contains the dash core components, the heart of your Dash.NET app.

open ComponentPropTypes

//Note that this layout uses css classes defined by Bulma (https://bulma.io/), which gets defined as a css dependency in the app section below.
let dslLayout = 
    Html.div [
        Attr.className "section"
        Attr.id "main-section" //the style for 'main-section' is actually defined in a custom css that you can serve with the dash app.
        Attr.children [
            Html.h1 [
                Attr.className "title has-text-centered"
                Attr.children "Hello Dash from F#"
            ]
            Html.div [
                Attr.className "content"
                Attr.children [
                    Html.p [
                        Attr.className "has-text-centered"
                        Attr.children "This is a simple example Dash.NET app that contains an input component, A world map graph, and a callback that highlights the country you type on that graph."
                    ]
                ]
            ]
            Html.div [
                Attr.className "container"
                Attr.children [
                    Html.h4 [ 
                        Attr.children "type a country name to highlight (Press enter to update)"
                    ]
                    Input.input "country-selection" [
                        Input.ClassName "input is-primary"
                        Input.Type InputType.Text
                        Input.Value "Germany"
                        Input.Debounce true
                    ] []
                ]
            ]
            Html.div [
                Attr.className "container"
                Attr.children [
                    Graph.graph "world-highlight" [
                        Graph.ClassName "graph-style" 
                        Graph.Figure (Helpers.createWorldHighlightFigure "Germany")
                    ] []
                ]
            ]
        ]
    ]

//----------------------------------------------------------------------------------------------------
//============================================= Callbacks ============================================
//----------------------------------------------------------------------------------------------------

//Callbacks define how your components can be updated and update each other. A callback has one or 
//more Input components (defined by their id and the property that acts as input) and an output 
//component (again defined by its id and output property). Additionally, a function that handles the 
//input and returns the desired output is needed.

///This callback takes the 'value' property of the component with the 'country-selection' id, and 
///returns a map chart that will update the 'figure' property of the component with the 
///'world-highlight' id
open Dash.NET.Operators


let countryHighlightCallback =
    Callback.singleOut(
        "country-selection" @. Value,
        "world-highlight" @. (CustomProperty "figure"),
        (fun (countryName:string) -> 
            "world-highlight" @. (CustomProperty "figure") => (countryName |> Helpers.createWorldHighlightFigure)
        )
    )

//----------------------------------------------------------------------------------------------------
//============================================= The App ==============================================
//----------------------------------------------------------------------------------------------------

//The 'DashApp' type is your central DashApp that contains all settings, configs, the layout, styles, 
//scripts, etc. that makes up your Dash.NET app. 

let myDashApp =
    DashApp.initDefault() // create a Dash.NET app with default settings
    |> DashApp.withLayout dslLayout // register the layout defined above.
    |> DashApp.appendCSSLinks [ 
        "main.css" // serve your custom css
        "https://cdnjs.cloudflare.com/ajax/libs/bulma/0.9.1/css/bulma.min.css" // register bulma as an external css dependency
    ]
    |> DashApp.addCallback countryHighlightCallback // register the callback that will update the map


// The things below are Giraffe/ASP:NetCore specific and will likely be abstracted in the future.

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder : CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080")
           .AllowAnyMethod()
           .AllowAnyHeader()
           |> ignore

let configureApp (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IWebHostEnvironment>()
    (match env.EnvironmentName with
    | "Development" -> app.UseDeveloperExceptionPage()
    | _ -> app.UseGiraffeErrorHandler(errorHandler))
        .UseHttpsRedirection()
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(DashApp.toHttpHandler myDashApp)

let configureServices (services : IServiceCollection) =
    services.AddCors()    |> ignore
    services.AddGiraffe() |> ignore

let configureLogging (builder : ILoggingBuilder) =
    builder.AddFilter(fun l -> l.Equals LogLevel.Debug)
           .AddConsole()
           .AddDebug() |> ignore

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    Host.CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(
            fun webHostBuilder ->
                webHostBuilder
                    .UseContentRoot(contentRoot)
                    .UseWebRoot(webRoot)
                    .Configure(Action<IApplicationBuilder> configureApp)
                    .ConfigureServices(configureServices)
                    .ConfigureLogging(configureLogging)
                    |> ignore)
        .Build()
        .Run()
    0