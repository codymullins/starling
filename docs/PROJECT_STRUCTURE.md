# Project structure

This file is a small map of the Starling repo. It gives new readers the main
folders and shows how the browser parts fit together.

## Top-level folders

| Path | What it is for |
|---|---|
| `src/` | Browser engine code, apps, and shared host code. |
| `tests/` | Tests for each main project in `src/`. |
| `browser-plan/` | Design notes and the roadmap. |
| `tasks/` | Work packages for agents. |
| `bench/` | Benchmarks for speed checks. |
| `testdata/` | Test pages, fixtures, and golden files. |
| `tools/` | Small helper tools for the repo. |
| `lib/` | External code that is checked out with the repo. |

## Main `src/` projects

| Project | Role |
|---|---|
| `Starling.Engine` | Connects the browser parts into a page engine. |
| `Starling.Html` | Parses HTML into nodes. |
| `Starling.Dom` | Holds the page tree, like documents and elements. |
| `Starling.Css` | Parses CSS and matches selectors. |
| `Starling.Layout` | Computes where boxes go on the page. |
| `Starling.Paint` | Draws the page. |
| `Starling.Js` | Runs the Starling JS engine. |
| `Starling.Bindings` | Connects JS objects to Starling DOM and CSS code. |
| `Starling.Net` | Fetches data over the network. |
| `Starling.Url` | Parses and resolves URLs. |
| `Starling.Headless` | Runs Starling from the command line. |
| `Starling.Gui` | Runs the desktop GUI shell. |
| `Starling.AppHost` | Starts the app pieces together with Aspire. |

## How bindings fit in

`Starling.Bindings` is the bridge between the Starling JS engine and browser
objects.

For example, page code can call:

```js
document.createElement("p")
```

The JS engine sees a JS call. `Starling.Bindings` turns that into a call to
`Starling.Dom`, which makes the real element.

Page code can also call:

```js
document.querySelector(".card")
```

`Starling.Bindings` takes the selector string. It asks `Starling.Css` to match
that selector against the Starling DOM tree.

So the short version is:

```text
JS code -> Starling.Bindings -> Starling.Dom
JS code -> Starling.Bindings -> Starling.Css
```

The projects stay separate. `Starling.Bindings` makes them feel like one
browser to page scripts.
