namespace Tessera.Dom;

/// <summary>
/// Attribute is no longer a Node since DOM4 (2013); it is modeled as a value
/// owned by an element. See 05_DOM.md §Node hierarchy.
/// </summary>
public readonly record struct Attr(string Name, string Value, string? Namespace = null);
