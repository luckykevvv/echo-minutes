using System.Windows.Input;
using System.Diagnostics;

namespace MeetingTransfer.App.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isExecuting;

    public RelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? onError = null)
    {
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : new Func<object?, bool>(_ => canExecute());
        _onError = onError;
    }

    public RelayCommand(
        Func<object?, Task> execute,
        Func<object?, bool>? canExecute = null,
        Action<Exception>? onError = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
        => await ExecuteAsync(parameter).ConfigureAwait(true);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            await _execute(parameter).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Async command failed: {ex}");
            _onError?.Invoke(ex);
            ExecutionFailed?.Invoke(this, ex);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler<Exception>? ExecutionFailed;

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
