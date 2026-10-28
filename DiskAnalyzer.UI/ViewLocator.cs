using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DiskAnalyzer.UI.ViewModels;

namespace DiskAnalyzer.UI;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;

        var viewModelType = data.GetType();
        var viewTypeName = viewModelType.FullName!.Replace("ViewModel", "View");
        var type = viewModelType.Assembly.GetType(viewTypeName);

        if (type is null)
            return new TextBlock { Text = $"View для {viewTypeName} не найден." };

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}