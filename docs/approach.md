## Approach

In the age of AI, we're building things a bit differently. Historically, a
project this size took one of two paths: it started as a passion project that
grew slowly over years through community effort, or a company formed around it
and hired a large team. Browsers have almost always been the latter — the
purview of large organizations like Apple, Google, and Mozilla.

We think that's going to change. What was impossible — or laughable — five years
ago is now entirely achievable with a very small team. Here's how we plan to do
it.

### Phase A: Zero to One

This phase is all about getting the browser working. We'll take shortcuts, it'll
be messy, and we'll lean heavily on AI agents.

Take the evolution of SpaceX's Raptor engine as an example. See Raptor 1? That's
our target for this phase. Raptor 2 and Raptor 3? That's where we want to go.

![Raptor engine comparison](docs/raptor-engine-comparison.jpeg)

Thankfully, the browser and web ecosystem is mature. There are tons of
specifications and tests, and none of it is new — implementing an existing spec
with little need for innovation is exactly the kind of work AI is suited for. So
a lot of this phase is spent herding ~~cats~~ agents into implementing specs and
passing tests, plus plenty of manual testing, actually using the product, and —
to a lesser extent — reviewing code.

Okay — you might be asking: how the hell are you reviewing all this code? Well,
we're not. Not all of it, anyway. We review the themes, the overall
organization, and the foundation we're building on, rather than every line.

### Phase 1 and Beyond
Test, fail, simplify, and scale. This is where refinement and innovation continue to happen.
