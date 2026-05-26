# WPT-05 — Attr / Document.createAttribute(NS) / NamedNodeMap

status: complete

## Scope

Implement DOM §4.9 — Attr as a Node, NamedNodeMap as the live attribute collection,
and the `Document.createAttribute`/`createAttributeNS` factory methods.

## Deliverables

- `AttrNode : Node` class (`src/Starling.Dom/AttrNode.cs`)
- `NamedNodeMap` migrated to `List<AttrNode>` (`src/Starling.Dom/NamedNodeMap.cs`)
- `Document.createAttribute` + `createAttributeNS` (`src/Starling.Dom/Document.cs`)
- `Document.IsHtml` flag for case behaviour
- `JsNamedNodeMapObject` exotic object (`src/Starling.Bindings/DomWrappers.cs`)
- `InstallAttr` + `InstallNamedNodeMap` (`src/Starling.Bindings/NodeBindings.cs`)
- Bonus: `getAttributeNode`/NS, `setAttributeNode`/NS, `removeAttributeNode`,
  `toggleAttribute`, `hasAttributes`, `getAttributeNames`
- `HierarchyRequestError` guard in `appendChild`/`insertBefore`/`replaceChild`
- 29 DOM-layer unit tests (`tests/Starling.Dom.Tests/AttrNodeTests.cs`)
- 38 JS binding unit tests (`tests/Starling.Bindings.Tests/AttrBindingTests.cs`)

## Results

| Metric | Before | After | Delta |
|--------|--------|-------|-------|
| WPT pass count | 1459 | 1604 | +145 |
| WPT pass rate | 27.79% | 30.60% | +2.81pp |

Key files now 100%:
- `dom/nodes/attributes-are-nodes.html` 4/4
- `dom/nodes/Document-createAttribute.html` 36/36
- `dom/nodes/attributes-namednodemap.html` 8/8
- `dom/nodes/namednodemap-supported-property-names.html` 3/3

## Key design decisions

- WHATWG DOM `createAttribute` accepts ANY non-empty string (no XML Name validation).
- `createAttribute` for HTML documents lower-cases the name in the JS binding layer;
  the C# `Document.CreateAttribute` method preserves case.
- `AttrNode.LocalName` for the non-namespaced path equals the full `Name` (no colon
  split); only `createAttributeNS` uses prefix/local splitting.
- Identity preservation: `Element.SetAttribute` mutates existing `AttrNode` in-place
  so JS references remain valid.
- `NamedNodeMap.length` is on the prototype (accessor), NOT an own property, so
  `Object.getOwnPropertyNames(map)` returns only `["0","1","id","class",...]`.
- Named property access (`map["id"]`) is implemented via `GetOwnPropertyDescriptor`
  override (not `Get`), because the VM's `AbstractOperations.Get` uses the descriptor
  chain internally.
