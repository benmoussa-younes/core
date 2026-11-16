// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Presenters;
using Core.Mvp.Views;

namespace Core.Mvp.Composition;

/// <summary>
/// The two navigation shapes of the shell. An application chooses one at
/// composition; the chrome, toolbar, secondary bar and overlays are unchanged.
/// </summary>
/// <remarks>
/// <para><b>Tabs</b> — several interchangeable views (documents, sheets). The
/// shell shows a navigation strip and swaps the contextual toolbar as the
/// active tab changes.</para>
/// <para><b>Stack</b> — a drill-in flow (a list, then a detail page). The header
/// shows a back affordance and the toolbar follows the top page.</para>
/// </remarks>
public interface INavigationHost
{
    /// <summary>Gets the active view, or <see langword="null"/> when none is open.</summary>
    IView? Active { get; }

    /// <summary>Opens a tab, or selects it when it is already open (tabs mode).</summary>
    /// <param name="id">The stable tab identifier.</param>
    /// <param name="title">The tab title.</param>
    /// <param name="view">The view.</param>
    void OpenTab(string id, string title, IView view);

    /// <summary>
    /// Opens a tab and binds its presenter's lifecycle to it: the presenter is
    /// initialized once, activated when the tab becomes active, deactivated when
    /// another tab takes over and disposed when the tab closes.
    /// </summary>
    /// <param name="id">The stable tab identifier.</param>
    /// <param name="title">The tab title.</param>
    /// <param name="presenter">The presenter that owns the view.</param>
    /// <remarks>
    /// A host that does not bind presenter lifecycles keeps this default, which
    /// refuses the call: silently showing the view would leave the presenter
    /// uninitialized and never disposed. The shell overrides this member and owns
    /// initialize/activate/dispose.
    /// </remarks>
    /// <exception cref="NotSupportedException">The host does not bind presenter lifecycles.</exception>
    void OpenTab(string id, string title, IViewPresenter presenter) =>
        throw new NotSupportedException("This host does not bind presenter lifecycles.");

    /// <summary>Closes a tab (tabs mode); its presenter, when one is bound, is disposed.</summary>
    /// <param name="id">The tab identifier.</param>
    /// <remarks>
    /// The presenter lifecycle is asynchronous. The call returns after the
    /// navigation state has changed; await <see cref="WhenIdleAsync"/> when the
    /// caller must observe the completed initialize/activate/deactivate/dispose
    /// queue for the presenters it just touched.
    /// </remarks>
    void CloseTab(string id);

    /// <summary>
    /// Completes when the presenter lifecycle operations queued by this host have
    /// finished. A synchronous navigation call returns before an asynchronous
    /// presenter hook completes; this is the barrier for a caller that must wait.
    /// </summary>
    /// <returns>A value task that completes when the current lifecycle queue is idle.</returns>
    ValueTask WhenIdleAsync() => ValueTask.CompletedTask;

    /// <summary>Pushes a page onto the stack (stack mode).</summary>
    /// <param name="title">The page title.</param>
    /// <param name="subtitle">The contextual subtitle.</param>
    /// <param name="view">The page view.</param>
    void Push(string title, string subtitle, IView view);

    /// <summary>Pushes a page and binds its presenter's lifecycle to the stack.</summary>
    /// <param name="title">The page title.</param>
    /// <param name="subtitle">The contextual subtitle.</param>
    /// <param name="presenter">The presenter that owns the view.</param>
    /// <remarks>
    /// A host that does not bind presenter lifecycles keeps this default, which
    /// refuses the call: silently showing the view would leave the presenter
    /// uninitialized and never disposed. The shell overrides this member and owns
    /// initialize/activate/dispose.
    /// </remarks>
    /// <exception cref="NotSupportedException">The host does not bind presenter lifecycles.</exception>
    void Push(string title, string subtitle, IViewPresenter presenter) =>
        throw new NotSupportedException("This host does not bind presenter lifecycles.");

    /// <summary>Pops the top page (stack mode).</summary>
    void Pop();
}
