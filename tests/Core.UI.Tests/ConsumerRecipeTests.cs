// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Core.UI.Chrome;
using Core.UI.Hosting;
using Core.UI.Tray;
using Core.Diagnostics;
using Core.Execution;
using Core.Platform.Linux;
using Core.Mvp.Presenters;
using Core.Mvp.Presentation;
using Core.Mvp.Services;
using Core.Mvp.Views;
using Core.Notifications;
using Core.Platform;
using Core.Primitives;
using Core.Results;
using Core.Security;
using Core.Serialization.Tables;
using Core.Serialization.Values;
using Core.Text;
using Core.Validation;

namespace Core.UI.Tests;

/// <summary>
/// Compiles and runs the consumer recipes against the shipped API. A compile
/// error here means a recipe no longer builds; keep the types below in lockstep
/// with the shipped API.
/// </summary>
[Collection("headless")]
public sealed class ConsumerRecipeTests
{
    [Fact]
    public async Task The_screen_recipe_drives_a_passive_view()
    {
        await Headless.Dispatch(() =>
        {
            var view = new GreetingView();
            var presenter = new GreetingPresenter(view, new GreetingService());

            presenter.InitializeAsync().AsTask().GetAwaiter().GetResult();
            presenter.ActivateAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("Hello, operator.", view.Greeting);
            Assert.False(view.IsBusy);
            Assert.Null(view.LastError);

            presenter.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task The_window_recipe_opens_a_screen_and_publishes_a_snapshot()
    {
        await Headless.Dispatch(() =>
        {
            var window = new GreetingWindow();
            var view = new GreetingView();

            window.OpenTab("greeting", "Greeting", view);
            window.SetSnapshot(new ShellSnapshot("Greeting", "the consumer recipe", view.Commands));

            Assert.Same(view, window.Active);
            Assert.Equal("Greeting", window.CurrentSnapshot?.Title);
        });
    }

    [Fact]
    public async Task The_presenter_recipe_opens_a_tab_and_uses_the_shell_services()
    {
        await Headless.Dispatch(() =>
        {
            var window = new GreetingWindow();
            var view = new GreetingView();
            var presenter = new GreetingPresenter(view, new GreetingService());

            window.Show();
            window.OpenTab("greeting", "Greeting", presenter);
            Assert.Same(view, window.Active);

            window.SetTabDirty("greeting", true);
            window.SetSnapshot(new ShellSnapshot("Greeting", "the presenter recipe", view.Commands)
            {
                IsDirty = true,
                Progress = 0.5,
            });

            Assert.True(window.CurrentSnapshot?.IsDirty);
            Assert.Equal(0.5, window.CurrentSnapshot?.Progress);
            Assert.NotNull(window.Prompts);
            Assert.NotNull(window.Folders);

            window.CloseTab("greeting");
            window.Close();
        });
    }

    [Fact]
    public async Task The_application_recipe_builds_and_takes_the_brand_accent()
    {
        var (name, accent) = await Headless.Dispatch(() =>
        {
            var application = new GreetingApp();
            application.SetAccent(Color.Parse("#10B981"));

            return (application.Name, Assert.IsType<SolidColorBrush>(application.Resources["PrimaryBrush"]).Color);
        });

        Assert.Equal("core-recipe", name);
        Assert.Equal(Color.Parse("#10B981"), accent);
        Assert.NotNull(CoreAppBuilder.Configure<GreetingApp>("core-recipe"));
    }

    /// <summary>
    /// The guide's XAML includes must be the exact URIs the shipped-shape app
    /// compiles and must resolve to a file on disk, so a renamed or removed theme
    /// file fails here instead of in a product.
    /// </summary>
    [Fact]
    public void The_theme_recipe_names_the_shipped_includes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(
            repositoryRoot, "tests", "Core.UI.Tests", "Fixtures", "App.axaml.txt"));

        string[] includes =
        [
            "avares://Core.UI/Themes/Tokens.axaml",
            "avares://Core.UI/Themes/Icons.axaml",
            "avares://Core.UI/Themes/Accents.axaml",
            "avares://Core.UI/Themes/Controls.axaml",
        ];

        foreach (var include in includes)
        {
            Assert.Contains(include, app, StringComparison.Ordinal);

            // Uri.Host lowercases the authority; take the literal segment so a
            // renamed Core.UI folder is caught on a case-sensitive file system.
            var authority = include["avares://".Length..].Split('/')[0];
            var path = Path.Combine(
                repositoryRoot, "src", authority,
                include["avares://".Length..][(authority.Length + 1)..].Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(path), $"{include} does not resolve to {path}.");
        }
    }

    /// <summary>
    /// Compiles and runs the enterprise-module recipe of the guide: text, data
    /// views, diagnostics, validation and the process seam.
    /// </summary>
    [Fact]
    public async Task The_enterprise_module_recipe_builds_and_runs()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            string.Concat("core-recipe-", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var table = new DataTable(
                "people",
                new DataSchema(
                [
                    new DataField("id", DataKind.Integer),
                    new DataField("name", DataKind.Text),
                ]));
            table.AddRow([DataValue.From(2L), DataValue.From("b")]);
            table.AddRow([DataValue.From(1L), DataValue.From("a")]);

            var sorted = new DataView(table).Sort(new SortKey("id"));
            Assert.True(sorted.IsSuccess);
            var visible = sorted.Value.FilterRows(index => table.Column(0).Integers[index] > 0);
            Assert.Equal(2, visible.Count);
            Assert.Equal(2, DataAggregates.CountNonNull(table.Column(0)));
            Assert.True(DataAggregates.Min(table.Column(0)).IsSuccess);
            Assert.True(DataJoin.Join(table, table, [new JoinKey("id", "id")]).IsSuccess);
            Assert.True(DataProjection.Select(table, ["id", "name"]).IsSuccess);

            var replacement = TextSearch.ReplaceAll("draft\n", "draft", "final", SearchOptions.Default);
            Assert.Equal("final\n", replacement.Text);

            var history = new UndoRedoHistory<string>();
            history.Record(replacement.Text);
            Assert.Equal("final\n", history.Undo("draft\n"));

            var store = new TextFileStore(16 * 1024 * 1024);
            var path = Path.Combine(directory, "document.txt");
            Assert.True(store.Write(path, replacement.Text, LineEnding.LineFeed).IsSuccess);
            Assert.Equal("final\n", store.Read(path).Value.Text);

            using (var logger = new RotatingFileLogger(Path.Combine(directory, "app.log")))
            {
                logger.Log(LogLevel.Information, "started");
            }

            using (var concreteJournal = new RotatingOperationJournal(Path.Combine(directory, "operations.log")))
            {
                IOperationJournal journal = concreteJournal;
                journal.Record("document.save", "saved");
            }

            using (var crash = new CrashLog(Path.Combine(directory, "crash.log")))
            {
                crash.Write(new InvalidOperationException("recipe"), "document.save");
            }

            Assert.Equal("~/a.txt", PathRedaction.ForMessage("/home/operator/a.txt", "/home/operator"));
            Assert.Equal("[redacted]", SecretText.From("secret").ToString());
            Assert.True(Validators.Port(51_820).IsSuccess);
            Assert.True(PathComponent.Validate("state").IsSuccess);
            Assert.Equal("one\\r\\nline", LogRedaction.SingleLine("one\r\nline"));

            var run = await new SystemProcessRunner()
                .RunAsync(new ProcessRequest("printf", ["ok"]), TestContext.Current.CancellationToken);
            Assert.True(run.IsSuccess);
            Assert.Equal("ok", run.Value.StandardOutput);

            var fake = new RecipeProcessRunner();
            ISystemLinkLauncher launcher = new SystemLinkLauncher(fake);
            Assert.True((await launcher.OpenAsync("https://example.org", TestContext.Current.CancellationToken)).IsSuccess);

            ISystemNotifier notifier = new NotifySendNotifier(fake);
            await notifier.NotifyAsync("Title", "Body", TestContext.Current.CancellationToken);

            using var single = new SingleInstanceLock(Path.Combine(directory, "instance.sock"));
            single.Activated += _ => { };
            single.ActivationRequested += () => { };
            Assert.Equal(65_536, SingleInstanceLock.MaxPayloadBytes);
            Assert.True(single.TryAcquire(Encoding.UTF8.GetBytes("document.md")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Core.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Core.slnx was not found above the test output directory.");
    }

    /// <summary>The guide's screen recipe: a passive view that is an Avalonia control.</summary>
    private sealed class GreetingView : ContentControl, IView
    {
        private readonly TextBlock _label = new();

        public GreetingView()
        {
            Content = _label;
        }

        public string? Greeting { get; private set; }

        public bool IsBusy { get; private set; }

        public CoreError? LastError { get; private set; }

        public CommandSet Commands { get; } = new([new AppCommand("greet", "Greet", IconKey: "IconAbout")]);

        public void ShowGreeting(string greeting)
        {
            Greeting = greeting;
            _label.Text = greeting;
        }

        public void SetBusy(bool busy)
        {
            IsBusy = busy;
        }

        public void ShowError(CoreError error)
        {
            LastError = error;
        }
    }

    /// <summary>The guide's service contract: expected failures are a <see cref="Result{T}"/>.</summary>
    private interface IGreetingService : IService
    {
        ValueTask<Result<string>> GreetAsync(CancellationToken cancellationToken);
    }

    private sealed class GreetingService : IGreetingService
    {
        public ValueTask<Result<string>> GreetAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result<string>.Success("Hello, operator."));
        }
    }

    /// <summary>The guide's presenter: the view stays passive, the presenter owns the logic.</summary>
    private sealed class GreetingPresenter : PresenterBase<GreetingView>
    {
        private readonly IGreetingService _service;

        public GreetingPresenter(GreetingView view, IGreetingService service)
            : base(view)
        {
            _service = service;
        }

        protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
        {
            return RunGuardedAsync(_service.GreetAsync, View.ShowGreeting, cancellationToken);
        }
    }

    /// <summary>The guide's window recipe: the product owns the tray and the content.</summary>
    private sealed class GreetingWindow : CoreWindow
    {
        public GreetingWindow()
            : base(new TrayService(icon: null, activated: _ => { }))
        {
            Title = "Recipe";
        }
    }

    /// <summary>The process seam a recipe substitutes for a real executable.</summary>
    private sealed class RecipeProcessRunner : IProcessRunner
    {
        public Task<Result<ProcessResult>> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ProcessResult>.Success(new ProcessResult(0, string.Empty, string.Empty)));
    }

    /// <summary>The guide's application recipe: derive, name the app id, build the window.</summary>
    private sealed class GreetingApp : CoreApplication
    {
        protected override string ApplicationId => "core-recipe";

        public override void Initialize()
        {
            base.Initialize();
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new GreetingWindow();
                window.QuitRequested += async () =>
                {
                    await window.ShutdownAsync();
                    desktop.Shutdown();
                };
                desktop.MainWindow = window;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
