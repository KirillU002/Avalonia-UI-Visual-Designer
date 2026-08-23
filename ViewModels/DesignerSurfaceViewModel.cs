using FormDesigner.DesignerSystem;
using FormDesigner.DesignerSystem.Hosting;
using System.ComponentModel;

namespace FormDesigner.ViewModels;

/// <summary>
/// Небольшая host-neutral модель состояния <see cref="Views.DesignerSurface"/>.
/// Она владеет только ссылкой на текущую document session; прикладные команды и
/// данные пока передаются через переходный Context, чтобы не менять проверенные
/// bindings текущего host одним большим шагом.
/// </summary>
public sealed class DesignerSurfaceViewModel : INotifyPropertyChanged
{
    private object? _context;
    private DesignerDocumentSession? _session;
    private IDesignerHostServices? _hostServices;

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

    /// <summary>
    /// Явно переданные services host. Surface не создаёт их и не обращается к
    /// static Current, поэтому другой host может предоставить собственную реализацию.
    /// </summary>
    public IDesignerHostServices? HostServices
    {
        get => _hostServices;
        set
        {
            if (ReferenceEquals(_hostServices, value))
                return;

            _hostServices = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HostServices)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
