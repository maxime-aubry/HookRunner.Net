using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HookRunner.Infrastructure;

/// <summary>Renomme un document avec remplacement POSIX, sans fermer ses lecteurs ni retirer le chemin publié.</summary>
internal sealed partial class WindowsStateFilePublisher
{
    private const uint DeleteAccess = 0x00010000;
    private const int FileRenameInfoEx = 22;
    private const int ReplaceExistingWithPosixSemantics = 3;
    private readonly OpenFile open;
    private readonly RenameFile rename;

    /// <summary>Prépare les appels Windows, substituables à la frontière native pour vérifier leurs erreurs.</summary>
    /// <param name="open">CreateFileW, ou sa simulation sans accès à Windows.</param>
    /// <param name="rename">SetFileInformationByHandle, ou sa simulation de renommage.</param>
    internal WindowsStateFilePublisher(OpenFile? open = null, RenameFile? rename = null)
    {
        this.open = open ?? CreateFileW;
        this.rename = rename ?? SetFileInformationByHandle;
    }

    /// <summary>Remplace le nom de destination tout en conservant les handles de l'ancien fichier.</summary>
    /// <param name="temporary">Document fermé et synchronisé, situé sur le volume de destination.</param>
    /// <param name="path">Chemin absolu à publier sous le verrou de document.</param>
    /// <exception cref="IOException">L'ouverture native ou le renommage échoue ; l'erreur Windows est conservée.</exception>
    /// <remarks>Requiert FileRenameInfoEx et FILE_RENAME_POSIX_SEMANTICS ; aucun repli non atomique n'est effectué.</remarks>
    public void Publish(string temporary, string path)
    {
        using SafeFileHandle file = open(temporary, DeleteAccess, FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (file.IsInvalid)
        {
            throw new IOException($"Cannot open state publication file '{temporary}'.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }

        byte[] name = Encoding.Unicode.GetBytes(path);
        // FILE_RENAME_INFO aligne RootDirectory sur la taille d'un pointeur ; FileName suit sa longueur DWORD.
        int lengthOffset = IntPtr.Size * 2;
        int nameOffset = lengthOffset + sizeof(int);
        byte[] information = new byte[nameOffset + name.Length + sizeof(char)];
        BinaryPrimitives.WriteInt32LittleEndian(information, ReplaceExistingWithPosixSemantics);
        BinaryPrimitives.WriteInt32LittleEndian(information.AsSpan(lengthOffset), name.Length);
        name.CopyTo(information, nameOffset);
        if (!rename(file, FileRenameInfoEx, information, information.Length))
        {
            throw new IOException($"Cannot atomically publish state file '{path}'.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    /// <summary>Ouvre le temporaire avec le droit DELETE nécessaire au renommage.</summary>
    /// <param name="path">Chemin du temporaire.</param>
    /// <param name="access">Droits natifs demandés.</param>
    /// <param name="share">Partages accordés aux autres handles.</param>
    /// <param name="security">Attributs de sécurité natifs, null pour l'héritage par défaut.</param>
    /// <param name="creation">OPEN_EXISTING, valant 3.</param>
    /// <param name="flags">Attributs supplémentaires du fichier.</param>
    /// <param name="template">Handle de modèle, null pour cette opération.</param>
    /// <returns>Handle possédé par l'appelant, invalide en cas d'échec.</returns>
    internal delegate SafeFileHandle OpenFile(string path, uint access, FileShare share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    /// <summary>Soumet le renommage étendu au système de fichiers.</summary>
    /// <param name="file">Temporaire ouvert avec le droit DELETE.</param>
    /// <param name="informationClass">FileRenameInfoEx, valant 22.</param>
    /// <param name="information">Structure FILE_RENAME_INFO suivie du nom UTF-16.</param>
    /// <param name="size">Taille du tampon natif en octets.</param>
    /// <returns>True au succès ; false laisse une erreur Windows à traduire.</returns>
    internal delegate bool RenameFile(SafeFileHandle file, int informationClass, byte[] information, int size);

    /// <inheritdoc cref="OpenFile" />
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    /// <inheritdoc cref="RenameFile" />
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int informationClass,
        [In] byte[] information, int size);
}
