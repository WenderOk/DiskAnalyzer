namespace DiskAnalyzer.Domain.Models;

public sealed class DirectoryNode
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public DirectoryNode? Parent { get; set; }

    public List<DirectoryNode> Children { get; } = new();

    /// Размер/количество файлов только в этом каталоге, без потомков.
    public FileSize OwnSize { get; set; }
    public long OwnFileCount { get; set; }

    /// Итоговые значения по каталогу и всем его потомкам
    public FileSize TotalSize { get; set; }
    public long FileCount { get; set; }
    public long FolderCount { get; set; }

    public IEnumerable<DirectoryNode> EnumerateSelfAndDescendants()
    {
        var stack = new Stack<DirectoryNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.Children)
                stack.Push(child);
        }
    }
}
