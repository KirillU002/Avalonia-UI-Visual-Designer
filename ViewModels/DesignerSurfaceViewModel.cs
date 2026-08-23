using FormDesigner.DesignerSystem;
using System.ComponentModel;

namespace FormDesigner.ViewModels;

/// <summary>
/// Небольшая host-neutral модель состояния <see cref="Views.DesignerSurface"/>.
/// Она владеет только ссылкой на текущую document session; прикладные команды и
/// данные пока передаются через переходный Context, чтобы не менять проверенные
/// bindings MainWindow одним большим шагом.
/// </summary>
public sealed class DesignerSurfaceViewModel : INotifyPropertyChanged
{
    private object? _context;
    private DesignerDocumentSession? _session;

    /// <summary>
    /// Переходный facade для существующих bindings. Его concrete type не входит
    /// в публичный контракт DesignerSurface.
    /// </summary>
    public object? Context
    {
        get => _context;
        set
        {
            if (ReferenceEquals(_context, value))
                return;

            _context = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Context)));
        }
    }

    public DesignerDocumentSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
                return;

            _session = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Session)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
