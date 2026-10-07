using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Services;
using ImageMagick;

namespace CinnabarSharp.Desktop.ViewModels;

// Work that survives a crash: a copy of every document with unsaved changes, and the offer to restore them at the next start.
public partial class MainViewModel
{
    private readonly Dictionary<Guid, (int Pointer, int Count)> _recoveryState = [];
    private DispatcherTimer? _recoveryTimer;

    /// <summary>Set by the app; null disables the feature (tests that do not need it).</summary>
    public RecoveryStore? Recovery { get; set; }

    /// <summary>How often copies are refreshed.</summary>
    public static TimeSpan AutosaveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Starts this session's copies folder and the timer that keeps them up to date.</summary>
    public void StartRecoverySession(bool timer = true)
    {
        if (Recovery is null)
            return;
        try
        {
            Recovery.Begin();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("recovery", "Could not start the recovery session.", e);
            Recovery = null;
            return;
        }
        if (timer)
        {
            _recoveryTimer = new DispatcherTimer { Interval = AutosaveInterval };
            _recoveryTimer.Tick += (_, _) => AutosaveNow();
            _recoveryTimer.Start();
        }
    }

    /// <summary>Normal exit: the copies are not needed.</summary>
    public void EndRecoverySession()
    {
        _recoveryTimer?.Stop();
        Recovery?.End();
    }

    /// <summary>
    /// Writes a copy of each document that changed since its last copy, deletes the copies of documents that were saved or
    /// closed. Quick when nothing changed; never throws.
    /// </summary>
    public void AutosaveNow()
    {
        if (Recovery is null || IsBusy)
            return;
        var open = new HashSet<Guid>();
        foreach (var tab in Documents)
        {
            var doc = tab.Document;
            open.Add(doc.Id);
            try
            {
                if (!doc.IsDirty)
                {
                    if (Recovery.Has(doc))
                        Recovery.Remove(doc.Id);
                    _recoveryState.Remove(doc.Id);
                    continue;
                }
                var history = doc.Workspace.History;
                var key = (history.Pointer, history.Items.Count);
                if (_recoveryState.TryGetValue(doc.Id, out var last) && last == key)
                    continue;
                Recovery.Save(doc);
                _recoveryState[doc.Id] = key;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or MagickException or NotSupportedException or InvalidOperationException)
            {
                AppLog.Error("recovery", $"Could not keep a copy of \"{doc.DisplayName}\".", e);
            }
        }
        foreach (var id in Recovery.Written.Where(id => !open.Contains(id)))
        {
            Recovery.Remove(id);
            _recoveryState.Remove(id);
        }
    }

    /// <summary>
    /// At start: if the last session left copies of unsaved documents, offers to open them again. They come back as
    /// documents with unsaved changes (under their original name when they had a file, else "… (recovered)").
    /// </summary>
    public async Task OfferRecoveryAsync()
    {
        if (Recovery is null || Dialogs is null)
            return;
        var found = Recovery.FindRecoverable();
        if (found.Count == 0)
            return;
        var list = string.Join("\n", found.Take(8).Select(d => "• " + d.Name)) + (found.Count > 8 ? $"\n… and {found.Count - 8} more" : "");
        var restore = await Dialogs.ConfirmAsync("Restore unsaved work?",
            $"CinnabarSharp did not close properly last time. {(found.Count == 1 ? "This document" : "These documents")} had unsaved changes:\n\n{list}",
            "Restore");
        var failed = new HashSet<string>();
        if (restore)
        {
            foreach (var item in found)
            {
                try
                {
                    Restore(item);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or MagickException or NotSupportedException or ArgumentException)
                {
                    failed.Add(item.SessionFolder);
                    AppLog.Error("recovery", $"Could not restore \"{item.Name}\".", e);
                    await Dialogs.ShowErrorAsync($"Could not restore \"{item.Name}\"", Describe(e));
                }
            }
        }
        // A session with a copy that could not be opened keeps its folder, so nothing is lost for good.
        Recovery.Discard(found.Where(d => !failed.Contains(d.SessionFolder)));
        AutosaveNow();
    }

    private void Restore(RecoverableDocument item)
    {
        var doc = _formats.Open(new FileInfo(item.DataFile));
        // The copy is a working file: the document goes back to where it came from, and counts as unsaved.
        if (item.OriginalPath is { } original)
        {
            doc.File = new FileInfo(original);
            doc.FileType = Path.GetExtension(original).TrimStart('.').ToLowerInvariant();
        }
        else
        {
            doc.File = null;
            doc.FileType = null;
            doc.DisplayName = item.Name.EndsWith("(recovered)", StringComparison.Ordinal) ? item.Name : item.Name + " (recovered)";
        }
        doc.IsDirty = true;
    }

    /// <summary>What the app does once the window is up: restore lost work, then look for an update.</summary>
    public async Task AfterLaunchAsync()
    {
        StartRecoverySession();
        await OfferRecoveryAsync();
        await StartUpdateCheckAsync();
    }
}
