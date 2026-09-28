using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Services;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>"Connect an AI Agent": the commands to paste in Claude Code or Claude Desktop, and the live status.</summary>
public partial class AgentConnectionViewModel : ViewModelBase, IDisposable
{
    private readonly AgentConnection _agents;
    private readonly IClipboardService? _clipboard;

    public AgentConnectionViewModel(AgentConnection agents, IClipboardService? clipboard,
        string? processPath = null, string? appAssembly = null)
    {
        _agents = agents;
        _clipboard = clipboard;
        var launch = AgentConnection.LaunchCommand(processPath, appAssembly);
        ClaudeCodeCommand = AgentConnection.ClaudeCodeCommand(launch);
        ClaudeDesktopConfig = AgentConnection.ClaudeDesktopConfig(launch);
        AllowedFolders = string.Join(Environment.NewLine, AgentConnection.AllowedFolders);
        _agents.Changed += OnChanged;
    }

    public string ClaudeCodeCommand { get; }
    public string ClaudeDesktopConfig { get; }
    public string AllowedFolders { get; }
    public string DocsUrl => AgentConnection.DocsUrl;

    public string StatusText => StatusFor(_agents);

    public static string StatusFor(AgentConnection agents) => agents.ConnectedAgents switch
    {
        _ when !agents.IsRunning => "Not accepting AI agents",
        0 => "Waiting for an AI agent",
        1 => "1 AI agent connected",
        var n => $"{n} AI agents connected",
    };

    /// <summary>Confirmation shown next to the Copy buttons ("Copied").</summary>
    [ObservableProperty]
    public partial string? CopiedText { get; set; }

    [RelayCommand]
    private Task CopyClaudeCode() => Copy(ClaudeCodeCommand, "Claude Code command copied");

    [RelayCommand]
    private Task CopyClaudeDesktop() => Copy(ClaudeDesktopConfig, "Claude Desktop configuration copied");

    private async Task Copy(string text, string done)
    {
        if (_clipboard is null)
            return;
        await _clipboard.SetTextAsync(text);
        CopiedText = done;
    }

    private void OnChanged() => OnPropertyChanged(nameof(StatusText));

    public void Dispose() => _agents.Changed -= OnChanged;
}
