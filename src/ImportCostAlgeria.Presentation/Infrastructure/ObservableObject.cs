using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ImportCostAlgeria.Presentation.Infrastructure;

/// <summary>
/// Classe de base MVVM minimale (volontairement sans dépendance à un toolkit MVVM externe, afin de
/// réduire au strict nécessaire les dépendances NuGet du projet Windows).
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
