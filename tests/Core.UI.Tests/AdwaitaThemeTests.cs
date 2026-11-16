// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Adwaita;

namespace Core.UI.Tests;

public sealed class AdwaitaThemeTests
{
    [Fact]
    public void The_metrics_follow_the_hig()
    {
        Assert.Equal(48, GnomeMetrics.HeaderBarHeight);
        Assert.Equal(6, GnomeMetrics.HeaderBarSpacing);
        Assert.Equal(6, GnomeMetrics.HeaderBarHorizontalPadding);
        Assert.Equal(34, GnomeMetrics.ButtonSize);
        Assert.Equal(6, GnomeMetrics.ButtonCornerRadius);
    }
}
