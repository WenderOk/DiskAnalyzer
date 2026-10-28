namespace DiskAnalyzer.Domain.Models;

public sealed class DirectoryNode
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public DirectoryNode? Parent { get; set; }

    public List<DirectoryNode> Children { get; } = new();
    public List<FileEntry> Files { get; } = new();

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