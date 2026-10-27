namespace OpenCodeSticky.App.ViewModels;

public sealed class ServiceHeaderViewModel
{
    public ServiceHeaderViewModel(string name) => Name = name.ToUpperInvariant();

    public string Name { get; }
}
