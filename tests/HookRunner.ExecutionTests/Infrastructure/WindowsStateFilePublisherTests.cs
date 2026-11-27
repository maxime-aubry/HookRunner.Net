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
    [Fact]
    public void PublicationSuppliesPosixRenameAndOwnsItsHandle()
    {
        string destination = @"C:\solution-é\Hooks\state.json";
        using SafeFileHandle handle = new(new IntPtr(42), ownsHandle: false);
        bool renamed = false;
        WindowsStateFilePublisher publisher = new((path, access, share, security, creation, flags, template) =>
        {
            Assert.Equal("prepared.tmp", path);
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
            Assert.Equal(destination.Length * 2, nameLength);
            Assert.Equal(destination, Encoding.Unicode.GetString(information, nameOffset, nameLength));
            Assert.Equal(nameOffset + nameLength + 2, size);
            Assert.Equal(new byte[] { 0, 0 }, information[^2..]);
            renamed = true;
            return true;
        });

        publisher.Publish("prepared.tmp", destination);

        Assert.True(renamed);
        Assert.True(handle.IsClosed);
    }

    /// <summary>Une erreur native d'ouverture ou de renommage reste observable avec son code Windows.</summary>
    /// <param name="openFails">True simule une ouverture refusée ; false refuse le renommage.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeFailuresPreserveTheirErrorAndReleaseTheHandle(bool openFails)
    {
        using SafeFileHandle handle = new(new IntPtr(42), ownsHandle: false);
        WindowsStateFilePublisher publisher = new((path, access, share, security, creation, flags, template) =>
        {
            if (openFails)
            {
                SafeFileHandle invalid = new(new IntPtr(-1), ownsHandle: false);
                Marshal.SetLastPInvokeError(5);
                return invalid;
            }

            return handle;
        }, (file, informationClass, information, size) =>
        {
            Marshal.SetLastPInvokeError(5);
            return false;
        });

        IOException failure = Assert.Throws<IOException>(() => publisher.Publish("prepared.tmp", "state.json"));

        Assert.Equal(5, Assert.IsType<Win32Exception>(failure.InnerException).NativeErrorCode);
        if (!openFails)
        {
            Assert.True(handle.IsClosed);
        }
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
