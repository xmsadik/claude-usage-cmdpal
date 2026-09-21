// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using ClaudeUsage.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage;

public partial class ClaudeUsageCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly ICommandItem _dockBand;
    private readonly SettingsManager _settingsManager = new();
    private readonly UsageStore _store;
    private readonly UsageBand _usageBand;

    public ClaudeUsageCommandsProvider()
    {
        DisplayName = "Claude Usage";
        Id = "ClaudeUsage";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _store = new UsageStore(_settingsManager);

        var detailPage = new UsageDetailPage(_store, _settingsManager);

        _commands = [
            new CommandItem(detailPage)
            {
                Title = DisplayName,
                Subtitle = "Claude subscription limits and token stats",
                MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
            },
        ];

        _usageBand = new UsageBand(_store, detailPage);

        // Command.Id must be non-empty or the host silently drops the band.
        _dockBand = new WrappedDockItem([_usageBand], "ClaudeUsage.dock.usage", "Claude Usage");

        Settings = _settingsManager.Settings;
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => [_dockBand];

    public override void Dispose()
    {
        _usageBand.Dispose();
        _store.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
