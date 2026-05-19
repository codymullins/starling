namespace Tessera.Js.Runtime;

public enum ConsoleLevel
{
    Log,
    Info,
    Warn,
    Error,
    Debug,
    Dir,
    Table,
    Trace,
}

public delegate void ConsoleSink(ConsoleLevel level, string message);
