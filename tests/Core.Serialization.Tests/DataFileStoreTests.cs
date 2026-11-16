// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using Core.Serialization.Codecs;
using Core.Platform.Linux;
using Core.Serialization.Documents;
using Core.Serialization.Storage;
using Core.Serialization.Tables;
using Core.Serialization.Values;

namespace Core.Serialization.Tests;

public sealed class DataFileStoreTests : IDisposable
{
    private static readonly LinuxAtomicFileWriterFactory Factory = new();

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "core-data-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void It_refuses_an_empty_directory()
    {
        Assert.Throws<ArgumentException>(() => new DataFileStore(" ", Factory, new LinuxFileSystemAdapter()));
    }

    [Fact]
    public async Task It_refuses_a_file_larger_than_the_maximum()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "big.json"), new string('x', 64));

        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter()) { MaxFileBytes = 8 };

        var loaded = await store.LoadAsync("big.json", cancellationToken: Ct);

        Assert.True(loaded.IsFailure);
        Assert.Equal("serialization.too_large", loaded.Error.Code);
    }

    [Fact]
    public async Task It_refuses_a_file_that_cannot_fit_an_int_indexed_buffer()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "huge.json");

        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            // Sparse: the file reports a size past int.MaxValue without writing
            // two gigabytes. The store must refuse it before the cast to int.
            stream.SetLength((long)int.MaxValue + 1);
        }

        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter()) { MaxFileBytes = long.MaxValue };

        var loaded = await store.LoadAsync("huge.json", cancellationToken: Ct);

        Assert.True(loaded.IsFailure);
        Assert.Equal("serialization.too_large", loaded.Error.Code);
    }

    [Theory]
    [InlineData("state.json", DataFormat.Json)]
    [InlineData("sheet.CSV", DataFormat.Csv)]
    [InlineData("wg.ini", DataFormat.Ini)]
    [InlineData("config.toml", DataFormat.Toml)]
    [InlineData("notes.txt", null)]
    public void It_resolves_the_format_from_the_extension(string name, DataFormat? expected)
    {
        Assert.Equal(expected, DataFileStore.FormatOf(name));
    }

    [Fact]
    public void FormatOf_refuses_a_null_name()
    {
        Assert.Throws<ArgumentNullException>(() => DataFileStore.FormatOf(null!));
    }

    [Theory]
    [InlineData(DataFormat.Json, ".json")]
    [InlineData(DataFormat.Csv, ".csv")]
    [InlineData(DataFormat.Ini, ".ini")]
    [InlineData(DataFormat.Toml, ".toml")]
    public void It_maps_a_format_to_its_extension(DataFormat format, string expected)
    {
        Assert.Equal(expected, DataFileStore.ExtensionOf(format));
    }

    [Fact]
    public async Task It_saves_and_loads_a_json_document_atomically()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());

        var saved = await store.SaveAsync("state.json", Document(), cancellationToken: Ct);
        Assert.True(saved.IsSuccess);
        Assert.Equal(_directory, store.Directory);

        var loaded = await store.LoadAsync("state.json", cancellationToken: Ct);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(1, loaded.Value.Root.RowCount);
        Assert.Equal(DataValue.From(7L), loaded.Value.Root[0, 0]);
        Assert.Single(loaded.Value.Sections);
        Assert.Equal("wg0", loaded.Value.Sections[0][0, 0].ToInvariantText());
    }

    [Fact]
    public async Task It_takes_an_explicit_format_for_an_unknown_extension()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());

        var saved = await store.SaveAsync("state.dat", Document(), DataFormat.Json, Ct);
        Assert.True(saved.IsSuccess);

        var loaded = await store.LoadAsync("state.dat", DataFormat.Json, Ct);
        Assert.True(loaded.IsSuccess);
    }

    [Fact]
    public async Task It_reports_an_unknown_extension_and_an_unimplemented_codec()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());

        var unknown = await store.SaveAsync("state.txt", Document(), cancellationToken: Ct);
        Assert.Equal("serialization.unsupported_format", unknown.Error.Code);

        var toml = await store.SaveAsync("config.toml", Document(), cancellationToken: Ct);
        Assert.Equal("serialization.unsupported_format", toml.Error.Code);

        var loadUnknown = await store.LoadAsync("state.txt", cancellationToken: Ct);
        Assert.Equal("serialization.unsupported_format", loadUnknown.Error.Code);

        var loadToml = await store.LoadAsync("config.toml", cancellationToken: Ct);
        Assert.Equal("serialization.unsupported_format", loadToml.Error.Code);
    }

    [Fact]
    public async Task It_saves_and_loads_an_ini_document()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());
        var table = new DataTable(
            "Peer",
            new DataSchema([new DataField("key", DataKind.Text), new DataField("value", DataKind.Text)]));
        table.AddRow([DataValue.From("AllowedIPs"), DataValue.From("0.0.0.0/0")]);
        var document = new DataDocument(
            new DataTable(string.Empty, new DataSchema([new DataField("key", DataKind.Text), new DataField("value", DataKind.Text)])),
            [table]);

        var saved = await store.SaveAsync("wg.ini", document, cancellationToken: Ct);
        Assert.True(saved.IsSuccess);

        var loaded = await store.LoadAsync("wg.ini", cancellationToken: Ct);
        Assert.True(loaded.IsSuccess);
        Assert.Single(loaded.Value.Sections);
        Assert.Equal(DataValue.From("AllowedIPs"), loaded.Value.Sections[0][0, 0]);
    }

    [Fact]
    public async Task It_saves_and_loads_a_csv_document()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());
        var table = new DataTable("root", new DataSchema([new DataField("a", DataKind.Integer), new DataField("b", DataKind.Text)]));
        table.AddRow([DataValue.From(1L), DataValue.From("x,y")]);

        var saved = await store.SaveAsync("sheet.csv", DataDocument.FromTable(table), cancellationToken: Ct);
        Assert.True(saved.IsSuccess);

        var loaded = await store.LoadAsync("sheet.csv", cancellationToken: Ct);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(1, loaded.Value.Root.RowCount);
        Assert.Equal(DataValue.From("x,y"), loaded.Value.Root[0, 1]);
    }


    [Fact]
    public async Task It_reports_a_missing_file()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());
        Directory.CreateDirectory(_directory);

        var result = await store.LoadAsync("missing.json", cancellationToken: Ct);

        Assert.Equal("serialization.missing", result.Error.Code);
    }

    [Fact]
    public async Task It_reports_an_unreadable_file()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());
        Directory.CreateDirectory(Path.Combine(_directory, "state.json"));

        var result = await store.LoadAsync("state.json", cancellationToken: Ct);

        Assert.Equal("serialization.unreadable", result.Error.Code);
    }

    [Fact]
    public async Task It_propagates_a_syntax_failure()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "state.json"), "{ not json", Ct);

        var result = await store.LoadAsync("state.json", cancellationToken: Ct);

        Assert.Equal("serialization.syntax", result.Error.Code);
    }

    [Fact]
    public async Task It_refuses_a_document_json_cannot_represent()
    {
        var table = new DataTable("root", new DataSchema([new DataField("a", DataKind.Integer)]));
        table.AddRow([DataValue.From(1L)]);
        table.AddRow([DataValue.From(2L)]);
        var document = new DataDocument(table, [new DataTable("s", DataSchema.Empty)]);

        var result = await new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter()).SaveAsync("state.json", document, cancellationToken: Ct);

        Assert.Equal("serialization.invalid_document", result.Error.Code);
    }

    [Fact]
    public async Task It_reports_an_unwritable_directory()
    {
        var file = Path.Combine(Path.GetTempPath(), "core-data-file-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(file, "x", Ct);

        try
        {
            var result = await new DataFileStore(file, Factory, new LinuxFileSystemAdapter()).SaveAsync("state.json", Document(), cancellationToken: Ct);
            Assert.Equal("serialization.unwritable", result.Error.Code);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task A_zero_read_is_unreadable()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "state.json"), "{}", Ct);

        var store = new DataFileStore(_directory, (_, _, _) => 0, Factory, new LinuxFileSystemAdapter());
        var loaded = await store.LoadAsync("state.json", cancellationToken: Ct);

        Assert.Equal("serialization.unreadable", loaded.Error.Code);
    }

    [Fact]
    public async Task It_refuses_a_symlink_on_read()
    {
        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, "real.json");
        await File.WriteAllTextAsync(target, "{}", Ct);
        File.CreateSymbolicLink(Path.Combine(_directory, "link.json"), target);

        var result = await new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter()).LoadAsync("link.json", cancellationToken: Ct);

        Assert.Equal("serialization.unreadable", result.Error.Code);
    }

    [Fact]
    public async Task It_propagates_a_refused_symlink_target()
    {
        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, "real.json");
        await File.WriteAllTextAsync(target, "{}", Ct);
        File.CreateSymbolicLink(Path.Combine(_directory, "state.json"), target);

        var result = await new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter()).SaveAsync("state.json", Document(), cancellationToken: Ct);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task It_refuses_a_name_that_is_not_a_single_component()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());

        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync("../escape.json", cancellationToken: Ct).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("a/b.json", Document(), cancellationToken: Ct).AsTask());
    }

    [Fact]
    public async Task It_saves_and_loads_a_typed_value()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());
        var sample = new MappingTests.Sample("wg", 7, [new MappingTests.Entry("wg0", 51820)]);

        var saved = await store.SaveValueAsync("state.json", sample, SerializationJsonContext.Default.Sample, cancellationToken: Ct);
        Assert.True(saved.IsSuccess);

        var loaded = await store.LoadValueAsync("state.json", SerializationJsonContext.Default.Sample, cancellationToken: Ct);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(sample.Name, loaded.Value.Name);
        Assert.Equal(sample.Count, loaded.Value.Count);
        Assert.Equal(sample.Entries, loaded.Value.Entries);
    }

    [Fact]
    public async Task A_typed_store_propagates_a_missing_file_a_bad_mapping_and_a_save_failure()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());

        var missing = await store.LoadValueAsync("missing.json", SerializationJsonContext.Default.Sample, cancellationToken: Ct);
        Assert.Equal("serialization.missing", missing.Error.Code);

        var table = new DataTable(
            "root",
            new DataSchema([new DataField("name", DataKind.Text), new DataField("count", DataKind.Text)]));
        table.AddRow([DataValue.From("x"), DataValue.From("not-a-number")]);
        await store.SaveAsync("bad.json", DataDocument.FromTable(table), cancellationToken: Ct);

        var bad = await store.LoadValueAsync("bad.json", SerializationJsonContext.Default.Sample, cancellationToken: Ct);
        Assert.Equal("serialization.invalid_document", bad.Error.Code);

        var unsupported = await store.SaveValueAsync(
            "state.txt",
            new MappingTests.Sample("x", 1, []),
            SerializationJsonContext.Default.Sample,
            cancellationToken: Ct);
        Assert.Equal("serialization.unsupported_format", unsupported.Error.Code);
    }

    [Fact]
    public async Task A_typed_save_of_a_null_value_is_refused()
    {
        var store = new DataFileStore(_directory, Factory, new LinuxFileSystemAdapter());
        MappingTests.Sample? value = null;

        var result = await store.SaveValueAsync("state.json", value!, SerializationJsonContext.Default.Sample, cancellationToken: Ct);

        Assert.True(result.IsFailure);
        Assert.Equal("serialization.invalid_document", result.Error.Code);
    }

    [Fact]
    public async Task It_creates_the_store_directory_owner_only()
    {
        var directory = Path.Combine(Path.GetTempPath(), "core-data-" + Guid.NewGuid().ToString("N"));

        try
        {
            var store = new DataFileStore(directory, Factory, new LinuxFileSystemAdapter());
            var saved = await store.SaveAsync("state.json", Document(), cancellationToken: Ct);
            Assert.True(saved.IsSuccess);

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static DataDocument Document()
    {
        var root = new DataTable("root", new DataSchema([new DataField("version", DataKind.Integer)]));
        root.AddRow([DataValue.From(7L)]);

        var section = new DataTable("tunnels", new DataSchema([new DataField("name", DataKind.Text)]));
        section.AddRow([DataValue.From("wg0")]);

        return new DataDocument(root, [section]);
    }
}
