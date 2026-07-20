using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using LexVerse.Application.Diagnostics;
using LexVerse.Application.Privacy;
using LexVerse.Infrastructure.Diagnostics;
using LexVerse.Infrastructure.Privacy;

namespace LexVerse.App;

public partial class App : System.Windows.Application
{
    private readonly LocalExceptionLog _exceptionLog = new();
    private readonly PersistentPrivacyPreferencesService _privacyPreferences;
    private readonly LocalOperationalTelemetry _localTelemetry;
    private readonly IOperationalTelemetry _telemetry;
    private readonly DateTimeOffset _processStartedUtc =
        new(System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime());
    private AppCompositionRoot? _compositionRoot;

    public App()
    {
        _privacyPreferences = new PersistentPrivacyPreferencesService();
        _localTelemetry = new LocalOperationalTelemetry();
        _telemetry = new ConsentAwareOperationalTelemetry(_localTelemetry, _privacyPreferences);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            _compositionRoot = new AppCompositionRoot(_telemetry, _privacyPreferences);
            MainWindow = _compositionRoot.CreateMainWindow();
            MainWindow.Show();
            _telemetry.TryRecord(new OperationalEvent
            {
                Kind = OperationalEventKind.ApplicationStartup,
                Outcome = OperationalOutcome.Succeeded,
                Duration = DateTimeOffset.UtcNow - _processStartedUtc,
                ModuleCount = _compositionRoot.Modules.Modules.Count,
                WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64
            });
        }
        catch (Exception exception)
        {
            RecordFailure(OperationalFailureCode.Startup);
            _exceptionLog.Report("startup", exception);
            MessageBox.Show(
                "LexVerse could not start. Check the local log for details.",
                "LexVerse startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        try
        {
            _compositionRoot?.Dispose();
            _telemetry.TryRecord(new OperationalEvent
            {
                Kind = OperationalEventKind.ApplicationShutdown,
                Outcome = OperationalOutcome.Stopped,
                Duration = DateTimeOffset.UtcNow - _processStartedUtc,
                WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64
            });
        }
        finally
        {
            _localTelemetry.Dispose();
            _privacyPreferences.Dispose();
            base.OnExit(e);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        RecordFailure(OperationalFailureCode.Dispatcher);
        _exceptionLog.Report("dispatcher", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "LexVerse encountered an unexpected error and needs to close.",
            "LexVerse error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        RecordFailure(OperationalFailureCode.UnobservedTask);
        _exceptionLog.Report("unobserved-task", e.Exception);
        e.SetObserved();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            RecordFailure(OperationalFailureCode.AppDomain);
            _exceptionLog.Report("app-domain", exception);
        }
    }

    private void RecordFailure(OperationalFailureCode failureCode) =>
        _telemetry.TryRecord(new OperationalEvent
        {
            Kind = OperationalEventKind.UnhandledException,
            Outcome = OperationalOutcome.Failed,
            FailureCode = failureCode,
            Duration = DateTimeOffset.UtcNow - _processStartedUtc,
            WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64
        });
}
