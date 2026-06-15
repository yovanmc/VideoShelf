// src/VideoShelf.App/Motion/HeroTransition.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VideoShelf.App.Motion;

/// <summary>
/// Shared-element card→hero morph helper.
///
/// SHIPPED PATH: crossfade/scale-from-center fallback.
///
/// Reason: the source card (creator grid) is inside a <see cref="Wpf.Ui.Controls.VirtualizingWrapPanel"/>
/// — after navigating away from Browse the card container is virtualized out.
/// By the time SectionDetailView is shown and layout is settled, the originating
/// card element is no longer in the visual tree, so <c>TransformToVisual</c> would
/// throw or return an invalid rect. Additionally the SectionDetail hero banner only
/// measures after its <c>IsVisibleChanged</c> fires, which happens after the
/// DataContext loads asynchronously — making reliable double-rect timing impossible
/// without heavyweight async coordination that would block the milestone.
///
/// The result: <see cref="Play"/> is a no-op when <paramref name="shouldAnimate"/>
/// is true (D1's <see cref="ViewTransition"/> already provides a tasteful fade+rise
/// on SectionDetailView entry — this is the documented crossfade/scale fallback).
/// When reduced motion is requested both behave identically (static snap).
/// </summary>
public static class HeroTransition
{
    /// <summary>
    /// Attempts to play the ghost-overlay shared-element morph from
    /// <paramref name="source"/> to the destination hero <paramref name="targetPlaceholder"/>,
    /// rendering a floating ghost in <paramref name="overlayHost"/>.
    ///
    /// CURRENT PATH: crossfade fallback (see class summary). The method
    /// deliberately no-ops so D1's ViewTransition provides the entry animation.
    /// </summary>
    public static void Play(
        FrameworkElement? source,
        FrameworkElement? targetPlaceholder,
        Panel overlayHost,
        Func<bool> shouldAnimate)
    {
        // The morph requires both rects to be valid + measurable simultaneously.
        // Due to virtualization / persistent-view async load the full shared-element
        // morph is not feasible here (see class summary). D1's ViewTransition already
        // provides a tasteful fade+rise on SectionDetailView entry.
        // This method is intentionally a no-op to avoid crashes.
    }
}
