using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

class Program
{
    static void Main()
    {
        IntPtr hFile = CreateFile(
            "pippo.txt",
            GENERIC_READ | GENERIC_WRITE,
            0, // No sharing
            IntPtr.Zero,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (hFile == INVALID_HANDLE_VALUE)
        {
            Console.WriteLine($"CreateFile failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        Console.WriteLine($"Size of SYSTEM_HANDLE: {Marshal.SizeOf<SYSTEM_HANDLE>()} bytes");
        Console.WriteLine($"Current Process ID: {Process.GetCurrentProcess().Id}");
        Console.WriteLine($"Handle of 'pippo.txt': {hFile}");

        Thread.Sleep(2000); // Attende un secondo per assicurarsi che il file sia aperto

        // Esempio di utilizzo
        string fileToClose = @"pippo.txt";
        
        try
        {
            if (CloseFileHandle(fileToClose))
            {
                Console.WriteLine($"Handle del file '{fileToClose}' chiuso con successo!");
            }
            else
            {
                Console.WriteLine($"Impossibile trovare o chiudere l'handle del file '{fileToClose}'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Errore: {ex.Message}");
        }
    }

    /// <summary>
    /// Risolve ricorsivamente i symlink e junction nel percorso del file
    /// </summary>
    /// <param name="filePath">Percorso del file da risolvere</param>
    /// <returns>Percorso completamente risolto</returns>
    public static string ResolveLinkTargetsRecursively(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return filePath;

        try
        {
            string currentPath = Path.GetFullPath(filePath);
            string resolvedPath = currentPath;

            // Prova a risolvere il file stesso se è un link
            try
            {
                var fileInfo = new FileInfo(currentPath);
                if (fileInfo.LinkTarget != null)
                {
                    resolvedPath = Path.GetFullPath(fileInfo.LinkTarget);
                    Console.WriteLine($"File link risolto: {currentPath} -> {resolvedPath}");
                }
            }
            catch
            {
                // Se il file non esiste ancora, continuiamo con il percorso normalizzato
            }

            // Risolve ricorsivamente i symlink nelle directory parent
            resolvedPath = ResolveParentLinksRecursively(resolvedPath);

            return resolvedPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Errore durante la risoluzione dei link: {ex.Message}");
            return Path.GetFullPath(filePath);
        }
    }

    /// <summary>
    /// Risolve ricorsivamente i symlink e junction nelle directory parent
    /// </summary>
    private static string ResolveParentLinksRecursively(string filePath)
    {
        try
        {
            string currentPath = filePath;
            var pathParts = new List<string>();

            // Estrai tutte le parti del percorso
            DirectoryInfo? dirInfo = new DirectoryInfo(currentPath).Parent;
            pathParts.Add(Path.GetFileName(currentPath));

            while (dirInfo != null)
            {
                pathParts.Insert(0, dirInfo.Name);
                dirInfo = dirInfo.Parent;
            }

            // Se è un percorso assoluto, aggiungi il root
            string? root = Path.GetPathRoot(currentPath);
            if (!string.IsNullOrEmpty(root) && pathParts[0] != root.TrimEnd(Path.DirectorySeparatorChar))
            {
                pathParts.Insert(0, root.TrimEnd(Path.DirectorySeparatorChar));
            }

            // Ricostruisci il percorso risolvendo ogni directory
            string resolvedPath = pathParts[0];
            for (int i = 1; i < pathParts.Count; i++)
            {
                string nextPath = Path.Combine(resolvedPath, pathParts[i]);

                // Tenta di risolvere il link della directory corrente
                try
                {
                    var dirInfo2 = new DirectoryInfo(resolvedPath);
                    if (dirInfo2.LinkTarget != null)
                    {
                        string resolvedParent = Path.GetFullPath(dirInfo2.LinkTarget);
                        resolvedPath = Path.Combine(resolvedParent, pathParts[i]);
                        Console.WriteLine($"Directory link risolto: {nextPath} -> {resolvedPath}");
                    }
                    else
                    {
                        resolvedPath = nextPath;
                    }
                }
                catch
                {
                    resolvedPath = nextPath;
                }
            }

            return resolvedPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Errore durante la risoluzione dei link parent: {ex.Message}");
            return filePath;
        }
    }

    /// <summary>
    /// Chiude forzosamente l'handle di un file aperto dal processo corrente
    /// </summary>
    /// <param name="filePath">Percorso completo del file</param>
    /// <returns>True se l'handle è stato chiuso con successo, False altrimenti</returns>
    public static bool CloseFileHandle(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(nameof(filePath));

        // Normalizza il percorso e risolve i symlink/junction
        string normalizedPath = ResolveLinkTargetsRecursively(filePath);

        try
        {
            var process = Process.GetCurrentProcess();
            
            // Enumera tutti gli handle del processo
            var handles = EnumerateProcessHandles(process.Id);
            
            // Filtra gli handle che corrispondono al nome del file
            var fileHandles = handles.Where(h => 
                IsFileHandle(h.Handle, normalizedPath)).ToList();

            if (fileHandles.Count == 0)
            {
                Console.WriteLine($"Nessun handle trovato per il file: {normalizedPath}");
                return false;
            }

            // Chiude tutti gli handle trovati
            bool success = true;
            foreach (var handleInfo in fileHandles)
            {
                if (!CloseHandle(handleInfo.Handle))
                {
                    Console.WriteLine($"Errore nel chiudere l'handle: {Marshal.GetLastWin32Error()}");
                    success = false;
                }
                else
                {
                    Console.WriteLine($"Handle chiuso: {handleInfo.Handle}");
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Eccezione durante la chiusura dell'handle: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enumera tutti gli handle aperti dal processo specificato
    /// </summary>
    private static List<HandleInfo> EnumerateProcessHandles(int processId)
    {
        var handles = new List<HandleInfo>();

        try
        {
            // Alloca il buffer per la query di sistema
            int infoSize = 0x10000;
            IntPtr infoPtr = Marshal.AllocHGlobal(infoSize);

            try
            {
                while (NtQuerySystemInformation(
                    SYSTEM_HANDLE_INFORMATION,
                    infoPtr,
                    infoSize,
                    out int returnedSize) == 0xC0000004) // STATUS_INFO_LENGTH_MISMATCH
                {
                    infoSize = returnedSize;
                    Marshal.FreeHGlobal(infoPtr);
                    infoPtr = Marshal.AllocHGlobal(infoSize);
                }

                // Legge la struttura SYSTEM_HANDLE_INFORMATION
                uint handleCount = unchecked((uint)Marshal.ReadInt32(infoPtr));
                IntPtr handlePtr = new IntPtr(infoPtr.ToInt64() + IntPtr.Size);

                for (uint i = 0; i < handleCount; i++)
                {
                    var handleStruct = Marshal.PtrToStructure<SYSTEM_HANDLE>(handlePtr);
                    //Console.WriteLine($"ProcessId: {handleStruct.ProcessId}, Handle: {handleStruct.Handle}, Type: {handleStruct.ObjectTypeIndex}");
                    
                    if (handleStruct.ProcessId == processId)
                    {
                        handles.Add(new HandleInfo
                        {
                            Handle = new IntPtr(handleStruct.Handle),
                            Type = handleStruct.ObjectTypeIndex
                        });
                    }

                    handlePtr = new IntPtr(handlePtr.ToInt64() + Marshal.SizeOf<SYSTEM_HANDLE>());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }
        }
        catch (Exception)
        {
            throw;
        }

        return handles;
    }

    /// <summary>
    /// Verifica se un handle è associato al file specificato
    /// </summary>
    private static bool IsFileHandle(IntPtr handle, string filePath)
    {
        try
        {
            Console.WriteLine($"Verifica handle: {handle}");
            string? handlePath = GetFilePathFromHandle(handle);
            
            if (string.IsNullOrEmpty(handlePath))
                return false;

            // Confronto case-insensitive
            return string.Equals(
                Path.GetFullPath(handlePath),
                filePath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ottiene il percorso del file da un handle
    /// </summary>
    private static string? GetFilePathFromHandle(IntPtr handle)
    {
        try
        {
            // Apre una copia dell'handle per il processo corrente
            IntPtr duplicateHandle = IntPtr.Zero;
            
            if (!DuplicateHandle(
                GetCurrentProcess(),
                handle,
                GetCurrentProcess(),
                out duplicateHandle,
                0,
                false,
                DUPLICATE_SAME_ACCESS))
            {
                return null;
            }

            if (duplicateHandle == IntPtr.Zero)
                return null;

            try
            {
                // Alloca buffer per il nome del file
                StringBuilder sb = new StringBuilder(260);
                uint result = GetFinalPathNameByHandle(duplicateHandle, sb, (uint)sb.Capacity, FILE_NAME_NORMALIZED);

                if (result != 0)
                {
                    if (result > sb.Capacity)
                    {
                        sb.Capacity = (int)result;
                        result = GetFinalPathNameByHandle(duplicateHandle, sb, (uint)sb.Capacity, FILE_NAME_NORMALIZED);
                    }

                    string path = sb.ToString();
                    // Rimuove il prefisso "\\?\" se presente
                    if (path.StartsWith(@"\\?\"))
                        path = path.Substring(4);
                    
                    return path;
                }

                return null;
            }
            finally
            {
                if (duplicateHandle != IntPtr.Zero)
                    CloseHandle(duplicateHandle);
            }
        }
        catch
        {
            return null;
        }
    }

    // ==================== P/Invoke Declarations ====================

    private class HandleInfo
    {
        public IntPtr Handle { get; set; }
        public int Type { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE
    {
        // 0
        public int ProcessId;
        // 4
        public byte ObjectTypeIndex;
        public byte Flags;
        public ushort Handle;
        // 8
        public IntPtr Object;
        // 16
        public int AccessMask;
    }

    private const int SYSTEM_HANDLE_INFORMATION = 16;
    private const int DUPLICATE_SAME_ACCESS = 2;
    private const uint FILE_NAME_NORMALIZED = 0;

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength,
        out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        bool bInheritHandle,
        int dwOptions);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(
        IntPtr hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_ALWAYS = 4;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
}
