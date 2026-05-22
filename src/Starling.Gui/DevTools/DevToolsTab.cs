namespace Starling.Gui.DevTools;

public enum DevToolsTab { Performance, Console, Internals, Inspect, Network }

/// <summary>
/// Where the DevTools panel lives relative to the page. The three docked
/// positions share the main window's middle area via a splitter; <see cref="Floating"/>
/// re-hosts the panel in its own top-level window.
/// </summary>
public enum DevToolsDock { Right, Left, Bottom, Floating }
