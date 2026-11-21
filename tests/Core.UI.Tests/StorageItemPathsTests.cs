// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.UI.Platform;

namespace Core.UI.Tests;

public sealed class StorageItemPathsTests
{
    [Fact]
    public void A_file_uri_maps_to_its_local_path()
    {
        Assert.Equal("/home/user/report.csv", StorageItemPaths.File(new Uri("file:///home/user/report.csv")));
        Assert.Equal("/home/user/reports", StorageItemPaths.Folder(new Uri("file:///home/user/reports/")));
    }

    [Fact]
    public void A_remote_uri_never_names_a_local_path()
    {
        // GVFS returns these for a mounted share; LocalPath would be "/share" or
        // "/etc", a local directory that is not the one the operator picked.
        Assert.Null(StorageItemPaths.File(new Uri("smb://server/share/file.txt")));
        Assert.Null(StorageItemPaths.Folder(new Uri("smb://server/share")));
        Assert.Null(StorageItemPaths.Folder(new Uri("sftp://host/etc")));
        Assert.Null(StorageItemPaths.Folder(new Uri("http://host/etc")));
    }

    [Fact]
    public void A_unc_file_uri_never_names_a_local_path()
    {
        // file://server/share is IsFile, but its LocalPath is a Windows UNC path.
        Assert.Null(StorageItemPaths.File(new Uri("file://server/share/file.txt")));
        Assert.Null(StorageItemPaths.Folder(new Uri("file://server/share")));
        Assert.Null(StorageItemPaths.Folder(new Uri("file://localhost/home/user")));
    }

    [Fact]
    public void A_relative_uri_never_names_a_local_path()
    {
        var relative = new Uri("share/reports", UriKind.Relative);

        Assert.Null(StorageItemPaths.File(relative));
        Assert.Null(StorageItemPaths.Folder(relative));
    }

    [Fact]
    public void A_file_folder_keeps_its_root()
    {
        Assert.Equal("/", StorageItemPaths.Folder(new Uri("file:///")));
    }

    [Fact]
    public void A_null_uri_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => StorageItemPaths.File(null!));
        Assert.Throws<ArgumentNullException>(() => StorageItemPaths.Folder(null!));
    }
}
