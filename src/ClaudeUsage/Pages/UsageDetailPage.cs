using System;
using ClaudeUsage.Core;
using ClaudeUsage.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage;

/// <summary>The flyout: full limits table, local token stats, and Refresh/Open/Settings commands.</summary>
internal sealed partial class UsageDetailPage : ContentPage
{
    private readonly UsageStore _store;
    private readonly SettingsManager _settings;

    public UsageDetailPage(UsageStore store, SettingsManager settingsManager)
    {
        _store = store;
        _settings = settingsManager;

        Id = "ClaudeUsage.page.detail";
        Name = "Open";
        Title = "Claude Usage";
        Icon = new IconInfo(string.Empty);

        Commands =
        [
            new CommandContextItem(new RefreshCommand(_store)),
            new CommandContextItem(new OpenUrlCommand("https://claude.ai/settings/usage")
            {
                Name = "Open usage on claude.ai",
            }),
            new CommandContextItem(settingsManager.Settings.SettingsPage),
        ];

        _store.Changed += (_, _) => RaiseItemsChanged();
    }

    public override IContent[] GetContent() =>
    [
        new MarkdownContent(MarkdownReport.Build(_store.Snapshot, _store.GetLocalStats(), DateTimeOffset.UtcNow, _settings.RefreshInterval)),
    ];
}

#pragma warning disable SA1402 // File may only contain a single type

/// <summary>Manually re-polls the usage endpoint; the store throttles this to at most once every 60s.</summary>
internal sealed partial class RefreshCommand : InvokableCommand
{
    private readonly UsageStore _store;

    public RefreshCommand(UsageStore store)
    {
        _store = store;
        Name = "Refresh";
        Icon = new IconInfo("");
    }

    public override CommandResult Invoke()
    {
        _store.RefreshNow();
        return CommandResult.KeepOpen();
    }
}

#pragma warning restore SA1402 // File may only contain a single type
