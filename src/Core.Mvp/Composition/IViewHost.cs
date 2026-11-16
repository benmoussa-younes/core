// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Mvp.Views;
using Core.Mvp.Presentation;
using Core.Mvp.Presenters;
using Core.Mvp.Services;

namespace Core.Mvp.Composition;

/// <summary>
/// The application shell a presenter drives: the fixed chrome (title, subtitle,
/// commands, busy) and the shell-level services. The presenter never names a
/// window; it publishes a snapshot and reacts to command activations.
/// </summary>
/// <remarks>
/// The host and its services are UI-thread affine: they mutate the visual tree
/// and read the active view. A background presenter marshals to the UI thread
/// before calling <see cref="Dialogs"/>, <see cref="Notifications"/>,
/// <see cref="Files"/>, <see cref="Folders"/>, <see cref="Clipboard"/>,
/// <see cref="SetSnapshot(ShellSnapshot)"/> or
/// <see cref="SetSnapshot(IViewPresenter, ShellSnapshot)"/>. The tray and
/// instance-lock events are marshalled by <c>CoreHost</c> once a window exists.
/// </remarks>
public interface IViewHost
{
    /// <summary>Gets the dialog service, rendered as shell overlays.</summary>
    IDialogService Dialogs { get; }

    /// <summary>Gets the text prompt service, rendered as a shell overlay.</summary>
    /// <remarks>
    /// A host that does not render prompts implements this member explicitly or
    /// keeps the default, which refuses the operation instead of silently
    /// ignoring it.
    /// </remarks>
    IPromptService Prompts => throw new NotSupportedException("This host does not provide prompts.");

    /// <summary>Gets the notification service (toasts and banners).</summary>
    INotificationService Notifications { get; }

    /// <summary>Gets the file picker (open and save).</summary>
    IFilePicker Files { get; }

    /// <summary>Gets the folder picker.</summary>
    /// <remarks>
    /// A host that does not render a folder picker keeps the default, which
    /// refuses the operation instead of silently ignoring it.
    /// </remarks>
    IFolderPicker Folders => throw new NotSupportedException("This host does not provide a folder picker.");

    /// <summary>Gets the system clipboard (text and images).</summary>
    IClipboardService Clipboard { get; }

    /// <summary>
    /// Raised when the operator activates a command; the arguments are the active
    /// view and the command id.
    /// </summary>
    /// <remarks>
    /// The shell passes the active view, so a presenter compares it with its own
    /// <see cref="IViewPresenter.View"/> and ignores a command that belongs to
    /// another tab. A presenter should still subscribe in <c>OnActivateAsync</c>
    /// and unsubscribe in <c>OnDeactivateAsync</c>: the active-view argument is
    /// the safety net, not a licence to keep a background subscription alive.
    /// </remarks>
    event Action<IView, string> CommandInvoked;

    /// <summary>Applies a shell snapshot to the fixed chrome of the active view.</summary>
    /// <param name="snapshot">The title, subtitle, commands and busy state to render.</param>
    void SetSnapshot(ShellSnapshot snapshot);

    /// <summary>
    /// Publishes a snapshot for the view a presenter owns, without assuming that
    /// the presenter is active. A background presenter uses this overload. A host
    /// that cannot identify the view keeps the default, which refuses the call.
    /// </summary>
    /// <param name="presenter">The presenter that owns the view.</param>
    /// <param name="snapshot">The snapshot to store for that view.</param>
    /// <remarks>
    /// A host that cannot identify the view must not silently repaint the active
    /// tab; it keeps the default, which refuses the operation.
    /// </remarks>
    void SetSnapshot(IViewPresenter presenter, ShellSnapshot snapshot) =>
        throw new NotSupportedException("This host cannot publish a snapshot for a specific presenter.");
}
