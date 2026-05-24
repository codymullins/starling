# sites/

Local, build-free test sites served at stable `localhost` paths so the Starling
engine (and a real browser) can load them over plain HTTP.

## Convention

Each subdirectory is one site, served at a path matching its folder name:

```
sites/
  index.html        ->  http://localhost:8088/
  todo/index.html   ->  http://localhost:8088/todo/
```

Drop a new folder with an `index.html` in it and it's available at
`http://localhost:8088/<folder>/` after the next AppHost (re)start — no code
changes, no rebuild.

## How it's served

The Aspire AppHost adds a YARP resource with static-file serving pointed at this
directory (see `Starling.AppHost/AppHost.cs`, the `"sites"` resource). The YARP
integration runs as a container and **copies this folder into the container when
it starts** — it is not a live bind-mount. So adding a site, or editing existing
HTML/CSS/JS, requires restarting the AppHost (or just the `sites` resource from
the Aspire dashboard) for the change to be served.

Run it with:

```bash
dotnet run --project Starling.AppHost
# or: aspire run
```

Then open the `sites` endpoint from the Aspire dashboard, or hit the fixed host
port directly (`http://localhost:8088/`).

> Note: this is distinct from `testdata/sites/`, which holds captured full-page
> HTML fixtures used by tests. `sites/` is for interactive, hand-written demos.
