// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    private const string DefaultRefreshMinutes = "5";

    private static readonly string _namespace = "ClaudeUsage";

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    private readonly ChoiceSetSetting _refreshInterval = new(
        Namespaced(nameof(RefreshInterval)),
        "Refresh interval",
        "How often to check Claude subscription limits",
        [
            new ChoiceSetSetting.Choice("1 minute", "1"),
            new ChoiceSetSetting.Choice("2 minutes", "2"),
            new ChoiceSetSetting.Choice("5 minutes", DefaultRefreshMinutes),
            new ChoiceSetSetting.Choice("10 minutes", "10"),
            new ChoiceSetSetting.Choice("15 minutes", "15"),
            new ChoiceSetSetting.Choice("30 minutes", "30"),
        ]);

    /// <summary>Raised whenever a persisted setting changes (refresh interval, etc.).</summary>
    public event EventHandler? RefreshIntervalChanged;

    public TimeSpan RefreshInterval
    {
        get
        {
            var raw = _refreshInterval.Value;
            return int.TryParse(raw, out var minutes) && minutes > 0
                ? TimeSpan.FromMinutes(minutes)
                : TimeSpan.FromMinutes(int.Parse(DefaultRefreshMinutes, CultureInfo.InvariantCulture));
        }
    }

    internal static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("ClaudeUsage");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_refreshInterval);

        // Load settings from file upon initialization
        LoadSettings();

        Settings.SettingsChanged += (_, _) =>
        {
            SaveSettings();
            RefreshIntervalChanged?.Invoke(this, EventArgs.Empty);
        };
    }
}
