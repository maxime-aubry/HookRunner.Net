using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using HookRunner.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace HookRunner.ExecutionTests;

/// <summary>Contrat de l'API native de publication, vérifié aussi sans noyau Windows.</summary>
public sealed class WindowsStateFilePublisherTests
{
    /// <summary>Le renommage transmet le chemin Unicode et les droits POSIX, puis libère son handle.</summary>
    /// <param name="directory">Dossier absolu Windows du temporaire et de la destination.</param>
    /// <param name="nativeDirectory">Dossier attendu par les API natives, avec son préfixe étendu.</param>
    [Theory]
    [InlineData(@"C:\solution-é\Hooks", @"\\?\C:\solution-é\Hooks")]
    [InlineData(@"\\server\share\Hooks", @"\\?\UNC\server\share\Hooks")]
    [InlineData(@"\\?\C:\solution-é\Hooks", @"\\?\C:\solution-é\Hooks")]
    [InlineData(@"\\?\UNC\server\share\Hooks", @"\\?\UNC\server\share\Hooks")]
    [InlineData(@"\??\C:\solution-é\Hooks", @"\??\C:\solution-é\Hooks")]
    [InlineData(@"\\.\C:\solution-é\Hooks", @"\\.\C:\solution-é\Hooks")]
    public void PublicationSuppliesPosixRenameAndOwnsItsHandle(string directory, string nativeDirectory)
    {
        string temporary = directory + @"\prepared.tmp";
        string destination = directory + @"\state.json";
        string nativeDestination = nativeDirectory + @"\state.json";
        using SafeFileHandle handle = new(new IntPtr(42), ownsHandle: false);
        bool renamed = false;
        WindowsStateFilePublisher publisher = new((path, access, share, security, creation, flags, template) =>
        {
            Assert.Equal(nativeDirectory + @"\prepared.tmp", path);
            Assert.Equal(0x00010000U, access);
            Assert.Equal(FileShare.ReadWrite | FileShare.Delete, share);
            Assert.Equal(IntPtr.Zero, security);
            Assert.Equal(3U, creation);
            Assert.Equal(0U, flags);
            Assert.Equal(IntPtr.Zero, template);
            return handle;
        }, (file, informationClass, information, size) =>
        {
            Assert.Same(handle, file);
            Assert.Equal(22, informationClass);
            Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(information));
            // Le nom commence à l'octet 20 sur un ABI 64 bits, à l'octet 12 sur un ABI 32 bits.
            int nameOffset = 12;
            if (Environment.Is64BitProcess)
            {
                nameOffset = 20;
            }

            Assert.All(information[4..(nameOffset - 4)], value => Assert.Equal(0, value));
            int nameLength = BinaryPrimitives.ReadInt32LittleEndian(information.AsSpan(nameOffset - 4));
            Assert.Equal(nativeDestination.Length * 2, nameLength);
            Assert.Equal(nativeDestination, Encoding.Unicode.GetString(information, nameOffset, nameLength));
            Assert.Equal(nameOffset + nameLength + 2, size);
            Assert.Equal(new byte[] { 0, 0 }, information[^2..]);
            renamed = true;
            return true;
        });

        publisher.Publish(temporary, destination);

        Assert.True(renamed);
        Assert.True(handle.IsClosed);
    }

    /// <summary>Une erreur native d'ouverture ou de renommage reste observable avec son code Windows.</summary>
    /// <param name="openFails">True simule une ouverture refusée ; false refuse le renommage.</param>
    /// <param name="expectedError">Code Windows attendu pour l'opération refusée.</param>
    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 32)]
    public void NativeFailuresPreserveTheirErrorAndReleaseTheHandle(bool openFails, int expectedError)
    {
        IntPtr nativeHandle = new(42);
        if (openFails)
        {
            nativeHandle = new IntPtr(-1);
        }

        using SafeFileHandle handle = new(nativeHandle, ownsHandle: false);
        bool renameAttempted = false;
        WindowsStateFilePublisher publisher = new((path, access, share, security, creation, flags, template) =>
        {
            if (openFails)
            {
                Marshal.SetLastPInvokeError(5);
            }

            return handle;
        }, (file, informationClass, information, size) =>
        {
            renameAttempted = true;
            Marshal.SetLastPInvokeError(32);
            return false;
        });

        IOException failure = Assert.Throws<IOException>(() => publisher.Publish(@"C:\solution\prepared.tmp", @"C:\solution\state.json"));

        Assert.Equal(expectedError, Assert.IsType<Win32Exception>(failure.InnerException).NativeErrorCode);
        Assert.Equal(!openFails, renameAttempted);
        Assert.True(handle.IsClosed);
    }

    /// <summary>Une exception du renommage est propagée après libération du handle du temporaire.</summary>
    [Fact]
    public void UnexpectedRenameFailureReleasesTheHandle()
    {
        using SafeFileHandle handle = new(new IntPtr(42), ownsHandle: false);
        InvalidOperationException expected = new("Unexpected rename failure.");
        WindowsStateFilePublisher publisher = new(
            (path, access, share, security, creation, flags, template) => handle,
            (file, informationClass, information, size) => throw expected);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            publisher.Publish(@"C:\solution\prepared.tmp", @"C:\solution\state.json"));

        Assert.Same(expected, failure);
        Assert.True(handle.IsClosed);
    }

    /// <summary>Le choix d'adaptateur n'invoque pas Windows et le renommage portable remplace un document fermé.</summary>
    [Fact]
    public void PlatformSelectionKeepsNativeCallsAtTheWindowsBoundary()
    {
        Assert.IsType<WindowsStateFilePublisher>(StateFilePublisher.Select(windows: true).Target);
        using SolutionFixture fixture = new();
        string temporary = Path.Combine(fixture.Root, "prepared.tmp");
        string path = Path.Combine(fixture.Root, "state.json");
        File.WriteAllText(temporary, "new document");
        File.WriteAllText(path, "previous document");

        StateFilePublisher.Select(windows: false)(temporary, path);

        Assert.Equal("new document", File.ReadAllText(path));
        Assert.False(File.Exists(temporary));
    }
}
