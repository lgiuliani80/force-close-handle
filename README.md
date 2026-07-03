# Force Close Handle

A C# code example that demonstrates how to programmatically close file handles opened by the current process on Windows, using the NT API (`NtQuerySystemInformation`).

## ⚠️ This is a Gist

This project is intended as a **code example / snippet** — not a standalone tool or library. Feel free to copy and integrate the relevant parts (e.g. `CloseFileHandle`, `EnumerateProcessHandles`, `ResolveLinkTargetsRecursively`) directly into your own application.

## Key Points

- **Current process only** — the code can only close handles that belong to the calling process. It does not (and cannot, without elevated privileges and process duplication) close handles held by other processes.
- **Symlink & Junction aware** — file paths are fully resolved before matching, so junctions and symbolic links present anywhere in the path are handled correctly.
- **Multi-architecture** — tested on **x64**, **x86**, and **ARM64**.

## Requirements

- .NET [it has been tested on .NET 10, but it should work with any .NET version, including .NET Framework - on this just remove the nullable references]
- Windows (uses P/Invoke to NT and kernel32 APIs)

## Usage

```csharp
// Resolve symlinks/junctions and close the handle(s) for the given file
if (CloseFileHandle(@"C:\path\to\file.txt"))
{
    Console.WriteLine("Handle closed successfully.");
}
```

## License

Use freely. No warranty.
