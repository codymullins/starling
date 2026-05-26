---
id: WPT-05
title: Attr / Document.createAttribute(NS) / NamedNodeMap
status: in_progress
area: wpt / dom
baseline: 27.79% (1459/5250, dom,css,url, sha-pinned, post-WP-01)
---

## Goal
Expose Attr-as-Node fully: `document.createAttribute(name)` /
`createAttributeNS(ns, qname)`, `Attr` as a window interface constructor with
`name`/`localName`/`namespaceURI`/`prefix`/`value`/`ownerElement` properties,
and `NamedNodeMap` (the live attribute collection) with
`length`/`item(i)`/`getNamedItem(name)`/`getNamedItemNS`/`setNamedItem(attr)`/
`setNamedItemNS`/`removeNamedItem(name)`/`removeNamedItemNS` (per DOM §4.9).

## Why these tests fail today (measured)
- `missing-method:createAttribute` 46 — e.g. `dom/attributes-are-nodes.html`.
- `dom/attributes-are-nodes.html` 0/4 (entirely failing on Attr-as-Node semantics).
- Downstream assert_equals tail across `dom/nodes` Attribute-* files.

Predicted Δ: **~40** (createAttribute + cascade in attributes-are-nodes +
adjacent attr-named-* files).

## Scope (in)
1. **`Document.createAttribute(localName)`** — name validation (per WP-01
   pattern using existing validation if applicable); returns a detached Attr
   node owned by the document, no namespace.
2. **`Document.createAttributeNS(namespace, qualifiedName)`** — prefix/local
   split + validation (NamespaceError on invalid qualified-name combos, mirror
   `createElementNS` rules from commit `ab947c0`).
3. **`Attr` as a window interface constructor** with prototype carrying:
   `name` (qualified), `localName`, `namespaceURI`, `prefix`, `value`
   (get/set — set updates the owner element if attached), `ownerElement`,
   `specified` (always `true` for compat).
4. **`NamedNodeMap`** as a JS object on `element.attributes` — live, indexed
   access, `.length`, `.item(i)`, `getNamedItem`/`getNamedItemNS`/
   `setNamedItem`/`setNamedItemNS`/`removeNamedItem`/`removeNamedItemNS`.
   Named property access (`element.attributes.id`) per §4.9.1.

## Scope (out)
- Hand-off coordination with WPT-03 on the *constructor object*: if WPT-03
  lands first, just install `.prototype`. If this lands first, install the
  ctor too. SME (Cody) will resolve at integration.
- Full Element attribute API rewrite — Attr nodes already exist internally
  (per WP-01 recon); this WP exposes the missing surface.

## Acceptance
- Measured Δ on full suite; report `pass X→Y` and `attributes-are-nodes.html`
  pass count.
- No regression in dom/nodes (Element.attributes is heavily used).
- MSTest: createAttribute/createAttributeNS validation + name spec tests,
  Attr.value updates owner element, NamedNodeMap live behaviour
  (mutate via setAttribute, observe via attributes.item).
- PLAN.md status log; WP doc → `complete`.

## Notes (recon)
- Existing Attr type: `src/Starling.Dom/` (confirm via search; namespaces already
  on Attr per WP-01 recon).
- `Element.attributes` may already exist as some collection — search before
  designing. If it's a non-live array, replace with a live NamedNodeMap.
- Binding pattern: WP-01 / `EventTargetBinding.DefineAccessor/DefineMethod` +
  `DomWrappers`.
