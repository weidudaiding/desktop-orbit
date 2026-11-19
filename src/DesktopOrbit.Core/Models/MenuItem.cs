namespace DesktopOrbit.Core.Models;

public enum ItemKind
{
    Folder,
    File,
    Shortcut,
    Application,
    Url
}

public sealed record MenuItem
{
    public required string Path { get; init; }
    public required string Label { get; init; }
    public ItemKind Kind { get; init; } = ItemKind.File;
    public bool IsFolder => Kind == ItemKind.Folder;
}
