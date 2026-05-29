namespace Starling.Css.Animations;

/// <summary>
/// A document's long-lived animation state: the <see cref="AnimationEngine"/>
/// (declarative <c>@keyframes</c> + Web Animations API playback), the
/// <see cref="TransitionEngine"/>, and the <see cref="AnimationCompositor"/>
/// that overlays their samples on the static cascade.
/// <para>
/// These three used to be owned by the <see cref="Cascade.StyleEngine"/>, which
/// is rebuilt on every layout pass — so each relayout dropped all playback state
/// and the engine had to re-import script animations and re-prime declarative
/// ones. Bundling them here lets a caller (the painter) keep one timeline per
/// document and hand the same instance to every <see cref="Cascade.StyleEngine"/>
/// it builds for that document, so animation playback survives relayouts.
/// </para>
/// <para>
/// The keyframe registry inside <see cref="Animations"/> is the one piece that
/// is rebuilt each layout (it tracks the current stylesheet set). The
/// <see cref="Cascade.StyleEngine"/> constructor clears it before re-registering
/// from the attached sheets, which leaves the playback state (active instances,
/// script animations, transition snapshots) untouched.
/// </para>
/// </summary>
public sealed class AnimationTimeline
{
    public AnimationEngine Animations { get; } = new();
    public TransitionEngine Transitions { get; } = new();
    public AnimationCompositor Compositor { get; }

    public AnimationTimeline()
    {
        Compositor = new AnimationCompositor(Animations, Transitions);
    }
}
